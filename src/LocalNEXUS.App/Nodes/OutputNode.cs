using System.IO;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.Planning;
using LocalNEXUS.App.Services.ProjectIndex;

namespace LocalNEXUS.App.Nodes;

/// <summary>
/// Writes the value arriving on its input pin to a file inside the opened project.
/// </summary>
/// <remarks>
/// The path is always resolved through <see cref="Services.Files.ProjectService"/>, which
/// refuses anything that would land outside the project folder.
///
/// When a whole plan arrives rather than one file, each file is written on its own as soon as it
/// is ready. A file that will not compile, or that the project rules refuse, is kept with its
/// reason instead of being written, and the rest of the plan carries on. Holding four finished
/// files hostage to a fifth protects nobody and throws away work that was correct.
///
/// Before anything is written, each file is put through the Unity rules, and only when the open
/// project is a Unity project. Those are refusals rather than warnings, because every one of them
/// describes a change that compiles cleanly and silently breaks a scene, and a warning in a feed
/// nobody rereads is not a defence against a prefab that lost its script. In any other project the
/// same edits are an ordinary rename or refactor, so none of them run.
///
/// One rule is not Unity's and runs everywhere: nothing may be declared twice.
/// </remarks>
public sealed partial class OutputNode : NodeBase
{
    /// <summary>
    /// What a node starts from when nothing has said otherwise.
    /// </summary>
    /// <remarks>
    /// Still Unity's own folder, because a node built with no project in sight has to start
    /// somewhere and this is where the application began. What changed in v1.45 is that a project
    /// which has been set up seeds its own answer through the factory, so a plain C# project stops
    /// having an Assets/Scripts folder created in it that has no business being there.
    ///
    /// The value lives on the node and is saved with the graph, which is why changing the project's
    /// answer does not move a graph somebody already saved. It changes what the next node added
    /// starts from, and the settings panel says so.
    /// </remarks>
    public const string DefaultSubfolder = "Assets/Scripts";

    /// <summary>Folder relative to the project root that the file is written into.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RelativePathPreview))]
    private string _targetSubfolder = DefaultSubfolder;

    /// <summary>Name of the file to write, including its extension.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RelativePathPreview))]
    private string _fileName = "GeneratedScript.cs";

    /// <summary>When true, the run pauses and asks in the feed before the file is written.</summary>
    [ObservableProperty]
    private bool _askBeforeWriting;

    public OutputNode()
        : base("Output")
    {
        Content = AddInput("Code", PinType.Code);
    }

    /// <summary>Receives the content to write.</summary>
    public Pin Content { get; }

    /// <inheritdoc />
    public override string TypeKey => "Output";

    /// <summary>The destination as it will appear inside the project, for display.</summary>
    public string RelativePathPreview
        => string.IsNullOrWhiteSpace(FileName)
            ? "no file name set"
            : $"{TargetSubfolder?.Replace('\\', '/').Trim('/')}/{FileName}";

    /// <inheritdoc />
    public override async Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
    {
        var arrived = ctx.GetValue(Content);

        // A plan is written as a set rather than as items, because each file carries its own
        // destination and the batch either lands or is restored.
        if (arrived is IReadOnlyList<GeneratedFile> files)
        {
            return await WritePlanAsync(ctx, files, ct).ConfigureAwait(false);
        }

        // Any other list is worked through one entry at a time. This used to stringify whatever it
        // did not recognise, so five things arriving as a list were concatenated into one file.
        //
        // Every entry lands on the same file name, because a plain value carries no destination
        // and this node has one configured. That is said out loud rather than left to be
        // discovered, because a fan out into one file name is almost never what somebody meant.
        if (FanOut.TryItems(arrived, out var items))
        {
            if (items.Count > 1)
            {
                ctx.Feed.Info(
                    $"{Title}: {items.Count} item(s) to write",
                    $"They carry no file names of their own, so each is written to {RelativePathPreview} in turn "
                    + "and the last one is what remains.");
            }

            return await FanOut.OverAsync(
                this,
                Content,
                items,
                ctx,
                ct,
                (itemContext, index, token) =>
                {
                    StatusMessage = $"{index + 1} of {items.Count}";
                    return WriteOnceAsync(itemContext, token);
                }).ConfigureAwait(false);
        }

        return await WriteOnceAsync(ctx, ct).ConfigureAwait(false);
    }

    /// <summary>Writes whatever is on the content pin to this node's configured file.</summary>
    private async Task<NodeResult> WriteOnceAsync(NodeExecutionContext ctx, CancellationToken ct)
    {
        var content = ctx.GetText(Content);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException(
                $"{Title} received nothing to write. Connect a node to its Code pin.");
        }

        var project = ctx.Services.Project;
        var absolutePath = project.ResolveTargetPath(TargetSubfolder ?? string.Empty, FileName);
        var displayPath = project.ToDisplayPath(absolutePath);

        if (AskBeforeWriting)
        {
            var approved = await ctx.Feed
                .RequestConfirmationAsync(
                    $"{Title}: write {displayPath}?",
                    $"{content.Length} characters will be written to{Environment.NewLine}{absolutePath}",
                    ct)
                .ConfigureAwait(false);

            if (!approved)
            {
                throw new OperationCanceledException($"Writing {displayPath} was declined.");
            }
        }

        // Read before writing so the feed can say how much changed rather than only how big the
        // result is. One extra read on a path that is about to write the same file anyway.
        var original = File.Exists(absolutePath)
            ? await File.ReadAllTextAsync(absolutePath, ct).ConfigureAwait(false)
            : null;

        SnapshotBefore(ctx, absolutePath);

        var bytes = await ctx.Services.FileWriter.WriteAsync(absolutePath, content, ct).ConfigureAwait(false);
        var change = DiffStat.Between(original, content);

        Record(ctx, displayPath, Services.History.FileOutcome.Written, change.HasChange ? change.Text : null);

        StatusMessage = $"{displayPath}  ({bytes} bytes)";
        ctx.Feed.Add(ActivityKind.FileWritten, $"Wrote {displayPath}", absolutePath, Id).Detail =
            change.HasChange ? change.Text : $"{bytes} bytes";

        return NodeResult.Empty;
    }

    /// <summary>
    /// Writes every file of a plan that is ready, and stages the ones that are not.
    /// </summary>
    /// <remarks>
    /// This used to write the whole plan or none of it, and that was the right instinct applied to
    /// the wrong unit. Holding four good files hostage to a fifth that will not compile does not
    /// protect anybody; it throws away work that was finished and correct, and with a local model
    /// one file of five failing is ordinary rather than exceptional.
    ///
    /// So the unit is the file. Each one is written on its own, in place, and either it lands or it
    /// is kept with its reason. Nothing about the guardrails is relaxed: a file is checked against
    /// them before it is written, exactly as before, and a refusal is a refusal. What changes is
    /// that a refusal stops that file rather than the run.
    ///
    /// A staged file is not a failure and is not written as one. It is work that has not finished,
    /// and it sits with what it was for and what stopped it so that somebody can say what to do
    /// about it from the chat box instead of starting the request again.
    /// </remarks>
    private async Task<NodeResult> WritePlanAsync(
        NodeExecutionContext ctx,
        IReadOnlyList<GeneratedFile> files,
        CancellationToken ct)
    {
        if (files.Count == 0)
        {
            throw new InvalidOperationException($"{Title} received an empty plan, so there was nothing to write.");
        }

        var project = ctx.Services.Project;
        var index = ctx.Services.ProjectIndex;
        var staging = ctx.Services.Staging;

        if (AskBeforeWriting)
        {
            var listing = string.Join(Environment.NewLine, files.Select(f =>
                $"{(f.Operation == FileOperation.Create ? "create" : "edit")} {f.RelativePath}"));

            var approved = await ctx.Feed
                .RequestConfirmationAsync($"{Title}: write {files.Count} file(s)?", listing, ct)
                .ConfigureAwait(false);

            if (!approved)
            {
                throw new OperationCanceledException($"Writing {files.Count} file(s) was declined.");
            }
        }

        var written = 0;
        var staged = 0;
        var bytes = 0L;

        // What the files of this plan have declared between them so far, by the name a type is
        // referred to by. The duplicate guard runs while the plan is being made and can only see
        // what the plan says it will write, which is a path and one type name per row. It cannot
        // see what a file turns out to declare.
        //
        // That gap let one through. A plan created ItemStack.cs and Inventory.cs, and the coder
        // wrote Inventory with an ItemStack nested inside it, so the project ended with two types
        // called ItemStack and nothing said a word. Both compile, which is exactly why nothing
        // downstream noticed: it is not an error, it is a project with two of something.
        var declaredHere = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            // A file the check could not get to compile is kept rather than written. Inconclusive
            // is not that: nothing was established about it, so it is treated as it would have been
            // before there was a check at all.
            if (file.Check == FileCheckState.DidNotCompile)
            {
                staging.Stage(Stage(file, StagedReason.DidNotCompile, file.CheckDetail));
                Record(ctx, file.RelativePath, Services.History.FileOutcome.Staged, file.CheckDetail);
                staged++;

                ctx.Feed.Info(
                    $"{file.RelativePath} is waiting",
                    $"It does not compile yet, so it was kept rather than written.{Environment.NewLine}{file.CheckDetail}");

                continue;
            }

            var folder = Path.GetDirectoryName(file.RelativePath)?.Replace('\\', '/') ?? string.Empty;
            var absolute = project.ResolveTargetPath(folder, Path.GetFileName(file.RelativePath));

            // One batch per file, so a write that fails part way puts that file back and leaves
            // every file already written alone. The guardrails run inside it, before anything
            // touches disk.
            var batch = new ProjectWriteBatch(ctx.Services.FileWriter);

            try
            {
                batch.EnforceExpectedExistence(absolute, file.Operation == FileOperation.Edit);

                // The Unity rules only where Unity is. Every one of them exists because an edit
                // that compiles cleanly unbinds a scene, and none of that is true of an ordinary
                // C# project, where the same edits are a rename and a refactor. A refusal that
                // makes no sense in the project somebody opened is worse than no refusal, and one
                // of these would have demanded a Unity attribute from a project with no Unity in
                // it: any public field counts as serialised, so renaming one would have been
                // refused until it carried [FormerlySerializedAs].
                if (project.IsUnity)
                {
                    UnityScriptRules.Enforce(file.RelativePath, file.Content, index.FindFile(file.RelativePath), file.Types);
                }

                // This one runs everywhere. Two types with one name is a problem in any codebase,
                // and it is the thing this application was built to prevent.
                EnforceNothingDeclaredTwice(file, declaredHere, index);
            }
            catch (UnityScriptRuleException ex)
            {
                staging.Stage(Stage(file, StagedReason.RefusedByProjectRules, ex.Message));
                Record(ctx, file.RelativePath, Services.History.FileOutcome.Refused, ex.Message);

                // Which rule, not merely that something refused. Seven quite different mistakes
                // arrive here and each has a different fix, and until the rule travelled with the
                // exception the only way to tell them apart was to read the sentence.
                ctx.Record(new RunDecision(
                    RunDecisionKind.WriteRefused,
                    ex.Rule.ToString(),
                    file.RelativePath,
                    file.Task.TypeName,
                    null,
                    ex.Message));

                staged++;

                // Deliberately not an error entry. This file compiles; it was refused, which is a
                // different thing and a different fix, and drawing it as a failure would send
                // somebody looking at the code rather than at the rename they meant to make.
                ctx.Feed.Info($"{file.RelativePath} was refused", ex.Message);
                continue;
            }

            // What is there now, kept before it is replaced. Only files about to change are
            // copied, which is what makes undo cheap enough to do on every run rather than on
            // request.
            SnapshotBefore(ctx, absolute);

            batch.Stage(absolute, file.Content);

            IReadOnlyList<WrittenFile> result;
            try
            {
                result = await batch.CommitAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                staging.Stage(Stage(file, StagedReason.WriteFailed, ex.Message));
                Record(ctx, file.RelativePath, Services.History.FileOutcome.Staged, ex.Message);
                staged++;

                ctx.Feed.Error($"{file.RelativePath} could not be written", ex.Message);
                continue;
            }

            // It landed, so anything this project was still holding about it is now answered.
            staging.Resolve(file.RelativePath);

            written++;
            bytes += result.Sum(w => w.Bytes);

            Record(
                ctx,
                file.RelativePath,
                Services.History.FileOutcome.Written,
                result.Count > 0 && result[0].Change.HasChange ? result[0].Change.Text : null);

            var entry = ctx.Feed.Add(
                ActivityKind.FileWritten,
                $"{(file.Operation == FileOperation.Create ? "Wrote" : "Edited")} {file.RelativePath}",
                null,
                Id);

            // How much changed, so that a three line fix and a rewrite do not read the same.
            if (result.Count > 0 && result[0].Change.HasChange)
            {
                entry.Detail = result[0].Change.Text;
            }

            if (project.IsUnity && UnityScriptRules.DescribeAttachmentNeeded(file.Types) is { } note)
            {
                ctx.Feed.Info($"{file.RelativePath} needs attaching", note);
            }
        }

        StatusMessage = staged == 0
            ? $"{written} file(s), {bytes} bytes"
            : $"{written} file(s) written, {staged} waiting";

        if (staged > 0)
        {
            ctx.Feed.Info(
                $"{staged} file(s) waiting to be resolved",
                $"{written} file(s) are on disk. Say what to do about the rest in the box below; "
                + "they are kept with the project, so closing the application does not lose them.");
        }

        return NodeResult.Empty;
    }

    /// <summary>
    /// Keeps what a file holds before this run changes it, so the run can be undone.
    /// </summary>
    /// <remarks>
    /// Nothing happens when no run identity was handed down, which is the case when the history
    /// could not be opened. A write is not held up for the want of a record of it.
    /// </remarks>
    private static void SnapshotBefore(NodeExecutionContext ctx, string absolutePath)
    {
        if (ctx.RunId is { } runId)
        {
            ctx.Services.History.Snapshot(runId, absolutePath);
        }
    }

    /// <summary>Files what became of one file under the run that dealt with it.</summary>
    private static void Record(
        NodeExecutionContext ctx,
        string relativePath,
        Services.History.FileOutcome outcome,
        string? detail)
    {
        if (ctx.RunId is { } runId)
        {
            ctx.Services.History.RecordFile(runId, relativePath, outcome, detail);
        }
    }

    /// <summary>Turns a file the run could not finish into the record that outlives the run.</summary>
    /// <summary>
    /// Refuses a file declaring a type that another file already declares.
    /// </summary>
    /// <remarks>
    /// The content level half of the rule the duplicate guard enforces on the plan. The guard is
    /// the authority on what the project has and runs before anything is written, which is the
    /// right place for it and cannot cover this: what a file declares is decided by the coder,
    /// after the plan has been approved, and a plan row promises one type name per path.
    ///
    /// Two places to look, and the second was missing for a version. A file of the same plan,
    /// which catches a plan that writes the same name twice; and the project itself, which catches
    /// a generated file quietly redeclaring something that was already there. The second is the
    /// one that actually happened: a generated Inventory.cs declared a second InventorySlot beside
    /// the one already sitting in its own file, nothing in the plan mentioned it, and comparing
    /// only against the plan could never have seen it.
    ///
    /// Nesting is why it stays invisible without this. A type nested inside another is a different
    /// type to a compiler and the same name to a person, so it compiles and every check after the
    /// coder is about whether something compiles. Names are compared rather than full names for
    /// that reason.
    ///
    /// A partial type is exempt, because being spread across files is what partial means. Editing
    /// the file a type already lives in is exempt for the more obvious reason: that is an edit.
    /// </remarks>
    /// <exception cref="UnityScriptRuleException">Another file already declares the same name.</exception>
    private static void EnforceNothingDeclaredTwice(
        GeneratedFile file,
        Dictionary<string, string> declaredHere,
        ProjectIndexService index)
    {
        foreach (var type in file.Types)
        {
            if (declaredHere.TryGetValue(type.Name, out var owner)
                && !string.Equals(owner, file.RelativePath, StringComparison.OrdinalIgnoreCase))
            {
                throw Collision(file, type.Name, owner, "in this same plan");
            }

            var existing = index.FindType(type.Name);

            if (existing.Count == 0 || existing.All(t => t.IsPartial) || type.IsPartial)
            {
                continue;
            }

            var elsewhere = existing
                .Select(index.FileOf)
                .Where(f => f is not null)
                .Select(f => f!.RelativePath)
                .FirstOrDefault(path => !string.Equals(path, file.RelativePath, StringComparison.OrdinalIgnoreCase));

            if (elsewhere is not null)
            {
                throw Collision(file, type.Name, elsewhere, "in this project");
            }
        }

        foreach (var type in file.Types)
        {
            declaredHere[type.Name] = file.RelativePath;
        }
    }

    /// <summary>The refusal, worded the same wherever the other one was found.</summary>
    private static UnityScriptRuleException Collision(GeneratedFile file, string name, string owner, string where)
        => new(
            ProjectWriteRule.NothingDeclaredTwice,
            $"{file.RelativePath} declares {name}, and {owner} {where} already declares a type of that name. "
            + "Two types with one name is the thing this application exists to prevent, and it compiles, so "
            + "nothing further along would have noticed. Fold the work into the one that exists, or give one "
            + "of them a different name.");

    private static StagedFile Stage(GeneratedFile file, StagedReason reason, string detail)
        => new(
            file.RelativePath,
            file.Task.TypeName,
            file.Operation == FileOperation.Create,
            file.Task.Intent,
            file.Content,
            reason,
            detail,
            DateTimeOffset.Now);

    /// <inheritdoc />
    public override JsonObject SaveSettings() => new()
    {
        ["targetSubfolder"] = TargetSubfolder,
        ["fileName"] = FileName,
        ["askBeforeWriting"] = AskBeforeWriting
    };

    /// <inheritdoc />
    public override void LoadSettings(JsonObject settings)
    {
        TargetSubfolder = settings["targetSubfolder"]?.GetValue<string>() ?? DefaultSubfolder;
        FileName = settings["fileName"]?.GetValue<string>() ?? "GeneratedScript.cs";
        AskBeforeWriting = settings["askBeforeWriting"]?.GetValue<bool>() ?? false;
    }
}
