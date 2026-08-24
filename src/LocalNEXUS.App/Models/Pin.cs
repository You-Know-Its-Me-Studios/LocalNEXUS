using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalNEXUS.App.Models;

/// <summary>
/// A single connection point on a node. Pins carry both the domain information used by
/// the executor (type, direction, owner) and the small amount of view state that the
/// canvas needs in order to draw wires (<see cref="Anchor"/>, <see cref="IsConnected"/>).
/// </summary>
public sealed partial class Pin : ObservableObject
{
    /// <summary>The point on the canvas where wires attach. Written by the canvas, read by connections.</summary>
    [ObservableProperty]
    private Point _anchor;

    /// <summary>True when at least one connection currently uses this pin.</summary>
    [ObservableProperty]
    private bool _isConnected;

    /// <summary>
    /// For an input, the output pin currently feeding it. Null for an output, and for an input
    /// with nothing wired in.
    /// </summary>
    /// <remarks>
    /// Maintained by <see cref="GraphModel"/> everywhere <see cref="IsConnected"/> is, and for the
    /// same reason: the graph owns wiring, and a pin records what was done to it. It exists so a
    /// node can answer a question about its neighbour without a run in progress. During a run the
    /// executor already provides that through the execution context, but a panel drawn before
    /// anybody presses Run has no context to ask, and a warning that only appears once the run has
    /// started is a warning about a decision already taken.
    ///
    /// An input takes one wire, which the connection validator enforces, so this is one pin rather
    /// than a list.
    /// </remarks>
    [ObservableProperty]
    private Pin? _sourcePin;

    public Pin(NodeBase owner, string name, PinType pinType, PinDirection direction)
    {
        Owner = owner;
        Name = name;
        PinType = pinType;
        Direction = direction;
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Stable identity for this pin. Assigned at construction and restored verbatim by
    /// <see cref="Services.Persistence.GraphSerializer"/> so that saved connections still resolve.
    /// </summary>
    public Guid Id { get; internal set; }

    /// <summary>Label shown next to the connector on the canvas.</summary>
    public string Name { get; }

    /// <summary>The value kind this pin carries. Drives both colour and connection validation.</summary>
    public PinType PinType { get; }

    /// <summary>Whether this pin consumes or produces a value.</summary>
    public PinDirection Direction { get; }

    /// <summary>The node this pin belongs to.</summary>
    public NodeBase Owner { get; }

    public override string ToString() => $"{Owner.Title}.{Name} ({PinType}, {Direction})";
}
