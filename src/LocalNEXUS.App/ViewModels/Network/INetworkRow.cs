using System.ComponentModel;
using LocalNEXUS.App.Services.Distributed;

namespace LocalNEXUS.App.ViewModels.Network;

/// <summary>
/// One line of the network table, whether it is a model in your mesh or a mesh you could join.
/// </summary>
/// <remarks>
/// The table exists to answer "what can I reach", and a mesh in the public directory is an answer
/// to that question with one more step attached. Keeping the two in separate lists would mean two
/// tables saying almost the same thing, and somebody looking for a model would have to know which
/// of the two to look in before they knew whether they had it.
///
/// So the columns are the interface, and the two kinds of row fill in what they can. A discovered
/// mesh honestly reports "not reported" for most of them, because a mesh you have not joined tells
/// you what it holds and nothing about how it holds it.
/// </remarks>
public interface INetworkRow : INotifyPropertyChanged
{
    /// <summary>What the row leads with.</summary>
    string Name { get; }

    /// <summary>The full identifier behind the name, for the tool tip.</summary>
    string ModelId { get; }

    /// <summary>The tag beside the name.</summary>
    string Quantisation { get; }

    /// <summary>Whether this can be used right now.</summary>
    ModelAvailability Availability { get; }

    /// <summary>The status badge.</summary>
    string StatusText { get; }

    /// <summary>Sections of the pipeline, which the coverage bar draws a segment for.</summary>
    IReadOnlyList<SourceAssignment> Sections { get; }

    /// <summary>True once there is a pipeline to draw.</summary>
    bool HasSections { get; }

    /// <summary>Colour of the weakest link, for the spare column.</summary>
    SectionCoverage Strength { get; }

    /// <summary>How many machines are involved.</summary>
    int SourceCount { get; }

    /// <summary>How many machines could take over a section.</summary>
    int SpareCount { get; }

    /// <summary>Size, or that it was not reported.</summary>
    string SizeText { get; }

    /// <summary>Context window, or that it was not reported.</summary>
    string ContextText { get; }

    /// <summary>Parameter count, or that it was not reported.</summary>
    string ParametersText { get; }

    /// <summary>Throughput, or that it was not reported.</summary>
    string ThroughputText { get; }

    /// <summary>When it was last seen.</summary>
    string LastVerifiedText { get; }

    /// <summary>Whether reaching this needs an invite.</summary>
    bool IsInviteOnly { get; }

    /// <summary>Public or invite only, for the filter group.</summary>
    ModelSharing Sharing { get; }

    /// <summary>Whether this looks like a GGUF, for the format filter.</summary>
    bool LooksLikeGguf { get; }

    /// <summary>True when this is a mesh from the directory rather than something already reachable.</summary>
    bool IsDiscovered { get; }

    /// <summary>What the inspector should show for this row.</summary>
    object InspectorTarget { get; }

    /// <summary>Sort key for one column, so ordering is done on values rather than on strings.</summary>
    IComparable? SortKey(ModelColumn column);

    /// <summary>Re-reads whatever depends on the mesh rather than on the row's own subject.</summary>
    void RefreshMeshState();
}
