using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Inference;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The bottom panel: the transcript, the chat box, and the controls that drive a run.
/// </summary>
/// <remarks>
/// The run itself lives in <see cref="GraphExecutor"/>. This view model owns only the parts a
/// person interacts with: what was typed, whether Run is available, and cancelling or pausing.
/// Command availability is derived from <see cref="RunState"/> rather than from separate flags.
/// </remarks>
public sealed partial class ActivityFeedViewModel : ObservableObject
{
    private readonly GraphExecutor _executor;
    private readonly GraphModel _graph;
    private readonly ActivityFeed _feed;
    private readonly Dispatcher _dispatcher;
    private readonly RunCostTracker _cost;
    private readonly Services.Files.StagingStore _staging;
    private readonly Services.History.RunRecorder? _recorder;
    private readonly Services.History.ConversationService? _conversation;
    private readonly Services.History.RunHistoryStore? _history;

    private CancellationTokenSource? _runCancellation;
    private RunContext? _run;

    /// <summary>The request typed by the user, sent to input nodes when the run starts.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string _requestText = string.Empty;

    /// <summary>The lifecycle state of the current or most recent run.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsPaused))]
    [NotifyPropertyChangedFor(nameof(PauseButtonText))]
    private RunState _runState = RunState.Idle;

    /// <summary>
    /// The run in progress, or the most recent one, as the history files it.
    /// </summary>
    /// <remarks>
    /// Held so that something outside this class can ask what to read afterwards. It used to be a
    /// local of the run method, which is all it needed to be until a caller who is not standing at
    /// the window wanted the answer.
    /// </remarks>
    [ObservableProperty]
    private string? _currentRunId;

    public ActivityFeedViewModel(GraphExecutor executor, GraphModel graph, ActivityFeed feed)
        : this(executor, graph, feed, Dispatcher.CurrentDispatcher)
    {
    }

    /// <summary>
    /// True while the run controls are in front of the user.
    /// </summary>
    /// <remarks>
    /// A run belongs to the canvas, and the canvas is only on screen in the Workspace. Without
    /// this, the Run menu keeps working while the Network is showing and starts a run nobody can
    /// see, on a graph they are not looking at. The shell keeps it in step with the active view.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearFeedCommand))]
    private bool _isActive = true;

    /// <summary>
    /// Web search, when this installation has a key for it.
    /// </summary>
    /// <remarks>
    /// Bound to directly by the request box, so the checkbox appears when a key exists and there
    /// is nothing at all when one does not. A toggle for something unavailable is a toggle that
    /// teaches people it does not work.
    /// </remarks>
    public Services.Search.WebSearchService? Search { get; }

    /// <summary>True when the search checkbox should be on the request box at all.</summary>
    public bool CanSearch => Search?.HasKey == true;

    /// <summary>The vision model that reads a pasted image, when one is configured.</summary>
    public Services.Vision.VisionReader? Vision { get; }

    /// <summary>
    /// Reads an image and puts what it says into the request box.
    /// </summary>
    /// <remarks>
    /// The image never goes any further than this. What joins the request is the text the vision
    /// model produced, so the graph, the pins and the coder are all untouched and none of them has
    /// to be multimodal.
    ///
    /// What was extracted is in the feed before the run uses it, because a wrong reading that
    /// silently becomes the request is the failure this feature could most easily have.
    /// </remarks>
    public async Task AttachImageAsync(byte[] image, string mediaType, CancellationToken ct = default)
    {
        if (Vision is not { } vision)
        {
            _feed.Info("No vision model", Services.Vision.VisionReader.NotConfiguredMessage);
            return;
        }

        if (!vision.IsConfigured)
        {
            _feed.Info("No vision model", Services.Vision.VisionReader.NotConfiguredMessage);
            return;
        }

        // A local vision model is loaded onto the card the first time an image arrives, which takes
        // long enough that saying nothing would look like nothing happening. The same entry is
        // rewritten as it goes rather than one being added per step.
        var progress = _feed.Add(Infrastructure.ActivityKind.Info, "Reading an image", "Starting");
        var status = new Infrastructure.DelegateProgress<string>(message => progress.Detail = message);

        try
        {
            var reading = await vision.ReadAsync(image, mediaType, status, ct).ConfigureAwait(true);

            progress.Detail = $"read in {reading.Elapsed.TotalSeconds:0.0} s";

            _feed.Add(
                Infrastructure.ActivityKind.Info,
                $"Read an image in {reading.Elapsed.TotalSeconds:0.0} s",
                reading.Text);

            RequestText = RequestText.Trim().Length == 0
                ? reading.Text
                : RequestText.TrimEnd() + Environment.NewLine + Environment.NewLine + reading.Text;
        }
        catch (Services.Vision.VisionException ex)
        {
            progress.Detail = "failed";

            _feed.Error("The image was not read", ex.Message);
        }
    }

    /// <summary>
    /// Whether this send may search.
    /// </summary>
    /// <remarks>
    /// Per send rather than per node. Most requests do not need search and whoever is typing knows
    /// which do, and a setting on each node would mean setting it in five places for one question.
    /// It applies to every Model node in the run, which the box says next to it.
    /// </remarks>
    public bool SearchThisSend
    {
        get => Search?.EnabledForThisRun == true;
        set
        {
            if (Search is { } search && search.EnabledForThisRun != value)
            {
                search.EnabledForThisRun = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Raised once when a run stops, carrying how it ended.
    /// </summary>
    /// <remarks>
    /// A property change says the value moved and leaves the reader to work out whether that was a
    /// run ending; this says a run ended. It exists because something that needed to know missed
    /// ten of them in a row while watching <see cref="RunState"/>, and the reason was never found.
    /// A run is an event, so it is raised as one, from the same block that records it.
    /// </remarks>
    public event Action<RunState>? RunFinished;

    public ActivityFeedViewModel(
        GraphExecutor executor,
        GraphModel graph,
        ActivityFeed feed,
        Dispatcher dispatcher,
        RunCostTracker? cost = null,
        Services.Files.StagingStore? staging = null,
        Services.History.RunRecorder? recorder = null,
        Services.History.ConversationService? conversation = null,
        Services.History.RunHistoryStore? history = null,
        Services.Search.WebSearchService? search = null,
        Services.Vision.VisionReader? vision = null)
    {
        Search = search;

        // The checkbox appears when a key exists, and a key can be added at any point from
        // Settings. Without this, CanSearch is read once when the window is built and never again,
        // so somebody who pasted a key watched nothing happen and had to restart the application
        // to be offered the thing they had just configured.
        if (search is not null)
        {
            search.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(Services.Search.WebSearchService.HasKey)
                    or nameof(Services.Search.WebSearchService.EnabledForThisRun))
                {
                    OnPropertyChanged(nameof(CanSearch));
                    OnPropertyChanged(nameof(SearchThisSend));
                }
            };
        }
        Vision = vision;
        _conversation = conversation;
        _history = history;

        // The run lifecycle is owned here, so this is where a run gets its identity in the record.
        // Doing it in the executor would put knowledge of the record into the one component that
        // is meant to know only how to order nodes.
        _recorder = recorder;

        // The same store the output node writes to, so the box below is describing the files that
        // are actually waiting rather than a second copy of the idea.
        _staging = staging ?? new Services.Files.StagingStore(dispatcher);

        _executor = executor;
        _graph = graph;
        _feed = feed;
        _dispatcher = dispatcher;

        // The same instance the nodes add to, so the total the feed reports is the one they
        // built rather than a second count of the same thing.
        _cost = cost ?? new RunCostTracker();

        _executor.RunCreated += (_, run) => AttachRun(run);
    }

    /// <summary>The transcript, oldest entry first.</summary>
    public ObservableCollection<ActivityEvent> Events => _feed.Events;

    /// <summary>True while nodes are executing or the run is holding.</summary>
    public bool IsRunning => RunState is RunState.Running or RunState.Paused;

    /// <summary>True while the run is holding between nodes.</summary>
    public bool IsPaused => RunState == RunState.Paused;

    /// <summary>Label for the pause and resume button.</summary>
    public string PauseButtonText => IsPaused ? "Resume" : "Pause";

    /// <summary>The work the last run left behind, for the box to show and the next run to read.</summary>
    public Services.Files.StagingStore Staging => _staging;

    /// <summary>The running conversation for this project, which the transcript binds to.</summary>
    public Services.History.ConversationService? Conversation => _conversation;

    /// <summary>Starts a fresh conversation without losing a word of the old one.</summary>
    [RelayCommand]
    private void NewConversation() => _conversation?.StartNew();

    /// <summary>Lets a run that asked something carry on unanswered.</summary>
    [RelayCommand]
    private void ProceedWithoutAnswering() => _conversation?.ProceedWithoutAnswering();

    /// <summary>
    /// The request the run is given: what was typed, and what is still waiting.
    /// </summary>
    /// <remarks>
    /// This is how staged work is resolved from the chat box rather than by starting the whole
    /// request over. Somebody types what to do about the file that did not compile, and the run
    /// begins knowing which file that is, what it was for and what the compiler said, without
    /// anyone having to repeat it.
    ///
    /// Appended rather than substituted, and clearly labelled, so the typed request stays the
    /// request. Nothing is added when nothing is waiting.
    /// </remarks>
    /// <summary>
    /// What the graph says back once a run ends.
    /// </summary>
    /// <remarks>
    /// Short on purpose. The transcript is the conversation, not a second copy of the feed, and
    /// somebody scrolling it wants to know how each attempt ended rather than to read it again.
    /// </remarks>
    private string DescribeOutcome()
    {
        var staged = _staging.HasPending ? $" {_staging.Summary}." : string.Empty;

        return RunState switch
        {
            RunState.Completed => $"Done.{staged}",
            RunState.Unresolved => $"Finished with work left over.{staged}",
            RunState.Faulted => "Stopped. The activity panel says where.",
            _ => $"Ended as {RunState}.{staged}"
        };
    }

    private async Task<string> ComposeRequestAsync(string typed)
    {
        var builder = new System.Text.StringBuilder(typed);

        if (_conversation is { } conversation)
        {
            var turns = conversation.Turns.ToList();
            var recent = Services.History.ConversationContext.Recent(turns);
            var carried = recent.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);

            var recalled = _history is null
                ? Array.Empty<Services.History.ConversationTurn>()
                : await _history
                    .RecallAsync(
                        conversation.ThreadId,
                        typed,
                        carried,
                        Services.History.ConversationContext.RecalledTurns,
                        CancellationToken.None)
                    .ConfigureAwait(true);

            var context = Services.History.ConversationContext.Build(turns, recalled);

            if (context.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.Append(context);
            }
        }

        if (_staging.HasPending)
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("Work left unfinished by an earlier run, still waiting:");
            builder.Append(_staging.Describe());
        }

        return builder.ToString();
    }

    /// <summary>Forgets a staged file, because it is no longer wanted.</summary>
    [RelayCommand]
    private void DiscardStaged(Services.Files.StagedFile? file)
    {
        if (file is not null)
        {
            _staging.Resolve(file.RelativePath);
        }
    }

    /// <summary>Forgets everything that is waiting.</summary>
    [RelayCommand]
    private void DiscardAllStaged() => _staging.Clear();

    /// <summary>Runs the graph with the text currently in the chat box.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        var typed = RequestText;

        // A message sent while a run is waiting on a question is the answer to it, not a new
        // request. The box means the obvious thing, which is the whole reason the questions are
        // asked here rather than in a dialog of their own.
        if (_conversation is { IsAwaitingAnswer: true })
        {
            _conversation.Say(typed);
            RequestText = string.Empty;
            return;
        }

        _runCancellation?.Dispose();
        _runCancellation = new CancellationTokenSource();

        // Begun before the first entry, so everything the run says lands under it. What is
        // recorded as the request is what the person actually typed, not the assembled prompt:
        // the history list is a list of things somebody asked for.
        var runId = _recorder?.BeginRun(typed, _graph.Name, _graph.Nodes.Count, _graph.Connections.Count);
        CurrentRunId = runId;

        // Said before the context is assembled, because the assembly reads the thread and this
        // message is the newest thing in it.
        _conversation?.Say(typed, runId);
        RequestText = string.Empty;

        var request = await ComposeRequestAsync(typed).ConfigureAwait(true);

        _feed.Add(ActivityKind.Request, "Request", request);

        // Each run is priced on its own, so the total starts at nothing.
        _cost.Reset();

        RunState = RunState.Running;

        try
        {
            var run = await Task.Run(
                () => _executor.RunAsync(_graph, request, _runCancellation.Token, runId),
                _runCancellation.Token).ConfigureAwait(true);

            RunState = run.State;
        }
        catch (OperationCanceledException)
        {
            RunState = RunState.Faulted;
        }
        catch (Exception ex)
        {
            _feed.Error("Run could not start", ex.Message);
            RunState = RunState.Faulted;
        }
        finally
        {
            // The final figure, once, and only when something actually cost money. A run made
            // entirely of local models says nothing rather than saying zero.
            if (_cost.HasCost)
            {
                _feed.Info(
                    "Run cost",
                    $"{RunCost.Format(_cost.Total)} across {_cost.Calls} call(s).");
            }

            // What the graph has to say back, which is what makes the next message a follow up
            // rather than a fresh start.
            _conversation?.Report(DescribeOutcome(), runId);

            // Last, so the cost entry above is inside the run it belongs to.
            _recorder?.EndRun(RunState.ToString(), _cost.Total, _cost.Calls);

            // Beside the line that writes the run down, and deliberately so. Anything that needs
            // to know a run ended can be told here, on the one statement that provably runs for
            // every run there has ever been, rather than by watching a property and hoping.
            RunFinished?.Invoke(RunState);

            // The caps are applied here rather than by a job that wakes up on its own. There is
            // no background work in this design at all: the record never goes stale, so there is
            // nothing for an idle task to reconcile, and the one thing that does grow is trimmed
            // at the only moment it grew.
            _recorder?.ApplyLimits();

            DetachRun();
            _runCancellation?.Dispose();
            _runCancellation = null;
        }
    }

    /// <summary>Stops the run, cancelling the node that is currently executing.</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _run?.Resume();
        _runCancellation?.Cancel();
    }

    /// <summary>Holds the run before the next node, or releases a held run.</summary>
    [RelayCommand(CanExecute = nameof(CanTogglePause))]
    private void TogglePause()
    {
        if (_run is null)
        {
            return;
        }

        if (_run.State == RunState.Paused)
        {
            _run.Resume();
        }
        else
        {
            _run.Pause();
        }

        RunState = _run.State;
    }

    /// <summary>Empties the transcript.</summary>
    [RelayCommand(CanExecute = nameof(IsActive))]
    private void ClearFeed() => _feed.Clear();

    private bool CanRun() => IsActive && !IsRunning && !string.IsNullOrWhiteSpace(RequestText);

    private bool CanCancel() => IsActive && IsRunning;

    private bool CanTogglePause() => IsActive && IsRunning;

    /// <summary>
    /// Follows the run's own state so that a fault raised deep inside the executor reaches the
    /// buttons. The executor runs off the UI thread, so the update is marshalled back.
    /// </summary>
    private void AttachRun(RunContext run)
    {
        DetachRun();
        _run = run;
        run.PropertyChanged += OnRunPropertyChanged;
    }

    private void DetachRun()
    {
        if (_run is not null)
        {
            _run.PropertyChanged -= OnRunPropertyChanged;
        }
    }

    private void OnRunPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RunContext.State) || sender is not RunContext run)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            RunState = run.State;
            return;
        }

        _dispatcher.BeginInvoke(() => RunState = run.State);
    }
}
