using System.Text.Json.Nodes;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;

namespace LocalNEXUS.App.Nodes;

/// <summary>
/// Holds the place of a node whose extension is not installed here.
/// </summary>
/// <remarks>
/// A graph is a file somebody keeps. Opening one on a machine that is missing an extension used
/// to be impossible to survive: the factory refused the unknown type key, the node was skipped,
/// its wires went with it, and the next save wrote a graph with a hole in it. The person who
/// installed the extension afterwards would find their work already gone.
/// <para>
/// So the node is kept rather than dropped. Everything read from the file is held untouched, the
/// type key, the settings payload and the pins with their saved identities, and written back out
/// exactly as it came in. Installing the extension and reopening restores the graph as it was.
/// This is the same rule that keeps every historical type key loading, applied to a type key that
/// belongs to somebody else.
/// </para>
/// <para>
/// It refuses to run, and says why. A placeholder that quietly produced nothing would let a run
/// report success having skipped a step.
/// </para>
/// </remarks>
public sealed class UnavailableNode : NodeBase
{
    private JsonObject _saved = new();

    public UnavailableNode(string typeKey)
        : base(typeKey)
    {
        TypeKey = typeKey;
    }

    /// <inheritdoc />
    public override string TypeKey { get; }

    /// <summary>
    /// Rebuilds the pins as the file recorded them, so the wires drawn to this node survive.
    /// </summary>
    /// <remarks>
    /// The type matters and the reasoning that said it did not was wrong. Compatibility is not
    /// only checked when a wire is drawn: restoring a connection goes through the same check, so a
    /// placeholder whose pins all claimed to be Text refused every Code wire it was holding and
    /// dropped it with a warning. The promise that the node and its wires are kept was only true
    /// for the plainest half of them.
    /// <para>
    /// So the type is read from the file, which now records it. A graph saved before it did has no
    /// type to read and falls back to Text, which is what those graphs already do today.
    /// </para>
    /// </remarks>
    public void AdoptSavedPins(JsonArray? inputs, JsonArray? outputs)
    {
        Adopt(inputs, PinDirection.Input);
        Adopt(outputs, PinDirection.Output);
    }

    /// <inheritdoc />
    public override Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
        => throw new InvalidOperationException(
            $"'{TypeKey}' is contributed by an extension that is not installed for this project. " +
            "Install it from Settings, Extensions, then open this graph again. The node and its wires have been kept.");

    /// <inheritdoc />
    public override JsonObject SaveSettings() => (JsonObject)_saved.DeepClone();

    /// <inheritdoc />
    public override void LoadSettings(JsonObject settings) => _saved = (JsonObject)settings.DeepClone();

    private void Adopt(JsonArray? saved, PinDirection direction)
    {
        if (saved is null)
        {
            return;
        }

        foreach (var entry in saved.OfType<JsonObject>())
        {
            var name = entry["name"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var pinType = ReadPinType(entry["type"]?.GetValue<string>());

            _ = direction == PinDirection.Input
                ? AddInput(name, pinType)
                : AddOutput(name, pinType);
        }
    }

    /// <summary>
    /// The type the file recorded, or Text when it recorded none.
    /// </summary>
    /// <remarks>
    /// Text for a graph saved before types were written, and Text for a type this build does not
    /// have a name for, which is a graph from a newer build. Neither is worth refusing to open
    /// over, and both leave the node exactly where it was.
    /// </remarks>
    private static PinType ReadPinType(string? saved)
        => Enum.TryParse<PinType>(saved, out var pinType) ? pinType : PinType.Text;
}
