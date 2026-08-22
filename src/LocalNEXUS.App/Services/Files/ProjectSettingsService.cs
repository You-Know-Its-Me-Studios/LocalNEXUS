using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Infrastructure;

namespace LocalNEXUS.App.Services.Files;

/// <summary>
/// The open project's own settings, loaded when it is opened and saved when they change.
/// </summary>
/// <remarks>
/// Observable and bound to directly, following the model catalogue and the extension registry
/// rather than projecting into a view model that then has to be kept in step.
///
/// It answers one question nothing else could: where does generated code go in this project. That
/// used to be Assets/Scripts everywhere, which is right for a Unity project and creates a folder
/// with no business existing in any other. Guessing src instead would have been the same mistake
/// with a different default, so it is asked once and remembered.
/// </remarks>
public sealed partial class ProjectSettingsService : ObservableObject
{
    /// <summary>What a Unity project starts from, which is where Unity keeps its scripts.</summary>
    public const string UnityDefault = "Assets/Scripts";

    /// <summary>What anything else starts from.</summary>
    /// <remarks>
    /// Offered rather than assumed. It is the first row of a list of folders the project actually
    /// has, and the point of the window is that somebody picks.
    /// </remarks>
    public const string PlainDefault = "src";

    private readonly IActivityFeed _feed;

    /// <summary>Where generated code goes, relative to the project root.</summary>
    [ObservableProperty]
    private string _scriptsFolder = UnityDefault;

    /// <summary>What the project is, once detection and any override have both been consulted.</summary>
    [ObservableProperty]
    private ProjectKind _kind = ProjectKind.None;

    /// <summary>The model a new Model node reaches for, or empty.</summary>
    [ObservableProperty]
    private string _defaultModelPath = string.Empty;

    /// <summary>True when tool calls are answered while this project is open.</summary>
    [ObservableProperty]
    private bool _mcpServerEnabled;

    /// <summary>True when the conventions are meant to be committed.</summary>
    [ObservableProperty]
    private bool _shareSettings;

    /// <summary>True once the setup window has been answered or skipped for this project.</summary>
    [ObservableProperty]
    private bool _hasBeenSetUp;

    /// <summary>The graph last open in this project, restored when it is opened again.</summary>
    [ObservableProperty]
    private string _lastGraphPath = string.Empty;

    /// <summary>The project these belong to, or null.</summary>
    [ObservableProperty]
    private string? _projectPath;

    public ProjectSettingsService(IActivityFeed feed) => _feed = feed;

    /// <summary>True when this project has never been set up on this machine.</summary>
    public bool NeedsSetUp => ProjectPath is { Length: > 0 } && !HasBeenSetUp;

    /// <summary>
    /// Reads the settings for a project that has just been opened.
    /// </summary>
    /// <param name="projectPath">The project, or null when one was closed.</param>
    /// <param name="detected">What detection made of it, before any override.</param>
    public void Open(string? projectPath, ProjectKind detected)
    {
        ProjectPath = projectPath;

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            Kind = ProjectKind.None;
            HasBeenSetUp = false;
            LastGraphPath = string.Empty;
            return;
        }

        var settings = ProjectSettings.Load(projectPath);

        Kind = settings.KindOverride ?? detected;

        ScriptsFolder = settings.ScriptsFolder is { Length: > 0 } folder
            ? folder
            : DefaultFolderFor(Kind);

        DefaultModelPath = settings.DefaultModelPath;
        McpServerEnabled = settings.McpServerEnabled;
        ShareSettings = settings.ShareSettings;
        HasBeenSetUp = settings.HasBeenSetUp;
        LastGraphPath = settings.LastGraphPath;
    }

    /// <summary>What a project of this kind starts from before anybody has said otherwise.</summary>
    public static string DefaultFolderFor(ProjectKind kind)
        => kind == ProjectKind.Unity ? UnityDefault : PlainDefault;

    /// <summary>
    /// The folders this project actually has, for the list somebody chooses from.
    /// </summary>
    /// <remarks>
    /// What is there rather than what might be. Free text covers a folder that does not exist yet,
    /// which is the other half of the answer and is why this is a list to choose from rather than
    /// a list to be limited to.
    /// </remarks>
    public IReadOnlyList<string> ExistingFolders()
    {
        if (ProjectPath is not { Length: > 0 } root || !Directory.Exists(root))
        {
            return Array.Empty<string>();
        }

        var skip = new[] { "bin", "obj", "node_modules", "Library", "Temp", "dist", "out" };
        var found = new List<string>();

        try
        {
            foreach (var path in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                var segments = relative.Split('/');

                // Two levels is where a scripts folder lives in every layout worth offering, and
                // walking a whole project to offer somebody four hundred folders is not a list.
                if (segments.Length > 2
                    || segments.Any(s => s.StartsWith('.') || skip.Contains(s, StringComparer.OrdinalIgnoreCase)))
                {
                    continue;
                }

                found.Add(relative);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A project that will not enumerate still gets the free text box.
        }

        found.Sort(StringComparer.OrdinalIgnoreCase);
        return found;
    }

    /// <summary>
    /// Writes the settings back, and says where they went.
    /// </summary>
    /// <remarks>
    /// Reports rather than throws. A project folder that cannot be written to is somebody's
    /// read only checkout, and it is not a reason to stop them using the application; what it
    /// costs is that the answers are not remembered, which is worth saying out loud.
    /// </remarks>
    public void Save()
    {
        if (ProjectPath is not { Length: > 0 } root)
        {
            return;
        }

        var settings = new ProjectSettings
        {
            ScriptsFolder = ScriptsFolder,
            KindOverride = Kind == ProjectKind.None ? null : Kind,
            DefaultModelPath = DefaultModelPath,
            McpServerEnabled = McpServerEnabled,
            ShareSettings = ShareSettings,
            HasBeenSetUp = HasBeenSetUp,
            LastGraphPath = LastGraphPath
        };

        try
        {
            settings.Save(root);
            EnsureIgnored(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _feed.Error(
                "This project's settings were not saved",
                $"{root} could not be written to, so the answers will be asked for again next time. {ex.Message}");
        }
    }

    /// <summary>
    /// Keeps the settings out of the repository, unless sharing says otherwise.
    /// </summary>
    /// <remarks>
    /// Only ever appends to a gitignore that is already there. Creating one in a project that has
    /// none would be this application deciding how somebody's repository is arranged, and a project
    /// with no gitignore has nothing to ignore it from.
    ///
    /// The local file is always listed. The shared one is listed only while it is not being shared,
    /// which is the whole of what the toggle does on disk.
    /// </remarks>
    private void EnsureIgnored(string root)
    {
        var path = Path.Combine(root, ".gitignore");

        if (!File.Exists(path))
        {
            return;
        }

        var wanted = ShareSettings
            ? new[] { ProjectSettings.LocalFileName }
            : new[] { ProjectSettings.LocalFileName, ProjectSettings.SharedFileName };

        string text;

        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var lines = text.ReplaceLineEndings("\n").Split('\n').Select(l => l.Trim()).ToHashSet(StringComparer.Ordinal);
        var missing = wanted.Where(w => !lines.Contains(w)).ToList();

        if (missing.Count == 0)
        {
            return;
        }

        try
        {
            var addition = (text.EndsWith('\n') ? string.Empty : Environment.NewLine)
                           + Environment.NewLine
                           + "# LocalNEXUS project settings" + Environment.NewLine
                           + string.Join(Environment.NewLine, missing) + Environment.NewLine;

            File.AppendAllText(path, addition);

            _feed.Info(
                "Added to .gitignore",
                string.Join(", ", missing) + " will not be committed.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _feed.Error(
                ".gitignore was not updated",
                $"{string.Join(", ", missing)} may be committed unless you add them yourself. {ex.Message}");
        }
    }
}
