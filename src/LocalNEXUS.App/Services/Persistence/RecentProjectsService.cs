using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalNEXUS.App.Services.Persistence;

/// <summary>
/// The projects this installation has opened, most recent first.
/// </summary>
/// <remarks>
/// The front door is answered from this list almost every time, because almost every session is a
/// return to something rather than a start of something. So it is a real list rather than one last
/// path, and it is ordered by when each was last opened rather than by name.
///
/// Observable and bound to directly, following the model catalogue and the extension registry.
/// Recording an open rebuilds the rows, which is what makes a project just opened jump to the top
/// of a list that is on screen.
/// </remarks>
public sealed partial class RecentProjectsService : ObservableObject
{
    /// <summary>
    /// How many are kept.
    /// </summary>
    /// <remarks>
    /// A list somebody scans rather than one they search. Past about this many the answer stops
    /// being visible at a glance, which is the only thing this list is for.
    /// </remarks>
    public const int Capacity = 10;

    private readonly AppConfig _config;
    private readonly Action _save;

    /// <summary>
    /// Builds the list from the configuration.
    /// </summary>
    /// <param name="config">Where the list is kept.</param>
    /// <param name="save">
    /// How a change is written, which the application leaves alone and a test replaces. The
    /// configuration file is one per user rather than one per run, so a test that wrote to it
    /// would be editing the recent projects of whoever ran the suite.
    /// </param>
    public RecentProjectsService(AppConfig config, Action? save = null)
    {
        _config = config;
        _save = save ?? config.Save;

        Rebuild();
    }

    /// <summary>The rows, most recently opened first.</summary>
    public ObservableCollection<RecentProjectEntry> Items { get; } = new();

    /// <summary>True when there is nothing to return to, which is a first run.</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>
    /// Records that a project was opened, whoever opened it.
    /// </summary>
    /// <remarks>
    /// Keyed on the full path so the same project opened through a different spelling of its path
    /// does not become a second row.
    /// </remarks>
    public void Record(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return;
        }

        string full;

        try
        {
            full = Path.GetFullPath(projectPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        _config.RecentProjects.RemoveAll(r =>
            string.Equals(r.Path, full, StringComparison.OrdinalIgnoreCase));

        _config.RecentProjects.Insert(0, new RecentProject { Path = full, LastOpened = DateTimeOffset.Now });

        Trim();
        _save();

        Rebuild();
    }

    /// <summary>Forgets one project, which is what a row that no longer exists offers.</summary>
    public void Remove(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return;
        }

        var removed = _config.RecentProjects.RemoveAll(r =>
            string.Equals(r.Path, projectPath, StringComparison.OrdinalIgnoreCase));

        if (removed == 0)
        {
            return;
        }

        _save();
        Rebuild();
    }

    /// <summary>Reads the folders again, so a project that has moved since is shown as missing.</summary>
    public void Refresh() => Rebuild();

    private void Trim()
    {
        if (_config.RecentProjects.Count <= Capacity)
        {
            return;
        }

        _config.RecentProjects.RemoveRange(Capacity, _config.RecentProjects.Count - Capacity);
    }

    private void Rebuild()
    {
        Items.Clear();

        foreach (var recent in _config.RecentProjects
                     .Where(r => !string.IsNullOrWhiteSpace(r.Path))
                     .OrderByDescending(r => r.LastOpened)
                     .Take(Capacity))
        {
            Items.Add(new RecentProjectEntry(recent.Path, recent.LastOpened));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }
}
