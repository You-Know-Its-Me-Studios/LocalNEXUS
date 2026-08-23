using System.IO;
using System.Windows.Threading;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.History;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.Services.ProjectIndex;
using LocalNEXUS.App.ViewModels;

namespace LocalNEXUS.App.Services.Mcp;

/// <summary>
/// The application's answer to each of the eight things a tool call may ask for.
/// </summary>
/// <remarks>
/// Everything here happens on the dispatcher, because all of it touches state the window is bound
/// to and a pipe call arrives on a thread pool thread.
///
/// A run goes through the same command the Run button does, deliberately and not merely
/// conveniently. That command composes the request, opens a history record, tells the conversation,
/// resets the cost, runs the executor and files the outcome; a second path that did most of that
/// would be a run that behaved almost the same, and almost is the failure. Pressing Run and calling
/// this are the same code from the first line.
/// </remarks>
public sealed class McpAppSurface : IMcpAppSurface
{
    private readonly Dispatcher _dispatcher;
    private readonly ProjectService _project;
    private readonly ProjectIndexService _index;
    private readonly GraphModel _graph;
    private readonly MainViewModel _main;
    private readonly ActivityFeedViewModel _feed;
    private readonly RunHistoryStore _history;
    private readonly Func<string, CancellationToken, Task> _openProject;

    public McpAppSurface(
        Dispatcher dispatcher,
        ProjectService project,
        ProjectIndexService index,
        GraphModel graph,
        MainViewModel main,
        ActivityFeedViewModel feed,
        RunHistoryStore history,
        Func<string, CancellationToken, Task> openProject)
    {
        _dispatcher = dispatcher;
        _project = project;
        _index = index;
        _graph = graph;
        _main = main;
        _feed = feed;
        _history = history;
        _openProject = openProject;
    }

    /// <inheritdoc />
    public async Task<string> OpenProjectAsync(string path, CancellationToken ct)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"There is no folder at {path}.");
        }

        await _openProject(path, ct).ConfigureAwait(false);

        return await OnDispatcher(() =>
            $"Opened {_project.ProjectName} at {_project.ProjectPath}. "
            + $"Detected as a {_project.KindText}. "
            + (_project.IsUnity
                ? "The Unity write rules are in force: a file name has to match its MonoBehaviour, and a "
                  + "type, namespace or serialized field cannot quietly change name. "
                : "The Unity write rules do not apply. ")
            + $"{_index.FileCount} source file(s) indexed, {_index.TypeCount} type(s).").ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<string> DescribeStateAsync(CancellationToken ct) => OnDispatcher(() =>
    {
        var lines = new List<string>
        {
            _project.HasProject
                ? $"Project: {_project.ProjectName} ({_project.KindText}) at {_project.ProjectPath}"
                : "Project: none open. Use localnexus_open_project.",
            $"Index: {_index.StatusText}",
            $"Graph: {_graph.Name}, {_graph.Nodes.Count} node(s), {_graph.Connections.Count} connection(s)",
            $"Run: {_feed.RunState}"
        };

        var unconfigured = _graph.Nodes
            .OfType<Nodes.ModelNode>()
            .Where(m => !m.IsConfigured)
            .ToList();

        if (unconfigured.Count > 0)
        {
            lines.Add(
                $"{unconfigured.Count} Model node(s) have no model chosen, so a run would fail. "
                + "Choose one in the window; a model is a machine setting rather than something a "
                + "caller should pick.");
        }

        return string.Join(Environment.NewLine, lines);
    });

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListGraphsAsync(CancellationToken ct) => OnDispatcher(() =>
    {
        var names = new List<string>();

        foreach (var template in _main.Templates)
        {
            names.Add($"{template.Name} (template): {template.Description}");
        }

        // The project's own graphs. The tool, its arguments and what it answers with are
        // unchanged; only where a graph is looked for is, because a graph lives with the project
        // it was arranged against.
        foreach (var folder in ProjectPaths.GraphFolders(_project.ProjectPath))
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*" + GraphSerializer.FileExtension))
            {
                names.Add(NameOf(path) + " (saved)");
            }
        }

        return (IReadOnlyList<string>)names;
    });

    /// <inheritdoc />
    public Task<string> OpenGraphAsync(string name, CancellationToken ct) => OnDispatcher(() =>
    {
        var template = _main.Templates
            .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        if (template is not null)
        {
            _main.ApplyTemplateCommand.Execute(template);
            return Describe();
        }

        var path = ProjectPaths.GraphFolders(_project.ProjectPath)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*" + GraphSerializer.FileExtension))
            .FirstOrDefault(p => string.Equals(NameOf(p), name, StringComparison.OrdinalIgnoreCase));

        if (path is null)
        {
            throw new FileNotFoundException(
                $"There is no graph or template called '{name}'. Use localnexus_list_graphs for what there is.");
        }

        _main.LoadGraphFrom(path);
        return Describe();

        string Describe()
            => $"Opened {_graph.Name}: {_graph.Nodes.Count} node(s), {_graph.Connections.Count} connection(s). "
               + string.Join(", ", _graph.Nodes.Select(n => n.Title));
    });

    /// <inheritdoc />
    public async Task<McpRunHandle> StartRunAsync(string request, CancellationToken ct)
    {
        var started = await OnDispatcher(() =>
        {
            if (_graph.Nodes.Count == 0)
            {
                throw new InvalidOperationException(
                    "The canvas is empty. Open a graph with localnexus_open_graph first.");
            }

            // The text goes in before the command is asked whether it can run, because one of the
            // things it checks is that there is something to run with. Asking first and setting
            // after was refused every time, which is what running this against the real window
            // found and no amount of reading the code had.
            _feed.RequestText = request;

            if (!_feed.RunCommand.CanExecute(null))
            {
                _feed.RequestText = string.Empty;

                throw new InvalidOperationException(
                    $"The graph cannot be run right now. The run state is {_feed.RunState}"
                    + (_feed.IsRunning
                        ? ", so a run already in progress has to finish first."
                        : ". Check that a graph is open and that the Workspace is the active section."));
            }

            _feed.RunCommand.Execute(null);

            return true;
        }).ConfigureAwait(false);

        _ = started;

        return await RunStateAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<McpRunHandle> RunStateAsync(CancellationToken ct) => OnDispatcher(() =>
    {
        var state = _feed.RunState;

        return new McpRunHandle(
            _feed.CurrentRunId,
            state.ToString(),
            state is not (RunState.Running or RunState.Paused));
    });

    /// <inheritdoc />
    public async Task<string> DescribeRunAsync(string? runId, CancellationToken ct)
    {
        var id = runId ?? await OnDispatcher(() => _feed.CurrentRunId).ConfigureAwait(false);

        if (id is null)
        {
            return "Nothing has run yet in this session, and no run was named.";
        }

        var runs = await _history.ListRunsAsync(McpToolSurface.MaximumRunLimit, ct).ConfigureAwait(false);
        var summary = runs.FirstOrDefault(r => string.Equals(r.RunId, id, StringComparison.Ordinal));

        if (summary is null)
        {
            return $"There is no run called {id} in the history.";
        }

        var files = await _history.ReadFilesAsync(id, ct).ConfigureAwait(false);
        var events = await _history.ReadEventsAsync(id, ct).ConfigureAwait(false);

        var text = new System.Text.StringBuilder();

        text.AppendLine($"Run {summary.RunId}, {summary.State}, started {summary.StartedAt:yyyy-MM-dd HH:mm:ss}, took {summary.Duration}.");
        text.AppendLine($"Request: {summary.Request}");
        text.AppendLine($"{summary.NodeCount} node(s). {summary.Written} file(s) written, {summary.Staged} held back.");

        if (summary.Cost > 0)
        {
            text.AppendLine($"Cost: {RunCost.Format(summary.Cost)}.");
        }

        if (files.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Files:");

            foreach (var file in files)
            {
                text.AppendLine($"- {file.Path}: {file.Outcome}{(file.Detail is { Length: > 0 } d ? $" ({Flatten(d)})" : string.Empty)}");
            }
        }

        // The refusals and the compile outcomes, which are the two things worth reading and are
        // in the event log rather than in the summary.
        var notable = events
            .Where(e => e.Title.Contains("refused", StringComparison.OrdinalIgnoreCase)
                        || e.Kind.Contains("Fault", StringComparison.OrdinalIgnoreCase)
                        || e.Title.Contains("compiled", StringComparison.OrdinalIgnoreCase)
                        || e.Title.Contains("not checked", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (notable.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("What happened:");

            foreach (var entry in notable)
            {
                text.AppendLine($"- {entry.Title}{(entry.Detail is { Length: > 0 } d ? $": {Flatten(d)}" : string.Empty)}");
            }
        }

        return text.ToString().TrimEnd();
    }

    /// <inheritdoc />
    public async Task<string> ListRunsAsync(int limit, CancellationToken ct)
    {
        var runs = await _history.ListRunsAsync(limit, ct).ConfigureAwait(false);

        if (runs.Count == 0)
        {
            return "Nothing has run yet.";
        }

        return string.Join(
            Environment.NewLine,
            runs.Select(r =>
                $"- {r.RunId}  {r.StartedAt:yyyy-MM-dd HH:mm}  {r.State}  "
                + $"{r.Written} written, {r.Staged} held  {r.RequestLine}"));
    }

    private static string NameOf(string path)
    {
        var name = Path.GetFileName(path);
        var cut = name.IndexOf(GraphSerializer.FileExtension, StringComparison.OrdinalIgnoreCase);

        return cut > 0 ? name[..cut] : Path.GetFileNameWithoutExtension(name);
    }

    /// <summary>A detail on one line, because a caller is reading a list.</summary>
    private static string Flatten(string text)
    {
        var flat = text.ReplaceLineEndings(" ").Trim();

        return flat.Length <= 300 ? flat : flat[..300] + "...";
    }

    private Task<T> OnDispatcher<T>(Func<T> work)
        => _dispatcher.CheckAccess()
            ? Task.FromResult(work())
            : _dispatcher.InvokeAsync(work).Task;
}
