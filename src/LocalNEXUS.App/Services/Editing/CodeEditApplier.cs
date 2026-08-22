using System.Text.RegularExpressions;

namespace LocalNEXUS.App.Services.Editing;

/// <summary>
/// Turns whatever a coder replied with into the full contents of a file.
/// </summary>
/// <remarks>
/// The caller says which format it asked for, but the reply decides what it actually is. A model
/// asked for a diff that returns a whole file has done something useful and is taken at its word;
/// a model asked for a whole file that returns a diff has not, and is also taken at its word. The
/// alternative, failing because the reply was the wrong shape, throws away a correct answer over
/// a formatting preference.
/// </remarks>
public static class CodeEditApplier
{
    /// <summary>
    /// How large a file can be and still be rewritten whole by a model known to write diffs well.
    /// </summary>
    /// <remarks>
    /// A short file costs little to resend and a diff against one has almost no context to anchor
    /// to, so even a capable model is asked for the whole thing here. Above it, a diff is what a
    /// capable model is for.
    /// </remarks>
    public const int WholeFileThreshold = 3000;

    /// <summary>
    /// How large a file can be and still be rewritten whole by any other model.
    /// </summary>
    /// <remarks>
    /// Roughly four hundred lines, which is where Cursor put the same line: rewriting outperforms
    /// diff style edits below it. The published numbers for the models this actually runs are
    /// worse than that suggests, so the threshold is set where a whole file still plausibly fits a
    /// reply rather than where the two approaches break even.
    ///
    /// The cost of being wrong is not symmetric. A whole file too large for the reply ceiling comes
    /// back truncated and is refused, loudly. A diff a model could not write comes back looking
    /// perfectly well formed and pointing at lines that do not exist.
    /// </remarks>
    public const int UnknownModelWholeFileThreshold = 16_000;

    /// <summary>
    /// Comment markers a model uses when it stops writing a file and says it carried on.
    /// </summary>
    /// <remarks>
    /// A reply carrying one of these is a file with real code cut out of the middle, and writing it
    /// would delete whatever it stood for. Every one of these needs an ellipsis as well as a word,
    /// so an ordinary comment that happens to say "the rest" is not caught.
    /// </remarks>
    private static readonly string[] ElisionWords =
    {
        "rest of", "remaining", "unchanged", "as before", "same as", "omitted", "snip",
        "existing code", "previous code", "other members", "and so on", "etc"
    };

    private static readonly Regex FencedBlock = new(
        @"(?s)^\s*```[A-Za-z0-9#+_-]*\s*\r?\n(.*?)\r?\n?```\s*$",
        RegexOptions.Compiled);

    /// <summary>
    /// Which format a task should be asked for, given the setting, the file and the model.
    /// </summary>
    /// <remarks>
    /// The threshold follows the model rather than the format following it, because for a small
    /// file the whole thing is the right answer either way and the only real question is how far up
    /// that holds. A model nothing establishes about is trusted with a diff much later than one the
    /// published benchmarks put in the band that writes them.
    /// </remarks>
    public static bool WantsWholeFile(
        EditFormat format,
        bool isNewFile,
        int existingLength,
        EditCapability capability = EditCapability.Unknown) => format switch
    {
        EditFormat.WholeFile => true,
        EditFormat.LineTaggedDiff => false,
        _ => isNewFile || existingLength < ThresholdFor(capability)
    };

    /// <summary>How large a whole file reply is worth asking this model for.</summary>
    public static int ThresholdFor(EditCapability capability)
        => capability == EditCapability.HandlesDiffs ? WholeFileThreshold : UnknownModelWholeFileThreshold;

    /// <summary>Lines, however the reply spelled its line endings.</summary>
    private static string[] SplitLines(string text)
        => text.ReplaceLineEndings("\n").Split('\n');

    /// <summary>
    /// True when a reply has stopped writing the file and left a note saying the rest is unchanged.
    /// </summary>
    /// <remarks>
    /// This is the one whole file failure that is worse than a bad diff. A diff that does not match
    /// is refused; an elided file matches nothing, applies perfectly, and silently deletes every
    /// member the model could not be bothered to repeat. It is treated as a failed edit and goes
    /// back through the retry, which is where a model gets told what it did.
    /// </remarks>
    public static bool LooksElided(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        foreach (var raw in SplitLines(content))
        {
            var line = raw.Trim();

            var isComment = line.StartsWith("//", StringComparison.Ordinal)
                            || line.StartsWith("/*", StringComparison.Ordinal)
                            || line.StartsWith("*", StringComparison.Ordinal)
                            || line.StartsWith("#", StringComparison.Ordinal);

            if (!isComment)
            {
                continue;
            }

            if (!line.Contains("...", StringComparison.Ordinal)
                && !line.Contains('…', StringComparison.Ordinal))
            {
                continue;
            }

            if (ElisionWords.Any(word => line.Contains(word, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Applies a reply to the file it was written against.
    /// </summary>
    /// <param name="reply">What the coder returned.</param>
    /// <param name="existingContent">The current file, or null when it is being created.</param>
    /// <exception cref="EditApplyException">The reply was empty, or a change block did not match.</exception>
    public static string Apply(string? reply, string? existingContent)
    {
        var body = Unfence(reply ?? string.Empty);

        if (body.Trim().Length == 0)
        {
            throw new EditApplyException("The coder returned nothing, so there is no change to apply.");
        }

        if (!LineTaggedDiff.LooksLikeDiff(body))
        {
            if (LooksElided(body))
            {
                throw new EditApplyException(
                    "The reply stopped part way through and left a comment saying the rest of the file "
                    + "is unchanged. Writing that would delete everything it stood for. Return the "
                    + "complete file with every member written out.");
            }

            return Normalise(body);
        }

        if (string.IsNullOrEmpty(existingContent))
        {
            // A diff against a file that does not exist yet can only mean its added lines.
            var added = LineTaggedDiff.Parse(body).SelectMany(h => h.After).ToList();

            if (added.Count == 0)
            {
                throw new EditApplyException("The coder returned a diff for a new file, and it added no lines.");
            }

            return Normalise(string.Join(Environment.NewLine, added));
        }

        return Normalise(LineTaggedDiff.Apply(existingContent, LineTaggedDiff.Parse(body)));
    }

    /// <summary>
    /// Strips a surrounding markdown fence. Models add one despite being asked not to often
    /// enough that treating it as an error would be a choice to fail on purpose.
    /// </summary>
    public static string Unfence(string reply)
    {
        var match = FencedBlock.Match(reply);
        return match.Success ? match.Groups[1].Value : reply;
    }

    /// <summary>
    /// A file ends with exactly one newline. Unity does not care, but a project where half the
    /// generated files disagree produces diffs full of noise.
    /// </summary>
    private static string Normalise(string content)
        => content.TrimEnd('\r', '\n', ' ', '\t') + Environment.NewLine;
}
