using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalNEXUS.App.ViewModels.Network;

/// <summary>
/// One heading in the filter sidebar and the mutually exclusive choices under it.
/// </summary>
public sealed partial class ModelFilterGroup : ObservableObject
{
    /// <summary>The choice currently in force. Never null: every group has an "all" entry.</summary>
    [ObservableProperty]
    private ModelFilter _selected;

    public ModelFilterGroup(string title, string note, IEnumerable<ModelFilter> filters)
    {
        Title = title;
        Note = note;
        Filters = new ObservableCollection<ModelFilter>(filters);
        _selected = Filters[0];
        _selected.IsSelected = true;
    }

    /// <summary>What the heading says.</summary>
    public string Title { get; }

    /// <summary>
    /// A sentence explaining where the group gets its answer, shown as a tool tip. Two of the
    /// groups infer their answer rather than being told it, and that is worth being able to find
    /// out.
    /// </summary>
    public string Note { get; }

    /// <summary>The choices, with the "all" entry first.</summary>
    public ObservableCollection<ModelFilter> Filters { get; }

    /// <summary>Puts one choice in force and takes the others out of it.</summary>
    public void Select(ModelFilter filter)
    {
        if (!Filters.Contains(filter))
        {
            return;
        }

        foreach (var candidate in Filters)
        {
            candidate.IsSelected = candidate == filter;
        }

        Selected = filter;
    }

    /// <summary>True when this row survives whatever this group has in force.</summary>
    public bool Keeps(INetworkRow row) => Selected.Keeps(row);
}
