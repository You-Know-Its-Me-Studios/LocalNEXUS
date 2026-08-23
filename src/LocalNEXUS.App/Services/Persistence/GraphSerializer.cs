using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;

namespace LocalNEXUS.App.Services.Persistence;

/// <summary>
/// Reads and writes graphs as JSON.
/// </summary>
/// <remarks>
/// Pin identifiers are part of the saved document. Connections refer to pins by identifier, and
/// a freshly constructed node generates new ones, so the identifiers recorded at save time are
/// pushed back onto the reconstructed pins before connections are rebuilt. Without that step a
/// loaded graph would come back with its nodes but none of its wires.
/// </remarks>
public sealed class GraphSerializer
{
    /// <summary>Version stamped into saved files so future format changes can be detected.</summary>
    public const int FormatVersion = 1;

    /// <summary>Extension used for saved graphs.</summary>
    public const string FileExtension = ".nexusgraph.json";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly NodeFactory _factory;

    public GraphSerializer(NodeFactory factory) => _factory = factory;

    /// <summary>Writes the graph to <paramref name="path"/>, creating the folder if needed.</summary>
    public void Save(GraphModel graph, string path)
    {
        ArgumentNullException.ThrowIfNull(graph);
        Save(graph, path, graph.Id);
    }

    /// <summary>
    /// Writes the graph under an identity other than its own.
    /// </summary>
    /// <remarks>
    /// One caller, and it is saving a graph as a template. A template made from a graph is a
    /// second document rather than the same one in another folder, and letting it keep the
    /// identity would leave two files claiming to be the same graph with no way to tell which a
    /// project meant.
    /// </remarks>
    public void Save(GraphModel graph, string path, Guid identity)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new JsonObject
        {
            ["version"] = FormatVersion,
            ["id"] = identity.ToString(),
            ["nodes"] = SerializeNodes(graph),
            ["connections"] = SerializeConnections(graph)
        };

        File.WriteAllText(path, document.ToJsonString(WriteOptions));
    }

    /// <summary>
    /// Replaces the contents of <paramref name="target"/> with the graph stored at
    /// <paramref name="path"/>. The graph instance itself is reused so that existing bindings
    /// keep working.
    /// </summary>
    /// <returns>Warnings for anything in the file that could not be restored.</returns>
    /// <exception cref="InvalidDataException">The file is not a graph this build can read.</exception>
    /// <summary>
    /// What the last load brought up to date, which is news rather than a problem.
    /// </summary>
    /// <remarks>
    /// Separate from the warnings because they mean opposite things. A warning is something the
    /// document lost; this is something it gained, and reporting the two the same way would put a
    /// successful upgrade in red beside a dropped connection.
    /// </remarks>
    public IReadOnlyList<string> Migrations { get; private set; } = Array.Empty<string>();

    public IReadOnlyList<string> LoadInto(GraphModel target, string path)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var json = File.ReadAllText(path);
        var document = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidDataException($"{Path.GetFileName(path)} is not a graph file.");

        var version = document["version"]?.GetValue<int>() ?? 0;
        if (version > FormatVersion)
        {
            throw new InvalidDataException(
                $"{Path.GetFileName(path)} was saved by a newer version of LocalNEXUS (format {version}).");
        }

        var warnings = new List<string>();

        target.Clear();

        var renames = new List<string>();

        // Before the nodes, because clearing the graph mints a fresh identity for the empty canvas
        // and the one in the file has to win. A file saved before graphs had identities is given
        // one here rather than refused, and it is only on disk once the graph is next saved.
        var idText = document["id"]?.GetValueKind() == JsonValueKind.String
            ? document["id"]!.GetValue<string>()
            : null;

        if (Guid.TryParse(idText, out var id))
        {
            target.Id = id;
        }
        else
        {
            renames.Add($"{Path.GetFileName(path)} had no identifier, so it was given one.");
        }

        var restored = RestoreNodes(document["nodes"] as JsonArray, target, warnings, renames);
        RestoreConnections(document["connections"] as JsonArray, target, restored, warnings, renames);

        // Last, once the graph is whole. A migration reads wires, so it cannot run before they
        // exist, and anything it adds is a real connection rather than a special case for the
        // executor to know about.
        //
        // Deliberately not added to the warnings. A warning is reported as an error, and a graph
        // that opened correctly and was brought up to date has nothing wrong with it. Saying so in
        // red would teach somebody to ignore the one thing that means a wire was lost.
        renames.AddRange(GraphMigrations.Apply(target));
        Migrations = renames;

        return warnings;
    }

    private static JsonArray SerializeNodes(GraphModel graph)
    {
        var nodes = new JsonArray();

        foreach (var node in graph.Nodes)
        {
            nodes.Add(new JsonObject
            {
                ["type"] = node.TypeKey,
                ["id"] = node.Id.ToString(),
                ["title"] = node.Title,
                ["x"] = node.X,
                ["y"] = node.Y,
                ["inputs"] = SerializePins(node.Inputs),
                ["outputs"] = SerializePins(node.Outputs),
                ["settings"] = node.SaveSettings()
            });
        }

        return nodes;
    }

    private static JsonArray SerializePins(IEnumerable<Pin> pins)
    {
        var array = new JsonArray();

        foreach (var pin in pins)
        {
            array.Add(new JsonObject
            {
                ["name"] = pin.Name,
                ["id"] = pin.Id.ToString(),

                // Written for the sake of a node this build cannot construct. Every other node
                // rebuilds its own pins and their types from code and ignores this, but a
                // placeholder has only the file to go on, and a placeholder that guessed Text
                // could not accept the Code wire it was holding.
                ["type"] = pin.PinType.ToString()
            });
        }

        return array;
    }

    private static JsonArray SerializeConnections(GraphModel graph)
    {
        var array = new JsonArray();

        foreach (var connection in graph.Connections)
        {
            array.Add(new JsonObject
            {
                ["sourceNodeId"] = connection.SourceNodeId.ToString(),
                ["sourcePinId"] = connection.SourcePinId.ToString(),
                ["targetNodeId"] = connection.TargetNodeId.ToString(),
                ["targetPinId"] = connection.TargetPinId.ToString(),

                // Written always rather than only when set, because a wire that quietly loses its
                // breakpoint between sessions is a wire somebody will set again and again.
                ["breakpoint"] = connection.HasBreakpoint
            });
        }

        return array;
    }

    private Dictionary<Guid, Pin> RestoreNodes(
        JsonArray? nodes,
        GraphModel target,
        List<string> warnings,
        List<string> renames)
    {
        var pinsById = new Dictionary<Guid, Pin>();

        if (nodes is null)
        {
            return pinsById;
        }

        foreach (var element in nodes.OfType<JsonObject>())
        {
            var typeKey = element["type"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(typeKey))
            {
                warnings.Add("Skipped a node with no type.");
                continue;
            }

            NodeBase node;
            try
            {
                node = _factory.Create(typeKey);
            }
            catch (NotSupportedException)
            {
                // Not skipped. A type key nobody owns almost always means an extension that is
                // not installed here, and dropping the node would take its wires with it and
                // then write the hole back out on the next save. The placeholder keeps
                // everything and refuses to run, so installing the extension restores the graph.
                var placeholder = NodeFactory.CreateUnavailable(typeKey);
                placeholder.AdoptSavedPins(element["inputs"] as JsonArray, element["outputs"] as JsonArray);
                node = placeholder;

                warnings.Add(
                    $"'{typeKey}' is not installed for this project, so that node is being held as a placeholder. " +
                    "Its settings and wires are kept.");
            }

            if (Guid.TryParse(element["id"]?.GetValue<string>(), out var id))
            {
                node.Id = id;
            }

            node.Title = element["title"]?.GetValue<string>() ?? node.Title;
            node.X = element["x"]?.GetValue<double>() ?? 0d;
            node.Y = element["y"]?.GetValue<double>() ?? 0d;

            // A key that resolved to a node calling itself something else is a rename this build
            // knows about. Noted rather than warned about: the graph opened whole, and the new key
            // is what the next save writes. Nothing here names a node type, so the next rename
            // reports itself without this being touched.
            if (!string.Equals(typeKey, node.TypeKey, StringComparison.Ordinal) && node is not UnavailableNode)
            {
                renames.Add($"{typeKey} is now called {node.TypeKey}. Saving this graph writes the new name.");
            }

            RestorePinIds(node.Inputs, element["inputs"] as JsonArray);
            RestorePinIds(node.Outputs, element["outputs"] as JsonArray);

            if (element["settings"] is JsonObject settings)
            {
                try
                {
                    node.LoadSettings(settings);
                }
                catch (Exception ex) when (ex is FormatException or InvalidOperationException or NotSupportedException)
                {
                    warnings.Add($"{node.Title}: settings could not be fully restored ({ex.Message}).");
                }
            }

            target.AddNode(node);

            foreach (var pin in node.Inputs.Concat(node.Outputs))
            {
                pinsById[pin.Id] = pin;
            }
        }

        return pinsById;
    }

    /// <summary>
    /// Pushes saved pin identifiers back onto freshly created pins. Matching is by name, with a
    /// positional fallback so that a renamed pin still finds its saved identity.
    /// </summary>
    private static void RestorePinIds(IList<Pin> pins, JsonArray? saved)
    {
        if (saved is null)
        {
            return;
        }

        var consumed = new HashSet<int>();

        for (var i = 0; i < pins.Count; i++)
        {
            var match = FindByName(saved, pins[i].Name, consumed) ?? FindByIndex(saved, i, consumed);

            if (match?["id"]?.GetValue<string>() is { } text && Guid.TryParse(text, out var id))
            {
                pins[i].Id = id;
            }
        }
    }

    private static JsonObject? FindByName(JsonArray saved, string name, HashSet<int> consumed)
    {
        for (var i = 0; i < saved.Count; i++)
        {
            if (consumed.Contains(i) || saved[i] is not JsonObject candidate)
            {
                continue;
            }

            if (string.Equals(candidate["name"]?.GetValue<string>(), name, StringComparison.Ordinal))
            {
                consumed.Add(i);
                return candidate;
            }
        }

        return null;
    }

    private static JsonObject? FindByIndex(JsonArray saved, int index, HashSet<int> consumed)
    {
        if (index >= saved.Count || consumed.Contains(index) || saved[index] is not JsonObject candidate)
        {
            return null;
        }

        consumed.Add(index);
        return candidate;
    }

    /// <summary>
    /// The pin a refused wire plainly meant, when there is one.
    /// </summary>
    /// <remarks>
    /// Only the one case, and only downwards: a Code output into a Text input, where the node it
    /// leaves has a Text output too. Anything cleverer would be guessing at what somebody drew.
    /// </remarks>
    private static Pin? Rerouted(Pin source, Pin target)
        => source.PinType == PinType.Code && target.PinType == PinType.Text
            ? source.Owner.Outputs.FirstOrDefault(p => p.PinType == PinType.Text)
            : null;

    private static void RestoreConnections(
        JsonArray? connections,
        GraphModel target,
        IReadOnlyDictionary<Guid, Pin> pinsById,
        List<string> warnings,
        List<string> moved)
    {
        if (connections is null)
        {
            return;
        }

        foreach (var element in connections.OfType<JsonObject>())
        {
            if (!Guid.TryParse(element["sourcePinId"]?.GetValue<string>(), out var sourceId)
                || !Guid.TryParse(element["targetPinId"]?.GetValue<string>(), out var targetId))
            {
                warnings.Add("Skipped a connection with malformed pin identifiers.");
                continue;
            }

            if (!pinsById.TryGetValue(sourceId, out var source) || !pinsById.TryGetValue(targetId, out var pinTarget))
            {
                warnings.Add("Skipped a connection whose pins are no longer present.");
                continue;
            }

            if (!target.TryConnect(source, pinTarget, out var reason))
            {
                // Code used to be allowed into Text, so a graph saved before that rule tightened
                // can hold a wire that is now refused. Where the node it came from has a text pin
                // of its own, that is plainly what the wire meant, so it is moved rather than
                // dropped and the move is reported as news rather than as a warning.
                if (Rerouted(source, pinTarget) is { } rerouted
                    && target.TryConnect(rerouted, pinTarget, out _))
                {
                    moved.Add($"{source.Owner.Title} to {pinTarget.Owner.Title} now leaves its {rerouted.Name} pin.");
                    continue;
                }

                warnings.Add($"Skipped the connection {source.Owner.Title} to {pinTarget.Owner.Title}: {reason}.");
                continue;
            }

            // Absent in anything saved before breakpoints existed, which reads as no breakpoint,
            // which is what those graphs meant.
            if (element["breakpoint"]?.GetValue<bool>() == true)
            {
                target.Connections[^1].HasBreakpoint = true;
            }
        }
    }
}
