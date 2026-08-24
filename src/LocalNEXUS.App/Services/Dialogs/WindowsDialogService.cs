using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace LocalNEXUS.App.Services.Dialogs;

/// <summary>The Windows implementation of <see cref="IDialogService"/>.</summary>
public sealed class WindowsDialogService : IDialogService
{
    /// <inheritdoc />
    public string? PickFolder(string title, string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };

        ApplyInitialDirectory(directory => dialog.InitialDirectory = directory, initialDirectory);

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    /// <inheritdoc />
    public string? PickSaveFile(string title, string defaultFileName, string filter, string? initialDirectory = null)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = defaultFileName,
            Filter = filter,
            AddExtension = false,
            OverwritePrompt = true
        };

        ApplyInitialDirectory(directory => dialog.InitialDirectory = directory, initialDirectory);

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public string? PickOpenFile(string title, string filter, string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false
        };

        ApplyInitialDirectory(directory => dialog.InitialDirectory = directory, initialDirectory);

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public void ShowError(string title, string message)
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    /// <inheritdoc />
    public bool Confirm(string title, string message)
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;

    /// <inheritdoc />
    public void OpenFolderInExplorer(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true
        });
    }

    /// <inheritdoc />
    public void OpenFileInEditor(string file)
    {
        if (!File.Exists(file))
        {
            return;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = true
        });
    }

    /// <inheritdoc />
    public void CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
        }
        catch (COMException)
        {
            // The clipboard is a shared resource and another process can hold it open. Failing to
            // copy an invite token is not worth interrupting anyone over, and the token is still
            // on screen to be selected by hand.
        }
    }

    private static void ApplyInitialDirectory(Action<string> assign, string? initialDirectory)
    {
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            assign(initialDirectory);
        }
    }
}
