using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// What the Agent node says a run could cost before anybody starts one.
/// </summary>
/// <remarks>
/// The number exists because a loop is not priced like a call. Every turn resends the
/// conversation, so each turn's output is paid for again on every turn after it, and the input
/// side grows with the square of the turn count rather than with the turn count. Twenty five
/// turns of a model allowed four thousand tokens is a genuinely expensive accident, and the
/// figure is only useful if that compounding is actually in it.
/// </remarks>
public sealed class AgentCostCeilingTests
{
    private static (AgentNode Agent, ModelNode Model) Wired(TestServices services, string providerId)
    {
        var graph = new GraphModel();
        var agent = new AgentNode();
        var model = (ModelNode)services.Factory.Create("Model", 0, 0);

        graph.AddNode(agent);
        graph.AddNode(model);
        Assert.True(graph.TryConnect(model.Self, agent.Model, out var reason), reason);

        model.Provider = ModelProvider.Cloud;
        model.CloudProviderId = providerId;

        return (agent, model);
    }

    /// <summary>The first paid provider this build knows, so the test does not hard code rates.</summary>
    private static CloudProvider PaidProvider()
        => ProviderCatalog.All.First(p => RunCost.HasRates(p));

    [Fact]
    public void ALocalModelHasNoMoneyCeiling()
    {
        using var services = TestServices.Create();
        var graph = new GraphModel();
        var agent = new AgentNode();
        var model = (ModelNode)services.Factory.Create("Model", 0, 0);

        graph.AddNode(agent);
        graph.AddNode(model);
        Assert.True(graph.TryConnect(model.Self, agent.Model, out _));

        Assert.Null(agent.CostCeiling);
        Assert.Contains("costs time rather than money", agent.CostCeilingText, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNoModelWiredThereIsNothingToSay()
    {
        var agent = new AgentNode();

        Assert.Null(agent.CostCeiling);
        Assert.False(agent.HasCostCeiling);
    }

    [Fact]
    public void APaidModelGetsAFigure()
    {
        using var services = TestServices.Create();
        var (agent, _) = Wired(services, PaidProvider().Id);

        Assert.NotNull(agent.CostCeiling);
        Assert.True(agent.CostCeiling > 0m);
        Assert.Contains("Up to about", agent.CostCeilingText, StringComparison.Ordinal);
    }

    /// <summary>More turns cost more, which is the least the figure has to get right.</summary>
    [Fact]
    public void MoreTurnsCostMore()
    {
        using var services = TestServices.Create();
        var (agent, _) = Wired(services, PaidProvider().Id);

        agent.MaxTurns = 5;
        var few = agent.CostCeiling;

        agent.MaxTurns = 25;
        var many = agent.CostCeiling;

        Assert.True(many > few, $"25 turns ({many}) should cost more than 5 ({few})");
    }

    /// <summary>
    /// The cost grows faster than the turn count, because every turn resends what came before.
    /// </summary>
    /// <remarks>
    /// This is the property that makes the warning worth having. If the figure were merely linear
    /// somebody could reason about it from the turn count alone and would not need to be told.
    /// Five times the turns is more than five times the cost, and this asserts exactly that.
    /// </remarks>
    [Fact]
    public void TheCostGrowsFasterThanTheTurnCount()
    {
        using var services = TestServices.Create();
        var (agent, _) = Wired(services, PaidProvider().Id);

        agent.MaxTurns = 5;
        var few = agent.CostCeiling!.Value;

        agent.MaxTurns = 25;
        var many = agent.CostCeiling!.Value;

        Assert.True(many > few * 5m,
            $"5 turns cost {few} and 25 cost {many}, which is linear or better. The conversation "
            + "resent every turn is not being priced.");
    }

    /// <summary>A bigger output allowance costs more, on both sides of the ledger.</summary>
    [Fact]
    public void ALargerTokenAllowanceCostsMore()
    {
        using var services = TestServices.Create();
        var (agent, model) = Wired(services, PaidProvider().Id);

        model.MaxTokens = 1024;
        var small = agent.CostCeiling;

        model.MaxTokens = 8192;
        var large = agent.CostCeiling;

        Assert.True(large > small, $"8192 tokens ({large}) should cost more than 1024 ({small})");
    }

    /// <summary>The figure follows the wired model rather than a value read once.</summary>
    [Fact]
    public void ChangingTheModelChangesTheFigure()
    {
        using var services = TestServices.Create();
        var (agent, model) = Wired(services, PaidProvider().Id);

        Assert.NotNull(agent.CostCeiling);

        model.Provider = ModelProvider.Local;

        Assert.Null(agent.CostCeiling);
    }

    /// <summary>
    /// What it cannot know is said, because a ceiling that reads as complete is worse than none.
    /// </summary>
    [Fact]
    public void ItSaysWhatItCannotPrice()
    {
        using var services = TestServices.Create();
        var (agent, _) = Wired(services, PaidProvider().Id);

        Assert.Contains("cannot price what the tools return", agent.CostCeilingText, StringComparison.Ordinal);
        Assert.Contains("stop well short", agent.CostCeilingText, StringComparison.Ordinal);
    }
}
