using System.IO;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Services.Extensions;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// Where the files this application keeps about a project actually go.
/// </summary>
/// <remarks>
/// One folder at the project root, obviously ours, that travels when the project is cloned or
/// moved. What is worth pinning is the two things that used to be somewhere else: a graph, which
/// belongs with the codebase it was arranged against rather than in a pile on one machine, and the
/// extension registry, which lived under a Unity shaped path in projects that need not be Unity.
///
/// The pile is now gone rather than read as a second place to look. Nothing in it was moved or
/// deleted, because it could hold graphs from any number of projects with no record of which
/// belonged where, and a graph guessed into the wrong project writes into the wrong codebase.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class ProjectFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "localnexus-projectfiles", Guid.NewGuid().ToString("N"));

    public ProjectFilesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A scratch folder that will not delete is not the test's problem.
        }
    }

    private string Project(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string WriteGraph(string folder, string name)
    {
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, name + GraphSerializer.FileExtension);
        File.WriteAllText(path, "{}");

        return path;
    }

    /// <summary>Everything about a project is under one folder at its root.</summary>
    /// <remarks>
    /// The name is decided in one place. It used to be a constant on the staging file, which meant
    /// the history store asked the staging file where a project keeps its database.
    /// </remarks>
    [Fact]
    public void OneFolderHoldsEverythingAboutAProject()
    {
        var project = Project("app");

        Assert.Equal(".localnexus", ProjectPaths.FolderName);
        Assert.Equal(ProjectPaths.FolderName, StagingStore.FolderName);

        Assert.Equal(Path.Combine(project, ".localnexus"), ProjectPaths.For(project));
        Assert.Equal(Path.Combine(project, ".localnexus", "graphs"), ProjectPaths.Graphs(project));
        Assert.Equal(Path.Combine(project, ".localnexus", "extensions.json"), ProjectPaths.Extensions(project));
    }

    /// <summary>The settings files stay at the root where somebody can find them.</summary>
    /// <remarks>
    /// One of them is meant to be committed and read by a team and the other is meant to be edited
    /// by hand, so hiding the two most worth seeing inside a dotted folder would be the wrong way
    /// round.
    /// </remarks>
    [Fact]
    public void TheSettingsFilesStayAtTheRoot()
    {
        var project = Project("app");

        Assert.Equal(Path.Combine(project, "localnexus.json"), ProjectSettings.SharedPath(project));
        Assert.Equal(Path.Combine(project, "localnexus.local.json"), ProjectSettings.LocalPath(project));
    }

    /// <summary>A graph is saved into the project it was arranged against.</summary>
    [Fact]
    public void AGraphIsSavedIntoTheProject()
    {
        var project = Project("app");

        Assert.Equal(ProjectPaths.Graphs(project), ProjectPaths.GraphFolderToShow(project));

        var folder = ProjectPaths.EnsureGraphs(project);

        Assert.True(Directory.Exists(folder));
        Assert.StartsWith(project, folder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The machine wide graphs folder is gone rather than still quietly read.</summary>
    /// <remarks>
    /// It could hold graphs from any number of projects with no record of which belonged where, so
    /// a graph opened out of it wrote into whichever codebase happened to be open. Nothing on disk
    /// was moved or deleted; it is simply no longer somewhere the application looks.
    /// </remarks>
    [Fact]
    public void TheMachineWideGraphsFolderIsGone()
    {
        Assert.Null(typeof(AppPaths).GetProperty("Graphs"));
        Assert.Null(typeof(GraphSerializer).GetMethod("BuildDefaultPath"));
    }

    /// <summary>Only the project's own folder is read.</summary>
    [Fact]
    public void OnlyTheProjectsOwnFolderIsRead()
    {
        var project = Project("app");

        WriteGraph(ProjectPaths.Graphs(project), "mine");

        Assert.Equal(new[] { ProjectPaths.Graphs(project) }, ProjectPaths.GraphFolders(project));
    }

    /// <summary>With no project open there is nowhere a graph belongs, and nowhere is the answer.</summary>
    [Fact]
    public void WithNoProjectThereIsNowhereAGraphBelongs()
    {
        Assert.Empty(ProjectPaths.GraphFolders(null));
        Assert.Null(ProjectPaths.GraphFolderToShow(null));
    }

    /// <summary>Opening and saving start in the same place, because there is only one place.</summary>
    [Fact]
    public void OpeningAndSavingStartInTheSamePlace()
    {
        var project = Project("app");

        WriteGraph(ProjectPaths.Graphs(project), "mine");

        Assert.Equal(ProjectPaths.Graphs(project), ProjectPaths.GraphFolderToShow(project));
    }

    /// <summary>The extension registry is no longer in a Unity shaped path.</summary>
    /// <remarks>
    /// The old location was Unity's convention for state belonging to whoever is at the machine,
    /// which was right reasoning applied to the wrong scope: this works on any codebase.
    /// </remarks>
    [Fact]
    public void TheExtensionRegistryIsNotInAUnityPath()
    {
        var project = Project("app");

        Assert.DoesNotContain("UserSettings", ProjectPaths.Extensions(project), StringComparison.Ordinal);
        Assert.Contains("UserSettings", ProjectPaths.LegacyExtensions(project), StringComparison.Ordinal);
    }

    /// <summary>A registry left in the old place is read, and moved to the new one.</summary>
    [Fact]
    public void ARegistryInTheOldPlaceIsMoved()
    {
        var project = Project("app");
        var legacy = ProjectPaths.LegacyExtensions(project);

        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllText(legacy, """{"version":1,"extensions":[]}""");

        var registry = new ExtensionRegistry(new ActivityFeed());
        registry.OpenProject(project);

        Assert.True(File.Exists(ProjectPaths.Extensions(project)));
        Assert.False(File.Exists(legacy));
    }

    /// <summary>What is already at the new path wins, and the old file is left alone.</summary>
    /// <remarks>
    /// Two files that both look like the registry is how a project ends up running the extensions
    /// it had a year ago.
    /// </remarks>
    [Fact]
    public void TheCurrentRegistryIsNotOverwrittenByAnOldOne()
    {
        var project = Project("app");
        var legacy = ProjectPaths.LegacyExtensions(project);
        var current = ProjectPaths.Extensions(project);

        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllText(legacy, """{"version":1,"extensions":[]}""");

        Directory.CreateDirectory(Path.GetDirectoryName(current)!);
        File.WriteAllText(current, """{"version":1,"extensions":[]}""");

        var registry = new ExtensionRegistry(new ActivityFeed());
        registry.OpenProject(project);

        Assert.True(File.Exists(legacy));
    }

    /// <summary>A project that never had a registry does not grow one for being opened.</summary>
    [Fact]
    public void AProjectWithNoExtensionsIsNotGivenAFile()
    {
        var project = Project("app");

        var registry = new ExtensionRegistry(new ActivityFeed());
        registry.OpenProject(project);

        Assert.False(File.Exists(ProjectPaths.Extensions(project)));
        Assert.Empty(registry.Extensions);
    }

    /// <summary>The folder is kept out of the repository, by appending to a gitignore that exists.</summary>
    /// <remarks>
    /// It holds run history, the snapshots taken before a write and conversation threads, which are
    /// large, machine local and nobody else's business.
    /// </remarks>
    [Fact]
    public void TheFolderIsGitignored()
    {
        var project = Project("app");
        var gitignore = Path.Combine(project, ".gitignore");

        File.WriteAllText(gitignore, "bin/\nobj/\n");

        var settings = new ProjectSettingsService(new ActivityFeed());
        settings.Open(project, ProjectKind.Plain);

        var lines = File.ReadAllLines(gitignore);

        Assert.Contains(".localnexus/", lines);

        // Only the folder on open. Which settings file is ignored depends on an answer that may
        // not have been given yet.
        Assert.DoesNotContain(ProjectSettings.SharedFileName, lines);
    }

    /// <summary>An entry already there is not added a second time.</summary>
    [Fact]
    public void TheEntryIsNotAddedTwice()
    {
        var project = Project("app");
        var gitignore = Path.Combine(project, ".gitignore");

        File.WriteAllText(gitignore, ".localnexus/\n");

        var settings = new ProjectSettingsService(new ActivityFeed());
        settings.Open(project, ProjectKind.Plain);
        settings.Open(project, ProjectKind.Plain);

        Assert.Single(File.ReadAllLines(gitignore), l => l.Trim() == ".localnexus/");
    }

    /// <summary>
    /// A project with no gitignore does not get one.
    /// </summary>
    /// <remarks>
    /// Creating one would be this application deciding how somebody's repository is arranged, and a
    /// project with no gitignore has nothing to be ignored from.
    /// </remarks>
    [Fact]
    public void AProjectWithNoGitignoreDoesNotGetOne()
    {
        var project = Project("app");

        var settings = new ProjectSettingsService(new ActivityFeed());
        settings.Open(project, ProjectKind.Plain);

        Assert.False(File.Exists(Path.Combine(project, ".gitignore")));
    }

    /// <summary>Staging and history are in the same folder, which they already were.</summary>
    [Fact]
    public void StagingAndHistoryAreInTheSameFolder()
    {
        var project = Project("app");

        Assert.Equal(
            ProjectPaths.For(project),
            Path.GetDirectoryName(Path.Combine(project, StagingStore.FolderName, StagingStore.FileName)));
    }
}
