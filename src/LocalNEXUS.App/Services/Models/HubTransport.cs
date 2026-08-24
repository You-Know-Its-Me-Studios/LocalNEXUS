using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.Services.Models;

/// <summary>
/// The connection to Hugging Face: how it is dialled, and what is written down when it fails.
/// </summary>
/// <remarks>
/// Both halves of this exist because of a measurement rather than a theory. Search failed with
/// "could not be reached" often enough to look broken, and the application was throwing away the
/// only thing that could have explained it: the socket error went into an exception nobody read,
/// so there was nothing on disk and nothing in the feed, and the answer had to be found by running
/// curl by hand from outside the application.
///
/// What that found was a connection reset mid response, to huggingface.co and to nothing else, on
/// a machine that reached github.com and google.com without a single failure. Split by address
/// family over thirty attempts each, every failure was on IPv6:
///
///   IPv4  30 of 30 succeeded
///   IPv6  17 of 30 succeeded
///
/// So this dials IPv4 for that host. It is a workaround for something outside the application and
/// it is written down as one: the route is what is broken, and preferring the family that works is
/// the only part of it this side of the wire can do anything about. Nothing else is affected,
/// because the callback only looks at connections to Hugging Face.
/// </remarks>
public static class HubTransport
{
    /// <summary>The host the address family preference applies to.</summary>
    private const string Host = "huggingface.co";

    /// <summary>Where failures are written, appended rather than one file per failure.</summary>
    private static readonly string LogPath = Path.Combine(AppPaths.Logs, "hub.log");

    private static readonly object LogGate = new();

    /// <summary>
    /// Builds the client the catalogue and the downloader share.
    /// </summary>
    /// <remarks>
    /// One client for both, as before. The timeout covers headers rather than a whole download,
    /// because the downloader reads its response as a stream.
    /// </remarks>
    public static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            // The system proxy, as the default handler did. Nothing here opts out of it.
            ConnectCallback = ConnectAsync,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("LocalNEXUS");

        return client;
    }

    /// <summary>
    /// Opens the socket, preferring IPv4 for Hugging Face and leaving every other host alone.
    /// </summary>
    /// <remarks>
    /// It falls back to whatever resolves if there is no IPv4 address, rather than refusing: a
    /// machine with no IPv4 route to Hugging Face is not one this should decide cannot work.
    /// </remarks>
    private static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken ct)
    {
        var endPoint = context.DnsEndPoint;

        var addresses = string.Equals(endPoint.Host, Host, StringComparison.OrdinalIgnoreCase)
            ? await ResolveAsync(endPoint.Host, ct).ConfigureAwait(false)
            : Array.Empty<IPAddress>();

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            if (addresses.Length > 0)
            {
                await socket.ConnectAsync(addresses, endPoint.Port, ct).ConfigureAwait(false);
            }
            else
            {
                await socket.ConnectAsync(endPoint.Host, endPoint.Port, ct).ConfigureAwait(false);
            }

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>The IPv4 addresses for a host, or none when there are none to be had.</summary>
    private static async Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
    {
        try
        {
            var entries = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);

            return entries
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .ToArray();
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            // Let the ordinary connect path deal with a name that will not resolve.
            return Array.Empty<IPAddress>();
        }
    }

    /// <summary>
    /// Writes the whole exception chain, with a timestamp, and never throws while doing it.
    /// </summary>
    /// <remarks>
    /// Appended to one file rather than a file per failure, because these come in threes and a
    /// folder of them is harder to read than a list. The message a person sees stays calm and
    /// says a log exists; this is the log.
    /// </remarks>
    public static void LogFailure(string what, Exception exception)
    {
        try
        {
            AppPaths.EnsureCreated();

            var text =
                $"[{DateTimeOffset.Now:O}] {what}{Environment.NewLine}"
                + exception + Environment.NewLine + Environment.NewLine;

            lock (LogGate)
            {
                File.AppendAllText(LogPath, text);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Being unable to write down a network failure must not become a second failure.
        }
    }

    /// <summary>Where the log is, so a message can point at it.</summary>
    public static string LogLocation => LogPath;
}
