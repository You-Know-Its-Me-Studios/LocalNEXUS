using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.ViewModels;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// The search that places a node from the canvas.
/// </summary>
/// <remarks>
/// Nothing here names a node type as a thing the search knows about. The list comes from the
/// factory, so a type added to the factory appears without this being touched, and the tests assert
/// against that list rather than against a copy of it.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class NodeSearchTests
{
    /// <summary>A search that records what it was asked to place rather than placing it.</summary>
    private sealed class Placements
    {
        public List<(string TypeKey, double X, double Y, Pin? From)> Placed { get; } = new();

        public NodeSearchViewModel SearchOn(NodeFactory factory)
            => new(factory, (key, x, y, from) => Placed.Add((key, x, y, from)));
    }

    /// <summary>Opening on empty canvas offers every type the factory has.</summary>
    [Fact]
    public void OpeningOnEmptyCanvasOffersEverything()
    {
        using var services = TestServices.Create();

        var search = new Placements().SearchOn(services.Factory);
        search.Open(120, 80);

        Assert.True(search.IsOpen);
        Assert.Equal(services.Factory.AvailableDescriptors().Count, search.Results.Count);

        // And it offers what the factory offers, in the factory's own order.
        Assert.Equal(
            services.Factory.AvailableDescriptors().Select(d => d.TypeKey).ToArray(),
            search.Results.Select(r => r.TypeKey).ToArray());
    }

    /// <summary>Typing narrows the list, by name and by what the node does.</summary>
    [Fact]
    public void TypingNarrowsTheList()
    {
        using var services = TestServices.Create();

        var search = new Placements().SearchOn(services.Factory);
        search.Open(0, 0);

        search.Query = "judge";
        Assert.Equal("Judge", Assert.Single(search.Results).TypeKey);

        // Found by what it does rather than by what it is called, which is the point of searching
        // the description at all.
        search.Query = "compiles the code";
        Assert.Equal("CompilerCheck", Assert.Single(search.Results).TypeKey);
    }

    /// <summary>A name match sorts above a node that merely mentions the word.</summary>
    [Fact]
    public void ANameMatchComesFirst()
    {
        using var services = TestServices.Create();

        var search = new Placements().SearchOn(services.Factory);
        search.Open(0, 0);

        search.Query = "model";

        Assert.NotEmpty(search.Results);
        Assert.Equal("Model", search.Results[0].TypeKey);
    }

    /// <summary>Nothing matching is an empty list rather than the whole list.</summary>
    [Fact]
    public void NothingMatchingIsEmpty()
    {
        using var services = TestServices.Create();

        var search = new Placements().SearchOn(services.Factory);
        search.Open(0, 0);

        search.Query = "kettle";

        Assert.Empty(search.Results);
        Assert.False(search.HasResults);
        Assert.Null(search.Selected);
        Assert.False(search.PlaceCommand.CanExecute(null));
    }

    /// <summary>Enter places the highlighted row where the search was opened.</summary>
    [Fact]
    public void PlacingReportsTheTypeAndThePoint()
    {
        using var services = TestServices.Create();

        var placements = new Placements();
        var search = placements.SearchOn(services.Factory);

        search.Open(240, 130);

        // Named exactly, because two node types are called something output and the point being
        // pinned here is where a node lands rather than which one a partial word finds.
        search.Query = "compiler check";
        search.PlaceCommand.Execute(null);

        var placed = Assert.Single(placements.Placed);

        Assert.Equal("CompilerCheck", placed.TypeKey);
        Assert.Equal(240, placed.X);
        Assert.Equal(130, placed.Y);
        Assert.Null(placed.From);

        // And it closed itself, rather than staying open over the node it just placed.
        Assert.False(search.IsOpen);
    }

    /// <summary>
    /// Dragging from an output offers only types with an input that could take it.
    /// </summary>
    /// <remarks>
    /// Asserted against the same compatibility table the canvas uses rather than against a list of
    /// node names, so this stays true when a node's pins change.
    /// </remarks>
    [Fact]
    public void DraggingFromAPinFiltersByWhatCouldConnect()
    {
        using var services = TestServices.Create();

        var prompt = services.Factory.Create("Prompt");
        var source = prompt.Outputs[0];

        var search = new Placements().SearchOn(services.Factory);
        search.OpenFrom(source, 0, 0);

        Assert.NotEmpty(search.Results);

        foreach (var result in search.Results)
        {
            var candidate = services.Factory.Create(result.TypeKey);

            Assert.Contains(
                candidate.Inputs,
                pin => PinTypeCompatibility.CanFlow(source.PinType, pin.PinType));
        }

        // And nothing that could not take it is offered.
        var offered = search.Results.Select(r => r.TypeKey).ToHashSet(StringComparer.Ordinal);

        foreach (var descriptor in services.Factory.AvailableDescriptors())
        {
            var candidate = services.Factory.Create(descriptor.TypeKey);
            var reachable = candidate.Inputs.Any(pin => PinTypeCompatibility.CanFlow(source.PinType, pin.PinType));

            Assert.Equal(reachable, offered.Contains(descriptor.TypeKey));
        }
    }

    /// <summary>Dragging backwards from an input filters on the other direction.</summary>
    [Fact]
    public void DraggingFromAnInputOffersWhatCouldFeedIt()
    {
        using var services = TestServices.Create();

        var output = services.Factory.Create("Output");
        var target = output.Inputs[0];

        var search = new Placements().SearchOn(services.Factory);
        search.OpenFrom(target, 0, 0);

        Assert.NotEmpty(search.Results);

        foreach (var result in search.Results)
        {
            var candidate = services.Factory.Create(result.TypeKey);

            Assert.Contains(
                candidate.Outputs,
                pin => PinTypeCompatibility.CanFlow(pin.PinType, target.PinType));
        }
    }

    /// <summary>A row from a dragged wire names the pin it would land on.</summary>
    [Fact]
    public void ARowFromADraggedWireNamesThePin()
    {
        using var services = TestServices.Create();

        var prompt = services.Factory.Create("Prompt");

        var search = new Placements().SearchOn(services.Factory);
        search.OpenFrom(prompt.Outputs[0], 0, 0);

        Assert.All(search.Results, r => Assert.False(string.IsNullOrWhiteSpace(r.PinName)));

        // Opened on its own it has no pin to name, and says the description instead.
        search.Open(0, 0);
        Assert.All(search.Results, r => Assert.Null(r.PinName));
    }

    /// <summary>Placing from a dragged wire carries the pin through, so it can be wired back.</summary>
    [Fact]
    public void PlacingFromADraggedWireCarriesThePin()
    {
        using var services = TestServices.Create();

        var placements = new Placements();
        var search = placements.SearchOn(services.Factory);

        var prompt = services.Factory.Create("Prompt");
        var source = prompt.Outputs[0];

        search.OpenFrom(source, 55, 66);
        search.PlaceCommand.Execute(null);

        Assert.Same(source, Assert.Single(placements.Placed).From);
    }

    /// <summary>Closing places nothing and forgets the pin.</summary>
    [Fact]
    public void ClosingPlacesNothing()
    {
        using var services = TestServices.Create();

        var placements = new Placements();
        var search = placements.SearchOn(services.Factory);

        var prompt = services.Factory.Create("Prompt");
        search.OpenFrom(prompt.Outputs[0], 0, 0);

        search.CloseCommand.Execute(null);

        Assert.Empty(placements.Placed);
        Assert.False(search.IsOpen);
        Assert.Null(search.From);
        Assert.Empty(search.Results);
    }

    /// <summary>The arrow keys move the highlight and wrap at both ends.</summary>
    [Fact]
    public void TheArrowKeysWalkTheList()
    {
        using var services = TestServices.Create();

        var search = new Placements().SearchOn(services.Factory);
        search.Open(0, 0);

        var first = search.Results[0];
        var last = search.Results[^1];

        Assert.Equal(first, search.Selected);

        search.SelectPreviousCommand.Execute(null);
        Assert.Equal(last, search.Selected);

        search.SelectNextCommand.Execute(null);
        Assert.Equal(first, search.Selected);
    }
}
