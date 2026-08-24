using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.Models;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.Services.ProjectIndex;
using LocalNEXUS.App.Services.Theming;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The settings panel: everything that is true of the application rather than of one node.
/// </summary>
/// <remarks>
/// The dividing line is ownership, not convenience. A watched model folder, a theme and a cloud
/// key are properties of this install, so they live here and every graph sees the same ones. Which
/// model a node uses and which file it writes are properties of that graph, so they stay on the
/// node where they can be saved with it and be different in the next graph.
///
/// The defaults section is the one place the line blurs, and it resolves the same way: what is
/// stored here is the value a newly added node starts from, never a value that reaches back into
/// nodes that already exist. Changing a default cannot silently change a graph somebody saved.
/// </remarks>
public sealed partial class AppSettingsViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly ModelCatalog _catalog;
    private readonly ProjectIndexService _index;
    private readonly IDialogService _dialogs;
    private readonly Func<Task> _reindex;

    /// <summary>Which section of the panel is showing.</summary>
    [ObservableProperty]
    private SettingsSection _section = SettingsSection.Appearance;

    public AppSettingsViewModel(
        AppConfig config,
        ThemeService themes,
        ModelCatalog catalog,
        ModelCatalogViewModel catalogCommands,
        PythonEnvironmentViewModel python,
        NetworkViewModel network,
        ExtensionsViewModel extensions,
        CloudProvidersViewModel providers,
        ProjectIndexService index,
        IDialogService dialogs,
        Func<Task> reindex)
    {
        _config = config;
        _catalog = catalog;
        Extensions = extensions;
        Providers = providers;
        _index = index;
        _dialogs = dialogs;
        _reindex = reindex;

        Themes = themes;
        Catalog = catalogCommands;
        Browser = new ModelBrowserViewModel(HubTransport.CreateClient(), catalogCommands, dialogs);
        Python = python;
        Network = network;

        ThemeChoices = ThemeService.Available
            .Select(t => new ThemeChoiceViewModel(t, ApplyTheme, t.Theme == themes.Current))
            .ToList();

        RefreshEntries();
    }

    /// <summary>The theme picker binds to this directly, so choosing one applies it at once.</summary>
    public ThemeService Themes { get; }

    /// <summary>Catalogue commands, shared with the model node panel.</summary>
    public ModelCatalogViewModel Catalog { get; }

    /// <summary>
    /// Finding a model to download, beside the ways of pointing at one already here.
    /// </summary>
    /// <remarks>
    /// In the Models section rather than a window of its own, because getting a model and telling
    /// the application where models are is one task from the point of view of somebody who has
    /// just installed this and has none.
    /// </remarks>
    public ModelBrowserViewModel Browser { get; }

    /// <summary>
    /// Searching run history by meaning, which is off until somebody chooses a model.
    /// </summary>
    /// <remarks>
    /// In the Models section because what it needs is a model, and somebody arranging one is
    /// already looking at the place models come from.
    /// </remarks>
    public SemanticSearchViewModel? Semantic { get; init; }

    /// <summary>The Python runtime, with its provisioning, healthy and broken states.</summary>
    public PythonEnvironmentViewModel Python { get; }

    /// <summary>Mesh membership and contribution.</summary>
    public NetworkViewModel Network { get; }

    /// <summary>The extensions registered against the open project.</summary>
    public ExtensionsViewModel Extensions { get; }

    /// <summary>Hosted providers, their keys and the spending threshold.</summary>
    public CloudProvidersViewModel Providers { get; }

    /// <summary>What the project index currently knows.</summary>
    public ProjectIndexService Index => _index;

    /// <summary>
    /// Everything feeding the catalogue: the folders being scanned and the models added by name.
    /// </summary>
    public ObservableCollection<CatalogEntryViewModel> Entries { get; } = new();

    /// <summary>What the last add or rescan did, said in the panel rather than in a dialog.</summary>
    public string CatalogMessage
    {
        get => _catalogMessage;
        private set => SetProperty(ref _catalogMessage, value);
    }

    private string _catalogMessage = string.Empty;

    /// <summary>Every theme that can be picked, with the one in force marked.</summary>
    public IReadOnlyList<ThemeChoiceViewModel> ThemeChoices { get; }

    /// <summary>The sections of this panel, in the order they are listed.</summary>
    public IReadOnlyList<SettingsSection> Sections { get; } = Enum.GetValues<SettingsSection>();

    /// <summary>
    /// Applies a theme, at once and for the next session, and takes the mark off the others.
    /// </summary>
    private void ApplyTheme(ThemeChoiceViewModel choice)
    {
        Themes.Apply(choice.Definition.Theme);

        foreach (var candidate in ThemeChoices)
        {
            if (candidate != choice)
            {
                candidate.SetSelectedQuietly(false);
            }
        }
    }

    /// <summary>The record of past runs, so the panel can say what it holds.</summary>
    public Services.History.RunHistoryStore History { get; private set; } = new();

    /// <summary>What the record is costing on disk, refreshed on demand.</summary>
    [ObservableProperty]
    private Services.History.HistoryUsage _historyUsage = Services.History.HistoryUsage.None;

    /// <summary>How many runs keep their snapshots.</summary>
    public int SnapshotRunLimit
    {
        get => _config.SnapshotRunLimit;
        set => SetConfig(Math.Clamp(value, 1, 1000), v => _config.SnapshotRunLimit = v);
    }

    /// <summary>How many days a snapshot is kept.</summary>
    public int SnapshotAgeDays
    {
        get => _config.SnapshotAgeDays;
        set => SetConfig(Math.Clamp(value, 1, 3650), v => _config.SnapshotAgeDays = v);
    }

    /// <summary>
    /// Whether a launch says that the previous one ended badly.
    /// </summary>
    /// <remarks>
    /// A setting rather than only an answer to a dialog, so it can be turned off once and stay
    /// off, and turned back on by somebody who wants to help rather than only by deleting a file.
    /// </remarks>
    public bool ReportCrashes
    {
        get => _config.ReportCrashes;
        set => SetConfig(value, v => _config.ReportCrashes = v);
    }

    /// <summary>
    /// Whether the Python runtime may be built, which is what safetensors models are served
    /// through.
    /// </summary>
    /// <remarks>
    /// Asked once at startup and answered here afterwards. It used to be answerable only in that
    /// dialog, so somebody who said no had no way back to it and somebody who said yes had no way
    /// to see what they had agreed to. Turning it on starts the build rather than waiting for the
    /// next launch, because somebody who just ticked it is asking for it now.
    /// </remarks>
    public bool BuildPythonRuntime
    {
        get => _config.PythonRuntimeConsent ?? false;
        set
        {
            SetConfig(value, v => _config.PythonRuntimeConsent = v);

            if (value && Python.RepairCommand.CanExecute(null))
            {
                Python.RepairCommand.Execute(null);
            }
        }
    }

    /// <summary>Reads what the record is costing.</summary>
    [RelayCommand]
    private async Task RefreshHistoryUsageAsync()
        => HistoryUsage = await History.ReadUsageAsync(CancellationToken.None).ConfigureAwait(true);

    /// <summary>Applies the caps now rather than waiting for the next run to do it.</summary>
    [RelayCommand]
    private async Task PruneSnapshotsAsync()
    {
        History.PruneSnapshots(SnapshotRunLimit, SnapshotAgeDays);
        await RefreshHistoryUsageAsync().ConfigureAwait(true);
    }

    /// <summary>Drops every snapshot, keeping what the runs said.</summary>
    [RelayCommand]
    private async Task ClearSnapshotsAsync()
    {
        History.ClearSnapshots();
        await RefreshHistoryUsageAsync().ConfigureAwait(true);
    }

    /// <summary>Drops the whole record for this project.</summary>
    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        History.ClearHistory();
        await RefreshHistoryUsageAsync().ConfigureAwait(true);
    }

    /// <summary>Points this panel at the record, which App owns.</summary>
    public void UseHistory(Services.History.RunHistoryStore history) => History = history;

    private Services.Mcp.McpBridgeServer? _mcp;
    private Services.Vision.VisionReader? _vision;

    /// <summary>Points this panel at the vision model, which App owns.</summary>
    public void UseVision(Services.Vision.VisionReader vision)
    {
        _vision = vision;
        OnPropertyChanged(nameof(VisionStatus));
        OnPropertyChanged(nameof(SelectedVisionModel));
    }

    /// <summary>What is configured, or that nothing is.</summary>
    public string VisionStatus => _vision?.Status ?? "Nothing configured.";

    /// <summary>
    /// The local models a vision model could be chosen from.
    /// </summary>
    /// <remarks>
    /// GGUF only, because the projector that lets a model see is a llama.cpp thing and the Python
    /// runtime has no equivalent. That is a filter on what can be offered rather than a question
    /// anybody is asked, which is the same rule the model dropdown follows.
    /// </remarks>
    public IEnumerable<Services.Persistence.LocalModelInfo> VisionModels
        => _catalog.Models.Where(m => m.Format == Services.Inference.ModelFormat.Gguf);

    /// <summary>
    /// The chosen local vision model, or null when the address is being used instead.
    /// </summary>
    /// <remarks>
    /// Setting it is where a model without a projector is refused. The alternative is accepting it
    /// here and failing on the first image, hours later, with a 400 that means nothing to anybody.
    /// </remarks>
    public Services.Persistence.LocalModelInfo? SelectedVisionModel
    {
        get => _catalog.FindByPath(_config.VisionModelPath);
        set
        {
            var lookup = Services.Inference.VisionProjectorLocator.Locate(value?.Path);

            if (value is not null && !lookup.IsUsable)
            {
                // Refused, and the choice is not stored. What was already configured stays.
                VisionProblem = lookup.Message;
                OnPropertyChanged();
                return;
            }

            VisionProblem = null;
            _config.VisionModelPath = value?.Path;
            _config.Save();

            _vision?.Refresh();

            OnPropertyChanged();
            OnPropertyChanged(nameof(VisionStatus));
        }
    }

    /// <summary>Why the last chosen vision model was refused, or null when none was.</summary>
    [ObservableProperty]
    private string? _visionProblem;

    /// <summary>Stops using a local vision model, leaving whatever address is set.</summary>
    [RelayCommand]
    private void ClearVisionModel() => SelectedVisionModel = null;

    /// <summary>The address of a server that can see.</summary>
    public string VisionBaseUrl
    {
        get => _config.VisionBaseUrl ?? string.Empty;
        set
        {
            SetConfig(value, v => _config.VisionBaseUrl = string.IsNullOrWhiteSpace(v) ? null : v.Trim(),
                nameof(VisionStatus));

            _vision?.Refresh();
        }
    }

    /// <summary>Which model there reads images.</summary>
    public string VisionModelId
    {
        get => _config.VisionModelId ?? string.Empty;
        set
        {
            SetConfig(value, v => _config.VisionModelId = string.IsNullOrWhiteSpace(v) ? null : v.Trim(),
                nameof(VisionStatus));

            _vision?.Refresh();
        }
    }

    /// <summary>Stores the key the vision endpoint wants, when it wants one.</summary>
    [RelayCommand]
    private void SetVisionKey(string? key)
    {
        _credentialsForVision?.Set(Services.Vision.VisionReader.ProviderId, key);
        OnPropertyChanged(nameof(VisionStatus));
    }

    private Services.Credentials.ICredentialStore? _credentialsForVision;

    /// <summary>Points this panel at the credential store, for the vision key.</summary>
    public void UseCredentials(Services.Credentials.ICredentialStore credentials)
        => _credentialsForVision = credentials;

    private Services.Search.WebSearchService? _search;

    /// <summary>Points this panel at web search, which App owns.</summary>
    public void UseSearch(Services.Search.WebSearchService search)
    {
        _search = search;

        OnPropertyChanged(nameof(HasSearchKey));
        OnPropertyChanged(nameof(SearchKeyState));
        OnPropertyChanged(nameof(SearchStatus));
    }

    /// <summary>True when a search key is stored, without decrypting it.</summary>
    public bool HasSearchKey => _search?.HasKey == true;

    /// <summary>Where a key comes from, and what having one does.</summary>
    public string SearchStatus => HasSearchKey
        ? "A key is stored. The request box has a search checkbox on it, and a model may call search "
          + "during a run when that is ticked."
        : "No key, so search is not offered anywhere. Brave gives five dollars of credit a month, "
          + $"then charges per thousand requests. Get a key at {Services.Search.WebSearchService.KeyUrl}";

    /// <summary>Where a key is obtained, for the link.</summary>
    public string SearchKeyUrl => Services.Search.WebSearchService.KeyUrl;

    /// <summary>
    /// Whether there is a key, in the two words a provider row uses.
    /// </summary>
    /// <remarks>
    /// Beside the full sentence rather than instead of it. The row says what state it is in and
    /// the line under the list says what that means, which is how the model provider rows read.
    /// </remarks>
    public string SearchKeyState => HasSearchKey ? "key stored" : "no key yet";

    /// <summary>Opens the page a key comes from.</summary>
    [RelayCommand]
    private void OpenSearchKeyUrl()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = SearchKeyUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _dialogs.ShowError("The page could not be opened", $"{SearchKeyUrl}: {ex.Message}");
        }
    }

    /// <summary>
    /// Stores or clears the search key.
    /// </summary>
    /// <remarks>
    /// Write only, like the model keys. What is stored is never read back into the interface,
    /// because a box that shows a key is a key on somebody's screen.
    /// </remarks>
    [RelayCommand]
    private void SetSearchKey(string? key)
    {
        _search?.SetKey(key);

        OnPropertyChanged(nameof(HasSearchKey));
        OnPropertyChanged(nameof(SearchStatus));
    }

    /// <summary>Forgets the search key.</summary>
    [RelayCommand]
    private void ClearSearchKey() => SetSearchKey(null);

    private Services.Files.ProjectSettingsService? _projectSettings;

    /// <summary>Points this panel at the open project's settings, which App owns.</summary>
    public void UseProjectSettings(Services.Files.ProjectSettingsService settings)
    {
        _projectSettings = settings;

        settings.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ScriptsFolder));
            OnPropertyChanged(nameof(ProjectKind));
            OnPropertyChanged(nameof(ProjectKindNote));
            OnPropertyChanged(nameof(ProjectMcpEnabled));
            OnPropertyChanged(nameof(ShareProjectSettings));
            OnPropertyChanged(nameof(ProjectSharingNote));
            OnPropertyChanged(nameof(ProjectFolders));
        };

        OnPropertyChanged(nameof(ProjectSettings));
    }

    /// <summary>The open project's settings, for the panel to bind its visibility to.</summary>
    public Services.Files.ProjectSettingsService? ProjectSettings => _projectSettings;

    /// <summary>Folders the project has, for the list to choose from.</summary>
    public IReadOnlyList<string> ProjectFolders => _projectSettings?.ExistingFolders() ?? Array.Empty<string>();

    /// <summary>Both kinds, for the override.</summary>
    public IReadOnlyList<Services.Files.ProjectKind> ProjectKinds { get; } =
        new[] { Services.Files.ProjectKind.Unity, Services.Files.ProjectKind.Plain };

    /// <summary>Where generated code goes in this project.</summary>
    public string ScriptsFolder
    {
        get => _projectSettings?.ScriptsFolder ?? string.Empty;
        set => SetProject(s => s.ScriptsFolder = Normalise(value));
    }

    /// <summary>What this project is, detected or overridden.</summary>
    public Services.Files.ProjectKind ProjectKind
    {
        get => _projectSettings?.Kind ?? Services.Files.ProjectKind.None;
        set => SetProject(s => s.Kind = value, nameof(ProjectKindNote));
    }

    /// <summary>What the kind means for what will be refused.</summary>
    public string ProjectKindNote => ProjectKind == Services.Files.ProjectKind.Unity
        ? "The Unity write rules are in force: a file name has to match its MonoBehaviour, and a type, "
          + "namespace or serialized field cannot quietly change name."
        : "The Unity write rules do not apply, so a rename that would break a scene is not refused.";

    /// <summary>Whether tool calls are answered while this project is open.</summary>
    public bool ProjectMcpEnabled
    {
        get => _projectSettings?.McpServerEnabled == true;
        set => SetProject(s => s.McpServerEnabled = value);
    }

    /// <summary>Whether the conventions are committed.</summary>
    public bool ShareProjectSettings
    {
        get => _projectSettings?.ShareSettings == true;
        set => SetProject(s => s.ShareSettings = value, nameof(ProjectSharingNote));
    }

    /// <summary>Where the answers are written, and which of them.</summary>
    public string ProjectSharingNote => ShareProjectSettings
        ? $"{Services.Files.ProjectSettings.SharedFileName} holds the folder and the project kind, for everybody "
          + $"working on this project. Your model choice and the tool call switch stay in "
          + $"{Services.Files.ProjectSettings.LocalFileName}, which is never committed."
        : $"Everything is in {Services.Files.ProjectSettings.LocalFileName}, which is added to .gitignore if this "
          + "project has one.";

    /// <summary>A folder as the settings store it: forward slashes, no leading or trailing one.</summary>
    private static string Normalise(string? value)
        => (value ?? string.Empty).Trim().Replace(Path.DirectorySeparatorChar, '/').Trim('/');

    private void SetProject(Action<Services.Files.ProjectSettingsService> assign, string? alsoChanged = null,
        [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (_projectSettings is not { } settings)
        {
            return;
        }

        assign(settings);
        settings.Save();

        OnPropertyChanged(property);

        if (alsoChanged is not null)
        {
            OnPropertyChanged(alsoChanged);
        }
    }

    /// <summary>Points this panel at the MCP server, which App owns.</summary>
    public void UseMcpServer(Services.Mcp.McpBridgeServer server)
    {
        _mcp = server;
        OnPropertyChanged(nameof(McpServerEnabled));
        OnPropertyChanged(nameof(McpServerStatus));
    }

    /// <summary>
    /// Whether this installation answers MCP tool calls from other tools.
    /// </summary>
    /// <remarks>
    /// Off unless somebody turns it on. With it on, anything on this account that can start a
    /// process can open a project, open a graph and run it, and a run writes files and spends
    /// whatever a cloud model costs.
    /// </remarks>
    public bool McpServerEnabled
    {
        get => _config.McpServerEnabled;
        set
        {
            SetConfig(value, v => _config.McpServerEnabled = v, nameof(McpServerStatus));

            if (value)
            {
                _mcp?.Start();
            }
            else
            {
                _mcp?.Stop();
            }
        }
    }

    /// <summary>What to say under the switch.</summary>
    public string McpServerStatus => _config.McpServerEnabled
        ? $"Answering on the local pipe {Services.Mcp.McpBridge.PipeName}. Point an MCP client at "
          + "LocalNEXUS.Mcp.exe beside the application."
        : "Not answering. Other tools cannot drive this installation.";

    /// <summary>Where cloud requests go by default. Blank uses whatever the provider defaults to.</summary>
    public string CloudBaseUrl
    {
        get => _config.CloudBaseUrl ?? string.Empty;
        set => SetConfig(value, v => _config.CloudBaseUrl = string.IsNullOrWhiteSpace(v) ? null : v.Trim());
    }

    /// <summary>The key a newly added model node starts with.</summary>
    public int DefaultRetryLimit
    {
        get => _config.DefaultRetryLimit;
        set => SetConfig(Math.Clamp(value, 0, 10), v => _config.DefaultRetryLimit = v);
    }

    /// <summary>Characters of project map a newly added plan node starts with.</summary>
    public int DefaultMapCharacters
    {
        get => _config.DefaultMapCharacters;
        set => SetConfig(Math.Max(0, value), v => _config.DefaultMapCharacters = v, nameof(DefaultBudgetSummary));
    }

    /// <summary>Characters of candidate file contents a newly added plan node starts with.</summary>
    public int DefaultCandidateCharacters
    {
        get => _config.DefaultCandidateCharacters;
        set => SetConfig(Math.Max(0, value), v => _config.DefaultCandidateCharacters = v, nameof(DefaultBudgetSummary));
    }

    /// <summary>Characters of same-run signatures a newly added plan node starts with.</summary>
    public int DefaultEmittedCharacters
    {
        get => _config.DefaultEmittedCharacters;
        set => SetConfig(Math.Max(0, value), v => _config.DefaultEmittedCharacters = v, nameof(DefaultBudgetSummary));
    }

    /// <summary>How many candidate files a newly added plan node offers before reading any.</summary>
    public int DefaultCandidateLimit
    {
        get => _config.DefaultCandidateLimit;
        set => SetConfig(Math.Clamp(value, 1, 64), v => _config.DefaultCandidateLimit = v);
    }

    /// <summary>The three budgets as one sentence, in characters and in approximate tokens.</summary>
    public string DefaultBudgetSummary => new ContextBudget
    {
        MapCharacters = DefaultMapCharacters,
        CandidateCharacters = DefaultCandidateCharacters,
        EmittedSignatureCharacters = DefaultEmittedCharacters,
        CandidateLimit = DefaultCandidateLimit
    }.Summary;

    /// <summary>Adds a folder that will be searched, and keeps being searched.</summary>
    [RelayCommand]
    private void AddFolder()
    {
        var folder = _dialogs.PickFolder("Choose a folder to search for models", AppPaths.Models);

        if (folder is not null)
        {
            Report(_catalog.AddFolder(folder));
        }
    }

    /// <summary>
    /// Adds one model file, which is the path a folder picker could never offer.
    /// </summary>
    /// <remarks>
    /// This exists because picking a folder full of models used to be the only way in, and a
    /// folder picker lists folders, so the models themselves were invisible in it and the whole
    /// thing looked broken while working exactly as written.
    /// </remarks>
    [RelayCommand]
    private void AddModelFile()
    {
        var file = _dialogs.PickOpenFile(
            "Choose a model file",
            "Models (*.gguf;*.safetensors)|*.gguf;*.safetensors|All files (*.*)|*.*",
            AppPaths.Models);

        if (file is not null)
        {
            Report(_catalog.AddModel(file));
        }
    }

    /// <summary>
    /// Adds one safetensors model, which is a folder rather than a file, without registering
    /// everything that happens to sit beside it.
    /// </summary>
    [RelayCommand]
    private void AddModelFolder()
    {
        var folder = _dialogs.PickFolder("Choose a model folder", AppPaths.Models);

        if (folder is not null)
        {
            Report(_catalog.AddModel(folder));
        }
    }

    /// <summary>Drops a folder from the search set, or stops offering a model added by name.</summary>
    [RelayCommand]
    private void RemoveEntry(CatalogEntryViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        var removed = entry.IsFolder
            ? _catalog.RemoveFolder(entry.Path)
            : _catalog.RemoveModel(entry.Path);

        if (removed)
        {
            CatalogMessage = entry.IsFolder
                ? $"No longer searching {entry.Path}."
                : $"No longer offering {entry.Label}.";
        }

        RefreshEntries();
    }

    /// <summary>Searches every folder again.</summary>
    [RelayCommand]
    private void Rescan()
    {
        _catalog.Refresh();
        RefreshEntries();

        CatalogMessage = _catalog.Models.Count == 1
            ? "1 model found."
            : $"{_catalog.Models.Count} models found.";
    }

    /// <summary>Opens the file that lists extra folders, one per line.</summary>
    [RelayCommand]
    private void EditModelPaths()
    {
        ModelPathsFile.EnsureCreated();
        _dialogs.OpenFileInEditor(AppPaths.ModelPathsFile);
    }

    /// <summary>Reads the open project again from scratch.</summary>
    [RelayCommand]
    private async Task ReindexAsync()
    {
        _index.Forget();
        await _reindex().ConfigureAwait(false);
    }

    /// <summary>Opens the folder this install keeps its configuration and logs in.</summary>
    [RelayCommand]
    private void OpenDataFolder()
    {
        AppPaths.EnsureCreated();
        _dialogs.OpenFolderInExplorer(AppPaths.Root);
    }

    /// <summary>Says what happened, and rebuilds the list when something changed.</summary>
    private void Report(CatalogAddition result)
    {
        CatalogMessage = result.Message;

        if (result.Added)
        {
            RefreshEntries();
        }
    }

    private void RefreshEntries()
    {
        Entries.Clear();

        foreach (var folder in _catalog.SearchFolders)
        {
            var removable = _catalog.IsRemovable(folder);

            var origin = removable
                ? "searched, added here"
                : string.Equals(Path.GetFullPath(folder), Path.GetFullPath(AppPaths.Models), StringComparison.OrdinalIgnoreCase)
                    ? "searched, built in"
                    : "searched, listed in model-paths.txt";

            Entries.Add(new CatalogEntryViewModel(folder, CatalogEntryKind.ScannedFolder, folder, origin, removable));
        }

        foreach (var path in _catalog.DirectPaths)
        {
            var model = _catalog.FindByPath(path);

            // A model added by name and then moved or deleted stays on the list saying so, rather
            // than disappearing and leaving somebody wondering where their entry went.
            var detail = model is null
                ? "added on its own, and no longer at that path"
                : $"added on its own, {model.Descriptor.SizeLabel}, {model.FormatLabel}";

            Entries.Add(new CatalogEntryViewModel(
                path,
                CatalogEntryKind.Model,
                model?.Name ?? Path.GetFileName(path),
                detail,
                CanRemove: true));
        }

        OnPropertyChanged(nameof(Catalog));
    }

    /// <summary>
    /// Writes a setting through to the file. Settings save as they are changed rather than behind
    /// an apply button, because a panel with no apply button cannot be left in a state where what
    /// is on screen and what is in force disagree.
    /// </summary>
    private void SetConfig<T>(T value, Action<T> assign, string? alsoChanged = null, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        assign(value);
        _config.Save();

        OnPropertyChanged(property);

        if (alsoChanged is not null)
        {
            OnPropertyChanged(alsoChanged);
        }
    }
}
