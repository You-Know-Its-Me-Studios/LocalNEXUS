using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models.Extensions;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.Extensions;

namespace LocalNEXUS.App.ViewModels;

/// <summary>Which shelf of the extensions panel is being looked at.</summary>
public enum ExtensionSource
{
    /// <summary>The curated entries, none of which are installed until somebody installs one.</summary>
    Presets,

    /// <summary>What this project has registered.</summary>
    Installed
}

/// <summary>
/// The extensions panel: a sources rail, a list, and a details pane.
/// </summary>
/// <remarks>
/// Laid out after Unity's package manager, which is the right shape because it already works for
/// this audience and because it scales past Unity, which is where this is going.
/// <para>
/// The details pane exists to make a misconfigured extension diagnosable without digging. Showing
/// the real command and arguments is the single most useful thing on it: an extension that will
/// not start is almost always one whose command is wrong, and that is invisible everywhere else.
/// </para>
/// </remarks>
public sealed partial class ExtensionsViewModel : ObservableObject
{
    private readonly ExtensionRegistry _registry;
    private readonly ExtensionHost _host;
    private readonly ExtensionInstaller _installer;
    private readonly ExtensionStarter _starter;
    private readonly PrerequisiteChecker _prerequisites;
    private readonly IDialogService _dialogs;
    private readonly IAddExtensionDialog _addDialog;
    private readonly IActivityFeed _feed;

    /// <summary>Which shelf is showing.</summary>
    [ObservableProperty]
    private ExtensionSource _source = ExtensionSource.Installed;

    /// <summary>The extension whose details are showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(HasNothingSelected))]
    [NotifyPropertyChangedFor(nameof(SelectedPrerequisites))]
    private InstalledExtension? _selected;

    /// <summary>The preset whose details are showing, when the presets shelf is up.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPreset))]
    [NotifyPropertyChangedFor(nameof(HasNothingSelected))]
    private ExtensionManifest? _selectedPreset;

    /// <summary>What a long running operation is doing right now.</summary>
    [ObservableProperty]
    private string? _busyMessage;

    /// <summary>Narrows both lists. Empty shows everything.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisiblePresets))]
    [NotifyPropertyChangedFor(nameof(VisibleInstalled))]
    private string _filter = string.Empty;

    public ExtensionsViewModel(
        ExtensionRegistry registry,
        ExtensionHost host,
        ExtensionInstaller installer,
        ExtensionStarter starter,
        PrerequisiteChecker prerequisites,
        IDialogService dialogs,
        IAddExtensionDialog addDialog,
        IActivityFeed feed)
    {
        _registry = registry;
        _host = host;
        _installer = installer;
        _starter = starter;
        _prerequisites = prerequisites;
        _dialogs = dialogs;
        _addDialog = addDialog;
        _feed = feed;

        Presets = new ObservableCollection<ExtensionManifest>(ExtensionPresets.All);

        _registry.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ExtensionRegistry.ProjectPath))
            {
                OnPropertyChanged(nameof(HasProject));
                OnPropertyChanged(nameof(EmptyMessage));
                Selected = null;
            }
        };

        _registry.Extensions.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(EmptyMessage));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(ShowEmptyMessage));
            OnPropertyChanged(nameof(VisibleInstalled));
            OnPropertyChanged(nameof(FilterHidesEverything));
        };
    }

    /// <summary>The curated entries. None of them are installed until somebody installs one.</summary>
    public ObservableCollection<ExtensionManifest> Presets { get; }

    /// <summary>What this project has registered.</summary>
    public ObservableCollection<InstalledExtension> Installed => _registry.Extensions;

    /// <summary>The presets the filter leaves showing.</summary>
    public IEnumerable<ExtensionManifest> VisiblePresets =>
        Presets.Where(p => Matches(p.Name, p.Description, p.Id));

    /// <summary>The installed extensions the filter leaves showing.</summary>
    public IEnumerable<InstalledExtension> VisibleInstalled =>
        Installed.Where(e => Matches(e.Manifest.Name, e.Manifest.Description, e.Manifest.Id));

    /// <summary>True when the filter is hiding everything rather than the shelf being empty.</summary>
    public bool FilterHidesEverything
        => Filter.Length > 0
           && (Source == ExtensionSource.Presets ? !VisiblePresets.Any() : !VisibleInstalled.Any());

    /// <summary>True when a project is open, which is what extensions are registered against.</summary>
    public bool HasProject => _registry.HasProject;

    /// <summary>True when the installed list is empty, which is an ordinary state.</summary>
    public bool IsEmpty => _registry.Extensions.Count == 0;

    /// <summary>
    /// True when the empty message should be drawn.
    /// </summary>
    /// <remarks>
    /// Only on the installed shelf. The presets shelf is never empty, so a message saying
    /// nothing is installed has nothing to do with it and drew straight over the list.
    /// </remarks>
    public bool ShowEmptyMessage => ShowInstalled && IsEmpty;

    /// <summary>True when something is selected in the installed list.</summary>
    public bool HasSelection => Selected is not null;

    /// <summary>True when a preset is selected.</summary>
    public bool HasSelectedPreset => SelectedPreset is not null;

    /// <summary>True when the details pane has nothing to show yet.</summary>
    public bool HasNothingSelected => !HasSelection && !HasSelectedPreset;

    /// <summary>
    /// Choosing something installed puts away whatever preset was showing.
    /// </summary>
    /// <remarks>
    /// One details pane, one thing in it. The two panels are drawn in the same place, each shown
    /// by whether its own selection is set, so both selections being set at once drew both panels
    /// on top of each other. Clearing the other one is what makes the pane hold one thing rather
    /// than whichever was set most recently plus whatever was set before it.
    ///
    /// Only when something was chosen. Clearing on a clear would have the two of them putting each
    /// other away in turn.
    /// </remarks>
    partial void OnSelectedChanged(InstalledExtension? value)
    {
        if (value is not null)
        {
            SelectedPreset = null;
        }
    }

    /// <summary>And choosing a preset puts away whatever installed extension was showing.</summary>
    partial void OnSelectedPresetChanged(ExtensionManifest? value)
    {
        if (value is not null)
        {
            Selected = null;
        }
    }

    /// <summary>Whether the prerequisites of the selection are met, checked when it is shown.</summary>
    public IReadOnlyList<PrerequisiteResult> SelectedPrerequisites => Selected is null
        ? Array.Empty<PrerequisiteResult>()
        : _prerequisites.Check(Selected.Manifest, _registry.ProjectPath);

    /// <summary>What to say when there is nothing in the list.</summary>
    public string EmptyMessage => HasProject
        ? "No extensions for this project yet. Install one from Presets, or add your own."
        : "Open a project first. Extensions belong to a project, because what they talk to does.";

    partial void OnSourceChanged(ExtensionSource value)
    {
        OnPropertyChanged(nameof(FilterHidesEverything));
        OnPropertyChanged(nameof(ShowPresets));
        OnPropertyChanged(nameof(ShowInstalled));
        OnPropertyChanged(nameof(ShowEmptyMessage));
    }

    /// <summary>True while the presets shelf is showing.</summary>
    public bool ShowPresets => Source == ExtensionSource.Presets;

    /// <summary>True while the installed shelf is showing.</summary>
    public bool ShowInstalled => Source == ExtensionSource.Installed;

    /// <summary>Case insensitive match across the fields worth searching.</summary>
    private bool Matches(params string?[] fields)
    {
        if (string.IsNullOrWhiteSpace(Filter))
        {
            return true;
        }

        return fields.Any(f => f is not null && f.Contains(Filter, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Installs one of the curated entries.</summary>
    [RelayCommand]
    private async Task InstallPresetAsync(ExtensionManifest? manifest)
    {
        if (manifest is null)
        {
            return;
        }

        await AddAsync(() => Task.FromResult(_installer.FromPreset(manifest)));
    }

    /// <summary>Adds an npm package, which is what most MCP servers are.</summary>
    [RelayCommand]
    private async Task AddNpmAsync()
    {
        if (_addDialog.Ask(AddExtensionMethod.Npm) is not { } request)
        {
            return;
        }

        await AddAsync(() => Task.FromResult(_installer.FromNpm(request.Value)));
    }

    /// <summary>Clones a repository and reads the manifest it carries.</summary>
    [RelayCommand]
    private async Task AddGitAsync()
    {
        if (_addDialog.Ask(AddExtensionMethod.Git) is not { } request)
        {
            return;
        }

        await AddAsync(ct => _installer.FromGitAsync(
            request.Value, new DelegateProgress<string>(m => BusyMessage = m), ct));
    }

    /// <summary>Adds a folder containing a manifest.</summary>
    [RelayCommand]
    private async Task AddDiskAsync()
    {
        var folder = _dialogs.PickFolder("Choose the extension folder");

        if (folder is null)
        {
            return;
        }

        await AddAsync(() => Task.FromResult(_installer.FromDisk(folder)));
    }

    /// <summary>Adds a raw command line, which is the route that always works.</summary>
    [RelayCommand]
    private async Task AddCommandAsync()
    {
        if (_addDialog.Ask(AddExtensionMethod.Command) is not { } request)
        {
            return;
        }

        var contracts = new List<ExtensionContract>();

        if (request.SpeaksMcp)
        {
            contracts.Add(ExtensionContract.Mcp);
        }

        if (request.SpeaksSpec)
        {
            contracts.Add(ExtensionContract.Spec);
        }

        if (request.SpeaksNode)
        {
            contracts.Add(ExtensionContract.Node);
        }

        var arguments = request.Arguments
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        await AddAsync(() => Task.FromResult(_installer.FromCommand(
            request.Name,
            request.Value,
            arguments,
            string.IsNullOrWhiteSpace(request.WorkingDirectory) ? null : request.WorkingDirectory,
            ParseEnvironment(request.Environment),
            contracts)));
    }

    /// <summary>
    /// Reads the environment box, one NAME=value per line.
    /// </summary>
    /// <remarks>
    /// A line with no equals sign is skipped rather than guessed at. Somebody who typed a bare
    /// name meant something, and inventing an empty value for it would be a worse answer than
    /// leaving it out.
    /// </remarks>
    private static readonly char[] NewLines = { (char)13, (char)10 };

    private static Dictionary<string, string>? ParseEnvironment(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in text.Split(NewLines, StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('=');

            if (separator <= 0)
            {
                continue;
            }

            parsed[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return parsed.Count == 0 ? null : parsed;
    }

    /// <summary>
    /// Starts an extension, reads what it can do, and shuts it down again.
    /// </summary>
    /// <remarks>
    /// The button remains, because there is a real reason to press it: an extension whose editor
    /// side was not running when the project opened is fixed by starting the editor and asking
    /// again, and nothing else in the application knows that happened.
    ///
    /// What it does is no longer written here. Opening a project asks every extension the same
    /// question automatically, and two copies of the asking would be two things to keep in step.
    /// </remarks>
    [RelayCommand]
    private async Task TestConnectAsync(InstalledExtension? extension)
    {
        if (extension is null)
        {
            return;
        }

        BusyMessage = $"Starting {extension.Manifest.Name}";

        try
        {
            await _starter.ConnectAsync(extension, CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            BusyMessage = null;
        }
    }

    /// <summary>Switches an extension off without removing it.</summary>
    [RelayCommand]
    private void Disable(InstalledExtension? extension)
    {
        if (extension is null)
        {
            return;
        }

        extension.IsEnabled = false;
        _host.Stop(extension.Manifest.Id);
        _registry.Save();
    }

    /// <summary>Switches an extension back on.</summary>
    [RelayCommand]
    private void Enable(InstalledExtension? extension)
    {
        if (extension is null)
        {
            return;
        }

        extension.IsEnabled = true;

        if (extension.State == ExtensionState.Failed)
        {
            // Enabling does not un-break it. It is still failed until it answers.
            extension.StateDetail += " Still failed. Fix the configuration and test connect.";
        }

        _registry.Save();
    }

    /// <summary>Removes an extension from this project.</summary>
    [RelayCommand]
    private void Remove(InstalledExtension? extension)
    {
        if (extension is null)
        {
            return;
        }

        _host.Stop(extension.Manifest.Id);
        _registry.Remove(extension);

        if (ReferenceEquals(Selected, extension))
        {
            Selected = null;
        }
    }

    /// <summary>Opens this extension's stderr log.</summary>
    [RelayCommand]
    private void ViewLogs(InstalledExtension? extension)
    {
        if (extension?.LogPath is { } path)
        {
            _dialogs.OpenFileInEditor(path);
            return;
        }

        _dialogs.ShowError(
            "No log yet",
            "This extension has not been started in this session, so it has not written anything.");
    }

    private Task AddAsync(Func<Task<InstalledExtension>> create)
        => AddAsync(_ => create());

    private async Task AddAsync(Func<CancellationToken, Task<InstalledExtension>> create)
    {
        if (!HasProject)
        {
            _dialogs.ShowError(
                "No project open",
                "Extensions are registered against a project, so open one first.");
            return;
        }

        try
        {
            var extension = await create(CancellationToken.None).ConfigureAwait(true);

            // Checked before anything is registered. An extension that is added and then found
            // to be unusable is a thing somebody has to debug later; this is the whole reason
            // the check happens here rather than at first use.
            var results = _prerequisites.Check(extension.Manifest, _registry.ProjectPath);
            var missing = results.Where(r => !r.Met).ToList();

            if (missing.Count > 0 && !await ResolveAsync(extension, missing).ConfigureAwait(true))
            {
                return;
            }

            extension.State = ExtensionState.Unreachable;
            extension.StateDetail = "Not started yet.";

            _registry.Add(extension);
            Selected = extension;
            Source = ExtensionSource.Installed;

            _feed.Info($"{extension.Manifest.Name} added", extension.Manifest.Launch.DisplayCommand);
        }
        catch (ExtensionException ex)
        {
            _dialogs.ShowError("Extension not added", ex.Message);
        }
        finally
        {
            BusyMessage = null;
        }
    }

    /// <summary>
    /// Offers to install what is missing. Declining installs nothing and adds nothing.
    /// </summary>
    private async Task<bool> ResolveAsync(InstalledExtension extension, IReadOnlyList<PrerequisiteResult> missing)
    {
        var installable = missing.Where(m => m.Prerequisite.CanInstall).ToList();
        var manual = missing.Where(m => !m.Prerequisite.CanInstall).ToList();

        if (manual.Count > 0)
        {
            // Nothing here can be installed from this application, so the honest thing is to say
            // what has to happen rather than offer a button that would not work.
            _dialogs.ShowError(
                $"{extension.Manifest.Name} needs something first",
                string.Join(
                    Environment.NewLine + Environment.NewLine,
                    manual.Select(m => $"{m.Prerequisite.Name}{Environment.NewLine}{m.Prerequisite.Reason}{Environment.NewLine}{m.Detail}")));
            return false;
        }

        var summary = string.Join(
            Environment.NewLine + Environment.NewLine,
            installable.Select(m => $"{m.Prerequisite.Name}{Environment.NewLine}{m.Prerequisite.Reason}"));

        var approved = await _feed
            .RequestConfirmationAsync(
                $"{extension.Manifest.Name} needs {(installable.Count == 1 ? "one thing" : $"{installable.Count} things")} installed",
                summary + Environment.NewLine + Environment.NewLine +
                "Install it now and add the extension, or cancel and nothing is changed.",
                CancellationToken.None)
            .ConfigureAwait(true);

        if (!approved)
        {
            _feed.Info(
                $"{extension.Manifest.Name} was not added",
                "Its prerequisites were declined, so nothing was installed and nothing was registered.");
            return false;
        }

        foreach (var result in installable)
        {
            BusyMessage = $"Installing {result.Prerequisite.Name}";

            try
            {
                await _installer
                    .InstallPrerequisiteAsync(
                        result.Prerequisite,
                        new DelegateProgress<string>(m => BusyMessage = m),
                        CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch (ExtensionException ex)
            {
                _dialogs.ShowError($"{result.Prerequisite.Name} was not installed", ex.Message);
                return false;
            }
        }

        return true;
    }
}
