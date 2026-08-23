namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// One reading of the mesh, taken from the node's management and model APIs together.
/// </summary>
/// <remarks>
/// A snapshot is immutable and describes the mesh at a moment. The manager diffs consecutive
/// snapshots into its observable state, which keeps the reconciliation in one place and keeps
/// everything that renders the mesh free of any knowledge of the engine's wire format.
/// </remarks>
/// <param name="NodeId">This node's own id, as the engine reports it.</param>
/// <param name="NodeState">The engine's word for what this node is doing.</param>
/// <param name="MeshName">Friendly name of the mesh this node is in.</param>
/// <param name="InviteToken">The token another machine needs to join this mesh.</param>
/// <param name="PublicationState">Whether the mesh is private, public, or failed to publish.</param>
/// <param name="IsServing">True when this node offers its own compute rather than only routing.</param>
/// <param name="ThisMachineName">Hostname the engine announces for this machine.</param>
/// <param name="ThisMachineMemoryMb">Memory this machine announces to the mesh, in MiB.</param>
/// <param name="Peers">Every other node the mesh currently reports.</param>
/// <param name="Models">Models this node can route to right now.</param>
/// <param name="AnnouncedModelIds">Model ids peers announce, whether or not they are routable here.</param>
/// <param name="Stages">Placed stages of every split model the mesh has planned.</param>
/// <param name="DaemonState">The runtime's own word for what it is doing: standby, loading or serving.</param>
/// <param name="LlamaReady">True once a local model runtime is up and able to answer.</param>
/// <param name="IsClient">True when this node has attached to a mesh as a consumer.</param>
/// <param name="NostrDiscovery">True when the node is listed on the public relays.</param>
public sealed record MeshSnapshot(
    string NodeId,
    string NodeState,
    string MeshName,
    string InviteToken,
    string PublicationState,
    bool IsServing,
    string ThisMachineName,
    long ThisMachineMemoryMb,
    IReadOnlyList<MeshPeer> Peers,
    IReadOnlyList<MeshModel> Models,
    IReadOnlyList<string> AnnouncedModelIds,
    IReadOnlyList<MeshStage> Stages,
    string DaemonState = "",
    bool LlamaReady = false,
    bool IsClient = false,
    bool NostrDiscovery = false);

/// <summary>A node in the mesh other than this one, exactly as the engine reports it.</summary>
/// <param name="Id">The peer's public key, which is its stable identity.</param>
/// <param name="DisplayName">Label the peer announces, or its short id when it announces none.</param>
/// <param name="State">The engine's word for the peer's condition.</param>
/// <param name="Role">Whether the peer serves models or only routes.</param>
/// <param name="MemoryMb">Memory the peer announces, zero when it announces none.</param>
/// <param name="RoundTripMs">Latency the mesh last measured to this peer.</param>
/// <param name="ServingModelIds">Models this peer is serving.</param>
/// <param name="Version">Engine version the peer runs.</param>
public sealed record MeshPeer(
    string Id,
    string DisplayName,
    string State,
    string Role,
    long MemoryMb,
    int? RoundTripMs,
    IReadOnlyList<string> ServingModelIds,
    string Version);

/// <summary>A model the mesh can currently route to, with the metadata the engine reports.</summary>
/// <param name="Id">The id sent as the model field on a request.</param>
/// <param name="Quantization">Quantization label.</param>
/// <param name="LayerCount">Transformer layer count, which is what stages divide.</param>
/// <param name="ParameterSize">Parameter count as the engine words it.</param>
/// <param name="ContextLength">Context window the model was loaded with.</param>
public sealed record MeshModel(
    string Id,
    string Quantization,
    int LayerCount,
    string ParameterSize,
    int ContextLength);

/// <summary>
/// One placed stage of a split model. Layer bounds are already inclusive here; the engine's
/// half open ranges are converted where the report is read.
/// </summary>
/// <param name="ModelId">The model this stage belongs to.</param>
/// <param name="StageIndex">Position in the pipeline, starting at zero.</param>
/// <param name="NodeId">The peer holding this stage, as a full public key.</param>
/// <param name="FirstLayer">First layer, inclusive.</param>
/// <param name="LastLayer">Last layer, inclusive.</param>
/// <param name="State">The engine's word for the stage state, for example ready or stopped.</param>
public sealed record MeshStage(
    string ModelId,
    int StageIndex,
    string NodeId,
    int FirstLayer,
    int LastLayer,
    string State);
