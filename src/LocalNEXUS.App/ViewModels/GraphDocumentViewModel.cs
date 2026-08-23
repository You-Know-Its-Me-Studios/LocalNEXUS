using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The open graph as an editor tab and as a run outline: what it is called, whether it has
/// unsaved changes, and the state of every node in it.
/// </summary>
/// <remarks>
/// This is the layer that lets the side bar double as a live run outline. The outline and the
/// canvas draw the same <see cref="NodeViewModel"/> instances, so a node that starts running
/// lights up in both places from one notification and neither has to go looking for the other.
///
/// The graph itself is still one document. The tab strip is a strip because that is the shape of
/// the interface, not because several graphs can be open: opening a second one is a change to how
/// running and saving work rather than to how they look, and this slice does not change
/// behaviour. The collection is where that would go.
/// </remarks>
public sealed partial class GraphDocumentViewModel : ObservableObject, IDisposable
{
    /// <summary>How often a running node re-reads its clock. Fast enough to move, cheap enough to ignore.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);

    private readonly GraphModel _graph;
    private readonly Func<RunState> _runState;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<NodeBase, NodeViewModel> _byNode = new();

    private bool _disposed;

    /// <summary>Path this graph was last saved to or loaded from, or null when it has never been either.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name))]
    [NotifyPropertyChangedFor(nameof(PathText))]
    private string? _path;

    /// <summary>True when the graph has changes that are not on disk.</summary>
    [ObservableProperty]
    private bool _isDirty;

    public GraphDocumentViewModel(GraphModel graph, Func<RunState> runState, Dispatcher dispatcher)
    {
        _graph = graph;
        _runState = runState;

        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TickInterval };
        _timer.Tick += (_, _) => Tick();

        graph.Nodes.CollectionChanged += OnNodesChanged;
        graph.Connections.CollectionChanged += OnConnectionsChanged;

        Rebuild();
    }

    /// <summary>Every node, in the order the graph holds them, as the canvas and the outline see them.</summary>
    public ObservableCollection<NodeViewModel> Nodes { get; } = new();

    /// <summary>What the editor tab and the title bar call this graph.</summary>
    /// <remarks>
    /// Read from the graph rather than kept beside it, so that what a run records and what the tab
    /// says cannot drift apart.
    /// </remarks>
    public string Name => _graph.Name;

    /// <summary>The full path, or a note that there is not one yet.</summary>
    public string PathText => Path ?? "not saved to disk yet";

    /// <summary>The right hand end of the status bar while the Workspace is showing.</summary>
    public string SummaryText
    {
        get
        {
            var nodes = _graph.Nodes.Count;
            var wires = _graph.Connections.Count;

            return $"{nodes} {(nodes == 1 ? "node" : "nodes")}, {wires} {(wires == 1 ? "wire" : "wires")}";
        }
    }

    /// <summary>Records that what is on the canvas now matches what is on disk.</summary>
    public void MarkSaved(string? path)
    {
        Path = path;

        // Saving under a new name renames the graph. There is one name and this is where it is set.
        _graph.Name = path is null
            ? "untitled" + GraphSerializer.FileExtension
            : System.IO.Path.GetFileName(path);

        OnPropertyChanged(nameof(Name));
        IsDirty = false;
    }

    /// <summary>
    /// Follows the run so that unreached nodes can tell the difference between waiting their turn
    /// and never having been reached, and so that a running node counts up.
    /// </summary>
    public void OnRunStateChanged()
    {
        foreach (var node in Nodes)
        {
            node.RefreshRunState();
        }

        var running = _runState() is RunState.Running or RunState.Paused;

        if (running)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            Tick();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _graph.Nodes.CollectionChanged -= OnNodesChanged;
        _graph.Connections.CollectionChanged -= OnConnectionsChanged;

        Clear();
    }

    private void Tick()
    {
        foreach (var node in Nodes)
        {
            node.Tick();
        }
    }

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Rebuild();
        MarkChanged();
    }

    private void OnConnectionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SummaryText));
        MarkChanged();
    }

    /// <summary>
    /// Mirrors the graph. Rebuilding rather than reconciling is right at this size: a graph is
    /// tens of nodes, and a wrapper that outlives the node it wraps is a subscription leak.
    /// </summary>
    private void Rebuild()
    {
        var wanted = _graph.Nodes.ToList();

        foreach (var gone in _byNode.Keys.Except(wanted).ToList())
        {
            _byNode[gone].Dispose();
            _byNode.Remove(gone);
        }

        Nodes.Clear();

        foreach (var node in wanted)
        {
            if (!_byNode.TryGetValue(node, out var wrapper))
            {
                wrapper = new NodeViewModel(node, _runState);
                wrapper.PropertyChanged += OnNodeViewModelChanged;
                wrapper.Node.SettingsChanged += OnNodeSettingsChanged;
                _byNode[node] = wrapper;
            }

            Nodes.Add(wrapper);
        }

        OnPropertyChanged(nameof(SummaryText));
    }

    private void Clear()
    {
        foreach (var wrapper in _byNode.Values)
        {
            wrapper.PropertyChanged -= OnNodeViewModelChanged;
            wrapper.Node.SettingsChanged -= OnNodeSettingsChanged;
            wrapper.Dispose();
        }

        _byNode.Clear();
        Nodes.Clear();
    }

    /// <summary>
    /// A node reporting where it has got to is not an edit. Only the settings and the layout that
    /// the serializer actually writes make a document dirty, which is why running a graph does
    /// not leave a dot on its tab.
    /// </summary>
    private void OnNodeViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NodeViewModel.Detail)
            or nameof(NodeViewModel.DisplayState)
            or nameof(NodeViewModel.StateText)
            or nameof(NodeViewModel.IsRunning)
            or nameof(NodeViewModel.IsSelected)
            or nameof(NodeViewModel.Elapsed)
            or nameof(NodeViewModel.ElapsedText)
            or nameof(NodeViewModel.HasElapsed)
            or nameof(NodeViewModel.Progress)
            or nameof(NodeViewModel.HasProgress)
            or nameof(NodeViewModel.ProgressPercent))
        {
            return;
        }

        MarkChanged();
    }

    /// <summary>
    /// A node said something worth saving has changed.
    /// </summary>
    /// <remarks>
    /// Listened to on the node rather than on its view model, which republishes four properties and
    /// none of them is a setting. Everything anybody sets in the inspector reaches here now, where
    /// none of it did before.
    /// </remarks>
    private void OnNodeSettingsChanged(NodeBase node) => MarkChanged();

    /// <summary>Records that the graph differs from what is on disk.</summary>
    /// <remarks>
    /// Public because a breakpoint is part of the saved graph and is toggled from outside the
    /// bindings this class watches, so nothing else would notice it changed.
    /// </remarks>
    public void MarkChanged() => IsDirty = true;
}
