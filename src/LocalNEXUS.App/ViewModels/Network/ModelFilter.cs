using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalNEXUS.App.ViewModels.Network;

/// <summary>
/// One entry in a filter group: what it is called, what it keeps, and how many rows it would
/// leave.
/// </summary>
/// <remarks>
/// The count is the point. A filter list without counts asks the person to click each one to find
/// out whether it is worth clicking, and the answer is usually no.
/// </remarks>
public sealed partial class ModelFilter : ObservableObject
{
    private readonly Func<INetworkRow, bool> _predicate;

    /// <summary>How many rows this filter would leave, out of everything the mesh knows.</summary>
    [ObservableProperty]
    private int _count;

    /// <summary>True while this is the filter in force for its group.</summary>
    [ObservableProperty]
    private bool _isSelected;

    public ModelFilter(string label, Func<INetworkRow, bool> predicate, ICommand apply, bool isSelected = false)
    {
        Label = label;
        _predicate = predicate;
        _isSelected = isSelected;
        Apply = apply;
    }

    /// <summary>What the row is called in the sidebar.</summary>
    public string Label { get; }

    /// <summary>
    /// Puts this filter in force. Carried on the item rather than reached for up the visual tree,
    /// because these are drawn by a list inside a list and binding out of two levels of item
    /// template is the kind of thing that works until somebody adds a third.
    /// </summary>
    public ICommand Apply { get; }

    /// <summary>True when this filter keeps a row.</summary>
    public bool Keeps(INetworkRow row) => _predicate(row);
}
