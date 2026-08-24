namespace LocalNEXUS.App.Services.Search;

/// <summary>
/// Turns text into a vector, so two pieces of text can be compared by meaning.
/// </summary>
/// <remarks>
/// An interface because everything downstream of it can then be built and checked without a model
/// on disk: the storage, the similarity, the ranking, the fallback and the backfill are all
/// arithmetic and bookkeeping, and none of them need a real embedding to be wrong in a way worth
/// catching. The one thing it hides is the only thing that genuinely needs a model.
/// </remarks>
public interface IEmbedder
{
    /// <summary>
    /// How many numbers a vector from this embedder has.
    /// </summary>
    /// <remarks>
    /// Stored beside every vector, because a vector made by one model cannot be compared to one
    /// made by another and the width is the cheapest way to notice.
    /// </remarks>
    int Dimensions { get; }

    /// <summary>What identifies the model, so vectors made by a different one are not trusted.</summary>
    string ModelId { get; }

    /// <summary>
    /// Embeds one piece of text.
    /// </summary>
    /// <exception cref="EmbeddingUnavailableException">There is no model to ask, or it refused.</exception>
    Task<float[]> EmbedAsync(string text, CancellationToken ct);
}

/// <summary>Semantic search could not run, with a reason somebody can act on.</summary>
/// <remarks>
/// Always recoverable by design. Everything that can raise this has keyword search behind it, so
/// the reason is worth saying and is never worth failing a search over.
/// </remarks>
public sealed class EmbeddingUnavailableException : Exception
{
    public EmbeddingUnavailableException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
