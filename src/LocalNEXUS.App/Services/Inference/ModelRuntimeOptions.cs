namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// The per launch settings a node hands to whichever runtime serves its model.
/// </summary>
/// <remarks>
/// Deliberately the two settings a model node already exposes rather than a union of everything
/// every runtime accepts. A runtime that has no use for one of them ignores it, which is a
/// smaller cost than a settings panel whose fields change meaning with the file that was picked.
/// </remarks>
public sealed record ModelRuntimeOptions
{
    /// <summary>Context window requested. Passed straight through to llama-server.</summary>
    public int ContextSize { get; init; } = LlamaLaunchOptions.DefaultContextSize;

    /// <summary>Layers to offload to the GPU. Meaningful to llama.cpp; the Python runtime places the whole model.</summary>
    public int GpuLayers { get; init; } = LlamaLaunchOptions.DefaultGpuLayers;

    /// <summary>
    /// The multimodal projector to load beside the weights, for a vision model.
    /// </summary>
    /// <remarks>
    /// Null for every model but a vision one, and ignored by a runtime that has no use for it,
    /// which is the rule the other two already follow. It is here rather than reached for directly
    /// so that the vision path asks the resolver for a model the same way a node does, instead of
    /// naming llama.cpp and undoing what the resolver is for.
    /// </remarks>
    public string? ProjectorPath { get; init; }

    /// <summary>
    /// Serve embeddings rather than completions.
    /// </summary>
    /// <remarks>
    /// Only semantic search asks for this, and it is here rather than reached for directly so that
    /// an embedding model is started the same way every other model is: through the resolver, with
    /// the same ownership and the same lifetime. A runtime that cannot serve embeddings ignores it,
    /// which is the rule the other settings already follow.
    /// </remarks>
    public bool Embeddings { get; init; }

    /// <summary>The llama.cpp shaped view of these options.</summary>
    public LlamaLaunchOptions ToLlamaLaunchOptions()
        => new()
        {
            ContextSize = ContextSize,
            GpuLayers = GpuLayers,
            ProjectorPath = ProjectorPath,
            Embeddings = Embeddings
        };
}
