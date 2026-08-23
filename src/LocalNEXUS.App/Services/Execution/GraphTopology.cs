using LocalNEXUS.App.Models;

namespace LocalNEXUS.App.Services.Execution;

/// <summary>
/// Orders the nodes of a graph so that every node runs after the nodes that feed it.
/// </summary>
/// <remarks>
/// Kahn's algorithm. Nodes are dequeued in the order they appear on the canvas, so a graph with
/// several independent branches produces the same order on every run, which makes the activity
/// feed reproducible.
/// </remarks>
public static class GraphTopology
{
    /// <summary>The result of ordering a graph.</summary>
    /// <param name="Ordered">Nodes in execution order. Empty when a cycle was found.</param>
    /// <param name="Cycle">The nodes that could not be ordered because they form or feed a cycle.</param>
    public readonly record struct SortResult(IReadOnlyList<NodeBase> Ordered, IReadOnlyList<NodeBase> Cycle)
    {
        /// <summary>True when every node could be ordered.</summary>
        public bool IsAcyclic => Cycle.Count == 0;
    }

    /// <summary>Produces an execution order for the graph, or reports the nodes involved in a cycle.</summary>
    /// <summary>
    /// True when everything this node is wired to wants it rather than what it produces.
    /// </summary>
    /// <remarks>
    /// The other half of the rule the sort below already follows, and the half that was missed. A
    /// model wire carries a reference to a configured model, so the consumer needs the model to
    /// exist rather than to have run. The sort stopped counting those as dependencies in v1.16,
    /// which fixed the ordering, and the node was still executed anyway. A model handed to a debate
    /// has nothing on its own prompt pin, because being handed over is the whole of its job, so it
    /// threw and the run stopped before a word was exchanged.
    ///
    /// The question is about outgoing use, not about the node. A model wired to a debate on its
    /// Model pin and to a file writer on its Code pin is both a configuration and a step, and it
    /// runs, because something downstream is waiting on what it says. Only a node whose every
    /// outgoing wire is a reference is not a step.
    ///
    /// A node wired to nothing at all is left alone and still runs. Whether an unconnected node
    /// should execute is a different question with a different answer, and it is not this one.
    ///
    /// Like the sort, this reasons about a pin type and never about a node type. Nothing here can
    /// tell a model node from anything else, and the executor above it still cannot either.
    /// </remarks>
    public static bool IsReferenceOnly(GraphModel graph, NodeBase node)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);

        var wired = false;

        foreach (var connection in graph.Connections)
        {
            if (connection.Source.Owner != node)
            {
                continue;
            }

            if (connection.Source.PinType != PinType.Model)
            {
                return false;
            }

            wired = true;
        }

        return wired;
    }

    /// <summary>
    /// Every node reachable by following wires forward from an output pin.
    /// </summary>
    /// <remarks>
    /// The whole reachable closure rather than the immediate neighbours, because a node fed by a
    /// driven node is itself driven. Anything short of the closure leaves a node reading the value
    /// the last iteration happened to leave on its input and running once against it.
    ///
    /// Model wires are followed like any other. A model handed to a driven node is read as
    /// configuration by <see cref="IsReferenceOnly"/> and never executed, so including it here
    /// costs nothing and excluding it would depend on the two rules agreeing forever.
    /// </remarks>
    public static IReadOnlyCollection<NodeBase> Downstream(GraphModel graph, Pin outputPin)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(outputPin);

        var reached = new HashSet<NodeBase>();
        var pending = new Queue<NodeBase>();

        foreach (var connection in graph.Connections.Where(c => c.Source == outputPin))
        {
            if (reached.Add(connection.Target.Owner))
            {
                pending.Enqueue(connection.Target.Owner);
            }
        }

        while (pending.Count > 0)
        {
            var node = pending.Dequeue();

            foreach (var connection in graph.Connections.Where(c => c.Source.Owner == node))
            {
                if (reached.Add(connection.Target.Owner))
                {
                    pending.Enqueue(connection.Target.Owner);
                }
            }
        }

        // A driver wired back into itself is a cycle, which the sort refuses before any of this
        // runs. Discarding it here as well means this answers sensibly whatever it is handed.
        reached.Remove(outputPin.Owner);

        return reached;
    }

    /// <summary>
    /// True when another node runs this one itself, so the ordinary pass must leave it alone.
    /// </summary>
    /// <remarks>
    /// The same shape as <see cref="IsReferenceOnly"/>: a question about the graph, answered by
    /// asking a capability rather than by recognising a node type. Nothing here can tell a loop
    /// from anything else, and the executor above it still cannot either.
    /// </remarks>
    public static bool IsDrivenByAnother(GraphModel graph, NodeBase node)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);

        foreach (var driver in graph.Nodes.OfType<IIterationDriver>())
        {
            if (!ReferenceEquals(driver, node) && Downstream(graph, driver.IterationOutput).Contains(node))
            {
                return true;
            }
        }

        return false;
    }

    public static SortResult Sort(GraphModel graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var nodes = graph.Nodes.ToList();

        // Several wires can join the same pair of nodes through different pins. Collapse them so
        // that one dependency is not counted twice.
        //
        // A model wire is not one of them. It carries a reference to a configured model rather
        // than a value that model produced, so the consumer needs the model to exist, not to have
        // run. Counting it as a dependency would make the ordinary planning graph a cycle and
        // refuse to run it: a triage node feeds its plan to a model, and that same model is the
        // one triage plans with, so the two wires point in opposite directions between the same
        // pair of nodes. They are not a loop, because only one of them is a flow.
        //
        // This reasons about a pin type, never a node type. It is the same table
        // PinTypeCompatibility consults, and the executor above it still knows nothing about what
        // any node is or does.
        var edges = graph.Connections
            .Where(c => c.Source.PinType != PinType.Model)
            .Select(c => (From: c.Source.Owner, To: c.Target.Owner))
            .Where(e => e.From != e.To)
            .Distinct()
            .ToList();

        var dependents = nodes.ToDictionary(n => n, _ => new List<NodeBase>());
        var indegree = nodes.ToDictionary(n => n, _ => 0);

        foreach (var (from, to) in edges)
        {
            if (!dependents.ContainsKey(from) || !indegree.ContainsKey(to))
            {
                continue;
            }

            dependents[from].Add(to);
            indegree[to]++;
        }

        var ready = new Queue<NodeBase>(nodes.Where(n => indegree[n] == 0));
        var ordered = new List<NodeBase>(nodes.Count);

        while (ready.Count > 0)
        {
            var node = ready.Dequeue();
            ordered.Add(node);

            foreach (var dependent in dependents[node])
            {
                if (--indegree[dependent] == 0)
                {
                    ready.Enqueue(dependent);
                }
            }
        }

        if (ordered.Count == nodes.Count)
        {
            return new SortResult(ordered, Array.Empty<NodeBase>());
        }

        var stuck = nodes.Where(n => indegree[n] > 0).ToList();
        return new SortResult(Array.Empty<NodeBase>(), stuck);
    }
}
