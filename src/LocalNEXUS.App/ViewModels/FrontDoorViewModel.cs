using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// What the application asks before anything else: what are you working on.
/// </summary>
/// <remarks>
/// A graph with no project cannot do anything. The index is empty, so Triage has nothing to plan
/// against; the duplicate guard has nothing to compare a proposed type to; the compiler has no
/// references to compile against; and an Output node has nowhere to write. An empty workspace with
/// no project does not look empty, it looks broken, so there is no route to one.
///
/// The recent list is the answer almost every time, because almost every session is a return to
/// something. It is first, largest and needs one click.
///
/// The one way past without a project is contributing this machine to the mesh, which genuinely
/// needs none: serving layers to somebody else's run is not work on a codebase. That takes the
/// Network section and leaves the door closed.
///
/// Nothing here waits on the door. Any route that opens a project closes it, including one opened
/// by a tool call arriving over MCP, because it watches the project rather than its own commands.
/// </remarks>
public sealed partial class FrontDoorViewModel : ObservableObject
{
    private readonly RecentProjectsService _recents;
    private readonly ProjectService _project;
    private readonly IDialogService _dialogs;
    private readonly AppConfig _config;
    private readonly Action<string> _open;
    private readonly Action _showNetwork;

    /// <summary>True while the door is showing, which is until a project is open or the mesh is chosen.</summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>What went wrong with the last choice, or null. Shown on the door rather than in a dialog.</summary>
    [ObservableProperty]
    private string? _problem;

    public FrontDoorViewModel(
        RecentProjectsService recents,
        ProjectService project,
        IDialogService dialogs,
        AppConfig config,
        Action<string> open,
        Action showNetwork)
    {
        _recents = recents;
        _project = project;
        _dialogs = dialogs;
        _config = config;
        _open = open;
        _showNetwork = showNetwork;

        // Watching the project rather than the commands here, so that a project opened by any
        // route closes the door and lands in the recent list. A tool call arriving over MCP is
        // exactly such a route, and it needs nothing added to it to work.
        _project.PropertyChanged += OnProjectChanged;
    }

    /// <summary>The projects to return to, most recent first.</summary>
    public RecentProjectsService Recents => _recents;

    /// <summary>True when nothing has ever been opened here, which reads differently.</summary>
    public bool IsFirstRun => _recents.IsEmpty;

    /// <summary>Shows the door, which is what launching does.</summary>
    public void Show()
    {
        _recents.Refresh();
        Problem = null;

        OnPropertyChanged(nameof(IsFirstRun));

        IsOpen = true;
    }

    /// <summary>Opens one of the recent projects.</summary>
    [RelayCommand]
    private void OpenRecent(RecentProjectEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        if (entry.IsMissing)
        {
            // Said rather than attempted. The row already shows it, and the button beside it is
            // the thing to do about it.
            Problem = $"{entry.Path} is not there any more. Remove it from the list, or put it back.";
            return;
        }

        Open(entry.Path);
    }

    /// <summary>Forgets a project that is no longer where it was.</summary>
    [RelayCommand]
    private void Forget(RecentProjectEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        _recents.Remove(entry.Path);
        Problem = null;

        OnPropertyChanged(nameof(IsFirstRun));
    }

    /// <summary>Picks an existing project folder.</summary>
    [RelayCommand]
    private void OpenProject()
    {
        if (_dialogs.PickFolder("Choose a project folder", _config.LastProjectPath) is { } folder)
        {
            Open(folder);
        }
    }

    /// <summary>
    /// Picks a folder that is not a project yet, and makes it one.
    /// </summary>
    /// <remarks>
    /// The same mechanic as opening one, and a different question. Nothing is created on disk
    /// here: what makes a folder a project is that this application has been told to work in it,
    /// and the setup window that follows is where it is asked what that means. The folder picker
    /// can make a new folder, which is the case this exists for.
    /// </remarks>
    [RelayCommand]
    private void StartProject()
    {
        if (_dialogs.PickFolder("Choose a folder to work in", _config.LastProjectPath) is { } folder)
        {
            Open(folder);
        }
    }

    /// <summary>
    /// Leaves without a project, for somebody who is only lending this machine to the mesh.
    /// </summary>
    /// <remarks>
    /// The only dismissal, and it goes somewhere rather than nowhere. Landing on an empty
    /// workspace would be the state this whole thing exists to prevent.
    /// </remarks>
    [RelayCommand]
    private void ContributeOnly()
    {
        IsOpen = false;
        _showNetwork();
    }

    private void Open(string folder)
    {
        Problem = null;

        try
        {
            _open(folder);
        }
        catch (Exception ex) when (ex is System.IO.DirectoryNotFoundException
                                       or System.IO.IOException
                                       or UnauthorizedAccessException)
        {
            // The door stays up, because a failed open leaves nothing open and there is nothing
            // behind it to go back to.
            Problem = ex.Message;
            _recents.Refresh();
        }
    }

    /// <summary>
    /// Records the open project and closes the door.
    /// </summary>
    /// <remarks>
    /// Called from the ordinary open path and from the watcher, which is what covers a project
    /// opened over MCP. Recording the same path twice is not a second row, so calling it twice
    /// costs nothing and missing one of the two routes would.
    /// </remarks>
    public void NoteProjectOpened()
    {
        if (!_project.HasProject)
        {
            return;
        }

        _recents.Record(_project.ProjectPath);

        Problem = null;
        IsOpen = false;
    }

    private void OnProjectChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectService.HasProject))
        {
            NoteProjectOpened();
        }
    }
}
