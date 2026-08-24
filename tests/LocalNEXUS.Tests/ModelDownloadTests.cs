using System.IO;
using System.Net;
using System.Net.Http;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.Models;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// Downloading a model: what resuming does, what verification refuses, and what a fit says.
/// </summary>
/// <remarks>
/// These run against a handler that answers in memory rather than against Hugging Face, because
/// the behaviour worth pinning down is what this code does with a range request, a server that
/// ignores one, and a file whose hash does not match. Those are the cases that are hard to
/// arrange against a real service and are exactly where a download quietly corrupts a file.
///
/// The one thing they cannot cover is whether the endpoints are shaped as expected, which is why
/// that was checked against the live service by hand and written down where the client is.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class ModelDownloadTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "localnexus-download-tests", Guid.NewGuid().ToString("N"));

    public ModelDownloadTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A scratch folder that will not delete is not the test's problem.
        }
    }

    /// <summary>Serves a fixed body, honouring byte ranges unless told not to.</summary>
    private sealed class Serving : HttpMessageHandler
    {
        private readonly byte[] _body;
        private readonly bool _honoursRanges;

        public Serving(byte[] body, bool honoursRanges = true)
        {
            _body = body;
            _honoursRanges = honoursRanges;
        }

        /// <summary>What range the last request asked for, so a test can see resuming happen.</summary>
        public long? AskedFrom { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var from = request.Headers.Range?.Ranges.FirstOrDefault()?.From;
            AskedFrom = from;

            if (from is { } start && _honoursRanges)
            {
                var slice = _body[(int)start..];

                var partial = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(slice)
                };

                partial.Content.Headers.ContentRange =
                    new System.Net.Http.Headers.ContentRangeHeaderValue(start, _body.Length - 1, _body.Length);

                return Task.FromResult(partial);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_body)
            });
        }
    }

    private static string Sha256Of(byte[] data)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();

    private static ModelFileOption FileOf(byte[] body, string? hash)
        => new("owner/repo", "model-q4_k_m.gguf", body.Length, hash);

    private string Destination => Path.Combine(_folder, "model-q4_k_m.gguf");

    [Fact]
    public async Task AFileArrivesAndIsVerifiedAgainstThePublishedHash()
    {
        var body = System.Text.Encoding.UTF8.GetBytes(new string('a', 5000));
        var http = new HttpClient(new Serving(body));

        var outcome = await new ModelDownloader(http)
            .DownloadAsync(FileOf(body, Sha256Of(body)), Destination, null, CancellationToken.None);

        Assert.Equal(DownloadOutcome.Verified, outcome);
        Assert.Equal(body, await File.ReadAllBytesAsync(Destination));
    }

    /// <summary>Without a published hash the file is kept, and the answer says it was not checked.</summary>
    [Fact]
    public async Task AFileWithNoPublishedHashIsKeptAndSaidToBeUnverified()
    {
        var body = System.Text.Encoding.UTF8.GetBytes("no hash for this one");
        var http = new HttpClient(new Serving(body));

        var outcome = await new ModelDownloader(http)
            .DownloadAsync(FileOf(body, null), Destination, null, CancellationToken.None);

        Assert.Equal(DownloadOutcome.Unverified, outcome);
        Assert.True(File.Exists(Destination));
    }

    /// <summary>A file that does not match its hash is refused and not left behind.</summary>
    [Fact]
    public async Task AFileThatDoesNotMatchItsHashIsNotKept()
    {
        var body = System.Text.Encoding.UTF8.GetBytes("the bytes that actually arrive");
        var http = new HttpClient(new Serving(body));

        var wrong = FileOf(body, Sha256Of(System.Text.Encoding.UTF8.GetBytes("something else")));

        await Assert.ThrowsAsync<DownloadFailedException>(
            () => new ModelDownloader(http).DownloadAsync(wrong, Destination, null, CancellationToken.None));

        Assert.False(File.Exists(Destination), "a corrupt download was moved into the models folder");
        Assert.False(File.Exists(ModelDownloader.PartFileFor(Destination)), "the corrupt part file was left behind");
    }

    /// <summary>An interrupted download resumes from what is already on disk.</summary>
    [Fact]
    public async Task ItResumesFromWhatIsAlreadyThere()
    {
        var body = System.Text.Encoding.UTF8.GetBytes(new string('b', 4000));
        var already = body[..1500];

        await File.WriteAllBytesAsync(ModelDownloader.PartFileFor(Destination), already);

        var handler = new Serving(body);

        var outcome = await new ModelDownloader(new HttpClient(handler))
            .DownloadAsync(FileOf(body, Sha256Of(body)), Destination, null, CancellationToken.None);

        Assert.Equal(1500L, handler.AskedFrom);
        Assert.Equal(DownloadOutcome.Verified, outcome);
        Assert.Equal(body, await File.ReadAllBytesAsync(Destination));
    }

    /// <summary>
    /// A server that ignores the range restarts rather than splicing two halves together.
    /// </summary>
    /// <remarks>
    /// The case that silently corrupts. Asking to resume from 1500 and being sent the whole file
    /// means appending produces a file of the right length made of the wrong bytes, which would
    /// pass every check except the hash.
    /// </remarks>
    [Fact]
    public async Task AServerThatIgnoresTheRangeStartsAgain()
    {
        var body = System.Text.Encoding.UTF8.GetBytes(new string('c', 4000));

        await File.WriteAllBytesAsync(ModelDownloader.PartFileFor(Destination), body[..1500]);

        var http = new HttpClient(new Serving(body, honoursRanges: false));

        var outcome = await new ModelDownloader(http)
            .DownloadAsync(FileOf(body, Sha256Of(body)), Destination, null, CancellationToken.None);

        Assert.Equal(DownloadOutcome.Verified, outcome);
        Assert.Equal(body.Length, new FileInfo(Destination).Length);
    }

    /// <summary>A part file larger than the finished file is not a resumable part of it.</summary>
    [Fact]
    public async Task AnOversizedPartFileIsThrownAwayRatherThanResumed()
    {
        var body = System.Text.Encoding.UTF8.GetBytes(new string('d', 2000));

        await File.WriteAllBytesAsync(ModelDownloader.PartFileFor(Destination), new byte[9000]);

        var handler = new Serving(body);

        var outcome = await new ModelDownloader(new HttpClient(handler))
            .DownloadAsync(FileOf(body, Sha256Of(body)), Destination, null, CancellationToken.None);

        Assert.Null(handler.AskedFrom);
        Assert.Equal(DownloadOutcome.Verified, outcome);
    }

    /// <summary>Nothing is called a model until it is complete.</summary>
    [Fact]
    public void AnUnfinishedDownloadDoesNotUseTheRealName()
    {
        Assert.EndsWith(ModelDownloader.PartExtension, ModelDownloader.PartFileFor(Destination), StringComparison.Ordinal);
        Assert.DoesNotContain(ModelDownloader.PartExtension, Destination, StringComparison.Ordinal);
    }

    /// <summary>Progress is reported, and it reaches the end.</summary>
    [Fact]
    public async Task ProgressIsReportedAndFinishesAtTheTotal()
    {
        var body = System.Text.Encoding.UTF8.GetBytes(new string('e', 40000));
        var seen = new List<DownloadProgress>();

        await new ModelDownloader(new HttpClient(new Serving(body))).DownloadAsync(
            FileOf(body, Sha256Of(body)),
            Destination,
            new Progress<DownloadProgress>(p => seen.Add(p)),
            CancellationToken.None);

        // Progress is raised on the synchronisation context, so a moment is allowed for it.
        await Task.Delay(150);

        Assert.NotEmpty(seen);
        Assert.Equal(body.Length, seen[^1].BytesSoFar);
        Assert.Equal(1d, seen[^1].Fraction);
    }

    /// <summary>Discarding removes the partial file, so nothing large is orphaned.</summary>
    [Fact]
    public async Task DiscardingRemovesThePartialFile()
    {
        var part = ModelDownloader.PartFileFor(Destination);
        await File.WriteAllBytesAsync(part, new byte[1024]);

        ModelDownloader.DiscardPartial(Destination);

        Assert.False(File.Exists(part));
    }

    /// <summary>Resumable bytes reports what is there, and nothing when there is not.</summary>
    [Fact]
    public async Task ResumableBytesReportsWhatIsOnDisk()
    {
        Assert.Equal(0L, ModelDownloader.ResumableBytes(Destination));

        await File.WriteAllBytesAsync(ModelDownloader.PartFileFor(Destination), new byte[777]);

        Assert.Equal(777L, ModelDownloader.ResumableBytes(Destination));
    }

    /// <summary>A gated repository is reported as gated rather than retried.</summary>
    [Fact]
    public async Task AGatedRepositoryIsNamedAsGated()
    {
        var http = new HttpClient(new Refusing(HttpStatusCode.Forbidden));

        var thrown = await Assert.ThrowsAsync<GatedRepositoryException>(
            () => new ModelDownloader(http).DownloadAsync(
                FileOf(new byte[10], null), Destination, null, CancellationToken.None));

        Assert.Contains("gated", thrown.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("huggingface.co/owner/repo", thrown.PageUrl, StringComparison.Ordinal);
    }

    private sealed class Refusing : HttpMessageHandler
    {
        private readonly HttpStatusCode _code;

        public Refusing(HttpStatusCode code) => _code = code;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(_code) { Content = new StringContent(string.Empty) });
    }

    /// <summary>The fit estimate is the one the model node already uses.</summary>
    [Fact]
    public void AQuantFileIsJudgedAgainstTheCardTheSameWayAModelNodeIs()
    {
        var small = new ModelFileOption("owner/repo", "m-q4_k_m.gguf", 4L * 1024 * 1024 * 1024, null);
        var huge = new ModelFileOption("owner/repo", "m-f16.gguf", 60L * 1024 * 1024 * 1024, null);

        Assert.Equal(FitVerdict.Fits, ModelFit.Verdict(small.SizeGb, 8192, 24d));
        Assert.Equal(FitVerdict.TooLarge, ModelFit.Verdict(huge.SizeGb, 8192, 12d));
    }

    /// <summary>A quantization is read from the file name the same way the catalogue reads it.</summary>
    [Fact]
    public void TheQuantisationComesFromTheFileName()
    {
        Assert.Equal("Q4_K_M", new ModelFileOption("o/r", "thing-q4_k_m.gguf", 1, null).Quantisation);
        Assert.Equal("Q2_K", new ModelFileOption("o/r", "thing-q2_k.gguf", 1, null).Quantisation);
    }

    /// <summary>One part of a split model is named as a part rather than offered as a choice.</summary>
    [Fact]
    public void APartOfASplitModelIsRecognised()
    {
        Assert.True(new ModelFileOption("o/r", "m-q4_0-00001-of-00002.gguf", 1, null).IsOnePartOfSeveral);
        Assert.False(new ModelFileOption("o/r", "m-q4_0.gguf", 1, null).IsOnePartOfSeveral);
    }

    /// <summary>The tool calling note is a note, and unknown is a real answer.</summary>
    [Fact]
    public void TheToolCallingNoteDoesNotOverclaim()
    {
        Assert.Equal(ToolCallingExpectation.Likely, ToolCallingFamilies.Expect("Qwen2.5-Coder-7B-Instruct-GGUF"));
        Assert.Equal(ToolCallingExpectation.Unlikely, ToolCallingFamilies.Expect("llama-3.1-8b-base"));
        Assert.Equal(ToolCallingExpectation.Unknown, ToolCallingFamilies.Expect("some-model-nobody-listed"));

        // Even the confident answer says to check, because the only real check needs it running.
        Assert.Contains("Check it", ToolCallingFamilies.Describe("Qwen2.5-Coder-7B-Instruct"), StringComparison.Ordinal);
    }
}
