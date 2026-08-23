using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;

namespace LocalNEXUS.App.Nodes;

/// <summary>
/// Runs everything downstream of it once for each item in a list, visibly.
/// </summary>
/// <remarks>
/// Fanning out already happens without this node: a list arriving at any node is worked through
/// one entry at a time, and what comes back is a list. What it does not do is show itself. The run
/// goes quiet, the node says it is running, and there is no way to tell whether it is on item two
/// of forty or has stopped, and no way to look at what is about to be processed before it is.
///
/// So this is the same iteration made into a thing on the canvas. The chain hanging off its Item
/// pin is run once per entry, the node says which entry that is and how many are left, and a
/// breakpoint on the wire out of it stops before every single one rather than once. That last part
/// is the reason to reach for it: stopping between items is not expressible any other way.
///
/// It drives its own neighbours rather than the executor doing it, and the executor is not told
/// that a loop exists. It advertises <see cref="IIterationDriver"/>, and the executor asks two
/// questions of that capability: leave alone anything a driver runs, and do not hold a second time
/// on wires a driver has already held on. Both are the shape every capability here has taken since
/// code repair, and neither names a node type.
///
/// What it deliberately does not do is gather results. Its chain ends where it ends: a file writer
/// writes each file, a text output shows each answer as it arrives. There is no honest way to pick
/// which node in a chain of unknown shape produced the answer worth collecting, and guessing would
/// be wrong in exactly the graphs somebody built a loop for. Wiring the list straight into a node
/// instead of through here is what returns a list, and that is the choice: a value back, or the
/// iteration in front of you.
/// </remarks>
public sealed partial class LoopNode : NodeBase, IIterationDriver
{
    /// <summary>How much of an item the node itself shows.</summary>
    /// <remarks>
    /// Enough to recognise which one is being worked on. The canvas is a diagram of the run and a
    /// node that grows to hold a file stops being one.
    /// </remarks>
    public const int PreviewLength = 60;

    public LoopNode()
        : base("Loop")
    {
        Items = AddInput("Text", PinType.Text);
        Item = AddOutput("Item", PinType.Text);
    }

    /// <inheritdoc />
    public override string TypeKey => "Loop";

    /// <summary>The list to work through.</summary>
    /// <remarks>
    /// Anything that is not a list is one item, rather than an error. A graph built around a loop
    /// still has to run on the day the thing upstream produced a single answer, and refusing would
    /// make the loop a thing to be removed and put back.
    /// </remarks>
    public Pin Items { get; }

    /// <summary>The item being worked on, which is what the driven chain reads.</summary>
    public Pin Item { get; }

    /// <inheritdoc />
    public Pin IterationOutput => Item;

    /// <summary>How many items there are, or zero before anything has run.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Remaining))]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private int _total;

    /// <summary>Which item is being worked on, counting from one, or zero before the first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Remaining))]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private int _position;

    /// <summary>The start of the item being worked on.</summary>
    [ObservableProperty]
    private string _currentItem = string.Empty;

    /// <summary>How many are left after the one in progress.</summary>
    public int Remaining => Math.Max(0, Total - Position);

    /// <summary>Where it has got to, in the words the node and the inspector both use.</summary>
    public string ProgressText => Total == 0
        ? "nothing has run yet"
        : Position == 0
            ? $"{Total} item(s) to work through"
            : $"item {Position} of {Total}, {Remaining} to go";

    /// <inheritdoc />
    public override async Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var arrived = ctx.GetValue(Items);

        var items = FanOut.TryItems(arrived, out var listed)
            ? listed
            : new[] { arrived };

        // Everything reachable from the Item pin, in the order the executor would have run it in.
        // Read once: a chain cannot change shape part way through a run, and asking per item would
        // be the same answer worked out again for every entry.
        var chain = ctx.DownstreamOf(Item);

        Total = items.Count;
        Position = 0;
        CurrentItem = string.Empty;

        if (chain.Count == 0)
        {
            // Not a failure. A loop with nothing wired to it is a graph half built, and saying so
            // is more use than iterating nothing successfully.
            StatusMessage = $"{items.Count} item(s), but nothing is wired to the Item pin";
            ctx.Feed.Add(
                ActivityKind.Info,
                $"{Title} has nothing to run",
                "Wire the Item pin into whatever should happen for each item.",
                Id);

            return NodeResult.FromPin(Item, null);
        }

        ctx.Feed.Info(
            $"{Title}: {items.Count} item(s) over {chain.Count} node(s)",
            string.Join(", ", chain.Select(n => n.Title)));

        for (var index = 0; index < items.Count; index++)
        {
            ct.ThrowIfCancellationRequested();

            Position = index + 1;
            CurrentItem = Preview(items[index]);
            StatusMessage = ProgressText;

            // Published before the hold, so what a breakpoint shows is this item rather than the
            // last one, and so an edit made there is what the chain goes on to read.
            ctx.PublishOutput(Item, items[index]);

            await ctx.HoldAtBreakpointsAsync(Item, ct).ConfigureAwait(false);

            ctx.Feed.Add(
                ActivityKind.Info,
                $"{Title}: item {Position} of {Total}",
                CurrentItem,
                Id);

            await RunChainAsync(ctx, chain, ct).ConfigureAwait(false);
        }

        StatusMessage = items.Count == 0
            ? "nothing to work through"
            : $"{items.Count} item(s) done";

        // The last item, so anything reading this pin outside the chain sees a value rather than
        // nothing. Which of a hundred is a poor answer, and it is the only one available; the
        // reason to wire something to the Item pin instead is that it sees all of them.
        return NodeResult.FromPin(Item, items.Count == 0 ? null : items[^1]);
    }

    /// <summary>
    /// Runs the driven chain once, in order, publishing what each node produced.
    /// </summary>
    /// <remarks>
    /// The same three steps the executor takes for a node, because the nodes cannot tell the
    /// difference and must not be able to: state, execute, publish, hold. A node that faults is
    /// marked where it faulted and the exception is left to travel, so the run stops exactly as it
    /// would have and the failure sits on the node that caused it rather than on this one.
    /// </remarks>
    private async Task RunChainAsync(
        NodeExecutionContext ctx,
        IReadOnlyList<NodeBase> chain,
        CancellationToken ct)
    {
        foreach (var node in chain)
        {
            ct.ThrowIfCancellationRequested();

            // A model handed to one of these is configuration here exactly as it is anywhere else.
            // Executing it would run a model node with nothing on its prompt pin, once per item.
            if (ctx.IsReferenceOnly(node))
            {
                node.StatusMessage = "Read as configuration by another node. Nothing ran here.";
                continue;
            }

            var context = ctx.ForNode(node);

            node.State = NodeState.Running;
            node.StatusMessage = null;
            ctx.Feed.Add(ActivityKind.NodeStarted, node.Title, null, node.Id);

            NodeResult result;
            try
            {
                result = await node.ExecuteAsync(context, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                node.State = NodeState.Faulted;
                node.StatusMessage = "Cancelled";
                throw;
            }
            catch (Exception ex)
            {
                node.State = NodeState.Faulted;
                node.StatusMessage = ex.Message;
                ctx.Feed.Add(ActivityKind.NodeFaulted, node.Title, ex.Message, node.Id);

                throw new InvalidOperationException(
                    $"{node.Title} failed on item {Position} of {Total}: {ex.Message}", ex);
            }

            foreach (var pin in node.Outputs)
            {
                if (result.Outputs.TryGetValue(pin.Id, out var value))
                {
                    context.PublishOutput(pin, value);
                }
            }

            node.State = NodeState.Completed;
            ctx.Feed.Add(ActivityKind.NodeCompleted, node.Title, node.StatusMessage, node.Id);

            foreach (var pin in node.Outputs)
            {
                await context.HoldAtBreakpointsAsync(pin, ct).ConfigureAwait(false);
            }
        }
    }

    private static string Preview(object? item)
    {
        var flat = (item?.ToString() ?? string.Empty).ReplaceLineEndings(" ").Trim();

        if (flat.Length == 0)
        {
            return "(empty)";
        }

        return flat.Length <= PreviewLength ? flat : flat[..PreviewLength] + "...";
    }

    /// <inheritdoc />
    /// <remarks>Nothing to save. Where it got to is this run's progress, not how it is configured.</remarks>
    public override JsonObject SaveSettings() => new();

    /// <inheritdoc />
    public override void LoadSettings(JsonObject settings)
    {
    }
}
