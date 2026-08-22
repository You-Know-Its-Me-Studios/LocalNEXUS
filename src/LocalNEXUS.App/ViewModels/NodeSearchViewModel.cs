using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The search that places a node, opened on the canvas rather than from a menu.
/// </summary>
/// <remarks>
/// Two ways in and one list. Double clicking empty canvas offers everything; releasing a dragged
/// wire into empty space offers only the types that have a pin the dragged one could reach, which
/// is the faster of the two because it answers the question somebody dragging a wire actually has.
///
/// The list is whatever the factory says it is, extension contributed nodes included. Nothing here
/// names a node type, so a type that appears in the palette appears here without being added.
///
/// Which pins a type has cannot be read off a descriptor, so the filter builds one of each and
/// looks. That is one construction per type per search, on a list of a dozen, and the alternative
/// is a second table of pin shapes that would drift from the nodes it describes.
/// </remarks>
public sealed partial class NodeSearchViewModel : ObservableObject
{
    private readonly NodeFactory _factory;
    private readonly Action<string, double, double, Pin?> _place;

    private IReadOnlyList<NodeSearchResult> _all = Array.Empty<NodeSearchResult>();
    private double _x;
    private double _y;

    /// <summary>True while the search is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    private bool _isOpen;

    /// <summary>What has been typed so far.</summary>
    [ObservableProperty]
    private string _query = string.Empty;

    /// <summary>The row that pressing enter would place.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlaceCommand))]
    private NodeSearchResult? _selected;

    /// <summary>What the search is for, which differs between the two ways of opening it.</summary>
    [ObservableProperty]
    private string _prompt = "Add a node";

    /// <summary>The pin a dragged wire came from, or null when the search was opened on its own.</summary>
    public Pin? From { get; private set; }

    /// <param name="factory">The authority on which node types exist and how to build one.</param>
    /// <param name="place">Called with the type, where to put it, and the pin to wire back to.</param>
    public NodeSearchViewModel(NodeFactory factory, Action<string, double, double, Pin?> place)
    {
        _factory = factory;
        _place = place;
    }

    /// <summary>The rows matching what has been typed.</summary>
    public ObservableCollection<NodeSearchResult> Results { get; } = new();

    /// <summary>True when there is something to place, which is what the empty state is drawn from.</summary>
    public bool HasResults => Results.Count > 0;

    /// <summary>Opens the search over empty canvas, offering every node type.</summary>
    public void Open(double x, double y)
    {
        From = null;
        Prompt = "Add a node";

        Begin(x, y, _factory.AvailableDescriptors().Select(d => new NodeSearchResult(d.TypeKey, d.DisplayName, d.Description, null)).ToList());
    }

    /// <summary>
    /// Opens the search where a dragged wire was released, offering only what it could connect to.
    /// </summary>
    /// <remarks>
    /// Filtered by the same validator the canvas uses while a wire is being dragged, so a type
    /// offered here cannot be refused a moment later. A type with no reachable pin is left out
    /// rather than offered and then explained.
    /// </remarks>
    public void OpenFrom(Pin source, double x, double y)
    {
        From = source;
        Prompt = source.Direction == PinDirection.Output
            ? $"Wire {source.Owner.Title}.{source.Name} into"
            : $"Wire into {source.Owner.Title}.{source.Name} from";

        var matches = new List<NodeSearchResult>();

        foreach (var descriptor in _factory.AvailableDescriptors())
        {
            NodeBase probe;

            try
            {
                probe = _factory.Create(descriptor.TypeKey);
            }
            catch (NotSupportedException)
            {
                // A type the palette offers that cannot be built here is not a type to offer.
                continue;
            }

            if (FirstReachable(source, probe) is { } pin)
            {
                matches.Add(new NodeSearchResult(descriptor.TypeKey, descriptor.DisplayName, descriptor.Description, pin.Name));
            }
        }

        Begin(x, y, matches);
    }

    /// <summary>Closes without placing anything.</summary>
    [RelayCommand]
    public void Close()
    {
        IsOpen = false;
        Query = string.Empty;
        Selected = null;
        From = null;
        Results.Clear();
        OnPropertyChanged(nameof(HasResults));
    }

    /// <summary>
    /// Places a type, and wires it back when the search came from a pin.
    /// </summary>
    /// <remarks>
    /// Takes what to place rather than always reading the highlight, because a row that was
    /// clicked is not necessarily the row that was highlighted: whether a list has updated its
    /// selection by the time a click is acted on depends on which element saw the press first,
    /// which is not a thing to rely on. Clicking says what it means; the keyboard passes nothing
    /// and gets the highlight, which is what it means there.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanPlace))]
    private void Place(NodeSearchResult? result)
    {
        if ((result ?? Selected) is not { } chosen)
        {
            return;
        }

        var from = From;
        var x = _x;
        var y = _y;

        Close();

        _place(chosen.TypeKey, x, y, from);
    }

    private bool CanPlace(NodeSearchResult? result) => (result ?? Selected) is not null;

    /// <summary>Moves the highlight down, so the list can be worked without leaving the box.</summary>
    [RelayCommand]
    private void SelectNext() => Step(1);

    /// <summary>Moves the highlight up.</summary>
    [RelayCommand]
    private void SelectPrevious() => Step(-1);

    private void Step(int by)
    {
        if (Results.Count == 0)
        {
            return;
        }

        var index = Selected is { } current ? Results.IndexOf(current) : -1;
        var next = index < 0 ? 0 : (index + by + Results.Count) % Results.Count;

        Selected = Results[next];
    }

    private void Begin(double x, double y, IReadOnlyList<NodeSearchResult> candidates)
    {
        _x = x;
        _y = y;
        _all = candidates;

        Query = string.Empty;
        Filter();

        IsOpen = true;
    }

    partial void OnQueryChanged(string value) => Filter();

    /// <summary>
    /// Narrows the list to what has been typed, matching the name first and the description after.
    /// </summary>
    /// <remarks>
    /// Two passes rather than one so that typing a word in a node's name never buries it under a
    /// node that merely mentions the word. Searching the description at all is what lets somebody
    /// type "compile" and find the node whose name is Compiler check without knowing the name.
    /// </remarks>
    private void Filter()
    {
        var query = Query.Trim();

        Results.Clear();

        IEnumerable<NodeSearchResult> matched = query.Length == 0
            ? _all
            : _all.Where(r => Contains(r.DisplayName, query))
                .Concat(_all.Where(r => !Contains(r.DisplayName, query) && Contains(r.Description, query)));

        foreach (var result in matched)
        {
            Results.Add(result);
        }

        Selected = Results.FirstOrDefault();
        OnPropertyChanged(nameof(HasResults));
    }

    private static bool Contains(string text, string query)
        => text.Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>The first pin on a candidate that the dragged pin could legally reach.</summary>
    private static Pin? FirstReachable(Pin source, NodeBase candidate)
    {
        var wanted = source.Direction == PinDirection.Output ? candidate.Inputs : candidate.Outputs;

        foreach (var pin in wanted)
        {
            var (output, input) = source.Direction == PinDirection.Output ? (source, pin) : (pin, source);

            if (PinTypeCompatibility.CanFlow(output.PinType, input.PinType))
            {
                return pin;
            }
        }

        return null;
    }
}

/// <summary>
/// One row of the search.
/// </summary>
/// <remarks>
/// A record rather than a record struct, and that is not a style choice. The selection is nullable
/// and is taken from the first row of a list that is often empty, and the default of a struct is
/// not null: an empty search would have selected a row of empty strings, and the button that places
/// it would have been enabled and asked the factory to build a node type called nothing.
/// </remarks>
/// <param name="TypeKey">What to build.</param>
/// <param name="DisplayName">What it is called.</param>
/// <param name="Description">What it does, which is also searched.</param>
/// <param name="PinName">The pin the dragged wire would land on, or null when nothing was dragged.</param>
public sealed record NodeSearchResult(string TypeKey, string DisplayName, string Description, string? PinName)
{
    /// <summary>What the row says under the name, which is the pin when there is one.</summary>
    public string Detail => PinName is null ? Description : $"{PinName}  ·  {Description}";
}
