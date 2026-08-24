using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using LocalNEXUS.App.Services.Models;
using LocalNEXUS.App.ViewModels;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// The Discover surface: reading a model card, and resuming a download that was cut off.
/// </summary>
/// <remarks>
/// Nothing here touches the network. The downloader takes an HttpClient, so a handler that drops
/// the connection halfway through the body is the whole of the reset, reproduced exactly and
/// without waiting for the real one to happen.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class ModelDiscoverTests
{
    // ---------------------------------------------------------------- the model card

    [Fact]
    public void ACardBecomesTheBlocksItIsMadeOf()
    {
        var blocks = ModelCard.Parse(
            "# Qwen3 Coder\n"
            + "\n"
            + "A **coding** model with `tools`.\n"
            + "Second line of the same paragraph.\n"
            + "\n"
            + "## Files\n"
            + "\n"
            + "- one\n"
            + "- two\n"
            + "\n"
            + "---\n"
            + "\n"
            + "```\n"
            + "llama-server -m model.gguf\n"
            + "```\n");

        Assert.Equal(CardBlockKind.Heading, blocks[0].Kind);
        Assert.Equal("Qwen3 Coder", blocks[0].Text);
        Assert.Equal(1, blocks[0].Level);

        // Two source lines, one paragraph, and the inline markup is gone rather than shown.
        Assert.Equal(CardBlockKind.Paragraph, blocks[1].Kind);
        Assert.Equal("A coding model with tools. Second line of the same paragraph.", blocks[1].Text);

        Assert.Equal(2, blocks[2].Level);
        Assert.Equal(CardBlockKind.Bullet, blocks[3].Kind);
        Assert.Equal("one", blocks[3].Text);
        Assert.Equal(CardBlockKind.Rule, blocks[5].Kind);
        Assert.Equal(CardBlockKind.Code, blocks[6].Kind);
        Assert.Equal("llama-server -m model.gguf", blocks[6].Text);
    }

    /// <summary>
    /// Front matter is kept, as code, because it is where the licence is declared.
    /// </summary>
    [Fact]
    public void TheFrontMatterIsShownRatherThanHidden()
    {
        var blocks = ModelCard.Parse("---\nlicense: apache-2.0\n---\n\nHello.\n");

        Assert.Equal(CardBlockKind.Code, blocks[0].Kind);
        Assert.Contains("license: apache-2.0", blocks[0].Text, StringComparison.Ordinal);
        Assert.Equal("Hello.", blocks[1].Text);
    }

    /// <summary>
    /// A card cannot draw whatever it likes inside this window.
    /// </summary>
    /// <remarks>
    /// A model card is text written by a stranger. Tags are removed rather than rendered, links
    /// keep their words and lose their target, and an image becomes its alt text rather than a
    /// request to somebody's server.
    /// </remarks>
    [Fact]
    public void ACardCannotSmuggleMarkupIn()
    {
        Assert.Equal("click here", ModelCard.Inline("[click here](https://example.com/tracking)"));
        Assert.Equal("a diagram", ModelCard.Inline("![a diagram](https://example.com/pixel.png)"));
        Assert.Equal("alert(1)", ModelCard.Inline("<script>alert(1)</script>"));

        // A real card from Hugging Face carried a signed URL inside an img tag. The pattern
        // used to be length bounded, so the whole tag was rendered as prose.
        Assert.Equal(
            string.Empty,
            ModelCard.Inline(
                "<img width=\"600\" alt=\"qwen unsloth desktop\" src=\"https://example.com/"
                + new string('a', 400) + "\">"));
        Assert.Equal("bold and italic", ModelCard.Inline("**bold** and _italic_"));
    }

    [Fact]
    public void NothingIsMadeUpFromAnEmptyCard()
    {
        Assert.Empty(ModelCard.Parse(null));
        Assert.Empty(ModelCard.Parse("   "));
    }

    // ---------------------------------------------------------------- the fit filter

    private static ModelFileViewModel Candidate(double sizeGb, double cardGb)
        => new(
            new ModelFileOption("owner/repo", "model.gguf", (long)(sizeGb * 1024 * 1024 * 1024), null),
            cardGb,
            8192);

    /// <summary>
    /// What the filter keeps is what the machine could actually run.
    /// </summary>
    /// <remarks>
    /// The filter hides a file when WillNotFit is true, so this is the filter: a verdict of spills
    /// or too large is hidden, and fits or tight is kept. Spills is deliberately on the hidden
    /// side. It does run, and it runs slowly enough that somebody who asked to be shown only what
    /// fits did not mean it.
    /// </remarks>
    [Theory]
    [InlineData(2.0, 12.0, false, "fits")]
    [InlineData(40.0, 12.0, true, "will not fit")]
    public void TheFitFilterKeepsWhatThisMachineCouldRun(
        double sizeGb,
        double cardGb,
        bool hidden,
        string badge)
    {
        var file = Candidate(sizeGb, cardGb);

        Assert.Equal(hidden, file.WillNotFit);
        Assert.Equal(badge, file.FitBadge);

        // The filter itself, which is the one expression the view model applies.
        var all = new[] { Candidate(2.0, cardGb), Candidate(40.0, cardGb) };
        var kept = all.Where(f => !f.WillNotFit).ToList();

        Assert.Single(kept);
        Assert.False(kept[0].WillNotFit);
    }

    /// <summary>With no card detected nothing is claimed, and nothing is filtered away.</summary>
    [Fact]
    public void WithNoCardNothingIsHidden()
    {
        var file = new ModelFileViewModel(
            new ModelFileOption("owner/repo", "model.gguf", 40L * 1024 * 1024 * 1024, null),
            null,
            8192);

        Assert.False(file.WillNotFit);
        Assert.Equal("not known", file.FitBadge);
    }

    // ---------------------------------------------------------------- resuming

    /// <summary>Serves the file, but drops the first attempt part way through the body.</summary>
    private sealed class ResettingHandler : HttpMessageHandler
    {
        private readonly byte[] _content;

        public ResettingHandler(byte[] content) => _content = content;

        /// <summary>How many requests arrived, so the test can prove a second one did.</summary>
        public int Requests { get; private set; }

        /// <summary>The range each request asked for, in order.</summary>
        public List<long> AskedFrom { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct)
        {
            // Yield before touching the request, the way a real handler does. Reading it
            // synchronously hid a bug where the caller disposed the request while the send was
            // still in flight: every real download threw and every test passed.
            await Task.Yield();

            Requests++;

            var from = request.Headers.Range?.Ranges.FirstOrDefault()?.From ?? 0;
            AskedFrom.Add(from);

            var remaining = _content.Skip((int)from).ToArray();

            // First attempt: hand back a stream that throws where the reset happened.
            var body = Requests == 1
                ? (Stream)new BreakingStream(remaining, remaining.Length / 2)
                : new MemoryStream(remaining);

            var response = new HttpResponseMessage(
                from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new StreamContent(body)
            };

            response.Content.Headers.ContentLength = remaining.Length;

            return response;
        }
    }

    /// <summary>A stream that reads a while and then fails the way a reset connection does.</summary>
    private sealed class BreakingStream : MemoryStream
    {
        private readonly int _breakAt;

        public BreakingStream(byte[] content, int breakAt)
            : base(content) => _breakAt = breakAt;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (Position >= _breakAt)
            {
                throw new IOException(
                    "Unable to read data from the transport connection.",
                    new SocketException((int)SocketError.ConnectionReset));
            }

            return base.Read(buffer, offset, Math.Min(count, _breakAt - (int)Position));
        }

        public override int Read(Span<byte> buffer)
        {
            var rented = new byte[buffer.Length];
            var read = Read(rented, 0, buffer.Length);
            rented.AsSpan(0, read).CopyTo(buffer);
            return read;
        }
    }

    /// <summary>
    /// A download cut off halfway finishes, and asks for the rest rather than starting again.
    /// </summary>
    /// <remarks>
    /// This is the behaviour the retry was added for. Before it, one reset surfaced as a failed
    /// download, and the bytes already on disk were only picked up if somebody pressed the button
    /// a second time themselves.
    /// </remarks>
    [Fact]
    public async Task ADownloadCutOffPartWayCarriesOnFromThere()
    {
        using var project = SampleProject.Create();

        var content = Encoding.UTF8.GetBytes(new string('m', 200_000));
        var handler = new ResettingHandler(content);

        using var http = new HttpClient(handler);

        var file = new ModelFileOption("owner/repo", "model.gguf", content.Length, null);
        var destination = Path.Combine(project.Root, "model.gguf");

        var outcome = await new ModelDownloader(http)
            .DownloadAsync(file, destination, null, CancellationToken.None);

        Assert.Equal(DownloadOutcome.Unverified, outcome);
        Assert.True(File.Exists(destination));
        Assert.Equal(content.Length, new FileInfo(destination).Length);

        // The point: a second request, and it asked for what was missing rather than for all of it.
        Assert.True(handler.Requests >= 2, $"only {handler.Requests} request(s) were made");
        Assert.Equal(0, handler.AskedFrom[0]);
        Assert.True(handler.AskedFrom[1] > 0, "the second attempt started again from zero");
    }
}
