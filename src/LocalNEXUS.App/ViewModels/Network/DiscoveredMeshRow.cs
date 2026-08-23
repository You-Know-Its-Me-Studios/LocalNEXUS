using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Services.Distributed;

namespace LocalNEXUS.App.ViewModels.Network;

/// <summary>
/// A mesh from the public directory, as a line in the network table.
/// </summary>
/// <remarks>
/// Everything it can say comes from a directory entry, which is a mesh describing itself to
/// strangers. That is enough to say how big it is, how many machines are in it and what it serves,
/// and nothing at all about how any of that is assembled: coverage, context, throughput and the
/// rest belong to a mesh you have joined. They read as not reported rather than as zero, because
/// zero is a measurement and this is an absence of one.
/// </remarks>
public sealed partial class DiscoveredMeshRow : ObservableObject, INetworkRow
{
    private const string Unreported = "not reported";

    public DiscoveredMeshRow(DiscoveredMesh mesh) => Mesh = mesh;

    /// <summary>The directory entry this row draws.</summary>
    public DiscoveredMesh Mesh { get; }

    /// <inheritdoc />
    public string Name => Mesh.DisplayName;

    /// <inheritdoc />
    public string ModelId => Mesh.ServingText;

    /// <inheritdoc />
    /// <remarks>The tag says what kind of row this is, because it is the one thing that sets it apart.</remarks>
    public string Quantisation => "mesh";

    /// <inheritdoc />
    /// <remarks>
    /// Its own state. A mesh in the directory is reachable in the sense that you could join it,
    /// which is not the sense the run path means, so it is never complete; and it is not starting
    /// either, because nothing about it is coming up. Saying starting made the status filter count
    /// seven meshes as seven things this machine was busy with.
    /// </remarks>
    public ModelAvailability Availability => ModelAvailability.NotJoined;

    /// <inheritdoc />
    public string StatusText => "not joined";

    /// <inheritdoc />
    public IReadOnlyList<SourceAssignment> Sections => Array.Empty<SourceAssignment>();

    /// <inheritdoc />
    public bool HasSections => false;

    /// <inheritdoc />
    public SectionCoverage Strength => SectionCoverage.Starting;

    /// <inheritdoc />
    public int SourceCount => Mesh.NodeCount;

    /// <inheritdoc />
    public int SpareCount => 0;

    /// <inheritdoc />
    public string SizeText => Mesh.CapacityText;

    /// <inheritdoc />
    public string ContextText => Unreported;

    /// <inheritdoc />
    /// <remarks>
    /// The models it serves, which is the whole reason to look at a mesh you are not in. The
    /// publisher and branch are dropped, because a table has one line and unsloth/Qwen3-8B-GGUF
    /// at main colon Q4_K_M is mostly not the model's name.
    /// </remarks>
    public string ContentsText => Mesh.ServingText;

    /// <inheritdoc />
    public string LastVerifiedText => Mesh.Freshness.Length > 0 ? Mesh.Freshness : Unreported;

    /// <inheritdoc />
    /// <remarks>Listed publicly and still gated, which is exactly what the lock means.</remarks>
    public bool IsInviteOnly => true;

    /// <inheritdoc />
    public ModelSharing Sharing => ModelSharing.Public;

    /// <inheritdoc />
    /// <remarks>Unknowable from a listing, so it is not claimed either way and the filter keeps it.</remarks>
    public bool LooksLikeGguf => true;

    /// <inheritdoc />
    public bool IsDiscovered => true;

    /// <inheritdoc />
    public object InspectorTarget => Mesh;

    /// <inheritdoc />
    public IComparable? SortKey(ModelColumn column) => column switch
    {
        ModelColumn.Name => Name,
        ModelColumn.Sources => SourceCount,
        ModelColumn.Spare => SpareCount,
        ModelColumn.Status => StatusText,
        _ => Name
    };

    /// <summary>What the mesh list shows for this row.</summary>
    public string RowTitle => Mesh.DisplayName;

    /// <summary>The second half of the row: how big it is and what it runs.</summary>
    public string RowDetail => $"{Mesh.CapacityText} · {Mesh.NodeCount} of them · {Mesh.ServingText}";

    /// <summary>
    /// Grey for a mesh that is running, and the working colour for one that is asking for help.
    /// </summary>
    /// <remarks>
    /// A mesh short of a model is the one worth joining, so it is the one that gets a colour. Both
    /// are neutral facts rather than problems, which is why neither is a warning.
    /// </remarks>
    public string RowStateBrushKey => Mesh.IsLookingForModels
        ? "ModelAvailability.Starting.Brush"
        : "ModelAvailability.NotJoined.Brush";

    /// <summary>Joining is the only thing to do to a mesh you found.</summary>
    public bool HasRowAction => true;

    /// <summary>What that action says.</summary>
    public string RowActionText => "Join";

    /// <inheritdoc />
    /// <remarks>Nothing here depends on this install's mesh, so there is nothing to re-read.</remarks>
    public void RefreshMeshState()
    {
    }
}
