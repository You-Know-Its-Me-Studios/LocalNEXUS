using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.Services.Processes;

namespace LocalNEXUS.App.Services.Python;

/// <summary>
/// Builds and repairs the isolated Python environment the safetensors runtime is served from.
/// </summary>
/// <remarks>
/// The environment is a supervised child process's environment and nothing else. It is never
/// loaded into this process, it is never a Python already on the machine, and nothing but this
/// class writes to it, so there is no shared installation for a future addition to pollute.
/// uv is bundled and the interpreter is one uv downloads, which means an install works on a
/// machine with no Python on it at all.
///
/// Provisioning runs on first launch for every install rather than the first time a safetensors
/// model is picked, because a 2 GB download that starts in the middle of real work is a worse
/// failure than one that starts while the application is being opened for the first time. The
/// application stays usable throughout, and GGUF models work immediately.
/// </remarks>
public sealed partial class PythonProvisioner : ObservableObject
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>The interpreter version the lockfiles were compiled against.</summary>
    private const string PythonVersion = "3.12";

    /// <summary>What has to import before the environment is called ready.</summary>
    private static readonly string[] RequiredImports = { "torch", "transformers", "accelerate", "fastapi", "uvicorn" };

    private readonly ChildProcessGroup _children;
    private readonly IActivityFeed _feed;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Where the environment has got to. Drives what the panel says and shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private PythonEnvironmentState _state = PythonEnvironmentState.Unknown;

    /// <summary>The coarse stage in progress, for example creating the environment.</summary>
    [ObservableProperty]
    private string _stage = "Not checked yet";

    /// <summary>The most recent line of live output from uv or the interpreter.</summary>
    [ObservableProperty]
    private string _detail = string.Empty;

    /// <summary>Why the environment failed, when it did. Empty otherwise.</summary>
    [ObservableProperty]
    private string _lastError = string.Empty;

    /// <summary>Which torch build this machine was given, and why.</summary>
    [ObservableProperty]
    private string _acceleratorSummary = string.Empty;

    public PythonProvisioner(ChildProcessGroup children, IActivityFeed feed, Dispatcher dispatcher)
    {
        _children = children;
        _feed = feed;
        _dispatcher = dispatcher;
    }

    /// <summary>True once the packages have been imported successfully.</summary>
    public bool IsReady => State == PythonEnvironmentState.Ready;

    /// <summary>True while work is in progress, so the panel can disable its buttons.</summary>
    public bool IsBusy => State == PythonEnvironmentState.Provisioning;

    /// <summary>A sentence for the panel that never claims more than is known.</summary>
    public string StatusText => State switch
    {
        PythonEnvironmentState.Ready => "Ready. Safetensors models can be run.",
        PythonEnvironmentState.Provisioning => "Setting up. GGUF models work while this runs.",
        PythonEnvironmentState.Failed => "Setup did not finish. Safetensors models cannot be run yet.",
        PythonEnvironmentState.Missing => "Not set up yet. Safetensors models cannot be run until it is.",
        _ => "Not checked yet."
    };

    /// <summary>The interpreter to run, once the environment is ready.</summary>
    public string InterpreterPath => AppPaths.PythonExecutable;

    /// <summary>
    /// Brings the environment up if it is not already there, and verifies it either way. Safe to
    /// call from anywhere: a second call while one is running waits for the first rather than
    /// starting a second install into the same folder.
    /// </summary>
    public async Task<bool> EnsureAsync(CancellationToken ct)
        => await RunAsync(reset: false, ct).ConfigureAwait(false);

    /// <summary>
    /// Rebuilds whatever is missing or broken, reusing the download cache. This is the repair
    /// path: it does not throw away work that is already correct.
    /// </summary>
    public async Task<bool> RepairAsync(CancellationToken ct)
        => await RunAsync(reset: false, ct).ConfigureAwait(false);

    /// <summary>
    /// Deletes the environment and builds it again. The downloads are still cached, so this
    /// costs time rather than bandwidth.
    /// </summary>
    public async Task<bool> ResetAsync(CancellationToken ct)
        => await RunAsync(reset: true, ct).ConfigureAwait(false);

    /// <summary>
    /// The reason a safetensors run cannot go ahead, or null when it can. Phrased for the person
    /// reading the activity feed rather than for a log.
    /// </summary>
    public string? DescribeUnavailability() => State switch
    {
        PythonEnvironmentState.Ready => null,
        PythonEnvironmentState.Provisioning => "The Python runtime is still being set up. The Local model panel shows how far it has got.",
        PythonEnvironmentState.Failed => $"The Python runtime is not usable: {LastError} Repair it from the Local model panel.",
        PythonEnvironmentState.Missing => "The Python runtime has not been set up. Set it up from the Local model panel.",
        _ => "The Python runtime has not been checked yet."
    };

    private async Task<bool> RunAsync(bool reset, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await SetAsync(PythonEnvironmentState.Provisioning, "Checking hardware", string.Empty).ConfigureAwait(false);

            var choice = AcceleratorProbe.Detect();
            await _dispatcher.InvokeAsync(() => AcceleratorSummary = choice.Reason);
            _feed.Info("Python runtime", choice.Reason);

            var uv = AppPaths.FindUvExecutable();
            if (uv is null)
            {
                return await FailAsync(BuildMissingUvMessage()).ConfigureAwait(false);
            }

            var lockfile = AppPaths.FindPythonLockfile(choice.LockfileName);
            if (lockfile is null)
            {
                return await FailAsync(
                    $"The dependency lockfile {choice.LockfileName} was not found beside the application.").ConfigureAwait(false);
            }

            var lockfileHash = HashOf(lockfile);

            if (reset)
            {
                await SetStageAsync("Removing the old environment").ConfigureAwait(false);
                DeleteEnvironment();
            }
            else if (IsRecordCurrent(choice, lockfileHash) && await VerifyAsync(ct).ConfigureAwait(false))
            {
                // Already built from exactly this lockfile, and the packages import. Nothing to do.
                await SetAsync(PythonEnvironmentState.Ready, "Ready", string.Empty).ConfigureAwait(false);
                return true;
            }

            Directory.CreateDirectory(AppPaths.PythonRoot);

            // Only created when it is not already there. uv refuses to create over an existing
            // environment, and clearing one would throw away an install that may be perfectly
            // good, which is the opposite of what repairing is for.
            if (!File.Exists(AppPaths.PythonExecutable))
            {
                await SetStageAsync($"Creating the environment on Python {PythonVersion}").ConfigureAwait(false);

                var venv = await RunUvAsync(
                    uv,
                    new[] { "venv", "--python", PythonVersion, AppPaths.PythonVenv },
                    ct).ConfigureAwait(false);

                if (!venv)
                {
                    return await FailAsync("The Python environment could not be created. The output above says why.").ConfigureAwait(false);
                }
            }

            var sizeNote = choice.Accelerator == PythonAccelerator.Cuda
                ? "about 2 GB, mostly torch"
                : "about 250 MB";

            await SetStageAsync($"Installing torch and the serving stack ({sizeNote})").ConfigureAwait(false);

            var installed = await RunUvAsync(
                uv,
                new[]
                {
                    "pip", "sync",
                    "--python", AppPaths.PythonExecutable,

                    // The lockfile pins builds that live on the torch index and packages that
                    // live on PyPI, so both are searched for the best match rather than the
                    // first index that has any version of a name.
                    "--index-strategy", "unsafe-best-match",
                    lockfile
                },
                ct).ConfigureAwait(false);

            if (!installed)
            {
                return await FailAsync("Installing the Python packages failed. The output above says why.").ConfigureAwait(false);
            }

            await SetStageAsync("Verifying the environment").ConfigureAwait(false);

            if (!await VerifyAsync(ct).ConfigureAwait(false))
            {
                return await FailAsync(
                    "The packages installed but could not be imported, so the environment is not usable.").ConfigureAwait(false);
            }

            WriteRecord(new PythonEnvironmentRecord
            {
                LockfileName = choice.LockfileName,
                LockfileHash = lockfileHash,
                Accelerator = choice.Accelerator,
                CompletedUtc = DateTime.UtcNow
            });

            await SetAsync(PythonEnvironmentState.Ready, "Ready", string.Empty).ConfigureAwait(false);
            _feed.Info("Python runtime ready", $"Safetensors models can be run. {choice.Reason}");

            PruneDownloadCache();
            return true;
        }
        catch (OperationCanceledException)
        {
            await SetAsync(PythonEnvironmentState.Missing, "Cancelled", string.Empty).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return await FailAsync(ex.Message).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Checks the environment by importing what the runtime needs. An install command's exit
    /// code says the download succeeded, which is a different question from whether the packages
    /// load on this machine, and that is the question that matters.
    /// </summary>
    private async Task<bool> VerifyAsync(CancellationToken ct)
    {
        if (!File.Exists(AppPaths.PythonExecutable))
        {
            return false;
        }

        var script = string.Join("; ", RequiredImports.Select(name => $"import {name}"));

        var startInfo = CreateStartInfo(AppPaths.PythonExecutable);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add($"{script}; print('imports ok')");

        var (exitCode, output) = await RunProcessAsync(startInfo, reportOutput: false, ct).ConfigureAwait(false);

        if (exitCode == 0 && output.Contains("imports ok", StringComparison.Ordinal))
        {
            return true;
        }

        await SetDetailAsync(FirstMeaningfulLine(output)).ConfigureAwait(false);
        return false;
    }

    private async Task<bool> RunUvAsync(string uv, IEnumerable<string> arguments, CancellationToken ct)
    {
        var startInfo = CreateStartInfo(uv);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var (exitCode, _) = await RunProcessAsync(startInfo, reportOutput: true, ct).ConfigureAwait(false);
        return exitCode == 0;
    }

    /// <summary>
    /// The environment every child of this class runs in. Everything uv writes is redirected
    /// into the application's own folders, so nothing lands in a shared cache or a user wide
    /// Python installation, and no Python already on the machine is ever selected.
    /// </summary>
    private static ProcessStartInfo CreateStartInfo(string executable)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = AppPaths.PythonRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.Environment["UV_PYTHON_INSTALL_DIR"] = AppPaths.PythonInterpreters;
        startInfo.Environment["UV_CACHE_DIR"] = AppPaths.PythonCache;
        startInfo.Environment["UV_PYTHON_PREFERENCE"] = "only-managed";
        startInfo.Environment["UV_NO_PROGRESS"] = "1";

        // An activated environment in the parent's environment would otherwise be inherited and
        // silently become the target of an install.
        startInfo.Environment["VIRTUAL_ENV"] = string.Empty;
        startInfo.Environment["PYTHONHOME"] = string.Empty;
        startInfo.Environment["PYTHONPATH"] = string.Empty;

        return startInfo;
    }

    private async Task<(int ExitCode, string Output)> RunProcessAsync(
        ProcessStartInfo startInfo,
        bool reportOutput,
        CancellationToken ct)
    {
        Directory.CreateDirectory(AppPaths.PythonRoot);

        using var process = Process.Start(startInfo)
            ?? throw new IOException($"Windows did not start {startInfo.FileName} and gave no reason.");

        _children.Track(process, "python-setup");

        var collected = new StringBuilder();

        var readOut = PumpAsync(process.StandardOutput, collected, reportOutput, ct);
        var readError = PumpAsync(process.StandardError, collected, reportOutput, ct);

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        await Task.WhenAll(readOut, readError).ConfigureAwait(false);

        _children.Terminate(process);

        return (process.ExitCode, collected.ToString());
    }

    private async Task PumpAsync(StreamReader reader, StringBuilder collected, bool report, CancellationToken ct)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            lock (collected)
            {
                collected.AppendLine(line);
            }

            if (report && line.Trim().Length > 0)
            {
                await SetDetailAsync(line.Trim()).ConfigureAwait(false);
            }
        }
    }

    private bool IsRecordCurrent(AcceleratorChoice choice, string lockfileHash)
    {
        var record = ReadRecord();

        return record is not null
               && record.Accelerator == choice.Accelerator
               && string.Equals(record.LockfileName, choice.LockfileName, StringComparison.OrdinalIgnoreCase)
               && string.Equals(record.LockfileHash, lockfileHash, StringComparison.Ordinal);
    }

    /// <summary>
    /// True when a finished environment is already on this machine.
    /// </summary>
    /// <remarks>
    /// Cheap on purpose: a recorded completion and an interpreter where it should be. It is not a
    /// verification, because verification runs the interpreter and imports the packages, and this
    /// is answered before a window is drawn.
    ///
    /// It exists so that nobody is asked for permission to build something that is already built.
    /// The consent question is about spending three gigabytes and somebody's bandwidth; when the
    /// environment is sitting on the disk there is nothing to spend and nothing to agree to, and
    /// asking anyway is how a question becomes noise.
    /// </remarks>
    public static bool IsAlreadyBuilt()
        => File.Exists(AppPaths.PythonExecutable)
            && ReadRecord() is { } record
            && record.CompletedUtc != default;

    private static PythonEnvironmentRecord? ReadRecord()
    {
        try
        {
            if (!File.Exists(AppPaths.PythonStateFile))
            {
                return null;
            }

            return JsonSerializer.Deserialize<PythonEnvironmentRecord>(
                File.ReadAllText(AppPaths.PythonStateFile),
                SerializerOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Removes the wheels uv downloaded, now that they are installed.
    /// </summary>
    /// <remarks>
    /// The cache holds a copy of everything that went into the environment, so after a successful
    /// build it is roughly the size of the environment again and serves nothing: the environment
    /// is verified, the record is written, and a rebuild downloads what it needs anyway. Keeping
    /// it doubles the cost of the one thing on this machine measured in gigabytes.
    ///
    /// Only after success, and never on the repair path before verification, because a cache is
    /// exactly what makes a second attempt quick when the first one did not finish.
    ///
    /// Best effort. A cache that will not delete, usually because something still has a handle on
    /// it, is disk that will be reclaimed next time rather than a reason to report a failure for
    /// an environment that is working.
    /// </remarks>
    private void PruneDownloadCache()
    {
        try
        {
            if (!Directory.Exists(AppPaths.PythonCache))
            {
                return;
            }

            var before = DirectoryBytes(AppPaths.PythonCache);
            Directory.Delete(AppPaths.PythonCache, recursive: true);

            if (before > 0)
            {
                _feed.Info(
                    "Reclaimed the Python download cache",
                    $"{before / (1024.0 * 1024):0.#} MB of wheels that are already installed.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left where it is. It costs disk, not correctness, and the next successful build
            // tries again.
        }
    }

    private static long DirectoryBytes(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Sum(file =>
                {
                    try
                    {
                        return new FileInfo(file).Length;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        return 0L;
                    }
                });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0L;
        }
    }

    private static void WriteRecord(PythonEnvironmentRecord record)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.PythonRoot);
            File.WriteAllText(AppPaths.PythonStateFile, JsonSerializer.Serialize(record, SerializerOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the record costs a verification pass on the next launch, nothing more.
        }
    }

    private static void DeleteEnvironment()
    {
        try
        {
            if (File.Exists(AppPaths.PythonStateFile))
            {
                File.Delete(AppPaths.PythonStateFile);
            }

            if (Directory.Exists(AppPaths.PythonVenv))
            {
                Directory.Delete(AppPaths.PythonVenv, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Whatever survives is dealt with by the install, which overwrites what it owns.
        }
    }

    private static string HashOf(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static string FirstMeaningfulLine(string output)
    {
        var line = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(l => l.Length > 0);

        return line ?? string.Empty;
    }

    private static string BuildMissingUvMessage()
    {
        var searched = string.Join(
            Environment.NewLine,
            AppPaths.EnumerateUvSearchDirectories().Distinct(StringComparer.OrdinalIgnoreCase).Take(3));

        return $"{AppPaths.UvExecutableName} was not found, so the Python runtime cannot be set up. "
               + $"Place a uv build in vendor\\uv. Searched:{Environment.NewLine}{searched}";
    }

    private async Task<bool> FailAsync(string reason)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            State = PythonEnvironmentState.Failed;
            Stage = "Setup failed";
            LastError = reason;
        });

        _feed.Info("Python runtime unavailable", reason);
        return false;
    }

    private async Task SetAsync(PythonEnvironmentState state, string stage, string detail)
        => await _dispatcher.InvokeAsync(() =>
        {
            State = state;
            Stage = stage;
            Detail = detail;

            if (state != PythonEnvironmentState.Failed)
            {
                LastError = string.Empty;
            }
        });

    private async Task SetStageAsync(string stage)
    {
        await _dispatcher.InvokeAsync(() => Stage = stage);
        _feed.Info("Python runtime", stage);
    }

    private async Task SetDetailAsync(string detail)
        => await _dispatcher.InvokeAsync(() => Detail = detail);
}
