using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// The Agent node says, before a run, when the model wired into it cannot call tools.
/// </summary>
/// <remarks>
/// This is the failure worth testing hardest because it is the one that looks like success. An
/// agent handed a model that cannot emit a tool call does not stop: it narrates the work in prose,
/// the loop sees no calls and concludes it is finished, and the run reports done with nothing
/// written. The warning is the only thing standing in front of that, so these check the three
/// states it distinguishes and that it follows the wire rather than a copy of an answer.
/// </remarks>
public sealed class AgentToolWarningTests
{
    private static (GraphModel Graph, AgentNode Agent, ModelNode Model) Wired(TestServices services)
    {
        var graph = new GraphModel();
        var agent = new AgentNode();
        var model = (ModelNode)services.Factory.Create("Model", 0, 0);

        graph.AddNode(agent);
        graph.AddNode(model);

        Assert.True(graph.TryConnect(model.Self, agent.Model, out var reason), reason);

        return (graph, agent, model);
    }

    [Fact]
    public void WithNoModelWiredItSaysSo()
    {
        var agent = new AgentNode();

        Assert.True(agent.HasToolWarning);
        Assert.False(agent.IsToolWarningSevere);
        Assert.Contains("No model is wired in", agent.ToolWarning, StringComparison.Ordinal);
    }

    [Fact]
    public void AModelThatCannotCallToolsIsASevereWarning()
    {
        using var services = TestServices.Create();
        var (_, agent, model) = Wired(services);

        model.ToolSupport = ToolSupport.Unsupported;

        Assert.True(agent.HasToolWarning);
        Assert.True(agent.IsToolWarningSevere);
        Assert.Contains("does not call tools", agent.ToolWarning, StringComparison.Ordinal);
    }

    /// <summary>Unknown is not No, and must not be reported as one.</summary>
    [Fact]
    public void AModelNobodyHasAskedAboutIsACautionRatherThanARefusal()
    {
        using var services = TestServices.Create();
        var (_, agent, model) = Wired(services);

        model.ToolSupport = ToolSupport.Unknown;

        Assert.True(agent.HasToolWarning);
        Assert.False(agent.IsToolWarningSevere);
        Assert.Contains("has not been established", agent.ToolWarning, StringComparison.Ordinal);
    }

    [Fact]
    public void AModelThatCallsToolsSaysNothing()
    {
        using var services = TestServices.Create();
        var (_, agent, model) = Wired(services);

        model.ToolSupport = ToolSupport.Supported;

        Assert.False(agent.HasToolWarning);
        Assert.False(agent.IsToolWarningSevere);
        Assert.Equal(string.Empty, agent.ToolWarning);
    }

    /// <summary>The warning follows the wired model rather than a value copied once.</summary>
    [Fact]
    public void ChangingTheAnswerOnTheModelChangesTheWarning()
    {
        using var services = TestServices.Create();
        var (_, agent, model) = Wired(services);

        model.ToolSupport = ToolSupport.Supported;
        Assert.False(agent.HasToolWarning);

        model.ToolSupport = ToolSupport.Unsupported;

        Assert.True(agent.IsToolWarningSevere);
    }

    /// <summary>Unwiring the model puts the warning back to having nothing to run at all.</summary>
    [Fact]
    public void RemovingTheWireReturnsToTheNoModelWarning()
    {
        using var services = TestServices.Create();
        var (graph, agent, model) = Wired(services);

        model.ToolSupport = ToolSupport.Unsupported;
        Assert.True(agent.IsToolWarningSevere);

        graph.DisconnectPin(agent.Model);

        Assert.False(agent.IsToolWarningSevere);
        Assert.Contains("No model is wired in", agent.ToolWarning, StringComparison.Ordinal);
    }

    /// <summary>
    /// A pin records what was wired into it, which is what lets a panel ask before a run.
    /// </summary>
    [Fact]
    public void AnInputPinKnowsWhatFeedsIt()
    {
        using var services = TestServices.Create();
        var (graph, agent, model) = Wired(services);

        Assert.Same(model.Self, agent.Model.SourcePin);
        Assert.Same(model, agent.Model.SourcePin!.Owner);

        graph.DisconnectPin(agent.Model);

        Assert.Null(agent.Model.SourcePin);
    }
}
