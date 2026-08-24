using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.Services.Processes;
using LocalNEXUS.App.Services.Python;

namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// Serves a safetensors model by splitting it across machines instead of holding it on one.
/// </summary>
/// <remarks>
/// The same shape as the Python runtime beside it, and deliberately so: one server per model,
/// started once and reused, started silently, owned by the child process group, polled on
/// <c>/health</c> and then spoken to over the OpenAI compatible API. What differs is the module
/// it runs and what that module does with the model, and neither of those reaches the request
/// path. That is the whole point of <see cref="IModelRuntime"/> having three questions in it.
///
/// This runtime exists for the case the other one cannot answer: a model too large for the
/// machine in front of it. It is not a faster way to run a model that already fits, because
/// every layer boundary it crosses is a network hop, which is why it is asked for rather than
/// chosen automatically and why it is off unless the mesh is on.
///
/// Only the host is started here. The machines contributing layers are started from a command
/// line and named in the configuration, which is what the brief scoped this to; when the panel
/// that owns them exists, where the list comes from changes and nothing else does.
/// </remarks>
public sealed class DistributedRuntimeManager : IModelRuntime, IDisposable
{
    private static readonly TimeSpan HealthPollInterval = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// How long the pipeline is given to come up.
    /// </summary>
    /// <remarks>
    /// Longer than the single machine runtime's ceiling because more has to happen: every
    /// contributing machine reads its own share off its own disk, and the host does not report
    /// itself healthy until the last of them has finished. A pipeline is as slow to start as its
    /// slowest member, not as slow as its own reading.
    /// </remarks>
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(40);

    /// <summary>The package run inside the environment's interpreter, from the vendor folder.</summary>
    private const string HostModule = "distributed";

    private readonly object _sync = new();
    private readonly Dictionary<string, PythonServerInstance> _servers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _health = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly ChildProcessGroup _children;
    private readonly PythonProvisioner _provisioner;
    private readonly AppConfig _config;

    private bool _disposed;

    public DistributedRuntimeManager(ChildProcessGroup children, PythonProvisioner provisioner, AppConfig config)
    {
        _children = children;
        _provisioner = provisioner;
        _config = config;
    }

    /// <inheritdoc />
    public string Name => "the distributed runtime";

    /// <inheritdoc />
    /// <remarks>
    /// Two conditions, and the second one is a switch rather than a property of the model. The
    /// resolver asks its runtimes in order and takes the first that says yes, so this one sits
    /// ahead of the single machine Python runtime and steps aside by answering no, which is what
    /// keeps the existing path exactly as it was whenever the mesh is off.
    /// </remarks>
    public bool CanServe(ModelDescriptor model)
        => model.Format == ModelFormat.Safetensors
            && _config.DistributedInferenceEnabled
            && WouldActuallyDistribute(model);

    /// <summary>
    /// Whether splitting this particular model would do anything worth the cost of splitting it.
    /// </summary>
    /// <remarks>
    /// The switch above says the user is willing. This says it would help, and the two are not
    /// the same question. With no other machines configured, this runtime plans a pipeline of one
    /// stage, which is the whole model on this machine reached through an extra process: the same
    /// answer the Python runtime already gives, arrived at more slowly. Claiming the model in
    /// that state would make turning the switch on a downgrade.
    ///
    /// So the path is taken when there is somewhere to distribute to, or when the model does not
    /// fit here and splitting is the only thing that could possibly work. That second case is
    /// deliberate even with no peers: the pipeline refuses with the exact shortfall in gigabytes,
    /// which is a better answer than the other runtime loading until the card fills up.
    ///
    /// This is the one policy decision in this file. Everything else here is mechanism.
    /// </remarks>
    private bool WouldActuallyDistribute(ModelDescriptor model)
        => _config.DistributedPeers.Any(entry => !string.IsNullOrWhiteSpace(entry))
            || !FitsOnThisMachine(model);

    /// <summary>
    /// Whether this machine could hold the whole model on its own.
    /// </summary>
    /// <remarks>
    /// Deliberately rough, and rough in the safe direction. It compares what the weights occupy
    /// on disk against what the card holds, less the share held back for everything that is not
    /// weights. It is the card's total rather than what is free, because free changes minute to
    /// minute and this decides which runtime answers rather than whether a load will succeed;
    /// the pipeline does the exact arithmetic later, against what each machine reports at the
    /// time, and refuses with real numbers if it does not add up.
    ///
    /// A machine with no NVIDIA driver answers no, which sends a model too large for it down the
    /// path that can refuse with a reason instead of the one that will try.
    /// </remarks>
    private static bool FitsOnThisMachine(ModelDescriptor model)
    {
        if (AcceleratorProbe.DetectMemory() is not { } card)
        {
            return false;
        }

        return model.SizeGb > 0 && model.SizeGb <= card.SafeCeilingGb;
    }

    /// <inheritdoc />
    public async Task<RuntimeEndpoint> EnsureServingAsync(
        ModelDescriptor model,
        ModelRuntimeOptions options,
        IProgress<string>? status,
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!Directory.Exists(model.Path))
        {
            throw new ModelClientException($"The model folder no longer exists: {model.Path}");
        }

        if (_provisioner.DescribeUnavailability() is { } unavailable)
        {
            throw new ModelClientException($"{model.DisplayName} cannot be run. {unavailable}");
        }

        if (FindPackageParent() is not { } packageParent)
        {
            throw new ModelClientException(
                "The distributed inference package is missing from the vendor folder, so a model "
                + "cannot be split across machines. Serve it on one machine by turning the mesh off.");
        }

        var fullPath = Path.GetFullPath(model.Path);

        // Serialised per model, for the same reason the single machine runtime is: two nodes
        // asking for the same model get one pipeline rather than two, and a second pipeline over
        // the same machines would be asking them to hold the model twice.
        var gate = GetGate(fullPath);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            PythonServerInstance? existing;
            lock (_sync)
            {
                _servers.TryGetValue(fullPath, out existing);
            }

            if (existing is not null)
            {
                if (existing.IsRunning)
                {
                    status?.Report($"Reusing the distributed pipeline on port {existing.Port}");
                    return new RuntimeEndpoint(existing.BaseUrl, ModelIdFor(fullPath));
                }

                existing.Dispose();
                lock (_sync)
                {
                    _servers.Remove(fullPath);
                }
            }

            var peers = ReadPeers();
            status?.Report(peers.Count == 0
                ? "No other machines are configured, so the pipeline is planned across this one"
                : $"Planning across this machine and {Describe(peers.Count, "other")}");

            var instance = StartHost(fullPath, model.DisplayName, peers, packageParent,
                _children, _provisioner.InterpreterPath);

            lock (_sync)
            {
                _servers[fullPath] = instance;
            }

            try
            {
                await WaitUntilHealthyAsync(instance, model.DisplayName, status, ct).ConfigureAwait(false);
            }
            catch
            {
                instance.Dispose();
                lock (_sync)
                {
                    _servers.Remove(fullPath);
                }

                throw;
            }

            return new RuntimeEndpoint(instance.BaseUrl, ModelIdFor(fullPath));
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public void ShutdownAll()
    {
        lock (_sync)
        {
            foreach (var server in _servers.Values)
            {
                server.Dispose();
            }

            _servers.Clear();
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

    /// <summary>
    /// What the pipeline calls the model, which is what its own model listing advertises.
    /// </summary>
    /// <remarks>
    /// The folder name rather than the path. The endpoint carries this because runtimes disagree
    /// about the model id, and guessing right for one and wrong for another is exactly what that
    /// field exists to prevent.
    /// </remarks>
    private static string ModelIdFor(string modelPath)
        => Path.GetFileName(modelPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    /// <summary>
    /// The machines named in the configuration, with the blank and the duplicate entries dropped.
    /// </summary>
    private List<string> ReadPeers()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var peers = new List<string>();

        foreach (var entry in _config.DistributedPeers)
        {
            var address = entry?.Trim();

            if (address is { Length: > 0 } && seen.Add(address))
            {
                peers.Add(address);
            }
        }

        return peers;
    }

    /// <summary>
    /// The folder that has to be on the interpreter's path for <c>-m distributed</c> to resolve.
    /// </summary>
    /// <remarks>
    /// The parent of the package, not the package itself. <c>python -m distributed</c> imports a
    /// package called <c>distributed</c>, so what goes on the path is the directory that
    /// contains it; pointing at the package would leave the name unimportable. Located through
    /// the same search the lockfiles use, which is what makes it resolve identically from a
    /// development run and from the published single file executable.
    /// </remarks>
    private static string? FindPackageParent()
    {
        foreach (var candidate in AppPaths.EnumeratePythonSearchDirectories())
        {
            if (File.Exists(Path.Combine(candidate, HostModule, "__main__.py")))
            {
                return candidate;
            }
        }

        return null;
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

    private static PythonServerInstance StartHost(
        string modelPath,
        string displayName,
        IReadOnlyList<string> peers,
        string packageParent,
        ChildProcessGroup children,
        string interpreter)
    {
        if (!File.Exists(interpreter))
        {
            throw new ModelClientException(
                "The Python runtime's interpreter is missing. Repair the runtime from the Local model panel.");
        }

        // Two ports, because the host is two things at once. One is the OpenAI API this
        // application talks to; the other is the socket the machine after it sends activations
        // to, which is a different protocol on a different connection and cannot share.
        var apiPort = ReserveFreePort();
        var stagePort = ReserveFreePort();

        var startInfo = new ProcessStartInfo
        {
            FileName = interpreter,
            WorkingDirectory = AppPaths.PythonRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add(HostModule);
        startInfo.ArgumentList.Add("host");
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(modelPath);
        startInfo.ArgumentList.Add("--api-host");
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("--api-port");
        startInfo.ArgumentList.Add(apiPort.ToString());
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(stagePort.ToString());

        foreach (var peer in peers)
        {
            startInfo.ArgumentList.Add("--peer");
            startInfo.ArgumentList.Add(peer);
        }

        // The package is not installed into the environment, it travels beside the lockfiles, so
        // the interpreter is told where to find it rather than being expected to know.
        startInfo.Environment["PYTHONPATH"] = packageParent;

        // Unbuffered, so the log shows how far the pipeline got rather than nothing at all when
        // a machine fails to load and the buffer is never flushed.
        startInfo.Environment["PYTHONUNBUFFERED"] = "1";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new ModelClientException("Windows did not start the distributed runtime and gave no reason.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ModelClientException($"Could not start the distributed runtime: {ex.Message}", ex);
        }

        children.Track(process, "python-distributed");

        var logPath = AppPaths.CreateLogFilePath($"distributed-{displayName}");
        var instance = new PythonServerInstance(process, modelPath, apiPort, logPath, children);
        instance.BeginCapturingOutput();
        return instance;
    }

    private async Task WaitUntilHealthyAsync(
        PythonServerInstance instance,
        string displayName,
        IProgress<string>? status,
        CancellationToken ct)
    {
        status?.Report($"Bringing up {displayName} across the pipeline, answering on port {instance.Port}");

        var deadline = DateTime.UtcNow + StartupTimeout;
        var announcedWait = false;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (!instance.IsRunning)
            {
                // The host exits rather than hanging when the machines cannot hold the model
                // between them, and it says by how much. That refusal is the most useful thing
                // this runtime ever prints, so it is carried out rather than summarised.
                throw new ModelClientException(
                    $"The distributed runtime stopped while bringing the pipeline up. Recent output:{Environment.NewLine}{instance.GetRecentOutput()}");
            }

            if (await IsHealthyAsync(instance, ct).ConfigureAwait(false))
            {
                status?.Report($"The pipeline is ready on port {instance.Port}");
                return;
            }

            if (!announcedWait)
            {
                announcedWait = true;
                status?.Report("Waiting for every machine in the pipeline to load its layers");
            }

            await Task.Delay(HealthPollInterval, ct).ConfigureAwait(false);
        }

        throw new ModelClientException(
            $"The distributed runtime did not become ready within {StartupTimeout.TotalMinutes:0} minutes. See {instance.LogPath}");
    }

    private async Task<bool> IsHealthyAsync(PythonServerInstance instance, CancellationToken ct)
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
            // Not accepting connections yet, which is the normal state while the machines load.
            return false;
        }
    }

    private static string Describe(int howMany, string noun)
        => howMany == 1 ? $"1 {noun} machine" : $"{howMany} {noun} machines";

    /// <summary>
    /// Asks the operating system for an unused loopback port, the same way the other runtimes do,
    /// with the same small race between releasing it and the server binding it.
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
}
