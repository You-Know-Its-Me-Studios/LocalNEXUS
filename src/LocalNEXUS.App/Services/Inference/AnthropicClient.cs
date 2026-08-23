using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// Talks to Anthropic's messages API.
/// </summary>
/// <remarks>
/// Hand written rather than taken from the official package, for three reasons. The slice of that
/// API this application uses is streaming text and a token count, which is far narrower than the
/// surface an SDK brings and about the same work to map onto our own types. Gemini has no
/// official package at all, so writing one adapter by hand and one against an SDK would leave two
/// different shapes in the same folder. And the argument that usually settles it, that an SDK
/// absorbs breaking changes, is already handled here by the version header below, which pins the
/// wire format so it cannot move underneath us.
///
/// Three things about this API differ from the OpenAI shape and each is a trap:
/// the system prompt is a top level field rather than a message, max_tokens is required rather
/// than optional, and the usage numbers arrive in two different events.
/// </remarks>
public sealed class AnthropicClient : IModelClient, IDisposable
{
    private const string DataPrefix = "data:";

    /// <summary>
    /// The wire format this adapter was written against.
    /// </summary>
    /// <remarks>
    /// Anthropic versions their API by header, so this value is what guarantees the shapes parsed
    /// below keep arriving. Changing it means re-reading the streaming documentation, not just
    /// bumping a number.
    /// </remarks>
    private const string ApiVersion = "2023-06-01";

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public AnthropicClient()
        : this(OpenAiCompatibleClient.CreateDefaultHttpClient(), ownsHttpClient: true)
    {
    }

    public AnthropicClient(HttpClient http, bool ownsHttpClient = false)
    {
        _http = http;
        _ownsHttpClient = ownsHttpClient;
    }

    /// <inheritdoc />
    public Task<ChatCompletionResult> StreamChatAsync(
        ModelEndpoint endpoint,
        string systemPrompt,
        string userContent,
        double temperature,
        int maxTokens,
        IProgress<string>? onToken,
        CancellationToken ct)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(ChatMessage.System(systemPrompt));
        }

        messages.Add(ChatMessage.User(userContent));

        return StreamChatAsync(endpoint, messages, null, temperature, maxTokens, onToken, ct);
    }

    /// <inheritdoc />
    public async Task<ChatCompletionResult> StreamChatAsync(
        ModelEndpoint endpoint,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        double temperature,
        int maxTokens,
        IProgress<string>? onToken,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (string.IsNullOrWhiteSpace(endpoint.ModelId))
        {
            throw new ModelClientException("No model is selected for this node.");
        }

        if (!endpoint.RequiresAuthorization)
        {
            throw new ModelClientException(
                "Anthropic needs an API key. Add one in Settings under API keys, then run again.");
        }

        // Required by this API, unlike the OpenAI shape where it is optional. Caught here so the
        // answer is a sentence about the node rather than a four hundred from a server, which
        // somebody would then have to decode.
        if (maxTokens <= 0)
        {
            throw new ModelClientException(
                "Anthropic needs a maximum reply length and this node has none set. " +
                "Set max tokens on the node to something above zero.");
        }

        using var request = BuildRequest(endpoint, messages, tools, temperature, maxTokens);

        var stopwatch = Stopwatch.StartNew();

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await ReadBodySafelyAsync(response, ct).ConfigureAwait(false);
            throw new ModelClientException(
                $"{(int)response.StatusCode} {response.ReasonPhrase} from {endpoint.SafeUrlFor(endpoint.MessagesUrl)}. {body}".TrimEnd());
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var accumulated = new StringBuilder();
        int? promptTokens = null;
        int? completionTokens = null;
        string? finishReason = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);

            if (line is null)
            {
                break;
            }

            // Only the data lines matter. The event: lines name the same type that the payload
            // carries in its own "type" field, so parsing one of the two is enough.
            if (!line.StartsWith(DataPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var payload = line[DataPrefix.Length..].Trim();

            if (payload.Length == 0)
            {
                continue;
            }

            if (ReadEvent(payload, accumulated, onToken, ref promptTokens, ref completionTokens, ref finishReason))
            {
                break;
            }
        }

        stopwatch.Stop();

        return new ChatCompletionResult(
            accumulated.ToString(),
            promptTokens,
            completionTokens,
            stopwatch.Elapsed,
            finishReason);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    /// <summary>
    /// Reads one streamed event.
    /// </summary>
    /// <returns>True when the stream is finished.</returns>
    private static bool ReadEvent(
        string payload,
        StringBuilder accumulated,
        IProgress<string>? onToken,
        ref int? promptTokens,
        ref int? completionTokens,
        ref string? finishReason)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            // A partial frame is not worth failing a run over.
            return false;
        }

        using (document)
        {
            var root = document.RootElement;

            if (!root.TryGetProperty("type", out var typeValue) || typeValue.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            switch (typeValue.GetString())
            {
                case "message_start":
                    // Input tokens arrive once, here, and nowhere else in the stream.
                    if (root.TryGetProperty("message", out var message)
                        && message.TryGetProperty("usage", out var startUsage))
                    {
                        promptTokens = ReadInt(startUsage, "input_tokens") ?? promptTokens;
                        completionTokens = ReadInt(startUsage, "output_tokens") ?? completionTokens;
                    }

                    return false;

                case "content_block_delta":
                    if (root.TryGetProperty("delta", out var delta)
                        && delta.TryGetProperty("type", out var deltaType)
                        && deltaType.ValueKind == JsonValueKind.String
                        && deltaType.GetString() == "text_delta"
                        && delta.TryGetProperty("text", out var text)
                        && text.ValueKind == JsonValueKind.String)
                    {
                        var chunk = text.GetString();

                        if (!string.IsNullOrEmpty(chunk))
                        {
                            accumulated.Append(chunk);
                            onToken?.Report(chunk);
                        }
                    }

                    return false;

                case "message_delta":
                    // ASSIGNED, NOT ADDED, AND THIS IS DELIBERATE.
                    //
                    // The output_tokens in a message_delta is the running total for the whole
                    // message, not the count for this event. Anthropic's streaming documentation
                    // says so explicitly. Summing these, which is the obvious reading and what
                    // the equivalent OpenAI code would do, inflates the number every time and
                    // therefore inflates the cost shown to the user.
                    if (root.TryGetProperty("usage", out var deltaUsage))
                    {
                        completionTokens = ReadInt(deltaUsage, "output_tokens") ?? completionTokens;
                    }

                    if (root.TryGetProperty("delta", out var topDelta)
                        && topDelta.TryGetProperty("stop_reason", out var stop)
                        && stop.ValueKind == JsonValueKind.String)
                    {
                        finishReason = stop.GetString();
                    }

                    return false;

                case "message_stop":
                    return true;

                case "error":
                    throw new ModelClientException(ReadErrorMessage(root));

                default:
                    // ping, content_block_start and content_block_stop carry nothing this needs.
                    return false;
            }
        }
    }

    private static HttpRequestMessage BuildRequest(
        ModelEndpoint endpoint,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        double temperature,
        int maxTokens)
    {
        var body = BuildRequestBody(endpoint.ModelId, messages, tools, temperature, maxTokens);

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint.MessagesUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        // The key is a header of its own here rather than a bearer token.
        request.Headers.TryAddWithoutValidation("x-api-key", endpoint.ApiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", ApiVersion);

        return request;
    }

    private static string BuildRequestBody(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        double temperature,
        int maxTokens)
    {
        var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("model", modelId);
            writer.WriteNumber("max_tokens", maxTokens);
            writer.WriteNumber("temperature", temperature);
            writer.WriteBoolean("stream", true);

            // The system prompt is a top level field here. Sent as a message with role system,
            // which is what the OpenAI shape wants, this API rejects the request.
            var system = string.Join(
                "\n\n",
                messages.Where(m => m.Role == "system" && !string.IsNullOrWhiteSpace(m.Content)).Select(m => m.Content));

            if (!string.IsNullOrWhiteSpace(system))
            {
                writer.WriteString("system", system);
            }

            writer.WriteStartArray("messages");

            foreach (var message in messages.Where(m => m.Role != "system"))
            {
                WriteMessage(writer, message);
            }

            writer.WriteEndArray();

            if (tools is { Count: > 0 })
            {
                writer.WriteStartArray("tools");

                foreach (var tool in tools)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", tool.Name);
                    writer.WriteString("description", tool.Description);
                    writer.WritePropertyName("input_schema");

                    if (tool.ParametersSchema is { } schema)
                    {
                        schema.WriteTo(writer);
                    }
                    else
                    {
                        writer.WriteStartObject();
                        writer.WriteString("type", "object");
                        writer.WriteStartObject("properties");
                        writer.WriteEndObject();
                        writer.WriteEndObject();
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteMessage(Utf8JsonWriter writer, ChatMessage message)
    {
        writer.WriteStartObject();

        // A tool result is a user turn carrying a tool_result block, rather than a role of its
        // own as it is in the OpenAI shape.
        if (message.ToolCallId is { } toolCallId)
        {
            writer.WriteString("role", "user");
            writer.WriteStartArray("content");
            writer.WriteStartObject();
            writer.WriteString("type", "tool_result");
            writer.WriteString("tool_use_id", toolCallId);
            writer.WriteString("content", message.Content ?? string.Empty);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
            return;
        }

        writer.WriteString("role", message.Role == "assistant" ? "assistant" : "user");
        writer.WriteString("content", message.Content ?? string.Empty);
        writer.WriteEndObject();
    }

    private static int? ReadInt(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.TryGetInt32(out var parsed) ? parsed : null;

    private static string ReadErrorMessage(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error))
        {
            if (error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? "Anthropic reported an error with no message.";
            }

            return error.ToString();
        }

        return "Anthropic reported an error with no message.";
    }

    private static async Task<string> ReadBodySafelyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return body.Length <= 600 ? body : body[..600] + "...";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            return string.Empty;
        }
    }
}
