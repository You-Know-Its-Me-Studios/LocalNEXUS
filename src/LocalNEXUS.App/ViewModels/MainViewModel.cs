using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.Files;
using System.Windows.Threading;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.Services.ProjectIndex;
using LocalNEXUS.App.Services.Theming;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The window: the shell around everything else, and the graph document inside it.
/// </summary>
/// <remarks>
/// The shell is an activity bar down the side, a side bar that doubles as the run outline, a
/// tabbed editor area, a tabbed bottom panel and a status bar. This view model owns which of
/// those are showing and which primary section the activity bar has selected; the parts
/// themselves belong to the view models they draw.
///
/// The right hand inspector is one slot rather than one per section, which is why
/// <see cref="InspectorContent"/> exists. It answers "what can I do about the selected thing"
/// wherever the selected thing came from, and WPF picks the panel by the type of whatever it
/// returns. A second inspector would drift from the first the first time either changed.
///
/// Canvas selection is tracked through each node's own <see cref="NodeBase.IsSelected"/> flag,
/// which the item container binds two way. That keeps the inspector in step with the canvas
/// without the view model reaching into the visual tree.
/// </remarks>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private const double CascadeStep = 36d;

    private readonly NodeFactory _factory;
    private readonly GraphSerializer _serializer;
    private readonly IDialogService _dialogs;
    private readonly ActivityFeed _feed;
    private readonly AppConfig _config;
    private readonly Services.Compilation.ICodeCompiler _compiler;
    private readonly Services.History.RunHistoryStore _history;
    private readonly IHistoryWindow _historyWindow;
    private readonly GraphTemplates _templates;
    private readonly Services.Extensions.ExtensionHost _extensionHost;
    private readonly IExtensionsWindow _extensionsWindow;

    /// <summary>Nodes whose selection state this view model is currently following.</summary>
    private readonly HashSet<NodeBase> _observedNodes = new();

    private int _cascadeIndex;
    private bool _disposed;

    /// <summary>The node whose settings the inspector is showing, or null when nothing is selected.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectionCommand))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(CanDeleteSelection))]
    [NotifyPropertyChangedFor(nameof(InspectorContent))]
    [NotifyPropertyChangedFor(nameof(InspectorHeader))]
    private NodeBase? _selectedNode;

    /// <summary>Path of the graph currently open, or null when it has never been saved.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyPropertyChangedFor(nameof(TitleText))]
    private string? _currentGraphPath;

    /// <summary>Which primary view the activity bar has selected.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkspace))]
    [NotifyPropertyChangedFor(nameof(IsNetwork))]
    [NotifyPropertyChangedFor(nameof(CanDeleteSelection))]
    [NotifyCanExecuteChangedFor(nameof(AddNodeCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewGraphCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveGraphCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadGraphCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePanelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowPanelTabCommand))]
    [NotifyPropertyChangedFor(nameof(IsPanelShowing))]
    [NotifyPropertyChangedFor(nameof(PanelRowHeight))]
    [NotifyPropertyChangedFor(nameof(InspectorContent))]
    [NotifyPropertyChangedFor(nameof(InspectorHeader))]
    [NotifyPropertyChangedFor(nameof(TitleText))]
    [NotifyPropertyChangedFor(nameof(StatusSummary))]
    [NotifyPropertyChangedFor(nameof(LeftPanel))]
    [NotifyPropertyChangedFor(nameof(RightPanel))]
    private PrimarySection _activeSection = PrimarySection.Workspace;

    /// <summary>True while the settings panel is covering the primary view.</summary>
    [ObservableProperty]
    private bool _isSettingsOpen;

    /// <summary>The Explorer, on the left of the Workspace.</summary>
    public CollapsiblePanelViewModel WorkspaceExplorer { get; } = new(PanelSide.Left, "the Explorer");

    /// <summary>The inspector, on the right of the Workspace.</summary>
    public CollapsiblePanelViewModel WorkspaceInspector { get; } = new(PanelSide.Right, "the inspector");

    /// <summary>The filter rail, on the left of the Network.</summary>
    public CollapsiblePanelViewModel NetworkFilters { get; } = new(PanelSide.Left, "the filters");

    /// <summary>The details pane, on the right of the Network.</summary>
    public CollapsiblePanelViewModel NetworkDetails { get; } = new(PanelSide.Right, "the details");

    /// <summary>Which tab of the bottom panel is showing.</summary>
    [ObservableProperty]
    private BottomPanelTab _panelTab = BottomPanelTab.Activity;

    /// <summary>True while the bottom panel is expanded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPanelShowing))]
    [NotifyPropertyChangedFor(nameof(PanelRowHeight))]
    private bool _isPanelVisible = true;

    /// <summary>The height the panel was last dragged to, remembered across being hidden.</summary>
    private GridLength _panelHeight = new(240d);

    public MainViewModel(
        GraphModel graph,
        NodeFactory factory,
        GraphSerializer serializer,
        IDialogService dialogs,
        ActivityFeed feed,
        ActivityFeedViewModel feedViewModel,
        ModelCatalogViewModel catalog,
        PythonEnvironmentViewModel pythonEnvironment,
        NetworkViewModel network,
        ProjectService project,
        ProjectIndexService projectIndex,
        ThemeService themes,
        AppSettingsViewModel settings,
        IExtensionsWindow extensionsWindow,
        AppConfig config,
        Dispatcher dispatcher,
        Services.Compilation.ICodeCompiler compiler,
        Services.History.RunHistoryStore history,
        IHistoryWindow historyWindow,
        Services.Execution.BreakpointService breakpoints,
        Services.Extensions.ExtensionRegistry extensions,
        Services.Extensions.ExtensionHost extensionHost,
        ProjectSettingsService projectSettings,
        RecentProjectsService recents,
        Services.Inference.LlamaServerManager? servers = null)
    {
        _extensionHost = extensionHost;
        Breakpoints = breakpoints;
        _compiler = compiler;
        _history = history;
        _historyWindow = historyWindow;
        _extensionsWindow = extensionsWindow;
        Graph = graph;
        _factory = factory;
        _serializer = serializer;
        _dialogs = dialogs;
        _feed = feed;
        _config = config;

        Feed = feedViewModel;
        Catalog = catalog;
        PythonEnvironment = pythonEnvironment;
        Network = network;
        Project = project;
        ProjectIndex = projectIndex;
        Themes = themes;
        Settings = settings;
        NodeSearch = new NodeSearchViewModel(factory, PlaceSearchedNode);

        // The tab, and the one thing it can do to the Workspace: put text in the request box. It
        // arrives the way anything typed arrives, so nothing in the Workspace knows or cares that
        // a tab sent it.
        ProjectSettings = projectSettings;
        ProjectSetup = new ProjectSetupViewModel(projectSettings, catalog.Models);

        // Asked before anything else, and answered from the recent list nearly every time. It is
        // handed the same open path the File menu uses rather than a second one, so a project
        // opened from the door and one opened from the menu are the same event.
        FrontDoor = new FrontDoorViewModel(
            recents,
            project,
            dialogs,
            config,
            OpenProjectFolder,
            () => ShowSection(PrimarySection.Network));

        Spec = new SpecViewModel(extensions, extensionHost, feed, text =>
        {
            Feed.RequestText = text;
            ShowSection(PrimarySection.Workspace);
        });

        extensions.Extensions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsSpecAvailable));
        _templates = new GraphTemplates(factory, serializer);

        PendingConnection = new PendingConnectionViewModel(
            graph,
            message => _feed.Error("Connection refused", message),
            OpenSearchFromPin);

        Document = new GraphDocumentViewModel(graph, () => Feed.RunState, dispatcher);
        Problems = new ProblemsViewModel(graph);

        NodePalette = NodeFactory.Descriptors
            .Select(d => new PaletteItemViewModel(d.TypeKey, d.DisplayName, d.Description, AddNodeCommand))
            .ToList();

        // One subscription for the whole window rather than one per node. A node that has been
        // removed from the graph simply stops being enumerated, where a node subscribing for itself
        // would outlive the graph it belonged to.
        if (servers is not null)
        {
            servers.StateChanged += () => dispatcher.InvokeAsync(RefreshModelStates);
        }

        Graph.Nodes.CollectionChanged += OnNodesChanged;
        Graph.Connections.CollectionChanged += OnConnectionsChanged;
        Project.PropertyChanged += OnProjectChanged;
        Feed.PropertyChanged += OnFeedChanged;
        Network.PropertyChanged += OnNetworkChanged;
        Document.PropertyChanged += OnDocumentChanged;

        Walkthrough = new WalkthroughViewModel(
            config,
            project,
            catalog.Models,
            graph,
            OpenProjectCommand,
            OpenSettingsCommand,
            ApplyTemplateCommand,
            Templates);

        // The graph handed in is normally empty, but it does not have to be.
        ResyncNodeSubscriptions();
        RefreshCompilerReachability();
    }

    /// <summary>The document on the canvas.</summary>
    public GraphModel Graph { get; }

    /// <summary>The bottom panel.</summary>
    public ActivityFeedViewModel Feed { get; }

    /// <summary>Catalog commands used by the model node settings panel.</summary>
    public ModelCatalogViewModel Catalog { get; }

    /// <summary>State of the Python runtime, shown in the same panel as the model list.</summary>
    public PythonEnvironmentViewModel PythonEnvironment { get; }

    /// <summary>The Network tab: available models, coverage, sources and contribution.</summary>
    public NetworkViewModel Network { get; }

    /// <summary>The project that output nodes write into.</summary>
    public ProjectService Project { get; }

    /// <summary>A run held on a wire, and what it is holding.</summary>
    public Services.Execution.BreakpointService Breakpoints { get; }

    /// <summary>The search that places a node, opened from the canvas rather than from a menu.</summary>
    public NodeSearchViewModel NodeSearch { get; }

    /// <summary>
    /// The graphs somebody can start from, rebuilt each time it is read.
    /// </summary>
    /// <remarks>
    /// Not cached, because the saved ones are files and somebody can add one while the application
    /// is open. It is a list of a handful read from a folder, once, when a menu is dropped.
    /// </remarks>
    public IReadOnlyList<GraphTemplate> Templates => _templates.All();

    /// <summary>True when the canvas has nothing on it, which is what the empty state is drawn from.</summary>
    public bool IsCanvasEmpty => Graph.Nodes.Count == 0;

    /// <summary>
    /// True when somebody has said they are starting from nothing.
    /// </summary>
    /// <remarks>
    /// The templates are a suggestion and a suggestion has to be refusable. Without this the only
    /// way past them is to accept one or to add a node, which is the canvas telling somebody what
    /// they are allowed to do next.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTemplates))]
    private bool _templatesDismissed;

    /// <summary>True when the empty canvas should be offering the templates.</summary>
    public bool ShowTemplates => IsCanvasEmpty && !TemplatesDismissed;

    /// <summary>Puts the templates away and leaves an empty canvas.</summary>
    [RelayCommand]
    private void DismissTemplates() => TemplatesDismissed = true;

    /// <summary>The first run checklist, which suggests and never blocks.</summary>
    public WalkthroughViewModel Walkthrough { get; }

    /// <summary>The Spec tab, which is only reachable when its extension is installed.</summary>
    public SpecViewModel Spec { get; }

    /// <summary>The questions asked the first time a project is opened.</summary>
    public ProjectSetupViewModel ProjectSetup { get; }

    /// <summary>The question asked before anything else: what are you working on.</summary>
    public FrontDoorViewModel FrontDoor { get; }

    /// <summary>What this project has been told about itself.</summary>
    public ProjectSettingsService ProjectSettings { get; }

    /// <summary>What the open project contains, shown under the explorer.</summary>
    public ProjectIndexService ProjectIndex { get; }

    /// <summary>The theme the window is wearing.</summary>
    public ThemeService Themes { get; }

    /// <summary>The settings panel, opened by the gear at the bottom of the activity bar.</summary>
    public AppSettingsViewModel Settings { get; }

    /// <summary>The open graph: its editor tab, and its nodes as the run outline shows them.</summary>
    public GraphDocumentViewModel Document { get; }

    /// <summary>Compiler diagnostics from the graph, for the Problems tab.</summary>
    public ProblemsViewModel Problems { get; }

    /// <summary>The wire currently being dragged.</summary>
    public PendingConnectionViewModel PendingConnection { get; }

    /// <summary>
    /// The node types a menu can offer, each carrying the command that adds one.
    /// </summary>
    /// <remarks>
    /// Built once from the factory's own list, so a new node type is still one entry there and
    /// nothing here. The command is on the item because these are shown from a context menu as
    /// well as from the menu bar, and a popup is its own visual tree to bind out of.
    /// </remarks>
    public IReadOnlyList<PaletteItemViewModel> NodePalette { get; }

    /// <summary>True when a node is selected.</summary>
    public bool HasSelection => SelectedNode is not null;

    /// <summary>True when there is a selection on a canvas that is actually showing.</summary>
    public bool CanDeleteSelection => HasSelection && IsWorkspace;

    /// <summary>
    /// True while the activity bar has the Workspace selected. Settable, so the activity bar
    /// expresses the choice as a radio button being chosen rather than as a command hanging off
    /// one, which is what makes the keyboard and the narrator work in it without being asked to.
    /// </summary>
    public bool IsWorkspace
    {
        get => ActiveSection == PrimarySection.Workspace;
        set
        {
            if (value)
            {
                ShowSection(PrimarySection.Workspace);
            }
        }
    }

    /// <summary>True while the activity bar has the Network selected.</summary>
    public bool IsNetwork
    {
        get => ActiveSection == PrimarySection.Network;
        set
        {
            if (value)
            {
                ShowSection(PrimarySection.Network);
            }
        }
    }

    /// <summary>True while the activity bar has the Spec tab selected.</summary>
    public bool IsSpec
    {
        get => ActiveSection == PrimarySection.Spec;
        set
        {
            if (value)
            {
                ShowSection(PrimarySection.Spec);
            }
        }
    }

    /// <summary>
    /// True when an extension that brings a tab is installed and usable.
    /// </summary>
    /// <remarks>
    /// The tab is hidden rather than disabled when this is false. A greyed out tab is a promise
    /// that something will happen if you find the right thing to click, and there is nothing behind
    /// this one until the extension is there.
    /// </remarks>
    public bool IsSpecAvailable => Spec.Installed() is not null;

    /// <summary>
    /// Whatever the right hand inspector should be showing: the selected node in the Workspace,
    /// and whichever machine, model or coverage section is selected in the Network.
    /// </summary>
    /// <remarks>
    /// One slot, one meaning. The view picks the panel from the type of what comes back, so a new
    /// kind of selectable thing is a data template and nothing else, and the inspector cannot
    /// drift between the two sections because there is only one of it.
    /// </remarks>
    public object? InspectorContent => ActiveSection == PrimarySection.Workspace
        ? SelectedNode
        : Network.InspectorTarget;

    /// <summary>What the top of the inspector says about whatever is selected.</summary>
    public InspectorHeader InspectorHeader => InspectorHeader.For(InspectorContent);

    /// <summary>
    /// The left panel of whichever tab is in front, and the right one.
    /// </summary>
    /// <remarks>
    /// The window binds to these rather than to the four, so the layout is written once for two
    /// slots instead of twice for four. Each tab keeps its own pair because the slots hold
    /// different things on each: collapsing the filters to widen the peer table says nothing about
    /// whether the Explorer should be out of the way on the canvas.
    /// </remarks>
    public CollapsiblePanelViewModel LeftPanel
        => ActiveSection == PrimarySection.Workspace ? WorkspaceExplorer : NetworkFilters;

    /// <summary>The right panel of whichever tab is in front.</summary>
    public CollapsiblePanelViewModel RightPanel
        => ActiveSection == PrimarySection.Workspace ? WorkspaceInspector : NetworkDetails;

    /// <summary>
    /// True when the bottom panel and the chat box are showing. Both belong to the Workspace: the
    /// Network has no run to transcribe and nothing to type a request at, so the space goes to the
    /// table instead of to an empty transcript.
    /// </summary>
    public bool IsPanelShowing => IsPanelVisible && IsWorkspace;

    /// <summary>
    /// The height of the panel row. Bound two way so the splitter writes the dragged height back
    /// here, which is what lets hiding the panel collapse the row to nothing and showing it again
    /// return to the size it was left at rather than to a default.
    /// </summary>
    public GridLength PanelRowHeight
    {
        get => IsPanelShowing ? _panelHeight : GridLength.Auto;
        set
        {
            if (!IsPanelShowing || value.IsAuto || value.Value <= 0d)
            {
                return;
            }

            _panelHeight = value;
        }
    }

    /// <summary>Text for the window title bar.</summary>
    public string WindowTitle => $"{TitleText} - LocalNEXUS";

    /// <summary>
    /// What the middle of the title bar says: the document, the project it writes into, and the
    /// application, in that order, because that is the order of how specific each one is.
    /// </summary>
    public string TitleText
    {
        get
        {
            if (ActiveSection == PrimarySection.Network)
            {
                return "Network";
            }

            var graphName = Document.Name;

            return Project.ProjectName is { } project
                ? $"{graphName} - {project}"
                : graphName;
        }
    }

    /// <summary>
    /// The right hand end of the status bar: what the section currently showing amounts to.
    /// </summary>
    public string StatusSummary => ActiveSection == PrimarySection.Network
        ? Network.CoverageSummary
        : Document.SummaryText;

    /// <summary>Switches the primary view, which is what the activity bar icons do.</summary>
    [RelayCommand]
    private void ShowSection(PrimarySection section)
    {
        ActiveSection = section;
        IsSettingsOpen = false;

        // The run controls belong to the canvas, so they go quiet when it is not showing.
        Feed.IsActive = IsWorkspace;
    }

    /// <summary>Opens the settings panel over whichever section is showing.</summary>
    [RelayCommand]
    private void OpenSettings() => IsSettingsOpen = true;

    /// <summary>
    /// Opens the extensions window.
    /// </summary>
    /// <remarks>
    /// A window rather than a settings page, and not modal, so a graph that needs an extension
    /// can be wired while looking at what the extension offers.
    /// </remarks>
    [RelayCommand]
    private void OpenExtensions() => _extensionsWindow.Show(Settings.Extensions);

    /// <summary>
    /// Opens the run history, which is a window rather than a panel.
    /// </summary>
    /// <remarks>
    /// The view model is built here and handed over each time, so the window opens onto whatever
    /// the record holds now rather than onto whatever it held when the application started.
    /// </remarks>
    [RelayCommand]
    private void OpenHistory()
    {
        var history = new HistoryViewModel(_history, _feed);

        // Taking a past request back is the half of undo that leaves the files alone.
        history.RequestReused += request => Feed.RequestText = request;

        _historyWindow.Show(history);
        _ = history.RefreshCommand.ExecuteAsync(null);
    }

    /// <summary>Closes the settings panel, landing back where the work was left.</summary>
    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;

    /// <summary>Shows or hides the left panel of whichever tab is in front.</summary>
    [RelayCommand]
    private void ToggleSideBar() => LeftPanel.Toggle();

    /// <summary>Shows or hides the right panel of whichever tab is in front.</summary>
    [RelayCommand]
    private void ToggleInspector() => RightPanel.Toggle();

    /// <summary>Shows or hides the bottom panel.</summary>
    [RelayCommand(CanExecute = nameof(IsWorkspace))]
    private void TogglePanel() => IsPanelVisible = !IsPanelVisible;

    /// <summary>Brings one tab of the bottom panel forward, expanding the panel if it was collapsed.</summary>
    [RelayCommand(CanExecute = nameof(IsWorkspace))]
    private void ShowPanelTab(BottomPanelTab tab)
    {
        PanelTab = tab;
        IsPanelVisible = true;
    }

    /// <summary>Adds a node of the given type at the cursor.</summary>
    [RelayCommand(CanExecute = nameof(IsWorkspace))]
    private void AddNode(string? typeKey)
    {
        if (string.IsNullOrWhiteSpace(typeKey))
        {
            return;
        }

        try
        {
            var location = NextNodeLocation();
            var node = _factory.Create(typeKey, location.X, location.Y);
            Graph.AddNode(node);
            SelectOnly(node);
        }
        catch (NotSupportedException ex)
        {
            _dialogs.ShowError("Node not added", ex.Message);
        }
    }

    /// <summary>
    /// Places a node the search chose, and wires it back when the search came from a pin.
    /// </summary>
    /// <remarks>
    /// The wiring is done through the same validator everything else uses, so a node offered by
    /// the search cannot land and then refuse to connect. The search only offers types with a
    /// reachable pin, so a refusal here would mean the two disagreed, which is worth saying rather
    /// than swallowing.
    /// </remarks>
    private void PlaceSearchedNode(string typeKey, double x, double y, Pin? from)
    {
        NodeBase node;

        try
        {
            node = _factory.Create(typeKey, x, y);
        }
        catch (NotSupportedException ex)
        {
            _dialogs.ShowError("Node not added", ex.Message);
            return;
        }

        Graph.AddNode(node);
        SelectOnly(node);

        if (from is null)
        {
            return;
        }

        var landing = from.Direction == PinDirection.Output ? node.Inputs : node.Outputs;

        foreach (var pin in landing)
        {
            var (output, input) = from.Direction == PinDirection.Output ? (from, pin) : (pin, from);

            if (Graph.TryConnect(output, input, out _))
            {
                return;
            }
        }

        _feed.Error(
            "Node added without a wire",
            $"{node.Title} was offered as somewhere {from.Owner.Title}.{from.Name} could go, and then no "
            + "pin on it would take the connection.");
    }

    /// <summary>
    /// Replaces the canvas with a template.
    /// </summary>
    /// <remarks>
    /// The same confirmation an ordinary open gets, because this discards the canvas exactly as
    /// opening a file does and a template is the thing somebody reaches for when they are not yet
    /// sure what they are doing.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(IsWorkspace))]
    private void ApplyTemplate(GraphTemplate? template)
    {
        if (template is null)
        {
            return;
        }

        try
        {
            var warnings = _templates.Apply(template, Graph);

            SelectedNode = null;
            CurrentGraphPath = null;
            Document.MarkSaved(null);
            _cascadeIndex = 0;

            // After marking it saved, not before. Marking a document saved is also what names it,
            // and with no path it names it untitled, so a template applied and then marked came out
            // called untitled however carefully the template had named it. The history records the
            // graph a run ran, so this was every template run filed under the same name.
            Graph.Name = template.Name;

            _feed.Info(
                $"Started from {template.Name}",
                template.Description + " Choose a model on each Model node, then type a request.");

            foreach (var warning in warnings)
            {
                _feed.Error("Part of the template did not open", warning);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            _dialogs.ShowError("Template not opened", ex.Message);
        }
    }

    /// <summary>
    /// Saves what is on the canvas as a template to start from later.
    /// </summary>
    /// <remarks>
    /// Through the ordinary save dialog pointed at the templates folder rather than a box asking
    /// for a name, so the name and the place it goes are the same question and somebody who wants
    /// it somewhere else can say so.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanSaveAsTemplate))]
    private void SaveAsTemplate()
    {
        Directory.CreateDirectory(GraphTemplates.Folder);

        var chosen = _dialogs.PickSaveFile(
            "Save this graph as a template",
            Graph.Name + GraphSerializer.FileExtension,
            $"LocalNEXUS graph|*{GraphSerializer.FileExtension}",
            GraphTemplates.Folder);

        if (chosen is null)
        {
            return;
        }

        try
        {
            _serializer.Save(Graph, chosen);

            _feed.Info(
                "Saved as a template",
                Path.GetDirectoryName(chosen)?.Equals(GraphTemplates.Folder, StringComparison.OrdinalIgnoreCase) == true
                    ? $"{Path.GetFileName(chosen)} is now on the File menu under Start from."
                    : $"Written to {chosen}. It is outside the templates folder, so it will not appear on the File menu.");

            OnPropertyChanged(nameof(Templates));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError("Template not saved", ex.Message);
        }
    }

    private bool CanSaveAsTemplate() => IsWorkspace && Graph.Nodes.Count > 0;

    /// <summary>
    /// Everything opening a project does: its own settings, where its extensions run, the recent
    /// list, the graph it was last working on, and the first open questions.
    /// </summary>
    /// <remarks>
    /// The asking happens once per project per machine and nothing waits on it. Somebody who
    /// dismisses it has the defaults, which is the state every project was in before this existed.
    ///
    /// Everything but the asking happens whoever opened it. That is the whole of what
    /// <see cref="ProjectOpenedBy"/> decides: a project opened by a tool call gets its settings,
    /// its extension host path and its graph, and does not get a window.
    /// </remarks>
    public void OnProjectOpened(ProjectOpenedBy who)
    {
        ProjectSettings.Open(Project.ProjectPath, Project.Kind);

        // Workers run in the project they exist for. Set here rather than at start up, because a
        // project can be opened and changed at any point in a session.
        _extensionHost.ProjectPath = Project.ProjectPath;

        // The door has been answered, however it was answered.
        FrontDoor.NoteProjectOpened();

        RestoreProjectGraph();

        // The one step that needs somebody there. A tool call opens the project with its defaults
        // and leaves the questions for whenever a person opens it, which is what stops a window
        // appearing on top of whatever they were actually doing. Nothing is recorded as answered,
        // so it is still asked the first time somebody opens it themselves.
        if (who == ProjectOpenedBy.Person && ProjectSettings.NeedsSetUp)
        {
            ProjectSetup.Open(Project.ProjectName ?? "this project", Project.Kind);
        }
    }

    /// <summary>
    /// Puts back the graph this project was last working on, or clears the canvas.
    /// </summary>
    /// <remarks>
    /// Per project rather than per installation, which is the whole of the fix. A graph names one
    /// project's files and reaches for one project's default model, so restoring the last one
    /// opened anywhere meant opening a graph belonging to a different project and finding out when
    /// it wrote somewhere unexpected.
    ///
    /// Clearing is the other half. Switching projects with the previous one's graph still on the
    /// canvas is the same mistake arrived at from the other direction.
    /// </remarks>
    private void RestoreProjectGraph()
    {
        var path = ProjectSettings.LastGraphPath;

        if (path is { Length: > 0 } && File.Exists(path))
        {
            LoadGraphFrom(path);
            return;
        }

        if (path is { Length: > 0 })
        {
            _feed.Info("Last graph not found", $"{path} is no longer there, so the canvas was left empty.");
            ProjectSettings.LastGraphPath = string.Empty;
            ProjectSettings.Save();
        }

        Graph.Clear();
        SelectedNode = null;
        CurrentGraphPath = null;
        Document.MarkSaved(null);
        TemplatesDismissed = false;
    }

    /// <summary>
    /// Tells every Model node to redraw what its model is doing.
    /// </summary>
    /// <remarks>
    /// A model starting, becoming ready or being restarted is a thing the node should show without
    /// anybody asking, and the only component that knows is the one that owns the servers. It says
    /// so once and this hands it to whichever nodes are on the canvas.
    /// </remarks>
    private void RefreshModelStates()
    {
        foreach (var node in Graph.Nodes.OfType<ModelNode>())
        {
            node.RefreshLoadState();
        }
    }

    /// <summary>Opens the search where a wire was let go over empty canvas.</summary>
    private void OpenSearchFromPin(Pin source)
        => NodeSearch.OpenFrom(source, LastCanvasPoint.X, LastCanvasPoint.Y);

    /// <summary>
    /// Where the pointer last was on the canvas, in the coordinates nodes are positioned in.
    /// </summary>
    /// <remarks>
    /// Written by the behaviour that watches the canvas, because a released wire arrives here as a
    /// pin and nothing else: the command carries no position, and the view model has no way to ask
    /// where the pointer is.
    /// </remarks>
    public Point LastCanvasPoint { get; set; }

    /// <summary>Removes every selected node together with its wires.</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteSelection))]
    private void DeleteSelection()
    {
        foreach (var node in Graph.Nodes.Where(n => n.IsSelected).ToList())
        {
            Graph.RemoveNode(node);
        }

        SelectedNode = null;
    }

    /// <summary>Removes every wire attached to a pin. Invoked by the canvas when a connector is detached.</summary>
    [RelayCommand]
    private void DisconnectPin(Pin? pin)
    {
        if (pin is not null)
        {
            Graph.DisconnectPin(pin);
        }
    }

    /// <summary>
    /// Turns a breakpoint on a wire on or off.
    /// </summary>
    /// <remarks>
    /// Deliberately unconditional. There is no run in progress requirement, because the ordinary
    /// way to set one is on an idle graph before pressing run, and a graph that had to be running
    /// before it could be marked would be a graph nobody could debug from the start.
    /// </remarks>
    [RelayCommand]
    private void ToggleBreakpoint(Connection? connection)
    {
        if (connection is null)
        {
            return;
        }

        connection.HasBreakpoint = !connection.HasBreakpoint;

        _feed.Info(
            connection.HasBreakpoint ? "Breakpoint set" : "Breakpoint cleared",
            $"{connection.Source.Owner.Title}.{connection.Source.Name} to "
            + $"{connection.Target.Owner.Title}.{connection.Target.Name}"
            + (connection.HasBreakpoint ? ". The run will stop here and show what is passing." : "."));

        Document.MarkChanged();
    }

    /// <summary>Removes a single wire. Invoked by the canvas when a connection is cut.</summary>
    [RelayCommand]
    private void RemoveConnection(Connection? connection)
    {
        if (connection is not null)
        {
            Graph.RemoveConnection(connection);
        }
    }

    /// <summary>
    /// Opens a project folder, works out what sort it is, and remembers it for the next session.
    /// </summary>
    /// <remarks>
    /// What was detected is said in the feed rather than left to be inferred, because whether the
    /// Unity write rules are in force changes what the application will refuse, and somebody who
    /// cannot tell which mode they are in cannot tell whether a refusal was right.
    /// </remarks>
    [RelayCommand]
    private void OpenProject()
    {
        var folder = _dialogs.PickFolder("Choose a project folder", _config.LastProjectPath);
        if (folder is null)
        {
            return;
        }

        try
        {
            OpenProjectFolder(folder);
        }
        catch (DirectoryNotFoundException ex)
        {
            _dialogs.ShowError("Project not opened", ex.Message);
        }
    }

    /// <summary>
    /// Opens a folder as the project, wherever the request came from.
    /// </summary>
    /// <remarks>
    /// One path rather than two. The File menu and the front door are asking the same thing, and a
    /// second copy of this is how the two would come to disagree about what opening a project does.
    /// Failure is thrown rather than shown, because where to say so differs: the menu has a window
    /// behind it and the front door has nothing behind it at all.
    /// </remarks>
    /// <exception cref="DirectoryNotFoundException">The folder is not there.</exception>
    public void OpenProjectFolder(string folder)
    {
        Project.Open(folder);
        _config.LastProjectPath = Project.ProjectPath;
        _config.Save();

        _feed.Info(
            $"{Project.KindText} opened",
            Project.IsUnity
                ? $"{Project.ProjectPath}. The Unity write rules are in force: a file name has to match "
                  + "its MonoBehaviour, and a type, namespace or serialized field cannot quietly change name."
                : $"{Project.ProjectPath}. An ordinary C# project, so the Unity write rules do not apply.");

        OnProjectOpened(ProjectOpenedBy.Person);
    }

    /// <summary>Clears the canvas.</summary>
    [RelayCommand(CanExecute = nameof(IsWorkspace))]
    private void NewGraph()
    {
        Graph.Clear();
        SelectedNode = null;
        CurrentGraphPath = null;
        Document.MarkSaved(null);
        _cascadeIndex = 0;

        // A new graph is a new decision, so the suggestion is offered again. Dismissing it lasts
        // as long as the thing it was dismissed on.
        TemplatesDismissed = false;

        _feed.Info("New graph", "The canvas was cleared.");
    }

    /// <summary>Saves the graph, asking for a path.</summary>
    [RelayCommand(CanExecute = nameof(IsWorkspace))]
    private void SaveGraph()
    {
        AppPaths.EnsureCreated();

        var suggested = CurrentGraphPath is null
            ? "graph" + GraphSerializer.FileExtension
            : Path.GetFileName(CurrentGraphPath);

        var path = _dialogs.PickSaveFile(
            "Save graph",
            suggested,
            $"LocalNEXUS graph (*{GraphSerializer.FileExtension})|*{GraphSerializer.FileExtension}|JSON (*.json)|*.json",
            Path.GetDirectoryName(CurrentGraphPath) ?? GraphFolder(forSaving: true));

        if (path is null)
        {
            return;
        }

        try
        {
            _serializer.Save(Graph, path);
            CurrentGraphPath = path;
            Document.MarkSaved(path);
            RememberGraph(path);
            _feed.Info("Graph saved", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError("Graph not saved", ex.Message);
        }
    }

    /// <summary>Loads a graph, replacing whatever is on the canvas.</summary>
    [RelayCommand(CanExecute = nameof(IsWorkspace))]
    private void LoadGraph()
    {
        AppPaths.EnsureCreated();

        var path = _dialogs.PickOpenFile(
            "Load graph",
            $"LocalNEXUS graph (*{GraphSerializer.FileExtension})|*{GraphSerializer.FileExtension}|JSON (*.json)|*.json|All files (*.*)|*.*",
            Path.GetDirectoryName(CurrentGraphPath) ?? GraphFolder(forSaving: false));

        if (path is null)
        {
            return;
        }

        LoadGraphFrom(path);
    }

    /// <summary>
    /// Where a graph dialog opens on, and the folder it will be saved into.
    /// </summary>
    /// <remarks>
    /// A graph is an arrangement of work on a particular codebase, so it belongs with that
    /// codebase rather than in a folder on one machine where every project's graphs pile up
    /// together. New ones are written into the project.
    ///
    /// The old folder is not emptied, and nothing is moved out of it. It can hold graphs from any
    /// number of projects with no record of which belongs where, so moving them would mean
    /// guessing, and a graph guessed into the wrong project writes into the wrong codebase. A load
    /// dialog opens on it while it holds graphs and the project holds none, which is what stops
    /// anybody having to go looking.
    /// </remarks>
    private string GraphFolder(bool forSaving)
    {
        var folder = ProjectPaths.GraphFolderToShow(Project.ProjectPath, forSaving);

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A dialog that opens somewhere else is better than one that does not open. The save
            // itself reports properly if the folder is the reason it failed.
            _feed.Info("Graph folder not created", $"{folder} could not be created: {ex.Message}");
        }

        return folder;
    }

    /// <summary>
    /// Remembers this as the project's graph, so opening the project opens it again.
    /// </summary>
    /// <remarks>
    /// Nothing is remembered with no project open, because there is nowhere for it to belong. That
    /// used to be an application wide setting, written on every save and read by nothing.
    /// </remarks>
    private void RememberGraph(string path)
    {
        if (!Project.HasProject)
        {
            return;
        }

        ProjectSettings.LastGraphPath = path;
        ProjectSettings.Save();
    }

    /// <summary>Loads a graph from an explicit path. Used by the File menu and at startup.</summary>
    public void LoadGraphFrom(string path)
    {
        try
        {
            SelectedNode = null;
            var warnings = _serializer.LoadInto(Graph, path);

            CurrentGraphPath = path;
            Document.MarkSaved(path);
            RememberGraph(path);

            _feed.Info("Graph loaded", $"{Graph.Nodes.Count} nodes, {Graph.Connections.Count} connections from {path}");

            // News, not a problem. A graph brought up to date opened correctly.
            foreach (var migration in _serializer.Migrations)
            {
                _feed.Info("Graph brought up to date", migration);
            }

            foreach (var warning in warnings)
            {
                _feed.Error("Graph load warning", warning);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _dialogs.ShowError("Graph not loaded", ex.Message);
        }
    }

    /// <summary>
    /// Chooses where a newly added node lands. Nodify exposes the cursor position as a read only
    /// dependency property, which cannot be bound, so new nodes cascade from the top left instead
    /// and the user drags them where they want.
    /// </summary>
    private Point NextNodeLocation()
    {
        var step = _cascadeIndex++;
        var column = step / 8;
        var row = step % 8;

        return new Point(90d + (column * 260d) + (row * CascadeStep), 90d + (row * CascadeStep));
    }

    private void SelectOnly(NodeBase node)
    {
        foreach (var other in Graph.Nodes)
        {
            other.IsSelected = other == node;
        }

        SelectedNode = node;
    }

    /// <summary>
    /// Asks every compile check node what it can reach right now.
    /// </summary>
    /// <remarks>
    /// Here rather than in the node because the node has no services outside a run, and this is
    /// the question that has to be answered before one. It names a node type, which the executor
    /// may not do and a view model may: this is the shell deciding what to show, not the engine
    /// deciding what to execute.
    /// </remarks>
    /// <summary>Reads the newly opened project's record and its conversation, in that order.</summary>
    private async Task ReopenRecordAsync()
    {
        await _history.OpenProjectAsync(Project.ProjectPath, CancellationToken.None).ConfigureAwait(true);

        if (_history.IsOpen && Feed.Conversation is { } conversation)
        {
            await conversation.OpenProjectAsync(CancellationToken.None).ConfigureAwait(true);
        }
    }

    private void RefreshCompilerReachability()
    {
        foreach (var node in Graph.Nodes.OfType<CompilerCheckNode>())
        {
            node.RefreshReachability(_compiler, Project.ProjectPath);
        }
    }

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsCanvasEmpty));
        OnPropertyChanged(nameof(ShowTemplates));
        SaveAsTemplateCommand.NotifyCanExecuteChanged();
        Walkthrough.Refresh();

        // A reset carries no item lists, which is what clearing the canvas raises. Rebuilding the
        // subscription set from the collection covers that case as well as ordinary add and
        // remove, and stops nodes from a discarded graph holding this view model alive.
        RefreshCompilerReachability();

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            ResyncNodeSubscriptions();
            SelectedNode = null;
            return;
        }

        foreach (var node in e.OldItems?.OfType<NodeBase>() ?? Enumerable.Empty<NodeBase>())
        {
            Unobserve(node);
        }

        foreach (var node in e.NewItems?.OfType<NodeBase>() ?? Enumerable.Empty<NodeBase>())
        {
            Observe(node);
        }

        if (SelectedNode is not null && !Graph.Nodes.Contains(SelectedNode))
        {
            SelectedNode = null;
        }
    }

    private void ResyncNodeSubscriptions()
    {
        foreach (var node in _observedNodes.ToList())
        {
            Unobserve(node);
        }

        foreach (var node in Graph.Nodes)
        {
            Observe(node);
        }
    }

    private void Observe(NodeBase node)
    {
        if (_observedNodes.Add(node))
        {
            node.PropertyChanged += OnNodePropertyChanged;
        }
    }

    private void Unobserve(NodeBase node)
    {
        if (_observedNodes.Remove(node))
        {
            node.PropertyChanged -= OnNodePropertyChanged;
        }
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NodeBase.IsSelected))
        {
            SelectedNode = Graph.Nodes.LastOrDefault(n => n.IsSelected);
        }
    }

    private void OnProjectChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectService.StatusText) or nameof(ProjectService.ProjectPath))
        {
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(TitleText));

            // What a check can reach depends entirely on which project is open, so the answer the
            // nodes are showing is stale the moment that changes. So is the staged work, which
            // belongs to a project rather than to this install: opening another project must not
            // offer somebody the unfinished work of the one they just left.
            RefreshCompilerReachability();
            Feed.Staging.OpenProject(Project.ProjectPath);

            // The record and the conversation kept inside it belong to a project too, and for
            // the same reason. Not awaited: opening a database is not something a property change
            // should wait behind.
            _ = ReopenRecordAsync();
        }
    }

    private void OnConnectionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(StatusSummary));

    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GraphDocumentViewModel.SummaryText))
        {
            OnPropertyChanged(nameof(StatusSummary));
        }
        else if (e.PropertyName is nameof(GraphDocumentViewModel.Name))
        {
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(TitleText));
        }
    }

    /// <summary>
    /// Follows the run, so that a fault turns every node the run never reached from pending to
    /// skipped. Nothing about a node changes when that happens, which is the point: the node did
    /// not do anything, the run did.
    /// </summary>
    private void OnFeedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ActivityFeedViewModel.RunState))
        {
            return;
        }

        Document.OnRunStateChanged();

        // The walkthrough's last step, and the only one nothing else can see afterwards. A run
        // that left files waiting still ran, so it counts: the step is having got the graph to do
        // something, not having got a perfect result on the first attempt.
        if (Feed.RunState is Services.Execution.RunState.Completed or Services.Execution.RunState.Unresolved)
        {
            Walkthrough.RecordSuccessfulRun();
        }
    }

    private void OnNetworkChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NetworkViewModel.InspectorTarget))
        {
            OnPropertyChanged(nameof(InspectorContent));
            OnPropertyChanged(nameof(InspectorHeader));
        }
        else if (e.PropertyName is nameof(NetworkViewModel.CoverageSummary))
        {
            OnPropertyChanged(nameof(StatusSummary));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Graph.Nodes.CollectionChanged -= OnNodesChanged;
        Graph.Connections.CollectionChanged -= OnConnectionsChanged;
        Project.PropertyChanged -= OnProjectChanged;
        Feed.PropertyChanged -= OnFeedChanged;
        Network.PropertyChanged -= OnNetworkChanged;
        Document.PropertyChanged -= OnDocumentChanged;

        foreach (var node in _observedNodes.ToList())
        {
            Unobserve(node);
        }

        Document.Dispose();
        Problems.Dispose();
    }
}
