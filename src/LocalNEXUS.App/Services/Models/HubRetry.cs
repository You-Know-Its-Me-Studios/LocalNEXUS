using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace LocalNEXUS.App.Services.Models;

/// <summary>
/// Sends a request to Hugging Face more than once before believing it.
/// </summary>
/// <remarks>
/// A connection reset is not an answer. The route to this host drops a third to a half of its
/// connections mid response on at least one machine, and the application treated the first failure
/// as final, so a search that would have worked on the second attempt was reported as Hugging Face
/// being unreachable.
///
/// Three attempts, which is chosen against the measurement rather than picked: at a per attempt
/// failure rate of one half, three attempts leaves roughly one search in eight failing, and at the
/// rate seen after the address family fix it is far below that. More attempts would buy very
/// little and spend somebody's time while they watch a spinner.
///
/// What is not retried matters as much. A status is an answer: 404 means it is not there and 401
/// means it is gated, and asking again three times changes neither while making the interface
/// slower to tell the truth. Only a transport failure is retried, and only a status of 5xx or 429,
/// which are the server saying ask again.
/// </remarks>
public static class HubRetry
{
    /// <summary>How many times a request is sent before its failure is believed.</summary>
    public const int Attempts = 3;

    /// <summary>The wait before the second attempt. The third waits twice this, plus jitter.</summary>
    private static readonly TimeSpan Backoff = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Sends until it works, until a status arrives that is an answer, or until attempts run out.
    /// </summary>
    /// <param name="send">Builds and sends one attempt. Called once per attempt, because a request
    /// message cannot be sent twice.</param>
    /// <param name="ct">Checked between attempts as well as during them.</param>
    /// <returns>The response of the last attempt.</returns>
    /// <exception cref="Exception">Whatever the last attempt threw, when every attempt threw.</exception>
    public static async Task<HttpResponseMessage> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        CancellationToken ct,
        int attempts = Attempts)
    {
        Exception? last = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var response = await send(ct).ConfigureAwait(false);

                if (attempt == attempts || !WorthRetrying(response.StatusCode))
                {
                    return response;
                }

                // The body is not going to be read, and the connection is wanted back.
                response.Dispose();
                last = null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (WorthRetrying(ex))
            {
                last = ex;

                if (attempt == attempts)
                {
                    throw;
                }
            }

            await DelayAsync(attempt, ct).ConfigureAwait(false);
        }

        // Unreachable: the loop either returns, throws, or runs its last attempt above.
        throw last ?? new HttpRequestException("The request was not attempted.");
    }

    /// <summary>True for a transport failure, which is the kind worth asking again about.</summary>
    /// <remarks>
    /// A reset is the one actually seen here, and it arrives wrapped: HttpRequestException over an
    /// IOException over a SocketException. The whole chain is walked rather than the outer type
    /// trusted, because which layer the reset surfaces at depends on how far the response had got.
    ///
    /// A TaskCanceledException with no cancellation asked for is the client timing out, which is
    /// also worth one more go. One that was asked for never reaches here.
    /// </remarks>
    public static bool WorthRetrying(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            switch (ex)
            {
                case SocketException socket
                    when socket.SocketErrorCode is SocketError.ConnectionReset
                        or SocketError.ConnectionAborted
                        or SocketError.TimedOut
                        or SocketError.HostUnreachable
                        or SocketError.NetworkUnreachable
                        or SocketError.TryAgain:
                    return true;

                case IOException:
                case HttpRequestException:
                case TaskCanceledException:
                    return true;
            }
        }

        return false;
    }

    /// <summary>True for a status that means ask again rather than an answer.</summary>
    public static bool WorthRetrying(HttpStatusCode status)
        => status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    /// <summary>
    /// Waits before the next attempt, longer each time, with jitter.
    /// </summary>
    /// <remarks>
    /// Jittered because every copy of this application retrying on the same schedule is how a
    /// service that is briefly struggling is kept struggling.
    /// </remarks>
    private static Task DelayAsync(int attempt, CancellationToken ct)
    {
        var backoff = Backoff * Math.Pow(2, attempt - 1);
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 150));

        return Task.Delay(backoff + jitter, ct);
    }
}
