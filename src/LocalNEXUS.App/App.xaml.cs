using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Compilation;
using LocalNEXUS.App.Services.Credentials;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.Distributed;
using LocalNEXUS.App.Services.Extensions;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.Services.Processes;
using LocalNEXUS.App.Services.ProjectIndex;
using LocalNEXUS.App.Services.Python;
using LocalNEXUS.App.Services.Theming;
using LocalNEXUS.App.ViewModels;
using LocalNEXUS.App.Views;

namespace LocalNEXUS.App;

/// <summary>
/// The composition root. Every service is constructed here once and handed to whoever needs it,
/// which keeps the rest of the application free of service location and static state.
/// </summary>
public partial class App : Application
{
    private ChildProcessGroup? _children;
    private LlamaServerManager? _llamaServers;
    private PythonRuntimeManager? _pythonRuntime;
    private ModelClientRouter? _modelClient;
    private MeshManager? _mesh;
    private CancellationTokenSource? _provisioning;
    private CancellationTokenSource? _indexing;
    private ViewModels.NetworkViewModel? _network;
    private Services.Extensions.ExtensionHost? _extensionHost;
    private Services.Dialogs.ExtensionsWindowService? _extensionsWindow;
    private Services.Credentials.DpapiCredentialStore? _credentials;

    /// <summary>
    /// The window's view model, held here rather than reached through the window.
    /// </summary>
    /// <remarks>
    /// Cleanup runs on the ProcessExit thread as well as on the dispatcher, and
    /// <c>Application.MainWindow</c> verifies thread access on the way in. Reading it from
    /// there threw on every ordinary exit and wrote a crash report for it, which is the worst
    /// place to spend a crash report: one that arrives every single time is one nobody reads
    /// when something has actually gone wrong.
    /// </remarks>
    private ViewModels.MainViewModel? _mainViewModel;
    private Services.History.RunHistoryStore? _history;
    private Services.Dialogs.IHistoryWindow? _historyWindow;
    private Services.Mcp.McpBridgeServer? _mcp;
    private Services.History.ConversationService? _conversation;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Every way this process can end, not just the tidy one. An engine process holds GPU
        // memory, so one left behind by a crash quietly degrades every run after it.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            Compose();
        }
        catch (Exception ex)
        {
            Report("Startup", ex);
            Shutdown(1);
        }
    }

    /// <summary>Builds the object graph and shows the window.</summary>
    private void Compose()
    {
        AppPaths.EnsureCreated();

        var config = AppConfig.LoadOrCreate();

        // The theme is applied before anything is constructed, so the window is painted in the
        // right palette from its first frame rather than flashing the default one.
        var themes = new ThemeService(config, Resources);
        themes.ApplySaved();

        // Written on first run so there is something to edit rather than something to invent.
        ModelPathsFile.EnsureCreated();

        var catalog = new ModelCatalog(config);
        catalog.Refresh();

        var graph = new GraphModel();
        var feed = new ActivityFeed(Dispatcher);
        var dialogs = new WindowsDialogService();

        // Nothing is opened here. The front door asks which project this session is for, because
        // opening whatever happened to be last is a guess about the one thing worth asking.
        var project = new ProjectService();
        var recents = new RecentProjectsService(config);

        // Owns every engine process this session starts. Built before anything can start one,
        // and given the chance to deal with anything a previous session failed to clean up.
        var children = new ChildProcessGroup();
        _children = children;
        ReportAbandonedProcesses(children, feed);

        var mesh = new MeshManager(config, feed, Dispatcher, children);
        _mesh = mesh;

        // Extensions are per project, so the registry starts empty and is pointed at a project
        // when one is opened. The host starts nothing here: extension processes are lazy, and an
        // install with a dozen of them has to cost nothing at launch.
        // Keys, encrypted for this Windows account. Built before anything that resolves an
        // endpoint, because a run cannot reach a hosted provider without it.
        var credentials = new DpapiCredentialStore(feed);
        _credentials = credentials;

        // What the current run has spent. One instance, reset when a run starts.
        var cost = new RunCostTracker();

        var extensions = new ExtensionRegistry(feed);
        extensions.OpenProject(project.ProjectPath);
        project.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProjectService.ProjectPath))
            {
                extensions.OpenProject(project.ProjectPath);
            }
        };
        var extensionHost = new ExtensionHost(children, feed);
        _extensionHost = extensionHost;

        // What the project has been told about itself, as opposed to what was guessed. Before the
        // factory, because a newly added Output node is seeded from it.
        var projectSettings = new Services.Files.ProjectSettingsService(feed);

        var factory = new NodeFactory(
            catalog, mesh, dialogs, config, extensions, extensionHost, credentials, projectSettings);
        var serializer = new GraphSerializer(factory);

        // Restoring the node is deliberately not awaited: composition must not block on a
        // child process, and the Network tab shows the node coming up on its own.
        _ = mesh.RestoreAsync();

        // The Python runtime has an environment to build before it can serve anything, so the
        // provisioner comes first and the runtime is handed the same instance the panel watches.
        var pythonEnvironment = new PythonProvisioner(children, feed, Dispatcher);

        _llamaServers = new LlamaServerManager(children);
        _pythonRuntime = new PythonRuntimeManager(children, pythonEnvironment);

        // Order is the order runtimes are asked, and each answers for exactly one format, so
        // adding a third changes this line and nothing else.
        var runtimes = new RuntimeResolver(_llamaServers, _pythonRuntime);

        // One router over three adapters. Everything upstream still asks for a completion
        // against an endpoint; which protocol answers is decided from the endpoint itself.
        _modelClient = new ModelClientRouter(
            new OpenAiCompatibleClient(),
            new AnthropicClient(),
            new GeminiClient());

        // Roslyn against the open project's own Unity references. The reference set is cached
        // behind this and rebuilt only when the project's compiled assemblies change.
        var compiler = new RoslynUnityCompiler(new UnityReferenceResolver());

        // Work a previous run left unfinished, read back from whichever project is open.
        var staging = new Services.Files.StagingStore(Dispatcher);
        staging.OpenProject(project.ProjectPath);

        // Every run this project has had. Opened without being awaited, because creating a
        // database is not something the window should wait behind, and nothing can be recorded
        // until a run starts anyway.
        var history = new Services.History.RunHistoryStore();
        _history = history;

        var recorder = new Services.History.RunRecorder(history, config);
        recorder.Attach(feed);

        // The conversation lives in the same database, so the transcript and the record are two
        // views of one thing rather than two copies that can drift.
        var conversation = new Services.History.ConversationService(history, Dispatcher);
        _conversation = conversation;

        // Not awaited: opening a database is not something the window should wait behind, and
        // nothing can be said or recorded until it is on screen anyway. The conversation is read
        // back after the store, because it reads from it.
        _ = OpenRecordAsync(history, conversation, project.ProjectPath);

        var historyWindow = new Services.Dialogs.HistoryWindowService();
        _historyWindow = historyWindow;

        // What the open project already contains. Built on demand rather than at startup, so a
        // session that never runs a graph never reads a project it was not asked about.
        var projectIndex = new ProjectIndexService();

        var services = new ExecutionServices(
            _modelClient,
            runtimes,
            mesh,
            compiler,
            projectIndex,
            project,
            new FileWriter(),
            feed,
            staging,
            history,
            conversation,
            extensions,
            new ToolSupportProbe(OpenAiCompatibleClient.CreateDefaultHttpClient()),
            credentials,
            cost)
        {
            CostWarningThreshold = config.CostWarningThreshold
        };
        // Web search, if this installation has a key for it. Built whatever the answer, because
        // the settings panel needs something to add a key to.
        var search = new Services.Search.WebSearchService(
            credentials, OpenAiCompatibleClient.CreateDefaultHttpClient());

        services.Search = search;

        // Reads an image into text before a run starts. Nothing in the graph knows it exists,
        // because what reaches the graph is the text it produced.
        // The resolver rather than llama.cpp by name, so a local vision model is served the same
        // way every other local model is. It starts on the first image, not at launch.
        var vision = new Services.Vision.VisionReader(
            config, credentials, OpenAiCompatibleClient.CreateDefaultHttpClient(), runtimes);

        var executor = new GraphExecutor(services);

        var feedViewModel = new ActivityFeedViewModel(executor, graph, feed, Dispatcher, cost, staging, recorder, conversation, history, search, vision);
        var catalogViewModel = new ModelCatalogViewModel(catalog, dialogs);
        var pythonViewModel = new PythonEnvironmentViewModel(pythonEnvironment, dialogs);
        var networkViewModel = new NetworkViewModel(mesh, catalog, config, feed, dialogs);
        _network = networkViewModel;

        // Reading the project again is the settings panel's business but the work belongs to the
        // application, so the panel is handed the same call the startup path uses rather than a
        // second one that could drift from it.
        var indexing = new CancellationTokenSource();
        _indexing = indexing;

        var extensionsViewModel = new ExtensionsViewModel(
            extensions,
            extensionHost,
            new ExtensionInstaller(children),
            new PrerequisiteChecker(),
            dialogs,
            new AddExtensionDialogService(),
            feed);

        var extensionsWindow = new ExtensionsWindowService();
        _extensionsWindow = extensionsWindow;

        var settingsViewModel = new AppSettingsViewModel(
            config,
            themes,
            catalog,
            catalogViewModel,
            pythonViewModel,
            networkViewModel,
            extensionsViewModel,
            new CloudProvidersViewModel(credentials, config, dialogs, feed),
            projectIndex,
            dialogs,
            () => IndexProjectAsync(projectIndex, project.ProjectPath, feed, indexing.Token));

        // The settings panel reports what the record holds, so it is pointed at the same one the
        // recorder writes to rather than at a second instance of nothing.
        settingsViewModel.UseHistory(history);
        settingsViewModel.UseProjectSettings(projectSettings);
        settingsViewModel.UseSearch(search);
        settingsViewModel.UseVision(vision);
        settingsViewModel.UseCredentials(credentials);

        var mainViewModel = new MainViewModel(
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
            projectIndex,
            themes,
            settingsViewModel,
            extensionsWindow,
            config,
            Dispatcher,
            compiler,
            history,
            historyWindow,
            services.Breakpoints,
            extensions,
            extensionHost,
            projectSettings,
            recents);

        // The MCP server, if this installation answers to other tools. Built whatever the setting
        // says so the toggle has something to start, and started only when it is on.
        var mcp = new Services.Mcp.McpBridgeServer(
            new Services.Mcp.McpToolSurface(new Services.Mcp.McpAppSurface(
                Dispatcher,
                project,
                projectIndex,
                graph,
                mainViewModel,
                feedViewModel,
                history,
                (path, token) =>
                {
                    // Everything a person's open does, minus the setup window. On the dispatcher
                    // because a tool call arrives on its own thread and restoring the project's
                    // graph replaces the contents of a collection the canvas is bound to, which is
                    // not something the framework marshals on anybody's behalf.
                    Dispatcher.Invoke(() =>
                    {
                        project.Open(path);
                        config.LastProjectPath = project.ProjectPath;
                        config.Save();

                        mainViewModel.OnProjectOpened(ViewModels.ProjectOpenedBy.Tool);
                    });

                    return IndexProjectAsync(projectIndex, project.ProjectPath, feed, token);
                })),
            feed);

        _mcp = mcp;
        settingsViewModel.UseMcpServer(mcp);

        if (config.McpServerEnabled)
        {
            mcp.Start();
        }

        ReportEnvironment(feed, catalog);

        _mainViewModel = mainViewModel;

        var window = new MainWindow { DataContext = mainViewModel };
        MainWindow = window;
        window.Show();

        // The first question, over the window rather than before it, so the application is already
        // there behind the thing it is asking. Nothing waits on it: every other piece of start up
        // carries on, and answering it opens a project the same way the File menu does.
        mainViewModel.FrontDoor.Show();

        // Deliberately not awaited. Building the Python environment is a download measured in
        // gigabytes, and the window has to be usable while it runs: GGUF models work throughout,
        // and the feed and the model panel show how far it has got.
        _provisioning = new CancellationTokenSource();
        _ = ProvisionPythonAsync(pythonEnvironment, _provisioning.Token);

        // The project index is read when a project is opened rather than when a graph is run, so
        // that what the application knows about the project is visible before anything depends on
        // it, and so the first run does not pay for it. Not awaited: reading a large project takes
        // long enough to notice and the window has to be usable throughout.
        _ = IndexProjectAsync(projectIndex, project.ProjectPath, feed, indexing.Token);

        project.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ProjectService.ProjectPath))
            {
                return;
            }

            projectIndex.Forget();
            _ = IndexProjectAsync(projectIndex, project.ProjectPath, feed, indexing.Token);
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        base.OnExit(e);
    }

    /// <summary>
    /// Releases everything this session owns. Called from every exit path, and safe to call more
    /// than once because more than one of those paths can run.
    /// </summary>
    private void Cleanup()
    {
        // Order matters: the managers stop their own work first, then the group confirms that
        // every process they started is actually gone and closes the job that guarantees it.
        _provisioning?.Cancel();
        _indexing?.Cancel();
        _mainViewModel?.Dispose();
        _history?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _network?.Dispose();
        // Before the group, so each extension is asked to stop and its connection closed rather
        // than every one of them being terminated cold.
        _extensionsWindow?.Close();
        _historyWindow?.Close();
        _extensionHost?.Dispose();
        _mesh?.Dispose();
        _llamaServers?.Dispose();
        _pythonRuntime?.Dispose();
        _modelClient?.Dispose();
        _children?.Dispose();
    }

    /// <summary>
    /// Reads what the open Unity project contains, in the background.
    /// </summary>
    /// <remarks>
    /// Reported to the feed with its timing, because how long this takes is the thing that decides
    /// whether the approach is usable and it should not be a number only a developer can see.
    /// </remarks>
    private static async Task IndexProjectAsync(
        ProjectIndexService index,
        string? projectPath,
        ActivityFeed feed,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return;
        }

        try
        {
            await index.EnsureAsync(projectPath, null, ct).ConfigureAwait(false);

            feed.Info(
                "Project index",
                $"{index.StatusText} {index.ReparsedCount} file(s) had to be read again; the rest came from the cache.");
        }
        catch (OperationCanceledException)
        {
            // The application is closing, or another project was opened.
        }
        catch (Exception ex)
        {
            CrashLog.Write("ProjectIndex", ex);
            feed.Info("Project index unavailable", ex.Message);
        }
    }

    /// <summary>
    /// Builds the Python environment in the background on every launch. A run that finds it
    /// already built verifies it and returns, so the cost after the first launch is one import.
    /// </summary>
    private static async Task ProvisionPythonAsync(PythonProvisioner provisioner, CancellationToken ct)
    {
        try
        {
            await provisioner.EnsureAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The application is closing. The child process group stops whatever uv had running.
        }
        catch (Exception ex)
        {
            CrashLog.Write("PythonProvisioning", ex);
        }
    }

    /// <summary>
    /// Opens a project's record and the conversation kept inside it, in that order.
    /// </summary>
    /// <remarks>
    /// One place, because the order matters and getting it wrong reads back an empty thread from
    /// a database that had not been opened yet.
    /// </remarks>
    private static async Task OpenRecordAsync(
        Services.History.RunHistoryStore history,
        Services.History.ConversationService conversation,
        string? projectPath)
    {
        await history.OpenProjectAsync(projectPath, CancellationToken.None).ConfigureAwait(false);

        if (history.IsOpen)
        {
            await conversation.OpenProjectAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes the two facts that decide whether a local run can work at all: whether the
    /// llama-server binary is present, and how many models were found.
    /// </summary>
    private static void ReportEnvironment(ActivityFeed feed, ModelCatalog catalog)
    {
        var executable = AppPaths.FindLlamaServerExecutable();

        if (executable is null)
        {
            feed.Info(
                "Local inference unavailable",
                $"{AppPaths.LlamaServerExecutableName} was not found. Place a llama.cpp build in vendor\\llama to run local models. OpenRouter nodes work without it.");
        }
        else
        {
            feed.Info("Local inference ready", executable);
        }

        feed.Info(
            "Model catalog",
            catalog.Models.Count == 0
                ? $"No models found. Drop one into {AppPaths.Models}, add a folder from a model node, or list a folder in {AppPaths.ModelPathsFile}."
                : $"{catalog.Models.Count} model(s) available.");

        ReportBundledFont(feed);

        // No longer load bearing. Fence stripping is a regular expression inside the model node
        // now, so a build without a script compiler loses one Reshape mode rather than losing the
        // thing the repair loop depends on.
        feed.Info(
            ReshapeNode.CanCompileScripts ? "Script mode ready" : "Script mode unavailable",
            ReshapeNode.CanCompileScripts
                ? "A Reshape node can run a C# expression for anything its four presets do not cover."
                : "The script compiler cannot be built into a single file executable, so a Reshape node has its "
                  + "four other modes and not this one. Nothing else is affected.");
    }

    /// <summary>
    /// Says whether the bundled monospace face actually loaded.
    /// </summary>
    /// <remarks>
    /// A font that fails to resolve does not throw; WPF quietly falls back to the next name in the
    /// family list and everything keeps working while looking different on every machine, which is
    /// exactly what bundling one was meant to prevent. Asking the question out loud is the only way
    /// to know, and it is the same path resolution gotcha the vendored binaries have, so it is
    /// worth reporting from the published exe rather than assumed from a development run.
    /// </remarks>
    private static void ReportBundledFont(ActivityFeed feed)
    {
        const string Expected = "JetBrains Mono";

        try
        {
            var loaded = Fonts
                .GetFontFamilies(new Uri("pack://application:,,,/"), "./Assets/Fonts/")
                .SelectMany(f => f.FamilyNames.Values)
                .Any(name => string.Equals(name, Expected, StringComparison.OrdinalIgnoreCase));

            feed.Info(
                loaded ? "Bundled font loaded" : "Bundled font unavailable",
                loaded
                    ? $"{Expected} is rendering paths, identifiers and diagnostics."
                    : $"{Expected} did not resolve from this build, so the monospace fallback is being used instead.");
        }
        catch (Exception ex) when (ex is IOException or UriFormatException or NotSupportedException)
        {
            feed.Info("Bundled font unavailable", ex.Message);
        }
    }

    /// <summary>
    /// Reports engine processes a previous session left behind. They have already been stopped by
    /// the time this runs; saying so is what tells the user why the machine had memory in use.
    /// </summary>
    private static void ReportAbandonedProcesses(ChildProcessGroup children, ActivityFeed feed)
    {
        var stopped = children.TerminateAbandoned();

        if (stopped > 0)
        {
            feed.Info(
                "Cleaned up after a previous session",
                $"{stopped} engine process(es) were still running from a session that did not close properly. They were stopped so this one starts from a clean machine.");
        }

        if (!children.HasKernelBackstop)
        {
            feed.Info(
                "Process cleanup is degraded",
                "Windows refused a job object, so engine processes are stopped explicitly but would survive this application being killed outright.");
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Report("Dispatcher", e.Exception);
        e.Handled = true;
    }

    /// <summary>
    /// A fault on a background thread ends the process whatever this handler does, so the only
    /// useful work here is recording it and stopping the children before it goes.
    /// </summary>
    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            CrashLog.Write("Unhandled", exception);
        }

        Cleanup();
    }

    /// <summary>
    /// A faulted task nobody awaited. It is observed here so it cannot bring the process down on
    /// its own, and recorded so the fault is not simply lost.
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CrashLog.Write("UnobservedTask", e.Exception);
        e.SetObserved();
    }

    /// <summary>The last point at which this process can still run code. Reached on paths that skip OnExit.</summary>
    private void OnProcessExit(object? sender, EventArgs e) => Cleanup();

    private static void Report(string context, Exception exception)
    {
        var logPath = CrashLog.Write(context, exception);

        var message = logPath is null
            ? exception.ToString()
            : $"{exception.Message}{Environment.NewLine}{Environment.NewLine}Full detail was written to:{Environment.NewLine}{logPath}";

        MessageBox.Show(message, "LocalNEXUS hit an unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
