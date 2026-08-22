using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Credentials;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.Distributed;
using LocalNEXUS.App.Services.Editing;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Extensions;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.Services.Planning;
using LocalNEXUS.App.Services.ProjectIndex;

namespace LocalNEXUS.App.Nodes;

/// <summary>
/// Sends its input to a language model and emits the reply.
/// </summary>
/// <remarks>
/// One node type covers every role in a pipeline. A planning node and a coding node differ only
/// in their system prompt and their chosen model, so there is no reason for them to be separate
/// classes. Every provider shares a single request path over the OpenAI compatible API; where
/// inference physically happens, one machine or several, is decided during resolution and the
/// graph does not care.
///
/// It is also a repair source and an answering model: something downstream that finds a problem
/// with the code this node produced can hand the problem back and ask for another attempt, and
/// something upstream that needs a model can borrow this one under its own instructions. The node
/// knows nothing about what kind of problem or who is asking.
///
/// When what arrives on its input is a list of files to write rather than a single instruction,
/// it runs once per file and emits a list. That is the whole of fan out: a wire carries one item
/// or many identically, so a graph that writes five files is the same graph that writes one.
/// </remarks>
public sealed partial class ModelNode : NodeBase, ICodeRepairSource, IModelHandle
{
    /// <summary>Base URL used for every OpenRouter request.</summary>
    public const string OpenRouterBaseUrl = "https://openrouter.ai/api/v1";

    /// <summary>
    /// The starting system prompt for a project that is not Unity, and for no project at all.
    /// </summary>
    /// <remarks>
    /// Not empty, and it is worth saying why. A coding model given no system prompt answers the way
    /// it was trained to answer a person: prose around the code, an explanation of what it did, and
    /// the whole thing inside markdown fences. The end of the default pipeline writes what comes
    /// back into a file, so every one of those is a file that does not compile. This exists because
    /// it works.
    ///
    /// What it no longer does is name an engine. It used to say Unity, in every project, including
    /// ones with no Unity anywhere in them, which is at best noise in the one instruction the model
    /// reads before everything else.
    /// </remarks>
    public const string DefaultSystemPrompt =
        "You are an expert software engineer. Produce complete, compilable code that does what was "
        + "asked and nothing more. Output raw code only: no markdown code fences, no commentary, "
        + "no explanation.";

    /// <summary>
    /// The starting system prompt for a Unity project.
    /// </summary>
    /// <remarks>
    /// Word for word what every node used to start with. Unity is a real target with real
    /// conventions, and a model told it is writing for Unity writes a MonoBehaviour rather than a
    /// class with a Main method. Kept exactly as it was so that what a Unity project produces is
    /// unchanged by any of this.
    /// </remarks>
    public const string UnitySystemPrompt =
        "You are an expert Unity C# engineer. Produce complete, compilable C# for Unity. "
        + "Output raw code only: no markdown code fences, no commentary, no explanation.";

    /// <summary>
    /// What a newly added node starts with, for a project of this kind.
    /// </summary>
    /// <remarks>
    /// Seeded, never enforced. The prompt is a setting on the node, so this decides what a node
    /// dropped on the canvas today begins as and reaches back into nothing: a node in a saved graph
    /// keeps whatever it was given, because the value belongs to the node and travels with it.
    ///
    /// Nothing known means the neutral one. Assuming Unity because nobody has said otherwise is the
    /// thing being fixed.
    /// </remarks>
    public static string PromptFor(Services.Files.ProjectKind kind)
        => kind == Services.Files.ProjectKind.Unity ? UnitySystemPrompt : DefaultSystemPrompt;

    /// <summary>Where this node's requests go.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocal))]
    [NotifyPropertyChangedFor(nameof(IsNetwork))]
    [NotifyPropertyChangedFor(nameof(IsSelfHosted))]
    [NotifyPropertyChangedFor(nameof(IsOpenRouter))]
    [NotifyPropertyChangedFor(nameof(IsCloud))]
    [NotifyPropertyChangedFor(nameof(NeedsKey))]
    [NotifyPropertyChangedFor(nameof(ProviderStatus))]
    [NotifyPropertyChangedFor(nameof(ModelDisplayName))]
    private ModelProvider _provider = ModelProvider.Local;

    /// <summary>The model selected from the catalog, when the provider is local.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelDisplayName))]
    [NotifyPropertyChangedFor(nameof(ModelSourceText))]
    [NotifyPropertyChangedFor(nameof(EffectiveLocalModelPath))]
    private LocalModelInfo? _selectedLocalModel;

    /// <summary>
    /// A model chosen by browsing, which this node runs instead of its catalogue selection. Null
    /// when the node uses the dropdown. A GGUF file or a safetensors folder, indifferently.
    /// </summary>
    /// <remarks>
    /// Per node on purpose. The alternative on offer, adding the folder to the catalogue, is a
    /// global and persistent change for the sake of one node, which is the wrong size of action
    /// for a model that simply lives on another drive.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelDisplayName))]
    [NotifyPropertyChangedFor(nameof(ModelSource))]
    [NotifyPropertyChangedFor(nameof(HasModelFile))]
    [NotifyPropertyChangedFor(nameof(IsModelFileMissing))]
    [NotifyPropertyChangedFor(nameof(ModelSourceText))]
    [NotifyPropertyChangedFor(nameof(EffectiveLocalModelPath))]
    [NotifyCanExecuteChangedFor(nameof(ClearModelFileCommand))]
    private string? _modelFilePath;

    /// <summary>The network served model this node uses, when the provider is network.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelDisplayName))]
    private NetworkServedModel? _selectedNetworkModel;

    /// <summary>
    /// The persisted network model identity when it could not be resolved at load time, kept
    /// so saving the graph again does not silently drop the choice.
    /// </summary>
    private string? _unresolvedNetworkModelKey;

    /// <summary>The model slug sent to OpenRouter, for example <c>anthropic/claude-sonnet-4</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelDisplayName))]
    private string _openRouterModel = string.Empty;

    /// <summary>The model id sent to a self hosted server.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelDisplayName))]
    private string _selfHostedModelId = string.Empty;

    /// <summary>The system message sent with every request.</summary>
    [ObservableProperty]
    private string _systemPrompt = DefaultSystemPrompt;

    /// <summary>
    /// Whether a reply that is nothing but a markdown code fence is unwrapped before it leaves.
    /// </summary>
    /// <remarks>
    /// On by default, because a model asked for a file wraps it in a fence whatever the prompt
    /// says, and a fenced reply is not a valid C# file. It used to take a node wired into every
    /// graph to undo that, which is boilerplate for an artifact of how models format text.
    ///
    /// A setting rather than a law, because this is a general model call. One feeding a planner
    /// produces a plan, one feeding a debate produces an argument, and one writing documentation
    /// is supposed to keep its code blocks. Turning it off is the right answer for all three.
    /// </remarks>
    [ObservableProperty]
    private bool _stripCodeFences = true;

    /// <summary>Sampling temperature.</summary>
    [ObservableProperty]
    private double _temperature = 0.4d;

    /// <summary>Upper bound on generated tokens.</summary>
    [ObservableProperty]
    private int _maxTokens = 4096;

    /// <summary>Context window requested when this node starts a llama-server.</summary>
    [ObservableProperty]
    private int _contextSize = LlamaLaunchOptions.DefaultContextSize;

    /// <summary>GPU layers requested when this node starts a llama-server.</summary>
    [ObservableProperty]
    private int _gpuLayers = LlamaLaunchOptions.DefaultGpuLayers;

    /// <summary>
    /// The endpoint root. Filled in automatically when the provider changes. Leaving it blank
    /// for a local model means "use servers this application starts"; setting it points the
    /// node at a server that is already running somewhere else and nothing is spawned.
    /// </summary>
    [ObservableProperty]
    private string _baseUrl = string.Empty;

    /// <summary>
    /// Which hosted provider this node uses, by catalogue id.
    /// </summary>
    /// <remarks>
    /// An identifier, never a key. The key for this provider lives in the credential store and is
    /// looked up when a run needs it, so a graph says Anthropic rather than saying a secret and
    /// can be shared or committed without taking one with it.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CloudProvider))]
    [NotifyPropertyChangedFor(nameof(NeedsKey))]
    [NotifyPropertyChangedFor(nameof(ProviderStatus))]
    private string _cloudProviderId = string.Empty;

    /// <summary>The model id sent to that provider. Free text, because a provider serves many.</summary>
    [ObservableProperty]
    private string _cloudModelId = string.Empty;

    /// <summary>
    /// How this node is asked to express a change to an existing file. Per node because the right
    /// answer depends on the model behind it.
    /// </summary>
    [ObservableProperty]
    private EditFormat _editFormat = EditFormat.Automatic;

    /// <summary>
    /// How many tool calls this node will make in one execution before it stops.
    /// </summary>
    /// <remarks>
    /// A model that has misunderstood a tool will call it again with the same arguments, and
    /// again, and the only thing that ends that is a number. Modest on purpose: a run that hits
    /// this cap has gone wrong, and the useful behaviour is to stop and say so rather than to
    /// keep paying for it.
    /// </remarks>
    [ObservableProperty]
    private int _maxToolCalls = 8;

    private readonly IDialogService _dialogs;
    private readonly ExtensionToolset? _toolset;
    private readonly ICredentialStore? _credentials;

    /// <summary>Extensions whose tools this node may call. Empty means the node offers no tools.</summary>
    public ObservableCollection<string> SelectedExtensionIds { get; } = new();

    /// <summary>
    /// Tool names to offer from those extensions, or empty for all of them.
    /// </summary>
    /// <remarks>
    /// Defaulting to all of an extension's tools is deliberate. Filtering is worth having on a
    /// small context window and is a nuisance to maintain otherwise, so it is available and not
    /// required.
    /// </remarks>
    public ObservableCollection<string> AllowedToolNames { get; } = new();

    public ModelNode(
        ModelCatalog catalog,
        MeshManager mesh,
        IDialogService dialogs,
        ExtensionToolset? toolset = null,
        ICredentialStore? credentials = null)
        : base("Model")
    {
        Catalog = catalog;
        Mesh = mesh;
        _dialogs = dialogs;
        _toolset = toolset;
        _credentials = credentials;

        Prompt = AddInput("Text", PinType.Text);
        Completion = AddOutput("Code", PinType.Code);

        // Appended after the completion, never before it. A saved graph matches its pins by name
        // and falls back to position, so putting this first would hand it the completion's saved
        // identity and drop every wire leaving this node.
        Self = AddOutput("Model", PinType.Model);

        // A fresh node is usable straight away when the machine already has a model.
        SelectedLocalModel = catalog.Models.FirstOrDefault();
    }

    /// <summary>The GGUF files available for the local provider.</summary>
    public ModelCatalog Catalog { get; }

    /// <summary>This install's mesh node: what the network serves, and where to send it.</summary>
    public MeshManager Mesh { get; }

    /// <summary>Receives the text to send to the model.</summary>
    public Pin Prompt { get; }

    /// <summary>Carries the model reply onwards.</summary>
    public Pin Completion { get; }

    /// <summary>
    /// Hands this model to whatever needs one, rather than handing over a reply.
    /// </summary>
    /// <remarks>
    /// This node is the call, so it emits itself rather than consuming one of these. It costs a
    /// model node nothing to leave unwired, and a node used the ordinary way is unchanged.
    /// </remarks>
    public Pin Self { get; }

    /// <inheritdoc />
    public override string TypeKey => "Model";

    /// <summary>True when the local provider is selected. Drives which settings are shown.</summary>
    public bool IsLocal => Provider == ModelProvider.Local;

    /// <summary>True when the network provider is selected.</summary>
    public bool IsNetwork => Provider == ModelProvider.Network;

    /// <summary>True when the self hosted provider is selected.</summary>
    public bool IsSelfHosted => Provider == ModelProvider.SelfHosted;

    /// <summary>True when the OpenRouter provider is selected.</summary>
    public bool IsOpenRouter => Provider == ModelProvider.OpenRouter;

    /// <summary>True while this node uses a hosted provider chosen from the catalogue.</summary>
    public bool IsCloud => Provider == ModelProvider.Cloud;

    /// <summary>Everything the provider list offers, for the node's selector.</summary>
    public static IReadOnlyList<CloudProvider> AvailableProviders => ProviderCatalog.All;

    /// <summary>Where this node's local model comes from: the catalogue, or one of its own.</summary>
    public LocalModelSource ModelSource
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ModelFilePath))
            {
                return LocalModelSource.Catalog;
            }

            // A safetensors model is a folder and a GGUF is a file, so presence is asked about
            // the path rather than about a file, and the node stays free of formats either way.
            return File.Exists(ModelFilePath) || Directory.Exists(ModelFilePath)
                ? LocalModelSource.File
                : LocalModelSource.MissingFile;
        }
    }

    /// <summary>True while this node runs a file of its own rather than the catalogue selection.</summary>
    public bool HasModelFile => ModelSource is LocalModelSource.File or LocalModelSource.MissingFile;

    /// <summary>True when the chosen file is no longer on disk, which the panel says out loud.</summary>
    public bool IsModelFileMissing => ModelSource == LocalModelSource.MissingFile;

    /// <summary>The model this node will actually run, whichever way it was chosen.</summary>
    public string? EffectiveLocalModelPath => HasModelFile ? ModelFilePath : SelectedLocalModel?.Path;

    /// <summary>
    /// Which of the two selections is in effect, so the panel is never ambiguous.
    /// </summary>
    /// <remarks>
    /// A file stays in effect until it is cleared, whatever happens in the dropdown above. The
    /// alternative, letting a catalogue selection silently drop the file, cannot be made to work
    /// consistently: re-choosing the entry that is already selected raises no change at all, so
    /// the rule would apply on some selections and not others.
    /// </remarks>
    public string ModelSourceText => ModelSource switch
    {
        LocalModelSource.File => "This node runs the model below, not the catalogue selection above.",
        LocalModelSource.MissingFile => "This node points at a model that is no longer there.",
        _ => SelectedLocalModel is null
            ? "No model selected. Choose one above, or browse for one anywhere on disk."
            : "This node runs the catalogue selection above."
    };

    /// <summary>The model this node will use, for display on the canvas.</summary>
    public string ModelDisplayName => Provider switch
    {
        ModelProvider.Local => LocalModelName(EffectiveLocalModelPath) ?? "no model selected",
        ModelProvider.Network => SelectedNetworkModel?.DisplayLabel ?? "no network model",
        ModelProvider.SelfHosted => string.IsNullOrWhiteSpace(SelfHostedModelId) ? "no model id" : SelfHostedModelId,
        ModelProvider.OpenRouter => string.IsNullOrWhiteSpace(OpenRouterModel) ? "no model slug" : OpenRouterModel,
        _ => "unknown"
    };

    /// <summary>
    /// True when this node has been pointed at a model of some kind.
    /// </summary>
    /// <remarks>
    /// Read off the same display name the canvas shows rather than repeating the per provider
    /// tests, so a node that says it has no model selected and a node that reports itself
    /// unconfigured cannot come to disagree. It answers whether something was chosen, not whether
    /// that something will answer, which only running it can establish.
    /// </remarks>
    public bool IsConfigured => !ModelDisplayName.StartsWith("no ", StringComparison.Ordinal)
                               && !string.Equals(ModelDisplayName, "unknown", StringComparison.Ordinal);

    /// <inheritdoc />
    public override async Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
    {
        // A list of files to write is not a prompt, it is a plan, and this node runs once per
        // entry in it. Nothing about the graph says so; the value on the wire does.
        if (ctx.GetValue(Prompt) is IReadOnlyList<CodeTask> tasks)
        {
            return await WriteFilesAsync(ctx, tasks, ct).ConfigureAwait(false);
        }

        var userContent = ctx.GetText(Prompt);
        if (string.IsNullOrWhiteSpace(userContent))
        {
            throw new InvalidOperationException(
                $"{Title} received no input. Connect something to its Text pin.");
        }

        var entry = ctx.Feed.Add(ActivityKind.ModelStream, $"{Title}  ({ModelDisplayName})", null, Id);

        try
        {
            // Recovery from a source dropping mid request belongs to the engine now: the mesh
            // routes around peers it has retired, so a node that second guessed it here would
            // be racing the thing that actually knows the topology.
            var endpoint = await ResolveEndpointAsync(ctx, entry, ct).ConfigureAwait(false);
            return await StreamOnceAsync(ctx, entry, endpoint, userContent, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            entry.Flush();
            entry.Detail = "cancelled";
            throw;
        }
        catch (Exception)
        {
            entry.Flush();
            entry.Detail = "failed";
            throw;
        }
    }

    /// <summary>
    /// Writes every file of a plan, in order, showing each one what the earlier ones defined.
    /// </summary>
    /// <remarks>
    /// In order and not in parallel on purpose. The third file of a plan usually calls into the
    /// first, and a model that has not been shown what the first actually declared will guess at
    /// the name and be wrong. Running them concurrently would be faster and would produce a set
    /// of files that do not fit together.
    /// </remarks>
    private async Task<NodeResult> WriteFilesAsync(
        NodeExecutionContext ctx,
        IReadOnlyList<CodeTask> tasks,
        CancellationToken ct)
    {
        var produced = new List<GeneratedFile>();
        var signatures = new List<string>();

        ctx.Feed.Info($"{Title}: writing {tasks.Count} file(s)", string.Join(Environment.NewLine, tasks.Select(t => t.ToString())));

        foreach (var planned in tasks)
        {
            ct.ThrowIfCancellationRequested();

            // Read from disk, now, before anything is asked. Not from the index, not from the plan,
            // not from what an earlier step in this run said the file held. A model shown a stale
            // copy produces a change against a file that no longer exists in that shape, which is
            // the failure this keeps producing, and it cannot invent what it was just handed.
            var task = planned;

            if (task.Operation == FileOperation.Edit)
            {
                var reading = Services.Editing.SourceFileReader.Read(
                    ctx.Services.Project.ProjectPath, task.RelativePath, task.TypeName);

                if (!reading.IsUsable)
                {
                    StageUnreadableFile(ctx, task, reading.Message);
                    continue;
                }

                task = WithFreshContent(task, reading);
            }

            var wholeFile = CodeEditApplier.WantsWholeFile(
                EditFormat,
                task.Operation == FileOperation.Create,
                task.ExistingContent?.Length ?? 0);

            var entry = ctx.Feed.Add(
                ActivityKind.ModelStream,
                $"{Title}  ({task.Order} of {tasks.Count}: {task.RelativePath}, {(wholeFile ? "whole file" : "diff")})",
                null,
                Id);

            StatusMessage = $"{task.Order} of {tasks.Count}: {task.FileName}";

            string reply;
            try
            {
                var endpoint = await ResolveEndpointAsync(ctx, entry, ct).ConfigureAwait(false);
                var emitted = FitSignatures(signatures);
                var message = PlanPrompt.BuildCoderMessage(task, emitted, wholeFile);

                reply = await StreamTextAsync(ctx, entry, endpoint, message, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                entry.Flush();
                entry.Detail = "cancelled";
                throw;
            }
            catch (Exception)
            {
                entry.Flush();
                entry.Detail = "failed";
                throw;
            }

            string content;

            try
            {
                content = await ApplyWithRetriesAsync(ctx, task, reply, signatures, ct).ConfigureAwait(false);
            }
            catch (EditApplyException ex)
            {
                // Out of attempts. The file is kept with what went wrong and the run carries on
                // with the rest of the plan, the same as a file that would not compile. One file
                // the coder could not write is not a reason to throw away the four that worked.
                entry.Detail = "could not be applied";

                StageUnappliedEdit(ctx, task, reply, ex.Message);
                continue;
            }

            var declared = DeclaredTypes(content, task.RelativePath, ct);

            produced.Add(new GeneratedFile(task, content, declared));

            foreach (var type in declared)
            {
                signatures.Add(ProjectDigest.DescribeType(type));
            }
        }

        StatusMessage = $"{produced.Count} file(s) written";
        return Emit(produced);
    }

    /// <summary>
    /// How many times the coder is asked again when its changes will not apply to the file.
    /// </summary>
    /// <remarks>
    /// Its own limit rather than the compile check's. That one belongs to a different node and is
    /// spent on a different failure: a file that compiles is a file that was successfully built,
    /// and one whose blocks did not match was never built at all. Sharing a budget between them
    /// would mean a file that took two attempts to apply had one attempt left to compile.
    ///
    /// Two, because a model that has been shown the file, told which lines it invented and asked
    /// for the whole file back has been given everything there is to give. A third attempt is the
    /// same attempt again.
    /// </remarks>
    public const int EditRetryLimit = 2;

    /// <summary>
    /// Applies the coder's reply, asking it again when the reply will not apply.
    /// </summary>
    /// <remarks>
    /// A block that does not match is an ordinary model mistake and is treated as one, the way the
    /// compile check treats code that does not build: the error goes back to whoever wrote it and
    /// it tries again, capped. It used to end the run.
    ///
    /// Nothing about the matching is relaxed to make this pass. A block that was accepted without
    /// matching would write the wrong thing into the right file, which is worse than not writing
    /// it, so the only thing that changes here is how many chances the model gets to be right.
    /// </remarks>
    /// <exception cref="EditApplyException">Still would not apply after the last attempt.</exception>
    private async Task<string> ApplyWithRetriesAsync(
        NodeExecutionContext ctx,
        CodeTask task,
        string reply,
        IReadOnlyList<string> signatures,
        CancellationToken ct)
    {
        var attempt = 0;

        while (true)
        {
            try
            {
                return CodeEditApplier.Apply(reply, task.ExistingContent);
            }
            catch (EditApplyException ex) when (attempt < EditRetryLimit)
            {
                attempt++;

                var retry = ctx.Feed.Add(
                    ActivityKind.ModelStream,
                    $"{Title}  ({task.RelativePath} would not apply, attempt {attempt} of {EditRetryLimit})",
                    ex.Message,
                    Id);

                StatusMessage = $"retrying {task.FileName} ({attempt} of {EditRetryLimit})";

                var endpoint = await ResolveEndpointAsync(ctx, retry, ct).ConfigureAwait(false);
                var message = PlanPrompt.BuildEditRetryMessage(task, FitSignatures(signatures), ex.Message);

                reply = await StreamTextAsync(ctx, retry, endpoint, message, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// The same task, carrying what the file holds right now rather than what it held when the plan
    /// was made.
    /// </summary>
    /// <remarks>
    /// An excerpt says so, in the project context, which is already where a coder is told what it
    /// has to fit into. Letting it believe it had been shown a whole file when it had been shown
    /// part of one would invite exactly the invention this exists to stop.
    /// </remarks>
    private static CodeTask WithFreshContent(CodeTask task, Services.Editing.FileReading reading)
    {
        var context = reading.Note.Length == 0
            ? task.ProjectContext
            : (task.ProjectContext.Length == 0
                ? reading.Note
                : $"{task.ProjectContext}{Environment.NewLine}{Environment.NewLine}{reading.Note}");

        return new CodeTask(
            task.Order,
            task.RelativePath,
            task.TypeName,
            task.Operation,
            task.Intent,
            context,
            reading.Content,
            task.ExistingType,
            task.ExistingTypePath);
    }

    /// <summary>Keeps a file that could not be read, and moves on without asking anything about it.</summary>
    private void StageUnreadableFile(NodeExecutionContext ctx, CodeTask task, string message)
    {
        ctx.Services.Staging.Stage(new Services.Files.StagedFile(
            task.RelativePath,
            task.TypeName,
            false,
            task.Intent,
            string.Empty,
            Services.Files.StagedReason.CouldNotBeRead,
            message,
            DateTimeOffset.Now));

        if (ctx.RunId is { } runId)
        {
            ctx.Services.History.RecordFile(
                runId, task.RelativePath, Services.History.FileOutcome.Staged, message);
        }

        ctx.Feed.Error($"{task.RelativePath} was not changed", message);
    }

    /// <summary>Keeps a file the coder could not write, with what went wrong, and moves on.</summary>
    private void StageUnappliedEdit(NodeExecutionContext ctx, CodeTask task, string reply, string failure)
    {
        var detail = $"{failure}{Environment.NewLine}{Environment.NewLine}"
                     + $"Asked again {EditRetryLimit} time(s) and it did not improve.";

        // What is kept is the last reply rather than the file, because the file was never built.
        // It is still the work, and it is what somebody picking this up later needs to see.
        ctx.Services.Staging.Stage(new Services.Files.StagedFile(
            task.RelativePath,
            task.TypeName,
            task.Operation == FileOperation.Create,
            task.Intent,
            reply,
            Services.Files.StagedReason.EditDidNotApply,
            detail,
            DateTimeOffset.Now));

        if (ctx.RunId is { } runId)
        {
            ctx.Services.History.RecordFile(
                runId, task.RelativePath, Services.History.FileOutcome.Staged, detail);
        }

        ctx.Feed.Error(
            $"{task.RelativePath} was not written",
            $"The coder kept asking to replace lines that are not in the file, so it was kept rather "
            + $"than written and the run carried on.{Environment.NewLine}{failure}");
    }

    /// <summary>
    /// What a generated file declares, read back out of it so the next file in the plan can be
    /// shown the real signatures rather than what the plan hoped they would be.
    /// </summary>
    private static IReadOnlyList<IndexedType> DeclaredTypes(string content, string relativePath, CancellationToken ct)
    {
        var temporary = Path.GetTempFileName();

        try
        {
            File.WriteAllText(temporary, content);
            return SourceFileParser.Parse(temporary, relativePath, ct)?.Types ?? Array.Empty<IndexedType>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<IndexedType>();
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A stray temporary file is not worth failing a run over.
            }
        }
    }

    /// <summary>
    /// The signatures written so far, newest last and trimmed to the budget. Newest last because
    /// the file about to be written is most likely to use what was written just before it.
    /// </summary>
    private static string FitSignatures(IReadOnlyList<string> signatures)
        => signatures.Count == 0
            ? string.Empty
            : ContextBudget.Fit(string.Join(Environment.NewLine + Environment.NewLine, signatures), 4000, "earlier signatures");

    /// <summary>
    /// The reply on the completion pin, and this node itself on the model pin.
    /// </summary>
    /// <remarks>
    /// Both every time. A consumer of the model pin needs the reference whether or not anything is
    /// reading the reply, and the executor gathers output pins the same way for all of them, so
    /// there is nothing to decide here.
    /// </remarks>
    private NodeResult Emit(object? produced) => NodeResult.FromValues(new Dictionary<Guid, object?>
    {
        [Completion.Id] = produced is string reply ? Clean(reply) : produced,
        [Self.Id] = this
    });

    /// <summary>
    /// The reply as it leaves this node, with its code fence off when that is wanted.
    /// </summary>
    /// <remarks>
    /// A setting rather than a law, and on by default. This is a general model call: one feeding
    /// triage produces a plan, one feeding a debate produces an argument, and one writing
    /// documentation is supposed to keep its code blocks. Stripping any of those would be wrong.
    /// What is right by default is the common case, a model that was asked for a file and wrapped
    /// it in a fence nobody asked for.
    /// </remarks>
    private string Clean(string reply) => StripCodeFences ? Infrastructure.CodeFence.Strip(reply) : reply;

    /// <inheritdoc />
    public bool CanAnswer(out string reason) => HasUsableModel(out reason);

    /// <inheritdoc />
    public async Task<string> AnswerAsync(
        string systemPrompt,
        string message,
        NodeExecutionContext ctx,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var entry = ctx.Feed.Add(ActivityKind.ModelStream, $"{Title}  (planning, {ModelDisplayName})", null, Id);

        try
        {
            var endpoint = await ResolveEndpointAsync(ctx, entry, ct).ConfigureAwait(false);
            var onToken = new DelegateProgress<string>(entry.Append);

            // The caller's system prompt, not this node's: the node is configured to write code
            // and is being borrowed to do something else.
            var result = await ctx.Services.ModelClient
                .StreamChatAsync(endpoint, systemPrompt, message, Temperature, MaxTokens, onToken, ct)
                .ConfigureAwait(false);

            entry.Flush();
            entry.Detail = result.Summary;

            return result.Text;
        }
        catch (OperationCanceledException)
        {
            entry.Flush();
            entry.Detail = "cancelled";
            throw;
        }
        catch (Exception)
        {
            entry.Flush();
            entry.Detail = "failed";
            throw;
        }
    }

    /// <summary>
    /// Starts the extensions this node selected and collects their tools.
    /// </summary>
    /// <remarks>
    /// Whether the model can use them at all is checked here, before the request rather than
    /// after a confusing answer. A model with no tool template does not refuse; it ignores the
    /// tools and writes prose, which looks exactly like a bug in this application.
    /// </remarks>
    private async Task<IReadOnlyList<ToolDefinition>> GatherToolsAsync(
        NodeExecutionContext ctx,
        ModelEndpoint endpoint,
        CancellationToken ct)
    {
        var search = ctx.Services.Search;
        var offerSearch = search?.IsOfferedThisRun == true;

        if (_toolset is null || SelectedExtensionIds.Count == 0)
        {
            // Search alone is still tools. A graph with no extensions selected and search turned
            // on for this send has exactly one tool, and it is worth the same check as any other.
            return offerSearch
                ? await WithSupportCheckAsync(ctx, endpoint, new[] { Services.Search.WebSearchService.Tool }, ct)
                    .ConfigureAwait(false)
                : Array.Empty<ToolDefinition>();
        }

        var tools = await _toolset
            .GatherAsync(
                SelectedExtensionIds,
                AllowedToolNames.Count == 0 ? null : AllowedToolNames.ToHashSet(StringComparer.Ordinal),
                (name, reason) => ctx.Feed.Error($"{Title} could not reach {name}", reason),
                ct)
            .ConfigureAwait(false);

        if (offerSearch)
        {
            tools = tools.Append(Services.Search.WebSearchService.Tool).ToList();
        }

        if (tools.Count == 0)
        {
            return tools;
        }

        return await WithSupportCheckAsync(ctx, endpoint, tools, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Says whether the model can call any of these, before the run rather than after.
    /// </summary>
    /// <remarks>
    /// A model without a tool template silently ignores every tool it is offered, so the run looks
    /// like one where the model chose not to search. Asked here, at the point the tools are
    /// assembled, so the answer is in the feed before the first token.
    /// </remarks>
    private static async Task<IReadOnlyList<ToolDefinition>> WithSupportCheckAsync(
        NodeExecutionContext ctx,
        ModelEndpoint endpoint,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken ct)
    {
        var (support, detail) = await ctx.Services.ToolSupport
            .ProbeAsync(endpoint, ct)
            .ConfigureAwait(false);

        if (support == ToolSupport.Unsupported)
        {
            ctx.Feed.Error($"{ctx.Node.Title} has {tools.Count} tool(s) it cannot use", detail);
        }
        else
        {
            ctx.Feed.Info($"{ctx.Node.Title} has {tools.Count} tool(s)", detail);
        }

        return tools;
    }

    /// <summary>
    /// Runs one search and hands the results back as the tool result.
    /// </summary>
    /// <remarks>
    /// Every search is in the feed, with the query and what came back, for the same reason every
    /// extension tool call is: a model quietly searching is the same problem as a model quietly
    /// firing a dozen editor commands.
    ///
    /// A failure goes back as a result rather than up as a fault, exactly as an extension tool's
    /// does, so the model can say something without the search rather than the run stopping.
    /// </remarks>
    private async Task<(string Text, bool IsError)> SearchAsync(
        NodeExecutionContext ctx,
        ToolCall call,
        CancellationToken ct)
    {
        if (ctx.Services.Search is not { } search)
        {
            return ("Web search is not available in this installation.", true);
        }

        string query;

        try
        {
            query = JsonNode.Parse(call.ArgumentsJson ?? "{}") is JsonObject arguments
                    && arguments["query"]?.GetValue<string>() is { Length: > 0 } text
                ? text
                : string.Empty;
        }
        catch (System.Text.Json.JsonException)
        {
            query = string.Empty;
        }

        if (query.Length == 0)
        {
            return ("The search tool needs a 'query' saying what to search for.", true);
        }

        try
        {
            var results = await search.SearchAsync(query, ct).ConfigureAwait(false);

            ctx.Feed.Info(
                $"{Title} searched for {query}",
                results.Count == 0
                    ? "Nothing came back."
                    : string.Join(Environment.NewLine, results.Select(r => $"{r.Title}  {r.Url}")));

            return (Services.Search.WebSearchService.Format(query, results), false);
        }
        catch (Services.Search.SearchException ex)
        {
            ctx.Feed.Error($"{Title} could not search for {query}", ex.Message);
            return (ex.Message, true);
        }
    }

    /// <summary>Shortens a payload for the feed, which shows what happened rather than everything.</summary>
    private static string Summarise(string value)
    {
        var flat = value.ReplaceLineEndings(" ").Trim();
        return flat.Length <= 160 ? flat : flat[..160] + "...";
    }

    /// <inheritdoc />
    public override JsonObject SaveSettings() => new()
    {
        ["provider"] = Provider.ToString(),
        ["editFormat"] = EditFormat.ToString(),
        ["localModelPath"] = SelectedLocalModel?.Path,
        ["localModelFilePath"] = ModelFilePath,
        ["networkModel"] = SelectedNetworkModel?.ModelKey ?? _unresolvedNetworkModelKey,
        ["openRouterModel"] = OpenRouterModel,
        ["selfHostedModelId"] = SelfHostedModelId,
        ["systemPrompt"] = SystemPrompt,
        ["stripCodeFences"] = StripCodeFences,
        ["temperature"] = Temperature,
        ["maxTokens"] = MaxTokens,
        ["contextSize"] = ContextSize,
        ["gpuLayers"] = GpuLayers,
        ["baseUrl"] = BaseUrl,
        ["cloudProvider"] = CloudProviderId,
        ["cloudModel"] = CloudModelId,
        ["maxToolCalls"] = MaxToolCalls,
        ["extensions"] = new JsonArray(SelectedExtensionIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()),
        ["allowedTools"] = new JsonArray(AllowedToolNames.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray())
    };

    /// <inheritdoc />
    public override void LoadSettings(JsonObject settings)
    {
        // Provider is applied first because changing it rewrites the base URL.
        if (Enum.TryParse<ModelProvider>(settings["provider"]?.GetValue<string>(), out var provider))
        {
            Provider = provider;
        }

        var localPath = settings["localModelPath"]?.GetValue<string>();
        SelectedLocalModel = Catalog.FindByPath(localPath);

        var filePath = settings["localModelFilePath"]?.GetValue<string>();

        // Graphs saved before a node could hold its own file recorded one path either way. A
        // path that no longer resolves in the catalogue is exactly what the override describes,
        // so it is restored as one rather than dropped, missing file and all.
        if (string.IsNullOrWhiteSpace(filePath) && SelectedLocalModel is null && !string.IsNullOrWhiteSpace(localPath))
        {
            filePath = localPath;
        }

        ModelFilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath;

        var networkKey = settings["networkModel"]?.GetValue<string>();
        SelectedNetworkModel = Mesh.FindByKey(networkKey);
        _unresolvedNetworkModelKey = SelectedNetworkModel is null ? networkKey : null;

        OpenRouterModel = settings["openRouterModel"]?.GetValue<string>() ?? string.Empty;
        SelfHostedModelId = settings["selfHostedModelId"]?.GetValue<string>() ?? string.Empty;
        SystemPrompt = settings["systemPrompt"]?.GetValue<string>() ?? DefaultSystemPrompt;

        // Absent in a graph saved before this existed, and true is right for those: every
        // one of them has a node in front of the compiler whose whole job was stripping the
        // fence, and that node passing clean text through unchanged is harmless.
        StripCodeFences = settings["stripCodeFences"]?.GetValue<bool>() ?? true;
        Temperature = settings["temperature"]?.GetValue<double>() ?? 0.4d;
        MaxTokens = settings["maxTokens"]?.GetValue<int>() ?? 4096;
        ContextSize = settings["contextSize"]?.GetValue<int>() ?? LlamaLaunchOptions.DefaultContextSize;
        GpuLayers = settings["gpuLayers"]?.GetValue<int>() ?? LlamaLaunchOptions.DefaultGpuLayers;
        BaseUrl = settings["baseUrl"]?.GetValue<string>() ?? DefaultBaseUrlFor(Provider);
        CloudProviderId = settings["cloudProvider"]?.GetValue<string>() ?? string.Empty;
        CloudModelId = settings["cloudModel"]?.GetValue<string>() ?? string.Empty;
        MaxToolCalls = settings["maxToolCalls"]?.GetValue<int>() ?? 8;

        SelectedExtensionIds.Clear();

        foreach (var id in (settings["extensions"] as JsonArray)?.Select(n => n?.GetValue<string>()) ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                SelectedExtensionIds.Add(id);
            }
        }

        AllowedToolNames.Clear();

        foreach (var tool in (settings["allowedTools"] as JsonArray)?.Select(n => n?.GetValue<string>()) ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(tool))
            {
                AllowedToolNames.Add(tool);
            }
        }

        EditFormat = Enum.TryParse<EditFormat>(settings["editFormat"]?.GetValue<string>(), out var editFormat)
            ? editFormat
            : EditFormat.Automatic;
    }

    /// <summary>
    /// Picks a model file anywhere on disk for this node alone. The catalogue is left untouched,
    /// which is the point: nothing about another node's choices changes.
    /// </summary>
    [RelayCommand]
    private void BrowseForModelFile()
    {
        var picked = _dialogs.PickOpenFile(
            "Choose a model file for this node",
            "Model files (*.gguf;*.safetensors)|*.gguf;*.safetensors|All files (*.*)|*.*",
            StartingFolder());

        if (!string.IsNullOrWhiteSpace(picked))
        {
            ModelFilePath = Path.GetFullPath(picked);
        }
    }

    /// <summary>
    /// Picks a model folder for this node alone, which is the shape a safetensors model has: a
    /// config beside its weight files rather than a single file.
    /// </summary>
    [RelayCommand]
    private void BrowseForModelFolder()
    {
        var picked = _dialogs.PickFolder("Choose a model folder for this node", StartingFolder());

        if (!string.IsNullOrWhiteSpace(picked))
        {
            ModelFilePath = Path.GetFullPath(picked);
        }
    }

    /// <summary>Drops the override so the node goes back to its catalogue selection.</summary>
    [RelayCommand(CanExecute = nameof(HasModelFile))]
    private void ClearModelFile() => ModelFilePath = null;

    /// <summary>Where a browse starts: beside whatever this node runs now, or the models folder.</summary>
    private string? StartingFolder()
    {
        var current = EffectiveLocalModelPath;

        if (string.IsNullOrWhiteSpace(current))
        {
            return AppPaths.Models;
        }

        return Directory.Exists(current) ? current : Path.GetDirectoryName(current);
    }

    /// <summary>The base URL filled in when a provider is selected.</summary>
    public static string DefaultBaseUrlFor(ModelProvider provider) => provider switch
    {
        ModelProvider.OpenRouter => OpenRouterBaseUrl,
        _ => string.Empty
    };

    private async Task<NodeResult> StreamOnceAsync(
        NodeExecutionContext ctx,
        ActivityEvent entry,
        ModelEndpoint endpoint,
        string userContent,
        CancellationToken ct)
    {
        var text = await StreamTextAsync(ctx, entry, endpoint, userContent, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"{Title} received an empty reply from {ModelDisplayName}.");
        }

        return Emit(text);
    }

    /// <summary>
    /// One streamed request. Separate from the node result so that a repair, which is another
    /// request to the same model with a different message, uses exactly this path.
    /// </summary>
    private async Task<string> StreamTextAsync(
        NodeExecutionContext ctx,
        ActivityEvent entry,
        ModelEndpoint endpoint,
        string userContent,
        CancellationToken ct)
    {
        await WarnIfExpensiveAsync(ctx, userContent, ct).ConfigureAwait(false);

        var onToken = new DelegateProgress<string>(entry.Append);

        var messages = new List<ChatMessage>();

        if (!string.IsNullOrWhiteSpace(SystemPrompt))
        {
            messages.Add(ChatMessage.System(SystemPrompt));
        }

        messages.Add(ChatMessage.User(userContent));

        var tools = await GatherToolsAsync(ctx, endpoint, ct).ConfigureAwait(false);

        // The tool loop lives here rather than in the graph, and it has to. A tool call is a
        // cycle: ask, call, feed the answer back, ask again. The executor sorts a graph
        // topologically and rejects cycles outright, which is the same constraint that made a
        // Loop node impossible. So the cycle happens inside one node's execution, where the
        // executor neither sees it nor needs to.
        var callsMade = 0;
        ChatCompletionResult result;

        while (true)
        {
            result = await ctx.Services.ModelClient
                .StreamChatAsync(endpoint, messages, tools, Temperature, MaxTokens, onToken, ct)
                .ConfigureAwait(false);

            if (!result.WantsTools || tools.Count == 0 || _toolset is null)
            {
                break;
            }

            if (callsMade >= MaxToolCalls)
            {
                // Said out loud and handed to the model, so its final answer can acknowledge
                // that it was cut off rather than pretending it finished.
                entry.Flush();
                ctx.Feed.Error(
                    $"{Title} stopped calling tools",
                    $"It reached the limit of {MaxToolCalls} calls in one run. Raise the limit on the node " +
                    "if the work genuinely needs more, or look at whether it is repeating itself.");

                messages.Add(ChatMessage.Assistant(result.Text, result.ToolCalls));

                foreach (var call in result.ToolCalls)
                {
                    messages.Add(ChatMessage.Tool(
                        call.Id,
                        $"Not run. This node has a limit of {MaxToolCalls} tool calls per run and it has been reached. " +
                        "Answer with what you already know."));
                }

                tools = Array.Empty<ToolDefinition>();
                continue;
            }

            messages.Add(ChatMessage.Assistant(result.Text, result.ToolCalls));

            foreach (var call in result.ToolCalls)
            {
                ct.ThrowIfCancellationRequested();
                callsMade++;

                var owner = tools.FirstOrDefault(t => string.Equals(t.Name, call.Name, StringComparison.Ordinal));

                if (owner is null)
                {
                    messages.Add(ChatMessage.Tool(call.Id, $"There is no tool called '{call.Name}'."));
                    continue;
                }

                var extension = ctx.Services.Extensions?.Find(owner.ExtensionId);
                var extensionName = extension?.Manifest.Name ?? owner.ExtensionId;

                // Every call is visible. A model quietly firing a dozen editor commands with no
                // trace of what it did is the worst possible version of this feature.
                var toolEntry = ctx.Feed.Add(
                    ActivityKind.Info,
                    $"{Title} called {call.Name} in {extensionName}",
                    Summarise(call.ArgumentsJson),
                    Id);

                StatusMessage = $"tool {callsMade} of {MaxToolCalls}: {call.Name}";

                var (text, isError) = owner.ExtensionId == Services.Search.WebSearchService.OwnerId
                    ? await SearchAsync(ctx, call, ct).ConfigureAwait(false)
                    : await _toolset.CallAsync(call, owner.ExtensionId, ct).ConfigureAwait(false);

                toolEntry.Detail = $"{Summarise(call.ArgumentsJson)} -> {(isError ? "failed: " : string.Empty)}{Summarise(text)}";

                // A failure goes back as a result, not up as a fault. That is what lets the model
                // correct itself, exactly as the compile repair loop hands diagnostics back.
                messages.Add(ChatMessage.Tool(call.Id, text));
            }

            entry.Flush();
        }

        entry.Flush();

        // What this call cost, added to the run total. Nothing is shown for a local model,
        // because a local model costs nothing and a zero would read as a measurement.
        var callCost = ctx.Services.Cost.Add(CloudProvider, result.PromptTokens, result.CompletionTokens);

        entry.Detail = callCost is { } spent
            ? $"{result.Summary}, {RunCost.Format(spent)}"
            : result.Summary;

        StatusMessage = entry.Detail;

        if (ctx.Services.Cost.HasCost)
        {
            ctx.Feed.Info("Run cost", ctx.Services.Cost.Summary);
        }

        return result.Text;
    }

    /// <inheritdoc />
    public bool CanRepair(NodeExecutionContext ctx, out string reason) => HasUsableModel(out reason);

    /// <summary>
    /// Whether this node has enough set on it to send a request at all. Cheap, and checked before
    /// a loop spends several calls discovering the same thing.
    /// </summary>
    private bool HasUsableModel(out string reason)
    {
        reason = string.Empty;

        switch (Provider)
        {
            case ModelProvider.Local when ModelSource == LocalModelSource.MissingFile:
                reason = $"{Title} points at a model that is no longer there: {ModelFilePath}";
                return false;

            case ModelProvider.Local when string.IsNullOrWhiteSpace(EffectiveLocalModelPath) && string.IsNullOrWhiteSpace(BaseUrl):
                reason = $"{Title} has no local model selected.";
                return false;

            case ModelProvider.Network when SelectedNetworkModel is null:
                reason = $"{Title} has no network model selected.";
                return false;

            case ModelProvider.SelfHosted when string.IsNullOrWhiteSpace(SelfHostedModelId):
                reason = $"{Title} has no model id set for its self hosted server.";
                return false;

            case ModelProvider.OpenRouter when string.IsNullOrWhiteSpace(OpenRouterModel):
                reason = $"{Title} has no OpenRouter model slug set.";
                return false;

            default:
                return true;
        }
    }

    /// <inheritdoc />
    public async Task<string> ReviseAsync(CodeRepairRequest request, NodeExecutionContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ctx);

        // What this node was asked for in this run, so the fix is aimed at the same goal rather
        // than only at silencing the compiler. Falls back to the run request when the prompt pin
        // carried nothing, which is the case for a node wired straight to the chat box.
        var intent = ctx.GetText(Prompt);

        if (string.IsNullOrWhiteSpace(intent))
        {
            intent = ctx.UserRequest;
        }

        var entry = ctx.Feed.Add(
            ActivityKind.ModelStream,
            $"{Title}  (repair {request.Attempt} of {request.AttemptLimit}, {ModelDisplayName})",
            null,
            Id);

        try
        {
            var endpoint = await ResolveEndpointAsync(ctx, entry, ct).ConfigureAwait(false);
            var message = BuildRepairMessage(request, intent);

            return await StreamTextAsync(ctx, entry, endpoint, message, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            entry.Flush();
            entry.Detail = "cancelled";
            throw;
        }
        catch (Exception)
        {
            entry.Flush();
            entry.Detail = "failed";
            throw;
        }
    }

    /// <summary>
    /// The message sent when asking for a fix.
    /// </summary>
    /// <remarks>
    /// Ordered so the model reads what it was for, then what is wrong, then the code, because the
    /// last thing in a prompt is the thing it edits. The errors come already capped by the caller.
    /// The system prompt is unchanged, so a node configured to emit raw code still does.
    /// </remarks>
    private static string BuildRepairMessage(CodeRepairRequest request, string intent)
    {
        var builder = new System.Text.StringBuilder();

        builder.AppendLine($"The C# file {request.FileName} you produced does not compile. Fix it.");
        builder.AppendLine();
        builder.AppendLine("This is what it was meant to do:");
        builder.AppendLine(intent.Trim());
        builder.AppendLine();
        builder.AppendLine($"These are the compiler messages, from {request.FileName}:");
        builder.AppendLine(request.FormattedDiagnostics);
        builder.AppendLine();
        builder.AppendLine($"This is the current content of {request.FileName}:");
        builder.AppendLine(request.FailingCode);
        builder.AppendLine();
        builder.Append(
            "Return the complete corrected file. Do not return a patch, a fragment, or an explanation, "
            + "and keep everything that already worked.");

        return builder.ToString();
    }

    /// <summary>
    /// Works out where this node's request goes. Local models are served by a process this
    /// application starts; network models are served by the mesh, which decides for itself
    /// whether that means one peer or layer stages across several.
    /// </summary>
    /// <summary>
    /// Asks before a call that could be expensive.
    /// </summary>
    /// <remarks>
    /// The number is a ceiling and the message says so, twice, because a person deciding whether
    /// to spend money is owed the truth about how firm the figure is. It is the input plus the
    /// most the model is allowed to write, priced at the provider's listed rate. The real cost is
    /// usually lower, because models rarely run to their limit, and can be higher, because the
    /// model id is free text and the rate is for whichever model that provider is best known for.
    ///
    /// Nothing local reaches this, since a local model has no rates and costs nothing.
    /// </remarks>
    private async Task WarnIfExpensiveAsync(NodeExecutionContext ctx, string userContent, CancellationToken ct)
    {
        var threshold = ctx.Services.CostWarningThreshold;

        if (threshold <= 0m || CloudProvider is not { } provider || !RunCost.HasRates(provider))
        {
            return;
        }

        var ceiling = RunCost.Ceiling(provider, (SystemPrompt?.Length ?? 0) + userContent.Length, MaxTokens);

        if (ceiling < threshold)
        {
            return;
        }

        var approved = await ctx.Feed
            .RequestConfirmationAsync(
                $"{Title} could cost up to {RunCost.Format(ceiling)}",
                $"That is a ceiling, not a quote: it prices the whole input plus the {MaxTokens} tokens this node " +
                $"allows at {provider.DisplayName}'s listed rate. The real cost is usually lower, and can be higher " +
                "if the model you named is priced above that rate. Run it?",
                ct)
            .ConfigureAwait(false);

        if (!approved)
        {
            throw new OperationCanceledException($"{Title} was not run, because of what it might have cost.");
        }
    }

    /// <summary>The catalogue entry this node points at, or null when it points at nothing yet.</summary>
    public CloudProvider? CloudProvider => ProviderCatalog.Find(EffectiveProviderId);

    /// <summary>
    /// True when this node names a provider that has no key yet.
    /// </summary>
    /// <remarks>
    /// Not an error and not drawn as one. A graph somebody else made will land here the first
    /// time it is opened, and the honest reading is that it needs something rather than that it
    /// is broken.
    /// </remarks>
    public bool NeedsKey
        => Provider is ModelProvider.OpenRouter or ModelProvider.Cloud
           && CloudProvider is not null
           && _credentials?.Has(CloudProvider.Id) != true;

    /// <summary>What the inspector says about the provider.</summary>
    public string ProviderStatus => CloudProvider is not { } provider
        ? "No provider chosen."
        : NeedsKey
            ? $"{provider.DisplayName} needs a key. Add one in Settings under Models."
            : $"{provider.DisplayName}, {provider.RateSummary}.";

    /// <summary>
    /// Which catalogue id this node resolves against.
    /// </summary>
    /// <remarks>
    /// OpenRouter predates the catalogue and its own provider value, so it maps onto the
    /// catalogue entry of the same name rather than being a second way of saying the same thing.
    /// </remarks>
    private string EffectiveProviderId
        => Provider == ModelProvider.OpenRouter ? "openrouter" : CloudProviderId;

    /// <summary>
    /// Builds an endpoint for a hosted provider, taking the key from the store.
    /// </summary>
    private ModelEndpoint ResolveCloud()
    {
        var provider = ProviderCatalog.Find(EffectiveProviderId)
            ?? throw new InvalidOperationException(
                $"{Title} has no provider chosen. Pick one in the node's settings.");

        var modelId = Provider == ModelProvider.OpenRouter && !string.IsNullOrWhiteSpace(OpenRouterModel)
            ? OpenRouterModel
            : CloudModelId;

        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new InvalidOperationException(
                $"{Title} has no model id set for {provider.DisplayName}.");
        }

        var key = _credentials?.Get(provider.Id);

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                $"{Title} uses {provider.DisplayName}, which has no key yet. " +
                $"Add one in Settings under Models. Keys are stored encrypted and never saved into a graph.");
        }

        // A base url typed on the node wins, so a provider can be pointed at a proxy without a
        // catalogue change.
        var baseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? provider.BaseUrl : BaseUrl;

        return new ModelEndpoint(baseUrl, modelId, key, provider.Wire, provider.Id);
    }

    private async Task<ModelEndpoint> ResolveEndpointAsync(
        NodeExecutionContext ctx,
        ActivityEvent entry,
        CancellationToken ct)
    {
        if (Provider is ModelProvider.OpenRouter or ModelProvider.Cloud)
        {
            return ResolveCloud();
        }

        if (Provider == ModelProvider.Network)
        {
            return ResolveNetwork(ctx);
        }

        if (Provider == ModelProvider.SelfHosted)
        {
            if (string.IsNullOrWhiteSpace(BaseUrl))
            {
                throw new InvalidOperationException($"{Title} has no base URL set for its self hosted server.");
            }

            if (string.IsNullOrWhiteSpace(SelfHostedModelId))
            {
                throw new InvalidOperationException($"{Title} has no model id set for its self hosted server.");
            }

            return new ModelEndpoint(BaseUrl, SelfHostedModelId);
        }

        if (ModelSource == LocalModelSource.MissingFile)
        {
            throw new InvalidOperationException(
                $"{Title} points at a model that is no longer there: {ModelFilePath}. "
                + "Browse for it again, or clear it to go back to the catalogue selection.");
        }

        var modelPath = EffectiveLocalModelPath;
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new InvalidOperationException(
                $"{Title} has no local model selected. Drop a model into the models folder, add a folder, or browse for one from the settings panel.");
        }

        // The original escape hatch, unchanged: an explicit base URL on a local node means the
        // user is pointing at their own server, so nothing is spawned.
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new ModelEndpoint(BaseUrl, Path.GetFileNameWithoutExtension(modelPath));
        }

        var status = new DelegateProgress<string>(message =>
        {
            entry.Detail = message;
            StatusMessage = message;
        });

        var launchOptions = new ModelRuntimeOptions { ContextSize = ContextSize, GpuLayers = GpuLayers };

        // Which runtime serves this is worked out from what the path actually holds, and the
        // node never learns the answer. Local means whatever local runtime can serve this.
        var served = await ctx.Services.Runtimes
            .ServeAsync(modelPath, launchOptions, status, ct)
            .ConfigureAwait(false);

        return new ModelEndpoint(served.BaseUrl, served.ModelId);
    }

    /// <summary>
    /// Points the request at the mesh. The gate is the mesh's own answer to whether it can
    /// assemble this model right now, and a refusal repeats the reason it gave rather than
    /// inventing one.
    /// </summary>
    private ModelEndpoint ResolveNetwork(NodeExecutionContext ctx)
    {
        var mesh = ctx.Services.Mesh;

        var networkModel = SelectedNetworkModel
            ?? throw new InvalidOperationException(
                $"{Title} has no network model selected. Pick one in the Network tab or the node settings.");

        if (!mesh.IsRunning)
        {
            throw new InvalidOperationException(
                $"{Title} cannot use {networkModel.DisplayLabel}: this install's mesh node is not running. Start it from the Network tab.");
        }

        if (!networkModel.CanRun)
        {
            // A model still coming up and one the mesh cannot assemble are both refusals, but
            // they are not the same news, so the message says which it is.
            var detail = networkModel.StatusDetail ?? (networkModel.Availability == ModelAvailability.Blocked
                ? "the mesh cannot assemble it right now."
                : "the mesh is still bringing it up.");

            throw new InvalidOperationException(
                networkModel.Availability == ModelAvailability.Blocked
                    ? $"{Title} cannot use {networkModel.DisplayLabel}. {detail}"
                    : $"{Title} cannot use {networkModel.DisplayLabel} yet. {detail}");
        }

        // Automatic but visible: the mesh chose the assembly, so the run shows its work.
        if (networkModel.Plan is { IsSplit: true } plan)
        {
            ctx.Feed.Info("Coverage plan", $"{Title}: {plan.Summary}");
        }

        return new ModelEndpoint(mesh.ApiBaseUrl, networkModel.ModelId);
    }

    partial void OnProviderChanged(ModelProvider value) => BaseUrl = DefaultBaseUrlFor(value);

    private static string? LocalModelName(string? path)
        => string.IsNullOrWhiteSpace(path) ? null : Path.GetFileNameWithoutExtension(path);
}
