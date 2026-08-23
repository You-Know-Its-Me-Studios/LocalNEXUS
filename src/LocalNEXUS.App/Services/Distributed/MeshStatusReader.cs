using System.Net.Http;
using System.Text.Json;

namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// Reads the mesh node's management and model APIs and maps them to a <see cref="MeshSnapshot"/>.
/// </summary>
/// <remarks>
/// Every quirk of the engine's wire format is contained here: that peers are reported by a
/// shortened key while stage placements carry the full one, that stage layer ranges are half
/// open, and that a model becomes routable only once every stage behind it is ready. Nothing
/// downstream should ever need to know any of that.
/// </remarks>
public sealed class MeshStatusReader : IDisposable
{
    private static readonly JsonDocumentOptions ParseOptions = new() { AllowTrailingCommas = true };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private bool _disposed;

    /// <summary>
    /// Takes one reading. Returns null when the node is not answering yet, which is an ordinary
    /// condition while it starts rather than an error.
    /// </summary>
    public async Task<MeshSnapshot?> ReadAsync(int consolePort, int apiPort, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var status = await TryGetAsync($"http://127.0.0.1:{consolePort}/api/status", ct).ConfigureAwait(false);
        if (status is null)
        {
            return null;
        }

        using (status)
        {
            var stagesDocument = await TryGetAsync($"http://127.0.0.1:{consolePort}/api/runtime/stages", ct).ConfigureAwait(false);
            var modelsDocument = await TryGetAsync($"http://127.0.0.1:{apiPort}/v1/models", ct).ConfigureAwait(false);

            try
            {
                var root = status.RootElement;
                var peers = ReadPeers(root);

                return new MeshSnapshot(
                    NodeId: ReadString(root, "node_id"),
                    NodeState: ReadString(root, "node_state"),
                    MeshName: ReadString(root, "mesh_name"),
                    InviteToken: ReadString(root, "token"),
                    PublicationState: ReadString(root, "publication_state"),
                    IsServing: ReadBool(root, "is_host") || ReadStringList(root, "serving_models").Count > 0,
                    ThisMachineName: ReadString(root, "my_hostname"),
                    ThisMachineMemoryMb: (long)(ReadDouble(root, "my_vram_gb") * 1024d),
                    Peers: peers,
                    Models: ReadModels(modelsDocument),
                    AnnouncedModelIds: ReadAnnouncedModelIds(root, peers),
                    Stages: ReadStages(stagesDocument),

                    // How far the node has got, in the engine's own words, so a row can say which
                    // part of joining is happening rather than the single word "connecting".
                    DaemonState: root.TryGetProperty("runtime", out var runtime)
                        ? ReadString(runtime, "daemon_state")
                        : string.Empty,
                    LlamaReady: ReadBool(root, "llama_ready"),
                    IsClient: ReadBool(root, "is_client"),

                    // The other half of what makes a mesh public. A node that has registered with
                    // the relays says so here, and its publication state can still be reporting
                    // the transition.
                    NostrDiscovery: ReadBool(root, "nostr_discovery"),

                    // The files this node was asked to serve. The mesh names a local model by the
                    // hash of its contents, so this is the only thing that can put a readable name
                    // on one.
                    RequestedModelPaths: ReadStringList(root, "requested_models"));
            }
            finally
            {
                stagesDocument?.Dispose();
                modelsDocument?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _http.Dispose();
    }

    private async Task<JsonDocument?> TryGetAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonDocument.Parse(body, ParseOptions);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // A node that is still loading, or one that has just exited, is not an error here.
            return null;
        }
    }

    private static IReadOnlyList<MeshPeer> ReadPeers(JsonElement root)
    {
        if (!root.TryGetProperty("peers", out var peers) || peers.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MeshPeer>();
        }

        var result = new List<MeshPeer>(peers.GetArrayLength());

        foreach (var peer in peers.EnumerateArray())
        {
            var id = ReadString(peer, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var serving = ReadStringList(peer, "serving_models");
            if (serving.Count == 0)
            {
                serving = ReadStringList(peer, "hosted_models");
            }

            var name = FirstNonEmpty(
                ReadString(peer, "name"),
                ReadString(peer, "display_name"),
                ReadString(peer, "hostname"),
                id);

            result.Add(new MeshPeer(
                Id: id,
                DisplayName: name,
                State: ReadString(peer, "state"),
                Role: ReadString(peer, "role"),
                MemoryMb: (long)(ReadDouble(peer, "vram_gb") * 1024d),
                RoundTripMs: ReadNullableInt(peer, "rtt_ms"),
                ServingModelIds: serving,
                Version: ReadString(peer, "version")));
        }

        return result;
    }

    private static IReadOnlyList<MeshModel> ReadModels(JsonDocument? models)
    {
        if (models is null
            || !models.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MeshModel>();
        }

        var result = new List<MeshModel>(data.GetArrayLength());

        foreach (var model in data.EnumerateArray())
        {
            var id = ReadString(model, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var hasMetadata = model.TryGetProperty("metadata", out var metadata)
                              && metadata.ValueKind == JsonValueKind.Object;

            result.Add(new MeshModel(
                Id: id,
                Quantization: hasMetadata ? FirstNonEmpty(ReadString(metadata, "quant"), "unknown") : "unknown",
                LayerCount: hasMetadata ? ReadInt(metadata, "layer_count") : 0,
                ParameterSize: hasMetadata ? ReadString(metadata, "parameter_size") : string.Empty,
                ContextLength: hasMetadata ? ReadInt(metadata, "context_length") : 0));
        }

        return result;
    }

    private static IReadOnlyList<string> ReadAnnouncedModelIds(JsonElement root, IReadOnlyList<MeshPeer> peers)
    {
        var announced = new List<string>();
        announced.AddRange(ReadStringList(root, "serving_models"));
        announced.AddRange(ReadStringList(root, "hosted_models"));

        foreach (var peer in peers)
        {
            announced.AddRange(peer.ServingModelIds);
        }

        return announced
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Builds the placed stages. Placement comes from the reported topologies, which are the
    /// only view carrying every stage of a model rather than just the ones this node runs, and
    /// each stage's state is matched in from the local status entries where one exists.
    /// </summary>
    private static IReadOnlyList<MeshStage> ReadStages(JsonDocument? stages)
    {
        if (stages is null)
        {
            return Array.Empty<MeshStage>();
        }

        var root = stages.RootElement;
        var states = ReadStageStates(root);
        var result = new List<MeshStage>();

        if (!root.TryGetProperty("topologies", out var topologies) || topologies.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var topology in topologies.EnumerateArray())
        {
            var modelId = ReadString(topology, "model_id");
            if (string.IsNullOrWhiteSpace(modelId)
                || !topology.TryGetProperty("stages", out var stageArray)
                || stageArray.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var stage in stageArray.EnumerateArray())
            {
                var index = ReadInt(stage, "stage_index");
                var start = ReadInt(stage, "layer_start");
                var end = ReadInt(stage, "layer_end");

                result.Add(new MeshStage(
                    ModelId: modelId,
                    StageIndex: index,
                    NodeId: ReadString(stage, "node_id"),
                    FirstLayer: start,

                    // Engine ranges are half open. This is the one place that is true.
                    LastLayer: Math.Max(start, end - 1),
                    State: states.TryGetValue((modelId, index), out var state) ? state : string.Empty));
            }
        }

        return result
            .OrderBy(s => s.ModelId, StringComparer.Ordinal)
            .ThenBy(s => s.StageIndex)
            .ToList();
    }

    private static Dictionary<(string ModelId, int StageIndex), string> ReadStageStates(JsonElement root)
    {
        var states = new Dictionary<(string, int), string>();

        foreach (var property in new[] { "statuses", "stages" })
        {
            if (!root.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var entry in array.EnumerateArray())
            {
                var modelId = ReadString(entry, "model_id");
                var state = ReadString(entry, "state");

                if (!string.IsNullOrWhiteSpace(modelId) && !string.IsNullOrWhiteSpace(state))
                {
                    // A later entry wins: the status list is the more current of the two.
                    states[(modelId, ReadInt(entry, "stage_index"))] = state;
                }
            }
        }

        return states;
    }

    private static string FirstNonEmpty(params string[] candidates)
        => candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? string.Empty;

    private static string ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool ReadBool(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static int ReadInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var parsed)
            ? parsed
            : 0;

    private static int? ReadNullableInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static double ReadDouble(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetDouble(out var parsed)
            ? parsed
            : 0d;

    private static IReadOnlyList<string> ReadStringList(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>(array.GetArrayLength());

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } value)
            {
                result.Add(value);
            }
        }

        return result;
    }
}
