using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LocalNEXUS.App.Services.Inference;

namespace LocalNEXUS.App.Services.Search;

/// <summary>
/// Embeds text on this machine, through the engine that already serves models.
/// </summary>
/// <remarks>
/// llama.cpp started with <c>--embeddings</c> answers the same OpenAI shaped endpoint everything
/// else here talks to, so this adds a mode rather than a dependency. Nothing is sent anywhere: the
/// server is a child process on loopback, exactly as it is for a completion.
///
/// The server is started on the first embedding and left running, which is the same arrangement
/// every other local model has. It is a separate server from any completion model, because
/// <c>--embeddings</c> is a different mode for the same weights and the two cannot share a
/// process.
///
/// The model is small by design. A run is a request and a handful of events, so what is being
/// compared is short, and a large embedding model would spend most of its cost on a context it
/// never uses.
/// </remarks>
public sealed class LocalEmbedder : IEmbedder
{
    /// <summary>
    /// The context an embedding server is started with.
    /// </summary>
    /// <remarks>
    /// Small on purpose. Embedding models are trained at a few hundred tokens and truncate past
    /// it, and what is being embedded here is a request and a summary rather than a document, so
    /// a larger window would reserve memory that is never used.
    /// </remarks>
    private const int EmbeddingContext = 512;

    private readonly LlamaServerManager _servers;
    private readonly HttpClient _http;
    private readonly string _modelPath;

    private int _dimensions;

    public LocalEmbedder(LlamaServerManager servers, HttpClient http, string modelPath)
    {
        _servers = servers;
        _http = http;
        _modelPath = modelPath;
    }

    /// <inheritdoc />
    /// <remarks>Zero until the first embedding comes back, because the model states it, not this.</remarks>
    public int Dimensions => _dimensions;

    /// <inheritdoc />
    public string ModelId => Path.GetFileNameWithoutExtension(_modelPath);

    /// <inheritdoc />
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<float>();
        }

        if (!File.Exists(_modelPath))
        {
            throw new EmbeddingUnavailableException(
                $"The embedding model is not where it was: {_modelPath}. Choose one again in "
                + "Settings, or turn semantic search off to go back to keyword search.");
        }

        var endpoint = await StartAsync(ct).ConfigureAwait(false);

        try
        {
            using var response = await _http
                .PostAsJsonAsync(
                    $"{endpoint}/embeddings",
                    new EmbeddingRequest(ModelId, text),
                    ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new EmbeddingUnavailableException(
                    $"The embedding model answered {(int)response.StatusCode}. It may not be an "
                    + "embedding model: a chat model loaded this way refuses every request.");
            }

            var answer = await response.Content
                .ReadFromJsonAsync<EmbeddingResponse>(ct)
                .ConfigureAwait(false);

            var vector = answer?.Data?.FirstOrDefault()?.Embedding;

            if (vector is null || vector.Length == 0)
            {
                throw new EmbeddingUnavailableException(
                    "The embedding model answered without a vector in it.");
            }

            _dimensions = vector.Length;

            return Normalise(vector);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new EmbeddingUnavailableException(
                $"The embedding model could not be reached: {ex.Message}", ex);
        }
    }

    private async Task<string> StartAsync(CancellationToken ct)
    {
        try
        {
            var endpoint = await _servers
                .EnsureServingAsync(
                    ModelFormatDetector.Describe(_modelPath),
                    new ModelRuntimeOptions { ContextSize = EmbeddingContext, Embeddings = true },
                    null,
                    ct)
                .ConfigureAwait(false);

            return endpoint.BaseUrl;
        }
        catch (ModelClientException ex)
        {
            throw new EmbeddingUnavailableException(
                $"The embedding model could not be started: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Scales a vector to unit length, so comparing two is a dot product.
    /// </summary>
    /// <remarks>
    /// Done once on the way in rather than on every comparison. A search compares the query
    /// against every stored run, so normalising at write time turns the per comparison cost from
    /// three passes into one.
    /// </remarks>
    public static float[] Normalise(float[] vector)
    {
        double sum = 0;

        foreach (var value in vector)
        {
            sum += (double)value * value;
        }

        if (sum <= 0)
        {
            return vector;
        }

        var length = Math.Sqrt(sum);
        var scaled = new float[vector.Length];

        for (var index = 0; index < vector.Length; index++)
        {
            scaled[index] = (float)(vector[index] / length);
        }

        return scaled;
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] string Input);

    private sealed class EmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingRow>? Data { get; set; }
    }

    private sealed class EmbeddingRow
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }
}
