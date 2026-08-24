using System.IO;
using LocalNEXUS.App.Services.Diagnostics;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// What a crash report would publish, checked before it can publish anything.
/// </summary>
/// <remarks>
/// The whole feature is an offer to put a crash log on a public issue tracker, so the thing worth
/// testing is not that the offer appears but what the offer carries. A crash log is written for
/// the person who owns the machine and says whatever the exception said, which is full paths with
/// an account name in every one of them.
///
/// These hold the scrubbing to the account name and the profile directories, and hold the excerpt
/// to a length a browser will actually follow. What they cannot prove is that nothing identifying
/// survives, because a stack frame can name anything; that is why the text is shown before it is
/// sent rather than described.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class CrashReportTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "localnexus-crash-tests", Guid.NewGuid().ToString("N"));

    public CrashReportTests() => Directory.CreateDirectory(_folder);

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

    private string WriteLog(string content)
    {
        var path = Path.Combine(_folder, "crash-20260824-120000-000.log");
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>The account name is the thing that appears in every frame, so it goes.</summary>
    [Fact]
    public void TheUserNameIsRemoved()
    {
        var user = Environment.UserName;
        var scrubbed = CrashReport.Scrub($"at Thing.Method() in C:\\Users\\{user}\\code\\Thing.cs:line 4");

        Assert.DoesNotContain(user, scrubbed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The profile directory goes with it, replaced rather than deleted.</summary>
    [Fact]
    public void TheHomeDirectoryIsReplacedNotDeleted()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var scrubbed = CrashReport.Scrub($"Reading {home}\\Desktop\\SecretProject\\file.cs failed");

        Assert.DoesNotContain(home, scrubbed, StringComparison.OrdinalIgnoreCase);

        // Which directory a file was in is often what explains the crash, so the shape survives.
        Assert.Contains("file.cs", scrubbed, StringComparison.Ordinal);
    }

    /// <summary>Scrubbing an empty or absent log is not an error.</summary>
    [Fact]
    public void NothingIsSafeToScrub()
    {
        Assert.Equal(string.Empty, CrashReport.Scrub(string.Empty));
    }

    /// <summary>The excerpt is what a report carries, and it comes from the file.</summary>
    [Fact]
    public void TheExcerptComesFromTheLog()
    {
        var report = CrashReport.Read(WriteLog("LocalNEXUS crash report\nBoom happened here"));

        Assert.NotNull(report);
        Assert.Contains("Boom happened here", report!.Excerpt, StringComparison.Ordinal);
    }

    /// <summary>A log too long to carry is trimmed and says so.</summary>
    [Fact]
    public void AnEnormousLogIsTrimmed()
    {
        var report = CrashReport.Read(WriteLog(new string('x', 20000)));

        Assert.NotNull(report);
        Assert.True(report!.Excerpt.Length < 20000, "an enormous log was carried whole");
        Assert.Contains("trimmed here", report.Excerpt, StringComparison.Ordinal);
    }

    /// <summary>A log that is not there is answered with nothing rather than a throw.</summary>
    [Fact]
    public void AMissingLogIsNotAnError()
    {
        Assert.Null(CrashReport.Read(Path.Combine(_folder, "not-here.log")));
    }

    /// <summary>
    /// The body is what would be posted, and it says what it is.
    /// </summary>
    [Fact]
    public void TheBodyCarriesTheExcerptAndAsksForContext()
    {
        var report = CrashReport.Read(WriteLog("crash report\nsomething broke"));

        Assert.NotNull(report);
        Assert.Contains("something broke", report!.Body, StringComparison.Ordinal);
        Assert.Contains("What I was doing", report.Body, StringComparison.Ordinal);
        Assert.Contains("```", report.Body, StringComparison.Ordinal);
    }

    /// <summary>The link is a real GitHub issue link with the text in it.</summary>
    [Fact]
    public void TheLinkIsAPrefilledIssue()
    {
        var report = CrashReport.Read(WriteLog("crash report\nsomething broke"));
        var url = CrashReporter.ComposeIssueUrl(report!);

        Assert.StartsWith("https://github.com/", url, StringComparison.Ordinal);
        Assert.Contains("/issues/new?title=", url, StringComparison.Ordinal);
        Assert.Contains("body=", url, StringComparison.Ordinal);
        Assert.Contains("something%20broke", Uri.UnescapeDataString(url).Replace(" ", "%20", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    /// <summary>
    /// A link a browser would refuse drops the body rather than silently failing to open.
    /// </summary>
    [Fact]
    public void AnOverlongLinkKeepsTheTitleAndDropsTheBody()
    {
        var report = CrashReport.Read(WriteLog(new string('y', 4000)));
        var url = CrashReporter.ComposeIssueUrl(report!);

        Assert.True(url.Length <= 7500, $"the link is {url.Length} characters, which a browser may refuse");
        Assert.Contains("title=", url, StringComparison.Ordinal);
    }
}
