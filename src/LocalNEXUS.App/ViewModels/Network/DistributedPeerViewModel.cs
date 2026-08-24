using System.Net;

namespace LocalNEXUS.App.ViewModels.Network;

/// <summary>
/// One machine a distributed pipeline may be planned across, as the user typed it.
/// </summary>
/// <remarks>
/// Deliberately just an address. Nothing is known about the machine until the pipeline is built
/// and it is asked what it has, which is the coordinator's job and not this one's. Adding a row
/// here does not start anything, contact anything, or reserve anything: a peer is a process
/// somebody runs on that machine, and this is a note of where to look for it.
/// </remarks>
public sealed class DistributedPeerViewModel
{
    public DistributedPeerViewModel(string address) => Address = address.Trim();

    /// <summary>Where the peer listens, as <c>host:port</c>.</summary>
    public string Address { get; }

    /// <summary>
    /// Whether a typed value is shaped like an address this could use.
    /// </summary>
    /// <remarks>
    /// Shape only. Whether anything is actually there is not knowable without connecting, and
    /// connecting to find out would make typing in a text box a network operation. The
    /// coordinator asks each machine when it builds the pipeline and leaves out whatever does
    /// not answer, so a wrong entry costs a line in the log rather than a failure.
    /// </remarks>
    public static bool IsWellFormed(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var trimmed = candidate.Trim();
        var separator = trimmed.LastIndexOf(':');

        if (separator <= 0 || separator == trimmed.Length - 1)
        {
            return false;
        }

        var host = trimmed[..separator];
        var port = trimmed[(separator + 1)..];

        if (!int.TryParse(port, out var number) || number is < 1 or > 65535)
        {
            return false;
        }

        // A name or an address, both of which are ordinary here: a machine on a home network is
        // as likely to be reachable by name as by number.
        return IPAddress.TryParse(host, out _) || Uri.CheckHostName(host) != UriHostNameType.Unknown;
    }
}
