using System.Text;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Agent;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Inference;

namespace LocalNEXUS.App.Nodes;

/// <summary>
/// A model, a set of tools, and a loop that decides what to do next.
/// </summary>
/// <remarks>
/// The other half of the application, beside the pipeline rather than instead of it. Prompt,
/// Triage, Model, Compiler check and Output is the right shape when the same work runs the same way
/// every time and every step is worth inspecting before it happens. It is the wrong shape when the
/// request is not the shape the pipeline describes: Triage runs unconditionally and its whole
/// vocabulary is file operations, so a request that wanted no files written became a plan to write
/// files, and the run failed doing the wrong thing correctly.
///
/// This is not a router. A router is another model call making a classification, which moves the
/// problem one step earlier rather than answering it, and real work interleaves anyway: write a
/// script, attach it to an object, set a field on it. No static route expresses that, and choosing
/// each move as it comes is what production coding agents actually do.
///
/// The loop lives here, exactly as the tool loop lives in the Model node and the repair loop in the
/// Compiler check. The executor still orders nodes and knows nothing about any of them.
///
/// It has no model of its own. The Model pin carries one, which is the same arrangement the planner
/// uses: a node that needs a model borrows the one that was configured rather than carrying a
/// second copy of every setting, and its selected extensions and its search key come with it.
/// </remarks>
public sealed partial class AgentNode : NodeBase
{
    /// <summary>
    /// What the agent is told it is, before anything else.
    /// </summary>
    /// <remarks>
    /// Short on purpose. Everything about what the tools do is on the tools, which is where a model
    /// reads it, and repeating it here would be two descriptions to keep in step. What is here is
    /// the part no tool description can say: that it is finished when it stops asking, and that a
    /// refusal is an answer rather than an obstacle.
    /// </remarks>
    public const string DefaultSystemPrompt =
        "You are working inside somebody's codebase, with tools. Work out what the request needs "
        + "and do it, one step at a time, using the tools you have. Look before you change "
        + "anything: read a file before editing it, and search for a type before writing a new "
        + "one.\n\n"
        + "Some things you try will be refused. The project has rules about what may be written "
        + "and they are there for a reason, so read what the refusal says and do something else "
        + "rather than trying the same thing again.\n\n"
        + "Not every request needs a file written. If the work is done through other tools, or "
        + "there is nothing to do, say so and stop.\n\n"
        + "When you are finished, answer with a short plain summary of what you did. Do not ask "
        + "for a tool in that final answer.";

    /// <summary>
    /// How many turns the loop takes before it stops.
    /// </summary>
    /// <remarks>
    /// A turn is one model call and the tools it asked for, so this is the budget for the whole
    /// task rather than for one file. Twenty five is enough for a request touching several files
    /// with a compile and a correction each, and small enough that a model going round in circles
    /// stops within a couple of minutes rather than overnight.
    /// </remarks>
    [ObservableProperty]
    private int _maxTurns = 25;

    /// <summary>What the agent is told it is.</summary>
    [ObservableProperty]
    private string _systemPrompt = DefaultSystemPrompt;

    public AgentNode()
        : base("Agent")
    {
        Request = AddInput("Text", PinType.Text);
        Model = AddInput("Model", PinType.Model);
        Result = AddOutput("Text", PinType.Text);
    }

    /// <inheritdoc />
    public override string TypeKey => "Agent";

    /// <summary>What to do, in words.</summary>
    public Pin Request { get; }

    /// <summary>The model that runs the loop, and whose tools it borrows.</summary>
    public Pin Model { get; }

    /// <summary>What it did, in words, so this composes with the rest of a graph.</summary>
    public Pin Result { get; }

    /// <inheritdoc />
    public override async Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var request = ctx.GetText(Request);

        if (string.IsNullOrWhiteSpace(request))
        {
            request = ctx.UserRequest;
        }

        if (string.IsNullOrWhiteSpace(request))
        {
            throw new InvalidOperationException(
                $"{Title} was not given anything to do. Wire something into its text input, or type a request.");
        }

        if (FindModel(ctx) is not { } model)
        {
            throw new InvalidOperationException(
                $"{Title} has no model. Wire a Model node into its Model pin; the agent borrows that node's "
                + "model, its selected extensions and its search key rather than carrying its own.");
        }

        if (!model.CanAnswer(out var reason))
        {
            throw new InvalidOperationException($"{Title} cannot run: {reason}");
        }

        var toolbox = new AgentToolbox(ctx, Id);
        var tools = await AllToolsAsync(model, ctx, ct).ConfigureAwait(false);

        var messages = new List<ChatMessage>
        {
            ChatMessage.System(SystemPrompt),
            ChatMessage.User(request)
        };

        ctx.Feed.Info(
            $"{Title} started with {tools.Count} tool(s)",
            string.Join(Environment.NewLine, tools.Select(t => t.Name)));

        var summary = string.Empty;
        var calls = 0;

        for (var turn = 1; turn <= MaxTurns; turn++)
        {
            ct.ThrowIfCancellationRequested();

            StatusMessage = $"turn {turn} of {MaxTurns}";

            var reply = await model.ContinueAsync(messages, tools, ctx, null, ct).ConfigureAwait(false);

            summary = reply.Text ?? string.Empty;

            if (!reply.WantsTools || reply.ToolCalls.Count == 0)
            {
                StatusMessage = $"finished in {turn} turn(s), {calls} tool call(s)";

                if (calls == 0)
                {
                    // Nothing was called, so nothing was read, written or run, whatever the answer
                    // says. A model that describes work it did not do reads as a completed run,
                    // and the only way to tell from the outside is that no tool was ever used.
                    ctx.Feed.Error(
                        $"{Title} finished without using a tool",
                        "It answered in one turn and called nothing, so nothing was read, written or "
                        + "run. If the answer claims work was done, it was not done here. That is "
                        + "usually a model that cannot emit tool calls: check tool support on the "
                        + "Model node. It is also the right answer when there was genuinely nothing "
                        + $"to do.{Environment.NewLine}{Environment.NewLine}{summary}");
                }
                else
                {
                    ctx.Feed.Info($"{Title} finished", $"{turn} turn(s), {calls} tool call(s).");
                }

                return Emit(summary, toolbox);
            }

            messages.Add(ChatMessage.Assistant(summary, reply.ToolCalls));

            foreach (var call in reply.ToolCalls)
            {
                ct.ThrowIfCancellationRequested();

                calls++;

                var (text, isError) = await RunAsync(model, toolbox, tools, call, ctx, ct).ConfigureAwait(false);

                messages.Add(ChatMessage.Tool(call.Id, text));
            }
        }

        // Out of turns rather than finished. Everything written stays written and everything staged
        // stays staged, because each was committed as it happened, and what comes out says where it
        // got to rather than pretending it was done.
        StatusMessage = $"stopped at the limit of {MaxTurns} turns";

        ctx.Feed.Error(
            $"{Title} reached its limit",
            $"It took {MaxTurns} turns and {calls} tool call(s) without finishing. Raise the limit on the "
            + "node, or narrow the request. Anything it wrote is written and anything refused is waiting.");

        return Emit(
            summary.Length > 0
                ? summary
                : $"Stopped after {MaxTurns} turns without finishing.",
            toolbox);
    }

    /// <summary>
    /// Runs one tool, wherever it came from, and writes what happened to the feed.
    /// </summary>
    /// <remarks>
    /// Every call is visible, with a one line result, and whatever it produced folded away behind
    /// the entry that produced it. Somebody watching should be able to follow what it is doing
    /// without reading a word of model output.
    ///
    /// A failure comes back as a result rather than as a fault, the same as the repair loop and the
    /// extension loop. That is what lets the model read a refusal and do something else, which is
    /// the whole of how a guardrail teaches rather than merely stops.
    /// </remarks>
    private async Task<(string Text, bool IsError)> RunAsync(
        IToolCallingModel model,
        AgentToolbox toolbox,
        IReadOnlyList<ToolDefinition> tools,
        ToolCall call,
        NodeExecutionContext ctx,
        CancellationToken ct)
    {
        var owner = tools.FirstOrDefault(t => string.Equals(t.Name, call.Name, StringComparison.Ordinal));

        if (owner is null)
        {
            return ($"There is no tool called '{call.Name}'.", true);
        }

        var entry = ctx.Feed.Add(
            ActivityKind.Info,
            $"{Title}: {call.Name}",
            Summarise(call.ArgumentsJson),
            Id);

        StatusMessage = call.Name;

        var (text, isError) = AgentToolbox.Owns(owner.ExtensionId)
            ? await toolbox.RunAsync(call, ct).ConfigureAwait(false)
            : await model.CallConfiguredToolAsync(call, owner.ExtensionId, ctx, ct).ConfigureAwait(false);

        entry.SetText($"{call.ArgumentsJson}{Environment.NewLine}{Environment.NewLine}{text}");
        entry.Detail = isError ? $"failed: {Summarise(text)}" : Summarise(text);

        return (text, isError);
    }

    /// <summary>Its own tools, plus whatever the model it borrowed was configured with.</summary>
    private static async Task<IReadOnlyList<ToolDefinition>> AllToolsAsync(
        IToolCallingModel model,
        NodeExecutionContext ctx,
        CancellationToken ct)
    {
        var tools = new List<ToolDefinition>(AgentToolbox.Tools);

        tools.AddRange(await model.ConfiguredToolsAsync(ctx, ct).ConfigureAwait(false));

        return tools;
    }

    /// <summary>
    /// The model wired into the Model pin, if it can call tools.
    /// </summary>
    /// <remarks>
    /// Asked of whatever is on the wire rather than looked up by type, which is what keeps the
    /// executor and this node ignorant of each other. A node that answers questions but cannot call
    /// tools is not one this can use, and saying so is better than running a loop that can only go
    /// round once.
    /// </remarks>
    private IToolCallingModel? FindModel(NodeExecutionContext ctx)
        => ctx.GetSourceNode(Model) as IToolCallingModel;

    private NodeResult Emit(string summary, AgentToolbox toolbox)
    {
        if (toolbox.Written.Count > 0)
        {
            StatusMessage = $"{toolbox.Written.Count} file(s) written";
        }

        return NodeResult.FromPin(Result, summary);
    }

    private static string Summarise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var flat = value.ReplaceLineEndings(" ").Trim();

        return flat.Length <= 120 ? flat : flat[..120] + "...";
    }

    /// <inheritdoc />
    public override JsonObject SaveSettings() => new()
    {
        ["maxTurns"] = MaxTurns,
        ["systemPrompt"] = SystemPrompt
    };

    /// <inheritdoc />
    public override void LoadSettings(JsonObject settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        MaxTurns = settings["maxTurns"]?.GetValue<int>() ?? 25;
        SystemPrompt = settings["systemPrompt"]?.GetValue<string>() ?? DefaultSystemPrompt;
    }
}
