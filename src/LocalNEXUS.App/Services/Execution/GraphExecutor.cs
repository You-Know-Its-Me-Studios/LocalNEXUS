using System.Diagnostics;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;

// System.Diagnostics also defines an ActivityKind; this file needs Stopwatch from that namespace
// and the feed's own entry classification, so the ambiguity is resolved explicitly.
using ActivityKind = LocalNEXUS.App.Infrastructure.ActivityKind;

namespace LocalNEXUS.App.Services.Execution;

/// <summary>
/// Walks a graph and executes its nodes in dependency order.
/// </summary>
/// <remarks>
/// The executor knows nothing about what any particular node does. It orders nodes, hands each
/// one the values arriving on its inputs, stores what comes back, and reports the transitions to
/// the feed. Adding a node type therefore requires no change here.
///
/// It does know two things about the shape of a graph, and both are read from the graph rather
/// than from what a node is: that a cycle cannot be ordered, and that a wire carrying
/// configuration is not a dependency. Breakpoints are the third of the same kind. A wire can be
/// marked, and a marked wire holds the run when a value reaches it. That is a property of a
/// connection, which this already handles, and it stays true whatever is at either end.
/// </remarks>
public sealed class GraphExecutor
{
    private readonly ExecutionServices _services;
    private readonly IActivityFeed _feed;

    private int _isRunning;

    public GraphExecutor(ExecutionServices services)
    {
        _services = services;
        _feed = services.Feed;
    }

    /// <summary>
    /// Raised as soon as a run context exists, before the first node executes. The UI subscribes
    /// so that pause, cancel and state display work while the run is still in progress rather
    /// than only once <see cref="RunAsync"/> has returned.
    /// </summary>
    public event EventHandler<RunContext>? RunCreated;

    /// <summary>The run in progress, or the most recent one. Null before the first run.</summary>
    public RunContext? Current { get; private set; }

    /// <summary>True while a run is in progress. The slice permits one run at a time.</summary>
    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

    /// <summary>
    /// Executes every node of the graph in topological order.
    /// </summary>
    /// <param name="graph">The graph to run.</param>
    /// <param name="userRequest">The text typed into the chat box, delivered to input nodes.</param>
    /// <param name="ct">Stops the run between nodes and cancels the node currently executing.</param>
    /// <returns>The run context, whose <see cref="RunContext.State"/> reports the outcome.</returns>
    /// <exception cref="InvalidOperationException">A run is already in progress.</exception>
    public async Task<RunContext> RunAsync(GraphModel graph, string userRequest, CancellationToken ct, string? runId = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) == 1)
        {
            throw new InvalidOperationException("A run is already in progress.");
        }

        var run = new RunContext(graph, userRequest ?? string.Empty, runId);
        Current = run;
        RunCreated?.Invoke(this, run);

        try
        {
            return await ExecuteAsync(run, ct).ConfigureAwait(false);
        }
        finally
        {
            run.CurrentNode = null;
            run.ReleasePauseGate();
            _services.Breakpoints.ReleaseAll();
            Volatile.Write(ref _isRunning, 0);
        }
    }

    private async Task<RunContext> ExecuteAsync(RunContext run, CancellationToken ct)
    {
        foreach (var node in run.Graph.Nodes)
        {
            node.ResetState();
        }

        run.State = RunState.Running;
        _feed.Add(ActivityKind.RunStarted, "Run started", $"{run.Graph.Nodes.Count} nodes, {run.Graph.Connections.Count} connections");

        var sort = GraphTopology.Sort(run.Graph);
        if (!sort.IsAcyclic)
        {
            var names = string.Join(", ", sort.Cycle.Select(n => n.Title));
            return Fault(run, "The graph contains a cycle", $"These nodes form or feed a loop: {names}");
        }

        if (sort.Ordered.Count == 0)
        {
            return Fault(run, "Nothing to run", "The canvas is empty. Add nodes and wire them together first.");
        }

        var stopwatch = Stopwatch.StartNew();

        foreach (var node in sort.Ordered)
        {
            // Something the graph reads rather than something it does. Asked of the topology,
            // which answers from pin types, so this still knows nothing about what any node is.
            if (GraphTopology.IsReferenceOnly(run.Graph, node))
            {
                node.StatusMessage = "Read as configuration by another node. Nothing ran here.";
                continue;
            }

            // The other node runs this one, once per item, so running it again here would run it a
            // final time against whatever the last iteration left behind. Asked of the topology,
            // which answers by asking a capability, so this still knows nothing about what any
            // node is.
            if (GraphTopology.IsDrivenByAnother(run.Graph, node))
            {
                continue;
            }

            try
            {
                await run.WaitWhilePausedAsync(ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                await ExecuteNodeAsync(node, run, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                node.State = NodeState.Faulted;
                node.StatusMessage = "Cancelled";
                return Fault(run, "Run cancelled", $"Stopped while running {node.Title}.");
            }
            catch (Exception ex)
            {
                node.State = NodeState.Faulted;
                node.StatusMessage = ex.Message;
                _feed.Add(ActivityKind.NodeFaulted, node.Title, ex.Message, node.Id);
                return Fault(run, "Run faulted", $"{node.Title} failed: {ex.Message}");
            }
        }

        stopwatch.Stop();

        // The one thing this asks about what a run produced, and it asks a service rather than
        // looking for a node type. A run that left files waiting has not failed and has not
        // finished, and the difference is worth a word of its own.
        var outstanding = _services.Staging.HasPending;

        run.State = outstanding ? RunState.Unresolved : RunState.Completed;

        _feed.Add(
            outstanding ? ActivityKind.Confirmation : ActivityKind.RunCompleted,
            outstanding ? "Run finished with work left over" : "Run completed",
            outstanding
                ? $"{sort.Ordered.Count} nodes in {stopwatch.Elapsed.TotalSeconds:0.0} s. {_services.Staging.Summary}."
                : $"{sort.Ordered.Count} nodes in {stopwatch.Elapsed.TotalSeconds:0.0} s");

        return run;
    }

    private async Task ExecuteNodeAsync(NodeBase node, RunContext run, CancellationToken ct)
    {
        run.CurrentNode = node;
        node.State = NodeState.Running;
        node.StatusMessage = null;
        _feed.Add(ActivityKind.NodeStarted, node.Title, null, node.Id);

        var context = new NodeExecutionContext(node, run, _services);
        var result = await node.ExecuteAsync(context, ct).ConfigureAwait(false);

        foreach (var pin in node.Outputs)
        {
            if (result.Outputs.TryGetValue(pin.Id, out var value))
            {
                run.SetValue(pin, value);
            }
        }

        node.State = NodeState.Completed;
        _feed.Add(ActivityKind.NodeCompleted, node.Title, node.StatusMessage, node.Id);

        // A node that ran its own downstream chain already held on its own wires, once for each
        // item that travelled down them, which is the only place between items exists. Holding
        // again here would stop a second time on a wire nothing is about to travel down.
        if (node is not IIterationDriver)
        {
            await HoldAtBreakpointsAsync(node, context, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Holds on each marked wire leaving this node, in turn.
    /// </summary>
    /// <remarks>
    /// After the node rather than before the next one, and that is the difference between showing
    /// a value and showing a value in the place it came from. Held here, the wire has exactly one
    /// producer and the thing on it has been produced and not yet read, so it can be changed with
    /// nothing to undo.
    ///
    /// The holding itself belongs to the context, because a node that publishes a value more than
    /// once has to stop each time and this is only reached once. Nothing about which wires or in
    /// what order differs between the two callers, so there is one implementation.
    /// </remarks>
    private static async Task HoldAtBreakpointsAsync(NodeBase node, NodeExecutionContext context, CancellationToken ct)
    {
        foreach (var pin in node.Outputs)
        {
            await context.HoldAtBreakpointsAsync(pin, ct).ConfigureAwait(false);
        }
    }

    private RunContext Fault(RunContext run, string title, string detail)
    {
        run.State = RunState.Faulted;
        run.FaultMessage = detail;
        _feed.Add(ActivityKind.RunFaulted, title, detail);
        return run;
    }
}
