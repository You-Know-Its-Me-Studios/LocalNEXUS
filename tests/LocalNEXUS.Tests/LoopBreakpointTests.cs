using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// A breakpoint on the wire out of a Loop, which stops between items rather than once.
/// </summary>
/// <remarks>
/// The pairing the two features were built for, and the only part of either that nothing checked.
/// The Loop's own description promises it in the node picker, and the difference between stopping
/// once and stopping per item is the whole reason somebody would put a breakpoint there: watching
/// item two of forty go past is the thing that cannot be done any other way.
///
/// It works because of where the hold sits rather than because anything knows about loops. The
/// Loop publishes each item and then holds, inside the iteration, so the executor's own hold has
/// already happened and this one happens again for every entry. Nothing in the executor is
/// involved, which is what these are really pinning down.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class LoopBreakpointTests
{
    /// <summary>A Loop over three items, with a recording node hanging off its Item pin.</summary>
    private static (GraphModel Graph, LoopNode Loop, RecordingNode Body, Connection Wire) Build(
        TestServices services,
        params string[] items)
    {
        var graph = new GraphModel();

        var source = new RecordingNode("source", new List<string>())
        {
            Produce = items.ToList()
        };

        var loop = (LoopNode)services.Factory.Create("Loop", 0, 0);
        var body = new RecordingNode("body", new List<string>());

        graph.AddNode(source);
        graph.AddNode(loop);
        graph.AddNode(body);

        Assert.True(graph.TryConnect(source.Out, loop.Items, out var first), first);
        Assert.True(graph.TryConnect(loop.Item, body.In, out var second), second);

        return (graph, loop, body, graph.Connections[^1]);
    }

    /// <summary>Without a breakpoint the loop runs straight through every item.</summary>
    [Fact]
    public async Task WithoutABreakpointItDoesNotStop()
    {
        using var services = TestServices.Create();
        var (graph, _, body, _) = Build(services, "one", "two", "three");

        var run = await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.Equal(RunState.Completed, run.State);
        Assert.False(services.Services.Breakpoints.IsHolding);
        Assert.Equal(3, body.Runs);
    }

    /// <summary>
    /// A breakpoint on the Item wire holds once per item, not once for the whole loop.
    /// </summary>
    [Fact]
    public async Task ItHoldsOncePerItem()
    {
        using var services = TestServices.Create();
        var (graph, _, body, wire) = Build(services, "one", "two", "three");

        wire.HasBreakpoint = true;

        var held = new List<string>();

        services.Services.Breakpoints.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(BreakpointService.Current)
                || services.Services.Breakpoints.Current is not { } stop)
            {
                return;
            }

            held.Add(stop.Text);
            stop.ContinueCommand.Execute(null);
        };

        var run = await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.Equal(RunState.Completed, run.State);
        Assert.Equal(new[] { "one", "two", "three" }, held);
        Assert.Equal(3, body.Runs);
    }

    /// <summary>
    /// What is edited at the stop is what that iteration goes on to work with.
    /// </summary>
    /// <remarks>
    /// The Loop publishes the item before holding, so an edit made at the breakpoint replaces the
    /// value the chain then reads. Editing item two must not disturb one or three.
    /// </remarks>
    [Fact]
    public async Task AnEditAtTheStopIsWhatThatIterationRunsWith()
    {
        using var services = TestServices.Create();
        var (graph, _, body, wire) = Build(services, "one", "two", "three");

        wire.HasBreakpoint = true;

        services.Services.Breakpoints.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(BreakpointService.Current)
                || services.Services.Breakpoints.Current is not { } stop)
            {
                return;
            }

            if (stop.Text == "two")
            {
                stop.Text = "replaced";
            }

            stop.ContinueCommand.Execute(null);
        };

        await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.Equal(new[] { "one", "replaced", "three" }, body.Seen);
    }

    /// <summary>Stopping the run while held between items unwinds rather than hanging.</summary>
    [Fact]
    public async Task StoppingWhileHeldBetweenItemsUnwinds()
    {
        using var services = TestServices.Create();
        var (graph, _, body, wire) = Build(services, "one", "two", "three");

        wire.HasBreakpoint = true;

        using var stopping = new CancellationTokenSource();

        services.Services.Breakpoints.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(BreakpointService.Current)
                || services.Services.Breakpoints.Current is null)
            {
                return;
            }

            // Cancel while the first item is held, which is what pressing Stop does.
            stopping.Cancel();
        };

        var run = await new GraphExecutor(services.Services).RunAsync(graph, "go", stopping.Token);

        Assert.NotEqual(RunState.Completed, run.State);
        Assert.False(services.Services.Breakpoints.IsHolding);
        Assert.True(body.Runs < 3, $"the loop ran {body.Runs} times after being stopped on the first");
    }
}
