using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Credentials;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.Distributed;
using LocalNEXUS.App.Services.Extensions;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.Nodes;

/// <summary>
/// Creates nodes, both for the palette and for the loader.
/// </summary>
/// <remarks>
/// Node construction lives here rather than in the view model or the serializer because both of
/// them need it, and because model nodes need their services injected. Registering a new node
/// type is a single entry in the built in table, which is what the palette is projected from and
/// what the factory looks up in, so the two cannot come to disagree.
/// </remarks>
public sealed class NodeFactory
{
    private readonly ModelCatalog _catalog;
    private readonly MeshManager _mesh;
    private readonly IDialogService _dialogs;
    private readonly AppConfig _config;
    private readonly ExtensionRegistry _extensions;
    private readonly ExtensionHost _host;
    private readonly ExtensionToolset _toolset;
    private readonly ICredentialStore _credentials;
    private readonly ProjectSettingsService? _project;
    private readonly Services.Inference.LlamaServerManager? _servers;
    private readonly Services.Inference.ToolSupportProbe? _probe;

    public NodeFactory(
        ModelCatalog catalog,
        MeshManager mesh,
        IDialogService dialogs,
        AppConfig config,
        ExtensionRegistry extensions,
        ExtensionHost host,
        ICredentialStore credentials,
        ProjectSettingsService? project = null,
        System.Windows.Threading.Dispatcher? dispatcher = null,
        Services.Inference.LlamaServerManager? servers = null,
        Services.Inference.ToolSupportProbe? probe = null)
    {
        _servers = servers;
        _probe = probe;
        _project = project;
        _credentials = credentials;
        _catalog = catalog;
        _mesh = mesh;
        _dialogs = dialogs;
        _config = config;
        _extensions = extensions;
        _host = host;
        _toolset = new ExtensionToolset(extensions, host, dispatcher);
    }

    /// <summary>
    /// The palette, which is the built in types plus whatever the open project's extensions
    /// contribute right now.
    /// </summary>
    /// <remarks>
    /// Extension nodes appear here and nowhere else that matters. Everything downstream, the
    /// canvas, the serializer and above all the executor, sees a node and not a category of node.
    /// </remarks>
    public IReadOnlyList<NodeDescriptor> AvailableDescriptors()
    {
        var available = new List<NodeDescriptor>(Descriptors);

        foreach (var (extension, node) in _extensions.UsableNodes())
        {
            available.Add(new NodeDescriptor(
                node.TypeKey,
                node.DisplayName,
                string.IsNullOrWhiteSpace(node.Description)
                    ? $"Contributed by {extension.Manifest.Name}."
                    : node.Description));
        }

        return available;
    }

    /// <summary>
    /// Creates a placeholder for a node whose extension is not installed here, so that opening a
    /// graph on a machine missing an extension does not discard the node and its wires.
    /// </summary>
    public static UnavailableNode CreateUnavailable(string typeKey) => new(typeKey);

    /// <summary>
    /// Creates a node contributed by one of the open project's extensions.
    /// </summary>
    /// <remarks>
    /// The built in switch is tried first, so an extension can never shadow a built in type by
    /// claiming its key. Anything left over is looked up among the extensions, and only a key
    /// that belongs to nobody is refused.
    /// </remarks>
    /// <exception cref="NotSupportedException">No built in type and no installed extension owns this key.</exception>
    private NodeBase CreateContributed(string typeKey)
    {
        var extension = _extensions.FindByNodeType(typeKey)
            ?? throw new NotSupportedException($"Unknown node type '{typeKey}'.");

        var contribution = extension.Manifest.Nodes
            .First(n => string.Equals(n.TypeKey, typeKey, StringComparison.OrdinalIgnoreCase));

        return new ExtensionNode(_host, _extensions, extension, contribution);
    }

    /// <summary>A node type as offered by the palette.</summary>
    /// <param name="TypeKey">The discriminator written to the graph file.</param>
    /// <param name="DisplayName">Label shown in the palette.</param>
    /// <param name="Description">Tooltip explaining what the node does.</param>
    public readonly record struct NodeDescriptor(string TypeKey, string DisplayName, string Description);

    /// <summary>
    /// One built in node type: what it is called, what it used to be called, and how to make one.
    /// </summary>
    /// <param name="TypeKey">The key this type writes when a graph is saved.</param>
    /// <param name="DisplayName">Label shown in the palette.</param>
    /// <param name="Description">Tooltip explaining what the node does.</param>
    /// <param name="FormerKeys">Every key this type has been saved under before, so old graphs open.</param>
    /// <param name="Build">Makes one, started from the application wide defaults.</param>
    private sealed record BuiltInNode(
        string TypeKey,
        string DisplayName,
        string Description,
        IReadOnlyList<string> FormerKeys,
        Func<NodeFactory, NodeBase> Build);

    /// <summary>
    /// Every built in node type, in palette order. The one place any of this is written down.
    /// </summary>
    /// <remarks>
    /// This table exists in this shape because the palette and the factory drifted apart twice,
    /// and the second time there was already a comment warning about the first. A comment is not a
    /// mechanism.
    ///
    /// What makes it one is that the two are no longer two things. The palette is projected from
    /// this list and the factory looks up in it, so a type offered on the palette that nothing can
    /// build is not a bug that ships, it is a row missing its <c>Build</c> argument and the build
    /// fails. Adding a node type is one row here and nothing else, and a row cannot be written
    /// without saying how to construct what it offers.
    ///
    /// Former keys live on the row for the same reason. A rename used to be a case label in a
    /// switch somewhere else, which is exactly the thing that got forgotten; here it sits beside
    /// the name that replaced it.
    /// </remarks>
    private static readonly IReadOnlyList<BuiltInNode> BuiltIn = new[]
    {
        new BuiltInNode(
            "Prompt",
            "Prompt",
            "Sends on what you typed in the chat box.",
            new[] { "Input" },
            _ => new PromptNode()),

        // One node with a model, a set of tools and a loop, beside the pipeline rather than
        // instead of it. The pipeline is right when the same work runs the same way every time;
        // this is right when the request is not the shape the pipeline describes.
        new BuiltInNode(
            "Agent",
            "Agent",
            "Does the work itself: reads, writes, compiles and calls tools, deciding each step. Wire a Model node into it.",
            Array.Empty<string>(),
            _ => new AgentNode()),

        new BuiltInNode(
            "Triage",
            "Triage",
            "Reads your project and decides which files to leave alone, edit, or write new.",
            new[] { "Plan" },
            factory => new TriageNode
            {
                MapCharacters = factory._config.DefaultMapCharacters,
                CandidateCharacters = factory._config.DefaultCandidateCharacters,
                EmittedCharacters = factory._config.DefaultEmittedCharacters,
                CandidateLimit = factory._config.DefaultCandidateLimit
            }),

        // No key is seeded, because a node no longer holds one. It names a provider and the key
        // is looked up from the store when a run needs it.
        new BuiltInNode(
            "Model",
            "Model",
            "Asks a model, local or hosted, and sends on its reply.",
            Array.Empty<string>(),
            // The system prompt is seeded from the project for the same reason the Output node's
            // folder is: a plain C# project has no business being told it is writing Unity code.
            // Nothing reaches back into a saved graph, because the prompt belongs to the node.
            factory => new ModelNode(
                factory._catalog,
                factory._mesh,
                factory._dialogs,
                factory._toolset,
                factory._credentials,
                factory._servers,
                factory._probe)
            {
                SystemPrompt = ModelNode.PromptFor(factory._project?.Kind ?? ProjectKind.None)
            }),

        new BuiltInNode(
            "Debate",
            "Debate",
            "Puts two models in genuine disagreement about how to approach something, over several rounds, and sends on what they settled.",
            Array.Empty<string>(),
            _ => new DebateNode()),

        new BuiltInNode(
            "Judge",
            "Judge",
            "Reads what a debate settled, or two models arguing separately, and makes the determination.",
            Array.Empty<string>(),
            _ => new JudgeNode()),

        // Iteration already happens wherever a list meets a node. This is the same thing made
        // visible, and the only way to stop between items rather than only before or after them.
        new BuiltInNode(
            "Loop",
            "Loop",
            "Runs everything wired to it once per item in a list, saying which item it is on. Put a breakpoint on its wire to stop between items.",
            Array.Empty<string>(),
            _ => new LoopNode()),

        new BuiltInNode(
            "Reshape",
            "Reshape",
            "Reshapes the text passing through it. Inject standing instructions, keep the part you want, find and replace, trim, or run an expression.",
            new[] { "Patch", "Transform" },
            _ => new ReshapeNode()),

        new BuiltInNode(
            "CompilerCheck",
            "Compiler check",
            "Compiles the code and asks the model to fix whatever does not build.",
            new[] { "CompileCheck", "Compile" },
            factory => new CompilerCheckNode { RetryLimit = factory._config.DefaultRetryLimit }),

        // Not everything is a file. This is the end of a chain that answers a question rather
        // than writing one, so nothing about the write path is involved.
        new BuiltInNode(
            "TextOutput",
            "Text output",
            "Shows the reply so you can read and copy it. Writes nothing to disk.",
            Array.Empty<string>(),
            factory => new TextOutputNode(factory._dialogs)),

        new BuiltInNode(
            "Output",
            "Output",
            "Writes the finished files into your project.",
            Array.Empty<string>(),

            // Seeded from the project rather than from the constant, so a plain C# project stops
            // being handed Unity's folder. Nothing reaches back into a graph already saved: the
            // value belongs to the node and travels with it.
            factory => new OutputNode
            {
                TargetSubfolder = factory._project?.ScriptsFolder is { Length: > 0 } folder
                    ? folder
                    : OutputNode.DefaultSubfolder
            })
    };

    /// <summary>
    /// Every key that resolves to a built in type, current and historical, to the type it makes.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, BuiltInNode> ByKey = BuiltIn
        .SelectMany(node => node.FormerKeys.Prepend(node.TypeKey), (node, key) => (key, node))
        .ToDictionary(pair => pair.key, pair => pair.node, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every node type that can be added to a graph, in palette order.</summary>
    public static IReadOnlyList<NodeDescriptor> Descriptors { get; } = BuiltIn
        .Select(node => new NodeDescriptor(node.TypeKey, node.DisplayName, node.Description))
        .ToList();

    /// <summary>Creates a node of the given type, started from the application wide defaults.</summary>
    /// <remarks>
    /// Every key a node has ever saved itself under is accepted here, and the current key is the
    /// one written back. That is the whole of the migration, and it is not optional: a key this
    /// does not recognise is reported as an unknown type and the node is held as a placeholder, so
    /// a rename without this leaves somebody's graph with a hole where a node used to be. It has
    /// happened twice, once when the palette offered <c>Compile</c> while the node saved itself as
    /// <c>CompileCheck</c>, and again when Patch became Reshape and Debate and Judge arrived.
    ///
    /// Nothing is listed here any more. The keys come from the table above, which is also what the
    /// palette is built from, so there is no second list to forget to update.
    /// </remarks>
    /// <exception cref="NotSupportedException">The type key is not one this build has ever used.</exception>
    public NodeBase Create(string typeKey)
        => ByKey.TryGetValue(typeKey, out var builtIn)
            ? builtIn.Build(this)
            : CreateContributed(typeKey);

    /// <summary>Creates a node of the given type at a canvas position.</summary>
    public NodeBase Create(string typeKey, double x, double y)
    {
        var node = Create(typeKey);
        node.X = x;
        node.Y = y;
        return node;
    }
}
