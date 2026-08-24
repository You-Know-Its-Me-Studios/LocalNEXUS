using System.IO;
using System.Threading.Channels;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Data.Sqlite;

namespace LocalNEXUS.App.Services.History;

/// <summary>
/// Every run this project has had, written to disk as it happens and read back by query.
/// </summary>
/// <remarks>
/// The whole system is: append rows as things occur, and query when something needs recalling.
/// There is no vector database, no embedding model and no background job, because none of those
/// are needed to find a line of text in a file that is already indexed.
///
/// Nothing is held in memory. The list of runs and the transcript of one are read on demand and
/// handed to the view, so a project with four years of runs costs the same at rest as a project
/// with none. That is the property that makes keeping the whole record affordable, and it is why
/// this replaces summarising rather than sitting beside it. A summary is a lossy answer to a
/// storage problem that does not exist here; the record stays whole and gets retrieved.
///
/// Writes go through a single background writer with its own connection, fed by a channel, so a
/// run never waits on the disk and two threads never share a connection. Reads open their own
/// short lived connection, which is what write ahead logging makes cheap.
///
/// Stored under the project, beside the staging file and for the same reason: opening a different
/// project must not show somebody this one's history.
/// </remarks>
public sealed partial class RunHistoryStore : ObservableObject, IAsyncDisposable
{
    /// <summary>The database file, inside the folder this application keeps under a project.</summary>
    public const string FileName = "history.db";

    /// <summary>
    /// The largest file kept as a snapshot.
    /// </summary>
    /// <remarks>
    /// A guard rather than a setting. Snapshots exist so a run can be undone, and a run writes
    /// source files; anything past this is not something this application wrote and copying it
    /// would turn a cheap safety net into a disk problem.
    /// </remarks>
    private const int MaximumSnapshotBytes = 4 * 1024 * 1024;

    /// <summary>
    /// How long closing waits for queued writes to land before giving up on them.
    /// </summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Queued writes for the open database, replaced each time one is opened.
    /// </summary>
    /// <remarks>
    /// One per open rather than one for the life of the store, because closing completes it so
    /// that the writer drains and stops. A completed channel refuses everything afterwards, so a
    /// single shared one would mean the first project switch silenced recording for the rest of
    /// the session.
    /// </remarks>
    private Channel<Action<SqliteConnection>> _writes = NewQueue();

    private static Channel<Action<SqliteConnection>> NewQueue()
        => Channel.CreateUnbounded<Action<SqliteConnection>>(new UnboundedChannelOptions
        {
            SingleReader = true
        });

    private readonly object _sync = new();

    private Task? _writer;
    private CancellationTokenSource? _writerStop;
    private string? _databasePath;

    /// <summary>True once a project's history is open and can be written to.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isOpen;

    /// <summary>Why the history is not available, when it is not.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _unavailableReason = string.Empty;

    /// <summary>What the settings panel says about the state of the record.</summary>
    public string StatusText => IsOpen
        ? "Recording every run for this project."
        : string.IsNullOrEmpty(UnavailableReason)
            ? "No project is open, so there is nothing to record against."
            : UnavailableReason;

    /// <summary>Where this project's history lives, or null when none is open.</summary>
    public string? DatabasePath
    {
        get
        {
            lock (_sync)
            {
                return _databasePath;
            }
        }
    }

    /// <summary>
    /// Points the store at a project, creating the database if that project has never had one.
    /// </summary>
    public async Task OpenProjectAsync(string? projectPath, CancellationToken ct)
    {
        await CloseAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            IsOpen = false;
            UnavailableReason = string.Empty;
            return;
        }

        var folder = Persistence.ProjectPaths.For(projectPath);
        var path = Path.Combine(folder, FileName);

        try
        {
            Directory.CreateDirectory(folder);

            await using var connection = Connect(path);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            CreateSchema(connection);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            IsOpen = false;
            UnavailableReason = $"This project's history could not be opened, so nothing is being recorded: {ex.Message}";
            return;
        }

        lock (_sync)
        {
            _databasePath = path;
        }

        StartWriter(path);

        IsOpen = true;
        UnavailableReason = string.Empty;
    }

    /// <summary>Stops the writer and lets go of the project.</summary>
    public async Task CloseAsync()
    {
        var writer = _writer;
        var stop = _writerStop;
        var queue = _writes;

        _writer = null;
        _writerStop = null;

        lock (_sync)
        {
            _databasePath = null;
        }

        IsOpen = false;

        if (writer is null)
        {
            return;
        }

        // What is already queued gets written before anything is cancelled. Cancelling first
        // discarded it, and the reasoning for that was wrong twice over: recording is deliberately
        // asynchronous so a run never waits on it, which means at the moment of closing there is
        // routinely a backlog of the most recent events, exactly the ones somebody would look for.
        // And closing is not only shutdown. It happens on every project switch, so a run that had
        // just finished could disappear from its own history.
        //
        // Found by CI rather than by reading: a test that recorded six runs and read three back
        // got one, because the machine was slower than the one where it was written and the queue
        // had not drained. That is the same race, in the open.
        queue.Writer.TryComplete();

        try
        {
            // Bounded, because a write wedged against a locked database must not hold the
            // application open. The queue is small and local, so this is generous.
            await writer.WaitAsync(DrainTimeout).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The loop observed cancellation on the way out. Anything it had already taken off
            // the queue is written.
        }
        catch (TimeoutException)
        {
            // Draining took longer than any healthy queue should. Stop waiting and cancel below,
            // which is the old behaviour and is now the exception rather than the rule.
        }

        stop?.Cancel();
        stop?.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);

    /// <summary>Records that a run has begun. Returns at once; the write happens behind it.</summary>
    public void BeginRun(string runId, string request, string graphName, int nodeCount, int connectionCount)
    {
        var startedAt = DateTimeOffset.Now;

        Enqueue(connection =>
        {
            Execute(
                connection,
                "INSERT OR REPLACE INTO runs (run_id, started_at, state, request, graph_name, node_count, connection_count) "
                + "VALUES ($id, $at, $state, $request, $graph, $nodes, $connections);",
                ("$id", runId),
                ("$at", startedAt.ToString("O")),
                ("$state", "Running"),
                ("$request", request),
                ("$graph", graphName),
                ("$nodes", nodeCount),
                ("$connections", connectionCount));

            Index(connection, runId, runId, "request", request);
        });
    }

    /// <summary>Records how a run ended and what it cost.</summary>
    public void EndRun(string runId, string state, decimal cost, int calls)
    {
        var endedAt = DateTimeOffset.Now;

        Enqueue(connection => Execute(
            connection,
            "UPDATE runs SET ended_at = $at, state = $state, cost = $cost, calls = $calls, "
            + "written = (SELECT COUNT(*) FROM files WHERE run_id = $id AND outcome = 'Written'), "
            + "staged = (SELECT COUNT(*) FROM files WHERE run_id = $id AND outcome = 'Staged') "
            + "WHERE run_id = $id;",
            ("$id", runId),
            ("$at", endedAt.ToString("O")),
            ("$state", state),
            ("$cost", (double)cost),
            ("$calls", calls)));
    }

    /// <summary>
    /// Records one line of a run's transcript, or replaces it once it has stopped changing.
    /// </summary>
    /// <remarks>
    /// Keyed on the entry's own identity so that a streamed reply is one row rather than one row
    /// per chunk. It goes in the moment it appears, so a crash cannot lose that it happened, and
    /// is rewritten when the stream ends so the record holds the whole reply rather than its first
    /// few tokens.
    /// </remarks>
    public void RecordEvent(
        string runId,
        Guid eventId,
        DateTimeOffset at,
        string kind,
        Guid? nodeId,
        string title,
        string? detail)
    {
        var key = eventId.ToString();

        Enqueue(connection =>
        {
            Execute(
                connection,
                "INSERT INTO events (event_id, run_id, at, kind, node_id, title, detail) "
                + "VALUES ($event, $id, $at, $kind, $node, $title, $detail) "
                + "ON CONFLICT(event_id) DO UPDATE SET detail = excluded.detail, title = excluded.title;",
                ("$event", key),
                ("$id", runId),
                ("$at", at.ToString("O")),
                ("$kind", kind),
                ("$node", nodeId?.ToString()),
                ("$title", title),
                ("$detail", detail));

            // The previous version of this entry leaves the index with it, so a stream that was
            // written twice is found once.
            Execute(connection, "DELETE FROM search WHERE event_id = $event;", ("$event", key));

            Index(connection, runId, key, kind, detail is null ? title : $"{title}{Environment.NewLine}{detail}");
        });
    }

    /// <summary>Records what became of one file a run dealt with.</summary>
    public void RecordFile(string runId, string relativePath, FileOutcome outcome, string? detail)
    {
        Enqueue(connection => Execute(
            connection,
            "INSERT INTO files (run_id, path, outcome, detail) VALUES ($id, $path, $outcome, $detail);",
            ("$id", runId),
            ("$path", relativePath),
            ("$outcome", outcome.ToString()),
            ("$detail", detail)));
    }

    /// <summary>
    /// Keeps what a file holds right now, before a run changes it.
    /// </summary>
    /// <remarks>
    /// Read here rather than on the writer thread, because what is on disk at the moment of the
    /// call is the thing being captured, and the writer runs behind. Only files about to change
    /// are snapshotted, which is what keeps this cheap: a run that writes three files copies three
    /// files, not a project.
    /// </remarks>
    public void Snapshot(string runId, string absolutePath)
    {
        if (!IsOpen)
        {
            return;
        }

        string? content = null;
        var existed = false;
        long bytes = 0;

        try
        {
            var info = new FileInfo(absolutePath);

            if (info.Exists)
            {
                if (info.Length > MaximumSnapshotBytes)
                {
                    // Recorded as having existed with no content, so undo refuses it by name
                    // rather than silently putting back an empty file.
                    existed = true;
                    bytes = info.Length;
                }
                else
                {
                    content = File.ReadAllText(absolutePath);
                    existed = true;
                    bytes = info.Length;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file that cannot be read cannot be put back either, and saying so at undo time is
            // more useful than refusing to run now.
            return;
        }

        var capturedAt = DateTimeOffset.Now;

        Enqueue(connection => Execute(
            connection,
            "INSERT INTO snapshots (run_id, absolute_path, existed, content, bytes, captured_at) "
            + "VALUES ($id, $path, $existed, $content, $bytes, $at);",
            ("$id", runId),
            ("$path", Path.GetFullPath(absolutePath)),
            ("$existed", existed ? 1 : 0),
            ("$content", content),
            ("$bytes", bytes),
            ("$at", capturedAt.ToString("O"))));
    }

    private void Enqueue(Action<SqliteConnection> write)
    {
        if (!IsOpen)
        {
            return;
        }

        // Unbounded and never awaited: a run reports what it did and carries straight on, which is
        // the whole reason recording cannot slow one down.
        _writes.Writer.TryWrite(write);
    }

    private void StartWriter(string path)
    {
        var stop = new CancellationTokenSource();
        _writerStop = stop;

        // A fresh queue for this database. The previous one was completed when its database was
        // closed and will not take anything else.
        var queue = NewQueue();
        _writes = queue;

        _writer = Task.Run(async () =>
        {
            await using var connection = Connect(path);
            await connection.OpenAsync(stop.Token).ConfigureAwait(false);

            // Ends when the queue is completed and empty, which is how closing drains rather
            // than discards. Cancellation is the other way out, and is now only reached when
            // draining has already taken longer than it ever should.
            while (await queue.Reader.WaitToReadAsync(stop.Token).ConfigureAwait(false))
            {
                while (queue.Reader.TryRead(out var write))
                {
                    try
                    {
                        write(connection);
                    }
                    catch (SqliteException)
                    {
                        // One row that would not go in must not take the writer down with it, or
                        // everything after it in this run goes unrecorded as well.
                    }
                }
            }
        }, stop.Token);
    }

    private static SqliteConnection Connect(string path)
        => new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default
        }.ToString());

    private static void CreateSchema(SqliteConnection connection)
    {
        // Write ahead logging is what lets the history window read while a run is still writing.
        Execute(connection, "PRAGMA journal_mode=WAL;");
        Execute(connection, "PRAGMA synchronous=NORMAL;");

        CreateVectorSchema(connection);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS runs (
                run_id TEXT PRIMARY KEY,
                started_at TEXT NOT NULL,
                ended_at TEXT,
                state TEXT NOT NULL,
                request TEXT NOT NULL,
                graph_name TEXT,
                node_count INTEGER NOT NULL DEFAULT 0,
                connection_count INTEGER NOT NULL DEFAULT 0,
                cost REAL NOT NULL DEFAULT 0,
                calls INTEGER NOT NULL DEFAULT 0,
                written INTEGER NOT NULL DEFAULT 0,
                staged INTEGER NOT NULL DEFAULT 0);
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                event_id TEXT NOT NULL UNIQUE,
                run_id TEXT NOT NULL,
                at TEXT NOT NULL,
                kind TEXT NOT NULL,
                node_id TEXT,
                title TEXT NOT NULL,
                detail TEXT);
            """);

        Execute(connection, "CREATE INDEX IF NOT EXISTS events_run ON events(run_id, id);");

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS files (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id TEXT NOT NULL,
                path TEXT NOT NULL,
                outcome TEXT NOT NULL,
                detail TEXT);
            """);

        Execute(connection, "CREATE INDEX IF NOT EXISTS files_run ON files(run_id);");

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS snapshots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id TEXT NOT NULL,
                absolute_path TEXT NOT NULL,
                existed INTEGER NOT NULL,
                content TEXT,
                bytes INTEGER NOT NULL,
                captured_at TEXT NOT NULL);
            """);

        Execute(connection, "CREATE INDEX IF NOT EXISTS snapshots_run ON snapshots(run_id);");

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS turns (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                turn_id TEXT NOT NULL UNIQUE,
                thread_id TEXT NOT NULL,
                role TEXT NOT NULL,
                text TEXT NOT NULL,
                at TEXT NOT NULL,
                run_id TEXT);
            """);

        Execute(connection, "CREATE INDEX IF NOT EXISTS turns_thread ON turns(thread_id, id);");

        // Which conversation is being talked in. One row, so that starting fresh survives a
        // restart rather than quietly resuming the thread it was meant to leave.
        Execute(connection, "CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);");

        // The index is a fraction of the content and is what makes a search over years of runs
        // return in milliseconds. A semantic layer, if one were ever wanted, would attach here:
        // a second table of vectors keyed by the same run_id, consulted alongside this one rather
        // than instead of it. It is deliberately not built, because keyword matching costs nothing,
        // needs no model, and answers most of what anybody asks.
        Execute(connection, "CREATE VIRTUAL TABLE IF NOT EXISTS search USING fts5(run_id UNINDEXED, event_id UNINDEXED, kind UNINDEXED, body);");
    }

    private static void Index(SqliteConnection connection, string runId, string eventId, string kind, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        Execute(
            connection,
            "INSERT INTO search (run_id, event_id, kind, body) VALUES ($id, $event, $kind, $body);",
            ("$id", runId),
            ("$event", eventId),
            ("$kind", kind),
            ("$body", body));
    }

    private static void Execute(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }
}
