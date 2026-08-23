using System.IO;
using System.Text.Json.Nodes;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Agent;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// A model, a set of tools, and a loop deciding what to do next.
/// </summary>
/// <remarks>
/// What is worth pinning is the loop rather than the tools: that a tool result comes back and the
/// conversation carries on, that a failure is a result the model reads rather than the end of the
/// run, that the cap actually stops it, and that stopping part way leaves what it already did
/// alone. The model is scripted, because a model is the one part that cannot answer the same way
/// twice.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class AgentTests
{
    /// <summary>A model on a Model pin that answers from a script and records what it was given.</summary>
    private sealed class ScriptedAgentModel : NodeBase, IToolCallingModel
    {
        private readonly Queue<ChatCompletionResult> _replies = new();

        private ChatCompletionResult? _always;

        public ScriptedAgentModel()
            : base("scripted")
            => Self = AddOutput("Model", PinType.Model);

        public override string TypeKey => "TestScriptedAgentModel";

        public Pin Self { get; }

        /// <summary>Every message list it was handed, so a test can read what the loop said.</summary>
        public List<IReadOnlyList<ChatMessage>> Turns { get; } = new();

        /// <summary>How many times it was asked to continue.</summary>
        public int Calls { get; private set; }

        /// <summary>What it was offered on the last turn.</summary>
        public IReadOnlyList<ToolDefinition> Offered { get; private set; } = Array.Empty<ToolDefinition>();

        public string ModelName => "scripted";

        public ScriptedAgentModel Asks(string tool, string argumentsJson)
        {
            _replies.Enqueue(new ChatCompletionResult(string.Empty, null, null, TimeSpan.Zero, "tool_calls")
            {
                ToolCalls = new[] { new ToolCall($"call-{_replies.Count}", tool, argumentsJson) }
            });

            return this;
        }

        public ScriptedAgentModel Answers(string text)
        {
            _replies.Enqueue(new ChatCompletionResult(text, null, null, TimeSpan.Zero, "stop"));
            return this;
        }

        /// <summary>Keeps asking for the same tool for ever, which is what a cap is for.</summary>
        public ScriptedAgentModel AlwaysAsks(string tool, string argumentsJson)
        {
            _always = new ChatCompletionResult(string.Empty, null, null, TimeSpan.Zero, "tool_calls")
            {
                ToolCalls = new[] { new ToolCall("call-again", tool, argumentsJson) }
            };

            return this;
        }

        private int _stopAtTurn = -1;
        private CancellationTokenSource? _stopper;

        /// <summary>
        /// Stops the run from inside the loop, on a chosen turn.
        /// </summary>
        /// <remarks>
        /// Deterministic, where cancelling from another thread is a race this always loses: a
        /// scripted model answers instantly, so fifty turns are over before a watcher notices the
        /// first one. Stopping from inside is the same thing the button does, at a known moment.
        /// </remarks>
        public ScriptedAgentModel StopsAtTurn(int turn, CancellationTokenSource stopper)
        {
            _stopAtTurn = turn;
            _stopper = stopper;

            return this;
        }

        public bool CanAnswer(out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public Task<IReadOnlyList<ToolDefinition>> ConfiguredToolsAsync(NodeExecutionContext ctx, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ToolDefinition>>(Array.Empty<ToolDefinition>());

        public Task<ChatCompletionResult> ContinueAsync(
            IReadOnlyList<ChatMessage> messages,
            IReadOnlyList<ToolDefinition> tools,
            NodeExecutionContext ctx,
            IProgress<string>? onToken,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            Calls++;
            Offered = tools;
            Turns.Add(messages.ToList());

            if (Calls == _stopAtTurn)
            {
                _stopper?.Cancel();
            }

            if (_replies.Count > 0)
            {
                return Task.FromResult(_replies.Dequeue());
            }

            return Task.FromResult(_always
                ?? new ChatCompletionResult("nothing was scripted", null, null, TimeSpan.Zero, "stop"));
        }

        public Task<(string Text, bool IsError)> CallConfiguredToolAsync(
            ToolCall call,
            string ownerId,
            NodeExecutionContext ctx,
            CancellationToken ct)
            => Task.FromResult(("this model contributed no tools", true));

        /// <summary>Nothing to do as a step. It exists to be wired to, like a real Model node.</summary>
        public override Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
            => Task.FromResult(NodeResult.Empty);

        public override JsonObject SaveSettings() => new();

        public override void LoadSettings(JsonObject settings)
        {
        }
    }

    private static async Task<(RunContext Run, string Text)> RunAsync(
        TestServices services,
        ScriptedAgentModel model,
        AgentNode agent,
        string request = "do the thing",
        CancellationToken ct = default)
    {
        var graph = new GraphModel();

        graph.AddNode(model);
        graph.AddNode(agent);
        Assert.True(graph.TryConnect(model.Self, agent.Model, out _));

        var run = await new GraphExecutor(services.Services).RunAsync(graph, request, ct);

        var text = run.TryGetValue(agent.Result, out var value) ? value?.ToString() ?? string.Empty : string.Empty;

        return (run, text);
    }

    /// <summary>The toolbox is what it says it is, and nothing runs a command line.</summary>
    /// <remarks>
    /// Worth pinning because the absence is deliberate. This application starts processes it owns
    /// and nothing else, and a tool that ran an arbitrary command would be a hole through all of it.
    /// </remarks>
    [Fact]
    public void TheToolsAreTheOnesItSaysItHas()
    {
        var names = AgentToolbox.Tools.Select(t => t.Name).ToList();

        Assert.Equal(
            new[] { "read_file", "write_file", "edit_file", "list_folder", "search_project", "compile" },
            names);

        Assert.All(AgentToolbox.Tools, t => Assert.Equal(AgentToolbox.OwnerId, t.ExtensionId));
        Assert.DoesNotContain(names, n => n.Contains("run", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A tool is called, its result comes back, and the loop carries on.
    /// </summary>
    /// <remarks>
    /// The whole of what makes this a loop rather than a pipeline: the model sees what its own move
    /// produced and chooses the next one from it.
    /// </remarks>
    [Fact]
    public async Task ItCallsAToolAndCarriesOn()
    {
        using var project = SampleProject.Create();
        using var services = TestServices.Create(project);

        var model = new ScriptedAgentModel()
            .Asks("search_project", """{"name":"NothingAtAll"}""")
            .Answers("There was nothing to change.");

        var (run, text) = await RunAsync(services, model, new AgentNode());

        Assert.NotEqual(RunState.Faulted, run.State);
        Assert.Equal(2, model.Calls);
        Assert.Equal("There was nothing to change.", text);

        // The second turn was given the first turn's result, which is the point.
        var second = model.Turns[1];

        Assert.Contains(second, m => m.Role == "tool" && m.Content!.Contains("NothingAtAll", StringComparison.Ordinal));
    }

    /// <summary>Its own tools are offered whether or not the model contributed any.</summary>
    [Fact]
    public async Task ItsOwnToolsAreAlwaysOffered()
    {
        using var project = SampleProject.Create();
        using var services = TestServices.Create(project);

        var model = new ScriptedAgentModel().Answers("done");

        await RunAsync(services, model, new AgentNode());

        Assert.Equal(AgentToolbox.Tools.Count, model.Offered.Count);
    }

    /// <summary>
    /// A tool that fails comes back as a result, and the run does not fault.
    /// </summary>
    /// <remarks>
    /// The same discipline as the repair loop and the extension loop. A model that is told what
    /// went wrong can do something else; a run that ended cannot.
    /// </remarks>
    [Fact]
    public async Task AToolFailureIsAResultRatherThanAFault()
    {
        using var project = SampleProject.Create();
        using var services = TestServices.Create(project);

        var model = new ScriptedAgentModel()
            .Asks("read_file", """{"path":"Assets/Scripts/NotThere.cs"}""")
            .Answers("That file is not there, so I stopped.");

        var (run, text) = await RunAsync(services, model, new AgentNode());

        Assert.NotEqual(RunState.Faulted, run.State);
        Assert.Equal("That file is not there, so I stopped.", text);

        var second = model.Turns[1];
        var result = second.Last(m => m.Role == "tool").Content!;

        Assert.Contains("not there", result, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A tool nobody has is refused the same way, rather than throwing.</summary>
    [Fact]
    public async Task AToolThatDoesNotExistIsARefusalNotAFault()
    {
        using var project = SampleProject.Create();
        using var services = TestServices.Create(project);

        var model = new ScriptedAgentModel()
            .Asks("delete_everything", "{}")
            .Answers("I cannot do that.");

        var (run, _) = await RunAsync(services, model, new AgentNode());

        Assert.NotEqual(RunState.Faulted, run.State);
        Assert.Contains(
            model.Turns[1],
            m => m.Role == "tool" && m.Content!.Contains("no tool called", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The cap stops it, and stopping is not failing.
    /// </summary>
    /// <remarks>
    /// A model that has gone round in circles has to be stopped by something, and what it already
    /// did stays done. The run completes and the node says where it got to.
    /// </remarks>
    [Fact]
    public async Task TheCapIsHonoured()
    {
        using var project = SampleProject.Create();
        using var services = TestServices.Create(project);

        var model = new ScriptedAgentModel().AlwaysAsks("search_project", """{"name":"Anything"}""");
        var agent = new AgentNode { MaxTurns = 3 };

        var (run, text) = await RunAsync(services, model, agent);

        Assert.Equal(3, model.Calls);
        Assert.NotEqual(RunState.Faulted, run.State);
        Assert.Contains("3 turns", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A write goes through the guarded path, so a duplicate type is refused.
    /// </summary>
    /// <remarks>
    /// The one thing that must be true of every tool here. An agent with a private route to disk
    /// would be an agent past the duplicate guard, the Unity rules and the staged write all at
    /// once, and the refusal comes back to it as a result to read rather than as a stopped run.
    /// </remarks>
    [Fact]
    public async Task AWriteGoesThroughTheGuards()
    {
        using var project = SampleProject.Create();
        using var services = TestServices.Create(project);

        await services.Index.EnsureAsync(project.Root, null, CancellationToken.None);

        var existing = services.Index.Files.SelectMany(f => f.Types).FirstOrDefault();

        Assert.NotNull(existing);

        var content = $"public class {existing!.Name} {{ }}";

        var model = new ScriptedAgentModel()
            .Asks("write_file", new JsonObject
            {
                ["path"] = "Assets/Scripts/Copy.cs",
                ["content"] = content
            }.ToJsonString())
            .Answers("It was refused, so I stopped.");

        var (run, _) = await RunAsync(services, model, new AgentNode());

        Assert.NotEqual(RunState.Faulted, run.State);

        var result = model.Turns[1].Last(m => m.Role == "tool").Content!;

        Assert.Contains("already declared", result, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(project.Root, "Assets", "Scripts", "Copy.cs")));
    }

    /// <summary>
    /// A file it did write is written, and the guard does not undo it.
    /// </summary>
    /// <remarks>
    /// The other half of the same claim. Guarded does not mean nothing gets through.
    /// </remarks>
    [Fact]
    public async Task AWriteThatPassesLands()
    {
        using var project = SampleProject.Create();
        using var services = TestServices.Create(project);

        var model = new ScriptedAgentModel()
            .Asks("write_file", new JsonObject
            {
                ["path"] = "Assets/Scripts/BrandNewThing.cs",
                ["content"] = "public class BrandNewThing { }"
            }.ToJsonString())
            .Answers("Written.");

        var (run, _) = await RunAsync(services, model, new AgentNode());

        Assert.NotEqual(RunState.Faulted, run.State);
        Assert.True(File.Exists(Path.Combine(project.Root, "Assets", "Scripts", "BrandNewThing.cs")));
    }

    /// <summary>An elided whole file is refused before it reaches disk.</summary>
    [Fact]
    public async Task AnElidedWriteIsRefused()
    {
        using var project = SampleProject.Create();
        using var services = TestServices.Create(project);

        var model = new ScriptedAgentModel()
            .Asks("write_file", new JsonObject
            {
                ["path"] = "Assets/Scripts/Half.cs",
                ["content"] = "public class Half {\n    // ... rest of the code unchanged ...\n}"
            }.ToJsonString())
            .Answers("stopped");

        await RunAsync(services, model, new AgentNode());

        Assert.Contains(
            model.Turns[1],
            m => m.Role == "tool" && m.Content!.Contains("complete file", StringComparison.OrdinalIgnoreCase));

        Assert.False(File.Exists(Path.Combine(project.Root, "Assets", "Scripts", "Half.cs")));
    }

    /// <summary>
    /// Stopping part way leaves what was already done alone.
    /// </summary>
    /// <remarks>
    /// Each write is committed as it happens rather than at the end, so a stop is not a rollback.
    /// What was written is written, and the run reports that it was cancelled rather than that it
    /// failed.
    /// </remarks>
    [Fact]
    public async Task StoppingPartWayLeavesWhatItAlreadyDid()
    {
        using var project = SampleProject.Create();
        using var services = TestServices.Create(project);
        using var stopping = new CancellationTokenSource();

        var model = new ScriptedAgentModel()
            .Asks("write_file", new JsonObject
            {
                ["path"] = "Assets/Scripts/Early.cs",
                ["content"] = "public class Early { }"
            }.ToJsonString())
            .AlwaysAsks("search_project", """{"name":"Anything"}""");

        // Stopped on the second turn, which is after the first file has been written and before
        // the loop could have finished on its own.
        model.StopsAtTurn(2, stopping);

        var agent = new AgentNode { MaxTurns = 50 };
        var written = Path.Combine(project.Root, "Assets", "Scripts", "Early.cs");

        var (run, _) = await RunAsync(services, model, agent, "do the thing", stopping.Token);

        Assert.NotEqual(RunState.Completed, run.State);
        Assert.True(File.Exists(written));
    }
}
