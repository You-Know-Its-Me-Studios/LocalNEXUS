using System.IO;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.Services.Diagnostics;

/// <summary>
/// Tells somebody, on the next launch, that the last one ended badly.
/// </summary>
/// <remarks>
/// One maintainer and no telemetry means a crash is invisible unless the person it happened to
/// goes looking for a log folder and then decides to write an issue about it. Almost nobody does
/// either, so the crashes that get reported are the ones somebody was annoyed enough to chase,
/// which is a bad sample of the crashes that happen.
///
/// This asks. It never sends: the most it does is open a browser at a page with the text already
/// filled in, which somebody can then read, edit or close. The distinction matters because a
/// crash log is written for the machine's owner and says whatever the exception said.
///
/// Asked once per crash. The last one reported is remembered, so declining is a decision that
/// sticks rather than a question that comes back every launch.
/// </remarks>
public sealed class CrashReporter
{
    /// <summary>Where an issue would be opened.</summary>
    private const string IssueUrl = "https://github.com/You-Know-Its-Me-Studios/LocalNEXUS/issues/new";

    private readonly AppConfig _config;
    private readonly IDialogService _dialogs;

    public CrashReporter(AppConfig config, IDialogService dialogs)
    {
        _config = config;
        _dialogs = dialogs;
    }

    /// <summary>
    /// The most recent crash nobody has been asked about, or null when there is none.
    /// </summary>
    /// <remarks>
    /// Only the newest. Somebody coming back after three crashes wants to be told once, and the
    /// most recent is the one they can still remember doing something to cause.
    /// </remarks>
    public CrashReport? Unreported()
    {
        try
        {
            if (!Directory.Exists(AppPaths.Logs))
            {
                return null;
            }

            var newest = new DirectoryInfo(AppPaths.Logs)
                .GetFiles("crash-*.log")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();

            if (newest is null || IsAlreadySeen(newest.Name))
            {
                return null;
            }

            return CrashReport.Read(newest.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Being unable to look for a crash report is not worth reporting.
            return null;
        }
    }

    private bool IsAlreadySeen(string fileName)
        => string.Equals(_config.LastReportedCrash, fileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Asks about the most recent crash, if there is one nobody has been asked about.
    /// </summary>
    /// <remarks>
    /// The excerpt goes in the question rather than behind a second click, because the thing
    /// being agreed to is publishing that text and it should be readable while deciding.
    /// </remarks>
    public void AskAboutAnyCrash()
    {
        if (Unreported() is not { } report)
        {
            return;
        }

        // Remembered before the question rather than after it. Declining has to stick, and so
        // does closing the dialog, and a crash on the way to the browser must not turn into the
        // same question every launch forever.
        Remember(report);

        var wants = _dialogs.Confirm(
            "LocalNEXUS crashed the last time it ran",
            "Sorry. Here is what was recorded, with your user name and the paths under it "
            + "replaced:" + Environment.NewLine + Environment.NewLine
            + Preview(report) + Environment.NewLine + Environment.NewLine
            + "Nothing has been sent anywhere. Open a pre-filled issue on GitHub so this can be "
            + "fixed? It opens in your browser with this text in it, and you can read and change "
            + "it before posting, or close it.");

        if (wants)
        {
            _dialogs.OpenUrl(ComposeIssueUrl(report));
        }
    }

    /// <summary>As much of the excerpt as belongs in a message box.</summary>
    private static string Preview(CrashReport report)
    {
        const int lines = 12;

        var head = report.Excerpt.Split(Environment.NewLine).Take(lines).ToList();
        var text = string.Join(Environment.NewLine, head);

        return report.Excerpt.Split(Environment.NewLine).Length > lines
            ? text + Environment.NewLine + "..."
            : text;
    }

    private void Remember(CrashReport report)
    {
        _config.LastReportedCrash = System.IO.Path.GetFileName(report.Path);
        _config.Save();
    }

    /// <summary>
    /// The link that opens a pre-filled issue, or a plain new issue when the text is too long.
    /// </summary>
    /// <remarks>
    /// Browsers refuse a URL past a certain length and the limit is not the same across them, so
    /// a body that would push past a conservative ceiling is dropped rather than risking a link
    /// that silently does nothing. The log is still on the machine and the message says where.
    /// </remarks>
    public static string ComposeIssueUrl(CrashReport report)
    {
        var title = Uri.EscapeDataString(report.Title);
        var body = Uri.EscapeDataString(report.Body);

        var full = $"{IssueUrl}?title={title}&body={body}";

        return full.Length <= 7500 ? full : $"{IssueUrl}?title={title}";
    }
}
