using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;

namespace LocalNEXUS.App.Services.Execution;

/// <summary>
/// Everything one node needs while it executes: the values on its input pins, the request the
/// user typed, the feed to report into, and the shared services.
/// </summary>
public sealed class NodeExecutionContext
{
    private readonly RunContext _run;

    /// <summary>
    /// Values standing in for what an input pin's wire carries, or null when nothing is overridden.
    /// </summary>
    /// <remarks>
    /// How one item of a list is handed to a node that reads a single value. The node's own code
    /// is unchanged and still asks its pin what arrived; only the answer differs, which is what
    /// lets the same body run once or a hundred times without knowing which is happening.
    /// </remarks>
    private readonly IReadOnlyDictionary<Pin, object?>? _overrides;

    public NodeExecutionContext(NodeBase node, RunContext run, ExecutionServices services)
        : this(node, run, services, null)
    {
    }

    private NodeExecutionContext(
        NodeBase node,
        RunContext run,
        ExecutionServices services,
        IReadOnlyDictionary<Pin, object?>? overrides)
    {
        Node = node;
        _run = run;
        Services = services;
        _overrides = overrides;
    }

    /// <summary>The node being executed.</summary>
    public NodeBase Node { get; }

    /// <summary>The request typed into the chat box before the run started.</summary>
    public string UserRequest => _run.UserRequest;

    /// <summary>This run's identity in the record, or null when nothing is recording.</summary>
    public string? RunId => _run.RunId;

    /// <summary>The live transcript of the run.</summary>
    public IActivityFeed Feed => Services.Feed;

    /// <summary>The services available to nodes.</summary>
    public ExecutionServices Services { get; }

    /// <summary>
    /// Reads the value arriving on an input pin by following its incoming wire back to the
    /// upstream output pin. Returns null when the pin is unconnected or the upstream node
    /// produced nothing.
    /// </summary>
    public object? GetValue(Pin inputPin)
    {
        ArgumentNullException.ThrowIfNull(inputPin);

        // What something handed this node directly wins over what the wire carries. Only a node
        // driving another one item at a time sets these, and it is doing so because the list on
        // the wire is exactly what must not be read whole.
        if (_overrides is not null && _overrides.TryGetValue(inputPin, out var handed))
        {
            return handed;
        }

        var connection = _run.Graph.Connections.FirstOrDefault(c => c.Target == inputPin);
        if (connection is null)
        {
            return null;
        }

        // What the wire was told to carry wins over what the pin produced. That is only ever
        // different when somebody stopped the run on this wire and changed it, and it is per wire
        // so that editing one branch of a fan out leaves the others alone.
        return _run.TryGetWireValue(connection, out var edited)
            ? edited
            : _run.TryGetValue(connection.Source, out var value) ? value : null;
    }

    /// <summary>Reads an input pin as text, yielding an empty string when nothing arrived.</summary>
    public string GetText(Pin inputPin) => GetValue(inputPin)?.ToString() ?? string.Empty;

    /// <summary>True when the given input pin has an incoming wire.</summary>
    public bool IsConnected(Pin inputPin) => _run.Graph.Connections.Any(c => c.Target == inputPin);

    /// <summary>
    /// Files one of this node's judgements on the run, where something other than a person can
    /// read it.
    /// </summary>
    /// <remarks>
    /// Beside the feed rather than instead of it. The feed is what somebody reads while a run
    /// happens and is the right shape for that; this is the same fact in a shape that can be
    /// counted, compared between runs, and asserted on.
    /// </remarks>
    public void Record(RunDecision decision) => _run.Record(decision);

    /// <summary>
    /// The node on the other end of an input pin's wire, or null when the pin is unconnected.
    /// </summary>
    /// <remarks>
    /// A node that needs to ask its upstream neighbour for something, rather than merely read
    /// what it produced, needs to be able to find it. Following the wire is the graph's own
    /// answer to who that is, so nothing here has to know what kind of node either end is.
    /// </remarks>
    public NodeBase? GetSourceNode(Pin inputPin)
    {
        ArgumentNullException.ThrowIfNull(inputPin);

        var connection = _run.Graph.Connections.FirstOrDefault(c => c.Target == inputPin);
        return connection?.Source.Owner;
    }

    /// <summary>
    /// The nodes an output pin feeds, in no particular order.
    /// </summary>
    /// <remarks>
    /// The mirror of <see cref="GetSourceNode"/>. A node that needs a capability none of its
    /// inputs has can look along its own output wire for one, which is how a planner borrows the
    /// model that is going to do the writing. These nodes have not run yet, and do not need to
    /// have: what is being borrowed is their configuration, not their result.
    /// </remarks>
    public IReadOnlyList<NodeBase> GetTargetNodes(Pin outputPin)
    {
        ArgumentNullException.ThrowIfNull(outputPin);

        return _run.Graph.Connections
            .Where(c => c.Source == outputPin)
            .Select(c => c.Target.Owner)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// A context belonging to another node of the same run, so that node can read its own inputs.
    /// </summary>
    /// <remarks>
    /// Used when one node asks another to do more work. The run, its values and its services are
    /// shared; only the node the context is about differs.
    /// </remarks>
    public NodeExecutionContext ForNode(NodeBase node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return ReferenceEquals(node, Node) ? this : new NodeExecutionContext(node, _run, Services, _overrides);
    }

    /// <summary>
    /// A context identical to this one except that one input pin reads the given value.
    /// </summary>
    /// <remarks>
    /// The whole of how iteration works, and the reason nothing about it reaches the executor. A
    /// node asked to run once per item is not told it is being iterated and does not have a second
    /// code path for it: it reads its pin exactly as it always does, and what it gets back is one
    /// item rather than the list.
    /// </remarks>
    public NodeExecutionContext WithValue(Pin inputPin, object? value)
    {
        ArgumentNullException.ThrowIfNull(inputPin);

        if (inputPin.Direction != PinDirection.Input)
        {
            throw new ArgumentException("Only an input pin can be handed a value.", nameof(inputPin));
        }

        var map = _overrides is null
            ? new Dictionary<Pin, object?>()
            : new Dictionary<Pin, object?>(_overrides);

        map[inputPin] = value;

        return new NodeExecutionContext(Node, _run, Services, map);
    }

    /// <summary>
    /// Publishes a value on an output pin, as though the node that owns it had just produced it.
    /// </summary>
    /// <remarks>
    /// For a node that ran another node itself. The executor writes a node's outputs from what its
    /// own execution returned, and a node the executor skipped never returns one, so without this
    /// everything downstream of a driven node would read nothing.
    /// </remarks>
    public void PublishOutput(Pin outputPin, object? value)
    {
        ArgumentNullException.ThrowIfNull(outputPin);

        if (outputPin.Direction != PinDirection.Output)
        {
            throw new ArgumentException("Only an output pin carries a produced value.", nameof(outputPin));
        }

        _run.SetValue(outputPin, value);
    }

    /// <summary>
    /// Holds on each marked wire leaving the given pin, in turn, and records what was released.
    /// </summary>
    /// <remarks>
    /// A breakpoint belongs to a wire rather than to a node, because the point of putting one there
    /// is that two branches out of the same pin can be stopped at differently. Each marked wire is
    /// held separately for that reason.
    ///
    /// Held here rather than only by the executor, because a node that publishes a value several
    /// times has to stop each time. A breakpoint that fired once on a wire a hundred values
    /// travelled down would be a breakpoint on the node in everything but where it was drawn.
    ///
    /// The released value is written back even when nothing was changed, so what the wire carries
    /// is decided in one place rather than depending on whether somebody typed into the box.
    /// </remarks>
    public async Task HoldAtBreakpointsAsync(Pin outputPin, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(outputPin);

        var marked = _run.Graph.Connections.Where(c => c.HasBreakpoint && c.Source == outputPin).ToList();

        foreach (var connection in marked)
        {
            var value = _run.TryGetValue(connection.Source, out var produced) ? produced : null;
            var released = await Services.Breakpoints.HoldAsync(connection, value, ct).ConfigureAwait(false);

            _run.SetWireValue(connection, released);
        }
    }

    /// <summary>
    /// Every node reachable by following wires forward from an output pin, in execution order.
    /// </summary>
    /// <remarks>
    /// For a node that runs its own downstream chain. The order is the run's own topological
    /// order filtered down, rather than a second ordering worked out here, so a chain driven by a
    /// node runs in exactly the order the executor would have run it in.
    /// </remarks>
    public IReadOnlyList<NodeBase> DownstreamOf(Pin outputPin)
    {
        ArgumentNullException.ThrowIfNull(outputPin);

        var reached = GraphTopology.Downstream(_run.Graph, outputPin);
        var sort = GraphTopology.Sort(_run.Graph);

        return sort.Ordered.Where(reached.Contains).ToList();
    }

    /// <summary>True when this node is something the graph reads rather than something it runs.</summary>
    /// <remarks>
    /// Asked by a node running its own chain, so that a model handed to one of the driven nodes is
    /// read as configuration there exactly as it would be in the ordinary pass. Without it a model
    /// wired into a driven node would be executed once per item with nothing on its prompt pin.
    /// </remarks>
    public bool IsReferenceOnly(NodeBase node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return GraphTopology.IsReferenceOnly(_run.Graph, node);
    }
}
