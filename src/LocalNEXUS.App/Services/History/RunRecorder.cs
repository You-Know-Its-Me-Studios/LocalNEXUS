using LocalNEXUS.App.Infrastructure;

namespace LocalNEXUS.App.Services.History;

/// <summary>
/// Turns what the feed reports into rows in the record.
/// </summary>
/// <remarks>
/// It sits between the feed and the store and knows which run is in progress, which is the one
/// thing neither of them does. The feed reports without knowing anybody is listening, and the
/// store writes without knowing where a row came from.
///
/// Everything here returns immediately. The store queues the write and a background thread does
/// it, so a node reporting what it did never waits on a disk, which is the condition on recording
/// at all: a record that slows a run down would be turned off within a week.
/// </remarks>
public sealed class RunRecorder
{
    private readonly RunHistoryStore _store;
    private readonly Persistence.AppConfig _config;

    private string? _runId;

    public RunRecorder(RunHistoryStore store, Persistence.AppConfig config)
    {
        _store = store;
        _config = config;
    }

    /// <summary>The run being recorded, or null between runs.</summary>
    public string? CurrentRunId => Volatile.Read(ref _runId);

    /// <summary>Attaches to a feed, so every entry it takes is written down.</summary>
    public void Attach(ActivityFeed feed)
    {
        ArgumentNullException.ThrowIfNull(feed);
        feed.Recorder = OnEntry;
    }

    /// <summary>
    /// What gives a finished run its vector, or null when semantic search is off.
    /// </summary>
    /// <remarks>
    /// Settable rather than injected, because the embedding model can be chosen and changed while
    /// the application is running and the recorder is built long before anybody opens Settings.
    /// Null is the ordinary state and means history is recorded exactly as it always was.
    /// </remarks>
    public Search.HistoryIndexer? Indexer { get; set; }

    /// <summary>Starts recording a run and returns its identity.</summary>
    public string BeginRun(string request, string graphName, int nodeCount, int connectionCount)
    {
        var runId = Guid.NewGuid().ToString();
        Volatile.Write(ref _runId, runId);

        _store.BeginRun(runId, request, graphName, nodeCount, connectionCount);
        return runId;
    }

    /// <summary>Closes the run off with how it ended and what it cost.</summary>
    public void EndRun(string state, decimal cost, int calls)
    {
        var runId = CurrentRunId;

        if (runId is null)
        {
            return;
        }

        _store.EndRun(runId, state, cost, calls);
        Volatile.Write(ref _runId, null);

        // After the run is recorded, never before, and never awaited. History is written whether
        // or not semantic search is switched on, whether or not a model is there, and whether or
        // not embedding works. A run is not lost because a vector could not be made for it.
        if (Indexer is { } indexer)
        {
            _ = Task.Run(() => indexer.IndexAsync(runId, CancellationToken.None));
        }
    }

    /// <summary>
    /// Trims the snapshots back to what the settings allow.
    /// </summary>
    /// <remarks>
    /// Called when a run ends, which is the only moment anything grew. There is deliberately no
    /// job that wakes up and does this on its own: a background pass exists in other tools because
    /// their memory layer goes stale and has to be reconciled against itself, and that problem is
    /// created by summarising in the first place. Nothing here summarises, so nothing drifts, so
    /// there is nothing for such a job to do.
    /// </remarks>
    public void ApplyLimits() => _store.PruneSnapshots(_config.SnapshotRunLimit, _config.SnapshotAgeDays);

    /// <summary>
    /// Records what became of a file, from whichever node dealt with it.
    /// </summary>
    public void RecordFile(string relativePath, FileOutcome outcome, string? detail)
    {
        if (CurrentRunId is { } runId)
        {
            _store.RecordFile(runId, relativePath, outcome, detail);
        }
    }

    /// <summary>Keeps what a file holds before the run changes it.</summary>
    public void Snapshot(string absolutePath)
    {
        if (CurrentRunId is { } runId)
        {
            _store.Snapshot(runId, absolutePath);
        }
    }

    /// <summary>
    /// One entry, on the way in or once it has finished changing.
    /// </summary>
    /// <remarks>
    /// Entries that arrive between runs are dropped rather than filed under the previous one.
    /// The feed carries startup notes and mesh chatter as well as run transcripts, and attaching
    /// those to whichever run happened to be last would make the record say something untrue.
    ///
    /// Both cases write the same statement, because the row is keyed on the entry's identity and
    /// the second write replaces the first. There is nothing to branch on: arriving and finishing
    /// are the same row at two moments.
    /// </remarks>
    private void OnEntry(ActivityEvent entry, bool _)
    {
        if (CurrentRunId is not { } runId)
        {
            return;
        }

        _store.RecordEvent(
            runId,
            entry.Id,
            entry.Timestamp,
            entry.Kind.ToString(),
            entry.NodeId,
            entry.Title,
            entry.Text.Length == 0 ? null : entry.Text);
    }
}
