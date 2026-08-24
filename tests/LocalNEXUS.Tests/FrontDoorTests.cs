using System.IO;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.ViewModels;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// The first question the application asks, and what the answers do.
/// </summary>
/// <remarks>
/// Nothing here writes to the configuration file. The recent list lives in a per user file, so a
/// suite that saved would be editing the recent projects of whoever ran it, which is why the
/// service takes how it saves rather than reaching for it.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class FrontDoorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "localnexus-frontdoor", Guid.NewGuid().ToString("N"));

    public FrontDoorTests() => Directory.CreateDirectory(_root);

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

    /// <summary>A folder picker that answers with whatever it was told to.</summary>
    private sealed class ScriptedDialogs : IDialogService
    {
        public string? Folder { get; set; }

        public string? PickFolder(string title, string? initialDirectory = null) => Folder;

        public string? PickOpenFile(string title, string filter, string? initialDirectory = null) => null;

        public string? PickSaveFile(string title, string defaultFileName, string filter, string? initialDirectory = null)
            => null;

        public void ShowError(string title, string message)
        {
        }

        /// <summary>A test never waits on a person, so nothing is confirmed.</summary>
        public bool Confirm(string title, string message) => false;

        /// <summary>A test opens no browsers.</summary>
        public void OpenUrl(string url) => LastUrl = url;

        /// <summary>The last link offered, so a test can read it.</summary>
        public string? LastUrl { get; private set; }

        public void OpenFolderInExplorer(string folder)
        {
        }

        public void OpenFileInEditor(string file)
        {
        }

        public void CopyToClipboard(string text)
        {
        }
    }

    private string Folder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static RecentProjectsService Recents(AppConfig config)
        => new(config, () => { });

    private sealed record Door(
        FrontDoorViewModel ViewModel,
        RecentProjectsService Recents,
        ProjectService Project,
        ScriptedDialogs Dialogs,
        List<string> Opened,
        List<int> NetworkShown);

    /// <summary>
    /// A door wired the way the application wires it, with the open path recorded rather than run.
    /// </summary>
    /// <remarks>
    /// The open path opens the project service, which is what the real one does before anything
    /// else. That is what lets the watcher be exercised: the door closes because a project became
    /// open, not because a command was pressed.
    /// </remarks>
    private Door BuildDoor(AppConfig? config = null, Action<string>? open = null)
    {
        config ??= new AppConfig();

        var recents = Recents(config);
        var project = new ProjectService();
        var dialogs = new ScriptedDialogs();
        var opened = new List<string>();
        var network = new List<int>();

        var door = new FrontDoorViewModel(
            recents,
            project,
            dialogs,
            config,
            folder =>
            {
                opened.Add(folder);

                if (open is not null)
                {
                    open(folder);
                    return;
                }

                project.Open(folder);
            },
            () => network.Add(1));

        return new Door(door, recents, project, dialogs, opened, network);
    }

    /// <summary>The most recently opened is first, because that is what is being looked for.</summary>
    [Fact]
    public void RecentProjectsAreMostRecentFirst()
    {
        var config = new AppConfig();
        var recents = Recents(config);

        recents.Record(Folder("alpha"));
        recents.Record(Folder("beta"));
        recents.Record(Folder("gamma"));

        Assert.Equal(new[] { "gamma", "beta", "alpha" }, recents.Items.Select(i => i.Name));
        Assert.False(recents.IsEmpty);
    }

    /// <summary>A row carries the three things that tell two projects apart.</summary>
    /// <remarks>
    /// Two checkouts of the same repository are both called src, which is the ordinary case rather
    /// than the awkward one, so the path is on the row rather than in a tooltip alone.
    /// </remarks>
    [Fact]
    public void ARowSaysWhatItIsWhereItIsAndWhen()
    {
        var config = new AppConfig();
        var recents = Recents(config);
        var folder = Folder("checkout");

        recents.Record(folder);

        var row = recents.Items.Single();

        Assert.Equal("checkout", row.Name);
        Assert.Equal(folder, row.Path);
        Assert.StartsWith("Today at", row.WhenText, StringComparison.Ordinal);
        Assert.True(row.IsAvailable);
    }

    /// <summary>Opening the same project again moves it up rather than adding a second row.</summary>
    [Fact]
    public void TheSameProjectIsNotTwoRows()
    {
        var config = new AppConfig();
        var recents = Recents(config);

        var first = Folder("alpha");
        var second = Folder("beta");

        recents.Record(first);
        recents.Record(second);
        recents.Record(first);

        Assert.Equal(2, recents.Items.Count);
        Assert.Equal("alpha", recents.Items[0].Name);
    }

    /// <summary>A path spelled differently is the same project.</summary>
    [Fact]
    public void ADifferentSpellingIsTheSameProject()
    {
        var config = new AppConfig();
        var recents = Recents(config);
        var folder = Folder("alpha");

        recents.Record(folder);
        recents.Record(folder.ToUpperInvariant());
        recents.Record(Path.Combine(folder, "..", "alpha"));

        Assert.Single(recents.Items);
    }

    /// <summary>The list stays a list somebody can scan.</summary>
    [Fact]
    public void TheListIsCapped()
    {
        var config = new AppConfig();
        var recents = Recents(config);

        for (var i = 0; i < RecentProjectsService.Capacity + 3; i++)
        {
            recents.Record(Folder($"project-{i}"));
        }

        Assert.Equal(RecentProjectsService.Capacity, recents.Items.Count);
        Assert.DoesNotContain(recents.Items, i => i.Name == "project-0");
    }

    /// <summary>
    /// A project that is not there any more says so rather than waiting to fail.
    /// </summary>
    /// <remarks>
    /// Read when the list is built, not when a row is pressed, so somebody sees which of their
    /// projects has moved before choosing rather than after.
    /// </remarks>
    [Fact]
    public void AProjectThatHasGoneIsMarkedRatherThanOffered()
    {
        var config = new AppConfig();
        var recents = Recents(config);
        var folder = Folder("moved");

        recents.Record(folder);
        Directory.Delete(folder);

        recents.Refresh();

        var row = recents.Items.Single();

        Assert.Equal(RecentProjectState.Missing, row.State);
        Assert.True(row.IsMissing);
        Assert.False(row.IsAvailable);
        Assert.Equal("Not found", row.WhenText);
    }

    /// <summary>Pressing one anyway says what happened and opens nothing.</summary>
    [Fact]
    public void OpeningAMissingProjectSaysSoRatherThanFailing()
    {
        var door = BuildDoor();
        var folder = Folder("moved");

        door.Recents.Record(folder);
        Directory.Delete(folder);
        door.Recents.Refresh();

        door.ViewModel.Show();
        door.ViewModel.OpenRecentCommand.Execute(door.Recents.Items.Single());

        Assert.Contains("not there any more", door.ViewModel.Problem, StringComparison.Ordinal);
        Assert.Empty(door.Opened);
        Assert.True(door.ViewModel.IsOpen);
    }

    /// <summary>And it can be forgotten, which is the thing to do about it.</summary>
    [Fact]
    public void AMissingProjectCanBeForgotten()
    {
        var door = BuildDoor();
        var folder = Folder("moved");

        door.Recents.Record(folder);
        Directory.Delete(folder);
        door.Recents.Refresh();

        door.ViewModel.ForgetCommand.Execute(door.Recents.Items.Single());

        Assert.Empty(door.Recents.Items);
        Assert.True(door.ViewModel.IsFirstRun);
    }

    /// <summary>Choosing a recent project opens it, closes the door and moves it to the top.</summary>
    [Fact]
    public void ChoosingARecentProjectOpensIt()
    {
        var door = BuildDoor();
        var folder = Folder("alpha");

        door.Recents.Record(folder);
        door.ViewModel.Show();

        Assert.True(door.ViewModel.IsOpen);

        door.ViewModel.OpenRecentCommand.Execute(door.Recents.Items.Single());

        Assert.Equal(folder, door.Project.ProjectPath);
        Assert.False(door.ViewModel.IsOpen);
        Assert.Equal("alpha", door.Recents.Items[0].Name);
    }

    /// <summary>The folder picker opens whatever it was given.</summary>
    [Fact]
    public void OpeningAProjectFromThePickerWorks()
    {
        var door = BuildDoor();
        var folder = Folder("picked");

        door.ViewModel.Show();
        door.Dialogs.Folder = folder;

        door.ViewModel.OpenProjectCommand.Execute(null);

        Assert.Equal(folder, door.Project.ProjectPath);
        Assert.False(door.ViewModel.IsOpen);
    }

    /// <summary>Starting one in a folder is the same act asked differently.</summary>
    [Fact]
    public void StartingAProjectInAFolderWorks()
    {
        var door = BuildDoor();
        var folder = Folder("brand-new");

        door.ViewModel.Show();
        door.Dialogs.Folder = folder;

        door.ViewModel.StartProjectCommand.Execute(null);

        Assert.Equal(folder, door.Project.ProjectPath);
        Assert.Single(door.Recents.Items);
    }

    /// <summary>Cancelling the picker changes nothing.</summary>
    [Fact]
    public void CancellingThePickerLeavesTheDoorUp()
    {
        var door = BuildDoor();

        door.ViewModel.Show();
        door.Dialogs.Folder = null;

        door.ViewModel.OpenProjectCommand.Execute(null);

        Assert.True(door.ViewModel.IsOpen);
        Assert.Empty(door.Opened);
    }

    /// <summary>
    /// The way out for somebody who is only lending their machine goes to the Network section.
    /// </summary>
    /// <remarks>
    /// The only dismissal there is, and it goes somewhere. Landing on an empty workspace with no
    /// project is the state the door exists to prevent.
    /// </remarks>
    [Fact]
    public void ContributingOnlyGoesToTheNetwork()
    {
        var door = BuildDoor();

        door.ViewModel.Show();
        door.ViewModel.ContributeOnlyCommand.Execute(null);

        Assert.False(door.ViewModel.IsOpen);
        Assert.Single(door.NetworkShown);
        Assert.False(door.Project.HasProject);
    }

    /// <summary>
    /// A project opened by anything else closes the door and is recorded.
    /// </summary>
    /// <remarks>
    /// This is the MCP case. A tool call opens the project service directly and knows nothing about
    /// the door; the door watches the project rather than its own commands, so it closes anyway and
    /// nothing had to be added to the tool surface.
    /// </remarks>
    [Fact]
    public void AProjectOpenedByAnythingElseClosesTheDoor()
    {
        var door = BuildDoor();
        var folder = Folder("opened-over-mcp");

        door.ViewModel.Show();
        Assert.True(door.ViewModel.IsOpen);

        // Nothing to do with the door: this is what the tool call does.
        door.Project.Open(folder);

        Assert.False(door.ViewModel.IsOpen);
        Assert.Equal("opened-over-mcp", door.Recents.Items.Single().Name);
    }

    /// <summary>An open that fails leaves the door up, because there is nothing behind it.</summary>
    [Fact]
    public void AFailedOpenLeavesTheDoorUp()
    {
        var door = BuildDoor(open: _ => throw new DirectoryNotFoundException("There is no folder there."));

        door.ViewModel.Show();
        door.Dialogs.Folder = Path.Combine(_root, "never-existed");

        door.ViewModel.OpenProjectCommand.Execute(null);

        Assert.True(door.ViewModel.IsOpen);
        Assert.Equal("There is no folder there.", door.ViewModel.Problem);
        Assert.False(door.Project.HasProject);
    }

    /// <summary>A first run says so rather than showing an empty heading.</summary>
    [Fact]
    public void AFirstRunSaysThereIsNothingToReturnTo()
    {
        var door = BuildDoor();

        door.ViewModel.Show();

        Assert.True(door.ViewModel.IsFirstRun);
        Assert.Empty(door.Recents.Items);
    }

    /// <summary>
    /// The last graph belongs to the project, not to the installation.
    /// </summary>
    /// <remarks>
    /// It used to be one application wide setting, written on every save and read by nothing at
    /// all. Had it been read, switching projects would have restored a graph belonging to the other
    /// one: a graph names one project's files and reaches for one project's default model.
    /// </remarks>
    [Fact]
    public void TheLastGraphBelongsToTheProject()
    {
        var alpha = Folder("alpha");
        var beta = Folder("beta");

        new ProjectSettings { LastGraphPath = @"C:\graphs\alpha.lnx", HasBeenSetUp = true }.Save(alpha);
        new ProjectSettings { LastGraphPath = @"C:\graphs\beta.lnx", HasBeenSetUp = true }.Save(beta);

        Assert.Equal(@"C:\graphs\alpha.lnx", ProjectSettings.Load(alpha).LastGraphPath);
        Assert.Equal(@"C:\graphs\beta.lnx", ProjectSettings.Load(beta).LastGraphPath);
    }

    /// <summary>It is machine local, so it is never committed with the conventions.</summary>
    /// <remarks>
    /// A path on one person's disk means nothing on anybody else's, so it belongs in the file that
    /// is never shared even when a project has decided to share its conventions.
    /// </remarks>
    [Fact]
    public void TheLastGraphIsNeverShared()
    {
        var folder = Folder("shared");

        new ProjectSettings
        {
            LastGraphPath = @"C:\graphs\mine.lnx",
            ScriptsFolder = "src",
            ShareSettings = true
        }.Save(folder);

        var shared = File.ReadAllText(ProjectSettings.SharedPath(folder));
        var local = File.ReadAllText(ProjectSettings.LocalPath(folder));

        Assert.DoesNotContain("lastGraphPath", shared, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lastGraphPath", local, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>And the setting it replaced is gone rather than left behind unread.</summary>
    [Fact]
    public void TheGlobalLastGraphSettingIsGone()
        => Assert.Null(typeof(AppConfig).GetProperty("LastGraphPath"));

    /// <summary>Nothing is remembered against a project that no longer has the graph.</summary>
    [Fact]
    public void AGraphThatIsNoLongerThereIsNotRestored()
    {
        var folder = Folder("stale");

        new ProjectSettings { LastGraphPath = Path.Combine(_root, "gone.lnx"), HasBeenSetUp = true }.Save(folder);

        var settings = ProjectSettings.Load(folder);

        Assert.False(File.Exists(settings.LastGraphPath));
    }
}
