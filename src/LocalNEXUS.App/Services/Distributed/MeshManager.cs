using System.ComponentModel;
using System.Globalization;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.Services.Processes;

namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// Owns this install's mesh node: the child process the distributed path runs on, and the
/// live picture of what the mesh can serve.
/// </summary>
/// <remarks>
/// The counterpart of <see cref="LlamaServerManager"/>, which still owns purely local
/// inference. Where that class starts a server for a model this machine holds, this one starts
/// a node that joins a mesh and lets the engine decide whether a model runs here, on a peer, or
/// as layer stages across several. Discovery, placement, transport and liveness are all the
/// engine's; this class starts the process, reads what the engine reports, and renders it.
///
/// Stopping the node belongs to the child process group rather than to this class. The engine's
/// own stop command cannot do it: it tracks instances through a runtime directory that a process
/// started this way is not registered in, so it reports nothing running and leaves the child
/// alive, and it takes no target, so it could not be aimed at our process even if it did work.
/// The engine also re-executes itself while starting, which is what made a single tree kill here
/// occasionally miss. Both facts were established against the bundled build rather than assumed.
/// </remarks>
public sealed partial class MeshManager : ObservableObject, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StartupGrace = TimeSpan.FromMinutes(3);

    private readonly AppConfig _config;
    private readonly IActivityFeed _feed;
    private readonly Dispatcher _dispatcher;
    private readonly ChildProcessGroup _children;
    private readonly MeshStatusReader _reader = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CancellationTokenSource? _shutdown;
    private Task? _pollLoop;
    private Process? _process;
    private StreamWriter? _log;
    private DateTimeOffset _startedAt;
    private bool _announcedReady;
    private bool _disposed;

    /// <summary>What this install's node is doing right now.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(CanJoinOrHost))]
    [NotifyPropertyChangedFor(nameof(EmptyModelsText))]
    [NotifyPropertyChangedFor(nameof(EmptySourcesText))]
    private MeshNodeState _state = MeshNodeState.Stopped;

    /// <summary>One line for the contribution card: what the node is doing.</summary>
    [ObservableProperty]
    private string _statusText = "Mesh node stopped";

    /// <summary>Friendly name of the mesh this node is in.</summary>
    [ObservableProperty]
    private string _meshName = string.Empty;

    /// <summary>The token another machine needs to join this mesh. Blank until the node hosts one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInviteToken))]
    private string _inviteToken = string.Empty;

    /// <summary>Whether the mesh is advertised publicly. False for the private default.</summary>
    [ObservableProperty]
    private bool _isPublic;

    /// <summary>True while this machine offers its own compute rather than only routing.</summary>
    [ObservableProperty]
    private bool _isContributing;

    /// <summary>
    /// The runtime's own word for what it is doing: standby, loading, or serving.
    /// </summary>
    /// <remarks>
    /// Reported rather than inferred, which is what makes it worth carrying. It is how a joined
    /// mesh can say it is loading models instead of saying connecting for a minute and a half.
    /// </remarks>
    [ObservableProperty]
    private string _daemonState = string.Empty;

    /// <summary>
    /// True when the node was asked to publish and could not.
    /// </summary>
    /// <remarks>
    /// Its own answer rather than being folded into not public, because a mesh that was never
    /// published and one that tried and failed are different situations and only the second is
    /// worth saying out loud.
    /// </remarks>
    [ObservableProperty]
    private bool _publishFailed;

    /// <summary>True once a local model runtime is up and able to answer.</summary>
    [ObservableProperty]
    private bool _llamaReady;

    /// <summary>True once the node has attached to a mesh, as a consumer or a host.</summary>
    [ObservableProperty]
    private bool _isAttached;

    /// <summary>Why the node is not running, when it failed. Null otherwise.</summary>
    [ObservableProperty]
    private string? _lastError;

    /// <summary>This install's own node, once the engine has reported its identity.</summary>
    [ObservableProperty]
    private InferenceSource? _thisMachine;

    public MeshManager(AppConfig config, IActivityFeed feed, Dispatcher dispatcher, ChildProcessGroup children)
    {
        _config = config;
        _feed = feed;
        _dispatcher = dispatcher;
        _children = children;

        Models.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasModels));
        Sources.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSources));
    }

    /// <summary>Every model the mesh can serve or is trying to. The Network tab's primary surface.</summary>
    public ObservableCollection<NetworkServedModel> Models { get; } = new();

    /// <summary>Every source in the mesh, this machine first. Populated entirely from mesh reports.</summary>
    public ObservableCollection<InferenceSource> Sources { get; } = new();

    /// <summary>True when the mesh has reported at least one model.</summary>
    public bool HasModels => Models.Count > 0;

    /// <summary>True when the mesh has reported at least one source, which includes this machine.</summary>
    public bool HasSources => Sources.Count > 0;

    /// <summary>
    /// What an empty model list means, which is never the same thing twice.
    /// </summary>
    /// <remarks>
    /// A node that has not answered yet and a mesh that genuinely serves nothing look identical
    /// on screen unless the difference is said out loud, and the first of those is the ordinary
    /// state of the tab for the first few seconds after the window opens.
    /// </remarks>
    public string EmptyModelsText => State switch
    {
        MeshNodeState.Starting => "Starting the mesh node. Models appear here as the mesh reports them.",
        MeshNodeState.Failed => "The mesh node is not running. Its last error is under This machine.",
        MeshNodeState.Stopped => "The mesh node is not running. Start it to see what your own mesh can serve, or find meshes to see who else is out there.",
        _ => "The mesh node is up and has not reported a model yet."
    };

    /// <summary>The same distinction for the source list.</summary>
    public string EmptySourcesText => State switch
    {
        MeshNodeState.Starting => "Starting the mesh node. Sources appear here as it finds them.",
        MeshNodeState.Failed => "The mesh node is not running.",
        MeshNodeState.Stopped => "The mesh node is not running.",
        _ => "The mesh node is up and has not reported a source yet."
    };

    /// <summary>True when a node process is up, whatever it is doing.</summary>
    public bool IsRunning => State is MeshNodeState.Starting or MeshNodeState.Client or MeshNodeState.Serving;

    /// <summary>True when membership settings can be edited, which is only while stopped.</summary>
    public bool CanJoinOrHost => State is MeshNodeState.Stopped or MeshNodeState.Failed;

    /// <summary>True once this node hosts a mesh that others can be invited into.</summary>
    public bool HasInviteToken => !string.IsNullOrWhiteSpace(InviteToken);

    /// <summary>Port the OpenAI compatible API listens on.</summary>
    public int ApiPort { get; private set; } = MeshLaunchOptions.DefaultApiPort;

    /// <summary>Port the management API answers on.</summary>
    public int ConsolePort { get; private set; } = MeshLaunchOptions.DefaultConsolePort;

    /// <summary>
    /// Where model nodes send requests. One endpoint for everything the mesh serves; which
    /// machine actually runs the model is the engine's business, not the graph's.
    /// </summary>
    public string ApiBaseUrl => $"http://127.0.0.1:{ApiPort}/v1";

    /// <summary>Finds a model by the identity a graph persisted. Null when the mesh no longer knows it.</summary>
    public NetworkServedModel? FindByKey(string? modelKey) => string.IsNullOrWhiteSpace(modelKey)
        ? null
        : Models.FirstOrDefault(m => string.Equals(m.ModelKey, modelKey, StringComparison.Ordinal));

    /// <summary>
    /// Starts the node if the user left it enabled. Failures are reported to the feed rather
    /// than allowed to interrupt composition.
    /// </summary>
    public async Task RestoreAsync()
    {
        if (!_config.MeshEnabled)
        {
            return;
        }

        try
        {
            await StartAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (ModelClientException ex)
        {
            _feed.Error("Mesh node not started", ex.Message);
        }
    }

    /// <summary>Starts the node process and begins reading mesh state.</summary>
    /// <exception cref="ModelClientException">The executable is missing or Windows refused to start it.</exception>
    public async Task StartAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_process is { HasExited: false })
            {
                return;
            }

            var options = BuildOptions();
            ApiPort = options.ApiPort;
            ConsolePort = options.ConsolePort;

            StartProcess(options);

            _shutdown = new CancellationTokenSource();
            _startedAt = DateTimeOffset.UtcNow;
            _announcedReady = false;

            State = MeshNodeState.Starting;
            IsContributing = options.Contribute;
            IsPublic = options.Publish;
            LastError = null;
            StatusText = options.Contribute
                ? "Starting, offering this machine to the mesh"
                : "Starting, joining as a client";

            _config.MeshEnabled = true;
            _config.Save();

            _pollLoop = Task.Run(() => PollLoopAsync(_shutdown.Token), CancellationToken.None);

            _feed.Info(
                "Mesh node starting",
                options.Contribute
                    ? $"Serving on port {options.ApiPort}, {(options.Publish ? "published publicly" : "private mesh on the local network")}."
                    : $"Joining as a client on port {options.ApiPort}.");

            // The engine takes one model on the command line, so anything else chosen is handed to
            // the node once it answers. Not awaited: the node takes seconds to come up and the
            // window has to stay usable, and a model that fails to load is reported rather than
            // stopping the node that is already serving the others.
            if (options.AdditionalModelPaths.Count > 0)
            {
                _ = LoadAdditionalModelsAsync(options, _shutdown.Token);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Asks the running node to load the models beyond the first.
    /// </summary>
    /// <remarks>
    /// Through the engine's own load command rather than by writing its configuration file. The
    /// engine owns what it is serving; this asks, and reports what it answers.
    /// </remarks>
    private async Task LoadAdditionalModelsAsync(MeshLaunchOptions options, CancellationToken ct)
    {
        var executable = AppPaths.FindMeshExecutable();

        if (executable is null)
        {
            return;
        }

        foreach (var path in options.AdditionalModelPaths)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            // The node has to be answering before it can be asked to load anything, so each attempt
            // waits for readiness rather than firing at a port nothing is listening on yet.
            if (!await WaitForConsoleAsync(options.ConsolePort, ct).ConfigureAwait(false))
            {
                _feed.Error(
                    "Models not offered",
                    "The mesh node did not answer in time, so the models after the first were not loaded.");
                return;
            }

            var name = Path.GetFileName(path);

            try
            {
                var exitCode = await RunMeshCommandAsync(
                    executable,
                    new[] { "load", path, "--port", options.ConsolePort.ToString(CultureInfo.InvariantCulture) },
                    ct).ConfigureAwait(false);

                if (exitCode == 0)
                {
                    _feed.Info("Model offered", name);
                }
                else
                {
                    _feed.Error("Model not offered", $"{name} was refused by the mesh node.");
                }
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
            {
                _feed.Error("Model not offered", $"{name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Rotates this node's identity key, which is what invalidates the invite token.
    /// </summary>
    /// <remarks>
    /// The token is not ours to reissue. It is base64 of the node's public key and its current
    /// addresses, minted by the engine, so the only way to stop an old one working is to give the
    /// node a new identity. That is a real cost and the caller is expected to have said so: the
    /// peer key is what CLAUDE.md says reputation attaches to, and this machine becomes a stranger
    /// to everyone who knew it.
    /// </remarks>
    public async Task<bool> RotateIdentityAsync(CancellationToken ct)
    {
        var executable = AppPaths.FindMeshExecutable();

        if (executable is null)
        {
            _feed.Error("Token not replaced", BuildMissingExecutableMessage());
            return false;
        }

        var wasRunning = IsRunning;

        if (wasRunning)
        {
            await StopAsync().ConfigureAwait(false);
        }

        try
        {
            var exitCode = await RunMeshCommandAsync(executable, new[] { "auth", "rotate-node" }, ct).ConfigureAwait(false);

            if (exitCode != 0)
            {
                _feed.Error("Token not replaced", "The mesh node refused to rotate its identity.");
                return false;
            }

            _feed.Info(
                "Invite token replaced",
                "This machine has a new identity, so the previous token no longer works and anyone who had it is no longer in this mesh.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            _feed.Error("Token not replaced", ex.Message);
            return false;
        }

        if (wasRunning)
        {
            await StartAsync(ct).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>Runs one mesh command to completion and returns its exit code.</summary>
    private static async Task<int> RunMeshCommandAsync(string executable, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The mesh command could not be started.");

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return process.ExitCode;
    }

    /// <summary>Waits for the management port to answer, so a command has something to talk to.</summary>
    private async Task<bool> WaitForConsoleAsync(int consolePort, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(90);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (ct.IsCancellationRequested)
            {
                return false;
            }

            if (await _reader.ReadAsync(consolePort, ApiPort, ct).ConfigureAwait(false) is not null)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>Stops the node and clears everything read from the mesh.</summary>
    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var wasRunning = _process is { HasExited: false };
            await ShutdownProcessAsync().ConfigureAwait(false);

            _config.MeshEnabled = false;
            _config.Save();

            await _dispatcher.InvokeAsync(() =>
            {
                Models.Clear();
                Sources.Clear();
                ThisMachine = null;
                State = MeshNodeState.Stopped;
                StatusText = "Mesh node stopped";
                MeshName = string.Empty;
                InviteToken = string.Empty;
                IsContributing = false;
            });

            if (wasRunning)
            {
                _feed.Info("Mesh node stopped", "This install left the mesh. Local inference is unaffected.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _shutdown?.Cancel();

        // On exit the node is stopped and confirmed stopped: nothing we start may outlive the
        // window, and the group is what verifies that rather than assuming it.
        if (_process is { } process)
        {
            _children.Terminate(process);
        }

        _process?.Dispose();
        _log?.Dispose();
        _shutdown?.Dispose();
        _reader.Dispose();
        _gate.Dispose();
    }

    private MeshLaunchOptions BuildOptions() => new()
    {
        ApiPort = _config.MeshApiPort is >= 1 and <= 65535 ? _config.MeshApiPort : MeshLaunchOptions.DefaultApiPort,
        ConsolePort = _config.MeshConsolePort is >= 1 and <= 65535 ? _config.MeshConsolePort : MeshLaunchOptions.DefaultConsolePort,
        Contribute = _config.MeshContribute,
        OfferedModelPaths = _config.MeshOfferedModelPaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList(),
        MaxVramGb = Math.Max(0d, _config.MeshMaxVramGb),
        JoinTokens = _config.MeshJoined.Select(m => m.Token).ToList(),
        MeshName = string.IsNullOrWhiteSpace(_config.MeshName) ? "LocalNEXUS" : _config.MeshName,
        Publish = _config.MeshPublish
    };

    private void StartProcess(MeshLaunchOptions options)
    {
        var executable = AppPaths.FindMeshExecutable()
            ?? throw new ModelClientException(BuildMissingExecutableMessage());

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,

            // Redirected so that closing it is a request the node could act on. This build does
            // not act on it, which was tested, so the group forces the issue afterwards.
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in options.BuildArguments(Environment.MachineName))
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new ModelClientException("Windows did not start the mesh node and gave no reason.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ModelClientException($"Could not start the mesh node: {ex.Message}", ex);
        }

        _process = process;
        _children.Track(process, "mesh-llm");

        _log = new StreamWriter(AppPaths.CreateLogFilePath("mesh"), append: true) { AutoFlush = true };

        process.OutputDataReceived += OnOutput;
        process.ErrorDataReceived += OnOutput;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    private void OnOutput(object? sender, DataReceivedEventArgs e)
    {
        if (e.Data is null)
        {
            return;
        }

        try
        {
            _log?.WriteLine(e.Data);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Losing a log line must never take the node down with it.
        }
    }

    private async Task ShutdownProcessAsync()
    {
        _shutdown?.Cancel();

        if (_pollLoop is { } loop)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }

            _pollLoop = null;
        }

        if (_process is { } process)
        {
            await Task.Run(() => _children.Terminate(process)).ConfigureAwait(false);

            process.Dispose();
            _process = null;
        }

        _log?.Dispose();
        _log = null;

        _shutdown?.Dispose();
        _shutdown = null;
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PollInterval);

        try
        {
            do
            {
                if (_process is { HasExited: true } exited)
                {
                    await ReportProcessDeathAsync(exited.ExitCode).ConfigureAwait(false);
                    return;
                }

                var snapshot = await _reader.ReadAsync(ConsolePort, ApiPort, ct).ConfigureAwait(false);

                if (snapshot is null)
                {
                    await ReportUnansweredAsync().ConfigureAwait(false);
                    continue;
                }

                await _dispatcher.InvokeAsync(() => Apply(snapshot));
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    private async Task ReportProcessDeathAsync(int exitCode)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            State = MeshNodeState.Failed;
            StatusText = $"Mesh node exited with code {exitCode}";
            LastError = $"The mesh node process exited with code {exitCode}. Its output is in the logs folder.";
            Models.Clear();
            Sources.Clear();
            ThisMachine = null;
        });

        _feed.Error(
            "Mesh node stopped unexpectedly",
            $"The node process exited with code {exitCode}. Local inference is unaffected; the distributed path is unavailable until it is started again.");
    }

    private async Task ReportUnansweredAsync()
    {
        // A node takes a while to answer while it resolves and loads a model, so silence is
        // only worth reporting once the startup grace has passed.
        if (State != MeshNodeState.Starting || DateTimeOffset.UtcNow - _startedAt < StartupGrace)
        {
            return;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            StatusText = "Starting, the node has not answered its management API yet";
        });
    }

    /// <summary>
    /// Folds one snapshot into the observable state. Entries are updated in place and keyed by
    /// identity so that a list row, or a model node holding a reference, sees the change live.
    /// </summary>
    private void Apply(MeshSnapshot snapshot)
    {
        var previousState = State;

        MeshName = snapshot.MeshName;
        InviteToken = snapshot.InviteToken;
        // The engine's own rule, taken from its web console rather than guessed at: public means
        // public, publish_failed means not, and anything else defers to whether the node is
        // actually listed on the relays. Reading only the first of those said a published mesh was
        // private for as long as the state read anything but that one word.
        IsPublic = string.Equals(snapshot.PublicationState, "public", StringComparison.OrdinalIgnoreCase)
            || (!string.Equals(snapshot.PublicationState, "publish_failed", StringComparison.OrdinalIgnoreCase)
                && snapshot.NostrDiscovery);

        PublishFailed = string.Equals(snapshot.PublicationState, "publish_failed", StringComparison.OrdinalIgnoreCase);
        IsContributing = snapshot.IsServing;
        DaemonState = snapshot.DaemonState;
        LlamaReady = snapshot.LlamaReady;
        IsAttached = snapshot.IsClient || snapshot.IsServing;
        State = snapshot.IsServing ? MeshNodeState.Serving : MeshNodeState.Client;
        LastError = null;

        ReconcileSources(snapshot);
        ReconcileModels(snapshot);

        var complete = Models.Count(m => m.CanRun);
        StatusText = State == MeshNodeState.Serving
            ? $"Serving in {DescribeMesh()}, {Count(Sources.Count, "source")}, {complete} ready"
            : $"Routing in {DescribeMesh()}, {Count(Sources.Count, "source")}, {complete} ready";

        // Only a genuine transition writes to the feed. A heartbeat must never write there on
        // every tick, because every entry is a blocking hop onto the UI thread.
        if (!_announcedReady && previousState == MeshNodeState.Starting)
        {
            _announcedReady = true;
            _feed.Info(
                "Mesh node ready",
                $"{DescribeMesh()} with {Count(Sources.Count, "source")} and {Count(complete, "model")} ready to serve.");
        }
    }

    /// <summary>
    /// Puts a readable name on the local models this node was asked to serve.
    /// </summary>
    /// <remarks>
    /// The mesh names a local model by the hash of its file, which is meaningless to read. The node
    /// reports the files it was asked for, and those are the same set in the same order, so the two
    /// are paired by position.
    ///
    /// Only when the counts agree. Position is an inference rather than something the engine
    /// promises, and a name put on the wrong model would be worse than a hash: a hash is unreadable
    /// and a wrong name is believed. Anything else keeps its hash.
    /// </remarks>
    private static Dictionary<string, string> NameLocalModels(MeshSnapshot snapshot)
    {
        var named = new Dictionary<string, string>(StringComparer.Ordinal);
        var paths = snapshot.RequestedModelPaths;

        if (paths is null || paths.Count == 0)
        {
            return named;
        }

        var local = snapshot.Models
            .Select(m => m.Id)
            .Where(id => id.Contains("sha256-", StringComparison.OrdinalIgnoreCase))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (local.Count != paths.Count)
        {
            return named;
        }

        for (var i = 0; i < local.Count; i++)
        {
            var name = Path.GetFileNameWithoutExtension(paths[i]);

            if (!string.IsNullOrWhiteSpace(name))
            {
                named[local[i]] = name;
            }
        }

        return named;
    }

    /// <summary>A count and its noun, pluralised properly rather than with a bracketed s.</summary>
    private static string Count(int howMany, string noun)
        => howMany == 1 ? $"1 {noun}" : $"{howMany} {noun}s";

    private string DescribeMesh()
    {
        var name = string.IsNullOrWhiteSpace(MeshName) ? "an unnamed mesh" : $"mesh '{MeshName}'";
        return IsPublic ? $"{name} (public)" : $"{name} (private)";
    }

    private void ReconcileSources(MeshSnapshot snapshot)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(snapshot.NodeId))
        {
            var self = ThisMachine;
            if (self is null || !string.Equals(self.SourceId, snapshot.NodeId, StringComparison.Ordinal))
            {
                self = new InferenceSource(
                    snapshot.NodeId,
                    string.IsNullOrWhiteSpace(snapshot.ThisMachineName) ? Environment.MachineName : snapshot.ThisMachineName,
                    SourceLocality.ThisMachine);

                ThisMachine = self;
                Sources.Insert(0, self);
            }

            self.State = snapshot.IsServing ? SourceState.Serving : SourceState.Available;
            self.MemoryMb = snapshot.ThisMachineMemoryMb;
            self.ServingModelCount = snapshot.Models.Count(m => IsServedHere(m.Id, snapshot));
            self.LastSeenUtc = DateTimeOffset.UtcNow;
            seen.Add(self.SourceId);
        }

        foreach (var peer in snapshot.Peers)
        {
            seen.Add(peer.Id);

            var existing = Sources.FirstOrDefault(s => string.Equals(s.SourceId, peer.Id, StringComparison.Ordinal));
            if (existing is null)
            {
                existing = new InferenceSource(peer.Id, peer.DisplayName, SourceLocality.LocalNetwork);
                Sources.Add(existing);
                _feed.Info("Source joined", $"{peer.DisplayName} joined {DescribeMesh()}.");
            }

            existing.DisplayName = peer.DisplayName;
            existing.State = MapPeerState(peer);
            existing.MemoryMb = peer.MemoryMb;
            existing.RoundTripMs = peer.RoundTripMs;
            existing.ServingModelCount = peer.ServingModelIds.Count;
            existing.Version = peer.Version;
            existing.LastSeenUtc = DateTimeOffset.UtcNow;
        }

        foreach (var gone in Sources.Where(s => !seen.Contains(s.SourceId)).ToList())
        {
            Sources.Remove(gone);
            _feed.Info("Source left", $"{gone.DisplayName} is no longer in the mesh.");
        }
    }

    private static bool IsServedHere(string modelId, MeshSnapshot snapshot)
        => !snapshot.Peers.Any(p => p.ServingModelIds.Contains(modelId, StringComparer.Ordinal));

    private static SourceState MapPeerState(MeshPeer peer)
    {
        if (peer.ServingModelIds.Count > 0)
        {
            return SourceState.Serving;
        }

        return peer.State.ToLowerInvariant() switch
        {
            "disconnected" or "dead" or "unreachable" => SourceState.Unreachable,
            "" => SourceState.Unknown,
            _ => SourceState.Available
        };
    }

    private void ReconcileModels(MeshSnapshot snapshot)
    {
        var routable = snapshot.Models.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var friendly = NameLocalModels(snapshot);

        var identities = routable.Keys
            .Concat(snapshot.AnnouncedModelIds)
            .Concat(snapshot.Stages.Select(s => s.ModelId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var id in identities)
        {
            var entry = Models.FirstOrDefault(m => string.Equals(m.ModelId, id, StringComparison.Ordinal));
            if (entry is null)
            {
                entry = new NetworkServedModel(id);
                Models.Add(entry);
            }

            entry.FriendlyName = friendly.GetValueOrDefault(id);

            var isRoutable = routable.TryGetValue(id, out var model);
            if (isRoutable && model is not null)
            {
                entry.Quantization = model.Quantization;
                entry.LayerCount = model.LayerCount;
                entry.ParameterSize = model.ParameterSize;
                entry.ContextLength = model.ContextLength;
            }

            var plan = BuildPlan(id, entry.LayerCount, isRoutable, snapshot);

            entry.Plan = plan;
            entry.Availability = plan.Availability;
            entry.StatusDetail = plan.StatusDetail;
            entry.PeerCount = plan.SourceCount;
            entry.WeakestSpare = plan.WeakestSpare;
        }

        foreach (var stale in Models.Where(m => !identities.Contains(m.ModelId, StringComparer.Ordinal)).ToList())
        {
            Models.Remove(stale);
        }

        // Keep the list ordered by identity so rows do not jump around between polls.
        for (var target = 0; target < identities.Count; target++)
        {
            var current = Models.IndexOf(Models.First(m => string.Equals(m.ModelId, identities[target], StringComparison.Ordinal)));
            if (current != target)
            {
                Models.Move(current, target);
            }
        }
    }

    /// <summary>
    /// Reads the current assembly of one model out of the snapshot. A model the node can route
    /// to is complete by the engine's own contract: stage zero only becomes routable once every
    /// stage behind it reports ready.
    /// </summary>
    private CoveragePlan BuildPlan(string modelId, int layerCount, bool isRoutable, MeshSnapshot snapshot)
    {
        var stages = snapshot.Stages
            .Where(s => string.Equals(s.ModelId, modelId, StringComparison.Ordinal))
            .OrderBy(s => s.StageIndex)
            .ToList();

        var holders = stages
            .Select(s => ResolveSource(s.NodeId))
            .OfType<InferenceSource>()
            .Select(s => s.SourceId)
            .ToHashSet(StringComparer.Ordinal);

        if (stages.Count == 0)
        {
            var holder = ResolveSingleHolder(modelId, isRoutable, snapshot);
            if (holder is not null)
            {
                holders.Add(holder.SourceId);
            }

            var spare = CountSpareSources(holders);
            // A layer count of zero means the mesh has not reported the model's shape, which
            // leaves the section's bounds unknown rather than empty.
            var section = new ModelSection(0, modelId, 0, layerCount - 1);

            // With no topology reported there is nothing to be blocked on. The model was
            // announced by somebody, so either it is loading or routing has not converged yet,
            // and neither of those is a failure this install can assert.
            //
            // Which of the two it is, though, is worth saying. A model waiting on a mesh of one
            // machine is not coming up, and reporting that as loading leaves somebody watching a
            // dot that will never change. The mesh being alone is a fact this install can read
            // straight off its own source list, so it says it rather than guessing at the engine's
            // reasoning.
            return new CoveragePlan(new[]
            {
                new SourceAssignment(
                    section,
                    holder,
                    isRoutable ? StageReadiness.Ready : StageReadiness.Pending,
                    isRoutable ? "serving" : IsAlone ? "no other machine has joined" : "announced, not routable here yet",
                    spare,
                    isRoutable ? null : ExplainPending())
            });
        }

        var spareForSplit = CountSpareSources(holders);

        var assignments = stages
            .Select((stage, ordinal) =>
            {
                var source = ResolveSource(stage.NodeId);

                return new SourceAssignment(
                    new ModelSection(ordinal, modelId, stage.FirstLayer, stage.LastLayer),
                    source,
                    Classify(stage, source, isRoutable),
                    string.IsNullOrWhiteSpace(stage.State) ? "not reported yet" : stage.State,
                    spareForSplit);
            })
            .ToList();

        return new CoveragePlan(assignments);
    }

    /// <summary>True when this machine is the only one in the mesh.</summary>
    /// <remarks>
    /// Read off the source list rather than the peer list, so a machine that has joined and is not
    /// yet usable still counts as somebody being there. What this answers is whether there is
    /// anywhere other than here for a section to go.
    /// </remarks>
    private bool IsAlone => Sources.All(s => s.IsThisMachine);

    /// <summary>
    /// Why a section has not been placed, when this install can honestly say.
    /// </summary>
    /// <remarks>
    /// Only the case it is certain about. A mesh of one machine cannot place a model that does not
    /// fit on that machine, and no amount of waiting changes it; anything else is the engine's
    /// business and is left as the engine's own word for it.
    ///
    /// It says the offer is registered first, because the thing somebody needs to know is that
    /// their machine is in and working, and that what is missing is somebody else.
    /// </remarks>
    private string? ExplainPending()
    {
        if (!IsAlone)
        {
            return null;
        }

        var self = Sources.FirstOrDefault(s => s.IsThisMachine);

        var share = self is { MemoryMb: > 0 }
            ? $" and offering {self.MemoryMb / 1024d:0.#} GB"
            : string.Empty;

        var offering = IsContributing
            ? $"This machine is in the mesh{share}. "
            : "This machine is in the mesh but is not offering any of itself. ";

        return offering
               + "Nothing else has joined, so a model too large for one machine has nowhere to put "
               + "the rest of itself. It will be placed as soon as another machine joins this mesh.";
    }

    /// <summary>
    /// Maps one reported stage onto what is actually known about it.
    /// </summary>
    /// <remarks>
    /// The default is deliberately <see cref="StageReadiness.Loading"/>: an engine word this
    /// version has never seen is a reason to say nothing, not a reason to declare a failure.
    /// Only two things count as knowing the section cannot serve, namely a placement onto a node
    /// the mesh no longer lists and a state word that names a failure outright.
    /// </remarks>
    private static StageReadiness Classify(MeshStage stage, InferenceSource? source, bool isRoutable)
    {
        // Routable settles it for every stage at once: the engine only routes to stage zero
        // once each stage behind it reports ready.
        if (isRoutable)
        {
            return StageReadiness.Ready;
        }

        if (source is null)
        {
            return string.IsNullOrWhiteSpace(stage.NodeId) ? StageReadiness.Pending : StageReadiness.Missing;
        }

        return stage.State.ToLowerInvariant() switch
        {
            "ready" or "running" or "serving" => StageReadiness.Ready,
            "failed" or "error" or "stopped" or "dead" or "evicted" or "cancelled" => StageReadiness.Failed,
            _ => StageReadiness.Loading
        };
    }

    /// <summary>
    /// Matches a stage's node id to a source. Stage placements carry the full public key while
    /// peers are reported by a shortened one, so the match is by prefix in either direction.
    /// </summary>
    private InferenceSource? ResolveSource(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return null;
        }

        return Sources.FirstOrDefault(s =>
            s.SourceId.StartsWith(nodeId, StringComparison.OrdinalIgnoreCase)
            || nodeId.StartsWith(s.SourceId, StringComparison.OrdinalIgnoreCase));
    }

    private InferenceSource? ResolveSingleHolder(string modelId, bool isRoutable, MeshSnapshot snapshot)
    {
        var peer = snapshot.Peers.FirstOrDefault(p => p.ServingModelIds.Contains(modelId, StringComparer.Ordinal));
        if (peer is not null)
        {
            return ResolveSource(peer.Id);
        }

        return isRoutable ? ThisMachine : null;
    }

    /// <summary>
    /// Usable sources not already holding a piece of this model: the slack the mesh has to
    /// place a stage on if one of the current holders goes away.
    /// </summary>
    private int CountSpareSources(IReadOnlySet<string> holders)
        => Sources.Count(s => s.IsUsable && !holders.Contains(s.SourceId));

    private static string BuildMissingExecutableMessage()
    {
        var searched = string.Join(Environment.NewLine, AppPaths.EnumerateMeshSearchDirectories().Distinct());
        return $"{AppPaths.MeshExecutableName} was not found. Place a Mesh LLM build in vendor\\mesh. Searched:{Environment.NewLine}{searched}";
    }
}
