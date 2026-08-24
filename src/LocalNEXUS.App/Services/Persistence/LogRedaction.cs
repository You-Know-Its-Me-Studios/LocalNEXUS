using System.Text.RegularExpressions;

namespace LocalNEXUS.App.Services.Persistence;

/// <summary>
/// Removes credentials from engine output before it is written to a log file.
/// </summary>
/// <remarks>
/// Engine processes print what they consider useful, and one of the things Mesh LLM considers
/// useful is the invite token it just created. That token is the credential for joining a
/// private mesh, and the log file it lands in is plain text, kept in a folder the bug report
/// template points people at. So a support request became a way to publish mesh access, which
/// is why this exists.
///
/// It matches the token rather than the sentence around it. The wording of an engine's log line
/// belongs to the engine and will change without warning; the shape of a base64 encoded JSON
/// document will not. That also means a token printed by some future line nobody anticipated is
/// caught by the same rule.
/// </remarks>
public static partial class LogRedaction
{
    /// <summary>What replaces a token, chosen to be obvious rather than to look like data.</summary>
    private const string Marker = "[invite token removed by LocalNEXUS]";

    /// <summary>
    /// A base64 encoded JSON document, which is what every one of these tokens is.
    /// </summary>
    /// <remarks>
    /// <c>eyJ</c> is what <c>{"</c> encodes to, so this anchors on the token being JSON rather
    /// than on it being long. That distinction matters: the mesh prints its own identifier as a
    /// sixty four character hex string on the same line, hex is a subset of the base64 alphabet,
    /// and a rule written around length alone would redact the one field that makes the line
    /// worth keeping.
    /// </remarks>
    [GeneratedRegex(@"eyJ[A-Za-z0-9_\-+/=]{16,}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    /// <summary>
    /// One line of engine output with any credential in it replaced.
    /// </summary>
    /// <remarks>
    /// Applied on the way into the log rather than on the way out of it, so that a token is
    /// never written to disk at all. Scrubbing at read time would leave the original sitting in
    /// the file for anyone who opened it directly, which is most people.
    /// </remarks>
    public static string Scrub(string line)
        => string.IsNullOrEmpty(line) ? line : TokenPattern().Replace(line, Marker);
}
