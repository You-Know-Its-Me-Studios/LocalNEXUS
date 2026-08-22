using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models.Extensions;

namespace LocalNEXUS.App.Services.Extensions;

/// <summary>
/// The extensions registered against the currently opened project.
/// </summary>
/// <remarks>
/// Per project rather than per install, because the thing an extension talks to is per project.
/// Both target servers bind to one Unity project through a port or a named pipe, so an extension
/// configured for one project is not merely unnecessary in another, it is pointed at the wrong
/// editor. Unity's own package manager is per project for the same reason.
/// <para>
/// A project with no extensions is a project with no extensions. That is the ordinary state of
/// every project that has never had one, and it is not a failure, is not an error, and does not
/// get an error colour anywhere.
/// </para>
/// <para>
/// Stored in the project's own <c>.localnexus</c> folder beside everything else this application
/// keeps about it. It used to live under <c>UserSettings</c>, which is Unity's convention for
/// state belonging to whoever is at the machine: the right reasoning applied to the wrong scope,
/// because this works on any codebase and a plain C# project has no reason to grow a folder named
/// after an engine it does not use. The reasoning still holds, and the folder it points at is
/// gitignored for it: a registry entry holds a command line, and command lines hold absolute paths
/// that are wrong on anybody else's computer.
/// </para>
/// <para>
/// A registry left in the old place is moved the first time that project is opened, so nobody
/// loses the extensions they had.
/// </para>
/// </remarks>
public sealed partial class ExtensionRegistry : ObservableObject
{

    private readonly IActivityFeed _feed;

    /// <summary>The project these extensions belong to, or null when no project is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject))]
    private string? _projectPath;

    public ExtensionRegistry(IActivityFeed feed) => _feed = feed;

    /// <summary>Everything registered against the open project. Empty is a normal state.</summary>
    public ObservableCollection<InstalledExtension> Extensions { get; } = new();

    /// <summary>True when a project is open and extensions can be registered at all.</summary>
    public bool HasProject => !string.IsNullOrWhiteSpace(ProjectPath);

    /// <summary>Raised when the set of installed extensions changes, so the palette can be rebuilt.</summary>
    public event Action? ContributionsChanged;

    /// <summary>
    /// Points the registry at a project and loads what that project has registered. Passing null
    /// clears it, which is what closing a project does.
    /// </summary>
    public void OpenProject(string? projectPath)
    {
        ProjectPath = projectPath;
        Extensions.Clear();

        if (!HasProject)
        {
            ContributionsChanged?.Invoke();
            return;
        }

        var file = MoveFromLegacyLocation(RegistryPath!);

        if (!File.Exists(file))
        {
            // Never had one. Not a failure, and nothing is written until something is added.
            ContributionsChanged?.Invoke();
            return;
        }

        try
        {
            if (JsonNode.Parse(File.ReadAllText(file)) is not JsonObject root
                || root["extensions"] is not JsonArray entries)
            {
                _feed.Error("Extensions not loaded", $"{file} is not in the expected shape, so it was ignored.");
                ContributionsChanged?.Invoke();
                return;
            }

            foreach (var entry in entries.OfType<JsonObject>())
            {
                LoadOne(entry, file);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _feed.Error("Extensions not loaded", $"{file} could not be read: {ex.Message}");
        }

        ContributionsChanged?.Invoke();
    }

    /// <summary>Adds an extension and writes the registry.</summary>
    public void Add(InstalledExtension extension)
    {
        var existing = Find(extension.Manifest.Id);

        if (existing is not null)
        {
            Extensions.Remove(existing);
        }

        Extensions.Add(extension);
        Save();
        ContributionsChanged?.Invoke();
    }

    /// <summary>Removes an extension and writes the registry.</summary>
    public void Remove(InstalledExtension extension)
    {
        Extensions.Remove(extension);
        Save();
        ContributionsChanged?.Invoke();
    }

    /// <summary>Finds a registered extension by id.</summary>
    public InstalledExtension? Find(string id)
        => Extensions.FirstOrDefault(e => string.Equals(e.Manifest.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds whichever extension contributes a node type, or null when none does.</summary>
    public InstalledExtension? FindByNodeType(string typeKey)
        => Extensions.FirstOrDefault(e => e.Manifest.Nodes
            .Any(n => string.Equals(n.TypeKey, typeKey, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Every node type contributed by an extension that is usable right now.</summary>
    public IEnumerable<(InstalledExtension Extension, NodeContribution Node)> UsableNodes()
        => Extensions
            .Where(e => e.IsUsable && e.Manifest.ProvidesNodes)
            .SelectMany(e => e.Manifest.Nodes.Select(n => (e, n)));

    /// <summary>Writes the registry back to the project. Called after every change.</summary>
    public void Save()
    {
        if (!HasProject)
        {
            return;
        }

        var file = RegistryPath!;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);

            var root = new JsonObject
            {
                ["version"] = 1,
                ["extensions"] = new JsonArray(Extensions.Select(e => (JsonNode?)new JsonObject
                {
                    ["manifest"] = ExtensionManifestJson.Write(e.Manifest),
                    ["origin"] = e.Origin.ToString(),
                    ["originDetail"] = e.OriginDetail,
                    ["enabled"] = e.IsEnabled,
                    // A failure is remembered so a broken extension is still broken after a
                    // restart, rather than looking fine until the next time it is needed.
                    ["failed"] = e.State == ExtensionState.Failed,
                    ["failureReason"] = e.State == ExtensionState.Failed ? e.StateDetail : null
                }).ToArray())
            };

            File.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _feed.Error("Extensions not saved", $"{file} could not be written: {ex.Message}");
        }
    }

    private string? RegistryPath
        => HasProject ? Services.Persistence.ProjectPaths.Extensions(ProjectPath!) : null;

    /// <summary>
    /// Brings a registry written by an older version across to where they live now, and says
    /// which file to read.
    /// </summary>
    /// <remarks>
    /// Moved rather than read in place, because two files that both look like the registry is how
    /// a project ends up with the extensions it had a year ago. Only when there is nothing at the
    /// new path: a project already opened by this version has the current answer there, and an old
    /// file beside it is history rather than a source.
    ///
    /// A move that cannot be made is not a failure worth stopping for, and the answer is then the
    /// old file: an extension somebody installed is not lost because a folder was read only.
    /// Nothing is deleted until the copy is there.
    /// </remarks>
    private string MoveFromLegacyLocation(string file)
    {
        var legacy = Services.Persistence.ProjectPaths.LegacyExtensions(ProjectPath!);

        if (File.Exists(file) || !File.Exists(legacy))
        {
            return file;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.Copy(legacy, file);
            File.Delete(legacy);

            _feed.Info(
                "Extensions moved",
                $"This project's extension registry moved from {legacy} to {file}, which is where "
                + "everything this application keeps about a project now lives.");

            return file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _feed.Info(
                "Extensions not moved",
                $"{legacy} could not be moved to {file}, so it is being read where it is. {ex.Message}");

            return legacy;
        }
    }

    private void LoadOne(JsonObject entry, string file)
    {
        try
        {
            if (entry["manifest"] is not JsonObject manifestJson)
            {
                return;
            }

            var manifest = ExtensionManifestJson.Read(manifestJson);

            var origin = Enum.TryParse<ExtensionOrigin>(entry["origin"]?.GetValue<string>(), out var parsed)
                ? parsed
                : ExtensionOrigin.Command;

            var extension = new InstalledExtension(
                manifest,
                origin,
                entry["originDetail"]?.GetValue<string>() ?? string.Empty)
            {
                IsEnabled = entry["enabled"]?.GetValue<bool>() ?? true
            };

            if (entry["failed"]?.GetValue<bool>() == true)
            {
                extension.Fail(entry["failureReason"]?.GetValue<string>() ?? "Saved in a failed state.");
            }
            else
            {
                // Installed, never started this session. Not running, and certainly not failed.
                extension.State = ExtensionState.Unreachable;
                extension.StateDetail = "Not started yet.";
            }

            Extensions.Add(extension);
        }
        catch (ExtensionException ex)
        {
            // A manifest that no longer parses is itself a failure worth showing, rather than an
            // entry that quietly disappears from the list.
            _feed.Error("Extension not loaded", $"An entry in {file} could not be read: {ex.Message}");
        }
    }
}
