using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Distributed;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The four things the top of the inspector says about whatever is selected, wherever it came
/// from.
/// </summary>
/// <remarks>
/// The inspector is one slot serving both sections, so its header has to work for a node, a
/// model, a machine and a coverage section without any of them knowing about it. This is the one
/// place that maps a selection onto the header, and everything below the header is a data
/// template picked by type, so a new kind of selectable thing is one entry here and one template.
///
/// The accent and the state are resolved to brush keys rather than to brushes, so the colours
/// still live in the theme and this stays a mapping rather than a palette.
/// </remarks>
/// <param name="Title">What the thing is called.</param>
/// <param name="TypeTag">One word for what kind of thing it is.</param>
/// <param name="StateText">What it is doing.</param>
/// <param name="AccentKey">Brush key for the bar down the leading edge.</param>
/// <param name="StateKey">Brush key for the state pill.</param>
public sealed record InspectorHeader(
    string Title,
    string TypeTag,
    string StateText,
    string AccentKey,
    string StateKey)
{
    /// <summary>The header for an empty slot, which is never drawn but is never null either.</summary>
    public static InspectorHeader None { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        "Accent.Neutral.Brush",
        "Accent.Neutral.Brush");

    /// <summary>Reads the header off whatever is selected.</summary>
    public static InspectorHeader For(object? selection) => selection switch
    {
        NodeBase node => new InspectorHeader(
            node.Title,
            NodeTypeLabel(node.TypeKey),
            node.State.ToString(),
            $"NodeType.{node.TypeKey}.Brush",
            $"NodeState.{node.State}.Brush"),

        Network.HostedMeshRow hosted => new InspectorHeader(
            hosted.DisplayName,
            "Hosting",
            hosted.StateText,
            "NodeType.Prompt.Brush",
            $"MeshNodeState.{hosted.State}.Brush"),

        JoinedMesh joined => new InspectorHeader(
            joined.DisplayName,
            "Joined",
            joined.StateText,
            "NodeType.Model.Brush",
            $"JoinState.{joined.State}.Brush"),

        DiscoveredMesh found => new InspectorHeader(
            found.DisplayName,
            "Found",
            "not joined",
            "Accent.Neutral.Brush",
            "ModelAvailability.NotJoined.Brush"),

        NetworkServedModel model => new InspectorHeader(
            model.Name,
            "Model",
            model.StatusText,
            "NodeType.Model.Brush",
            $"ModelAvailability.{model.Availability}.Brush"),

        InferenceSource source => new InspectorHeader(
            source.DisplayName,
            source.IsThisMachine ? "This machine" : "Machine",
            source.State.ToString(),
            $"SourceState.{source.State}.Brush",
            $"SourceState.{source.State}.Brush"),

        SourceAssignment section => new InspectorHeader(
            section.Section.HasKnownRange
                ? $"Layers {section.Section.FirstLayer}-{section.Section.LastLayer}"
                : $"Section {section.Section.Index + 1}",
            "Section",
            section.IsCovered ? "Covered" : section.IsBlocking ? "Uncovered" : "Starting",
            $"SectionCoverage.{section.Coverage}.Brush",
            $"SectionCoverage.{section.Coverage}.Brush"),

        _ => None
    };

    /// <summary>
    /// The type key as a person would say it, taken from the palette rather than spelled again.
    /// </summary>
    /// <remarks>
    /// This used to carry its own special case, which made it a fourth place a node type was
    /// named and the one that disagreed: it said Compile where the palette said Compile check.
    /// Reading the palette is what stops that happening again.
    /// </remarks>
    private static string NodeTypeLabel(string typeKey)
        => Nodes.NodeFactory.Descriptors.FirstOrDefault(d => d.TypeKey == typeKey).DisplayName ?? typeKey;
}
