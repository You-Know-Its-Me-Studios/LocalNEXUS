using System.Diagnostics;
using LocalNEXUS.App.Services.History;

namespace LocalNEXUS.App.Services.Search;

/// <summary>What a backfill did.</summary>
/// <param name="Indexed">How many runs gained a vector.</param>
/// <param name="Failed">How many could not be embedded.</param>
/// <param name="Elapsed">How long it took, which is what the estimate for next time comes from.</param>
public readonly record struct BackfillResult(int Indexed, int Failed, TimeSpan Elapsed)
{
    /// <summary>Average time per run, which is the figure worth quoting.</summary>
    public TimeSpan Each => Indexed > 0
        ? TimeSpan.FromMilliseconds(Elapsed.TotalMilliseconds / Indexed)
        : TimeSpan.Zero;
}

/// <summary>
/// Gives runs their vectors: new ones as they finish, old ones when asked.
/// </summary>
/// <remarks>
/// Indexing one run is one embedding call over a couple of hundred characters, which is why it
/// can happen as a run ends without anybody noticing. It is deliberately not part of recording
/// the run: history is written whether or not this is switched on, whether or not a model is
/// there, and whether or not the embedding works. A run is never lost because a vector could not
/// be made for it.
///
/// The backfill is offered once rather than run automatically, because it is the only part of
/// this with a cost somebody should agree to: a project with a thousand runs is a thousand
/// embedding calls, and while that is a minute rather than an hour it is still a minute of
/// somebody's card that they did not ask for.
/// </remarks>
public sealed class HistoryIndexer
{
    private readonly RunHistoryStore _history;
    private readonly IEmbedder _embedder;

    public HistoryIndexer(RunHistoryStore history, IEmbedder embedder)
    {
        _history = history;
        _embedder = embedder;
    }

    /// <summary>
    /// Gives one run a vector, quietly.
    /// </summary>
    /// <remarks>
    /// Returns whether it worked rather than throwing, because the caller is the end of a run and
    /// there is nothing useful it could do about a failure. What matters is that the run itself is
    /// already recorded by the time this is called.
    /// </remarks>
    public async Task<bool> IndexAsync(string runId, CancellationToken ct)
    {
        try
        {
            var text = await _history.DescribeForEmbeddingAsync(runId, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var vector = await _embedder.EmbedAsync(text, ct).ConfigureAwait(false);

            if (vector.Length == 0)
            {
                return false;
            }

            _history.SaveVector(runId, _embedder.ModelId, vector);

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (EmbeddingUnavailableException)
        {
            // Searching still works without this, so a run that could not be indexed costs a
            // slightly worse search rather than anything a person has to deal with now.
            return false;
        }
    }

    /// <summary>How many runs have no vector yet, which is what a backfill would work through.</summary>
    public async Task<int> OutstandingAsync(CancellationToken ct)
        => (await _history.RunsWithoutVectorsAsync(_embedder.ModelId, ct).ConfigureAwait(false)).Count;

    /// <summary>
    /// Indexes every run that has no vector yet.
    /// </summary>
    /// <remarks>
    /// Stops at the first sign the model is not going to work rather than failing a thousand times
    /// in a row: if the first few cannot be embedded, none of the rest will be either, and a
    /// backfill that spends two minutes discovering that is worse than one that says so.
    /// </remarks>
    public async Task<BackfillResult> BackfillAsync(
        IProgress<(int Done, int Total)>? progress,
        CancellationToken ct)
    {
        var outstanding = await _history
            .RunsWithoutVectorsAsync(_embedder.ModelId, ct)
            .ConfigureAwait(false);

        var clock = Stopwatch.StartNew();
        var indexed = 0;
        var failed = 0;

        foreach (var runId in outstanding)
        {
            ct.ThrowIfCancellationRequested();

            if (await IndexAsync(runId, ct).ConfigureAwait(false))
            {
                indexed++;
            }
            else
            {
                failed++;

                // Three in a row with nothing succeeding is a model that is not going to work.
                if (failed >= 3 && indexed == 0)
                {
                    break;
                }
            }

            progress?.Report((indexed + failed, outstanding.Count));
        }

        return new BackfillResult(indexed, failed, clock.Elapsed);
    }
}
