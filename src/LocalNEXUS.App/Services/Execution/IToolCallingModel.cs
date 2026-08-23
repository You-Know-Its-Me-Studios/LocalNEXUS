using LocalNEXUS.App.Services.Inference;

namespace LocalNEXUS.App.Services.Execution;

/// <summary>
/// A node whose model can be given tools and asked to choose between them.
/// </summary>
/// <remarks>
/// The third capability advertised by interface rather than named anywhere, after
/// <see cref="ICodeRepairSource"/> and the planning one. A node that needs a model which can call
/// tools looks along its own wires and asks whatever it finds, and the executor learns nothing
/// about either of them.
///
/// Separate from <see cref="IModelHandle"/> rather than added to it. That interface asks one
/// question and gets one answer, which is what Triage and Debate want; a caller running a loop
/// needs the whole reply including what it asked to call, and needs the tools the model was
/// configured with. Widening the simpler interface would make every implementer answer a question
/// it has no use for.
///
/// The tools an extension contributes belong to the model node, because that is where they are
/// selected and where their cost is counted. A caller borrows them rather than carrying a second
/// copy of the same selection, the same way the planner borrows the model that is going to write.
/// </remarks>
public interface IToolCallingModel
{
    /// <summary>Whether this node could answer right now, and why not when it could not.</summary>
    bool CanAnswer(out string reason);

    /// <summary>What this node is configured as, for the feed.</summary>
    string ModelName { get; }

    /// <summary>
    /// The tools this node was configured with: its selected extensions, and search when the send
    /// asked for it.
    /// </summary>
    /// <remarks>
    /// Starting an extension is what listing its tools means, so this is asked once per run rather
    /// than once per turn.
    /// </remarks>
    Task<IReadOnlyList<ToolDefinition>> ConfiguredToolsAsync(NodeExecutionContext ctx, CancellationToken ct);

    /// <summary>
    /// Runs one turn of a conversation and returns the whole reply, including any tool calls.
    /// </summary>
    /// <param name="messages">Everything said so far, including tool results.</param>
    /// <param name="tools">What the model may call this turn.</param>
    /// <param name="ctx">A context belonging to the calling node.</param>
    /// <param name="onToken">Told about each chunk, for a live entry.</param>
    /// <param name="ct">Cancels the turn.</param>
    Task<ChatCompletionResult> ContinueAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        NodeExecutionContext ctx,
        IProgress<string>? onToken,
        CancellationToken ct);

    /// <summary>
    /// Runs one of the tools this node contributed, and says whether it failed.
    /// </summary>
    /// <remarks>
    /// A failure comes back as a result rather than an exception, because that is what the model
    /// reads and corrects itself from. It is the same discipline the repair loop follows.
    /// </remarks>
    Task<(string Text, bool IsError)> CallConfiguredToolAsync(
        ToolCall call,
        string ownerId,
        NodeExecutionContext ctx,
        CancellationToken ct);
}
