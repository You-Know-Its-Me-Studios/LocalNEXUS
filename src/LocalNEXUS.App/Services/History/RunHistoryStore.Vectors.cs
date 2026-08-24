using System.IO;
using Microsoft.Data.Sqlite;

namespace LocalNEXUS.App.Services.History;

/// <summary>One run's vector, and what made it.</summary>
/// <param name="RunId">Which run.</param>
/// <param name="Vector">The embedding, already scaled to unit length.</param>
public readonly record struct RunVector(string RunId, float[] Vector);

/// <summary>
/// The vectors semantic search compares against, kept beside the runs they describe.
/// </summary>
/// <remarks>
/// In the same database as everything else rather than a store of its own, because a vector is
/// worth exactly as long as the run it belongs to and putting it elsewhere would mean two things
/// to keep in step. Deleting a project's history takes its vectors with it, which is the correct
/// behaviour and comes for free.
///
/// No vector index. A search is a dot product against every stored run, and a busy project has
/// thousands rather than millions: at 384 numbers a run, ten thousand runs is fifteen megabytes
/// and a few milliseconds to scan. An index would be a second structure to keep correct in
/// exchange for a saving nothing here can measure.
///
/// The model and the width are stored with every vector. Vectors made by two different models are
/// not comparable, and the cheapest way to avoid comparing them by accident is to record what
/// made each one and ignore anything made by something else.
/// </remarks>
public sealed partial class RunHistoryStore
{
    /// <summary>Adds the vector table. Called from the schema, so an old database gains it.</summary>
    private static void CreateVectorSchema(SqliteConnection connection)
    {
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS run_vectors (
                run_id     TEXT PRIMARY KEY,
                model      TEXT NOT NULL,
                dimensions INTEGER NOT NULL,
                vector     BLOB NOT NULL,
                indexed_at TEXT NOT NULL
            );
            """);
    }

    /// <summary>Records the vector for one run, replacing anything already there for it.</summary>
    public void SaveVector(string runId, string model, float[] vector)
    {
        if (string.IsNullOrWhiteSpace(runId) || vector.Length == 0)
        {
            return;
        }

        Enqueue(connection => Execute(
            connection,
            """
            INSERT INTO run_vectors (run_id, model, dimensions, vector, indexed_at)
            VALUES ($run, $model, $dimensions, $vector, $at)
            ON CONFLICT(run_id) DO UPDATE SET
                model = excluded.model,
                dimensions = excluded.dimensions,
                vector = excluded.vector,
                indexed_at = excluded.indexed_at;
            """,
            ("$run", runId),
            ("$model", model),
            ("$dimensions", vector.Length),
            ("$vector", ToBytes(vector)),
            ("$at", DateTimeOffset.UtcNow.ToString("O"))));
    }

    /// <summary>
    /// Every vector made by the given model, which is everything a search compares against.
    /// </summary>
    /// <remarks>
    /// Filtered by model in the query rather than after it, so switching embedding model does not
    /// quietly compare new vectors against old ones. What it does mean is that switching model
    /// makes every existing vector invisible until a backfill is run, and the panel says so.
    /// </remarks>
    public async Task<IReadOnlyList<RunVector>> ReadVectorsAsync(string model, CancellationToken ct)
        => await ReadAsync(
            "SELECT run_id, vector FROM run_vectors WHERE model = $model;",
            reader => new RunVector(reader.GetString(0), FromBytes(ReadBlob(reader, 1))),
            ct,
            ("$model", model)).ConfigureAwait(false);

    /// <summary>Runs with no vector from this model yet, which is what a backfill works through.</summary>
    public async Task<IReadOnlyList<string>> RunsWithoutVectorsAsync(string model, CancellationToken ct)
        => await ReadAsync(
            """
            SELECT r.run_id
            FROM runs r
            LEFT JOIN run_vectors v ON v.run_id = r.run_id AND v.model = $model
            WHERE v.run_id IS NULL
            ORDER BY r.started_at DESC;
            """,
            reader => reader.GetString(0),
            ct,
            ("$model", model)).ConfigureAwait(false);

    /// <summary>How many runs already have a vector from this model.</summary>
    public async Task<int> VectorCountAsync(string model, CancellationToken ct)
    {
        var rows = await ReadAsync(
            "SELECT COUNT(*) FROM run_vectors WHERE model = $model;",
            reader => reader.GetInt32(0),
            ct,
            ("$model", model)).ConfigureAwait(false);

        return rows.Count > 0 ? rows[0] : 0;
    }

    /// <summary>Throws away every vector, which is what turning the feature off offers to do.</summary>
    public void ClearVectors() => Enqueue(connection => Execute(connection, "DELETE FROM run_vectors;"));

    /// <summary>
    /// The text that stands for a run when it is embedded.
    /// </summary>
    /// <remarks>
    /// The request plus what the run said it did, which is what somebody is searching for when
    /// they describe a run from memory. Not the whole transcript: a transcript is mostly generated
    /// code, and embedding it buries the one sentence a person actually wrote under a thousand
    /// lines that all look alike.
    /// </remarks>
    public async Task<string> DescribeForEmbeddingAsync(string runId, CancellationToken ct)
    {
        var rows = await ReadAsync(
            "SELECT request FROM runs WHERE run_id = $run;",
            reader => reader.GetString(0),
            ct,
            ("$run", runId)).ConfigureAwait(false);

        var request = rows.Count > 0 ? rows[0] : string.Empty;

        var titles = await ReadAsync(
            """
            SELECT title FROM events
            WHERE run_id = $run AND title IS NOT NULL AND title <> ''
            ORDER BY at
            LIMIT 24;
            """,
            reader => reader.GetString(0),
            ct,
            ("$run", runId)).ConfigureAwait(false);

        return string.Join(". ", new[] { request }.Concat(titles).Where(part => part.Length > 0));
    }

    /// <summary>
    /// One run's headline, for a hit that came from a vector rather than a word match.
    /// </summary>
    /// <remarks>
    /// A keyword hit arrives with a snippet showing the matched words in place. A vector hit has
    /// no matched words to point at, so what stands in for the snippet is the request itself,
    /// which is the sentence somebody was trying to remember in the first place.
    /// </remarks>
    public async Task<HistoryHit?> ReadHitAsync(string runId, CancellationToken ct)
    {
        var rows = await ReadAsync(
            "SELECT run_id, started_at, request FROM runs WHERE run_id = $run;",
            reader => new HistoryHit(
                reader.GetString(0),
                DateTimeOffset.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(2)),
            ct,
            ("$run", runId)).ConfigureAwait(false);

        return rows.Count > 0 ? rows[0] : null;
    }

    /// <summary>A vector as bytes, little endian, which is what goes in the blob.</summary>
    internal static byte[] ToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);

        return bytes;
    }

    /// <summary>The vector those bytes were.</summary>
    internal static float[] FromBytes(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);

        return vector;
    }

    private static byte[] ReadBlob(SqliteDataReader reader, int column)
    {
        using var stream = reader.GetStream(column);
        using var memory = new MemoryStream();

        stream.CopyTo(memory);

        return memory.ToArray();
    }
}
