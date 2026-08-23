using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// One thing to do, and whether it has been done.
/// </summary>
/// <remarks>
/// Whether a step is done is computed rather than remembered. A checklist that records having been
/// clicked goes wrong the moment somebody closes their project or deletes their last model: it
/// keeps saying done about a thing that is no longer true, which is worse than not tracking it at
/// all. Every step here answers from the state of the application right now.
/// </remarks>
public sealed partial class WalkthroughStep : ObservableObject
{
    private readonly Func<bool> _test;

    /// <summary>True when the application is in the state this step describes.</summary>
    [ObservableProperty]
    private bool _isDone;

    internal WalkthroughStep(string title, string detail, string actionLabel, Func<bool> isDone, IRelayCommand? action)
    {
        Title = title;
        Detail = detail;
        ActionLabel = actionLabel;
        Action = action;

        _test = isDone;
        IsDone = isDone();
    }

    /// <summary>What to do, in a few words.</summary>
    public string Title { get; }

    /// <summary>Why, and what happens after.</summary>
    public string Detail { get; }

    /// <summary>What the button says, or empty when the step has nothing to press.</summary>
    public string ActionLabel { get; }

    /// <summary>What the button does, or null when there is nothing to do from here.</summary>
    public IRelayCommand? Action { get; }

    /// <summary>True when there is a button to draw.</summary>
    public bool HasAction => Action is not null && ActionLabel.Length > 0;

    /// <summary>Asks again whether this is done.</summary>
    internal void Refresh() => IsDone = _test();
}

/// <summary>
/// A short path from a first launch to one run that worked.
/// </summary>
/// <remarks>
/// The moment most people give up is the first launch: an empty canvas, no models, no project, and
/// a Python environment downloading in the background with nothing saying so. This is a checklist
/// of the five things that have to be true before a run can do anything, each one able to do itself
/// so that reading it and following it are the same act.
///
/// It is a suggestion and never a gate. Nothing is disabled while it is showing, nothing waits for
/// a step, and dismissing it changes nothing except whether it is on screen. Somebody who has done
/// this before should not be walked through it, and somebody who dismissed it and then wanted it
/// should not have to reinstall, so it is on the Help menu as well and the dismissal is one line in
/// the config.
///
/// The steps are computed rather than ticked off. A checklist that remembers having been clicked
/// starts lying the moment somebody closes their project.
/// </remarks>
public sealed partial class WalkthroughViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly ProjectService _project;
    private readonly System.Collections.ObjectModel.ObservableCollection<LocalModelInfo> _models;
    private readonly GraphModel _graph;

    /// <summary>True while the panel is showing.</summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>Whether every step was already done last time this was asked.</summary>
    private bool _wasFinished;

    public WalkthroughViewModel(
        AppConfig config,
        ProjectService project,
        ObservableCollection<LocalModelInfo> models,
        GraphModel graph,
        ActivityFeedViewModel feed,
        IRelayCommand openProject,
        IRelayCommand openSettings,
        IRelayCommand applyTemplate,
        IReadOnlyList<GraphTemplate> templates)
    {
        _config = config;
        _project = project;
        _models = models;
        _graph = graph;

        var starter = templates.FirstOrDefault();

        Steps = new ObservableCollection<WalkthroughStep>
        {
            new(
                "Open a codebase",
                "Point LocalNEXUS at the folder you want it to work in. It reads what is already there, "
                + "so it can edit rather than write a second copy of something you have. Unity projects are "
                + "recognised on sight and get the extra rules that keep a scene from losing its scripts; "
                + "anything else is an ordinary C# project and those rules stay out of the way.",
                "Open project",
                () => _project.HasProject,
                openProject),

            new(
                "Point it at a model",
                "A model file on this machine, or a key for a hosted one. Local models are found by reading "
                + "the file rather than trusting its name, so a folder of them is enough.",
                "Open settings",
                () => _models.Count > 0 || !string.IsNullOrWhiteSpace(_config.CloudBaseUrl),
                openSettings),

            new(
                "Start from a graph that already works",
                starter is null
                    ? "Pick one from the File menu under Start from."
                    : $"{starter.Name}. {starter.Description}",
                starter is null ? string.Empty : "Open it",
                () => _graph.Nodes.Count > 0,
                starter is null ? null : new RelayCommand(() => applyTemplate.Execute(starter))),

            new(
                "Choose the model on each Model node",
                "Click a Model node on the canvas and pick one on the right. A template leaves this empty "
                + "on purpose, because which model you have is about this machine rather than about the graph.",
                string.Empty,
                () => _graph.Nodes.Any(n => n is Nodes.ModelNode model && model.IsConfigured),
                null),

            new(
                "Type what you want, and run it",
                "The box under the canvas. Describe the change in a sentence, then press Run or Ctrl+Enter. "
                + "The nodes light up in turn and the last line names the file it wrote.",
                string.Empty,
                () => _config.HasCompletedAWalkthroughRun,
                null)
        };

        _project.PropertyChanged += OnSomethingChanged;
        _models.CollectionChanged += (_, _) => Refresh();
        _graph.Nodes.CollectionChanged += (_, _) => Refresh();

        // Straight from the run, rather than through the window watching a property and passing it
        // on. That is how it was wired, and it never once fired: ten runs finished, every one of
        // them written to the history as completed, and the step stayed unticked. Which link
        // failed was never established, so the links were removed instead of guessed at.
        feed.RunFinished += OnRunFinished;

        _wasFinished = IsFinished;
        IsOpen = !_config.WalkthroughDismissed;
    }

    /// <summary>The steps, in the order they have to happen.</summary>
    public ObservableCollection<WalkthroughStep> Steps { get; }

    /// <summary>How many are done, for the line at the top.</summary>
    public string Progress => $"{Steps.Count(s => s.IsDone)} of {Steps.Count} done";

    /// <summary>True once every step is done, which is what turns the panel into a goodbye.</summary>
    public bool IsFinished => Steps.All(s => s.IsDone);

    /// <summary>Hides the walkthrough and remembers not to open it again.</summary>
    [RelayCommand]
    private void Dismiss()
    {
        IsOpen = false;

        _config.WalkthroughDismissed = true;
        _config.Save();
    }

    /// <summary>Shows it again, from the Help menu.</summary>
    [RelayCommand]
    private void Show()
    {
        Refresh();
        IsOpen = true;
    }

    /// <summary>
    /// Records that a run finished, which is the last step and the only one nothing else can see.
    /// </summary>
    /// <remarks>
    /// The one piece of state that is remembered rather than computed, because a run that completed
    /// leaves nothing behind that is still true a minute later. It is written to the config so that
    /// somebody who has already done this once is not asked to do it again on the next launch.
    /// </remarks>
    public void RecordSuccessfulRun()
    {
        if (_config.HasCompletedAWalkthroughRun)
        {
            return;
        }

        _config.HasCompletedAWalkthroughRun = true;
        _config.Save();

        Refresh();
    }

    /// <summary>Asks every step again whether it is done.</summary>
    public void Refresh()
    {
        foreach (var step in Steps)
        {
            step.Refresh();
        }

        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(IsFinished));

        // Nothing left to walk anybody through, so it closes itself and stays closed. Leaving it
        // up with every box ticked is a panel whose only remaining content is an instruction to
        // dismiss it, which is a thing to do rather than a thing to read. The Help menu still has
        // it for anybody who wants it back.
        //
        // On the moment it becomes finished, and not on being finished. Somebody who opened a
        // completed one from the Help menu is looking at it deliberately, and having it disappear
        // the next time anything refreshed would be the application arguing with them.
        var finished = IsFinished;

        if (finished && !_wasFinished && IsOpen)
        {
            Dismiss();
        }

        _wasFinished = finished;
    }

    /// <summary>
    /// A run stopped. Only two of the ways it can stop count as having run it.
    /// </summary>
    /// <remarks>
    /// A run that left files waiting still ran, so it counts: the step is having got the graph to
    /// do something, not having got a perfect result first time.
    /// </remarks>
    private void OnRunFinished(Services.Execution.RunState state)
    {
        if (state is Services.Execution.RunState.Completed or Services.Execution.RunState.Unresolved)
        {
            RecordSuccessfulRun();
        }
    }

    private void OnSomethingChanged(object? sender, PropertyChangedEventArgs e) => Refresh();
}
