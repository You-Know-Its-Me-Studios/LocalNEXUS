using System.IO;
using System.Text.Json.Nodes;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// What survives saving a graph and opening it again.
/// </summary>
/// <remarks>
/// A setting that is written and not read back, or read back and not noticed, is the same thing to
/// whoever set it: the graph that had tools yesterday runs with none today and says nothing. The
/// serializer has written node settings since v0.1 and that half was never the problem; what was
/// missing is covered by the last test here, which is that changing a setting makes the graph count
/// as edited at all.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class SettingsRoundTripTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "localnexus-roundtrip", Guid.NewGuid().ToString("N"));

    private readonly TestServices _services = TestServices.Create();

    public SettingsRoundTripTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        _services.Dispose();

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A scratch folder that will not delete is not the test's problem.
        }
    }

    private string PathFor(string name) => Path.Combine(_folder, name + GraphSerializer.FileExtension);

    /// <summary>Saves a graph and loads it into a fresh one, as opening it again does.</summary>
    private GraphModel RoundTrip(GraphModel graph, string name)
    {
        var serializer = new GraphSerializer(_services.Factory);
        var path = PathFor(name);

        serializer.Save(graph, path);

        var reopened = new GraphModel();
        serializer.LoadInto(reopened, path);

        return reopened;
    }

    /// <summary>
    /// Every node type writes what it reads.
    /// </summary>
    /// <remarks>
    /// The audit, done by the machine rather than by eye. Each type is built, saved and loaded, and
    /// what it says about itself afterwards has to match what it said before. A type that drops a
    /// field on the way out or fails to pick it up on the way in fails here, whichever half is
    /// wrong, and a type added later is covered without anybody remembering to add it.
    /// </remarks>
    [Fact]
    public void EveryNodeTypeWritesWhatItReads()
    {
        foreach (var descriptor in NodeFactory.Descriptors)
        {
            var graph = new GraphModel();
            var node = _services.Factory.Create(descriptor.TypeKey);

            node.Title = $"{descriptor.TypeKey} renamed";
            node.X = 123;
            node.Y = 456;

            graph.AddNode(node);

            var reopened = RoundTrip(graph, descriptor.TypeKey);
            var back = Assert.Single(reopened.Nodes);

            Assert.Equal(descriptor.TypeKey, back.TypeKey);
            Assert.Equal($"{descriptor.TypeKey} renamed", back.Title);
            Assert.Equal(123, back.X);
            Assert.Equal(456, back.Y);

            Assert.Equal(
                node.SaveSettings().ToJsonString(),
                back.SaveSettings().ToJsonString());
        }
    }

    /// <summary>
    /// The extension and tool selection survives, which is the one that was being lost.
    /// </summary>
    /// <remarks>
    /// Two observable collections rather than two properties, which is why they were worth pinning
    /// separately: nothing about a list is covered by a test that compares scalar settings.
    /// </remarks>
    [Fact]
    public void TheToolSelectionSurvives()
    {
        var graph = new GraphModel();
        var model = (ModelNode)_services.Factory.Create("Model");

        model.SelectedExtensionIds.Add("studio.anklebreaker.unity-mcp");
        model.SelectedExtensionIds.Add("ai.fission.openspec");
        model.AllowedToolNames.Add("unity_list_instances");
        model.AllowedToolNames.Add("unity_hub_list_editors");
        model.MaxToolCalls = 17;

        graph.AddNode(model);

        var back = (ModelNode)Assert.Single(RoundTrip(graph, "tools").Nodes);

        Assert.Equal(
            new[] { "studio.anklebreaker.unity-mcp", "ai.fission.openspec" },
            back.SelectedExtensionIds);

        Assert.Equal(
            new[] { "unity_list_instances", "unity_hub_list_editors" },
            back.AllowedToolNames);

        Assert.Equal(17, back.MaxToolCalls);
    }

    /// <summary>The rest of a model node's settings survive with it.</summary>
    [Fact]
    public void AModelNodesOwnSettingsSurvive()
    {
        var graph = new GraphModel();
        var model = (ModelNode)_services.Factory.Create("Model");

        model.Provider = ModelProvider.OpenRouter;
        model.OpenRouterModel = "some/model";
        model.Temperature = 0.9d;
        model.MaxTokens = 1234;
        model.ContextSize = 16384;
        model.GpuLayers = 42;
        model.SystemPrompt = "be brief";
        model.StripCodeFences = false;

        graph.AddNode(model);

        var back = (ModelNode)Assert.Single(RoundTrip(graph, "model").Nodes);

        Assert.Equal(ModelProvider.OpenRouter, back.Provider);
        Assert.Equal("some/model", back.OpenRouterModel);
        Assert.Equal(0.9d, back.Temperature);
        Assert.Equal(1234, back.MaxTokens);
        Assert.Equal(16384, back.ContextSize);
        Assert.Equal(42, back.GpuLayers);
        Assert.Equal("be brief", back.SystemPrompt);
        Assert.False(back.StripCodeFences);
    }

    /// <summary>The agent's own two settings survive.</summary>
    [Fact]
    public void AnAgentsSettingsSurvive()
    {
        var graph = new GraphModel();
        var agent = (AgentNode)_services.Factory.Create("Agent");

        agent.MaxTurns = 7;
        agent.SystemPrompt = "do less";

        graph.AddNode(agent);

        var back = (AgentNode)Assert.Single(RoundTrip(graph, "agent").Nodes);

        Assert.Equal(7, back.MaxTurns);
        Assert.Equal("do less", back.SystemPrompt);
    }

    /// <summary>
    /// Changing a setting makes the graph count as edited.
    /// </summary>
    /// <remarks>
    /// This is what was actually broken. The serializer wrote and read everything correctly, and
    /// the document was watching the node's view model, which republishes four properties and none
    /// of them is a setting. So nothing anybody set marked the graph as changed, the tab showed no
    /// dot, and closing it lost the lot.
    /// </remarks>
    [Fact]
    public void ChangingASettingMarksTheGraphEdited()
    {
        var model = (ModelNode)_services.Factory.Create("Model");
        var edited = false;

        model.SettingsChanged += _ => edited = true;

        // Whether a change is noticed, not how many notifications it produces. One setter can
        // republish several properties, and counting them would be pinning an implementation
        // detail rather than the thing that matters.
        bool Noticed(Action change)
        {
            edited = false;
            change();

            return edited;
        }

        Assert.True(Noticed(() => model.Temperature = 0.75d), "temperature");
        Assert.True(Noticed(() => model.MaxToolCalls = 3), "tool call limit");
        Assert.True(Noticed(() => model.SystemPrompt = "be brief"), "system prompt");

        // Lists, which raise a collection change and no property change at all.
        Assert.True(Noticed(() => model.SelectedExtensionIds.Add("something")), "extension selection");
        Assert.True(Noticed(() => model.AllowedToolNames.Add("a_tool")), "tool selection");

        // Moving a node is a change to the file too, and was equally invisible.
        Assert.True(Noticed(() => model.X = 10), "position");
    }

    /// <summary>
    /// A node reporting where it has got to is not an edit.
    /// </summary>
    /// <remarks>
    /// The other half, and the reason this cannot simply mark everything. Running a graph would
    /// otherwise leave a dot on its tab and prompt to save something nobody changed.
    /// </remarks>
    [Fact]
    public void RunningIsNotEditing()
    {
        var model = (ModelNode)_services.Factory.Create("Model");
        var edits = 0;

        model.SettingsChanged += _ => edits++;

        model.State = NodeState.Running;
        model.StatusMessage = "turn 3 of 25";
        model.IsSelected = true;

        Assert.Equal(0, edits);
    }
}
