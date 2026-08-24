using System.Windows.Threading;
using LocalNEXUS.App.Infrastructure;
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
using LocalNEXUS.App.Services.Processes;
using LocalNEXUS.App.Services.ProjectIndex;

namespace LocalNEXUS.Tests.Support;

/// <summary>
/// The application's services, assembled for a test with nothing that reaches the network or the
/// user's own data.
/// </summary>
/// <remarks>
/// The application wires itself by hand in <c>App.Compose</c> and there is no container to borrow,
/// so this does the same by hand. It is deliberately the real types rather than mocks of them: a
/// test of the executor should exercise the real graph, the real pins and the real nodes, and the
/// only thing worth replacing is the model, because a model is the one part that cannot answer the
/// same way twice.
///
/// The configuration is a fresh instance rather than the one on disk, and nothing here calls Save,
/// so running the suite cannot change what the application does next time somebody opens it.
/// </remarks>
public sealed class TestServices : IDisposable
{
    private readonly ChildProcessGroup _children;
    private readonly DispatcherLoop _loop;

    private TestServices(
        DispatcherLoop loop,
        ChildProcessGroup children,
        AppConfig config,
        ActivityFeed feed,
        StubModelClient models,
        NodeFactory factory,
        ExecutionServices services,
        ProjectService project,
        ProjectIndexService index)
    {
        _loop = loop;
        _children = children;
        Config = config;
        Feed = feed;
        Models = models;
        Factory = factory;
        Services = services;
        Project = project;
        Index = index;
    }

    /// <summary>A configuration that exists only for this test.</summary>
    public AppConfig Config { get; }

    /// <summary>The live transcript, which tests read to see what a node reported.</summary>
    public ActivityFeed Feed { get; }

    /// <summary>The scripted model. Queue replies on it before running anything.</summary>
    public StubModelClient Models { get; }

    /// <summary>Builds nodes, including from historical type keys.</summary>
    public NodeFactory Factory { get; }

    /// <summary>What a node reaches for while it runs.</summary>
    public ExecutionServices Services { get; }

    /// <summary>The open project, or none.</summary>
    public ProjectService Project { get; }

    /// <summary>What the open project contains.</summary>
    public ProjectIndexService Index { get; }

    /// <summary>Assembles a set, optionally pointed at a generated project.</summary>
    public static TestServices Create(SampleProject? project = null)
    {
        // A dispatcher on a thread that pumps. See DispatcherLoop for why this cannot be
        // Dispatcher.CurrentDispatcher.
        var loop = new DispatcherLoop();
        var dispatcher = loop.Dispatcher;
        var children = new ChildProcessGroup();

        var config = new AppConfig();
        var feed = new ActivityFeed(dispatcher);
        var models = new StubModelClient();

        var catalog = new ModelCatalog(config);
        var mesh = new MeshManager(config, feed, dispatcher, children);
        var dialogs = new SilentDialogService();
        var extensions = new ExtensionRegistry(feed);
        var host = new ExtensionHost(children, feed);
        var credentials = new InMemoryCredentialStore();

        var factory = new NodeFactory(catalog, mesh, dialogs, config, extensions, host, credentials);

        var projectService = new ProjectService();

        if (project is not null)
        {
            projectService.Open(project.Root);
        }

        var index = new ProjectIndexService();
        var compiler = new RoslynUnityCompiler(new UnityReferenceResolver());
        var python = new App.Services.Python.PythonProvisioner(children, feed, dispatcher);
        // The same three, in the same order, as App.Compose. Order is load bearing: both the
        // distributed runtime and the Python one answer for safetensors, and the distributed one
        // has to be asked first or it can never take a model. This list drifted out of step once
        // already, and the test that was supposed to catch it was asserting against this line
        // rather than against the application, so it stayed green while being wrong.
        var runtimes = new RuntimeResolver(
            new LlamaServerManager(children),
            new DistributedRuntimeManager(children, python, config),
            new PythonRuntimeManager(children, python));

        var services = new ExecutionServices(
            models,
            runtimes,
            mesh,
            compiler,
            index,
            projectService,
            new FileWriter(),
            feed,
            new StagingStore(dispatcher),
            new App.Services.History.RunHistoryStore(),
            null,
            extensions,
            null,
            credentials);

        return new TestServices(loop, children, config, feed, models, factory, services, projectService, index);
    }

    /// <summary>A context for running one node, as the executor would build it.</summary>
    public NodeExecutionContext ContextFor(App.Models.NodeBase node, App.Models.GraphModel graph, string request = "")
        => new(node, new RunContext(graph, request), Services);

    public void Dispose()
    {
        _children.Dispose();
        _loop.Dispose();
    }
}

/// <summary>A dialog service that answers nothing, because a test has nobody to ask.</summary>
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

    /// <summary>A test opens no browsers.</summary>
    public void OpenUrl(string url) => LastUrl = url;

    /// <summary>The last link offered, so a test can read it.</summary>
    public string? LastUrl { get; private set; }

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
/// A credential store that keeps keys in memory.
/// </summary>
/// <remarks>
/// The real one writes an encrypted file into the user's application data, and a test suite has no
/// business putting anything there. What the real one does with encryption is tested separately and
/// directly.
/// </remarks>
internal sealed class InMemoryCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, string> _keys = new(StringComparer.OrdinalIgnoreCase);

    public bool Has(string providerId) => _keys.ContainsKey(providerId);

    public string? Get(string providerId) => _keys.TryGetValue(providerId, out var key) ? key : null;

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
