using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LocalNEXUS.App.Services.Files;

/// <summary>
/// What this project has been told about itself, as opposed to what was guessed.
/// </summary>
/// <remarks>
/// Two files rather than one, and the split is the same one every editor eventually arrives at.
/// <c>localnexus.json</c> holds what a team agrees about a project: where generated code goes, and
/// what kind of project it is. Those are conventions, they are the same for everyone working on it,
/// and a repository is where a convention belongs. <c>localnexus.local.json</c> holds what is true
/// of this machine and nobody else's: which model to reach for, whose path exists only here, and
/// whether this installation answers tool calls, which is a security posture rather than a project
/// fact.
///
/// Putting them in one file and gitignoring it would mean the conventions could never be shared.
/// Putting them in one file and committing it would put somebody's model path and somebody's
/// security choice in everybody's checkout. Neither is a thing to do, so there are two.
///
/// The shared one is gitignored by default all the same, because a project that has not decided to
/// share its conventions should not have a new file appear in its next commit without being asked.
/// The setup window is where that is decided.
/// </remarks>
public sealed class ProjectSettings
{
    /// <summary>The conventions, which a team may agree to share.</summary>
    public const string SharedFileName = "localnexus.json";

    /// <summary>What is true of this machine only, and is never committed.</summary>
    public const string LocalFileName = "localnexus.local.json";

    private static readonly JsonSerializerOptions Write = new() { WriteIndented = true };

    /// <summary>
    /// Where generated code goes, relative to the project root.
    /// </summary>
    /// <remarks>
    /// The value a newly added Output node starts from. Empty means nobody has said, which is what
    /// makes the setup window worth showing rather than guessing a second time.
    /// </remarks>
    public string ScriptsFolder { get; set; } = string.Empty;

    /// <summary>
    /// What this project is, when detection got it wrong and somebody said so.
    /// </summary>
    /// <remarks>
    /// Null means detection decides, which is the ordinary case. It is worth being able to
    /// override because the answer silently changes which write rules apply, and a project told it
    /// is not Unity when it is loses the refusals that stop a scene quietly losing its scripts.
    /// </remarks>
    public ProjectKind? KindOverride { get; set; }

    /// <summary>The model a new Model node in this project reaches for, or empty for none.</summary>
    public string DefaultModelPath { get; set; } = string.Empty;

    /// <summary>True when this installation answers MCP tool calls while this project is open.</summary>
    /// <remarks>
    /// Per project as well as globally, and both have to be on. The global switch is the decision
    /// that this installation answers to anything at all; this is the decision that it answers
    /// about this project in particular.
    /// </remarks>
    public bool McpServerEnabled { get; set; }

    /// <summary>True when the shared file is meant to be committed rather than ignored.</summary>
    public bool ShareSettings { get; set; }

    /// <summary>
    /// The graph that was last saved or loaded while this project was open.
    /// </summary>
    /// <remarks>
    /// Here rather than in the application configuration, which is where it used to be written and
    /// never read. A graph belongs to a project: it names that project's files, reaches for that
    /// project's default model, and is meaningless against a different one. Kept per machine
    /// because it is a path on this machine.
    /// </remarks>
    public string LastGraphPath { get; set; } = string.Empty;

    /// <summary>True once the setup window has been answered or skipped for this project.</summary>
    /// <remarks>
    /// In the local file rather than the shared one. Somebody cloning a repository that shares its
    /// conventions still has not been asked about their own machine, so they get the window once,
    /// with the shared answers already filled in.
    /// </remarks>
    public bool HasBeenSetUp { get; set; }

    /// <summary>Where the shared file lives for a project.</summary>
    public static string SharedPath(string projectPath) => Path.Combine(projectPath, SharedFileName);

    /// <summary>Where the local file lives for a project.</summary>
    public static string LocalPath(string projectPath) => Path.Combine(projectPath, LocalFileName);

    /// <summary>True when this project has never been set up on this machine.</summary>
    public static bool IsFirstOpen(string projectPath)
    {
        if (!Read(LocalPath(projectPath), out var local))
        {
            return true;
        }

        return local["hasBeenSetUp"]?.GetValue<bool>() != true;
    }

    /// <summary>
    /// Reads both files, with the local one winning where they overlap.
    /// </summary>
    /// <remarks>
    /// Never throws. A settings file that will not read is a project with no settings, which is the
    /// state every project was in before this existed and is not a reason to refuse to open one.
    /// </remarks>
    public static ProjectSettings Load(string? projectPath)
    {
        var settings = new ProjectSettings();

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return settings;
        }

        if (Read(SharedPath(projectPath), out var shared))
        {
            settings.ScriptsFolder = Text(shared, "scriptsFolder") ?? string.Empty;
            settings.KindOverride = ReadKind(Text(shared, "projectKind"));
            settings.ShareSettings = true;
        }

        if (Read(LocalPath(projectPath), out var local))
        {
            settings.DefaultModelPath = Text(local, "defaultModelPath") ?? string.Empty;
            settings.McpServerEnabled = local["mcpServerEnabled"]?.GetValue<bool>() == true;
            settings.HasBeenSetUp = local["hasBeenSetUp"]?.GetValue<bool>() == true;
            settings.LastGraphPath = Text(local, "lastGraphPath") ?? string.Empty;

            // The shared file may not exist, in which case the conventions live here instead. That
            // is the ordinary case, because sharing them is off until somebody says otherwise.
            if (!settings.ShareSettings)
            {
                settings.ScriptsFolder = Text(local, "scriptsFolder") ?? settings.ScriptsFolder;
                settings.KindOverride = ReadKind(Text(local, "projectKind")) ?? settings.KindOverride;
            }
        }

        return settings;
    }

    /// <summary>
    /// Writes both files, putting the conventions wherever sharing says they belong.
    /// </summary>
    /// <exception cref="IOException">The project folder could not be written to.</exception>
    public void Save(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        var local = new JsonObject
        {
            ["defaultModelPath"] = DefaultModelPath,
            ["mcpServerEnabled"] = McpServerEnabled,
            ["hasBeenSetUp"] = HasBeenSetUp,
            ["lastGraphPath"] = LastGraphPath
        };

        if (ShareSettings)
        {
            File.WriteAllText(SharedPath(projectPath), Conventions().ToJsonString(Write));
        }
        else
        {
            // Not shared, so the conventions travel in the local file and the shared one is not
            // left behind saying something out of date.
            foreach (var pair in Conventions())
            {
                local[pair.Key] = pair.Value?.DeepClone();
            }

            Delete(SharedPath(projectPath));
        }

        File.WriteAllText(LocalPath(projectPath), local.ToJsonString(Write));
    }

    private JsonObject Conventions() => new()
    {
        ["scriptsFolder"] = ScriptsFolder,
        ["projectKind"] = KindOverride?.ToString()
    };

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A stale shared file that will not delete is a file saying something old, which the
            // local one now overrides anyway.
        }
    }

    private static bool Read(string path, out JsonObject payload)
    {
        payload = new JsonObject();

        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject parsed)
            {
                return false;
            }

            payload = parsed;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static ProjectKind? ReadKind(string? text)
        => Enum.TryParse<ProjectKind>(text, ignoreCase: true, out var kind) && kind != ProjectKind.None
            ? kind
            : null;

    private static string? Text(JsonObject payload, string name)
        => payload[name]?.GetValueKind() == JsonValueKind.String ? payload[name]!.GetValue<string>() : null;
}
