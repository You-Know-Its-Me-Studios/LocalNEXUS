using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models.Extensions;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.Services.Processes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace LocalNEXUS.App.Services.Extensions;

/// <summary>
/// Starts, holds and stops the processes that extensions run in.
/// </summary>
/// <remarks>
/// Nothing an extension author wrote is ever loaded into this application's address space. That
/// is the whole design and it is not caution for its own sake: the alternative has been tried
/// publicly and repeatedly, and a shared in process environment turns one extension's dependency
/// into every extension's problem. A separate process cannot do that, can be killed without
/// consequence, and lets an extension be written in whatever language its author prefers.
/// <para>
/// Every process started here is handed to <see cref="ChildProcessGroup"/>, so it joins a Windows
/// job object and the kernel terminates it when this application's handle closes, however this
/// application ends. An extension that spawns its own children cannot escape either, which
/// matters more here than for the bundled engines because a package runner is a launcher by
/// definition.
/// </para>
/// <para>
/// Nothing runs at application launch, because at that point no project is open and extensions
/// belong to projects. Opening one starts everything it has registered and holds them up for as
/// long as it stays open, so a run never waits on a cold start and a package runner never
/// downloads in the middle of somebody's work. Closing the project or opening another stops them,
/// which is what <see cref="StopAll"/> is for.
/// </para>
/// </remarks>
public sealed class ExtensionHost : IDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(45);

    private readonly ChildProcessGroup _children;
    private readonly IActivityFeed _feed;
    private readonly Dictionary<string, ExtensionSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;

    /// <summary>Raised when a node worker reports progress, carrying the extension id and its text.</summary>
    public event Action<string, string>? NodeProgressReported;

    /// <summary>
    /// The open project, which every worker is started in and told about.
    /// </summary>
    /// <remarks>
    /// Set when a project is opened and read when a worker starts. An extension exists because of
    /// a project, so starting one anywhere else means it looks for that project's files in the
    /// application's own folder. The spec bridge found this first, because it cannot resolve an
    /// openspec directory without it, but it was equally true of the others.
    /// </remarks>
    public string? ProjectPath { get; set; }

    public ExtensionHost(ChildProcessGroup children, IActivityFeed feed)
    {
        _children = children;
        _feed = feed;
    }

    /// <summary>
    /// Returns a running session for this extension, starting it if it is not already up.
    /// </summary>
    /// <exception cref="ExtensionException">It could not be started, with the reason.</exception>
    public async Task<ExtensionSession> EnsureRunningAsync(
        InstalledExtension extension,
        ExtensionContract contract,
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!extension.IsEnabled)
        {
            throw new ExtensionException($"{extension.Manifest.Name} is switched off, so it was not started.");
        }

        var key = Key(extension.Manifest.Id, contract);
        var gate = GetGate(key);
        await gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (_sessions.TryGetValue(key, out var existing))
            {
                if (existing.IsAlive)
                {
                    return existing;
                }

                // It died since last time. Clear it out rather than handing back a dead pipe.
                existing.Dispose();
                _sessions.Remove(key);
            }

            extension.State = ExtensionState.Starting;
            extension.StateDetail = null;

            var session = await StartAsync(extension, contract, ct).ConfigureAwait(false);
            _sessions[key] = session;

            extension.State = ExtensionState.Running;
            extension.LogPath = session.LogPath;

            return session;
        }
        catch (ExtensionException ex)
        {
            extension.State = ExtensionState.Unreachable;
            extension.StateDetail = ex.Message;
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Stops every session this extension has, across both contracts.</summary>
    public void Stop(string extensionId)
    {
        foreach (var contract in Enum.GetValues<ExtensionContract>())
        {
            var key = Key(extensionId, contract);

            if (!_sessions.TryGetValue(key, out var session))
            {
                continue;
            }

            _sessions.Remove(key);
            StopSession(session);
        }
    }

    /// <summary>
    /// Stops every session this host is holding, whatever extension it belongs to.
    /// </summary>
    /// <remarks>
    /// For closing a project or opening a different one. An extension exists because of a project
    /// and is started in it, so a worker left up while somebody works somewhere else is a process
    /// pointed at a folder nobody is looking at. The workers for the new project are started by the
    /// same pass that stops these.
    /// </remarks>
    public void StopAll()
    {
        foreach (var key in _sessions.Keys.ToList())
        {
            if (_sessions.Remove(key, out var session))
            {
                StopSession(session);
            }
        }
    }

    /// <summary>True when this extension currently has a live process for any contract.</summary>
    public bool IsRunning(string extensionId)
        => Enum.GetValues<ExtensionContract>()
            .Any(c => _sessions.TryGetValue(Key(extensionId, c), out var session) && session.IsAlive);

    private static string Key(string extensionId, ExtensionContract contract) => $"{extensionId}/{contract}";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var session in _sessions.Values.ToList())
        {
            StopSession(session);
        }

        _sessions.Clear();

        foreach (var gate in _gates.Values)
        {
            gate.Dispose();
        }

        _gates.Clear();
    }

    private async Task<ExtensionSession> StartAsync(
        InstalledExtension extension,
        ExtensionContract contract,
        CancellationToken ct)
    {
        var launch = extension.Manifest.Launch;
        var logPath = AppPaths.CreateLogFilePath($"extension-{Sanitise(extension.Manifest.Id)}");

        // Resolved before it is started. A command that is only ever a .cmd on Windows, which npx
        // is, cannot be found or run by CreateProcess without this.
        var resolved = Services.Processes.CommandLauncher.Resolve(launch.Command);

        var startInfo = new ProcessStartInfo
        {
            // The manifest wins, then the open project, then the application's own folder. A
            // manifest that names a directory means it, and a worker with no opinion belongs in
            // the project it is there for.
            WorkingDirectory = launch.WorkingDirectory is { Length: > 0 } dir && Directory.Exists(dir)
                ? dir
                : ProjectPath is { Length: > 0 } project && Directory.Exists(project)
                    ? project
                    : AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        resolved.ApplyTo(startInfo, launch.Arguments);

        if (launch.Environment is { } environment)
        {
            foreach (var pair in environment)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        // Said as well as implied by the working directory, because a worker that changes
        // directory for its own reasons still needs to know which project it is there for.
        if (ProjectPath is { Length: > 0 } openProject)
        {
            startInfo.Environment["LOCALNEXUS_PROJECT"] = openProject;
        }

        Process process;

        try
        {
            process = Process.Start(startInfo)
                ?? throw new ExtensionException($"Windows did not start '{launch.Command}' and did not say why.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // By far the most common failure, and the one worth naming precisely: the command is
            // not on the path. Saying "file not found" without saying which file is useless.
            throw new ExtensionException(
                $"'{launch.Command}' could not be run. It is either not installed or not on the path. " +
                $"The full command was: {launch.DisplayCommand}", ex);
        }

        _children.Track(process, $"extension {extension.Manifest.Id}");

        // stderr is drained to a file rather than parsed. Debugging a stdio worker without its
        // stderr is guesswork, and this is the file the panel links to.
        _ = Task.Run(() => DrainErrorAsync(process, logPath), CancellationToken.None);

        // Give it a moment to fall over on its own. A worker that exits immediately is the
        // second most common failure after a missing command, and it produces a far clearer
        // message caught here than as a protocol timeout forty five seconds later.
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _children.Terminate(process);
            throw;
        }

        if (process.HasExited)
        {
            var tail = ReadTail(logPath);
            throw new ExtensionException(
                $"{extension.Manifest.Name} exited immediately with code {process.ExitCode}. " +
                (tail is null ? $"Its log is at {logPath}." : $"It said: {tail}"));
        }

        return contract switch
        {
            ExtensionContract.Mcp => await StartMcpAsync(extension, process, logPath, ct).ConfigureAwait(false),
            _ => StartRpc(extension, contract, process, logPath)
        };
    }

    /// <summary>
    /// A session speaking newline delimited JSON-RPC over the worker's stdio.
    /// </summary>
    /// <remarks>
    /// Shared by the node contract and the spec contract, because the framing is the same one and
    /// a second reader of the same stdout would be a second way to lose messages. The contract is
    /// carried through rather than assumed, so a session says which of the two it is rather than
    /// every one of them claiming to be a node session.
    /// </remarks>
    private ExtensionSession StartRpc(
        InstalledExtension extension,
        ExtensionContract contract,
        Process process,
        string logPath)
    {
        var connection = new JsonRpcConnection(process);

        connection.ProtocolViolation += line => _feed.Error(
            $"{extension.Manifest.Name} wrote to stdout",
            "stdout carries the protocol and nothing else, so this line was discarded. Logging " +
            $"belongs on stderr, which is captured at {logPath}. The line was: {Truncate(line)}");

        connection.NotificationReceived += (method, payload) =>
        {
            // A worker reporting "2 of 5" gets the progress bar the built in nodes get, without
            // knowing that is what it is doing, because the node view model reads that shape out
            // of the status line already.
            if (method == "node/progress" && payload?["text"]?.GetValue<string>() is { } text)
            {
                NodeProgressReported?.Invoke(extension.Manifest.Id, text);
                return;
            }

            // Both contracts log the same way and reach the same feed. A worker that says
            // something has one place to say it whichever contract it is speaking.
            if (method is "node/log" or "spec/log" && payload?["message"]?.GetValue<string>() is { } message)
            {
                _feed.Info(extension.Manifest.Name, Truncate(message));
            }
        };

        return new ExtensionSession(
            extension.Manifest.Id, contract, process, logPath, connection, null);
    }

    private async Task<ExtensionSession> StartMcpAsync(
        InstalledExtension extension,
        Process process,
        string logPath,
        CancellationToken ct)
    {
        // The SDK's transport reads the process output itself, so it is handed the streams of a
        // process this application started and owns, rather than being allowed to start its own.
        // StdioClientTransport would have spawned it, and a process spawned inside the SDK is one
        // the job object never sees.
        var transport = new StreamClientTransport(process.StandardInput.BaseStream, process.StandardOutput.BaseStream);

        try
        {
            using var timer = new CancellationTokenSource(StartTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, ct);

            var client = await McpClient
                .CreateAsync(transport, cancellationToken: linked.Token)
                .ConfigureAwait(false);

            return new ExtensionSession(
                extension.Manifest.Id, ExtensionContract.Mcp, process, logPath, null, client);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _children.Terminate(process);
            throw new ExtensionException(
                $"{extension.Manifest.Name} started but never answered the MCP handshake within " +
                $"{StartTimeout.TotalSeconds:0} seconds. Its log is at {logPath}.");
        }
        catch (Exception ex) when (ex is not ExtensionException and not OperationCanceledException)
        {
            _children.Terminate(process);
            throw new ExtensionException(
                $"{extension.Manifest.Name} started but the MCP handshake failed: {ex.Message} " +
                $"Its log is at {logPath}.", ex);
        }
    }

    private void StopSession(ExtensionSession session)
    {
        session.Dispose();

        try
        {
            _children.Terminate(session.Process);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone, which is the outcome that was wanted.
        }
    }

    private SemaphoreSlim GetGate(string extensionId)
    {
        if (!_gates.TryGetValue(extensionId, out var gate))
        {
            gate = new SemaphoreSlim(1, 1);
            _gates[extensionId] = gate;
        }

        return gate;
    }

    private static async Task DrainErrorAsync(Process process, string logPath)
    {
        try
        {
            await using var file = new StreamWriter(logPath, append: true) { AutoFlush = true };

            while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                await file.WriteLineAsync(line).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The log is a convenience. Losing it must never take the extension down with it.
        }
    }

    private static string? ReadTail(string logPath)
    {
        try
        {
            if (!File.Exists(logPath))
            {
                return null;
            }

            var lines = File.ReadAllLines(logPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            return lines.Count == 0 ? null : Truncate(lines[^1]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Truncate(string value)
        => value.Length <= 300 ? value : value[..300] + "...";

    private static string Sanitise(string id)
        => string.Concat(id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));

    /// <summary>The timeout a first call to a freshly started extension is given.</summary>
    public static TimeSpan HandshakeTimeout => StartTimeout;
}
