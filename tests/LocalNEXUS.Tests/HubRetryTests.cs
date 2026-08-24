using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using LocalNEXUS.App.Services.Models;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// Asking Hugging Face again when the answer was a dropped connection rather than an answer.
/// </summary>
/// <remarks>
/// Measured rather than assumed: connections to huggingface.co from the machine this was written
/// on were reset mid response between a third and a half of the time, while github.com and
/// google.com did not fail once. The application sent one request, caught the reset, and reported
/// that Hugging Face could not be reached, which was true of that attempt and false of Hugging
/// Face.
///
/// Nothing here touches the network. The point of the retry being a function over a send delegate
/// is that the delegate can be a counter.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class HubRetryTests
{
    private static HttpResponseMessage Status(HttpStatusCode code) => new(code);

    /// <summary>The reset actually seen, wrapped the way the runtime wraps it.</summary>
    private static Exception Reset()
        => new HttpRequestException(
            "An error occurred while sending the request.",
            new IOException(
                "Unable to read data from the transport connection.",
                new SocketException((int)SocketError.ConnectionReset)));

    [Fact]
    public async Task ItGivesUpAfterThree()
    {
        var attempts = 0;

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() =>
            HubRetry.SendAsync(
                _ =>
                {
                    attempts++;
                    throw Reset();
                },
                CancellationToken.None));

        Assert.Equal(3, attempts);
        Assert.Equal(HubRetry.Attempts, attempts);
        Assert.IsType<IOException>(thrown.InnerException);
    }

    [Fact]
    public async Task ItStopsAsSoonAsOneWorks()
    {
        var attempts = 0;

        var response = await HubRetry.SendAsync(
            _ =>
            {
                attempts++;
                return attempts < 2
                    ? throw Reset()
                    : Task.FromResult(Status(HttpStatusCode.OK));
            },
            CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// A 4xx is an answer and is returned on the first attempt.
    /// </summary>
    /// <remarks>
    /// 404 means the repository is not there and 401 means it is gated. Asking twice more changes
    /// neither, and only makes the interface slower to say so.
    /// </remarks>
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ItDoesNotRetryAnAnswer(HttpStatusCode code)
    {
        var attempts = 0;

        var response = await HubRetry.SendAsync(
            _ =>
            {
                attempts++;
                return Task.FromResult(Status(code));
            },
            CancellationToken.None);

        Assert.Equal(1, attempts);
        Assert.Equal(code, response.StatusCode);
    }

    /// <summary>A 5xx and a 429 are the server saying ask again, so they are asked again.</summary>
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ItRetriesAServerSayingLater(HttpStatusCode code)
    {
        var attempts = 0;

        await HubRetry.SendAsync(
            _ =>
            {
                attempts++;
                return Task.FromResult(Status(code));
            },
            CancellationToken.None);

        Assert.Equal(3, attempts);
    }

    /// <summary>Cancellation is honoured between attempts, not only during one.</summary>
    [Fact]
    public async Task ItStopsWhenCancelledBetweenAttempts()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            HubRetry.SendAsync(
                _ =>
                {
                    attempts++;
                    cancellation.Cancel();
                    throw Reset();
                },
                cancellation.Token));

        Assert.Equal(1, attempts);
    }

    /// <summary>Something that is not a transport failure is not retried and comes straight back.</summary>
    [Fact]
    public async Task ItDoesNotRetryWhatItDoesNotUnderstand()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            HubRetry.SendAsync(
                _ =>
                {
                    attempts++;
                    throw new InvalidOperationException("not a network problem");
                },
                CancellationToken.None));

        Assert.Equal(1, attempts);
    }

    /// <summary>The reset is recognised however deep in the chain it sits.</summary>
    [Fact]
    public void ItRecognisesAResetThroughTheWholeChain()
    {
        Assert.True(HubRetry.WorthRetrying(Reset()));
        Assert.True(HubRetry.WorthRetrying(new SocketException((int)SocketError.ConnectionReset)));
        Assert.False(HubRetry.WorthRetrying(new InvalidOperationException()));
        Assert.False(HubRetry.WorthRetrying(new FormatException()));
    }
}
