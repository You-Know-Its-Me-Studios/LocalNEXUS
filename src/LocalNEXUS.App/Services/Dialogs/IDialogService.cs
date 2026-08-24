namespace LocalNEXUS.App.Services.Dialogs;

/// <summary>
/// Shows the operating system dialogs the application needs.
/// </summary>
/// <remarks>
/// View models call this instead of touching <c>Microsoft.Win32</c> or <c>MessageBox</c>
/// directly, which keeps them free of window handles and testable in isolation.
/// </remarks>
public interface IDialogService
{
    /// <summary>Asks the user to pick a folder. Returns null when they cancel.</summary>
    string? PickFolder(string title, string? initialDirectory = null);

    /// <summary>Asks the user where to save a file. Returns null when they cancel.</summary>
    string? PickSaveFile(string title, string defaultFileName, string filter, string? initialDirectory = null);

    /// <summary>Asks the user which file to open. Returns null when they cancel.</summary>
    string? PickOpenFile(string title, string filter, string? initialDirectory = null);

    /// <summary>Reports a problem the user needs to see even if they are not watching the feed.</summary>
    void ShowError(string title, string message);

    /// <summary>
    /// Asks a yes or no question the user has to see, whatever they are looking at.
    /// </summary>
    /// <remarks>
    /// Separate from the activity feed's confirmation, which is the right place for a question
    /// arising from a run: it appears in the transcript beside the work that raised it. This is
    /// for a question asked when there may be no run, no project and no feed on screen at all.
    /// The feed only renders inside the Workspace, so a question asked at startup went into a
    /// panel nobody could see and waited for an answer nobody could give.
    /// </remarks>
    bool Confirm(string title, string message);

    /// <summary>Opens a folder in Explorer. Does nothing when the folder is missing.</summary>
    void OpenFolderInExplorer(string folder);

    /// <summary>Opens a file in whatever the system uses to edit it. Does nothing when it is missing.</summary>
    void OpenFileInEditor(string file);

    /// <summary>
    /// Puts text on the clipboard. Behind the service because the clipboard is a shared operating
    /// system resource that another process can have open, so it can fail and a view model should
    /// not be the thing that knows that.
    /// </summary>
    void CopyToClipboard(string text);
}
