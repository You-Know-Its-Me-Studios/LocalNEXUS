using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LocalNEXUS.App.Services.Persistence;

/// <summary>
/// Finds a saved graph by what it is rather than by where it was.
/// </summary>
/// <remarks>
/// A project remembers the graph it was last working on. It used to remember a path, so renaming
/// the file, moving it into a subfolder, or saving it under a different name all lost it silently,
/// and the next open reported that a graph that was sitting right there was no longer there.
///
/// The identifier is written inside the file, so all three of those survive it. The last known
/// path is still kept, but only as somewhere to look first: it is a shortcut, and it is trusted
/// only when the file at the end of it turns out to carry the right identifier.
///
/// What this cannot do is find a graph moved out of the project altogether, because there is no
/// index of every graph on the machine and building one would put back exactly the machine wide
/// store this replaced. That case reports honestly that the graph was not found.
/// </remarks>
public static class GraphLocator
{
    /// <summary>
    /// Reads the identifier out of a saved graph without loading it.
    /// </summary>
    /// <returns>The identifier, or null when the file is missing, unreadable, or predates them.</returns>
    /// <remarks>
    /// Never throws. Being asked to identify a file is not a reason to fail an open: a file that
    /// cannot be read is simply not the graph being looked for.
    /// </remarks>
    public static Guid? ReadId(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject document)
            {
                return null;
            }

            if (document["id"]?.GetValueKind() != JsonValueKind.String)
            {
                return null;
            }

            return Guid.TryParse(document["id"]!.GetValue<string>(), out var id) ? id : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Finds the graph carrying <paramref name="id"/> in this project.
    /// </summary>
    /// <param name="projectPath">The open project, or null when there is none.</param>
    /// <param name="id">The identifier the project recorded.</param>
    /// <param name="hint">Where it was last seen, which is checked before anything is scanned.</param>
    /// <returns>The file, or null when nothing in the project carries that identifier.</returns>
    /// <remarks>
    /// Two graphs can carry the same identifier, and the way that happens is somebody copying a
    /// graph file rather than saving one under a new name. Nothing here refuses that, because a
    /// duplicate is not damage and refusing to open either one would be a worse answer than
    /// opening one of them. The hint wins when it matches, and otherwise the first in ordinal file
    /// name order does, so the same project restores the same graph every time rather than
    /// whichever the file system happened to enumerate first.
    /// </remarks>
    public static string? Find(string? projectPath, Guid id, string? hint)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        if (hint is { Length: > 0 } && ReadId(hint) == id)
        {
            return hint;
        }

        foreach (var folder in ProjectPaths.GraphFolders(projectPath))
        {
            foreach (var path in Enumerate(folder))
            {
                if (ReadId(path) == id)
                {
                    return path;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> Enumerate(string folder)
    {
        try
        {
            return Directory
                .EnumerateFiles(folder, "*" + GraphSerializer.FileExtension, SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
