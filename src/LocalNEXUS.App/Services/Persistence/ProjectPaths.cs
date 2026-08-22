using System.IO;

namespace LocalNEXUS.App.Services.Persistence;

/// <summary>
/// Everything this application keeps about one project, and where inside that project it lives.
/// </summary>
/// <remarks>
/// One folder, obviously ours, at the root of the project it belongs to. What is kept here is
/// about a particular codebase: the graphs arranged against it, what was run and what those runs
/// wrote, the conversation, unfinished work, and which extensions this project uses. All of it
/// travels when the project is cloned or moved, which is the point.
///
/// The two settings files are the exception and stay at the root beside it. They are meant to be
/// found and edited by a person, and one of them is meant to be committed and read by a team, so
/// putting them inside a dotted folder would hide the two files most worth seeing.
///
/// What is not here is what belongs to the machine rather than to any project: the application
/// configuration, the model folders and their catalogue, the engine logs, the Python runtime, and
/// the graph templates, which exist to be reused across projects and would be pointless kept
/// inside one.
/// </remarks>
public static class ProjectPaths
{
    /// <summary>The folder inside a project where this application keeps its own state.</summary>
    public const string FolderName = ".localnexus";

    /// <summary>The folder inside that one holding graphs arranged against this project.</summary>
    public const string GraphsFolderName = "graphs";

    /// <summary>What a project's extension registry is called.</summary>
    public const string ExtensionsFileName = "extensions.json";

    /// <summary>
    /// Where the extension registry used to live.
    /// </summary>
    /// <remarks>
    /// Unity's convention for state belonging to whoever is at the machine, which was the right
    /// reasoning applied to the wrong scope: this works on any codebase, and a plain C# project
    /// has no reason to grow a folder named after an engine it does not use.
    /// </remarks>
    public const string LegacyExtensionsFolderName = "UserSettings/LocalNEXUS";

    /// <summary>The application's folder inside a project.</summary>
    public static string For(string projectPath) => Path.Combine(projectPath, FolderName);

    /// <summary>Where graphs for this project are written.</summary>
    public static string Graphs(string projectPath) => Path.Combine(For(projectPath), GraphsFolderName);

    /// <summary>Where this project's extension registry is.</summary>
    public static string Extensions(string projectPath) => Path.Combine(For(projectPath), ExtensionsFileName);

    /// <summary>Where this project's extension registry used to be.</summary>
    public static string LegacyExtensions(string projectPath)
        => Path.Combine(projectPath, LegacyExtensionsFolderName, ExtensionsFileName);

    /// <summary>
    /// Every folder a graph for this project may be in, in the order they should be looked at.
    /// </summary>
    /// <remarks>
    /// Two, for as long as anybody has graphs from before they lived with their project. Nothing
    /// is moved out of the old folder: it can hold graphs from any number of projects with no
    /// record of which belongs where, so moving them would mean guessing, and a graph put in the
    /// wrong project writes into the wrong codebase. They are read where they are instead, and
    /// saving one from an open project is what puts it with that project.
    /// </remarks>
    public static IEnumerable<string> GraphFolders(string? projectPath)
    {
        if (projectPath is { Length: > 0 } project && Directory.Exists(Graphs(project)))
        {
            yield return Graphs(project);
        }

        if (Directory.Exists(AppPaths.Graphs))
        {
            yield return AppPaths.Graphs;
        }
    }

    /// <summary>
    /// Where a graph save or load dialog should start.
    /// </summary>
    /// <remarks>
    /// The project's own folder, unless it has no graphs yet and the old one does, in which case
    /// starting there is what stops somebody having to go looking for work they already did.
    /// </remarks>
    public static string GraphFolderToShow(string? projectPath, bool forSaving)
    {
        if (projectPath is not { Length: > 0 } project)
        {
            return AppPaths.Graphs;
        }

        var mine = Graphs(project);

        if (forSaving || HasGraphs(mine) || !HasGraphs(AppPaths.Graphs))
        {
            return mine;
        }

        return AppPaths.Graphs;
    }

    private static bool HasGraphs(string folder)
    {
        try
        {
            return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*" + GraphSerializer.FileExtension).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates the graphs folder, and returns it.
    /// </summary>
    /// <remarks>
    /// Made when something is about to be put in it rather than when a project is opened, so a
    /// project nobody has saved a graph in does not acquire an empty folder for having been looked
    /// at.
    /// </remarks>
    /// <exception cref="IOException">The project folder could not be written to.</exception>
    public static string EnsureGraphs(string projectPath)
    {
        var folder = Graphs(projectPath);
        Directory.CreateDirectory(folder);

        return folder;
    }
}
