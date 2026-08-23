using System.Text.Json.Nodes;

namespace LocalNEXUS.App.Services.Inference;

/// <summary>Whether a model actually emits tool calls, established by asking it to.</summary>
public enum ToolSupport
{
    /// <summary>It was given a tool, asked to use it, and called it.</summary>
    Supported,

    /// <summary>It was given a tool, asked to use it, and did not call it.</summary>
    Unsupported,

    /// <summary>It could not be asked, which is not the same as an answer.</summary>
    Unknown
}

/// <summary>
/// Asks a model whether it can call tools by giving it one and seeing what comes back.
/// </summary>
/// <remarks>
/// This used to read <c>chat_template_caps</c> from llama.cpp's <c>/props</c>, and that was the
/// wrong question. Those flags describe what the chat template can express, not what the model
/// does with it, and the difference is not academic: Qwen2.5-Coder-7B reports
/// <c>supports_tools</c> and <c>supports_tool_calls</c> both true, is handed the tool definitions
/// and Qwen's own instruction to answer inside <c>&lt;tool_call&gt;</c> tags, and then answers with
/// a markdown fence full of JSON instead. Measured, at temperature zero, with no system prompt and
/// again with an explicit reminder: it never once produced the tags, and invented a different wrong
/// format each time.
///
/// A caller reading the template's capabilities is told yes and then watches the agent stop after
/// one turn with nothing done. So the only honest check is behavioural: hand it a trivial tool,
/// ask for something that plainly needs it, and see whether a tool call comes back through the
/// protocol.
///
/// It costs one small model call, which is why the answer is remembered. It is remembered against
/// the model rather than the address, because the address of a local server is a port picked at
/// startup and the model behind it is the thing being asked about.
/// </remarks>
public sealed class ToolSupportProbe
{
    /// <summary>How long the probe waits before giving up and answering Unknown.</summary>
    /// <remarks>
    /// Generous, because a model that has just been loaded is answering its first request and a
    /// large one on a busy card is slow before it is warm. Unknown from an impatient probe would be
    /// worse than no probe: it would say nothing is known about a model that works.
    /// </remarks>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    /// <summary>What the model is offered. As small as a tool can be and still be worth calling.</summary>
    private static readonly ToolDefinition Trivial = new(
        "report_number",
        "Report a number back. Call this whenever you are asked for a number.",
        new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["value"] = new JsonObject { ["type"] = "integer", ["description"] = "The number." }
            },
            ["required"] = new JsonArray("value")
        },
        "localnexus.probe");

    private readonly IModelClient _client;
    private readonly object _sync = new();
    private readonly Dictionary<string, (ToolSupport Support, string Detail)> _answers =
        new(StringComparer.OrdinalIgnoreCase);

    public ToolSupportProbe(IModelClient client) => _client = client;

    /// <summary>What is already known, without asking anything.</summary>
    public (ToolSupport Support, string Detail)? Remembered(string? modelKey)
    {
        if (string.IsNullOrWhiteSpace(modelKey))
        {
            return null;
        }

        lock (_sync)
        {
            return _answers.TryGetValue(modelKey, out var answer) ? answer : null;
        }
    }

    /// <summary>Forgets what was learned about a model, so the next ask measures it again.</summary>
    public void Forget(string? modelKey)
    {
        if (string.IsNullOrWhiteSpace(modelKey))
        {
            return;
        }

        lock (_sync)
        {
            _answers.Remove(modelKey);
        }
    }

    /// <summary>
    /// Gives the model a tool and sees whether it calls it.
    /// </summary>
    /// <param name="endpoint">Where to ask.</param>
    /// <param name="modelKey">
    /// What the answer is remembered against: a model file for a local one, and the address and
    /// model id for anything else.
    /// </param>
    /// <param name="ct">Cancels the probe.</param>
    public async Task<(ToolSupport Support, string Detail)> ProbeAsync(
        ModelEndpoint endpoint,
        string? modelKey,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (Remembered(modelKey) is { } known)
        {
            return known;
        }

        if (string.IsNullOrWhiteSpace(endpoint.BaseUrl))
        {
            return (ToolSupport.Unknown, "There is no address to ask.");
        }

        var messages = new[]
        {
            ChatMessage.System("You have tools. When a tool fits the request, call it rather than describing it."),
            ChatMessage.User("Report the number 7.")
        };

        try
        {
            using var timer = new CancellationTokenSource(Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, ct);

            var result = await _client
                .StreamChatAsync(endpoint, messages, new[] { Trivial }, 0d, 128, null, linked.Token)
                .ConfigureAwait(false);

            var answer = result.WantsTools && result.ToolCalls.Count > 0
                ? (ToolSupport.Supported, "This model calls tools. It was given one and it called it.")
                : (ToolSupport.Unsupported, Explain(result.Text));

            Remember(modelKey, answer);

            return answer;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is ModelClientException or OperationCanceledException)
        {
            // Not remembered. Nothing was established, and a failure to reach a server is a fact
            // about this moment rather than about the model.
            return (ToolSupport.Unknown, $"It could not be asked, so nothing is known: {ex.Message}");
        }
    }

    /// <summary>
    /// What to say about a model that did not call the tool.
    /// </summary>
    /// <remarks>
    /// Quoting what it did instead is the useful part. A model answering in prose has not
    /// understood; one answering with a JSON function call in a code fence has understood perfectly
    /// and cannot express it, which is the common case and reads as a bug in this application until
    /// somebody sees the reply.
    /// </remarks>
    private static string Explain(string? text)
    {
        var said = (text ?? string.Empty).ReplaceLineEndings(" ").Trim();

        var looksLikeACall = said.Contains("report_number", StringComparison.OrdinalIgnoreCase);

        var reason = looksLikeACall
            ? "It wrote the tool call out as text instead of calling it, which the protocol cannot see. "
            : "It answered without calling the tool. ";

        var quoted = said.Length == 0
            ? string.Empty
            : $" It said: {(said.Length <= 160 ? said : said[..160] + "...")}";

        return "This model does not call tools. " + reason
               + "Anything selected for it is context spent for nothing, so use a model tuned for "
               + "tool use, or a hosted one." + quoted;
    }

    private void Remember(string? modelKey, (ToolSupport Support, string Detail) answer)
    {
        if (string.IsNullOrWhiteSpace(modelKey))
        {
            return;
        }

        lock (_sync)
        {
            _answers[modelKey] = answer;
        }
    }
}
