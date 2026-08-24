using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Services.History;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The history window: past runs, what they did, and putting one back.
/// </summary>
/// <remarks>
/// Everything here is read on demand and dropped when the window closes. The list holds the rows
/// currently on screen and nothing else, so opening a project with four years of runs costs a
/// query rather than a load.
/// </remarks>
public sealed partial class HistoryViewModel : ObservableObject
{
    /// <summary>How many runs the list asks for at a time.</summary>
    private const int PageSize = 200;

    /// <summary>How many search hits are worth showing before somebody should search better.</summary>
    private const int SearchLimit = 100;

    private readonly RunHistoryStore _store;
    private readonly IActivityFeed _feed;

    private CancellationTokenSource? _loading;

    /// <summary>The run whose record is open, or null when nothing is selected.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private RunSummary? _selected;

    /// <summary>What to look for. Empty means show the recent runs instead.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>True while a query is in flight.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>What the list is currently showing, said plainly.</summary>
    [ObservableProperty]
    private string _listSummary = string.Empty;

    /// <summary>What the last undo did, or an empty string before one.</summary>
    [ObservableProperty]
    private string _undoSummary = string.Empty;

    public HistoryViewModel(RunHistoryStore store, IActivityFeed feed)
    {
        _store = store;
        _feed = feed;
    }

    /// <summary>The runs on screen, either the recent ones or the search hits.</summary>
    /// <summary>
    /// Searching by meaning, or null when it is off.
    /// </summary>
    /// <remarks>
    /// Settable, because the embedding model is chosen in Settings while this panel already
    /// exists. Null is the ordinary state and means the search is exactly what it always was.
    /// </remarks>
    public Services.Search.SemanticHistorySearch? Semantic { get; set; }

    /// <summary>True when the last search compared meaning rather than words.</summary>
    [ObservableProperty]
    private bool _searchedByMeaning;

    /// <summary>Why the last search fell back to words, when it did.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchNote))]
    private string _searchNote = string.Empty;

    /// <summary>True when there is something to say about how the search ran.</summary>
    public bool HasSearchNote => SearchNote.Length > 0;

    public ObservableCollection<RunSummary> Runs { get; } = new();

    /// <summary>The transcript of the selected run, in the order it happened.</summary>
    public ObservableCollection<RunEventRecord> Events { get; } = new();

    /// <summary>What the selected run did to files.</summary>
    public ObservableCollection<RunFileRecord> Files { get; } = new();

    /// <summary>Where a search hit matched, when the list is showing search results.</summary>
    public ObservableCollection<HistoryHit> Hits { get; } = new();

    /// <summary>True when a run is open.</summary>
    public bool HasSelection => Selected is not null;

    /// <summary>True when this project has a record at all.</summary>
    public bool IsAvailable => _store.IsOpen;

    /// <summary>What the window says when there is no record to show.</summary>
    public string UnavailableText => _store.StatusText;

    /// <summary>
    /// The sentence the window shows about what undo can and cannot reach.
    /// </summary>
    /// <remarks>
    /// Said in the interface rather than left to be discovered. Undo restores what this
    /// application wrote and nothing else, and somebody who believes it is version control will
    /// find out at the worst possible moment.
    /// </remarks>
    public string UndoScopeText =>
        "Undo puts back only the files this application wrote or edited during that run. Anything Unity "
        + "regenerated, an extension changed, or you edited by hand is invisible to it, and putting a file "
        + "back also discards whatever was done to it since. This is run undo, not version control.";

    /// <summary>Reads the most recent runs, or the hits for whatever is in the search box.</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        _loading?.Cancel();
        _loading?.Dispose();
        _loading = new CancellationTokenSource();

        var ct = _loading.Token;

        IsBusy = true;

        try
        {
            OnPropertyChanged(nameof(IsAvailable));
            OnPropertyChanged(nameof(UnavailableText));

            Hits.Clear();

            IReadOnlyList<RunSummary> rows;

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                rows = await _store.ListRunsAsync(PageSize, ct).ConfigureAwait(true);
                ListSummary = rows.Count == 0
                    ? "No runs recorded for this project yet."
                    : $"{rows.Count} most recent run(s).";
            }
            else
            {
                // By meaning when that is switched on and working, and by word otherwise. The
                // search itself decides and says which it did, because falling back quietly would
                // leave somebody wondering why a phrase they know works found nothing.
                var found = Semantic is { } semantic
                    ? await semantic.SearchAsync(SearchText, SearchLimit, ct).ConfigureAwait(true)
                    : new Services.Search.SearchOutcome(
                        await _store.SearchAsync(SearchText, SearchLimit, ct).ConfigureAwait(true),
                        Services.Search.SearchMethod.Keyword,
                        string.Empty);

                var hits = found.Hits;
                SearchNote = found.Note;
                SearchedByMeaning = found.Method == Services.Search.SearchMethod.Semantic;

                foreach (var hit in hits)
                {
                    Hits.Add(hit);
                }

                var all = await _store.ListRunsAsync(PageSize, ct).ConfigureAwait(true);
                var matched = hits.Select(h => h.RunId).ToHashSet(StringComparer.Ordinal);

                rows = all.Where(r => matched.Contains(r.RunId)).ToList();

                var how = SearchedByMeaning ? "match" : "mention";

                ListSummary = rows.Count == 0
                    ? $"Nothing in this project's history matches \"{SearchText}\"."
                    : $"{rows.Count} run(s) {how} \"{SearchText}\".";
            }

            Runs.Clear();

            foreach (var row in rows)
            {
                Runs.Add(row);
            }

            if (Selected is not null && Runs.All(r => r.RunId != Selected.RunId))
            {
                Selected = null;
            }
        }
        catch (OperationCanceledException)
        {
            // A newer query is already on its way.
        }
        catch (Services.History.HistoryQueryException ex)
        {
            // Said out loud rather than shown as no results. A search that could not run and a
            // search that found nothing look identical from the outside, and that is precisely how
            // a broken query went unnoticed for as long as it did.
            Runs.Clear();
            Hits.Clear();
            ListSummary = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Opens the full record of one run.</summary>
    [RelayCommand]
    private async Task OpenAsync(RunSummary? run)
    {
        Selected = run;

        Events.Clear();
        Files.Clear();
        UndoSummary = string.Empty;

        if (run is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var events = await _store.ReadEventsAsync(run.RunId, CancellationToken.None).ConfigureAwait(true);
            var files = await _store.ReadFilesAsync(run.RunId, CancellationToken.None).ConfigureAwait(true);

            foreach (var entry in events)
            {
                Events.Add(entry);
            }

            foreach (var file in files)
            {
                Files.Add(file);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Puts back every file the selected run wrote, leaving the request alone.
    /// </summary>
    /// <remarks>
    /// Restoring the files and discarding what was asked for are separate on purpose. Somebody who
    /// reverts an attempt usually wants to try it again, and throwing away the request as well
    /// would make them retype it.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        if (Selected is not { } run)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var outcome = await _store.UndoAsync(run.RunId, CancellationToken.None).ConfigureAwait(true);

            UndoSummary = outcome.Complete
                ? outcome.Summary
                : $"{outcome.Summary}.{Environment.NewLine}{string.Join(Environment.NewLine, outcome.Failed)}";

            if (outcome.Complete)
            {
                _feed.Info($"Undid a run from {run.StartedAt:HH:mm:ss}", outcome.Summary);
            }
            else
            {
                _feed.Error($"Undid part of a run from {run.StartedAt:HH:mm:ss}", UndoSummary);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanUndo() => Selected is { CanUndo: true };

    /// <summary>Puts the selected run's request back in the chat box, without touching any file.</summary>
    /// <remarks>
    /// The other half of the separation: taking the question back without taking the answer back.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ReuseRequest()
    {
        if (Selected is { } run)
        {
            RequestReused?.Invoke(run.Request);
        }
    }

    /// <summary>Raised when somebody wants a past request back in the box.</summary>
    public event Action<string>? RequestReused;
}
