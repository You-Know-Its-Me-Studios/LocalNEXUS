using System.Diagnostics;
using System.IO;
using LocalNEXUS.App.Services.Processes;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// Starting a command that is only a batch script, which on Windows is most of npm.
/// </summary>
/// <remarks>
/// Starting a process without the shell means CreateProcess, which appends .exe and nothing else
/// and starts executable images only. Node ships npx as a shell script with no extension and
/// npx.cmd beside it, so a search that takes the first match finds the one Windows cannot run and
/// reports a plainly installed tool as missing.
///
/// Nothing here starts a process. What is being checked is what the start info ends up saying, and
/// the search is given its own directory rather than the machine's path so the answer does not
/// depend on what happens to be installed.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class CommandLauncherTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "localnexus-launcher", Guid.NewGuid().ToString("N"));

    private const string Extensions = ".COM;.EXE;.BAT;.CMD";

    public CommandLauncherTests() => Directory.CreateDirectory(_folder);

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

    private string Write(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "rem nothing");

        return path;
    }

    private ResolvedCommand Resolve(string command)
        => CommandLauncher.Resolve(command, _folder, Extensions);

    private static ProcessStartInfo Apply(ResolvedCommand resolved, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo { UseShellExecute = false };
        resolved.ApplyTo(startInfo, arguments);

        return startInfo;
    }

    /// <summary>
    /// The npx case exactly: both files present, and the one Windows can run wins.
    /// </summary>
    /// <remarks>
    /// This is the whole defect. `where npx` lists the extensionless script first, and taking the
    /// first match is what reported npx as not installed on a machine that has it twice over.
    /// </remarks>
    [Fact]
    public void AnExtensionlessScriptNextToACmdResolvesToTheCmd()
    {
        Write("npx");
        var cmd = Write("npx.cmd");

        var resolved = Resolve("npx");

        Assert.Equal(CommandKind.BatchScript, resolved.Kind);
        Assert.Equal(cmd, resolved.Path);
    }

    /// <summary>An extensionless file on its own is not something that can be started.</summary>
    /// <remarks>
    /// CreateProcess cannot run it whatever it holds, so reporting it as unresolved is the honest
    /// answer and produces the same message as a tool that genuinely is not installed.
    /// </remarks>
    [Fact]
    public void AnExtensionlessFileAloneIsNotResolved()
    {
        Write("npx");

        Assert.Equal(CommandKind.Unresolved, Resolve("npx").Kind);
    }

    /// <summary>A batch script goes through the command interpreter, because nothing else can run it.</summary>
    [Fact]
    public void ABatchScriptIsRunThroughTheInterpreter()
    {
        var cmd = Write("npx.cmd");

        var startInfo = Apply(Resolve("npx"), "--yes", "some-package@latest");

        Assert.Equal(CommandLauncher.Interpreter, startInfo.FileName);
        Assert.Empty(startInfo.ArgumentList);

        // /s makes the interpreter strip the outer pair of quotes and take the rest literally,
        // which is the only predictable way to hand it a path and arguments together.
        Assert.StartsWith("/d /s /c \"", startInfo.Arguments, StringComparison.Ordinal);
        Assert.EndsWith("\"", startInfo.Arguments, StringComparison.Ordinal);

        Assert.Contains(cmd, startInfo.Arguments, StringComparison.Ordinal);
        Assert.Contains("--yes", startInfo.Arguments, StringComparison.Ordinal);
        Assert.Contains("some-package@latest", startInfo.Arguments, StringComparison.Ordinal);
    }

    /// <summary>A path with a space in it survives, which every Program Files install has.</summary>
    [Fact]
    public void APathWithASpaceIsQuoted()
    {
        var spaced = Path.Combine(_folder, "with space");
        Directory.CreateDirectory(spaced);

        var cmd = Path.Combine(spaced, "npx.cmd");
        File.WriteAllText(cmd, "rem nothing");

        var startInfo = Apply(CommandLauncher.Resolve("npx", spaced, Extensions), "one arg");

        Assert.Contains($"\"{cmd}\"", startInfo.Arguments, StringComparison.Ordinal);
        Assert.Contains("\"one arg\"", startInfo.Arguments, StringComparison.Ordinal);
    }

    /// <summary>A real executable is started directly, exactly as it was before any of this.</summary>
    [Fact]
    public void AnExecutableIsStartedDirectly()
    {
        var exe = Write("tool.exe");

        var resolved = Resolve("tool");

        Assert.Equal(CommandKind.Executable, resolved.Kind);
        Assert.Equal(exe, resolved.Path);

        var startInfo = Apply(resolved, "--version");

        Assert.Equal(exe, startInfo.FileName);
        Assert.Equal(new[] { "--version" }, startInfo.ArgumentList);
        Assert.Empty(startInfo.Arguments);
    }

    /// <summary>An executable is preferred over a script of the same name, as the path order says.</summary>
    [Fact]
    public void AnExecutableWinsOverAScriptOfTheSameName()
    {
        var exe = Write("tool.exe");
        Write("tool.cmd");

        Assert.Equal(exe, Resolve("tool").Path);
    }

    /// <summary>A command given as a path is taken as given, and still classified.</summary>
    [Fact]
    public void APathIsTakenAsGiven()
    {
        var cmd = Write("runner.cmd");

        var resolved = CommandLauncher.Resolve(cmd, string.Empty, Extensions);

        Assert.Equal(CommandKind.BatchScript, resolved.Kind);
        Assert.Equal(cmd, resolved.Path);
    }

    /// <summary>
    /// Something genuinely missing keeps its name, so the failure reads the way it always did.
    /// </summary>
    /// <remarks>
    /// The message that names the command is the useful one. Replacing it with a resolver's own
    /// wording would tell somebody about a search rather than about a tool they have not installed.
    /// </remarks>
    [Fact]
    public void SomethingMissingKeepsItsName()
    {
        var resolved = Resolve("nothing-is-installed-here");

        Assert.Equal(CommandKind.Unresolved, resolved.Kind);
        Assert.Equal("nothing-is-installed-here", resolved.Path);

        Assert.Equal("nothing-is-installed-here", Apply(resolved).FileName);
    }

    /// <summary>A quoted entry on the path is a directory, not a directory called with quotes.</summary>
    [Fact]
    public void QuotedPathEntriesAreRead()
    {
        var exe = Write("tool.exe");

        Assert.Equal(exe, CommandLauncher.Resolve("tool", $"\"{_folder}\"", Extensions).Path);
    }

    /// <summary>An empty command is an answer rather than a throw.</summary>
    [Fact]
    public void AnEmptyCommandIsNotResolved()
        => Assert.Equal(CommandKind.Unresolved, CommandLauncher.Resolve(string.Empty, _folder, Extensions).Kind);
}
