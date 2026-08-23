using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.Services.Processes;

namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// Owns the llama-server child processes that serve local GGUF models on this machine.
/// </summary>
/// <remarks>
/// One server is started per model and configuration pair and then reused for every later
/// request, because loading a large model onto the GPU is by far the most expensive part of a
/// local run. Servers are started silently with no console window and are killed when the
/// application exits. Startup is serialised per key rather than globally, so two different
/// models can load at the same time while two requests for the same model still share one
/// server.
///
/// Every server started here is handed to the child process group, which owns stopping it and
/// guarantees none of them outlives the application.
///
/// It is one implementation of <see cref="IModelRuntime"/> among the local runtimes rather than
/// something callers reach for directly. That is the only change from the version that served
/// every local model: what it does with a GGUF is unchanged.
/// </remarks>
public sealed class LlamaServerManager : IModelRuntime, IDisposable
{
    private static readonly TimeSpan HealthPollInterval = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(10);

    private readonly object _sync = new();
    private readonly Dictionary<string, LlamaServerInstance> _servers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _health = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly ChildProcessGroup _children;

    /// <summary>What each model is doing, keyed by its full path.</summary>
    private readonly Dictionary<string, LocalModelState> _states = new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;

    public LlamaServerManager(ChildProcessGroup children) => _children = children;

    /// <summary>
    /// Raised whenever a model starts, becomes ready, is restarted or goes away.
    /// </summary>
    /// <remarks>
    /// So a node can draw what its model is doing without asking on a timer. One event rather than
    /// one per model, because whatever listens is redrawing a handful of nodes and working out
    /// which of them cares is cheaper than carrying an identity through the event.
    /// </remarks>
    public event Action? StateChanged;

    /// <summary>What this model is doing right now.</summary>
    public LocalModelState StateFor(string? ggufPath)
    {
        if (Normalise(ggufPath) is not { } full)
        {
            return LocalModelState.NotLoaded;
        }

        lock (_sync)
        {
            return _states.TryGetValue(full, out var state) ? state : LocalModelState.NotLoaded;
        }
    }

    private void SetState(string fullPath, LocalModelState state)
    {
        lock (_sync)
        {
            if (state == LocalModelState.NotLoaded)
            {
                _states.Remove(fullPath);
            }
            else
            {
                _states[fullPath] = state;
            }
        }

        StateChanged?.Invoke();
    }

    private static string? Normalise(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public string Name => "llama.cpp";

    /// <inheritdoc />
    public bool CanServe(ModelDescriptor model) => model.Format == ModelFormat.Gguf;

    /// <inheritdoc />
    public async Task<RuntimeEndpoint> EnsureServingAsync(
        ModelDescriptor model,
        ModelRuntimeOptions options,
        IProgress<string>? status,
        CancellationToken ct)
    {
        var baseUrl = await EnsureServerAsync(model.Path, options.ToLlamaLaunchOptions(), status, ct)
            .ConfigureAwait(false);

        // llama-server serves whatever it was loaded with whatever name it is asked for, so the
        // file name is used, exactly as it was before there was more than one runtime.
        return new RuntimeEndpoint(baseUrl, Path.GetFileNameWithoutExtension(model.Path));
    }

    /// <summary>
    /// Returns the base URL of a running server for the given model and options, starting one
    /// if needed.
    /// </summary>
    /// <param name="ggufPath">Absolute path of the GGUF file to serve.</param>
    /// <param name="options">Per launch settings. Part of the identity of the server.</param>
    /// <param name="status">Receives progress messages while the model loads.</param>
    /// <param name="ct">Cancels the wait. A server that is already loading keeps loading.</param>
    /// <exception cref="ModelClientException">The executable or model is missing, or the server failed to become healthy.</exception>
    public async Task<string> EnsureServerAsync(
        string ggufPath,
        LlamaLaunchOptions options,
        IProgress<string>? status,
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(ggufPath))
        {
            throw new ModelClientException("No local model is selected for this node.");
        }

        if (!File.Exists(ggufPath))
        {
            throw new ModelClientException($"The model file no longer exists: {ggufPath}");
        }

        if (options.ProjectorPath is { Length: > 0 } declared && !File.Exists(declared))
        {
            throw new ModelClientException($"The multimodal projector no longer exists: {declared}");
        }

        var fullPath = Path.GetFullPath(ggufPath);
        var key = options.BuildServerKey(fullPath);

        // Serialised per key so that two nodes asking for the same model at once start one
        // server, not two, while a different model is free to load concurrently.
        var gate = GetGate(key);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            LlamaServerInstance? existing;
            lock (_sync)
            {
                _servers.TryGetValue(key, out existing);
            }

            if (existing is not null)
            {
                if (existing.IsRunning)
                {
                    status?.Report($"Reusing llama-server on port {existing.Port}");
                    SetState(fullPath, LocalModelState.Running);

                    return existing.BaseUrl;
                }

                existing.Dispose();
                lock (_sync)
                {
                    _servers.Remove(key);
                }
            }

            // A load parameter is fixed at start, so a changed one is a different server and the
            // old one is now dead weight holding a card's worth of memory. Retiring it here rather
            // than leaving both up is the difference between a setting that applies on the next run
            // and one that quietly does nothing until somebody restarts the application.
            var restarting = RetireOtherConfigurations(fullPath, key, status);

            SetState(fullPath, restarting ? LocalModelState.Restarting : LocalModelState.Starting);

            var instance = StartServer(fullPath, options, _children);
            lock (_sync)
            {
                _servers[key] = instance;
            }

            try
            {
                await WaitUntilHealthyAsync(instance, status, ct).ConfigureAwait(false);
            }
            catch
            {
                instance.Dispose();
                lock (_sync)
                {
                    _servers.Remove(key);
                }

                SetState(fullPath, LocalModelState.NotLoaded);
                throw;
            }

            SetState(fullPath, LocalModelState.Running);

            return instance.BaseUrl;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// What is running right now for a model, or null when nothing is.
    /// </summary>
    /// <remarks>
    /// So a node can show what the live server actually has rather than only what its fields say.
    /// The two can differ for exactly as long as it takes to run again, and a difference nobody can
    /// see is one somebody finds out about from a refusal naming a context they thought they had
    /// changed.
    /// </remarks>
    public RunningServer? Describe(string? ggufPath)
    {
        if (Normalise(ggufPath) is not { } full)
        {
            return null;
        }

        lock (_sync)
        {
            foreach (var server in _servers.Values)
            {
                if (server.IsRunning
                    && string.Equals(server.GgufPath, full, StringComparison.OrdinalIgnoreCase))
                {
                    return new RunningServer(
                        server.Options.ContextSize,
                        server.Options.GpuLayers,
                        server.Port);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Stops any server serving this model that was started with different load parameters.
    /// </summary>
    /// <remarks>
    /// Collected under the lock and stopped outside it, because stopping one means asking a process
    /// to end and then waiting for it, and nothing else should be held up by that.
    /// </remarks>
    private bool RetireOtherConfigurations(string fullPath, string key, IProgress<string>? status)
    {
        List<(string Key, LlamaServerInstance Server)> stale;

        lock (_sync)
        {
            stale = _servers
                .Where(pair => !string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)
                               && string.Equals(pair.Value.GgufPath, fullPath, StringComparison.OrdinalIgnoreCase))
                .Select(pair => (pair.Key, pair.Value))
                .ToList();

            foreach (var (staleKey, _) in stale)
            {
                _servers.Remove(staleKey);
            }
        }

        if (stale.Count == 0)
        {
            return false;
        }

        foreach (var (_, server) in stale)
        {
            status?.Report(
                $"Restarting the model: it is running with a context of {server.Options.ContextSize} "
                + $"and {server.Options.GpuLayers} GPU layers, which have changed.");

            server.Dispose();
        }

        return true;
    }

    /// <summary>Stops every running server. Called when the application exits.</summary>
    public void ShutdownAll()
    {
        lock (_sync)
        {
            foreach (var server in _servers.Values)
            {
                server.Dispose();
            }

            _servers.Clear();
            _states.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ShutdownAll();

        lock (_sync)
        {
            foreach (var gate in _gates.Values)
            {
                gate.Dispose();
            }

            _gates.Clear();
        }

        _health.Dispose();
    }

    private SemaphoreSlim GetGate(string key)
    {
        lock (_sync)
        {
            if (!_gates.TryGetValue(key, out var gate))
            {
                gate = new SemaphoreSlim(1, 1);
                _gates[key] = gate;
            }

            return gate;
        }
    }

    private static LlamaServerInstance StartServer(string ggufPath, LlamaLaunchOptions options, ChildProcessGroup children)
    {
        var executable = AppPaths.FindLlamaServerExecutable()
            ?? throw new ModelClientException(BuildMissingExecutableMessage());

        var port = ReserveFreePort();
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,

            // Redirected so that closing it is a request the server could act on. This build of
            // llama.cpp does not, which is why the group forces the issue afterwards.
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in options.BuildArguments(ggufPath, port))
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new ModelClientException("Windows did not start llama-server and gave no reason.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ModelClientException($"Could not start llama-server: {ex.Message}", ex);
        }

        children.Track(process, "llama-server");

        var logPath = AppPaths.CreateLogFilePath($"llama-{Path.GetFileNameWithoutExtension(ggufPath)}");
        var instance = new LlamaServerInstance(process, ggufPath, port, logPath, children, options);
        instance.BeginCapturingOutput();
        return instance;
    }

    private async Task WaitUntilHealthyAsync(
        LlamaServerInstance instance,
        IProgress<string>? status,
        CancellationToken ct)
    {
        var modelName = Path.GetFileNameWithoutExtension(instance.GgufPath);
        status?.Report($"Loading {modelName} on port {instance.Port}");

        var deadline = DateTime.UtcNow + StartupTimeout;
        var announcedWait = false;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (!instance.IsRunning)
            {
                throw new ModelClientException(
                    $"llama-server exited while loading the model. Recent output:{Environment.NewLine}{instance.GetRecentOutput()}");
            }

            if (await IsHealthyAsync(instance, ct).ConfigureAwait(false))
            {
                status?.Report($"Model ready on port {instance.Port}");
                return;
            }

            if (!announcedWait)
            {
                announcedWait = true;
                status?.Report("Waiting for the model to finish loading");
            }

            await Task.Delay(HealthPollInterval, ct).ConfigureAwait(false);
        }

        throw new ModelClientException(
            $"llama-server did not become ready within {StartupTimeout.TotalMinutes:0} minutes. See {instance.LogPath}");
    }

    private async Task<bool> IsHealthyAsync(LlamaServerInstance instance, CancellationToken ct)
    {
        try
        {
            using var response = await _health.GetAsync(instance.HealthUrl, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // The socket is not accepting connections yet, which is the normal state while loading.
            return false;
        }
    }

    /// <summary>
    /// Asks the operating system for an unused loopback port. There is a small race between
    /// releasing the port and llama-server binding it, which is acceptable here because the
    /// alternative is a fixed port that collides with a second instance of the app.
    /// </summary>
    private static int ReserveFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string BuildMissingExecutableMessage()
    {
        var searched = string.Join(
            Environment.NewLine,
            AppPaths.EnumerateLlamaSearchDirectories().Distinct(StringComparer.OrdinalIgnoreCase).Take(4));

        return $"{AppPaths.LlamaServerExecutableName} was not found. Place a llama.cpp build in vendor\\llama. Searched:{Environment.NewLine}{searched}";
    }
}
