using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.Distributed;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.Services.Python;
using LocalNEXUS.App.ViewModels.Network;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The Network section: what the mesh can serve, as a table of models, filtered from the sidebar.
/// </summary>
/// <remarks>
/// Models lead and machines are the detail underneath, because the question the screen answers is
/// "what can the network serve", not "which machines do I know about". A machine is a filter and
/// an inspector target rather than the spine of the page.
///
/// Everything binds to the mesh manager directly, so what is drawn is what the engine reports
/// rather than anything this view model computes. Where a column has no answer it says so: the
/// mesh reports coverage, peers and metadata, and does not report file size or throughput, and a
/// dash is the honest rendering of that.
///
/// Membership and contribution are launch settings of the node process, so changing one saves it
/// and restarts the node. That is deliberate: a half applied membership change would be a worse
/// surprise than a visible restart.
/// </remarks>
public sealed partial class NetworkViewModel : ObservableObject, IDisposable
{
    private readonly AppConfig _config;
    private readonly IActivityFeed _feed;
    private readonly IDialogService _dialogs;
    private readonly Dictionary<NetworkServedModel, NetworkModelRow> _rows = new();

    /// <summary>Meshes the public directory listed, which live alongside the mesh's own models.</summary>
    private readonly List<DiscoveredMeshRow> _discovered = new();

    private bool _disposed;

    /// <summary>The model whose coverage the inspector shows. Selecting a complete one arms it for use.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(InspectorTarget))]
    [NotifyPropertyChangedFor(nameof(IsInsideAModel))]
    [NotifyPropertyChangedFor(nameof(ClearInspectorText))]
    private NetworkServedModel? _selectedModel;

    /// <summary>The row backing <see cref="SelectedModel"/>, which is what the table highlights.</summary>
    [ObservableProperty]
    private INetworkRow? _selectedRow;

    /// <summary>The machine the sidebar has selected, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InspectorTarget))]
    [NotifyPropertyChangedFor(nameof(IsInsideAModel))]
    [NotifyPropertyChangedFor(nameof(ClearInspectorText))]
    private InferenceSource? _selectedSource;

    /// <summary>The coverage section the inspector is showing, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InspectorTarget))]
    [NotifyPropertyChangedFor(nameof(IsInsideAModel))]
    [NotifyPropertyChangedFor(nameof(ClearInspectorText))]
    private SourceAssignment? _selectedSection;

    /// <summary>Free text typed into the filter box in the title bar.</summary>
    [ObservableProperty]
    private string _filterText = string.Empty;

    /// <summary>The column the table is ordered by.</summary>
    [ObservableProperty]
    private ModelColumn _sortColumn = ModelColumn.Coverage;

    /// <summary>True when the order is reversed.</summary>
    [ObservableProperty]
    private bool _sortDescending;

    /// <summary>Whether the join form is open. It lives behind the plus, not on the page.</summary>
    [ObservableProperty]
    private bool _isJoinOpen;


    /// <summary>The mesh being joined right now, for as long as that takes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MembershipText))]
    private string _joiningName = string.Empty;

    /// <summary>An invite somebody has pasted in but not joined yet.</summary>
    /// <remarks>
    /// Its own field rather than the membership itself, which is what it used to be. Typing into a
    /// box was joining a mesh, and clearing the box was leaving one.
    /// </remarks>
    [ObservableProperty]
    private string _typedToken = string.Empty;

    /// <summary>Name this install gives the mesh it hosts.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MembershipText))]
    private string _meshName;

    /// <summary>Whether this machine offers its own compute rather than only routing.</summary>
    [ObservableProperty]
    private bool _contribute;

    /// <summary>How much of the card is shared, in GB. The slider owns this.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemoryReadout))]
    [NotifyPropertyChangedFor(nameof(MemorySummary))]
    private double _memoryShareGb;

    /// <summary>
    /// True when the cap follows the card instead of a typed number.
    /// </summary>
    /// <remarks>
    /// It changes what the slider can reach, not where it currently sits. Unchecked, the slider
    /// stops at the backoff, so overcommitting the card is not something that can be done by
    /// accident. Checked, the rest of the range unlocks and the value stays where it was; going
    /// further is then a thing somebody drags to on purpose.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemoryMaximumGb))]
    [NotifyPropertyChangedFor(nameof(MemorySummary))]
    private bool _offerAllMemory;

    /// <summary>Port the node's OpenAI compatible API listens on.</summary>
    [ObservableProperty]
    private string _apiPort;

    /// <summary>Advertises this mesh publicly. Off by default and the only setting that leaves the local network.</summary>
    [ObservableProperty]
    private bool _publish;

    private readonly MeshDirectory? _directory;

    public NetworkViewModel(
        MeshManager mesh,
        ModelCatalog catalog,
        AppConfig config,
        IActivityFeed feed,
        IDialogService dialogs,
        MeshDirectory? directory = null)
    {
        _directory = directory;
        Mesh = mesh;
        Catalog = catalog;
        _config = config;
        _feed = feed;
        _dialogs = dialogs;

        _meshName = string.IsNullOrWhiteSpace(config.MeshName) ? "LocalNEXUS" : config.MeshName;
        _contribute = config.MeshContribute;
        _publish = config.MeshPublish;
        foreach (var record in config.MeshJoined)
        {
            Joined.Add(new JoinedMesh(record.Name, record.Token, record.JoinedAt));
        }
        _apiPort = config.MeshApiPort.ToString(CultureInfo.InvariantCulture);
        _offerAllMemory = config.MeshOfferAllMemory;

        // A machine that has never been configured starts at the safe ceiling rather than at zero,
        // so the number on screen is both useful and one the hardware can keep.
        var share = config.MeshMaxVramGb > 0 ? config.MeshMaxVramGb : SafeCeilingGb;
        _memoryShareGb = Math.Clamp(share, 0d, Math.Max(SafeCeilingGb, _offerAllMemory ? MemoryCeilingGb : SafeCeilingGb));

        RebuildOfferedModels();
        RefreshJoinedStates();

        Hosted.Add(new HostedMeshRow(
            Mesh,
            () => MeshName,
            () => Machines.Count,
            () => OfferedCount,
            () => Contribute));
        catalog.Models.CollectionChanged += (_, _) => RebuildOfferedModels();

        Groups = BuildFilterGroups();

        Mesh.Models.CollectionChanged += OnModelsChanged;
        Mesh.Sources.CollectionChanged += OnSourcesChanged;
        Mesh.PropertyChanged += OnMeshChanged;

        RebuildRows();
        SelectedModel = Mesh.Models.FirstOrDefault();
    }

    /// <summary>This install's mesh node and everything it reports. The primary surface.</summary>
    public MeshManager Mesh { get; }

    /// <summary>The local model files, which is what this machine can offer to serve.</summary>
    public ModelCatalog Catalog { get; }

    /// <summary>Every model the mesh knows about, as table rows.</summary>
    public ObservableCollection<INetworkRow> Rows { get; } = new();

    /// <summary>The rows the filters and the sort leave, which is what the table draws.</summary>
    public ObservableCollection<INetworkRow> VisibleRows { get; } = new();

    /// <summary>The filter headings in the sidebar, above the contribute card.</summary>
    public IReadOnlyList<ModelFilterGroup> Groups { get; }

    /// <summary>The machines in the mesh, which are both a filter and something to inspect.</summary>
    public ObservableCollection<InferenceSource> Machines { get; } = new();

    /// <summary>True when a model is selected, which is when the coverage table has something to draw.</summary>
    public bool HasSelection => SelectedModel is not null;

    /// <summary>
    /// What the one inspector slot shows on this section. A section beats a machine and a machine
    /// beats a model, because that is the order of how specific the question is: someone who
    /// clicked an uncovered section is asking about that section.
    /// </summary>
    public object? InspectorTarget
        => (object?)SelectedSection
           ?? (object?)SelectedSource
           ?? (object?)SelectedHosted
           ?? (object?)SelectedJoined
           ?? (object?)SelectedDirectoryMesh
           ?? SelectedModel;

    /// <summary>The joined mesh the inspector is showing, or null.</summary>
    /// <remarks>
    /// A fifth thing the one slot can hold. Selecting one lets go of whatever the table above had
    /// pinned, because the two tables are two answers to two questions and showing one while the
    /// other is highlighted is how somebody reads the wrong panel.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InspectorTarget))]
    [NotifyPropertyChangedFor(nameof(IsInsideAModel))]
    [NotifyPropertyChangedFor(nameof(ClearInspectorText))]
    private JoinedMesh? _selectedJoined;

    partial void OnSelectedJoinedChanged(JoinedMesh? value)
    {
        if (value is null)
        {
            return;
        }

        SelectedSection = null;
        SelectedSource = null;
        SelectedDirectoryMesh = null;
        SelectedRow = null;
        SelectedModel = null;
    }

    /// <summary>The directory entry the inspector is showing, or null.</summary>
    /// <remarks>
    /// A fourth thing the one slot can hold. It is not a model and not a source: it is a mesh
    /// somebody else runs, and the only thing to do about it is join.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InspectorTarget))]
    private DiscoveredMesh? _selectedDirectoryMesh;

    /// <summary>True when the inspector is pinned to something inside a model rather than the model.</summary>
    /// <remarks>
    /// What decides whether the control in the header is a way back or a way out. Clicking a
    /// coverage section replaces the whole panel, so from inside one the thing somebody wants is
    /// the model they were looking at, not an empty inspector.
    /// </remarks>
    public bool IsInsideAModel => SelectedSection is not null || SelectedSource is not null;

    /// <summary>What the header control does next, said in the words of where it goes.</summary>
    public string ClearInspectorText => IsInsideAModel ? "Back to the model" : "Select nothing";

    /// <summary>The right hand end of the status bar while this section is showing.</summary>
    public string CoverageSummary
    {
        get
        {
            if (!Mesh.IsRunning)
            {
                return "mesh node stopped";
            }

            var blocked = Rows.Count(r => r.Availability == ModelAvailability.Blocked);
            var starting = Rows.Count(r => r.Availability == ModelAvailability.Starting);

            if (blocked > 0)
            {
                return starting > 0
                    ? $"{blocked} blocked, {starting} starting"
                    : $"{blocked} blocked";
            }

            return starting > 0 ? $"{starting} starting" : $"{Rows.Count} model(s) complete";
        }
    }

    /// <summary>The invite token, which is the only way into a private mesh.</summary>
    public string InviteToken => Mesh.InviteToken;

    /// <summary>Every local model, each with whether this machine offers it.</summary>
    public ObservableCollection<OfferedModelViewModel> OfferedModels { get; } = new();

    /// <summary>How many models are ticked.</summary>
    public int OfferedCount => OfferedModels.Count(m => m.IsOffered);

    /// <summary>
    /// True when this machine is offering its compute but has not been given anything to serve.
    /// </summary>
    /// <remarks>
    /// Not a failure. It is a coherent thing to be doing, and the panel says what is missing
    /// rather than colouring itself red at somebody who is halfway through a decision.
    /// </remarks>
    public bool IsOfferingNothing => Contribute && OfferedCount == 0;

    /// <summary>What this machine's graphics card reports, or null when no driver answered.</summary>
    public GraphicsMemory? Memory => AcceleratorProbe.DetectMemory();

    /// <summary>True once a card was found, which is when there is a range to slide along.</summary>
    public bool HasMemoryReading => MemoryCeilingGb > 0d;

    /// <summary>Everything on the card, which is the ceiling nothing can go above.</summary>
    public double MemoryCeilingGb => Memory?.TotalGb ?? 0d;

    /// <summary>What is held back before anything is shared.</summary>
    public double MemoryBackoffGb => Memory?.BackoffGb ?? 0d;

    /// <summary>The most that can be shared while the backoff stands.</summary>
    public double SafeCeilingGb => Memory?.SafeCeilingGb ?? 0d;

    /// <summary>
    /// The end of the slider: the safe ceiling normally, the whole card once that is asked for.
    /// </summary>
    public double MemoryMaximumGb => OfferAllMemory ? MemoryCeilingGb : SafeCeilingGb;

    /// <summary>The marker on the track, so the safe ceiling is visible against the whole card.</summary>
    public System.Windows.Media.DoubleCollection MemoryTicks => new() { SafeCeilingGb };

    /// <summary>The slider value as it is shown beside it. Display only.</summary>
    public string MemoryReadout => $"{MemoryShareGb:0.#} GB";

    /// <summary>What the card has and what is being offered from it.</summary>
    public string MemorySummary
    {
        get
        {
            if (Memory is not { } memory)
            {
                return "No graphics driver answered, so there is no ceiling to check a cap against. "
                       + "The engine decides how much it can use.";
            }

            var held = Math.Max(0d, memory.TotalGb - MemoryShareGb);

            var opening = $"{memory.GpuName} has {memory.TotalGb:0.#} GB. "
                + $"Sharing {MemoryShareGb:0.#} GB keeps {held:0.#} GB for everything else.";

            return OfferAllMemory
                ? $"{opening} The whole card is available to the slider, so your own models can end up competing with the mesh."
                : $"{opening} {memory.BackoffSummary}";
        }
    }

    /// <summary>Orders the table by a column, reversing it when the same column is picked twice.</summary>
    [RelayCommand]
    private void Sort(ModelColumn column)
    {
        if (SortColumn == column)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = column;
            SortDescending = false;
        }

        ApplyFilters();
    }

    /// <summary>Puts one filter in force within its group.</summary>
    [RelayCommand]
    private void ApplyFilter(ModelFilter? filter)
    {
        if (filter is null)
        {
            return;
        }

        foreach (var group in Groups.Where(g => g.Filters.Contains(filter)))
        {
            group.Select(filter);
        }

        ApplyFilters();
    }

    /// <summary>
    /// Steps the inspector back one level, and off entirely once there is nowhere left to go.
    /// </summary>
    /// <remarks>
    /// The inspector reads a section, then a source, then a model, so clicking into a section left
    /// somebody a level down with nothing that went back up and nothing that closed it. A model is
    /// selected for them on load as well, so there was never a state with nothing pinned to return
    /// to. One press goes up; pressing again with a model pinned lets go of it.
    /// </remarks>
    [RelayCommand]
    private void ClearInspector()
    {
        if (SelectedSection is not null || SelectedSource is not null)
        {
            SelectedSection = null;
            SelectedSource = null;
            return;
        }

        if (SelectedJoined is not null || SelectedHosted is not null)
        {
            SelectedJoined = null;
            SelectedHosted = null;
            return;
        }

        SelectedRow = null;
        SelectedModel = null;
    }

    /// <summary>Puts the invite token on the clipboard, which is how another machine joins.</summary>
    [RelayCommand]
    private void CopyInvite()
    {
        if (string.IsNullOrWhiteSpace(Mesh.InviteToken))
        {
            _feed.Error("Nothing to copy", "The mesh node has not issued an invite token yet.");
            return;
        }

        _dialogs.CopyToClipboard(Mesh.InviteToken);
        _feed.Info("Invite token copied", "It is private and only usable on the local network.");
    }

    /// <summary>
    /// Replaces this machine's invite token by giving the node a new identity.
    /// </summary>
    /// <remarks>
    /// The token cannot be reissued on its own: it is the node's public key and addresses, minted
    /// by the engine. Rotating the identity is what makes an old token stop working, and it is
    /// also what makes this machine a stranger to every peer that knew it, so the question is
    /// asked before it happens rather than after.
    /// </remarks>
    [RelayCommand]
    private async Task RegenerateInviteAsync()
    {
        var approved = await _feed
            .RequestConfirmationAsync(
                "Replace the invite token?",
                "Anyone using the old token leaves this mesh and needs the new one. This machine also gets a new "
                + "identity on the network, so peers see it as a machine they have not met.",
                CancellationToken.None)
            .ConfigureAwait(true);

        if (!approved)
        {
            return;
        }

        await Mesh.RotateIdentityAsync(CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// What the start button says, which is the opposite of what the node is doing.
    /// </summary>
    /// <remarks>
    /// One button rather than two, because the node is either up or it is not and a pair would
    /// leave one of them dead at all times. It says the action rather than the pair of actions:
    /// "Start or stop the node" makes somebody work out which of the two they are about to get.
    /// </remarks>
    public string StartButtonText => Mesh.IsRunning ? "Stop the node" : "Start the node";

    /// <summary>
    /// What the last thing pressed on this tab did, shown on the tab itself.
    /// </summary>
    /// <remarks>
    /// The Network tab writes to the activity feed like everything else, and the activity feed is
    /// only drawn in the Workspace. So joining a mesh reported what it did, twice, into a panel
    /// this section never shows: the button went grey, came back, and said nothing. An action gets
    /// its answer where it was taken.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string _notice = string.Empty;

    /// <summary>True when there is something to say about the last thing pressed.</summary>
    public bool HasNotice => Notice.Length > 0;

    /// <summary>Whether the last thing pressed went wrong, so the strip can say so in colour.</summary>
    [ObservableProperty]
    private bool _noticeIsProblem;

    /// <summary>Puts the strip away.</summary>
    [RelayCommand]
    private void DismissNotice() => Notice = string.Empty;

    /// <summary>Says what just happened, on the tab and in the feed both.</summary>
    private void Say(string title, string detail, bool problem = false)
    {
        Notice = string.IsNullOrWhiteSpace(detail) ? title : $"{title}. {detail}";
        NoticeIsProblem = problem;

        if (problem)
        {
            _feed.Error(title, detail);
            return;
        }

        _feed.Info(title, detail);
    }

    /// <summary>
    /// Every mesh this machine has joined, which is a list because a machine can be in several.
    /// </summary>
    /// <remarks>
    /// The engine's join argument repeats, so this was only ever one mesh because the interface
    /// made it one: joining a second replaced the first with nothing said.
    /// </remarks>
    public ObservableCollection<JoinedMesh> Joined { get; } = new();

    /// <summary>
    /// The mesh this machine hosts, as a table of one.
    /// </summary>
    /// <remarks>
    /// A collection so the table binds to it like the other two, and so hosting more than one is a
    /// change to what fills this rather than a change to how it is drawn.
    /// </remarks>
    public ObservableCollection<HostedMeshRow> Hosted { get; } = new();

    /// <summary>The hosted row the inspector is showing, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InspectorTarget))]
    [NotifyPropertyChangedFor(nameof(IsInsideAModel))]
    [NotifyPropertyChangedFor(nameof(ClearInspectorText))]
    private HostedMeshRow? _selectedHosted;

    partial void OnSelectedHostedChanged(HostedMeshRow? value)
    {
        if (value is null)
        {
            return;
        }

        SelectedSection = null;
        SelectedSource = null;
        SelectedDirectoryMesh = null;
        SelectedJoined = null;
        SelectedRow = null;
        SelectedModel = null;
    }

    /// <summary>True when this machine has joined anything, which is what the table is for.</summary>
    public bool HasJoined => Joined.Count > 0;

    /// <summary>What the joined table says when there is nothing in it.</summary>
    public string JoinedEmptyText => "Not in anybody else's mesh. Find meshes above, pick one and join it, and it appears here.";

    /// <summary>Which mesh this install is in, for the line beside the node state.</summary>
    public string MembershipText => IsJoining
        ? $"joining {JoiningName}"
        : Joined.Count switch
        {
            0 => $"hosting {(string.IsNullOrWhiteSpace(MeshName) ? "a mesh" : MeshName)}",
            1 => $"in {Joined[0].DisplayName}",
            var many => $"in {many} meshes"
        };

    /// <summary>
    /// Puts the joined list back in step with what the node is doing.
    /// </summary>
    /// <remarks>
    /// The engine reports the mesh it is in rather than a list of memberships, so a row's state is
    /// read from whether the node is up rather than from the engine confirming that mesh in
    /// particular. Said as connecting rather than joined while it comes up, because claiming
    /// membership before the node is serving would be claiming something unverified.
    /// </remarks>
    /// <summary>
    /// Which part of joining is happening, from what the node reports about itself.
    /// </summary>
    /// <remarks>
    /// Every branch reads something the engine says rather than a timer: whether the process is up,
    /// whether it has attached to a mesh, and its runtime's own word for whether models are loading
    /// or serving. One state per thing that can take time and fail on its own, because "connecting"
    /// covered starting a process, finding a mesh and reading gigabytes off disk.
    ///
    /// The engine reports one node rather than one membership per mesh, so every joined mesh shares
    /// this answer. That is honest for the common case of being in one, and it is the most the
    /// engine gives us for the rest.
    /// </remarks>
    private JoinState ReadJoinStage()
    {
        if (Mesh.State == MeshNodeState.Failed)
        {
            return JoinState.Failed;
        }

        if (!Mesh.IsRunning)
        {
            return JoinState.NodeStopped;
        }

        if (Mesh.State == MeshNodeState.Starting && !Mesh.IsAttached)
        {
            return JoinState.StartingNode;
        }

        if (!Mesh.IsAttached)
        {
            return JoinState.ReachingMesh;
        }

        // Attached. Loading is the runtime's own word, and a node with nothing loaded and nothing
        // to load reports standby rather than serving, which is ready as far as the mesh goes.
        return Mesh.LlamaReady || string.Equals(Mesh.DaemonState, "serving", StringComparison.OrdinalIgnoreCase)
            ? JoinState.Ready
            : string.Equals(Mesh.DaemonState, "loading", StringComparison.OrdinalIgnoreCase)
                ? JoinState.LoadingModels
                : JoinState.Ready;
    }

    /// <summary>Re-reads the hosted row, all of which is derived from the node and the settings.</summary>
    private void RefreshHosted()
    {
        foreach (var row in Hosted)
        {
            row.Refresh();
        }
    }

    private void RefreshJoinedStates()
    {
        foreach (var mesh in Joined)
        {
            mesh.State = ReadJoinStage();
        }
    }

    /// <summary>Leaves one mesh, keeping any others.</summary>
    [RelayCommand]
    private async Task LeaveJoinedAsync(JoinedMesh? mesh)
    {
        if (mesh is null)
        {
            return;
        }

        Joined.Remove(mesh);
        SaveSettings();

        OnPropertyChanged(nameof(HasJoined));
        OnPropertyChanged(nameof(MembershipText));

        Say($"Left {mesh.DisplayName}", Joined.Count == 0
            ? "You are hosting your own mesh again."
            : $"Still in {Joined.Count} other {(Joined.Count == 1 ? "mesh" : "meshes")}.");

        await ApplySettingsAsync().ConfigureAwait(true);
    }

    /// <summary>True while the public directory is being asked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FindMeshesText))]
    [NotifyPropertyChangedFor(nameof(IsBusyWithDirectory))]
    private bool _isSearchingDirectory;

    /// <summary>
    /// True while a mesh is being joined.
    /// </summary>
    /// <remarks>
    /// Separate from searching, because they are separate acts and shared one flag. Joining a mesh
    /// turned the Find meshes button into "Searching...", which says the wrong thing is happening
    /// while saying nothing about the thing that is.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusyWithDirectory))]
    [NotifyPropertyChangedFor(nameof(MembershipText))]
    private bool _isJoining;

    /// <summary>True while either directory action is in flight, so neither starts twice.</summary>
    public bool IsBusyWithDirectory => IsSearchingDirectory || IsJoining;

    /// <summary>What the find button says, so a search in progress is visible on the button itself.</summary>
    public string FindMeshesText => IsSearchingDirectory ? "Searching..." : "Find meshes";

    /// <summary>
    /// Asks the public directory what meshes exist and puts them in the table.
    /// </summary>
    /// <remarks>
    /// On the button and nowhere else. This is the only thing in the application that reaches past
    /// the local network without a model being run, so it happens when somebody asks and never as
    /// a side effect of opening a tab or starting up.
    ///
    /// Results replace the previous ones rather than accumulating, because a directory listing is
    /// a reading of a moment and a mesh that has gone should go with it.
    /// </remarks>
    [RelayCommand]
    private async Task FindMeshesAsync()
    {
        if (_directory is null || IsBusyWithDirectory)
        {
            return;
        }

        IsSearchingDirectory = true;

        try
        {
            var found = await _directory.ListAsync(CancellationToken.None).ConfigureAwait(true);

            _discovered.Clear();

            foreach (var mesh in found)
            {
                _discovered.Add(new DiscoveredMeshRow(mesh));
            }

            Say(
                found.Count == 1 ? "1 mesh found" : $"{found.Count} meshes found",
                found.Count == 0
                    ? "Meshes only appear here while they are publishing themselves."
                    : "They are in the table, above your own models.");

            // Rebuild, not filter. The table is assembled in one place, and what was discovered is
            // put into it there; filtering only decides which of an already assembled list shows,
            // so on its own it kept filtering a list nothing had been added to.
            RebuildRows();
        }
        finally
        {
            IsSearchingDirectory = false;
        }
    }

    /// <summary>
    /// Joins the mesh the inspector is showing.
    /// </summary>
    /// <remarks>
    /// The token is fetched now rather than kept from the listing, because the listing prints it
    /// truncated and a truncated token is not one. A mesh that named itself is asked for by name;
    /// one that did not is asked for by a model it serves, which is the best the directory can
    /// express and is said on the panel rather than hidden here.
    /// </remarks>
    [RelayCommand]
    private async Task JoinDiscoveredAsync()
    {
        if (_directory is null || SelectedDirectoryMesh is not { } mesh || IsBusyWithDirectory)
        {
            return;
        }

        IsJoining = true;
        JoiningName = mesh.DisplayName;

        try
        {
            var token = await _directory
                .ResolveTokenAsync(
                    mesh.HasName ? mesh.Name : null,
                    mesh.Serving.FirstOrDefault(),
                    CancellationToken.None)
                .ConfigureAwait(true);

            if (token is null)
            {
                Say(
                    "Could not join that mesh",
                    "The directory had no invite for it. It may have stopped publishing since the list was taken.",
                    problem: true);

                return;
            }

            // Added rather than assigned, so joining a second mesh is joining a second mesh.
            if (!Joined.Any(m => string.Equals(m.Token, token, StringComparison.Ordinal)))
            {
                Joined.Add(new JoinedMesh(mesh.DisplayName, token, DateTimeOffset.Now));
            }

            SaveSettings();

            OnPropertyChanged(nameof(HasJoined));
            OnPropertyChanged(nameof(MembershipText));

            // Joining is a request to be in that mesh, so it starts the node rather than only
            // saving the invite. Apply on its own restarts a node that is up and does nothing at
            // all to one that is down, which is exactly the case somebody is in when they have
            // just found a mesh worth joining.
            if (!Mesh.IsRunning)
            {
                try
                {
                    await Mesh.StartAsync(CancellationToken.None).ConfigureAwait(true);
                    Say($"Joined {mesh.DisplayName}", "The node started in it. Its models appear here as the mesh reports them.");
                }
                catch (ModelClientException ex)
                {
                    Say("Joined, but the node would not start", ex.Message, problem: true);
                }

                return;
            }

            await ApplySettingsAsync().ConfigureAwait(true);

            Say($"Joined {mesh.DisplayName}", "The node restarted in it. Its models appear here as the mesh reports them.");
        }
        finally
        {
            IsJoining = false;
            OnPropertyChanged(nameof(MembershipText));
        }
    }

    /// <summary>Starts or stops this install's mesh node.</summary>
    [RelayCommand]
    private async Task ToggleMeshAsync()
    {
        try
        {
            if (Mesh.IsRunning)
            {
                await Mesh.StopAsync();
            }
            else
            {
                SaveSettings();
                await Mesh.StartAsync(CancellationToken.None);
            }
        }
        catch (ModelClientException ex)
        {
            _feed.Error("Mesh node failed", ex.Message);
        }
    }

    /// <summary>
    /// What the apply button says, which depends on what pressing it can actually do.
    /// </summary>
    /// <remarks>
    /// A stopped node has nothing to restart, so a button promising a restart did the saving half
    /// in silence and looked broken. Every field on the panel says changes take effect when you
    /// apply and restart, so pressing it and seeing nothing at all is the worst possible answer.
    /// </remarks>
    public string ApplyButtonText => Mesh.IsRunning ? "Apply and restart the node" : "Save these settings";

    /// <summary>Applies the contribution and membership settings, restarting the node if it is up.</summary>
    /// <remarks>
    /// Always says what it did. Saving without a word is indistinguishable from a button that is
    /// not wired to anything, which is what it was reported as.
    /// </remarks>
    [RelayCommand]
    private async Task ApplySettingsAsync()
    {
        SaveSettings();

        if (!Mesh.IsRunning)
        {
            Say(
                "Saved",
                "The node is not running, so nothing was restarted. Start it and it will come up with these settings.");

            return;
        }

        try
        {
            await Mesh.StopAsync();
            await Mesh.StartAsync(CancellationToken.None);

            Say("Applied", "The node restarted, so it is running with these settings now.");
        }
        catch (ModelClientException ex)
        {
            Say("The mesh node failed", ex.Message, problem: true);
        }
    }

    /// <summary>Opens or closes the join form.</summary>
    [RelayCommand]
    private void ToggleJoin() => IsJoinOpen = !IsJoinOpen;

    /// <summary>Joins the mesh the pasted invite token describes.</summary>
    [RelayCommand]
    private async Task JoinMeshAsync()
    {
        if (string.IsNullOrWhiteSpace(TypedToken))
        {
            Say("Mesh not joined", "Paste the invite token printed by the machine hosting the mesh.", problem: true);
            return;
        }

        if (!Joined.Any(m => string.Equals(m.Token, TypedToken.Trim(), StringComparison.Ordinal)))
        {
            Joined.Add(new JoinedMesh(string.Empty, TypedToken.Trim(), DateTimeOffset.Now));
        }

        TypedToken = string.Empty;
        OnPropertyChanged(nameof(HasJoined));
        OnPropertyChanged(nameof(MembershipText));

        IsJoinOpen = false;
        await ApplySettingsAsync();
    }

    /// <summary>Leaves the joined mesh and goes back to hosting a private one.</summary>
    [RelayCommand]
    private async Task LeaveMeshAsync()
    {
        Joined.Clear();
        JoiningName = string.Empty;

        OnPropertyChanged(nameof(HasJoined));
        OnPropertyChanged(nameof(MembershipText));
        await ApplySettingsAsync();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Mesh.Models.CollectionChanged -= OnModelsChanged;
        Mesh.Sources.CollectionChanged -= OnSourcesChanged;
        Mesh.PropertyChanged -= OnMeshChanged;

        foreach (var row in _rows.Values)
        {
            row.PropertyChanged -= OnRowChanged;
            row.Dispose();
        }

        _rows.Clear();
    }

    partial void OnFilterTextChanged(string value) => ApplyFilters();

    partial void OnContributeChanged(bool value)
    {
        OnPropertyChanged(nameof(CoverageSummary));
        OnPropertyChanged(nameof(IsOfferingNothing));
    }

    /// <summary>
    /// Only the reach changes. Growing it leaves the value alone, and shrinking it pulls a value
    /// that is now out of range back to the safe ceiling rather than leaving the slider showing a
    /// number it can no longer represent.
    /// </summary>
    partial void OnOfferAllMemoryChanged(bool value)
    {
        if (!value && MemoryShareGb > SafeCeilingGb)
        {
            MemoryShareGb = SafeCeilingGb;
        }
    }

    /// <summary>
    /// Picking a row in the table is picking a model, and it takes the inspector off whatever it
    /// was pinned to. Selection lives on the lists rather than behind commands so that the
    /// keyboard works in them for free.
    /// </summary>
    partial void OnSelectedRowChanged(INetworkRow? value)
    {
        SelectedSection = null;
        SelectedSource = null;
        SelectedModel = (value as NetworkModelRow)?.Model;
        SelectedDirectoryMesh = (value as DiscoveredMeshRow)?.Mesh;

        if (value is not null)
        {
            SelectedJoined = null;
        }
    }

    partial void OnSelectedSourceChanged(InferenceSource? value)
    {
        if (value is not null)
        {
            SelectedSection = null;
        }
    }

    /// <summary>
    /// The filter groups. Two of them infer their answer from what the engine does report rather
    /// than being told it directly, and each says so in its note rather than pretending otherwise.
    /// </summary>
    private IReadOnlyList<ModelFilterGroup> BuildFilterGroups() => new[]
    {
        new ModelFilterGroup(
            "STATUS",
            "Whether the network can run the model right now.",
            new[]
            {
                new ModelFilter("All", _ => true, ApplyFilterCommand, isSelected: true),
                new ModelFilter("Complete", r => r.Availability == ModelAvailability.Complete, ApplyFilterCommand),
                new ModelFilter("Starting", r => r.Availability == ModelAvailability.Starting, ApplyFilterCommand),
                new ModelFilter("Blocked", r => r.Availability == ModelAvailability.Blocked, ApplyFilterCommand),
                new ModelFilter("Found, not joined", r => r.Availability == ModelAvailability.NotJoined, ApplyFilterCommand)
            }),

        new ModelFilterGroup(
            "FORMAT",
            "Worked out from the quantization label. The mesh reports a quantization rather than a format, so "
            + "anything without a label a GGUF file would carry is left as not reported.",
            new[]
            {
                new ModelFilter("All", _ => true, ApplyFilterCommand, isSelected: true),
                new ModelFilter("GGUF", r => r.LooksLikeGguf, ApplyFilterCommand),
                new ModelFilter("Not reported", r => !r.LooksLikeGguf, ApplyFilterCommand)
            }),

        new ModelFilterGroup(
            "PROVIDER",
            "Where the model is served from. Cloud models are set up on a model node and are not part of "
            + "the mesh, so none appear here.",
            new[]
            {
                new ModelFilter("All", _ => true, ApplyFilterCommand, isSelected: true),
                new ModelFilter("Mesh", _ => true, ApplyFilterCommand),
                new ModelFilter("Cloud", _ => false, ApplyFilterCommand)
            }),

        new ModelFilterGroup(
            "SHARING",
            "A private mesh is joined by invitation, so everything in it is invite only. Publishing the "
            + "mesh makes all of it public at once.",
            new[]
            {
                new ModelFilter("All", _ => true, ApplyFilterCommand, isSelected: true),
                new ModelFilter("Public", r => r.Sharing == ModelSharing.Public, ApplyFilterCommand),
                new ModelFilter("Invite only", r => r.Sharing == ModelSharing.InviteOnly, ApplyFilterCommand)
            })
    };

    private void OnModelsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildRows();

        // Keep a sensible selection without ever stealing one the user made: pick the first row
        // when nothing is selected, and let go of a row that no longer exists.
        if (SelectedModel is not null && !Mesh.Models.Contains(SelectedModel))
        {
            SelectedModel = null;
            SelectedRow = null;
            SelectedSection = null;
        }

        if (SelectedModel is null)
        {
            SelectedModel = Mesh.Models.FirstOrDefault();
            SelectedRow = SelectedModel is null ? null : _rows.GetValueOrDefault(SelectedModel);
        }
    }

    private void OnSourcesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Machines.Clear();

        foreach (var source in Mesh.Sources)
        {
            Machines.Add(source);
        }

        if (SelectedSource is not null && !Mesh.Sources.Contains(SelectedSource))
        {
            SelectedSource = null;
        }

        OnPropertyChanged(nameof(CoverageSummary));
    }

    private void OnMeshChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MeshManager.IsPublic):
                foreach (var row in _rows.Values)
                {
                    row.RefreshMeshState();
                }

                ApplyFilters();
                RefreshHosted();
                break;

            case nameof(MeshManager.InviteToken):
                OnPropertyChanged(nameof(InviteToken));
                RefreshHosted();
                break;

            case nameof(MeshManager.MeshName):
                RefreshHosted();
                break;

            case nameof(MeshManager.DaemonState):
            case nameof(MeshManager.LlamaReady):
            case nameof(MeshManager.IsAttached):
                RefreshJoinedStates();
                break;

            case nameof(MeshManager.State):
                OnPropertyChanged(nameof(CoverageSummary));

                // Both buttons say what pressing them will do, and both answers depend on whether
                // there is a node up. So does every joined mesh's own state.
                OnPropertyChanged(nameof(ApplyButtonText));
                OnPropertyChanged(nameof(StartButtonText));
                RefreshJoinedStates();
                RefreshHosted();
                break;
        }
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A row republishes everything when the engine updates it, so the counts and the ordering
        // are recomputed rather than guessed at from which property changed.
        RefreshCounts();
        OnPropertyChanged(nameof(CoverageSummary));
    }

    private void RebuildRows()
    {
        var wanted = Mesh.Models.ToList();

        foreach (var gone in _rows.Keys.Except(wanted).ToList())
        {
            _rows[gone].PropertyChanged -= OnRowChanged;
            _rows[gone].Dispose();
            _rows.Remove(gone);
        }

        Rows.Clear();

        // What the directory listed goes in the same table, above what the mesh itself reports,
        // because a mesh you could join is an answer to "what can I reach" with one more step
        // attached and a second table would make somebody guess which one to look in first.
        foreach (var discovered in _discovered)
        {
            Rows.Add(discovered);
        }

        foreach (var model in wanted)
        {
            if (!_rows.TryGetValue(model, out var row))
            {
                row = new NetworkModelRow(model, () => Mesh.IsPublic);
                row.PropertyChanged += OnRowChanged;
                _rows[model] = row;
            }

            Rows.Add(row);
        }

        ApplyFilters();
        OnPropertyChanged(nameof(CoverageSummary));
    }

    private void RefreshCounts()
    {
        foreach (var group in Groups)
        {
            foreach (var filter in group.Filters)
            {
                filter.Count = Rows.Count(filter.Keeps);
            }
        }
    }

    private void ApplyFilters()
    {
        RefreshCounts();

        var text = FilterText.Trim();

        IEnumerable<INetworkRow> kept = Rows.Where(row =>
            Groups.All(group => group.Keeps(row))
            && (text.Length == 0
                || row.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || row.Quantisation.Contains(text, StringComparison.OrdinalIgnoreCase)));

        kept = SortDescending
            ? kept.OrderByDescending(r => r.SortKey(SortColumn))
            : kept.OrderBy(r => r.SortKey(SortColumn));

        VisibleRows.Clear();

        foreach (var row in kept)
        {
            VisibleRows.Add(row);
        }
    }

    private void SaveSettings()
    {
        _config.MeshContribute = Contribute;
        _config.MeshPublish = Publish;
        _config.MeshName = string.IsNullOrWhiteSpace(MeshName) ? "LocalNEXUS" : MeshName.Trim();
        _config.MeshJoined = Joined
            .Select(m => new JoinedMeshRecord { Name = m.Name, Token = m.Token, JoinedAt = m.JoinedAt })
            .ToList();
        _config.MeshOfferedModelPaths = OfferedModels.Where(m => m.IsOffered).Select(m => m.Path).ToList();
        _config.MeshOfferAllMemory = OfferAllMemory;
        _config.MeshMaxVramGb = MemoryShareGb;
        _config.MeshApiPort = ParsePort(ApiPort, _config.MeshApiPort);
        _config.Save();
    }

    /// <summary>Rebuilds the offer list, keeping every tick that still points at a model on disk.</summary>
    private void RebuildOfferedModels()
    {
        var offered = new HashSet<string>(_config.MeshOfferedModelPaths, StringComparer.OrdinalIgnoreCase);

        foreach (var row in OfferedModels)
        {
            row.PropertyChanged -= OnOfferedModelChanged;
        }

        OfferedModels.Clear();

        foreach (var model in Catalog.Models)
        {
            var row = new OfferedModelViewModel(model, offered.Contains(model.Path), OnOfferChanged);
            row.PropertyChanged += OnOfferedModelChanged;
            OfferedModels.Add(row);
        }

        OnOfferChanged();
    }

    private void OnOfferedModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OfferedModelViewModel.IsOffered))
        {
            OnPropertyChanged(nameof(OfferedCount));
            OnPropertyChanged(nameof(IsOfferingNothing));
        }
    }

    /// <summary>
    /// Saves the offer as it changes, so a tick is not something that has to be applied twice.
    /// The node still needs restarting for it to take effect, which the panel says.
    /// </summary>
    private void OnOfferChanged()
    {
        _config.MeshOfferedModelPaths = OfferedModels.Where(m => m.IsOffered).Select(m => m.Path).ToList();
        _config.Save();

        OnPropertyChanged(nameof(OfferedCount));
        OnPropertyChanged(nameof(IsOfferingNothing));
    }

    private int ParsePort(string text, int fallback)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed is > 0 and < 65536)
        {
            return parsed;
        }

        _feed.Error("Port ignored", $"The port has to be a number between 1 and 65535. Keeping {fallback}.");
        return fallback;
    }
}
