using System.Windows;
using System.Windows.Threading;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Compilation;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.Distributed;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Extensions;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.History;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.Services.Processes;
using LocalNEXUS.App.Services.ProjectIndex;
using LocalNEXUS.App.Services.Python;
using LocalNEXUS.App.Services.Theming;
using LocalNEXUS.App.ViewModels;

namespace LocalNEXUS.Tests.Support;

/// <summary>
/// A real <see cref="MainViewModel"/>, assembled the way the application assembles one.
/// </summary>
/// <remarks>
/// The application wires itself by hand and there is no container to borrow, so this does the same.
/// It exists because some behaviour is only true of the whole shell: whether opening a project puts
/// a window in front of somebody is a decision the view model makes, and a test of the pieces
/// underneath it would not have caught it being made wrongly.
///
/// Nothing here reaches the network, the user's configuration or the user's data. The configuration
/// is a fresh instance, the recent projects list is given a save that does nothing, and no window
/// service opens a window.
/// </remarks>
public sealed class ShellHarness : IDisposable
{
    private readonly DispatcherLoop _loop;
    private readonly ChildProcessGroup _children;

    private ShellHarness(
        DispatcherLoop loop,
        ChildProcessGroup children,
        MainViewModel main,
        ProjectService project,
        AppConfig config,
        ActivityFeed feed,
        GraphModel graph,
        NodeFactory factory,
        GraphSerializer serializer,
        ExtensionHost extensionHost)
    {
        _loop = loop;
        _children = children;
        Main = main;
        Project = project;
        Config = config;
        Feed = feed;
        Graph = graph;
        Factory = factory;
        Serializer = serializer;
        ExtensionHost = extensionHost;
    }

    /// <summary>The shell, as the window binds to it.</summary>
    public MainViewModel Main { get; }

    /// <summary>The open project, which anything may open directly.</summary>
    public ProjectService Project { get; }

    /// <summary>A configuration that exists only for this test.</summary>
    public AppConfig Config { get; }

    /// <summary>The live transcript.</summary>
    public ActivityFeed Feed { get; }

    /// <summary>The canvas contents.</summary>
    public GraphModel Graph { get; }

    /// <summary>Builds nodes, so a test can put a real graph on disk.</summary>
    public NodeFactory Factory { get; }

    /// <summary>The same serializer the shell was given.</summary>
    public GraphSerializer Serializer { get; }

    /// <summary>
    /// Where extension workers run, which is the shell's to set when a project opens.
    /// </summary>
    /// <remarks>
    /// Read from the host rather than through the view model, because the view model has no reason
    /// to expose it and widening what the application offers to suit a test is the wrong way round.
    /// </remarks>
    public ExtensionHost ExtensionHost { get; }

    /// <summary>Builds one, on a dispatcher that pumps.</summary>
    public static ShellHarness Build()
    {
        var loop = new DispatcherLoop();

        return loop.Dispatcher.Invoke(() => Compose(loop));
    }

    /// <summary>
    /// Runs something on the harness's dispatcher, which is where the shell lives.
    /// </summary>
    /// <remarks>
    /// The application's view models are built and used on one thread and are entitled to assume
    /// it, so a test that poked at them from the test runner's thread would be testing something
    /// the application never does.
    /// </remarks>
    public T On<T>(Func<T> work) => _loop.Dispatcher.Invoke(work);

    /// <summary>Runs something on the harness's dispatcher.</summary>
    public void On(Action work) => _loop.Dispatcher.Invoke(work);

    private static ShellHarness Compose(DispatcherLoop loop)
    {
        var dispatcher = loop.Dispatcher;
        var children = new ChildProcessGroup();

        var config = new AppConfig();
        var feed = new ActivityFeed(dispatcher);
        var dialogs = new SilentDialogService();

        var catalog = new ModelCatalog(config);
        var mesh = new MeshManager(config, feed, dispatcher, children);
        var extensions = new ExtensionRegistry(feed);
        var host = new ExtensionHost(children, feed);
        var credentials = new InMemoryCredentialStore();

        var factory = new NodeFactory(catalog, mesh, dialogs, config, extensions, host, credentials);
        var serializer = new GraphSerializer(factory);

        var graph = new GraphModel();
        var project = new ProjectService();
        var index = new ProjectIndexService();
        var compiler = new RoslynUnityCompiler(new UnityReferenceResolver());
        var provisioner = new PythonProvisioner(children, feed, dispatcher);

        var runtimes = new RuntimeResolver(
            new LlamaServerManager(children),
            new PythonRuntimeManager(children, provisioner));

        var history = new RunHistoryStore();
        var staging = new StagingStore(dispatcher);
        var breakpoints = new BreakpointService(feed);

        var services = new ExecutionServices(
            new StubModelClient(),
            runtimes,
            mesh,
            compiler,
            index,
            project,
            new FileWriter(),
            feed,
            staging,
            history,
            null,
            extensions,
            null,
            credentials);

        var executor = new GraphExecutor(services);
        var feedViewModel = new ActivityFeedViewModel(executor, graph, feed, dispatcher);

        var catalogViewModel = new ModelCatalogViewModel(catalog, dialogs);
        var pythonViewModel = new PythonEnvironmentViewModel(provisioner, dialogs);
        var networkViewModel = new NetworkViewModel(mesh, catalog, config, feed, dialogs);
        var themes = new ThemeService(config, new ResourceDictionary());

        var extensionsViewModel = new ExtensionsViewModel(
            extensions,
            host,
            new ExtensionInstaller(children),
            new PrerequisiteChecker(),
            dialogs,
            new SilentAddExtensionDialog(),
            feed);

        var settings = new AppSettingsViewModel(
            config,
            themes,
            catalog,
            catalogViewModel,
            pythonViewModel,
            networkViewModel,
            extensionsViewModel,
            new CloudProvidersViewModel(credentials, config, dialogs, feed),
            index,
            dialogs,
            () => Task.CompletedTask);

        var projectSettings = new ProjectSettingsService(feed);
        var recents = new RecentProjectsService(config, () => { });

        var main = new MainViewModel(
            graph,
            factory,
            serializer,
            dialogs,
            feed,
            feedViewModel,
            catalogViewModel,
            pythonViewModel,
            networkViewModel,
            project,
            index,
            themes,
            settings,
            new SilentWindow(),
            config,
            dispatcher,
            compiler,
            history,
            new SilentWindow(),
            breakpoints,
            extensions,
            host,
            projectSettings,
            recents);

        return new ShellHarness(loop, children, main, project, config, feed, graph, factory, serializer, host);
    }

    public void Dispose()
    {
        _children.Dispose();
        _loop.Dispose();
    }
}

/// <summary>A window service that opens nothing, because a test has no screen.</summary>
internal sealed class SilentWindow : IExtensionsWindow, IHistoryWindow
{
    public void Show(object viewModel)
    {
    }

    public void Close()
    {
    }
}

/// <summary>An add dialog nobody answers.</summary>
internal sealed class SilentAddExtensionDialog : IAddExtensionDialog
{
    public AddExtensionRequest? Ask(AddExtensionMethod method) => null;
}
