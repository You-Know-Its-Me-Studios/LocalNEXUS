using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalNEXUS.App.Services.Persistence;

/// <summary>
/// The settings that survive between sessions: everything the settings panel edits, plus what the
/// window was last left doing.
/// </summary>
public sealed class AppConfig
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// The theme the window is wearing. Applied before anything is painted.
    /// </summary>
    /// <remarks>
    /// The default is what an install with no configuration file gets, and nothing more than that.
    /// A file that exists has this property in it, whether or not anybody ever opened the picker,
    /// so moving the default cannot reach back and repaint a machine that has already run.
    /// </remarks>
    public Theming.AppTheme Theme { get; set; } = Theming.ThemeService.DefaultTheme;

    /// <summary>
    /// How many runs keep their file snapshots.
    /// </summary>
    /// <remarks>
    /// Text is small and snapshots are not, so only snapshots are capped. A run past this keeps
    /// its whole transcript and simply can no longer be undone, which is the right thing to lose
    /// first: what happened is worth more than the ability to reverse something from last month.
    /// </remarks>
    public int SnapshotRunLimit { get; set; } = 50;

    /// <summary>How many days a snapshot is kept before it is dropped.</summary>
    public int SnapshotAgeDays { get; set; } = 30;

    /// <summary>
    /// How opaque the window's base layer is, from the readability floor to fully solid.
    /// </summary>
    /// <remarks>
    /// Kept whatever the theme is, rather than cleared when a theme without transparency is
    /// picked, so that going back to one that has it returns to the setting it was left at.
    /// </remarks>
    public double WindowOpacity { get; set; } = 0.86d;

    /// <summary>
    /// Repair attempts a newly added compile check node starts with.
    /// </summary>
    /// <remarks>
    /// These defaults are starting points, not overrides. A node writes its own value into the
    /// graph file, so changing a default here changes the next node added and never a graph that
    /// already exists.
    /// </remarks>
    public int DefaultRetryLimit { get; set; } = 3;

    /// <summary>Characters of project map a newly added plan node starts with.</summary>
    public int DefaultMapCharacters { get; set; } = 4000;

    /// <summary>Characters of candidate file contents a newly added plan node starts with.</summary>
    public int DefaultCandidateCharacters { get; set; } = 16000;

    /// <summary>Characters of same-run signatures a newly added plan node starts with.</summary>
    public int DefaultEmittedCharacters { get; set; } = 4000;

    /// <summary>How many candidate files a newly added plan node offers before reading any.</summary>
    public int DefaultCandidateLimit { get; set; } = 12;

    /// <summary>
    /// The cloud provider a newly added model node is pointed at when it is set to a cloud
    /// provider. Blank means the provider default.
    /// </summary>
    public string? CloudBaseUrl { get; set; }

    /// <summary>
    /// What a run may cost before it asks first, in dollars. Zero switches the warning off.
    /// </summary>
    public decimal CostWarningThreshold { get; set; } = 1.00m;

    /// <summary>OpenAI compatible endpoints the user added by url.</summary>
    public List<CustomProviderRecord> CustomProviders { get; set; } = new();

    /// <summary>
    /// The key a newly added model node starts with, so a key is typed once rather than once per
    /// node.
    /// </summary>
    /// <remarks>
    /// Stored in clear text in this file, which is the same posture a graph file already has: a
    /// model node writes its key into the graph it belongs to. Worth knowing before pasting a key
    /// into a file that gets shared.
    /// </remarks>

    /// <summary>
    /// The project folder the folder pickers start in.
    /// </summary>
    /// <remarks>
    /// No longer what is opened at launch. The front door asks instead, because opening whatever
    /// happened to be last is a guess made on somebody's behalf about the thing they are least
    /// likely to want guessed. What it is still good for is where a folder picker starts.
    /// </remarks>
    public string? LastProjectPath { get; set; }

    /// <summary>
    /// The projects this installation has opened, most recent first.
    /// </summary>
    /// <remarks>
    /// The front door is answered from this almost every time, so it is a list rather than one
    /// path. Order in the file is not trusted; it is sorted by when each was last opened.
    /// </remarks>
    public List<RecentProject> RecentProjects { get; set; } = new();

    /// <summary>Folders added by the user that are scanned for models alongside the default one.</summary>
    public List<string> ExtraModelFolders { get; set; } = new();

    /// <summary>
    /// Individual models added by the user: one GGUF file, or one safetensors folder.
    /// </summary>
    /// <remarks>
    /// Separate from the scanned folders because it means something different. A folder is a
    /// standing instruction to look inside it and keep looking; this is one model, named, and
    /// nothing around it is registered by having added it.
    /// </remarks>
    public List<string> ExtraModelPaths { get; set; } = new();

    /// <summary>
    /// This install's own identity, generated once and never regenerated. A running mesh node
    /// has a stronger identity of its own, its public key, which is what peers and any later
    /// reputation attach to; this one exists so an install still has a stable id of its own
    /// before its node has ever been started.
    /// </summary>
    public Guid SourceId { get; set; }

    /// <summary>Whether the mesh node is started with the application.</summary>
    public bool MeshEnabled { get; set; }

    /// <summary>Whether this machine offers its own compute to the mesh rather than only routing.</summary>
    public bool MeshContribute { get; set; }

    /// <summary>
    /// The one model this machine used to serve. Kept only so an existing file can be read; the
    /// list below replaced it and is what gets written.
    /// </summary>
    public string? MeshOfferedModelPath { get; set; }

    /// <summary>
    /// The models this machine offers to the mesh. Empty means it offers none, which is the
    /// starting state: what gets served to other people is a decision somebody has to make.
    /// </summary>
    public List<string> MeshOfferedModelPaths { get; set; } = new();

    /// <summary>Cap on the memory this machine offers, in GB. Zero lets the engine decide.</summary>
    public double MeshMaxVramGb { get; set; }

    /// <summary>
    /// True when the cap follows the card rather than a typed number, so a bigger card is used
    /// without anyone having to come back and edit a figure.
    /// </summary>
    public bool MeshOfferAllMemory { get; set; }

    /// <summary>Invite token of a mesh to join. Blank means this install hosts its own private mesh.</summary>
    public string? MeshJoinToken { get; set; }

    /// <summary>Friendly name of the mesh this install hosts.</summary>
    public string? MeshName { get; set; }

    /// <summary>
    /// Advertises this mesh for public discovery. Off by default: a private mesh on the local
    /// network is the default posture, and this is the only setting that changes it.
    /// </summary>
    public bool MeshPublish { get; set; }

    /// <summary>Port the mesh node's OpenAI compatible API listens on.</summary>
    public int MeshApiPort { get; set; } = 9337;

    /// <summary>Port the mesh node's management API answers on.</summary>
    public int MeshConsolePort { get; set; } = 3131;

    /// <summary>
    /// True when this installation answers MCP tool calls from other tools.
    /// </summary>
    /// <remarks>
    /// Off by default, and that is a decision rather than caution. With it on, anything on this
    /// account that can start a process can open a project, open a graph and run it, and a run
    /// writes files and spends whatever a cloud model costs. That is a different security posture
    /// from an application that only does what the person in front of it asks for, and it is the
    /// sort of difference somebody should choose rather than discover.
    ///
    /// The server is a local named pipe and never a socket, so switching it on is not a network
    /// exposure. What it is is a second way in.
    /// </remarks>
    /// <summary>
    /// Where the vision model that reads images lives, or null when there is none.
    /// </summary>
    /// <remarks>
    /// One for the application rather than one per node, because reading an image happens before a
    /// run rather than inside one: the image never enters the graph, and what joins the request is
    /// the text it produced.
    ///
    /// Two ways in, and both are kept. A local model is a GGUF picked from the model folders like
    /// any other, and this application starts the server for it. An address and a model id point
    /// at a hosted model or at a server somebody is already running, which the local path cannot
    /// replace.
    /// </remarks>
    public string? VisionBaseUrl { get; set; }

    /// <summary>Which model at that address reads images.</summary>
    public string? VisionModelId { get; set; }

    /// <summary>
    /// A local vision model on disk, served by this application, or null when there is none.
    /// </summary>
    /// <remarks>
    /// Takes precedence over the address, because someone who has picked a file has said which of
    /// the two they mean. The projector beside it is found rather than stored: it lives with the
    /// weights, and a saved path would go stale the moment the folder moved.
    /// </remarks>
    public string? VisionModelPath { get; set; }

    public bool McpServerEnabled { get; set; }

    /// <summary>True once somebody has closed the walkthrough, so it stops opening itself.</summary>
    /// <remarks>
    /// Dismissal and not completion. Somebody who works through every step never has to press
    /// anything for it to stop being useful, and somebody who wants it back finds it on the Help
    /// menu, which is why this is the only thing about it that is remembered.
    /// </remarks>
    public bool WalkthroughDismissed { get; set; }

    /// <summary>True once a run has finished on this machine, which is the walkthrough's last step.</summary>
    /// <remarks>
    /// The one step nothing else can see. A run that completed leaves nothing behind that is still
    /// true a minute later, so unlike the other four this one has to be written down.
    /// </remarks>
    public bool HasCompletedAWalkthroughRun { get; set; }

    /// <summary>
    /// What went wrong reading the configuration, or null when nothing did.
    /// </summary>
    /// <remarks>
    /// Read once by the shell and written to the feed. Falling back to defaults is still the right
    /// behaviour, because a configuration that will not parse must not stop the application
    /// starting, but doing it in silence was not: the session then saves those defaults back over
    /// the file at the first opportunity and every setting is gone with nothing said. Losing them
    /// is survivable. Losing them without being told is what makes it look like the application
    /// simply ignores what it is told.
    /// </remarks>
    public static string? LoadProblem { get; private set; }

    /// <summary>
    /// Reads the configuration from disk. A missing or unreadable file yields defaults rather
    /// than an error, because losing this state is never worth blocking startup over.
    /// </summary>
    public static AppConfig Load()
    {
        LoadProblem = null;

        try
        {
            if (!File.Exists(AppPaths.ConfigFile))
            {
                return new AppConfig();
            }

            var json = File.ReadAllText(AppPaths.ConfigFile);
            var config = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions) ?? new AppConfig();

            config.Migrate();
            return config;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            LoadProblem = $"{AppPaths.ConfigFile} could not be read, so this session started from defaults "
                          + $"and anything it saves will replace what was there. {ex.Message}";

            KeepBrokenFile();

            return new AppConfig();
        }
    }

    /// <summary>
    /// Puts an unreadable configuration somewhere it will not be overwritten.
    /// </summary>
    /// <remarks>
    /// Copied rather than moved, so the application still starts from a file it recognises and
    /// nothing depends on this having worked. What it buys is that the settings are recoverable by
    /// hand instead of being replaced by the first save of the session.
    /// </remarks>
    private static void KeepBrokenFile()
    {
        try
        {
            var kept = Path.Combine(
                Path.GetDirectoryName(AppPaths.ConfigFile)!,
                $"config.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}.json");

            File.Copy(AppPaths.ConfigFile, kept, overwrite: false);

            LoadProblem += $" A copy of it was kept as {Path.GetFileName(kept)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keeping a copy is a courtesy. Failing at it must not turn a recoverable start into a
            // failed one.
        }
    }

    /// <summary>
    /// Brings a file written by an earlier build forward.
    /// </summary>
    /// <remarks>
    /// The theme rename needs nothing done to it, and that is worth writing down rather than
    /// leaving to be rediscovered. Enums are written here as numbers, so a saved theme is a
    /// position rather than a name: <c>VsCodeDark</c> became <c>EditorDark</c> without moving in
    /// the enum, so an existing choice reads back as the same palette. Reordering those members,
    /// or writing them as names, would silently change what everybody is looking at, and this is
    /// where the migration for that would go.
    /// </remarks>
    private void Migrate()
    {
        // One offered model became a list, and the old value is deliberately not carried across.
        // It was never chosen: the panel defaulted it to whichever model happened to be first and
        // saved that, which is exactly the implicit decision the list exists to replace. Starting
        // at none means the first thing shared is the first thing somebody ticked.
        MeshOfferedModelPath = null;
    }

    /// <summary>
    /// Reads the configuration, writing a default file when there is not one yet, so that a first
    /// run leaves a complete and editable data folder behind.
    /// </summary>
    public static AppConfig LoadOrCreate()
    {
        var existed = File.Exists(AppPaths.ConfigFile);
        var config = Load();

        if (!existed)
        {
            config.Save();
        }

        return config;
    }

    /// <summary>
    /// Writes the configuration to disk, creating the data folder if needed.
    /// </summary>
    /// <remarks>
    /// Written beside the real file and moved into place, rather than over it. Writing in place
    /// truncates first, so anything reading during that window sees an empty or half written file,
    /// and anything that stops the process during it leaves one on disk. Either way the next launch
    /// cannot parse it, falls back to defaults, and saves those defaults over everything.
    ///
    /// Under a lock for the same reason. This is called from the mesh, the model scan, the theme,
    /// the recent projects list and half the panels, several of them off the user interface thread,
    /// and two of them writing the same file at once produces exactly the file that cannot be read.
    /// </remarks>
    public void Save()
    {
        AppPaths.EnsureCreated();

        var json = JsonSerializer.Serialize(this, SerializerOptions);

        lock (SaveGate)
        {
            var temporary = AppPaths.ConfigFile + ".writing";

            File.WriteAllText(temporary, json);
            File.Move(temporary, AppPaths.ConfigFile, overwrite: true);
        }
    }

    /// <summary>Serialises writers, because a torn configuration file loses every setting there is.</summary>
    private static readonly object SaveGate = new();
}

/// <summary>
/// An OpenAI compatible endpoint somebody added themselves.
/// </summary>
/// <remarks>
/// Only the name and the address. The key for it lives in the credential store like every other
/// key, so this record is safe to sit in a plain settings file.
/// </remarks>
/// <param name="Name">What to call it.</param>
/// <param name="BaseUrl">Root of its API.</param>
public sealed record CustomProviderRecord(string Name, string BaseUrl);
