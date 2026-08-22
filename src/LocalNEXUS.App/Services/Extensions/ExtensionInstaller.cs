using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalNEXUS.App.Models.Extensions;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.Services.Processes;

namespace LocalNEXUS.App.Services.Extensions;

/// <summary>
/// Turns each of the five ways of naming an extension into a manifest that can be registered.
/// </summary>
/// <remarks>
/// Modelled on Unity's package manager, which is the right model because it already solved this
/// for an audience that overlaps entirely: a curated shortlist for the common cases, a package
/// name for the registry, a git url, a folder, and a raw escape hatch that guarantees anything
/// works even when none of the shortcuts fit.
/// <para>
/// The escape hatch is not an afterthought. Every curated list is wrong about somebody, and a
/// hub whose answer to an unlisted server is "no" is not a hub.
/// </para>
/// </remarks>
public sealed class ExtensionInstaller
{
    private static readonly TimeSpan CloneTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(15);

    private readonly ChildProcessGroup _children;

    public ExtensionInstaller(ChildProcessGroup children) => _children = children;

    /// <summary>Where cloned and copied extension payloads live.</summary>
    public static string Root { get; } = Path.Combine(AppPaths.Root, "extensions");

    /// <summary>One of the curated entries, which needs nothing fetched.</summary>
    public InstalledExtension FromPreset(ExtensionManifest manifest)
        => new(manifest, ExtensionOrigin.Preset, manifest.Id);

    /// <summary>
    /// An npm package, run through the package runner rather than installed globally.
    /// </summary>
    /// <remarks>
    /// Most MCP servers are npm packages, and running them with the package runner means the
    /// version resolves per run and nothing is added to the machine behind the user's back.
    /// </remarks>
    public InstalledExtension FromNpm(string packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            throw new ExtensionException("No package name was given.");
        }

        var trimmed = packageName.Trim();

        return new InstalledExtension(
            new ExtensionManifest(
                Id: $"npm.{Sanitise(trimmed)}",
                Name: trimmed,
                Version: "resolved per run",
                Description: $"The npm package {trimmed}, run as an MCP server.",
                Author: null,
                Homepage: $"https://www.npmjs.com/package/{StripVersion(trimmed)}",
                Contracts: new[] { ExtensionContract.Mcp },
                Tools: Array.Empty<ToolContribution>(),
                Nodes: Array.Empty<NodeContribution>(),
                Prerequisites: new[]
                {
                    new ExtensionPrerequisite(
                        PrerequisiteKind.Executable,
                        "node",
                        "The package is run by Node.",
                        InstallCommand: "winget",
                        InstallArguments: new[]
                        {
                            "install", "--id", "OpenJS.NodeJS.LTS", "--exact",
                            "--silent", "--accept-package-agreements", "--accept-source-agreements"
                        })
                },
                Launch: new ExtensionLaunch("npx", new[] { "--yes", trimmed })),
            ExtensionOrigin.Npm,
            trimmed);
    }

    /// <summary>
    /// Clones a git repository and reads the manifest it carries.
    /// </summary>
    public async Task<InstalledExtension> FromGitAsync(
        string url,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ExtensionException("No repository url was given.");
        }

        var folder = Path.Combine(Root, Sanitise(url));

        if (Directory.Exists(folder))
        {
            // A second install of the same url replaces rather than accumulates.
            TryDelete(folder);
        }

        Directory.CreateDirectory(Root);
        progress?.Report($"Cloning {url}");

        var exit = await RunAsync(
            "git",
            new[] { "clone", "--depth", "1", url, folder },
            Root,
            CloneTimeout,
            progress,
            ct).ConfigureAwait(false);

        if (exit != 0)
        {
            TryDelete(folder);
            throw new ExtensionException(
                $"Cloning {url} failed with exit code {exit}. Check the url, and that git is installed and on the path.");
        }

        var extension = FromDisk(folder);
        return new InstalledExtension(extension.Manifest, ExtensionOrigin.Git, url);
    }

    /// <summary>A folder on disk holding a manifest.</summary>
    public InstalledExtension FromDisk(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            throw new ExtensionException($"There is no folder at {folder}.");
        }

        var manifestPath = Path.Combine(folder, ExtensionManifestJson.FileName);

        if (!File.Exists(manifestPath))
        {
            throw new ExtensionException(
                $"{folder} has no {ExtensionManifestJson.FileName}, so there is nothing saying what it contributes " +
                "or how to start it. Add one, or use the command option to launch it directly.");
        }

        ExtensionManifest manifest;

        try
        {
            if (JsonNode.Parse(File.ReadAllText(manifestPath)) is not JsonObject json)
            {
                throw new ExtensionException($"{manifestPath} is not a JSON object.");
            }

            manifest = ExtensionManifestJson.Read(json);
        }
        catch (JsonException ex)
        {
            throw new ExtensionException($"{manifestPath} is not valid JSON: {ex.Message}", ex);
        }
        catch (IOException ex)
        {
            throw new ExtensionException($"{manifestPath} could not be read: {ex.Message}", ex);
        }

        // A relative working directory in a manifest means relative to the extension, which is
        // the only interpretation that survives the folder being anywhere.
        var launch = manifest.Launch with
        {
            WorkingDirectory = manifest.Launch.WorkingDirectory is { Length: > 0 } declared
                ? Path.GetFullPath(Path.Combine(folder, declared))
                : folder
        };

        return new InstalledExtension(manifest with { Launch = launch }, ExtensionOrigin.Disk, folder);
    }

    /// <summary>
    /// A raw command line. The escape hatch that makes anything work.
    /// </summary>
    public InstalledExtension FromCommand(
        string name,
        string command,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        IReadOnlyList<ExtensionContract> contracts)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ExtensionException("No command was given.");
        }

        if (contracts.Count == 0)
        {
            throw new ExtensionException(
                "Say which contract this speaks. Without one the host has no way to talk to it.");
        }

        var label = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(command) : name.Trim();

        return new InstalledExtension(
            new ExtensionManifest(
                Id: $"command.{Sanitise(label)}",
                Name: label,
                Version: "unversioned",
                Description: "Added by command.",
                Author: null,
                Homepage: null,
                Contracts: contracts,
                Tools: Array.Empty<ToolContribution>(),
                Nodes: Array.Empty<NodeContribution>(),
                Prerequisites: Array.Empty<ExtensionPrerequisite>(),
                Launch: new ExtensionLaunch(command.Trim(), arguments, workingDirectory, environment)),
            ExtensionOrigin.Command,
            $"{command} {string.Join(' ', arguments)}".Trim());
    }

    /// <summary>
    /// Installs a prerequisite this application is able to install, reporting progress as it goes.
    /// </summary>
    /// <exception cref="ExtensionException">It cannot be installed, or the installer failed.</exception>
    public async Task InstallPrerequisiteAsync(
        ExtensionPrerequisite prerequisite,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        if (prerequisite.InstallCommand is null)
        {
            throw new ExtensionException(
                $"{prerequisite.Name} cannot be installed from here. {prerequisite.Reason}");
        }

        progress?.Report($"Installing {prerequisite.Name}");

        var exit = await RunAsync(
            prerequisite.InstallCommand,
            prerequisite.InstallArguments ?? Array.Empty<string>(),
            AppContext.BaseDirectory,
            InstallTimeout,
            progress,
            ct).ConfigureAwait(false);

        if (exit != 0)
        {
            throw new ExtensionException(
                $"Installing {prerequisite.Name} failed with exit code {exit}. " +
                $"The command was: {prerequisite.InstallCommand} {string.Join(' ', prerequisite.InstallArguments ?? Array.Empty<string>())}");
        }
    }

    private async Task<int> RunAsync(
        string command,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var resolved = Services.Processes.CommandLauncher.Resolve(command);

        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        resolved.ApplyTo(startInfo, arguments);

        Process process;

        try
        {
            process = Process.Start(startInfo)
                ?? throw new ExtensionException($"Windows did not start '{command}'.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new ExtensionException(
                $"'{command}' could not be run. It is either not installed or not on the path.", ex);
        }

        // Tracked like everything else, so an installer that hangs cannot outlive this session.
        _children.Track(process, $"extension install {command}");

        // Both streams are drained while waiting. A process whose output buffer fills and whose
        // reader is not running deadlocks, and package managers are chatty.
        var stdout = Task.Run(() => PumpAsync(process.StandardOutput, progress), CancellationToken.None);
        var stderr = Task.Run(() => PumpAsync(process.StandardError, progress), CancellationToken.None);

        using var timer = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, ct);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _children.Terminate(process);

            throw timer.IsCancellationRequested && !ct.IsCancellationRequested
                ? new ExtensionException($"'{command}' did not finish within {timeout.TotalMinutes:0} minutes.")
                : new OperationCanceledException(ct);
        }

        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        return process.ExitCode;
    }

    private static async Task PumpAsync(StreamReader reader, IProgress<string>? progress)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    progress?.Report(line.Trim());
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The process ended. Nothing here is worth failing an install over.
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind. The clone below will fail with a clearer message than this would.
        }
    }

    private static string StripVersion(string package)
    {
        var at = package.LastIndexOf('@');
        return at > 0 ? package[..at] : package;
    }

    private static string Sanitise(string value)
    {
        var cleaned = new string(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' or '_' ? c : '-').ToArray());
        return cleaned.Trim('-').ToLowerInvariant();
    }
}
