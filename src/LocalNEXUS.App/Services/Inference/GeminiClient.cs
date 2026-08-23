using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// Talks to Google's generative language API.
/// </summary>
/// <remarks>
/// Hand written because there is no official C# package for this API. The Vertex client is a
/// different product with a different authentication model, and the community package is a
/// larger dependency than the adapter it would replace.
///
/// The thing to know about this API, and the reason for the care taken below: the key travels
/// as a query parameter rather than a header. A url built for this endpoint contains a
/// credential, so no url from here reaches a log, an exception message or the activity feed
/// without going through the endpoint's redaction first. Putting a key back into a text file
/// would undo the entire reason the credential store exists.
///
/// The other differences from the OpenAI shape: the assistant role is called model, the system
/// prompt is its own object rather than a message, and the reply is nested two levels deep in
/// candidates and parts.
/// </remarks>
public sealed class GeminiClient : IModelClient, IDisposable
{
    private const string DataPrefix = "data:";

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public GeminiClient()
        : this(OpenAiCompatibleClient.CreateDefaultHttpClient(), ownsHttpClient: true)
    {
    }

    public GeminiClient(HttpClient http, bool ownsHttpClient = false)
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
                "Gemini needs an API key. Add one in Settings under API keys, then run again.");
        }

        var url = endpoint.GeminiStreamUrl;

        // Everything a person could ever see says this instead. The real url stays in the request.
        var safeUrl = endpoint.SafeUrlFor(url);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                BuildRequestBody(messages, tools, temperature, maxTokens),
                Encoding.UTF8,
                "application/json")
        };

        var stopwatch = Stopwatch.StartNew();

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await ReadBodySafelyAsync(response, endpoint, ct).ConfigureAwait(false);
            throw new ModelClientException(
                $"{(int)response.StatusCode} {response.ReasonPhrase} from {safeUrl}. {body}".TrimEnd());
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

            if (!line.StartsWith(DataPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var payload = line[DataPrefix.Length..].Trim();

            if (payload.Length == 0)
            {
                continue;
            }

            ReadChunk(payload, accumulated, onToken, ref promptTokens, ref completionTokens, ref finishReason);
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

    private static void ReadChunk(
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
            return;
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                throw new ModelClientException(ReadErrorMessage(error));
            }

            if (root.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
            {
                foreach (var candidate in candidates.EnumerateArray())
                {
                    if (candidate.TryGetProperty("content", out var content)
                        && content.TryGetProperty("parts", out var parts)
                        && parts.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var part in parts.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                            {
                                var chunk = text.GetString();

                                if (!string.IsNullOrEmpty(chunk))
                                {
                                    accumulated.Append(chunk);
                                    onToken?.Report(chunk);
                                }
                            }
                        }
                    }

                    if (candidate.TryGetProperty("finishReason", out var reason) && reason.ValueKind == JsonValueKind.String)
                    {
                        finishReason = reason.GetString();
                    }
                }
            }

            // Usage arrives on chunks as the stream goes and is a running total for the request,
            // so the last one seen is the answer. Assigned rather than accumulated, for the same
            // reason as the Anthropic adapter.
            if (root.TryGetProperty("usageMetadata", out var usage))
            {
                promptTokens = ReadInt(usage, "promptTokenCount") ?? promptTokens;
                completionTokens = ReadInt(usage, "candidatesTokenCount") ?? completionTokens;
            }
        }
    }

    private static string BuildRequestBody(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        double temperature,
        int maxTokens)
    {
        var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            // Its own object rather than a message, like Anthropic and unlike OpenAI.
            var system = string.Join(
                "\n\n",
                messages.Where(m => m.Role == "system" && !string.IsNullOrWhiteSpace(m.Content)).Select(m => m.Content));

            if (!string.IsNullOrWhiteSpace(system))
            {
                writer.WriteStartObject("systemInstruction");
                writer.WriteStartArray("parts");
                writer.WriteStartObject();
                writer.WriteString("text", system);
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteStartArray("contents");

            foreach (var message in messages.Where(m => m.Role != "system"))
            {
                writer.WriteStartObject();

                // The assistant is called the model here, and a tool result is folded back in as
                // an ordinary user turn, which is the closest thing this shape has to one.
                writer.WriteString("role", message.Role == "assistant" ? "model" : "user");

                writer.WriteStartArray("parts");
                writer.WriteStartObject();
                writer.WriteString("text", message.Content ?? string.Empty);
                writer.WriteEndObject();
                writer.WriteEndArray();

                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            if (tools is { Count: > 0 })
            {
                writer.WriteStartArray("tools");
                writer.WriteStartObject();
                writer.WriteStartArray("functionDeclarations");

                foreach (var tool in tools)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", tool.Name);
                    writer.WriteString("description", tool.Description);

                    if (tool.ParametersSchema is { } schema)
                    {
                        writer.WritePropertyName("parameters");
                        schema.WriteTo(writer);
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.WriteEndArray();
            }

            writer.WriteStartObject("generationConfig");
            writer.WriteNumber("temperature", temperature);

            if (maxTokens > 0)
            {
                writer.WriteNumber("maxOutputTokens", maxTokens);
            }

            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static int? ReadInt(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.TryGetInt32(out var parsed) ? parsed : null;

    private static string ReadErrorMessage(JsonElement error)
    {
        if (error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
        {
            return message.GetString() ?? "Gemini reported an error with no message.";
        }

        return "Gemini reported an error with no message.";
    }

    /// <summary>
    /// Reads an error body, with the key taken out of it.
    /// </summary>
    /// <remarks>
    /// Google echoes the request url back in some error payloads, and that url carries the key.
    /// Redacted here as well as at the call site, because this text goes to the feed.
    /// </remarks>
    private static async Task<string> ReadBodySafelyAsync(
        HttpResponseMessage response,
        ModelEndpoint endpoint,
        CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var safe = endpoint.SafeUrlFor(body);
            return safe.Length <= 600 ? safe : safe[..600] + "...";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            return string.Empty;
        }
    }
}
