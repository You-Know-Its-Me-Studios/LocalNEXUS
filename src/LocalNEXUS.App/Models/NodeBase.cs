using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Services.Execution;

namespace LocalNEXUS.App.Models;

/// <summary>
/// Base class for every node on the canvas. A node owns its pins, its canvas position,
/// its execution state, and knows how to execute itself and how to persist its settings.
/// </summary>
public abstract partial class NodeBase : ObservableObject
{
    /// <summary>Text shown in the node header on the canvas.</summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>Horizontal canvas position in graph space.</summary>
    [ObservableProperty]
    private double _x;

    /// <summary>Vertical canvas position in graph space.</summary>
    [ObservableProperty]
    private double _y;

    /// <summary>Execution state for the current run.</summary>
    [ObservableProperty]
    private NodeState _state = NodeState.Pending;

    /// <summary>True while this node is the canvas selection. Drives the settings panel.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Short status line shown in the node footer, for example a token count or an error.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>
    /// Raised when something the serializer would write has changed on this node.
    /// </summary>
    /// <remarks>
    /// The document listens to this to know it differs from what is on disk. It used to watch the
    /// node's view model instead, which republishes four properties and none of them is a setting,
    /// so nothing anybody changed on a node ever marked the graph as edited. A model, a
    /// temperature, an output folder and a whole extension selection could all be set, and the tab
    /// showed no dot, so the graph was closed without being saved and every one of them was lost.
    ///
    /// Raised from the property change notification rather than from each setter, so a node cannot
    /// gain a setting that quietly does not count as an edit. What is left out is the three things
    /// that move while a run is going, because a node reporting where it has got to is not an edit
    /// and a graph running is not a graph changing.
    /// </remarks>
    public event Action<NodeBase>? SettingsChanged;

    /// <summary>
    /// Says that something worth saving has changed, for a change no property notification covers.
    /// </summary>
    /// <remarks>
    /// A node holding an observable collection needs this: adding to a list raises a collection
    /// change and never a property change, so the selection of extensions and tools was invisible
    /// to anything watching properties.
    /// </remarks>
    protected void RaiseSettingsChanged() => SettingsChanged?.Invoke(this);

    /// <inheritdoc />
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName is nameof(State) or nameof(StatusMessage) or nameof(IsSelected))
        {
            return;
        }

        RaiseSettingsChanged();
    }

    protected NodeBase(string title)
    {
        Id = Guid.NewGuid();
        Title = title;
    }

    /// <summary>Stable identity, preserved across save and load.</summary>
    public Guid Id { get; internal set; }

    /// <summary>Discriminator written to the graph file and used to reconstruct this node on load.</summary>
    public abstract string TypeKey { get; }

    /// <summary>Pins that consume values from upstream nodes.</summary>
    public ObservableCollection<Pin> Inputs { get; } = new();

    /// <summary>Pins that produce values for downstream nodes.</summary>
    public ObservableCollection<Pin> Outputs { get; } = new();

    /// <summary>
    /// Canvas position as a single point. The canvas binds to this two way while the
    /// executor and the serializer work with <see cref="X"/> and <see cref="Y"/>.
    /// </summary>
    public Point Location
    {
        get => new(X, Y);
        set
        {
            X = value.X;
            Y = value.Y;
        }
    }

    /// <summary>Runs this node against the values gathered from its incoming connections.</summary>
    public abstract Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct);

    /// <summary>Captures this node's settings for persistence. Position and identity are handled by the serializer.</summary>
    public abstract JsonObject SaveSettings();

    /// <summary>Restores settings previously produced by <see cref="SaveSettings"/>.</summary>
    public abstract void LoadSettings(JsonObject settings);

    /// <summary>Returns this node to the pending state before a new run begins.</summary>
    public void ResetState()
    {
        State = NodeState.Pending;
        StatusMessage = null;
    }

    /// <summary>Declares an input pin. Intended for use from derived constructors.</summary>
    protected Pin AddInput(string name, PinType pinType)
    {
        var pin = new Pin(this, name, pinType, PinDirection.Input);
        Inputs.Add(pin);
        return pin;
    }

    /// <summary>Declares an output pin. Intended for use from derived constructors.</summary>
    protected Pin AddOutput(string name, PinType pinType)
    {
        var pin = new Pin(this, name, pinType, PinDirection.Output);
        Outputs.Add(pin);
        return pin;
    }

    partial void OnXChanged(double value) => OnPropertyChanged(nameof(Location));

    partial void OnYChanged(double value) => OnPropertyChanged(nameof(Location));

    public override string ToString() => $"{TypeKey}:{Title}";
}
