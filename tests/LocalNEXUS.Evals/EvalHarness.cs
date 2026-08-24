using System.IO;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Compilation;
using LocalNEXUS.App.Services.Credentials;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.Distributed;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Extensions;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.Services.Planning;
using LocalNEXUS.App.Services.Processes;
using LocalNEXUS.App.Services.ProjectIndex;
using LocalNEXUS.App.Services.Python;

namespace LocalNEXUS.Evals;

/// <summary>
/// Runs the task set against one model and reports what came out.
/// </summary>
/// <remarks>
/// It drives the real graph: Prompt into Triage, Triage into the coder, the coder into the
/// compiler check, the check into the writer, with the coder wired back to Triage on the Model pin
/// so the model that writes the files is the one that plans them. That is the canonical graph this
/// application is for, and measuring anything less would measure a pipeline nobody runs.
///
/// Everything it observes is either counted directly or read off disk afterwards. Nothing parses
/// the model's prose to decide whether it did well, because that is not a thing a harness can
/// know, and a number derived that way would be confidently wrong.
/// </remarks>
public sealed class EvalHarness : IDisposable
{
    /// <summary>
    /// A generous ceiling on one task, after which it is recorded as having run out of time.
    /// </summary>
    /// <remarks>
    /// A local model on a cold load is slow, and a plan of several files is several requests. This
    /// is set high enough that hitting it means something is wrong rather than that the machine is
    /// busy.
    /// </remarks>
    public static readonly TimeSpan TaskTimeout = TimeSpan.FromMinutes(20);

    private readonly DispatcherLoop _loop = new();
    private readonly ChildProcessGroup _children = new();
    private readonly MeasuringModelClient _models;
    private readonly RuntimeResolver _runtimes;
    private readonly ActivityFeed _feed;
    private readonly AppConfig _config = new();
    private readonly EvalOptions _options;
    private readonly NodeFactory _factory;
    private readonly MeshManager _mesh;
    private readonly ExtensionRegistry _extensions;
    private readonly RoslynUnityCompiler _compiler;

    public EvalHarness(EvalOptions options)
    {
        _options = options;

        var dispatcher = _loop.Dispatcher;

        _feed = new ActivityFeed(dispatcher);

        // The real router, wrapped so every request is weighed. Nothing about the requests
        // changes; the decorator only watches.
        _models = new MeasuringModelClient(new ModelClientRouter(
            new OpenAiCompatibleClient(),
            new AnthropicClient(),
            new GeminiClient()));

        _runtimes = new RuntimeResolver(
            new LlamaServerManager(_children),
            new PythonRuntimeManager(_children, new PythonProvisioner(_children, _feed, dispatcher)));

        _mesh = new MeshManager(_config, _feed, dispatcher, _children);
        _extensions = new ExtensionRegistry(_feed);
        _compiler = new RoslynUnityCompiler(new UnityReferenceResolver());

        _factory = new NodeFactory(
            new ModelCatalog(_config),
            _mesh,
            new SilentDialogService(),
            _config,
            _extensions,
            new ExtensionHost(_children, _feed),
            new InMemoryCredentialStore());
    }

    /// <summary>Runs every selected task, in order, the requested number of times.</summary>
    public async Task<EvalRun> RunAsync(string modelPath, IReadOnlyList<EvalTask> tasks, CancellationToken ct)
    {
        var whole = System.Diagnostics.Stopwatch.StartNew();
        var results = new List<TaskResult>();

        var conditions = DescribeConditions(modelPath, tasks);

        for (var attempt = 1; attempt <= _options.Repeats; attempt++)
        {
            foreach (var task in tasks)
            {
                ct.ThrowIfCancellationRequested();

                Console.WriteLine($"  {task.Id} (attempt {attempt} of {_options.Repeats})");

                var result = await RunOneAsync(modelPath, task, attempt, ct).ConfigureAwait(false);
                results.Add(result);

                Console.WriteLine(
                    $"    {(result.MetTheBar(task) ? "met the bar" : "did not")}, "
                    + $"{result.FilesCompiledFirstPass}/{result.FilesChecked} first pass, "
                    + $"{result.RepairAttempts} repair(s), {result.WallTime.TotalSeconds:0} s");
            }
        }

        // The server stays up between tasks so that only the first one pays for loading the model,
        // and comes down when the model is finished with.
        _runtimes.ShutdownAll();

        whole.Stop();
        return new EvalRun(conditions, results, whole.Elapsed);
    }

    private async Task<TaskResult> RunOneAsync(string modelPath, EvalTask task, int attempt, CancellationToken outer)
    {
        using var project = ScratchProject.Create(task);
        using var clock = CancellationTokenSource.CreateLinkedTokenSource(outer);
        clock.CancelAfter(TaskTimeout);

        var ct = clock.Token;

        _models.Reset();
        _feed.Clear();

        var projectService = new ProjectService();
        projectService.Open(project.Root);

        var index = new ProjectIndexService();
        var staging = new StagingStore(_loop.Dispatcher);
        staging.OpenProject(project.Root);

        var cost = new RunCostTracker();

        var services = new ExecutionServices(
            _models,
            _runtimes,
            _mesh,
            _compiler,
            index,
            projectService,
            new FileWriter(),
            _feed,
            staging,
            // No history store and no conversation. A record of the run is not what is being
            // measured, and a conversation nobody is present for would leave Triage waiting for an
            // answer to a question it can proceed without.
            null,
            null,
            _extensions,
            null,
            new InMemoryCredentialStore(),
            cost);

        var (graph, coder, check, triage) = BuildGraph(modelPath);

        // What the application seeds a new Model node with for this kind of project. Set here
        // rather than by handing the factory the project's settings, because that would also move
        // where the Output node writes, and a measurement with two things changed in it measures
        // neither.
        coder.SystemPrompt = ModelNode.PromptFor(projectService.Kind);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        RunContext? run = null;
        string? fault = null;

        try
        {
            run = await new GraphExecutor(services).RunAsync(graph, task.Request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            fault = $"{ex.GetType().Name}: {ex.Message}";
        }

        watch.Stop();

        return Measure(task, attempt, project, index, staging, run, fault, watch.Elapsed, coder, check, triage, cost);
    }

    /// <summary>
    /// The canonical graph, wired the way somebody using this application would wire it.
    /// </summary>
    private (GraphModel Graph, ModelNode Coder, CompilerCheckNode Check, TriageNode Triage) BuildGraph(string modelPath)
    {
        var graph = new GraphModel { Name = "eval" };

        var prompt = (PromptNode)_factory.Create("Prompt");
        var triage = (TriageNode)_factory.Create("Triage");
        var coder = (ModelNode)_factory.Create("Model");
        var check = (CompilerCheckNode)_factory.Create("CompilerCheck");
        var output = (OutputNode)_factory.Create("Output");

        coder.Provider = ModelProvider.Local;
        coder.ModelFilePath = modelPath;
        coder.ContextSize = _options.ContextSize;
        coder.GpuLayers = _options.GpuLayers;
        coder.MaxTokens = _options.MaxTokens;
        coder.Temperature = _options.Temperature;

        check.RetryLimit = _options.RetryLimit;

        // Files that will not compile are held back rather than stopping the run, which is what
        // makes a partial outcome measurable instead of collapsing to one failure.
        check.FailureBehaviour = CompileFailureBehaviour.StageForLater;

        // Nobody is here to answer a dialog.
        output.AskBeforeWriting = false;

        foreach (var node in new NodeBase[] { prompt, triage, coder, check, output })
        {
            graph.AddNode(node);
        }

        Connect(graph, prompt.Request, triage.Request);
        Connect(graph, coder.Self, triage.Model);
        Connect(graph, triage.Plan, coder.Prompt);
        Connect(graph, coder.Completion, check.Code);
        Connect(graph, check.Checked, output.Content);

        return (graph, coder, check, triage);
    }

    private static void Connect(GraphModel graph, Pin source, Pin target)
    {
        if (!graph.TryConnect(source, target, out var why))
        {
            throw new InvalidOperationException($"The eval graph could not be wired: {why}");
        }
    }

    /// <summary>Reads the numbers out of what the run left behind.</summary>
    private TaskResult Measure(
        EvalTask task,
        int attempt,
        ScratchProject project,
        ProjectIndexService index,
        StagingStore staging,
        RunContext? run,
        string? fault,
        TimeSpan wallTime,
        ModelNode coder,
        CompilerCheckNode check,
        TriageNode triage,
        RunCostTracker cost)
    {
        var plan = run is not null && run.TryGetValue(triage.Plan, out var planned)
            ? planned as IReadOnlyList<CodeTask> ?? Array.Empty<CodeTask>()
            : Array.Empty<CodeTask>();

        var generated = run is not null && run.TryGetValue(check.Checked, out var checkedFiles)
            ? checkedFiles as IReadOnlyList<GeneratedFile> ?? Array.Empty<GeneratedFile>()
            : Array.Empty<GeneratedFile>();

        // Read off the file the check emitted. It used to be counted by matching the wording of an
        // activity feed title, which was the weakest measurement in the harness and the one thing
        // here that a reworded log line would have silently zeroed.
        var repairAttempts = generated.Sum(f => f.Repairs);

        var compiledFiles = generated.Where(f => f.Check == FileCheckState.Compiled).ToList();
        var firstPass = compiledFiles.Count(f => f.Repairs == 0);
        var repairedAndCompiled = compiledFiles.Count - firstPass;

        var newFiles = project.NewFiles();
        var changed = project.ChangedFiles();

        var expectedNew = task.ExpectedNewFiles
            .Count(name => newFiles.Any(p => p.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase)));

        var expectedEdits = task.ExpectedEditedFiles
            .Count(name => changed.Any(p => p.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase)));

        var unexpected = newFiles
            .Where(p => !task.ExpectedNewFiles.Any(name => p.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Read from the run rather than from the staging list. Staging says a write was refused;
        // the run says which of seven rules refused it, which is the difference between knowing
        // that something happened and knowing what happened.
        var decisions = run?.Decisions ?? Array.Empty<RunDecision>();

        var refusals = decisions
            .Where(d => d.Kind == RunDecisionKind.WriteRefused)
            .Select(d => $"{d.Rule} on {d.RelativePath}")
            .ToList();

        var blocked = decisions
            .Where(d => d.Kind == RunDecisionKind.DuplicateRefused)
            .Select(d => $"{d.Subject} wanted at {d.RelativePath}, already in {d.OtherPath ?? "this same plan"}")
            .ToList();

        var verdicts = decisions
            .Where(d => d.Kind == RunDecisionKind.CandidateVerdict)
            .Select(d => $"{d.RelativePath}: {d.Rule}{(d.Subject is { Length: > 0 } s ? $" {s}" : string.Empty)}")
            .ToList();

        var calls = _models.Calls;

        return new TaskResult(
            task.Id,
            task.Shape,
            attempt,
            fault is not null || run?.State == RunState.Faulted,
            fault ?? FaultFromRun(run),
            run?.State.ToString() ?? "NeverStarted",
            plan.Count,
            plan.Select(t => t.ToString()).ToList(),
            CountPlannedFilesLanded(plan, newFiles, changed),
            firstPass,
            repairedAndCompiled,
            generated.Count(f => f.Check == FileCheckState.DidNotCompile),
            generated.Count(f => f.Check == FileCheckState.Inconclusive),
            repairAttempts,
            expectedNew,
            expectedEdits,
            unexpected,
            project.DeletedFiles(),
            project.ScriptsMissingTheirMeta(),
            DuplicateTypesOnDisk(project),
            blocked,
            plan.Count(t => t.Choice == PlanChoice.CreateNew),
            plan.Count(t => t.Choice == PlanChoice.EditExisting),
            plan.Where(t => t.ExistingType is { Length: > 0 })
                .Select(t => $"{t.ExistingType} in {t.ExistingTypePath}")
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            verdicts,
            refusals,
            task.ExpectsRefusal,
            staging.Count,
            generated.Count(f => f.Content.Contains("```", StringComparison.Ordinal)),
            calls.Count,
            calls.Sum(c => c.PromptTokens ?? 0),
            calls.Sum(c => c.CompletionTokens ?? 0),
            cost.Calls > 0 ? cost.Total : null,
            wallTime,
            TimeSpan.FromTicks(calls.Sum(c => c.Elapsed.Ticks)),
            calls.FirstOrDefault()?.ToFirstToken,
            calls.Count(c => string.Equals(c.FinishReason, "length", StringComparison.OrdinalIgnoreCase)),
            generated.Sum(f => f.Content.Length),
            decisions.Count(d => d.Kind == RunDecisionKind.ClarificationAsked),
            changed,
            CaptureTouchedFiles(project, newFiles, changed),
            calls.Select(c => c.Reply).ToList());
    }

    /// <summary>
    /// What every file the run touched looks like now.
    /// </summary>
    /// <remarks>
    /// Kept because several of the numbers cannot be read without it. A guardrail that did not
    /// fire is either a change that legitimately trips no rule or a rule that missed one, and
    /// those are opposite findings that look identical in a count. The same goes for an edit that
    /// landed: whether it added the method or replaced the file is not something a byte count
    /// says. These files are a few hundred bytes each and there are a handful per task, so keeping
    /// them costs nothing worth saving.
    /// </remarks>
    private static IReadOnlyDictionary<string, string> CaptureTouchedFiles(
        ScratchProject project,
        IReadOnlyList<string> newFiles,
        IReadOnlyList<string> changed)
    {
        var all = project.ReadAllScripts();
        var touched = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in newFiles.Concat(changed).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (all.TryGetValue(path, out var content))
            {
                touched[path] = content;
            }
        }

        return touched;
    }

    /// <summary>
    /// How many of the planner's own rows actually reached disk.
    /// </summary>
    /// <remarks>
    /// Deliberately measured against the plan rather than against what the task wanted. A run that
    /// carried out every row of a plan that was the wrong plan scores full marks here and nothing
    /// on the expectations, and being able to see those apart is what says whether the planner or
    /// the coder is the thing to look at.
    /// </remarks>
    private static int CountPlannedFilesLanded(
        IReadOnlyList<CodeTask> plan,
        IReadOnlyList<string> newFiles,
        IReadOnlyList<string> changed)
    {
        var landed = newFiles.Concat(changed).Select(Normalise).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return plan.Count(t => landed.Contains(Normalise(t.RelativePath)));
    }

    /// <summary>
    /// Types declared in more than one file, read from disk rather than from anything reported.
    /// </summary>
    /// <remarks>
    /// The measurement that matters most, and it is taken independently of everything the
    /// application says about itself. A guard that reported a refusal and let the file through
    /// anyway would look correct in every other field here and be caught by this one.
    /// </remarks>
    private static IReadOnlyList<string> DuplicateTypesOnDisk(ScratchProject project)
    {
        var index = new ProjectIndexService();
        index.EnsureAsync(project.Root, null, CancellationToken.None).GetAwaiter().GetResult();

        return index.Files
            .SelectMany(file => file.Types.Select(type => (type.FullName, file.RelativePath)))
            .GroupBy(pair => pair.FullName, StringComparer.Ordinal)
            .Where(group => group.Select(g => g.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(group => $"{group.Key} in {string.Join(", ", group.Select(g => g.RelativePath).Distinct())}")
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();
    }

    private static string Normalise(string path) => path.Replace('\\', '/').Trim();


    /// <summary>
    /// Why a run stopped, in enough words to act on.
    /// </summary>
    /// <remarks>
    /// This used to read the title of the last error entry, which for a node fault is the node's
    /// name and nothing else, so every one of thirteen faults in two hundred runs was reported as
    /// the single word "Triage". The run itself carries the detail; it just was not being asked.
    /// </remarks>
    private string? FaultFromRun(RunContext? run)
    {
        if (run?.FaultMessage is { Length: > 0 } message)
        {
            return message;
        }

        var entry = _feed.Events.LastOrDefault(e => e.Kind is ActivityKind.Error or ActivityKind.NodeFaulted);

        return entry is null
            ? null
            : string.IsNullOrWhiteSpace(entry.Text) ? entry.Title : $"{entry.Title}: {entry.Text}";
    }

    private RunConditions DescribeConditions(string modelPath, IReadOnlyList<EvalTask> tasks)
    {
        var name = Path.GetFileName(modelPath);

        return new RunConditions(
            name,
            modelPath,
            ReadQuantization(name),
            _options.ContextSize,
            _options.GpuLayers,
            _options.Temperature,
            _options.MaxTokens,
            new TriageNode().Budget.Summary,
            TaskSets.VersionFor(tasks),
            AppVersion(),
            Environment.MachineName,
            DateTimeOffset.Now);
    }

    /// <summary>
    /// The quantization, taken from the file name.
    /// </summary>
    /// <remarks>
    /// Read from the name rather than the file because the name is where it is actually stated in
    /// practice, and because reading it properly would mean parsing GGUF metadata for one string
    /// on a report. When the name does not say, this says it does not know rather than guessing.
    /// </remarks>
    private static string ReadQuantization(string fileName)
    {
        var parts = Path.GetFileNameWithoutExtension(fileName).Split('.', '-', '_');

        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length >= 2
                && (parts[i][0] is 'Q' or 'q' or 'F' or 'f' or 'I' or 'i')
                && char.IsDigit(parts[i][1]))
            {
                return string.Join("_", parts.Skip(i));
            }
        }

        return "not stated in the file name";
    }

    private static string AppVersion()
        => typeof(NodeFactory).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion
           ?? typeof(NodeFactory).Assembly.GetName().Version?.ToString()
           ?? "unknown";

    public void Dispose()
    {
        _runtimes.ShutdownAll();
        _children.Dispose();
        _loop.Dispose();
    }
}

/// <summary>A dialog service that answers nothing, because a harness has nobody to ask.</summary>
internal sealed class SilentDialogService : IDialogService
{
    public string? PickFolder(string title, string? initialDirectory = null) => null;

    public string? PickOpenFile(string title, string filter, string? initialDirectory = null) => null;

    public string? PickSaveFile(string title, string defaultFileName, string filter, string? initialDirectory = null) => null;

    public void ShowError(string title, string message)
    {
    }

    /// <summary>A test never waits on a person, so nothing is confirmed.</summary>
    public bool Confirm(string title, string message) => false;

    public void OpenFolderInExplorer(string folder)
    {
    }

    public void OpenFileInEditor(string file)
    {
    }

    public void CopyToClipboard(string text)
    {
    }
}

/// <summary>
/// A credential store that keeps nothing.
/// </summary>
/// <remarks>
/// The real one reads and writes the user's own encrypted key file, and a harness has no business
/// touching it. Every task here runs against a local model, so there is no key to need.
/// </remarks>
internal sealed class InMemoryCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, string> _keys = new(StringComparer.OrdinalIgnoreCase);

    public string? Get(string providerId) => _keys.TryGetValue(providerId, out var key) ? key : null;

    public bool Has(string providerId) => Get(providerId) is not null;

    public void Set(string providerId, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            _keys.Remove(providerId);
            return;
        }

        _keys[providerId] = key;
    }

    public void Remove(string providerId) => _keys.Remove(providerId);

    public IReadOnlyCollection<string> ConfiguredProviders() => _keys.Keys.ToList();
}
