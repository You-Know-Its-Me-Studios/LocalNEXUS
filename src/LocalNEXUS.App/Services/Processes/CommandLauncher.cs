using System.Diagnostics;
using System.IO;
using System.Text;

namespace LocalNEXUS.App.Services.Processes;

/// <summary>What a named command turned out to be.</summary>
public enum CommandKind
{
    /// <summary>A real executable image, which can be started directly.</summary>
    Executable,

    /// <summary>A batch script, which only the command interpreter can run.</summary>
    BatchScript,

    /// <summary>Nothing on the path matched, so the original name is used and will fail loudly.</summary>
    Unresolved
}

/// <summary>
/// A named command, resolved to something Windows will actually start.
/// </summary>
/// <param name="Kind">What it turned out to be.</param>
/// <param name="Path">The resolved file, or the original name when nothing matched.</param>
/// <param name="Original">What was asked for, for the message when it could not be run.</param>
public sealed record ResolvedCommand(CommandKind Kind, string Path, string Original)
{
    /// <summary>
    /// Points a start info at this command and gives it the arguments.
    /// </summary>
    /// <remarks>
    /// A batch script cannot be handed to CreateProcess at all, so it goes through the command
    /// interpreter, and that is the one case where the arguments cannot be given one at a time.
    /// The interpreter has its own parsing, and <c>/s</c> is the documented way to make it
    /// predictable: it strips the first and last quote of what follows and treats the rest
    /// literally, so the whole command line is wrapped in one more pair. <c>/d</c> skips any
    /// AutoRun the machine has configured, which has no business running inside a tool this
    /// started.
    /// </remarks>
    public void ApplyTo(ProcessStartInfo startInfo, IReadOnlyList<string> arguments)
    {
        if (Kind != CommandKind.BatchScript)
        {
            startInfo.FileName = Path;

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            return;
        }

        startInfo.FileName = CommandLauncher.Interpreter;

        var line = new StringBuilder("/d /s /c \"");
        line.Append(Quote(Path));

        foreach (var argument in arguments)
        {
            line.Append(' ').Append(Quote(argument));
        }

        line.Append('"');

        startInfo.Arguments = line.ToString();
    }

    /// <summary>One argument, quoted the way the Windows command line parser reads it back.</summary>
    private static string Quote(string argument)
    {
        if (argument.Length > 0 && !argument.Any(c => c is ' ' or '\t' or '"'))
        {
            return argument;
        }

        var quoted = new StringBuilder("\"");
        var backslashes = 0;

        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                quoted.Append('\\', (backslashes * 2) + 1);
                backslashes = 0;
            }
            else
            {
                quoted.Append('\\', backslashes);
                backslashes = 0;
            }

            quoted.Append(c);
        }

        quoted.Append('\\', backslashes * 2);
        quoted.Append('"');

        return quoted.ToString();
    }
}

/// <summary>
/// Works out what a named command actually is before trying to start it.
/// </summary>
/// <remarks>
/// Starting a process without the shell means CreateProcess, and CreateProcess is stricter than
/// typing the same thing at a prompt in two ways that matter here. It appends <c>.exe</c> and
/// nothing else, so a command whose only real form is a <c>.cmd</c> is never found. And it starts
/// executable images only, so even handed the <c>.cmd</c> outright it cannot run it, because a
/// batch script is not a program.
///
/// npx is exactly that shape. Node ships <c>npx</c>, which is a shell script for platforms that
/// have one, and <c>npx.cmd</c>, which is the one Windows uses. A search that takes the first match
/// finds the extensionless one and fails on something that is plainly installed, which is what the
/// extensions were doing.
///
/// So a command with no extension is only ever matched against the extensions the machine says are
/// executable, and one that turns out to be a script is run through the interpreter. Everything
/// else is unchanged and still started directly.
/// </remarks>
public static class CommandLauncher
{
    /// <summary>What runs a batch script, from the environment rather than assumed.</summary>
    public static string Interpreter =>
        Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } comspec ? comspec : "cmd.exe";

    /// <summary>Extensions used when the machine does not say.</summary>
    private const string DefaultPathExtensions = ".COM;.EXE;.BAT;.CMD";

    /// <summary>The extensions only the command interpreter can run.</summary>
    private static readonly string[] ScriptExtensions = { ".cmd", ".bat" };

    /// <summary>
    /// Resolves a command against the search path.
    /// </summary>
    /// <param name="command">A bare name, or a path. A path is taken as given.</param>
    /// <param name="searchPath">Directories to search, or null for the machine's own.</param>
    /// <param name="pathExtensions">Executable extensions, or null for the machine's own.</param>
    public static ResolvedCommand Resolve(string command, string? searchPath = null, string? pathExtensions = null)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new ResolvedCommand(CommandKind.Unresolved, command, command);
        }

        var extensions = Split(pathExtensions ?? Environment.GetEnvironmentVariable("PATHEXT") ?? DefaultPathExtensions);

        // A command given as a path is what somebody meant, so it is not searched for. It still has
        // to be classified, because a script is a script wherever it was found.
        if (command.Contains(System.IO.Path.DirectorySeparatorChar)
            || command.Contains(System.IO.Path.AltDirectorySeparatorChar))
        {
            return Classify(command, command);
        }

        var hasExtension = System.IO.Path.GetExtension(command).Length > 0;

        foreach (var directory in Split(searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty))
        {
            if (hasExtension)
            {
                var exact = Combine(directory, command);

                if (exact is not null)
                {
                    return Classify(exact, command);
                }

                continue;
            }

            // Extensions only. The extensionless file is what a shell would run and is not
            // something CreateProcess can start, so finding it first is how this failed.
            foreach (var extension in extensions)
            {
                if (Combine(directory, command + extension) is { } found)
                {
                    return Classify(found, command);
                }
            }
        }

        return new ResolvedCommand(CommandKind.Unresolved, command, command);
    }

    private static ResolvedCommand Classify(string path, string original)
    {
        var extension = System.IO.Path.GetExtension(path);

        var kind = ScriptExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            ? CommandKind.BatchScript
            : CommandKind.Executable;

        return new ResolvedCommand(kind, path, original);
    }

    private static string? Combine(string directory, string name)
    {
        try
        {
            var candidate = System.IO.Path.Combine(directory, name);

            if (!File.Exists(candidate))
            {
                return null;
            }

            // The name as the disk spells it, not as the extension list does. Windows does not care
            // either way, and a log line reading npx.CMD for a file called npx.cmd is a small lie
            // that costs somebody a minute when they go looking for it.
            var actual = Directory.GetFiles(directory, System.IO.Path.GetFileName(candidate));

            return actual.Length == 1 ? actual[0] : candidate;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A malformed entry on the path is a malformed entry on the path, not a reason to stop
            // looking through the rest of it.
            return null;
        }
    }

    private static string[] Split(string value) => value
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(entry => entry.Trim('"'))
        .Where(entry => entry.Length > 0)
        .ToArray();
}
