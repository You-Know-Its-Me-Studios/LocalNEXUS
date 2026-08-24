using System.IO;
using Microsoft.Win32;

namespace LocalNEXUS.Installer.Services;

/// <summary>
/// Puts the install in Add or remove programs, and takes it out again.
/// </summary>
/// <remarks>
/// Under HKEY_CURRENT_USER, which is where a per user install belongs and is why none of this
/// needs elevation. The uninstall command is this same installer, run against itself with a
/// flag, so there is one program to maintain rather than two.
/// </remarks>
public static class UninstallRegistrar
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalNEXUS";

    /// <summary>The switch that turns this installer into an uninstaller.</summary>
    public const string UninstallSwitch = "--uninstall";

    /// <summary>Records the install so Windows can offer to remove it.</summary>
    public static void Register(string version, long estimatedBytes)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);

            if (key is null)
            {
                return;
            }

            key.SetValue("DisplayName", "LocalNEXUS");
            key.SetValue("DisplayVersion", version);
            key.SetValue("Publisher", "You Know Its Me Studios");
            key.SetValue("DisplayIcon", InstallLocations.AppExecutable);
            key.SetValue("InstallLocation", InstallLocations.InstallRoot);
            key.SetValue("UninstallString", $"\"{InstallLocations.UninstallerPath}\" {UninstallSwitch}");
            key.SetValue("QuietUninstallString", $"\"{InstallLocations.UninstallerPath}\" {UninstallSwitch} --silent");
            key.SetValue("URLInfoAbout", "https://github.com/You-Know-Its-Me-Studios/LocalNEXUS");
            key.SetValue("NoModify", 0, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 0, RegistryValueKind.DWord);
            key.SetValue("ModifyPath", $"\"{InstallLocations.UninstallerPath}\"");
            key.SetValue("EstimatedSize", (int)Math.Max(1, estimatedBytes / 1024), RegistryValueKind.DWord);
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            throw new SetupException(
                "The install could not be recorded in Add or remove programs. Everything else was installed and " +
                $"works; removing it later means deleting {InstallLocations.InstallRoot} by hand.",
                ex);
        }
    }

    /// <summary>Removes the record. Silent when it is not there.</summary>
    public static void Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // The files are what matter. A stale entry is a nuisance and not a failure.
        }
    }

    /// <summary>The version recorded by a previous install, or null when there is none.</summary>
    public static string? InstalledVersion()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue("DisplayVersion") as string;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return null;
        }
    }
}
