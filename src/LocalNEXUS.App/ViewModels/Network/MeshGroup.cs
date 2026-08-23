using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LocalNEXUS.App.ViewModels.Network;

/// <summary>
/// One heading in the mesh list, and the meshes under it.
/// </summary>
/// <remarks>
/// Yours, joined and found are the same kind of thing at three distances, so they are three groups
/// of one list rather than two tables and a set of rows mixed into a third. Grouping is what lets
/// the list say which is which without a column spent saying it.
///
/// Collapsible, because the interesting group differs by what somebody is doing. Somebody hosting
/// wants the top one open and the other two shut; somebody who has just searched wants the
/// opposite.
/// </remarks>
public sealed partial class MeshGroup : ObservableObject
{
    public MeshGroup(string title, string emptyText, bool isExpanded = true)
    {
        Title = title;
        EmptyText = emptyText;
        _isExpanded = isExpanded;
    }

    /// <summary>What the heading says.</summary>
    public string Title { get; }

    /// <summary>What to show when the group is open and holds nothing.</summary>
    public string EmptyText { get; }

    /// <summary>The meshes in it, of whichever row type this group holds.</summary>
    public ObservableCollection<object> Items { get; } = new();

    /// <summary>Whether the group is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Chevron))]
    private bool _isExpanded;

    /// <summary>How many are in it, for the heading, so a shut group still says whether it is worth opening.</summary>
    public int Count => Items.Count;

    /// <summary>True when the group holds nothing, so the heading can say so rather than opening onto a blank.</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>The arrow, which points down when the group is open.</summary>
    public string Chevron => IsExpanded ? "" : "";

    /// <summary>Opens or shuts the group, which the heading is the control for.</summary>
    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;

    /// <summary>Re-reads what depends on the contents, which the collection cannot announce itself.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
