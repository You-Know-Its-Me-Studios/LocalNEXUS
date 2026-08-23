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
    /// One, now that the machine wide folder is gone. It is still a sequence rather than a single
    /// path because a project may yet grow a second place to keep graphs, and because every caller
    /// was already written to iterate.
    ///
    /// Nothing was moved out of the old folder and nothing was deleted from it. It could hold
    /// graphs from any number of projects with no record of which belonged where, so moving one
    /// would have meant guessing, and a graph guessed into the wrong project writes into the wrong
    /// codebase. It is simply no longer read; anything in it is opened by pointing the open dialog
    /// at it, and saving from an open project is what puts it with that project.
    /// </remarks>
    public static IEnumerable<string> GraphFolders(string? projectPath)
    {
        if (projectPath is { Length: > 0 } project && Directory.Exists(Graphs(project)))
        {
            yield return Graphs(project);
        }
    }

    /// <summary>
    /// Where a graph save or load dialog should start, or null when there is nowhere it belongs.
    /// </summary>
    /// <remarks>
    /// Null rather than a machine wide folder. A graph names one project's files and reaches for
    /// one project's default model, so with no project open there is no correct answer, and
    /// inventing one is how graphs ended up somewhere they could not be matched to a codebase. The
    /// dialog opens wherever the system last left it instead.
    ///
    /// The same answer for saving and for opening, which it did not used to be. It differed only
    /// so that opening could fall back to the old machine wide folder, and there is no longer one
    /// to fall back to.
    /// </remarks>
    public static string? GraphFolderToShow(string? projectPath)
        => projectPath is { Length: > 0 } project ? Graphs(project) : null;

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
