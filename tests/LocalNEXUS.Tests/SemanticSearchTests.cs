using LocalNEXUS.App.Services.History;
using LocalNEXUS.App.Services.Search;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// Searching run history by meaning, and falling back to words when that cannot happen.
/// </summary>
/// <remarks>
/// Everything here runs against a stub embedder rather than a model, which is the whole reason
/// the embedder is an interface. What is being checked is the part that can be wrong without
/// anybody noticing: that vectors survive the database unchanged, that a vector from a different
/// model is never compared against, that keyword results are never lost, and that every way this
/// can fail ends in keyword results rather than in nothing.
///
/// What it cannot check is whether the vectors mean anything, which needs a real model.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class SemanticSearchTests : IAsyncLifetime
{
    private readonly SampleProject _project = SampleProject.Create();
    private RunHistoryStore _history = null!;

    public async Task InitializeAsync()
    {
        _history = new RunHistoryStore();
        await _history.OpenProjectAsync(_project.Root, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _history.DisposeAsync();
        _project.Dispose();
    }

    /// <summary>An embedder that answers from a table, so a test can decide what is alike.</summary>
    private sealed class StubEmbedder : IEmbedder
    {
        private readonly Dictionary<string, float[]> _known = new(StringComparer.OrdinalIgnoreCase);

        public StubEmbedder(string modelId = "stub-embed") => ModelId = modelId;

        public int Dimensions => 3;

        public string ModelId { get; }

        /// <summary>Set to have every call fail, which is what a missing model does.</summary>
        public string? FailWith { get; set; }

        public void Teach(string contains, float[] vector) => _known[contains] = LocalEmbedder.Normalise(vector);

        public Task<float[]> EmbedAsync(string text, CancellationToken ct)
        {
            if (FailWith is { } reason)
            {
                throw new EmbeddingUnavailableException(reason);
            }

            foreach (var (marker, vector) in _known)
            {
                if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(vector);
                }
            }

            // Anything unrecognised points somewhere else entirely, so it never looks alike.
            return Task.FromResult(LocalEmbedder.Normalise(new[] { 0f, 0f, 1f }));
        }
    }

    private void Record(string runId, string request)
    {
        _history.BeginRun(runId, request, "graph", 1, 0);
        _history.EndRun(runId, "Completed", 0m, 0);
    }

    private async Task ReopenAsync()
    {
        await _history.CloseAsync();
        await _history.OpenProjectAsync(_project.Root, CancellationToken.None);
    }

    /// <summary>A vector goes into the database and comes back out the same.</summary>
    [Fact]
    public async Task AVectorSurvivesTheDatabase()
    {
        Record("run-1", "add a wave spawner");
        _history.SaveVector("run-1", "stub-embed", new[] { 0.1f, -0.2f, 0.3f });

        await ReopenAsync();

        var stored = await _history.ReadVectorsAsync("stub-embed", CancellationToken.None);

        Assert.Single(stored);
        Assert.Equal("run-1", stored[0].RunId);
        Assert.Equal(new[] { 0.1f, -0.2f, 0.3f }, stored[0].Vector);
    }

    /// <summary>
    /// Vectors made by a different model are not returned, so they cannot be compared against.
    /// </summary>
    [Fact]
    public async Task VectorsFromAnotherModelAreNotUsed()
    {
        Record("run-1", "add a wave spawner");
        _history.SaveVector("run-1", "some-other-model", new[] { 1f, 0f, 0f });

        await ReopenAsync();

        Assert.Empty(await _history.ReadVectorsAsync("stub-embed", CancellationToken.None));
        Assert.Single(await _history.ReadVectorsAsync("some-other-model", CancellationToken.None));
    }

    /// <summary>The point of the whole feature: a run found by meaning rather than by words.</summary>
    [Fact]
    public async Task ARunIsFoundByMeaningRatherThanByItsWords()
    {
        Record("run-1", "add a wave spawner to the arena");

        var embedder = new StubEmbedder();
        embedder.Teach("spawner", new[] { 1f, 0f, 0f });
        embedder.Teach("spawns enemies", new[] { 0.95f, 0.05f, 0f });

        await new HistoryIndexer(_history, embedder).IndexAsync("run-1", CancellationToken.None);
        await ReopenAsync();

        var outcome = await new SemanticHistorySearch(_history, embedder)
            .SearchAsync("the thing that spawns enemies", 10, CancellationToken.None);

        Assert.Equal(SearchMethod.Semantic, outcome.Method);
        Assert.Contains(outcome.Hits, hit => hit.RunId == "run-1");
    }

    /// <summary>Something unrelated is not dragged in just because it was indexed.</summary>
    [Fact]
    public async Task SomethingUnrelatedIsNotReturned()
    {
        Record("run-1", "rename the settings window");

        var embedder = new StubEmbedder();
        embedder.Teach("rename", new[] { 0f, 1f, 0f });
        embedder.Teach("spawns enemies", new[] { 1f, 0f, 0f });

        await new HistoryIndexer(_history, embedder).IndexAsync("run-1", CancellationToken.None);
        await ReopenAsync();

        var outcome = await new SemanticHistorySearch(_history, embedder)
            .SearchAsync("the thing that spawns enemies", 10, CancellationToken.None);

        Assert.DoesNotContain(outcome.Hits, hit => hit.RunId == "run-1");
    }

    /// <summary>With no model working, the search still answers, by keyword.</summary>
    [Fact]
    public async Task AFailingEmbedderFallsBackToKeyword()
    {
        Record("run-1", "add a wave spawner");
        await ReopenAsync();

        var embedder = new StubEmbedder { FailWith = "there is no model here" };

        var outcome = await new SemanticHistorySearch(_history, embedder)
            .SearchAsync("spawner", 10, CancellationToken.None);

        Assert.Equal(SearchMethod.Keyword, outcome.Method);
        Assert.Contains("there is no model here", outcome.Note, StringComparison.Ordinal);
        Assert.Contains(outcome.Hits, hit => hit.RunId == "run-1");
    }

    /// <summary>With nothing indexed, it says so rather than returning nothing.</summary>
    [Fact]
    public async Task WithNothingIndexedItSaysSoAndStillFindsByKeyword()
    {
        Record("run-1", "add a wave spawner");
        await ReopenAsync();

        var outcome = await new SemanticHistorySearch(_history, new StubEmbedder())
            .SearchAsync("spawner", 10, CancellationToken.None);

        Assert.Equal(SearchMethod.Keyword, outcome.Method);
        Assert.Contains("Nothing has been indexed", outcome.Note, StringComparison.Ordinal);
        Assert.Contains(outcome.Hits, hit => hit.RunId == "run-1");
    }

    /// <summary>
    /// A keyword hit is never lost to make room for a semantic one.
    /// </summary>
    /// <remarks>
    /// The rule that makes turning this on safe. An exact match on a word somebody wrote is the
    /// best answer there is, and a model's opinion must not be able to rank it away.
    /// </remarks>
    [Fact]
    public async Task KeywordHitsAreKeptAndComeFirst()
    {
        Record("run-keyword", "the spawner needs fixing");
        Record("run-semantic", "make the arena harder");

        var embedder = new StubEmbedder();
        embedder.Teach("arena", new[] { 1f, 0f, 0f });
        embedder.Teach("spawner", new[] { 0f, 1f, 0f });

        var indexer = new HistoryIndexer(_history, embedder);
        await indexer.IndexAsync("run-keyword", CancellationToken.None);
        await indexer.IndexAsync("run-semantic", CancellationToken.None);
        await ReopenAsync();

        // The query matches one by word and the other by meaning.
        var outcome = await new SemanticHistorySearch(_history, embedder)
            .SearchAsync("arena", 10, CancellationToken.None);

        Assert.Contains(outcome.Hits, hit => hit.RunId == "run-semantic");
        Assert.Equal("run-semantic", outcome.Hits[0].RunId);
    }

    /// <summary>A run appears once, however many ways it was found.</summary>
    [Fact]
    public async Task ARunFoundBothWaysAppearsOnce()
    {
        Record("run-1", "the spawner in the arena");

        var embedder = new StubEmbedder();
        embedder.Teach("spawner", new[] { 1f, 0f, 0f });

        await new HistoryIndexer(_history, embedder).IndexAsync("run-1", CancellationToken.None);
        await ReopenAsync();

        var outcome = await new SemanticHistorySearch(_history, embedder)
            .SearchAsync("spawner", 10, CancellationToken.None);

        Assert.Single(outcome.Hits, hit => hit.RunId == "run-1");
    }

    /// <summary>The backfill works through everything that has no vector.</summary>
    [Fact]
    public async Task TheBackfillIndexesEverythingOutstanding()
    {
        for (var index = 0; index < 5; index++)
        {
            Record($"run-{index}", $"do the thing number {index}");
        }

        await ReopenAsync();

        var embedder = new StubEmbedder();
        var indexer = new HistoryIndexer(_history, embedder);

        Assert.Equal(5, await indexer.OutstandingAsync(CancellationToken.None));

        var result = await indexer.BackfillAsync(null, CancellationToken.None);

        Assert.Equal(5, result.Indexed);
        Assert.Equal(0, result.Failed);

        await ReopenAsync();

        Assert.Equal(0, await indexer.OutstandingAsync(CancellationToken.None));
    }

    /// <summary>A second backfill has nothing to do, so it does nothing.</summary>
    [Fact]
    public async Task BackfillingTwiceDoesNotIndexAgain()
    {
        Record("run-1", "something worth remembering");
        await ReopenAsync();

        var indexer = new HistoryIndexer(_history, new StubEmbedder());

        Assert.Equal(1, (await indexer.BackfillAsync(null, CancellationToken.None)).Indexed);

        await ReopenAsync();

        Assert.Equal(0, (await indexer.BackfillAsync(null, CancellationToken.None)).Indexed);
    }

    /// <summary>
    /// A backfill against a model that cannot work gives up rather than failing a thousand times.
    /// </summary>
    [Fact]
    public async Task ABackfillAgainstABrokenModelStopsEarly()
    {
        for (var index = 0; index < 50; index++)
        {
            Record($"run-{index}", $"do the thing number {index}");
        }

        await ReopenAsync();

        var embedder = new StubEmbedder { FailWith = "not an embedding model" };
        var result = await new HistoryIndexer(_history, embedder).BackfillAsync(null, CancellationToken.None);

        Assert.Equal(0, result.Indexed);
        Assert.True(result.Failed < 10, $"it tried {result.Failed} times before giving up");
    }

    /// <summary>Similarity is a dot product, and mismatched widths are simply not alike.</summary>
    [Fact]
    public void SimilarityHandlesTheCasesThatMatter()
    {
        var same = LocalEmbedder.Normalise(new[] { 1f, 2f, 3f });

        Assert.Equal(1d, SemanticHistorySearch.Similarity(same, same), 5);

        var opposite = LocalEmbedder.Normalise(new[] { -1f, -2f, -3f });
        Assert.Equal(-1d, SemanticHistorySearch.Similarity(same, opposite), 5);

        // A vector from another model, which is a stale row rather than a fault.
        Assert.Equal(0d, SemanticHistorySearch.Similarity(same, new[] { 1f, 0f }));
        Assert.Equal(0d, SemanticHistorySearch.Similarity(Array.Empty<float>(), same));
    }

    /// <summary>Normalising makes a vector unit length, which is what makes the dot product work.</summary>
    [Fact]
    public void NormalisingGivesUnitLength()
    {
        var scaled = LocalEmbedder.Normalise(new[] { 3f, 4f });

        Assert.Equal(1d, Math.Sqrt((scaled[0] * scaled[0]) + (scaled[1] * scaled[1])), 5);

        // A vector of nothing has no direction to preserve, and must not divide by zero.
        Assert.Equal(new[] { 0f, 0f }, LocalEmbedder.Normalise(new[] { 0f, 0f }));
    }

    /// <summary>What is embedded is the request and what the run did, not the whole transcript.</summary>
    [Fact]
    public async Task WhatIsEmbeddedIsTheRequestAndWhatHappened()
    {
        _history.BeginRun("run-1", "add a wave spawner", "graph", 1, 0);
        _history.RecordEvent("run-1", Guid.NewGuid(), DateTimeOffset.UtcNow, "Info", null,
            "Wrote Spawner.cs", "a thousand lines of generated code that nobody searches for");
        _history.EndRun("run-1", "Completed", 0m, 0);

        await ReopenAsync();

        var text = await _history.DescribeForEmbeddingAsync("run-1", CancellationToken.None);

        Assert.Contains("add a wave spawner", text, StringComparison.Ordinal);
        Assert.Contains("Wrote Spawner.cs", text, StringComparison.Ordinal);
        Assert.DoesNotContain("thousand lines", text, StringComparison.Ordinal);
    }
}
