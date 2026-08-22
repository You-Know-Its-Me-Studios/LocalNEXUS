using System.Text.Json;
using System.Text.Json.Nodes;
using LocalNEXUS.App.Models.Extensions;
using LocalNEXUS.App.Services.Inference;

namespace LocalNEXUS.App.Services.Extensions;

/// <summary>
/// Gathers the tools a model node may call, and routes a call back to whichever extension owns it.
/// </summary>
/// <remarks>
/// Routing is by extension rather than by tool name because tool names are not unique across
/// servers. Two Unity servers both offering something called <c>get_scene</c> is entirely
/// ordinary, and a call sent to the wrong one would do the wrong thing quietly.
/// <para>
/// Starting is still lazy. Nothing here runs an extension until a node that selected it is about
/// to ask a model something.
/// </para>
/// </remarks>
public sealed class ExtensionToolset
{
    private readonly ExtensionRegistry _registry;
    private readonly ExtensionHost _host;
    private readonly Dictionary<string, McpToolClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    public ExtensionToolset(ExtensionRegistry registry, ExtensionHost host)
    {
        _registry = registry;
        _host = host;
    }

    /// <summary>
    /// What this project has installed, so a node can offer the choice of them.
    /// </summary>
    /// <remarks>
    /// Exposed here rather than handed to every node separately, because a node that can call an
    /// extension's tools already holds the thing that knows how to call them and asking it what
    /// there is to call is the same question.
    /// </remarks>
    public ExtensionRegistry Registry => _registry;

    /// <summary>
    /// Starts each selected MCP extension and asks it what tools it has.
    /// </summary>
    /// <param name="extensionIds">Which extensions this node selected.</param>
    /// <param name="allowedTools">
    /// Tool names to keep, or null for all of them. Filtering is a convenience rather than a
    /// requirement: several hundred tools is a few thousand tokens of schema every turn, which is
    /// nothing on a large context and worth trimming on a small one.
    /// </param>
    /// <param name="onProblem">Told about an extension that could not be reached, rather than throwing.</param>
    public async Task<IReadOnlyList<ToolDefinition>> GatherAsync(
        IEnumerable<string> extensionIds,
        IReadOnlyCollection<string>? allowedTools,
        Action<string, string> onProblem,
        CancellationToken ct)
    {
        var definitions = new List<ToolDefinition>();

        foreach (var id in extensionIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var extension = _registry.Find(id);

            if (extension is null)
            {
                onProblem(id, "It is not registered against this project.");
                continue;
            }

            if (!extension.IsUsable)
            {
                onProblem(extension.Manifest.Name, extension.StateDetail ?? $"It is {extension.StateText}.");
                continue;
            }

            try
            {
                var session = await _host
                    .EnsureRunningAsync(extension, ExtensionContract.Mcp, ct)
                    .ConfigureAwait(false);

                var client = new McpToolClient(session);
                _clients[extension.Manifest.Id] = client;

                var tools = await client.ListToolsAsync(ct).ConfigureAwait(false);

                // What the running server says outranks what the manifest claimed, and is kept so
                // the details pane can show it without starting anything again.
                extension.DiscoveredTools.Clear();

                foreach (var tool in tools)
                {
                    extension.DiscoveredTools.Add(tool);

                    if (allowedTools is { Count: > 0 } && !allowedTools.Contains(tool.Name))
                    {
                        continue;
                    }

                    definitions.Add(new ToolDefinition(
                        tool.Name,
                        tool.Description,
                        tool.InputSchema,
                        extension.Manifest.Id));
                }
            }
            catch (ExtensionException ex)
            {
                // One unreachable extension must not stop a run that has others, or that could
                // have answered without any of them.
                onProblem(extension.Manifest.Name, ex.Message);
            }
        }

        return definitions;
    }

    /// <summary>
    /// Calls one tool and returns what it said.
    /// </summary>
    /// <returns>The text to hand back to the model, and whether it was a failure.</returns>
    public async Task<(string Text, bool IsError)> CallAsync(
        ToolCall call,
        string extensionId,
        CancellationToken ct)
    {
        if (!_clients.TryGetValue(extensionId, out var client))
        {
            return ($"The extension providing '{call.Name}' is no longer running.", true);
        }

        JsonObject arguments;

        try
        {
            arguments = string.IsNullOrWhiteSpace(call.ArgumentsJson)
                ? new JsonObject()
                : JsonNode.Parse(call.ArgumentsJson) as JsonObject ?? new JsonObject();
        }
        catch (JsonException ex)
        {
            // Back to the model rather than up as a fault. A model that emitted malformed
            // arguments can fix them if it is told; it cannot do anything with a stopped run.
            return ($"The arguments for '{call.Name}' were not valid JSON: {ex.Message}", true);
        }

        return await client.CallAsync(call.Name, arguments, ct).ConfigureAwait(false);
    }
}
