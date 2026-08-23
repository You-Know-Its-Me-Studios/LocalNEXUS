using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Compilation;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Planning;

namespace LocalNEXUS.App.Nodes;

/// <summary>
/// Compiles the code arriving on its input, and asks whoever produced it to fix what does not.
/// </summary>
/// <remarks>
/// This is what makes a run's success mean something. Without it a model can produce plausible,
/// broken C#, the file is written, and the run reports that everything went well.
///
/// The repair loop lives here rather than in the executor, which orders nodes and knows nothing
/// about any of them. This node follows its own incoming wire, asks whatever it finds there
/// whether it implements <see cref="ICodeRepairSource"/>, and if it does, hands it the compiler
/// errors and asks for another go. It never names a node type, so a coder node is not special
/// cased and a node that cannot revise simply reports that it cannot.
///
/// A cycle in the graph would be the other way to express this, and it is not available: the
/// executor rejects cycles, and it is right to, because the retry cap belongs in a setting rather
/// than in how many times somebody drew a loop.
///
/// When a whole plan arrives rather than one file, every file is checked against every other file
/// of the same plan as one compilation. That is the answer to the obvious problem with checking a
/// file at a time: the third file of a plan legitimately calls into the first, and neither Unity
/// nor the project's compiled assemblies have ever seen either of them.
/// </remarks>
public sealed partial class CompilerCheckNode : NodeBase
{
    /// <summary>Repair attempts allowed by default after the first failure.</summary>
    public const int DefaultRetryLimit = 3;

    /// <summary>The most attempts the panel will accept, so a typo cannot start a hundred model calls.</summary>
    public const int MaximumRetryLimit = 10;

    /// <summary>
    /// How many diagnostics are sent back to the coder. One missing brace can produce fifty knock
    /// on errors, and burying the real one under them makes the fix less likely, not more.
    /// </summary>
    private const int DiagnosticsSentToCoder = 20;

    /// <summary>How many diagnostics go into the feed and the failure message.</summary>
    private const int DiagnosticsShown = 12;

    /// <summary>The name used for diagnostics when the code declares no type to take one from.</summary>
    private const string FallbackFileName = "Generated.cs";

    /// <summary>Repair attempts allowed after the first failed compile.</summary>
    [ObservableProperty]
    private int _retryLimit = DefaultRetryLimit;

    /// <summary>What happens to the run when the code still does not compile.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FaultsTheRun))]
    private CompileFailureBehaviour _failureBehaviour = CompileFailureBehaviour.StageForLater;

    /// <summary>How the last check ended. Drives the badge in the settings panel.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutcomeText))]
    private CompileOutcome _outcome = CompileOutcome.NotRun;

    /// <summary>The compiler diagnostics from the last check, as a compiler prints them.</summary>
    [ObservableProperty]
    private string _lastDiagnostics = string.Empty;

    /// <summary>
    /// The same diagnostics with their parts still separate, which is what the Problems panel
    /// lists.
    /// </summary>
    /// <remarks>
    /// A whole new list is published rather than one being added to, because a check runs off the
    /// UI thread and the binding engine marshals a property change for us but not a collection
    /// change. That is also why this sits beside the printed form rather than replacing it: the
    /// text is what goes to the model during a repair, and it has already earned its place.
    /// </remarks>
    [ObservableProperty]
    private IReadOnlyList<CompileDiagnostic> _lastProblems = Array.Empty<CompileDiagnostic>();

    /// <summary>What the last check compiled against, so a passing result can be judged.</summary>
    [ObservableProperty]
    private string _referenceSummary = string.Empty;

    /// <summary>
    /// What this node can reach, asked before anything runs.
    /// </summary>
    /// <remarks>
    /// Visible on the node on purpose. Under a partial set a type the project defines reads as a
    /// type that does not exist, and somebody who does not know that spends three repair attempts
    /// and a good deal of patience on errors that were never in the code. Knowing beforehand is
    /// the difference between a weaker check and a misleading one.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReachabilityIsPartial))]
    private CompileReferenceState _reachability = CompileReferenceState.Unknown;

    /// <summary>The one line form of what it can reach, for the node and the panel.</summary>
    [ObservableProperty]
    private string _reachabilityText = "Not checked yet";

    /// <summary>What it would be compiling against right now, in full.</summary>
    [ObservableProperty]
    private string _reachabilityDetail = string.Empty;

    public CompilerCheckNode()
        : base("Compiler check")
    {
        Code = AddInput("Code", PinType.Code);
        Checked = AddOutput("Code", PinType.Code);
    }

    /// <summary>Receives the code to check.</summary>
    public Pin Code { get; }

    /// <summary>Carries onward the code that compiled, or the last attempt when failure is tolerated.</summary>
    public Pin Checked { get; }

    /// <inheritdoc />
    public override string TypeKey => "CompilerCheck";

    /// <summary>True when a failed check stops the run. Bound by the settings panel.</summary>
    public bool FaultsTheRun => FailureBehaviour == CompileFailureBehaviour.FaultTheRun;

    /// <summary>One line describing how the last check ended.</summary>
    public string OutcomeText => Outcome switch
    {
        CompileOutcome.Checking => "Checking",
        CompileOutcome.Compiled => "Compiled",
        CompileOutcome.Repaired => "Repaired, then compiled",
        CompileOutcome.Failed => "Did not compile",
        CompileOutcome.Inconclusive => "Could not tell, references incomplete",
        CompileOutcome.Unavailable => "Could not be checked",
        _ => "Not run yet"
    };

    /// <summary>True when what it can reach is short of what the code may legitimately use.</summary>
    public bool ReachabilityIsPartial
        => Reachability is CompileReferenceState.ProjectNotCompiled
            or CompileReferenceState.ProjectNotRestored
            or CompileReferenceState.FrameworkOnly;

    /// <summary>
    /// Asks the compiler what it can reach and records the answer, without compiling anything.
    /// </summary>
    /// <remarks>
    /// Called when a project is opened or closed and when the node is added, so the node is
    /// telling the truth before a run rather than after one. It is a real probe: it builds the
    /// reference set, so a Unity install that is present but unreadable answers unreadable rather
    /// than answering from the fact that a folder exists.
    /// </remarks>
    public void RefreshReachability(ICodeCompiler compiler, string? projectPath)
    {
        ArgumentNullException.ThrowIfNull(compiler);

        var set = compiler.DescribeReferences(projectPath);

        Reachability = set.State;
        ReachabilityText = set.Reachability;
        ReachabilityDetail = set.Summary;
    }

    /// <inheritdoc />
    public override async Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
    {
        var arrived = ctx.GetValue(Code);

        // A plan is checked as a set rather than as items, because each file has to see the ones
        // settled before it. That is a property of compiling, not of iterating.
        if (arrived is IReadOnlyList<GeneratedFile> files)
        {
            return await CheckPlanAsync(ctx, files, ct).ConfigureAwait(false);
        }

        // Any other list is worked through one entry at a time. This used to stringify whatever it
        // did not recognise, so five pieces of code arriving as a list were concatenated and
        // compiled as one file.
        if (FanOut.TryItems(arrived, out var items))
        {
            ctx.Feed.Info($"{Title}: {items.Count} item(s) to check", null);

            return await FanOut.OverAsync(
                this,
                Code,
                items,
                ctx,
                ct,
                (itemContext, index, token) =>
                {
                    StatusMessage = $"{index + 1} of {items.Count}";
                    return CheckOnceAsync(itemContext, token);
                }).ConfigureAwait(false);
        }

        return await CheckOnceAsync(ctx, ct).ConfigureAwait(false);
    }

    /// <summary>Compiles whatever is on the code pin, once, and repairs it if it does not build.</summary>
    private async Task<NodeResult> CheckOnceAsync(NodeExecutionContext ctx, CancellationToken ct)
    {
        var source = ctx.GetText(Code);

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new InvalidOperationException(
                $"{Title} received nothing to check. Connect a node to its Code pin.");
        }

        Outcome = CompileOutcome.Checking;
        LastDiagnostics = string.Empty;
        LastProblems = Array.Empty<CompileDiagnostic>();

        var compiler = ctx.Services.Compiler;
        var projectPath = ctx.Services.Project.ProjectPath;
        var fileName = RoslynUnityCompiler.DeriveFileName(source, FallbackFileName);

        CompileResult result;
        try
        {
            result = await compiler.CompileAsync(source, fileName, projectPath, ct).ConfigureAwait(false);
        }
        catch (CompilerUnavailableException ex)
        {
            return Unavailable(ctx, source, ex);
        }

        ReferenceSummary = result.ReferenceSummary;
        ReportAttempt(ctx, attempt: 0, fileName, result);

        if (result.Succeeded)
        {
            Outcome = CompileOutcome.Compiled;
            StatusMessage = $"{fileName} compiled in {result.Elapsed.TotalMilliseconds:0} ms";
            return NodeResult.FromPin(Checked, source);
        }

        if (result.IsInconclusive)
        {
            return Inconclusive(ctx, source, fileName, result);
        }

        var repaired = await TryRepairAsync(ctx, source, fileName, result, ct).ConfigureAwait(false);

        if (repaired.Result.Succeeded)
        {
            Outcome = CompileOutcome.Repaired;
            StatusMessage = $"{fileName} compiled after {repaired.Attempts} repair attempt(s)";
            return NodeResult.FromPin(Checked, repaired.Code);
        }

        return Fail(ctx, fileName, repaired, ct);
    }

    /// <summary>
    /// Checks every file of a plan, each against the ones before it, repairing as it goes.
    /// </summary>
    /// <remarks>
    /// The accumulated set is what makes this work. File one is compiled alone, file two with
    /// file one, and so on, so a call into a sibling generated moments earlier resolves. The
    /// diagnostics still carry the file they came from, and only the file being checked is ever
    /// offered for repair: a file that already passed is not rewritten because a later one broke.
    /// </remarks>
    private async Task<NodeResult> CheckPlanAsync(
        NodeExecutionContext ctx,
        IReadOnlyList<GeneratedFile> files,
        CancellationToken ct)
    {
        if (files.Count == 0)
        {
            throw new InvalidOperationException($"{Title} received an empty plan to check.");
        }

        Outcome = CompileOutcome.Checking;
        LastDiagnostics = string.Empty;
        LastProblems = Array.Empty<CompileDiagnostic>();

        var settled = new List<GeneratedFile>();

        // What later files are compiled against, which is not the same list. A file that did not
        // compile is not something anything can legitimately be built on: putting it in the set
        // hands its errors to every file after it, so one broken row failed the whole rest of the
        // plan for reasons that had nothing to do with any of them. Worse, only the file being
        // checked is ever offered for repair, so nothing downstream could fix what was actually
        // wrong and the run reported a pile of unrepairable files.
        //
        // A file whose only complaints were missing references is kept, because what it declares
        // is real and later files legitimately depend on it. Those complaints are untrusted
        // wherever they surface again.
        var compiling = new List<GeneratedFile>();
        var repairs = 0;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            var checkedFile = await CheckOneAsync(ctx, compiling, file, files.Count, ct).ConfigureAwait(false);

            if (checkedFile.Repairs > 0)
            {
                repairs += checkedFile.Repairs;
            }

            settled.Add(checkedFile.File);

            if (checkedFile.File.Check is FileCheckState.Compiled or FileCheckState.Inconclusive)
            {
                compiling.Add(checkedFile.File);
            }
            else
            {
                ctx.Feed.Info(
                    $"{Title}: {checkedFile.File.RelativePath} is not being compiled into the rest",
                    "It does not compile, so anything after it is checked without it. A later file that "
                    + "genuinely needed it will say so, rather than inheriting errors from this one.");
            }
        }

        var failed = settled.Count(f => f.Check == FileCheckState.DidNotCompile);
        var unjudged = settled.Count(f => f.Check == FileCheckState.Inconclusive);

        Outcome = failed > 0
            ? CompileOutcome.Failed
            : unjudged > 0
                ? CompileOutcome.Inconclusive
                : repairs > 0
                    ? CompileOutcome.Repaired
                    : CompileOutcome.Compiled;

        var compiled = settled.Count - failed - unjudged;

        StatusMessage = failed > 0
            ? $"{compiled} of {settled.Count} file(s) compiled, {failed} left for later"
            : repairs > 0
                ? $"{settled.Count} file(s) compiled after {repairs} repair attempt(s)"
                : $"{settled.Count} file(s) compiled";

        return NodeResult.FromPin(Checked, settled);
    }

    /// <summary>One file of a plan, checked against everything settled before it.</summary>
    private readonly record struct CheckedFile(GeneratedFile File, int Repairs);

    private async Task<CheckedFile> CheckOneAsync(
        NodeExecutionContext ctx,
        IReadOnlyList<GeneratedFile> settled,
        GeneratedFile file,
        int total,
        CancellationToken ct)
    {
        var label = $"{file.Task.Order} of {total}: {file.RelativePath}";
        var projectPath = ctx.Services.Project.ProjectPath;

        CompileResult result;
        try
        {
            result = await ctx.Services.Compiler
                .CompileAsync(BuildSet(settled, file, file.Content), projectPath, ct)
                .ConfigureAwait(false);
        }
        catch (CompilerUnavailableException ex)
        {
            Outcome = CompileOutcome.Unavailable;
            ReferenceSummary = ex.Message;
            ctx.Feed.Info($"{Title}: {label} was not checked", ex.Message);

            return new CheckedFile(file, 0);
        }

        ReferenceSummary = result.ReferenceSummary;
        ReportAttempt(ctx, attempt: 0, label, result);

        if (result.Succeeded)
        {
            return new CheckedFile(file with { Check = FileCheckState.Compiled, Repairs = 0 }, 0);
        }

        if (result.IsInconclusive)
        {
            ReportInconclusive(ctx, label, result);

            return new CheckedFile(
                file with
                {
                    Check = FileCheckState.Inconclusive,
                    CheckDetail = result.FormatDiagnostics(DiagnosticsShown),
                    Repairs = 0
                },
                0);
        }

        var current = file.Content;
        var attempts = 0;

        var upstream = ctx.GetSourceNode(Code);
        var repairSource = upstream as ICodeRepairSource;
        var upstreamContext = upstream is null ? null : ctx.ForNode(upstream);

        var whyNot = repairSource is null
            ? $"The code arrived from {upstream?.Title ?? "nothing"}, which cannot be asked for another attempt."
            : string.Empty;

        var canRepair = repairSource is not null
                        && upstreamContext is not null
                        && repairSource.CanRepair(upstreamContext, out whyNot);

        if (!canRepair)
        {
            ctx.Feed.Info($"{Title}: {label} cannot be repaired", whyNot);
        }
        else if (repairSource is not null && upstreamContext is not null && upstream is not null)
        {
            for (var attempt = 1; attempt <= RetryLimit; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                var request = new CodeRepairRequest(
                    attempt,
                    RetryLimit,
                    file.Task.FileName,
                    current,
                    result.Diagnostics
                        .Where(d => d.File.EndsWith(file.Task.FileName, StringComparison.OrdinalIgnoreCase) || d.Line == 0)
                        .OrderByDescending(d => d.Severity)
                        .ThenBy(d => d.Line)
                        .Take(DiagnosticsSentToCoder)
                        .ToList());

                ctx.Feed.Add(
                    ActivityKind.NodeStarted,
                    $"{Title}: {label}, repair attempt {attempt} of {RetryLimit}",
                    $"Asking {upstream!.Title} to fix {request.ErrorCount} error(s)",
                    Id);

                StatusMessage = $"{label}: repair attempt {attempt} of {RetryLimit}";

                var revised = await repairSource.ReviseAsync(request, upstreamContext, ct).ConfigureAwait(false);
                attempts = attempt;

                if (string.IsNullOrWhiteSpace(revised))
                {
                    ctx.Feed.Info($"{Title}: repair attempt {attempt} produced nothing", "The previous content stands.");
                    continue;
                }

                current = Services.Editing.CodeEditApplier.Apply(revised, file.Content);

                result = await ctx.Services.Compiler
                    .CompileAsync(BuildSet(settled, file, current), projectPath, ct)
                    .ConfigureAwait(false);

                ReportAttempt(ctx, attempt, label, result);

                if (result.Succeeded)
                {
                    return new CheckedFile(
                        file with { Content = current, Check = FileCheckState.Compiled, Repairs = attempts },
                        attempts);
                }
            }
        }

        if (result.IsInconclusive)
        {
            ReportInconclusive(ctx, label, result);

            return new CheckedFile(
                file with
                {
                    Content = current,
                    Check = FileCheckState.Inconclusive,
                    CheckDetail = result.FormatDiagnostics(DiagnosticsShown),
                    Repairs = attempts
                },
                attempts);
        }

        Outcome = CompileOutcome.Failed;

        var listing = result.FormatDiagnostics(DiagnosticsShown);
        StatusMessage = $"{result.TrustedErrors.Count} error(s) remain in {file.RelativePath}";

        if (FailureBehaviour == CompileFailureBehaviour.FaultTheRun)
        {
            throw new InvalidOperationException(
                $"{Title}: {file.RelativePath} does not compile and "
                + $"{(attempts == 0 ? "no repair was attempted" : $"{attempts} repair attempt(s) did not fix it")}. "
                + $"Nothing has been written.{Environment.NewLine}{listing}");
        }

        // The rest of the plan still runs. Stopping here would throw away every file that would
        // have worked and every step that had not run yet, to no one's benefit.
        ctx.Feed.Info(
            $"{Title}: {file.RelativePath} is left for later",
            $"{result.TrustedErrors.Count} error(s) remain after "
            + $"{(attempts == 0 ? "no repair attempt" : $"{attempts} repair attempt(s)")}. "
            + "The rest of the plan carries on and this file is staged rather than written.");

        return new CheckedFile(
            file with
            {
                Content = current,
                Check = FileCheckState.DidNotCompile,
                CheckDetail = listing,
                Repairs = attempts
            },
            attempts);
    }

    /// <summary>
    /// The compilation unit for one file of a plan: everything settled before it, plus itself.
    /// </summary>
    private static IReadOnlyList<CompileSource> BuildSet(
        IReadOnlyList<GeneratedFile> earlier,
        GeneratedFile file,
        string content)
    {
        // One source per path, and the file being checked wins. A plan is perfectly capable of
        // naming the same file twice, and this application has seen one that planned to create
        // Health.cs and then to edit it. Compiled together those are the same type declared in two
        // places, which is a wall of CS0101 describing a problem the code does not have.
        var byPath = new Dictionary<string, CompileSource>(StringComparer.OrdinalIgnoreCase);

        foreach (var written in earlier)
        {
            byPath[Key(written.RelativePath)] = new CompileSource(written.Task.FileName, written.Content);
        }

        byPath[Key(file.RelativePath)] = new CompileSource(file.Task.FileName, content);

        return byPath.Values.ToList();
    }

    /// <summary>Two spellings of the same path are the same file.</summary>
    private static string Key(string relativePath) => relativePath.Replace('\\', '/').Trim();

    /// <inheritdoc />
    public override JsonObject SaveSettings() => new()
    {
        ["retryLimit"] = RetryLimit,
        ["failureBehaviour"] = FailureBehaviour.ToString()
    };

    /// <inheritdoc />
    public override void LoadSettings(JsonObject settings)
    {
        RetryLimit = Math.Clamp(settings["retryLimit"]?.GetValue<int>() ?? DefaultRetryLimit, 0, MaximumRetryLimit);

        FailureBehaviour = Enum.TryParse<CompileFailureBehaviour>(
            settings["failureBehaviour"]?.GetValue<string>(),
            out var behaviour)
            ? behaviour
            : CompileFailureBehaviour.StageForLater;
    }

    partial void OnRetryLimitChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, MaximumRetryLimit);

        if (clamped != value)
        {
            RetryLimit = clamped;
        }
    }

    /// <summary>The outcome of a repair loop: the best code it reached, and what the compiler said about it.</summary>
    private readonly record struct RepairOutcome(string Code, CompileResult Result, int Attempts);

    /// <summary>
    /// Asks whoever produced the code to fix it, once per allowed attempt, stopping as soon as it
    /// compiles.
    /// </summary>
    private async Task<RepairOutcome> TryRepairAsync(
        NodeExecutionContext ctx,
        string source,
        string fileName,
        CompileResult firstFailure,
        CancellationToken ct)
    {
        var current = source;
        var result = firstFailure;

        if (RetryLimit == 0)
        {
            return new RepairOutcome(current, result, 0);
        }

        var upstream = ctx.GetSourceNode(Code);

        if (upstream is not ICodeRepairSource repairSource)
        {
            var what = upstream is null ? "nothing" : upstream.Title;

            ctx.Feed.Info(
                $"{Title}: nothing upstream can repair this",
                $"The code arrived from {what}, which cannot be asked for another attempt. Wire a model node into this node to enable repair.");

            return new RepairOutcome(current, result, 0);
        }

        var upstreamContext = ctx.ForNode(upstream);

        if (!repairSource.CanRepair(upstreamContext, out var reason))
        {
            ctx.Feed.Info($"{Title}: {upstream.Title} cannot repair this", reason);
            return new RepairOutcome(current, result, 0);
        }

        for (var attempt = 1; attempt <= RetryLimit; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var request = new CodeRepairRequest(
                attempt,
                RetryLimit,
                fileName,
                current,
                result.Diagnostics.OrderByDescending(d => d.Severity).ThenBy(d => d.Line).Take(DiagnosticsSentToCoder).ToList());

            ctx.Feed.Add(
                ActivityKind.NodeStarted,
                $"{Title}: repair attempt {attempt} of {RetryLimit}",
                $"Asking {upstream.Title} to fix {request.ErrorCount} error(s) in {fileName}",
                Id);

            StatusMessage = $"Repair attempt {attempt} of {RetryLimit}";

            var revised = await repairSource.ReviseAsync(request, upstreamContext, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(revised))
            {
                ctx.Feed.Info(
                    $"{Title}: repair attempt {attempt} produced nothing",
                    $"{upstream.Title} returned an empty reply, so the previous code stands.");

                continue;
            }

            current = revised;

            try
            {
                result = await ctx.Services.Compiler
                    .CompileAsync(current, fileName, ctx.Services.Project.ProjectPath, ct)
                    .ConfigureAwait(false);
            }
            catch (CompilerUnavailableException)
            {
                // The project or the editor went away mid loop. The outer caller reports it.
                throw;
            }

            ReportAttempt(ctx, attempt, fileName, result);

            if (result.Succeeded)
            {
                return new RepairOutcome(current, result, attempt);
            }
        }

        return new RepairOutcome(current, result, RetryLimit);
    }

    /// <summary>Writes one attempt's outcome to the feed, so a loop is never silent.</summary>
    private void ReportAttempt(NodeExecutionContext ctx, int attempt, string fileName, CompileResult result)
    {
        var label = attempt == 0
            ? $"{Title}: {fileName}"
            : $"{Title}: {fileName} after repair attempt {attempt}";

        if (result.Succeeded)
        {
            ctx.Feed.Add(
                ActivityKind.NodeCompleted,
                $"{label} compiles",
                $"{result.Summary}. {result.ReferenceSummary}",
                Id);

            LastDiagnostics = string.Empty;
            LastProblems = Array.Empty<CompileDiagnostic>();
            return;
        }

        var listing = result.FormatDiagnostics(DiagnosticsShown);
        LastDiagnostics = listing;
        LastProblems = result.Diagnostics;

        ctx.Feed.Add(
            ActivityKind.NodeFaulted,
            $"{label} does not compile",
            listing,
            Id);
    }

    /// <summary>
    /// Reports a check whose every complaint could be a reference it did not have.
    /// </summary>
    /// <remarks>
    /// Deliberately not a failure and deliberately not a pass. Spending the repair limit here
    /// would ask a model to fix a name that is not wrong, and reporting it as a failure would tell
    /// somebody their code is broken when the truth is that their project has not been compiled.
    /// </remarks>
    private NodeResult Inconclusive(NodeExecutionContext ctx, string source, string fileName, CompileResult result)
    {
        ReportInconclusive(ctx, fileName, result);
        return NodeResult.FromPin(Checked, source);
    }

    private void ReportInconclusive(NodeExecutionContext ctx, string label, CompileResult result)
    {
        Outcome = CompileOutcome.Inconclusive;
        StatusMessage = $"{label}: could not tell, references incomplete";

        ctx.Feed.Info(
            $"{Title}: {label} could not be judged",
            $"{result.Errors.Count} error(s), and every one of them names something this check had no reference for. "
            + $"{result.ReferenceSummary} Nothing was repaired, because there is no reason to believe the code is wrong."
            + $"{Environment.NewLine}{result.FormatDiagnostics(DiagnosticsShown)}");
    }

    /// <summary>
    /// Reports a check that could not be run. Not a compile failure, and deliberately not treated
    /// as one: the code passes through untouched and the run continues.
    /// </summary>
    private NodeResult Unavailable(NodeExecutionContext ctx, string source, CompilerUnavailableException ex)
    {
        Outcome = CompileOutcome.Unavailable;
        ReferenceSummary = ex.Message;
        StatusMessage = "Could not be checked";

        ctx.Feed.Info($"{Title}: nothing was checked", ex.Message);

        return NodeResult.FromPin(Checked, source);
    }

    /// <summary>Ends a check that ran and failed, in whichever way the node is configured to.</summary>
    private NodeResult Fail(NodeExecutionContext ctx, string fileName, RepairOutcome repaired, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (repaired.Result.IsInconclusive)
        {
            ReportInconclusive(ctx, fileName, repaired.Result);
            return NodeResult.FromPin(Checked, repaired.Code);
        }

        Outcome = CompileOutcome.Failed;

        var errors = repaired.Result.TrustedErrors.Count;
        var attempted = repaired.Attempts == 0
            ? "no repair was attempted"
            : $"{repaired.Attempts} repair attempt(s) did not fix it";

        var listing = repaired.Result.FormatDiagnostics(DiagnosticsShown);
        StatusMessage = $"{errors} error(s) remain in {fileName}";

        if (FailureBehaviour == CompileFailureBehaviour.FaultTheRun)
        {
            throw new InvalidOperationException(
                $"{Title}: {fileName} does not compile and {attempted}. "
                + $"{errors} error(s) remain:{Environment.NewLine}{listing}");
        }

        ctx.Feed.Info(
            $"{Title}: continuing with code that does not compile",
            $"{errors} error(s) remain in {fileName} and {attempted}. This node is set to continue anyway.");

        return NodeResult.FromPin(Checked, repaired.Code);
    }
}
