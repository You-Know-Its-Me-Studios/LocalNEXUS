using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalNEXUS.App.Services.Persistence;

/// <summary>Whether a project in the recent list is still where it was left.</summary>
public enum RecentProjectState
{
    /// <summary>The folder is there and can be opened.</summary>
    Available,

    /// <summary>The folder has been moved, renamed or deleted since it was last opened.</summary>
    Missing
}

/// <summary>
/// One row of the recent projects list, as the front door shows it.
/// </summary>
/// <remarks>
/// Enough to tell two projects apart, which is the whole job of this list: the name, the path
/// under it, and when it was last opened. Two projects called <c>src</c> in different checkouts is
/// the ordinary case rather than the awkward one.
///
/// Whether the folder is still there is read when the list is built rather than when a row is
/// clicked, so a project that has moved says so on the way in instead of failing on the way out.
/// </remarks>
public sealed partial class RecentProjectEntry : ObservableObject
{
    public RecentProjectEntry(string path, DateTimeOffset lastOpened)
    {
        Path = path;
        LastOpened = lastOpened;
        State = Directory.Exists(path) ? RecentProjectState.Available : RecentProjectState.Missing;
    }

    /// <summary>Absolute path of the project folder.</summary>
    public string Path { get; }

    /// <summary>When it was last opened.</summary>
    public DateTimeOffset LastOpened { get; }

    /// <summary>Whether the folder is still there.</summary>
    public RecentProjectState State { get; }

    /// <summary>True when clicking this row would open something.</summary>
    public bool IsAvailable => State == RecentProjectState.Available;

    /// <summary>True when this row is offering to be forgotten rather than opened.</summary>
    public bool IsMissing => State == RecentProjectState.Missing;

    /// <summary>The leaf folder name, which is what a project is called.</summary>
    public string Name
    {
        get
        {
            var name = System.IO.Path.GetFileName(Path.TrimEnd(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar));

            return name.Length > 0 ? name : Path;
        }
    }

    /// <summary>
    /// When it was last opened, in the terms somebody thinks in.
    /// </summary>
    /// <remarks>
    /// A date and time is precise and answers the wrong question. What is being asked is which of
    /// these was I in yesterday, so near days are named and anything older falls back to a date.
    /// </remarks>
    public string WhenText
    {
        get
        {
            if (State == RecentProjectState.Missing)
            {
                return "Not found";
            }

            var days = (DateTimeOffset.Now.Date - LastOpened.Date).Days;

            return days switch
            {
                <= 0 => $"Today at {LastOpened.LocalDateTime:HH:mm}",
                1 => $"Yesterday at {LastOpened.LocalDateTime:HH:mm}",
                < 7 => $"{days} days ago",
                < 14 => "Last week",
                _ => LastOpened.LocalDateTime.ToString("d MMMM yyyy")
            };
        }
    }
}
