using LocalNEXUS.App.Services.History;

namespace LocalNEXUS.App.Services.Search;

/// <summary>How a set of results was found, so the panel can say.</summary>
public enum SearchMethod
{
    /// <summary>Words that were actually written, which is what always works.</summary>
    Keyword,

    /// <summary>Meaning, through embeddings, with keyword results merged in behind.</summary>
    Semantic
}

/// <summary>What a search found, and how.</summary>
/// <param name="Hits">The runs, best first.</param>
/// <param name="Method">Which way they were found.</param>
/// <param name="Note">Why it fell back, when it did. Empty otherwise.</param>
public sealed record SearchOutcome(IReadOnlyList<HistoryHit> Hits, SearchMethod Method, string Note);

/// <summary>
/// Searching run history by meaning, with keyword matching underneath it.
/// </summary>
/// <remarks>
/// Keyword search finds the words somebody wrote. Asked for "the thing that spawns enemies" it
/// finds nothing, because nobody wrote that sentence: they wrote "add a wave spawner". Embedding
/// both and comparing them is what closes that gap, and it is the only thing here that needs a
/// model.
///
/// Two rules make it safe to turn on. Keyword search stays the default, so somebody who never
/// opts in has lost nothing and gained no dependency. And semantic search never replaces keyword
/// results, it leads them: an exact word match is still the best answer when there is one, so the
/// two lists are merged with the keyword hits kept rather than ranked away.
///
/// Anything that goes wrong falls back rather than fails. A missing model, a model that is not an
/// embedding model, a server that will not start: all of them end in keyword results and a line
/// saying why, because a search that returns nothing is indistinguishable from a project with
/// nothing in it and that is the failure this whole area was built to stop making.
/// </remarks>
public sealed class SemanticHistorySearch
{
    /// <summary>
    /// How alike two things have to be before it is worth showing.
    /// </summary>
    /// <remarks>
    /// Cosine similarity over normalised vectors, so this is between minus one and one. Short
    /// texts from a small embedding model sit higher than intuition suggests, and below about a
    /// third the results are things that share a topic rather than a subject.
    /// </remarks>
    private const double Floor = 0.35d;

    private readonly RunHistoryStore _history;
    private readonly IEmbedder _embedder;

    public SemanticHistorySearch(RunHistoryStore history, IEmbedder embedder)
    {
        _history = history;
        _embedder = embedder;
    }

    /// <summary>
    /// Searches by meaning where it can and by words where it cannot.
    /// </summary>
    /// <remarks>
    /// The keyword search runs first and always, which is what makes the fallback free: by the
    /// time anything can go wrong there is already a set of results to return.
    /// </remarks>
    public async Task<SearchOutcome> SearchAsync(string query, int limit, CancellationToken ct)
    {
        var keyword = await _history.SearchAsync(query, limit, ct).ConfigureAwait(false);

        try
        {
            var wanted = await _embedder.EmbedAsync(query, ct).ConfigureAwait(false);

            if (wanted.Length == 0)
            {
                return new SearchOutcome(keyword, SearchMethod.Keyword, string.Empty);
            }

            var stored = await _history.ReadVectorsAsync(_embedder.ModelId, ct).ConfigureAwait(false);

            if (stored.Count == 0)
            {
                return new SearchOutcome(
                    keyword,
                    SearchMethod.Keyword,
                    "Nothing has been indexed yet, so this was a keyword search. Index the history "
                    + "from Settings to search by meaning.");
            }

            var ranked = stored
                .Select(entry => (entry.RunId, Score: Similarity(wanted, entry.Vector)))
                .Where(entry => entry.Score >= Floor)
                .OrderByDescending(entry => entry.Score)
                .Take(limit)
                .ToList();

            var merged = await MergeAsync(keyword, ranked, limit, ct).ConfigureAwait(false);

            return new SearchOutcome(merged, SearchMethod.Semantic, string.Empty);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (EmbeddingUnavailableException ex)
        {
            return new SearchOutcome(
                keyword,
                SearchMethod.Keyword,
                $"This was a keyword search, because searching by meaning did not work: {ex.Message}");
        }
    }

    /// <summary>
    /// Keyword hits first, then anything the vectors found that keywords did not.
    /// </summary>
    /// <remarks>
    /// Deliberately not one ranking over both. An exact match on a word somebody actually wrote is
    /// the best answer available and does not need a model's opinion to be ordered above a
    /// paraphrase. What embeddings add is the runs that would otherwise not appear at all.
    /// </remarks>
    private async Task<IReadOnlyList<HistoryHit>> MergeAsync(
        IReadOnlyList<HistoryHit> keyword,
        IReadOnlyList<(string RunId, double Score)> ranked,
        int limit,
        CancellationToken ct)
    {
        var merged = new List<HistoryHit>(keyword);
        var seen = new HashSet<string>(keyword.Select(hit => hit.RunId), StringComparer.OrdinalIgnoreCase);

        foreach (var (runId, _) in ranked)
        {
            if (merged.Count >= limit || !seen.Add(runId))
            {
                continue;
            }

            if (await _history.ReadHitAsync(runId, ct).ConfigureAwait(false) is { } hit)
            {
                merged.Add(hit);
            }
        }

        return merged;
    }

    /// <summary>
    /// How alike two vectors are, between minus one and one.
    /// </summary>
    /// <remarks>
    /// A dot product rather than a full cosine, because both vectors were scaled to unit length
    /// when they were made. Vectors of different widths came from different models and are not
    /// comparable at all, which is answered with no similarity rather than an exception: it is a
    /// stale row rather than a fault, and a search should skip it and carry on.
    /// </remarks>
    public static double Similarity(float[] left, float[] right)
    {
        if (left.Length == 0 || left.Length != right.Length)
        {
            return 0d;
        }

        double total = 0;

        for (var index = 0; index < left.Length; index++)
        {
            total += (double)left[index] * right[index];
        }

        return total;
    }
}
