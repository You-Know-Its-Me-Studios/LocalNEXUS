using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Services.Compilation;
using LocalNEXUS.App.Services.Credentials;
using LocalNEXUS.App.Services.Distributed;
using LocalNEXUS.App.Services.Extensions;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.ProjectIndex;

namespace LocalNEXUS.App.Services.Execution;

/// <summary>
/// The services a node may reach for while executing. Passing this one object keeps node
/// signatures stable as capabilities are added, and keeps nodes free of any knowledge of how
/// those services were constructed.
/// </summary>
public sealed class ExecutionServices
{
    public ExecutionServices(
        IModelClient modelClient,
        RuntimeResolver runtimes,
        MeshManager mesh,
        ICodeCompiler compiler,
        ProjectIndexService projectIndex,
        ProjectService project,
        FileWriter fileWriter,
        IActivityFeed feed,
        StagingStore? staging = null,
        History.RunHistoryStore? history = null,
        History.ConversationService? conversation = null,
        ExtensionRegistry? extensions = null,
        ToolSupportProbe? toolSupport = null,
        ICredentialStore? credentials = null,
        RunCostTracker? cost = null)
    {
        Extensions = extensions;
        Credentials = credentials;
        Cost = cost ?? new RunCostTracker();
        ModelClient = modelClient;

        // The same client the run uses, because what is being established is whether a tool call
        // survives the path a real request takes rather than whether a server says it would.
        ToolSupport = toolSupport ?? new ToolSupportProbe(modelClient);
        Runtimes = runtimes;
        Mesh = mesh;
        Compiler = compiler;
        ProjectIndex = projectIndex;
        Project = project;
        FileWriter = fileWriter;
        Staging = staging ?? new StagingStore();
        Breakpoints = new BreakpointService(feed);
        History = history ?? new History.RunHistoryStore();
        Conversation = conversation;
        Feed = feed;
    }

    /// <summary>Sends chat requests to local and cloud endpoints alike.</summary>
    public IModelClient ModelClient { get; }

    /// <summary>
    /// Serves models this machine runs on its own, on whichever local runtime the model's format
    /// calls for. Which one that is never reaches the node asking for it.
    /// </summary>
    public RuntimeResolver Runtimes { get; }

    /// <summary>This install's mesh node: what the network can serve, and where to send it.</summary>
    public MeshManager Mesh { get; }

    /// <summary>Answers whether a piece of code compiles against the open Unity project.</summary>
    public ICodeCompiler Compiler { get; }

    /// <summary>What the open Unity project already contains, so a run is not written blind.</summary>
    public ProjectIndexService ProjectIndex { get; }

    /// <summary>The Unity project that output nodes write into.</summary>
    public ProjectService Project { get; }

    /// <summary>Writes generated files to disk.</summary>
    public FileWriter FileWriter { get; }

    /// <summary>
    /// The work a run left unfinished, kept with the project.
    /// </summary>
    /// <remarks>
    /// A service rather than a node, which is what lets the executor ask whether a run ended with
    /// anything outstanding without learning that a node called Output exists.
    /// </remarks>
    public StagingStore Staging { get; }

    /// <summary>Holds a run on the wires somebody marked, and says what is passing along them.</summary>
    public BreakpointService Breakpoints { get; }

    /// <summary>
    /// Web search, when this run was sent with it turned on and a key exists.
    /// </summary>
    /// <remarks>
    /// Set after construction rather than taken as an argument, because the constructor is already
    /// fifteen parameters long and search is optional in a way none of the others are: an
    /// installation with no key never has one.
    /// </remarks>
    public Search.WebSearchService? Search { get; set; }

    /// <summary>
    /// The record of every run this project has had.
    /// </summary>
    /// <remarks>
    /// Reached by a node only to say what it did to a file and to keep a copy of what was there
    /// first. Reading the record is the history window's business, not a node's.
    /// </remarks>
    public History.RunHistoryStore History { get; }

    /// <summary>
    /// The running conversation, or null when nothing is driving one.
    /// </summary>
    /// <remarks>
    /// Reached by a node that has a question it cannot answer from the project. Awaiting the reply
    /// is what pauses the run, and it pauses the same way a confirmation does: this node's task
    /// has not returned. Nothing above it is involved.
    /// </remarks>
    public History.ConversationService? Conversation { get; }

    /// <summary>The extensions registered against the open project, or null when none is open.</summary>
    public ExtensionRegistry? Extensions { get; }

    /// <summary>Answers whether a model behind an endpoint can call tools.</summary>
    public ToolSupportProbe ToolSupport { get; }

    /// <summary>The API keys for hosted providers, or null when nothing configured one.</summary>
    public ICredentialStore? Credentials { get; }

    /// <summary>What this run has spent so far.</summary>
    public RunCostTracker Cost { get; }

    /// <summary>What a run may cost before it asks first. Zero switches the warning off.</summary>
    public decimal CostWarningThreshold { get; init; }

    /// <summary>The live transcript of the run.</summary>
    public IActivityFeed Feed { get; }
}
