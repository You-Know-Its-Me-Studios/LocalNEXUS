using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalNEXUS.App.Models.Extensions;

namespace LocalNEXUS.App.Services.Extensions;

/// <summary>
/// Answers whether an extension's prerequisites are met, before it is added rather than after.
/// </summary>
/// <remarks>
/// The order matters: nothing is registered until this passes or the user has been told exactly
/// what is missing and has chosen to fix it. An extension that is registered but cannot start is
/// a thing somebody has to debug later, and the whole point of checking first is that nobody
/// ever has to.
/// <para>
/// The three kinds are not interchangeable. An executable can be installed. A Unity package can
/// be read but not installed, because installing it means Unity resolving and importing it and
/// this application does not drive the editor. Whether the editor is running cannot even be
/// determined here, only attempted, so it is reported as a thing to go and do.
/// </para>
/// </remarks>
public sealed class PrerequisiteChecker
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Checks every prerequisite of a manifest against the machine and the open project.</summary>
    public IReadOnlyList<PrerequisiteResult> Check(ExtensionManifest manifest, string? projectPath)
        => manifest.Prerequisites.Select(p => Check(p, projectPath)).ToList();

    /// <summary>Checks one prerequisite.</summary>
    public PrerequisiteResult Check(ExtensionPrerequisite prerequisite, string? projectPath) => prerequisite.Kind switch
    {
        PrerequisiteKind.Executable => CheckExecutable(prerequisite),
        PrerequisiteKind.UnityPackage => CheckUnityPackage(prerequisite, projectPath),
        _ => CheckUnityEditor(prerequisite)
    };

    private static PrerequisiteResult CheckExecutable(ExtensionPrerequisite prerequisite)
    {
        // An absolute path is checked as a file. Anything else is looked for on the path, which
        // is what a command in a manifest usually is.
        if (Path.IsPathRooted(prerequisite.Name))
        {
            return File.Exists(prerequisite.Name)
                ? new PrerequisiteResult(prerequisite, true, prerequisite.Name)
                : new PrerequisiteResult(prerequisite, false, $"Not found at {prerequisite.Name}.");
        }

        var found = Resolve(prerequisite.Name);

        if (found is null)
        {
            return new PrerequisiteResult(prerequisite, false, $"'{prerequisite.Name}' is not on the path.");
        }

        var version = TryVersion(prerequisite.Name);

        return new PrerequisiteResult(
            prerequisite,
            true,
            version is null ? found : $"{version} at {found}");
    }

    private static PrerequisiteResult CheckUnityPackage(ExtensionPrerequisite prerequisite, string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return new PrerequisiteResult(prerequisite, false, "No project is open, so its packages cannot be read.");
        }

        var manifestPath = Path.Combine(projectPath, "Packages", "manifest.json");

        if (!File.Exists(manifestPath))
        {
            return new PrerequisiteResult(
                prerequisite,
                false,
                $"{manifestPath} does not exist, so this does not look like a Unity project.");
        }

        try
        {
            if (JsonNode.Parse(File.ReadAllText(manifestPath)) is not JsonObject root
                || root["dependencies"] is not JsonObject dependencies)
            {
                return new PrerequisiteResult(prerequisite, false, $"{manifestPath} has no dependencies section.");
            }

            foreach (var pair in dependencies)
            {
                if (string.Equals(pair.Key, prerequisite.Name, StringComparison.OrdinalIgnoreCase))
                {
                    var version = pair.Value?.GetValue<string>() ?? "present";
                    return new PrerequisiteResult(prerequisite, true, version);
                }
            }

            return new PrerequisiteResult(
                prerequisite,
                false,
                $"'{prerequisite.Name}' is not in the project's packages. Add it in Unity through the package manager.");
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new PrerequisiteResult(prerequisite, false, $"{manifestPath} could not be read: {ex.Message}");
        }
    }

    private static PrerequisiteResult CheckUnityEditor(ExtensionPrerequisite prerequisite)
    {
        // Whether the right editor is up, on the right project, with the right package loaded, is
        // only knowable by connecting. A process called Unity is evidence and not proof, so it is
        // reported as evidence.
        var running = Process.GetProcessesByName("Unity").Length > 0;

        return new PrerequisiteResult(
            prerequisite,
            running,
            running
                ? "A Unity editor is running. Whether it is this project can only be found out by connecting."
                : "No Unity editor is running. Open the project in Unity before using this extension.");
    }

    private static string? Resolve(string command)
    {
        var extensions = new[] { string.Empty, ".exe", ".cmd", ".bat" };
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();

        foreach (var directory in paths)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            foreach (var extension in extensions)
            {
                try
                {
                    var candidate = Path.Combine(directory, command + extension);

                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry is not this application's problem to solve.
                }
            }
        }

        return null;
    }

    private static string? TryVersion(string command)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            // The same resolution the launcher uses, so a prerequisite is not reported missing for
            // the one reason that has nothing to do with whether it is installed.
            Services.Processes.CommandLauncher
                .Resolve(command)
                .ApplyTo(startInfo, new[] { "--version" });

            using var process = Process.Start(startInfo);

            if (process is null)
            {
                return null;
            }

            if (!process.WaitForExit((int)ProbeTimeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Already gone.
                }

                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            return string.IsNullOrWhiteSpace(output) ? null : output.Split('\n')[0].Trim();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
