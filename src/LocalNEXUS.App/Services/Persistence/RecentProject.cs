namespace LocalNEXUS.App.Services.Persistence;

/// <summary>
/// One project this installation has opened, as it is written to the configuration file.
/// </summary>
/// <remarks>
/// The path and when it was last opened, and nothing else. The name is the leaf folder and is
/// worked out when it is shown rather than stored, because a folder that was renamed on disk
/// should appear under the name it has now rather than the one it had when it was last opened.
/// </remarks>
public sealed class RecentProject
{
    /// <summary>Absolute path of the project folder.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>When it was last opened, for the ordering and for what the row says.</summary>
    public DateTimeOffset LastOpened { get; set; }
}
