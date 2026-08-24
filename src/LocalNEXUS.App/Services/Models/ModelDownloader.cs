using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace LocalNEXUS.App.Services.Models;

/// <summary>How far a download has got, reported as it goes.</summary>
/// <param name="BytesSoFar">What is on disk, including anything resumed.</param>
/// <param name="TotalBytes">How large the finished file will be, or zero when unknown.</param>
/// <param name="BytesPerSecond">Recent rate, which is what a person reads to decide whether to wait.</param>
public readonly record struct DownloadProgress(long BytesSoFar, long TotalBytes, double BytesPerSecond)
{
    /// <summary>How far along, from zero to one, or null when the total is unknown.</summary>
    public double? Fraction => TotalBytes > 0 ? Math.Clamp((double)BytesSoFar / TotalBytes, 0d, 1d) : null;

    /// <summary>What is left, at the current rate, or null when that cannot be worked out.</summary>
    public TimeSpan? Remaining => TotalBytes > 0 && BytesPerSecond > 1d
        ? TimeSpan.FromSeconds((TotalBytes - BytesSoFar) / BytesPerSecond)
        : null;
}

/// <summary>What happened to a download that finished.</summary>
public enum DownloadOutcome
{
    /// <summary>It arrived and matched the hash the repository published.</summary>
    Verified,

    /// <summary>It arrived, and the repository published no hash to check it against.</summary>
    Unverified,

    /// <summary>It arrived and did not match the hash. The file was not kept.</summary>
    Corrupt
}

/// <summary>A download that could not be finished, with the reason.</summary>
public sealed class DownloadFailedException : Exception
{
    public DownloadFailedException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Fetches a model file into the folder the catalogue already watches.
/// </summary>
/// <remarks>
/// Nothing here needs a privilege. The destination is the per user models folder, which this
/// application created and owns, so no part of downloading a model prompts for elevation.
///
/// Written to a neighbouring part file and moved into place at the end, which is what makes two
/// things true at once: a half finished file is never mistaken for a model by the catalogue, and
/// an interrupted download still has its bytes to resume from. The move is the only moment the
/// real name exists, and by then the file is complete and checked.
///
/// Resuming is a byte range request, which the content server supports and advertises. A server
/// that refuses one is handled by starting again rather than by appending to bytes that may not
/// line up, because a file that is subtly wrong is worse than one that took longer.
/// </remarks>
public sealed class ModelDownloader
{
    /// <summary>What an unfinished download is called while it is unfinished.</summary>
    public const string PartExtension = ".part";

    /// <summary>How often progress is reported, so a fast disk does not flood the interface.</summary>
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(200);

    private const int BufferBytes = 128 * 1024;

    private readonly HttpClient _http;

    public ModelDownloader(HttpClient http) => _http = http;

    /// <summary>Where an unfinished download for this destination would be.</summary>
    public static string PartFileFor(string destination) => destination + PartExtension;

    /// <summary>
    /// How many bytes of this download are already on disk and could be resumed.
    /// </summary>
    public static long ResumableBytes(string destination)
    {
        var part = new FileInfo(PartFileFor(destination));

        return part.Exists ? part.Length : 0L;
    }

    /// <summary>
    /// Downloads a file, resuming anything already there, and returns whether it verified.
    /// </summary>
    /// <param name="file">What to fetch.</param>
    /// <param name="destination">The final path, which only exists once the file is complete.</param>
    /// <param name="progress">Told as it goes.</param>
    /// <param name="ct">Cancelling keeps the part file, so the same download can be resumed.</param>
    /// <exception cref="GatedRepositoryException">The repository requires an account.</exception>
    /// <exception cref="DownloadFailedException">It could not be finished.</exception>
    public async Task<DownloadOutcome> DownloadAsync(
        ModelFileOption file,
        string destination,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);

        var part = PartFileFor(destination);

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        var alreadyHave = ResumableBytes(destination);

        // Anything larger than the finished file is not a resumable part of it, it is a mistake
        // from a previous attempt at a different file with the same name.
        if (file.SizeBytes > 0 && alreadyHave > file.SizeBytes)
        {
            File.Delete(part);
            alreadyHave = 0;
        }

        if (file.SizeBytes > 0 && alreadyHave == file.SizeBytes)
        {
            return await FinishAsync(file, part, destination, ct).ConfigureAwait(false);
        }

        await FetchWithRetryAsync(file, part, alreadyHave, progress, ct).ConfigureAwait(false);

        return await FinishAsync(file, part, destination, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches, and asks again from wherever it got to when the connection drops.
    /// </summary>
    /// <remarks>
    /// The retry is around the whole fetch rather than around the request, and that is the point.
    /// The failure being handled is a reset mid response, so the request succeeds, the headers
    /// arrive, and the connection dies part way through the body: a policy wrapped around sending
    /// alone never sees it. Retrying the request and retrying the copy would also be two nested
    /// policies multiplying into nine attempts for a failure that is one thing.
    ///
    /// Each attempt reads how much is on disk again rather than trusting what it was told, because
    /// the previous attempt wrote an unknown amount before it died. That is what makes a retry a
    /// resume: the second attempt asks for the rest.
    /// </remarks>
    private async Task FetchWithRetryAsync(
        ModelFileOption file,
        string part,
        long alreadyHave,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        var from = alreadyHave;

        for (var attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await FetchAsync(file, part, from, progress, ct).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < HubRetry.Attempts && HubRetry.WorthRetrying(ex))
            {
                var landed = File.Exists(part) ? new FileInfo(part).Length : 0L;

                HubTransport.LogFailure(
                    $"Download of {file.Path} was interrupted at byte {landed} on attempt {attempt}",
                    ex);

                // No progress at all means asking again from the same place, which is fine: the
                // failure may have been in getting the response rather than in reading it.
                from = landed;

                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// One attempt at the bytes from a given offset.
    /// </summary>
    /// <remarks>
    /// Awaited here rather than returning the task, and that is not a style preference. Returning
    /// it let the using block dispose the request while the send was still in flight, which threw
    /// "cannot access a disposed object" on every real download and on no test, because a fake
    /// handler reads the headers before it returns and a real one does not. Found by downloading
    /// something.
    /// </remarks>
    private async Task<HttpResponseMessage> Send(ModelFileOption file, long from, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, file.DownloadUrl);

        if (from > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(from, null);
        }

        return await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
    }

    private async Task FetchAsync(
        ModelFileOption file,
        string part,
        long from,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        HttpResponseMessage response;

        try
        {
            response = await Send(file, from, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            HubTransport.LogFailure(
                $"Download of {file.Path} from byte {from} failed after {HubRetry.Attempts} attempts",
                ex);

            throw new DownloadFailedException(
                $"The download was interrupted {HubRetry.Attempts} times. That is usually the "
                + "network between this machine and Hugging Face rather than anything here. "
                + "Everything already fetched is kept, so starting it again carries on from "
                + "where it stopped. The detail is in hub.log.",
                ex);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new GatedRepositoryException(file.Repository);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new DownloadFailedException(
                    $"The download was refused with {(int)response.StatusCode}. Try again shortly.");
            }

            // A server that ignored the range answers 200 with the whole file, so what is on disk
            // is not a prefix of what is arriving and appending would splice two halves together.
            var resuming = from > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            var startAt = resuming ? from : 0L;

            var total = file.SizeBytes > 0
                ? file.SizeBytes
                : startAt + (response.Content.Headers.ContentLength ?? 0L);

            await using var incoming = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var target = new FileStream(
                part,
                resuming ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferBytes,
                useAsync: true);

            await CopyAsync(incoming, target, startAt, total, progress, ct).ConfigureAwait(false);
        }
    }

    private static async Task CopyAsync(
        Stream incoming,
        Stream target,
        long startAt,
        long total,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        var buffer = new byte[BufferBytes];
        var written = startAt;

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        var lastBytes = startAt;

        while (true)
        {
            var read = await incoming.ReadAsync(buffer, ct).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            written += read;

            if (clock.Elapsed - lastReport < ReportInterval)
            {
                continue;
            }

            var seconds = (clock.Elapsed - lastReport).TotalSeconds;
            var rate = seconds > 0 ? (written - lastBytes) / seconds : 0d;

            progress?.Report(new DownloadProgress(written, total, rate));

            lastReport = clock.Elapsed;
            lastBytes = written;
        }

        // Flushed before the caller is told it finished, so the size on disk is the size reported.
        await target.FlushAsync(ct).ConfigureAwait(false);

        progress?.Report(new DownloadProgress(written, total, 0d));
    }

    /// <summary>
    /// Checks the finished part file and moves it into place, or refuses to.
    /// </summary>
    /// <remarks>
    /// A file that does not match its published hash is deleted rather than kept, because the one
    /// thing worse than a failed download is a corrupt model that loads and produces nonsense.
    /// Where no hash was published, the file is kept and the caller is told it could not be
    /// checked, which is a different answer from having been checked and passed.
    /// </remarks>
    private static async Task<DownloadOutcome> FinishAsync(
        ModelFileOption file,
        string part,
        string destination,
        CancellationToken ct)
    {
        var outcome = DownloadOutcome.Unverified;

        if (file.CanBeVerified)
        {
            var actual = await Sha256Async(part, ct).ConfigureAwait(false);

            if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(part);

                throw new DownloadFailedException(
                    $"{file.Path} arrived but does not match the hash the repository published, so "
                    + "it was not kept. That usually means the download was interrupted in a way "
                    + "that went unnoticed. Try again.");
            }

            outcome = DownloadOutcome.Verified;
        }

        try
        {
            File.Move(part, destination, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DownloadFailedException(
                $"{file.Path} downloaded but could not be moved into the models folder: {ex.Message}", ex);
        }

        return outcome;
    }

    /// <summary>The hash of a file on disk, read in blocks so a large one costs no memory.</summary>
    public static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, useAsync: true);

        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Removes an unfinished download.
    /// </summary>
    /// <remarks>
    /// What cancelling does when somebody does not intend to come back to it. Cancelling on its
    /// own keeps the part file so the same download can be resumed, and this is the deliberate
    /// second step: no orphaned multi gigabyte file left behind by a decision to stop.
    /// </remarks>
    public static void DiscardPartial(string destination)
    {
        try
        {
            var part = PartFileFor(destination);

            if (File.Exists(part))
            {
                File.Delete(part);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still being written, or not ours. The next attempt at the same file overwrites it.
        }
    }
}
