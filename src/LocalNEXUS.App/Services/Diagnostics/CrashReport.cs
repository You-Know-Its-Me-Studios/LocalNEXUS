using System.IO;
using System.Text;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.Services.Diagnostics;

/// <summary>
/// One crash log, and what may be said about it in public.
/// </summary>
/// <remarks>
/// A crash log is written for the person who owns the machine, so it says whatever the exception
/// said: full paths, the account name inside them, and the name of whatever project was open.
/// Offering to post that to a public issue tracker is offering to publish it, so what goes into
/// the issue is built here and is not the file.
///
/// Two rules. Nothing leaves this machine without somebody choosing it, which is why this only
/// ever produces a link for a browser and never sends anything. And what the link carries is
/// scrubbed and then shown, so the choice is made with the text in front of them rather than a
/// promise about it.
/// </remarks>
public sealed class CrashReport
{
    /// <summary>
    /// How much of the log is carried into the issue body.
    /// </summary>
    /// <remarks>
    /// A browser will not follow an unbounded URL, and the useful part of a stack trace is the
    /// top. Four thousand characters is comfortably inside what every browser accepts once
    /// escaped, and is several times more than the frames anybody reads.
    /// </remarks>
    private const int ExcerptLimit = 4000;

    private CrashReport(string path, DateTimeOffset written, string excerpt)
    {
        Path = path;
        Written = written;
        Excerpt = excerpt;
    }

    /// <summary>The file this came from, on this machine.</summary>
    public string Path { get; }

    /// <summary>When it was written.</summary>
    public DateTimeOffset Written { get; }

    /// <summary>
    /// The scrubbed excerpt, which is exactly what an issue would carry.
    /// </summary>
    /// <remarks>
    /// Shown before anything is opened. What is in this string is what would be published, so
    /// there is nothing to take on trust.
    /// </remarks>
    public string Excerpt { get; }

    /// <summary>Reads a crash log and prepares what could be said about it.</summary>
    public static CrashReport? Read(string path)
    {
        try
        {
            var written = File.GetLastWriteTime(path);
            var content = File.ReadAllText(path);

            return new CrashReport(path, written, Scrub(Shorten(content)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A crash log that cannot be read is not worth a second failure on top of the first.
            return null;
        }
    }

    /// <summary>
    /// Removes what identifies the machine and the person using it.
    /// </summary>
    /// <remarks>
    /// The account name is the one that matters, because it appears in every path in every stack
    /// frame and is very often somebody's real name. It is replaced rather than the whole path
    /// removed, since which directory a file was in is frequently the thing that explains the
    /// crash.
    ///
    /// This is not a promise of anonymity and is not offered as one. A stack trace can still name
    /// a project, a file somebody wrote, or a model. That is why the text is shown before it goes
    /// anywhere rather than described.
    /// </remarks>
    public static string Scrub(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        var scrubbed = new StringBuilder(content);

        // Longest first, so the deeper paths are replaced before the profile they sit inside.
        foreach (var (path, replacement) in Identifying())
        {
            if (path.Length > 0)
            {
                scrubbed.Replace(path, replacement);
            }
        }

        var user = Environment.UserName;

        if (user.Length > 2)
        {
            scrubbed.Replace(user, "<user>");
        }

        return scrubbed.ToString();
    }

    private static IEnumerable<(string Path, string Replacement)> Identifying()
    {
        var candidates = new List<(string Path, string Replacement)>
        {
            (AppPaths.Root, "<appdata>"),
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "<localappdata>"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "<appdata>"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "<home>")
        };

        return candidates
            .Where(c => !string.IsNullOrEmpty(c.Path))
            .OrderByDescending(c => c.Path.Length);
    }

    private static string Shorten(string content)
        => content.Length <= ExcerptLimit
            ? content
            : content[..ExcerptLimit] + Environment.NewLine
              + $"... trimmed here. The whole log is on this machine at {AppPaths.Logs}.";

    /// <summary>What the issue would be titled.</summary>
    public string Title => "Crash on " + Written.ToString("yyyy-MM-dd");

    /// <summary>
    /// The whole issue body, exactly as it would be posted.
    /// </summary>
    /// <remarks>
    /// Built here rather than in the view so that what is shown and what is sent are one string
    /// and cannot drift into disagreeing.
    /// </remarks>
    public string Body =>
        "**What I was doing when it crashed**" + Environment.NewLine + Environment.NewLine
        + "(please replace this line)" + Environment.NewLine + Environment.NewLine
        + "**Crash log**" + Environment.NewLine + Environment.NewLine
        + "```" + Environment.NewLine
        + Excerpt + Environment.NewLine
        + "```" + Environment.NewLine;
}
