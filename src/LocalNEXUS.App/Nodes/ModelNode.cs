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
public sealed partial class ModelNode : NodeBase, ICodeRepairSource, IModelHandle, IToolCallingModel
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
    /// <remarks>
    /// A load parameter. The server allocates its key and value cache when it comes up, so changing
    /// this changes nothing about a server already running and takes effect when the next run
    /// restarts it.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadedText))]
    [NotifyPropertyChangedFor(nameof(HasLoadDrift))]
    private int _contextSize = LlamaLaunchOptions.DefaultContextSize;

    /// <summary>GPU layers requested when this node starts a llama-server.</summary>
    /// <remarks>A load parameter, for the same reason as the context.</remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadedText))]
    [NotifyPropertyChangedFor(nameof(HasLoadDrift))]
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

    /// <summary>
    /// What is serving local models, asked only about what it already has running.
    /// </summary>
    /// <remarks>
    /// Named directly rather than reached through the resolver, and that is deliberate. A context
    /// window and a layer count are llama.cpp's own parameters, which the runtime options say the
    /// Python runtime ignores, so asking the llama manager about them is asking the right component
    /// rather than widening an interface every runtime would have to answer null to.
    /// </remarks>
    private readonly LlamaServerManager? _servers;

    /// <summary>
    /// What establishes whether a model actually calls tools, shared so it is asked once.
    /// </summary>
    /// <remarks>
    /// The same instance the run path uses, so an answer measured here is the answer a run gets
    /// and neither of them pays for it twice.
    /// </remarks>
    private readonly ToolSupportProbe? _probe;
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

    /// <summary>
    /// What the running server for this node's model actually has, and whether it matches.
    /// </summary>
    /// <remarks>
    /// The context and the layer count are fixed when a server starts, so a node whose fields have
    /// been edited and whose server has not been restarted is asking for one thing and talking to
    /// another. Saying so is the whole point: the alternative is finding out from a refusal naming
    /// a context somebody thought they had changed.
    ///
    /// Nothing is started to answer this. A model with no server up says so, which is the ordinary
    /// state before the first run.
    /// </remarks>
    public string LoadedText
    {
        get
        {
            if (Provider != ModelProvider.Local)
            {
                return string.Empty;
            }

            var checkedAt = _lastCheckedAt is { } at ? $" Checked at {at:HH:mm:ss}." : string.Empty;

            // What it is doing is on the badge. This says what with, which is the part a badge has
            // no room for and somebody still needs: the numbers it actually loaded with.
            switch (LoadState)
            {
                case LocalModelState.Starting:
                    return "The run is waiting for it to finish loading." + checkedAt;

                case LocalModelState.Restarting:
                    return "A load setting changed, so it is being stopped and started again." + checkedAt;
            }

            if (_servers?.Describe(EffectiveLocalModelPath) is not { } running)
            {
                return "These apply when the model starts." + checkedAt;
            }

            var loaded = $"Context {running.ContextSize}, {running.GpuLayers} GPU layers, port {running.Port}.";

            return running.ContextSize == ContextSize && running.GpuLayers == GpuLayers
                ? loaded + checkedAt
                : $"{loaded} That is not what is set here, so the next run restarts it.{checkedAt}";
        }
    }

    /// <summary>The state as the badge spells it, which is not how the enum spells it.</summary>
    public string LoadStateText => LoadState switch
    {
        LocalModelState.Starting => "Starting",
        LocalModelState.Restarting => "Restarting",
        LocalModelState.Running => "Running",
        _ => "Not loaded"
    };

    /// <summary>What the model is doing, which the node shows whether or not anything asked.</summary>
    public LocalModelState LoadState => Provider == ModelProvider.Local && _servers is { } servers
        ? servers.StateFor(EffectiveLocalModelPath)
        : LocalModelState.NotLoaded;

    /// <summary>When somebody last pressed the button, so pressing it visibly does something.</summary>
    private DateTimeOffset? _lastCheckedAt;

    /// <summary>True while the running server disagrees with what is set here.</summary>
    public bool HasLoadDrift
    {
        get
        {
            if (Provider != ModelProvider.Local || _servers?.Describe(EffectiveLocalModelPath) is not { } running)
            {
                return false;
            }

            return running.ContextSize != ContextSize || running.GpuLayers != GpuLayers;
        }
    }

    /// <summary>
    /// Reads what is running again, and says that it did.
    /// </summary>
    /// <remarks>
    /// The time is on the line for one reason: with nothing loaded the answer does not change, so
    /// a button that only redrew the same sentence looked broken. Saying when it last looked is
    /// what makes pressing it visibly do something.
    /// </remarks>
    [RelayCommand]
    private void RefreshLoaded()
    {
        _lastCheckedAt = DateTimeOffset.Now;
        RefreshLoadState();
    }

    /// <summary>Redraws what the model is doing, without claiming anybody asked.</summary>
    public void RefreshLoadState()
    {
        OnPropertyChanged(nameof(LoadState));
        OnPropertyChanged(nameof(LoadStateText));
        OnPropertyChanged(nameof(LoadedText));
        OnPropertyChanged(nameof(HasLoadDrift));
    }

    /// <summary>
    /// Rebuilds the offered extensions from what this project has installed.
    /// </summary>
    /// <remarks>
    /// Called when the panel opens. Nothing is started here: an extension is a process, and one is
    /// not launched to fill in a panel somebody opened to change the temperature. What is selected
    /// survives an extension that is no longer installed, because the graph is the record of what
    /// was chosen and this machine not having it is a fact about this machine.
    /// </remarks>
    [RelayCommand]
    public void RefreshToolExtensions()
    {
        ToolExtensions.Clear();

        if (_toolset is null)
        {
            RaiseToolCostChanged();
            return;
        }

        foreach (var installed in _toolset.Registry.Extensions.Where(e => e.Manifest.ProvidesTools))
        {
            ToolExtensions.Add(new ExtensionChoice(
                installed.Manifest.Id,
                installed.Manifest.Name,
                installed.StateText,
                installed.IsUsable,
                SelectedExtensionIds.Contains(installed.Manifest.Id, StringComparer.OrdinalIgnoreCase),
                OnExtensionChoiceChanged));
        }

        RaiseToolCostChanged();
    }

    /// <summary>
    /// Starts one selected extension and asks it what tools it has.
    /// </summary>
    /// <remarks>
    /// Asked for rather than done on opening the panel. Listing means starting the process, and a
    /// server with three hundred tools takes a moment to answer, which is a thing to wait for
    /// deliberately rather than a thing to make every panel open slowly.
    /// </remarks>
    [RelayCommand]
    public async Task ListToolsAsync(ExtensionChoice? choice)
    {
        if (choice is null || _toolset is null || !choice.IsUsable)
        {
            return;
        }

        choice.Listing = ToolListingState.Listing;
        choice.Problem = null;

        var problem = string.Empty;

        var tools = await _toolset
            .GatherAsync(new[] { choice.Id }, null, (_, reason) => problem = reason, CancellationToken.None)
            .ConfigureAwait(true);

        if (problem.Length > 0)
        {
            choice.Problem = problem;
            choice.Listing = ToolListingState.Unavailable;
            RaiseToolCostChanged();
            return;
        }

        choice.Tools.Clear();

        foreach (var tool in tools)
        {
            // Empty means all of them, so an unnarrowed extension shows every tool ticked, which
            // is what it actually does rather than what an empty list looks like.
            var selected = AllowedToolNames.Count == 0
                           || AllowedToolNames.Contains(tool.Name, StringComparer.Ordinal);

            choice.Tools.Add(new ToolChoice(tool, selected, OnToolChoiceChanged));
        }

        choice.Listing = ToolListingState.Listed;

        // Asking an extension what it has is a statement of intent to use it. Leaving it unticked
        // after showing somebody its eighty tools is how a run started with none of them.
        choice.IsSelected = true;

        choice.RefreshSummary();

        RaiseToolCostChanged();
    }

    /// <summary>Asks the model's own server whether it can call tools at all.</summary>
    [RelayCommand]
    public async Task CheckToolSupportAsync()
    {
        ToolSupport = ToolSupport.Unknown;
        ToolSupportDetail = "Checking.";

        if (Catalog is null)
        {
            return;
        }

        try
        {
            if (_probe is null)
            {
                ToolSupportDetail = "Nothing here can ask.";
                return;
            }

            // A real answer means asking the model, so there has to be something to ask. A local
            // model with no server up is not a model that cannot call tools; it is one nothing has
            // been established about, which is a different state and is said as one.
            var address = BaseUrl;

            if (string.IsNullOrWhiteSpace(address) && Provider == ModelProvider.Local)
            {
                if (_servers?.Describe(EffectiveLocalModelPath) is not { } running)
                {
                    ToolSupport = ToolSupport.Unknown;
                    ToolSupportDetail = "The model is not loaded, so it cannot be asked yet. Run once, then check.";

                    return;
                }

                address = $"http://127.0.0.1:{running.Port}/v1";
            }

            ToolSupportDetail = "Asking the model to call a tool.";

            var endpoint = new ModelEndpoint(address, ModelDisplayName, null);

            var (support, detail) = await _probe
                .ProbeAsync(endpoint, ToolSupportKey, CancellationToken.None)
                .ConfigureAwait(true);

            ToolSupport = support;
            ToolSupportDetail = detail;
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or InvalidOperationException or UriFormatException)
        {
            ToolSupport = ToolSupport.Unknown;
            ToolSupportDetail = $"The model's server could not be asked: {ex.Message}";
        }
    }

    /// <summary>
    /// What a tool support answer is remembered against.
    /// </summary>
    /// <remarks>
    /// The model file for a local one, because the address of a local server is a port picked when
    /// it started and the model behind it is the thing being asked about. Anything else is the
    /// address and the model id, which together are what somebody is pointing at.
    /// </remarks>
    private string ToolSupportKey => Provider == ModelProvider.Local
        && EffectiveLocalModelPath is { Length: > 0 } file
            ? file
            : $"{BaseUrl}|{ModelDisplayName}";

    /// <summary>
    /// Where a check looks when the node has no address of its own.
    /// </summary>
    /// <remarks>
    /// A local model is served on a port picked when it starts, so there is nothing to ask before
    /// a run has started one. The check then reports that it could not be asked, which is the
    /// honest answer and is not the same as saying the model cannot call tools.
    /// </remarks>
    private const string LoopbackProbeUrl = "http://127.0.0.1:0/v1";

    private void OnExtensionChoiceChanged(ExtensionChoice choice)
    {
        if (choice.IsSelected)
        {
            if (!SelectedExtensionIds.Contains(choice.Id, StringComparer.OrdinalIgnoreCase))
            {
                SelectedExtensionIds.Add(choice.Id);
            }
        }
        else
        {
            for (var i = SelectedExtensionIds.Count - 1; i >= 0; i--)
            {
                if (string.Equals(SelectedExtensionIds[i], choice.Id, StringComparison.OrdinalIgnoreCase))
                {
                    SelectedExtensionIds.RemoveAt(i);
                }
            }
        }

        RaiseToolCostChanged();
    }

    /// <summary>
    /// Keeps the allowed names in step with the ticks, and keeps empty meaning all of them.
    /// </summary>
    /// <remarks>
    /// An extension with everything ticked writes nothing, because empty means all and writing out
    /// three hundred names would make the graph file mostly a list of things nobody narrowed. The
    /// moment one is unticked the rest are written down, which is what narrowing means.
    /// </remarks>
    private void OnToolChoiceChanged(ToolChoice choice)
    {
        var listed = ToolExtensions.Where(e => e.Listing == ToolListingState.Listed).ToList();

        if (listed.Count == 0)
        {
            return;
        }

        var everything = listed.SelectMany(e => e.Tools).All(t => t.IsSelected);

        AllowedToolNames.Clear();

        if (!everything)
        {
            foreach (var name in listed.SelectMany(e => e.Tools).Where(t => t.IsSelected).Select(t => t.Name))
            {
                AllowedToolNames.Add(name);
            }
        }

        foreach (var extension in listed)
        {
            extension.RefreshSummary();
        }

        RaiseToolCostChanged();
    }

    private void RaiseToolCostChanged()
    {
        OnPropertyChanged(nameof(ToolTokenEstimate));
        OnPropertyChanged(nameof(ToolCostText));
    }

    /// <summary>
    /// The installed extensions this node could use, as the panel offers them.
    /// </summary>
    /// <remarks>
    /// Built on demand rather than kept in step with the registry, because the panel is the only
    /// thing that reads it and it is rebuilt every time the panel is opened. What is saved with the
    /// graph is the two lists of names above; this is a view of them beside what is installed now,
    /// so a graph opened on a machine without an extension says so rather than losing the choice.
    /// </remarks>
    public ObservableCollection<ExtensionChoice> ToolExtensions { get; } = new();

    /// <summary>Roughly what the whole selection costs, every turn.</summary>
    public int ToolTokenEstimate => ToolExtensions
        .Where(e => e.IsSelected)
        .Sum(e => e.Tools.Count == 0 ? 0 : e.SelectedTokens);

    /// <summary>What the selection costs, worded for the panel.</summary>
    public string ToolCostText
    {
        get
        {
            var selected = ToolExtensions.Count(e => e.IsSelected);

            if (selected == 0)
            {
                return "No tools. Nothing is offered to the model and nothing is spent on schemas.";
            }

            var listed = ToolExtensions.Where(e => e.IsSelected).All(e => e.Listing == ToolListingState.Listed);

            return listed
                ? $"{selected} extension(s), about {ToolTokenEstimate} tokens of schema on every turn."
                : $"{selected} extension(s). List their tools to see what that costs.";
        }
    }

    /// <summary>Whether the model behind this node can call tools at all.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolSupportText))]
    private ToolSupport _toolSupport = ToolSupport.Unknown;

    /// <summary>What the probe said, when it said anything.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolSupportText))]
    private string _toolSupportDetail = string.Empty;

    /// <summary>
    /// What to say about tool support where tools are being chosen.
    /// </summary>
    /// <remarks>
    /// Before a run rather than after it. Selecting tools for a model that will ignore them is a
    /// silent waste of the context they were paid for, and the run that discovers it has already
    /// spent it.
    ///
    /// The answer comes from giving the model a tool and seeing whether it calls one, because the
    /// capability its server reports is about the chat template and not about the model. A model
    /// that reports both tool flags true and then writes the call out as text in a code fence is
    /// the ordinary case rather than the odd one.
    /// </remarks>
    public string ToolSupportText => ToolSupport switch
    {
        ToolSupport.Supported => ToolSupportDetail.Length > 0
            ? ToolSupportDetail
            : "This model calls tools.",

        ToolSupport.Unsupported => ToolSupportDetail.Length > 0
            ? ToolSupportDetail
            : "This model does not call tools, so anything selected here is context spent for nothing.",

        _ => ToolSupportDetail.Length > 0
            ? ToolSupportDetail
            : "Whether this model calls tools has not been established. Check it before relying on them."
    };

    public ModelNode(
        ModelCatalog catalog,
        MeshManager mesh,
        IDialogService dialogs,
        ExtensionToolset? toolset = null,
        ICredentialStore? credentials = null,
        LlamaServerManager? servers = null,
        ToolSupportProbe? probe = null)
        : base("Model")
    {
        _servers = servers;
        _probe = probe;

        // A list changing raises a collection change and never a property change, so without this
        // the whole extension and tool selection was invisible to anything watching for an edit.
        SelectedExtensionIds.CollectionChanged += (_, _) => RaiseSettingsChanged();
        AllowedToolNames.CollectionChanged += (_, _) => RaiseSettingsChanged();
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

        // Appended last, for the same reason Self was: a saved graph matches pins by name and falls
        // back to position, so anything inserted ahead of an existing pin takes its identity and
        // every wire leaving this node lands somewhere else.
        //
        // The same reply as the code pin, and it exists so that a reply meant to be read has a pin
        // that says so. Code used to be allowed to flow into Text, which meant a wire into anything
        // expecting prose was drawn from a pin whose whole point is that it carries a file.
        Answer = AddOutput("Text", PinType.Text);

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

    /// <summary>The same reply, for anything that wants it as text rather than as a file.</summary>
    public Pin Answer { get; }

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

    /// <summary>
    /// Whether anything establishes that this node's model writes diffs well.
    /// </summary>
    /// <remarks>
    /// The provider is the only signal there is. A model file on this machine is a small one by
    /// definition, because that is what fits, and the published benchmarks put small models well
    /// below the line where asking for a diff is sensible. A hosted frontier provider is above it.
    ///
    /// A self hosted server and a mesh model are unknown, because either could be serving anything,
    /// and unknown leans towards sending the whole file. That is the safer way to be wrong: a whole
    /// file that will not fit is refused loudly, and a diff a model could not write comes back well
    /// formed and pointing at lines that do not exist.
    /// </remarks>
    public EditCapability Capability => Provider is ModelProvider.Cloud or ModelProvider.OpenRouter
        ? EditCapability.HandlesDiffs
        : EditCapability.Unknown;

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
                task.ExistingContent?.Length ?? 0,
                Capability);

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
                return await ApplyOnceAsync(ctx, task, reply, ct).ConfigureAwait(false);
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
    /// Turns one reply into the new file, structurally where it can and by text where it cannot.
    /// </summary>
    /// <remarks>
    /// A reply that names what it is changing goes through Roslyn: the member is found by walking
    /// the tree, so there is no search text and nothing to hallucinate, and the formatter lays the
    /// replacement out to fit rather than refusing it for its indentation. That is the route that
    /// makes this class of failure impossible rather than recoverable.
    ///
    /// Everything else is unchanged. An edit that names nothing, names something the file does not
    /// have, or would need a type and one of its members changed at once falls back to the whole
    /// file and diff path exactly as before, and the fallback is said out loud rather than being a
    /// silent second attempt.
    /// </remarks>
    private async Task<string> ApplyOnceAsync(
        NodeExecutionContext ctx,
        CodeTask task,
        string reply,
        CancellationToken ct)
    {
        var body = CodeEditApplier.Unfence(reply);

        if (task.ExistingContent is not { Length: > 0 } existing
            || !Services.Editing.StructuredEditParser.LooksStructured(body))
        {
            return CodeEditApplier.Apply(reply, task.ExistingContent);
        }

        var edits = Services.Editing.StructuredEditParser.Parse(body);
        var result = await Services.Editing.RoslynEditApplier
            .ApplyAsync(existing, edits, ct)
            .ConfigureAwait(false);

        if (result.IsApplied)
        {
            ctx.Feed.Info(
                $"{task.RelativePath} changed by name, not by text",
                string.Join(Environment.NewLine, edits.Select(e => e.ToString())));

            return result.Content;
        }

        // Refused is a real problem with what the model asked for, and the retry is the place that
        // tells it so. Not mappable is not a failure at all; it is a reply in another shape.
        if (result.State == Services.Editing.StructuredEditState.Refused)
        {
            throw new EditApplyException(result.Message);
        }

        ctx.Feed.Info($"{task.RelativePath} was not expressed as named changes", result.Message);

        return CodeEditApplier.Apply(reply, task.ExistingContent);
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
    private NodeResult Emit(object? produced)
    {
        var cleaned = produced is string reply ? Clean(reply) : produced;

        return NodeResult.FromValues(new Dictionary<Guid, object?>
        {
            [Completion.Id] = cleaned,
            [Answer.Id] = cleaned,
            [Self.Id] = this
        });
    }

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
    /// <inheritdoc />
    public string ModelName => ModelDisplayName;

    /// <inheritdoc />
    /// <remarks>
    /// The endpoint is resolved to ask it, because whether a model can call tools is a question its
    /// own server answers and the answer decides whether offering any is worth the context.
    /// </remarks>
    public async Task<IReadOnlyList<ToolDefinition>> ConfiguredToolsAsync(
        NodeExecutionContext ctx,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var entry = ctx.Feed.Add(ActivityKind.Info, $"{Title}: listing its tools", null, Id);

        try
        {
            var endpoint = await ResolveEndpointAsync(ctx, entry, ct).ConfigureAwait(false);
            var tools = await GatherToolsAsync(ctx, endpoint, ct).ConfigureAwait(false);

            entry.Detail = tools.Count == 0 ? "none" : $"{tools.Count} tool(s)";

            return tools;
        }
        catch (ModelClientException ex)
        {
            entry.Detail = "unavailable";
            ctx.Feed.Error($"{Title} could not be asked for its tools", ex.Message);

            return Array.Empty<ToolDefinition>();
        }
    }

    /// <inheritdoc />
    public async Task<ChatCompletionResult> ContinueAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        NodeExecutionContext ctx,
        IProgress<string>? onToken,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var entry = ctx.Feed.Add(ActivityKind.ModelStream, $"{Title}  ({ModelDisplayName})", null, Id);

        try
        {
            var endpoint = await ResolveEndpointAsync(ctx, entry, ct).ConfigureAwait(false);

            var result = await ctx.Services.ModelClient
                .StreamChatAsync(
                    endpoint,
                    messages,
                    tools.Count == 0 ? null : tools,
                    Temperature,
                    MaxTokens,
                    onToken ?? new DelegateProgress<string>(entry.Append),
                    ct)
                .ConfigureAwait(false);

            entry.Flush();
            entry.Detail = result.Summary;

            ctx.Services.Cost.Add(CloudProvider, result.PromptTokens, result.CompletionTokens);

            return result;
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

    /// <inheritdoc />
    public Task<(string Text, bool IsError)> CallConfiguredToolAsync(
        ToolCall call,
        string ownerId,
        NodeExecutionContext ctx,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        if (string.Equals(ownerId, Services.Search.WebSearchService.OwnerId, StringComparison.Ordinal))
        {
            return SearchAsync(ctx, call, ct);
        }

        if (_toolset is null)
        {
            return Task.FromResult(("This installation has no extension host, so that tool cannot be run.", true));
        }

        return _toolset.CallAsync(call, ownerId, ct);
    }

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
    private async Task<IReadOnlyList<ToolDefinition>> WithSupportCheckAsync(
        NodeExecutionContext ctx,
        ModelEndpoint endpoint,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken ct)
    {
        var (support, detail) = await ctx.Services.ToolSupport
            .ProbeAsync(endpoint, ToolSupportKey, ct)
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
    /// Says so when the model plainly meant to call a tool and wrote it out instead.
    /// </summary>
    /// <remarks>
    /// It is not acted on and nothing is parsed out of it to run. This is diagnosis: a reply that
    /// is a function call in a code fence, delivered as the answer, looks like this application
    /// ignoring the tools it was given, and the model naming a tool it was offered is proof enough
    /// that it understood and could not express it.
    ///
    /// The model has to be shown the tool for this to fire, and has to name it. A reply that merely
    /// mentions searching the web is a reply, not a failed call.
    /// </remarks>
    private void WarnIfItWroteTheCallOut(
        NodeExecutionContext ctx,
        IReadOnlyList<ToolDefinition> tools,
        string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)
            || !reply.Contains("\"name\"", StringComparison.Ordinal)
            || (!reply.Contains("\"arguments\"", StringComparison.Ordinal)
                && !reply.Contains("\"parameters\"", StringComparison.Ordinal)))
        {
            return;
        }

        var named = tools.FirstOrDefault(t => reply.Contains(t.Name, StringComparison.Ordinal));

        if (named is null)
        {
            return;
        }

        ctx.Feed.Error(
            $"{Title} wrote a tool call out as text",
            $"It asked for {named.Name} in the body of its reply instead of calling it, so nothing ran "
            + "and the reply you get is the request rather than the result. The model chose the right "
            + "tool and cannot emit it through the protocol. Check tool support on this node, and use "
            + "a model tuned for tool use or a hosted one. Nothing here parses a call out of text and "
            + $"runs it, because a misread one would run the wrong thing.{Environment.NewLine}{Environment.NewLine}{reply}");
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
                WarnIfItWroteTheCallOut(ctx, tools, result.Text);
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

        // Said before the server is stopped, not written up afterwards. A restart is tens of
        // seconds of a run apparently doing nothing, and the reason for it is a setting somebody
        // changed, which is worth knowing while it is happening rather than once it is over.
        if (_servers?.Describe(modelPath) is { } current
            && (current.ContextSize != ContextSize || current.GpuLayers != GpuLayers))
        {
            ctx.Feed.Info(
                $"Restarting {ModelDisplayName}",
                $"It is running with a context of {current.ContextSize} and {current.GpuLayers} GPU layers, "
                + $"and this node asks for {ContextSize} and {GpuLayers}. Those are fixed when the model "
                + "loads, so it is being stopped and started again. This takes as long as loading it did.");
        }

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
