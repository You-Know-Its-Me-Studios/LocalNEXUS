using System.Diagnostics;
using System.IO;
using LocalNEXUS.App.Services.Processes;

namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// One llama-server child process serving one GGUF file.
/// </summary>
/// <remarks>
/// The process writes nothing to a console. Its standard output and error are pumped into a log
/// file under the user data folder, which is also where the manager looks when a server dies
/// during startup and the failure has to be explained to the user.
///
/// Stopping it is not this class's job. The child process group owns that, because a single tree
/// kill can race a child that starts another one and reports the failure as an aggregate that
/// callers were not catching. Disposing an instance asks the group to terminate it and confirm.
/// </remarks>
public sealed class LlamaServerInstance : IDisposable
{
    private const int RetainedLogLines = 40;

    private readonly Queue<string> _recentOutput = new();
    private readonly object _sync = new();

    private readonly ChildProcessGroup _children;

    private StreamWriter? _log;
    private bool _disposed;

    public LlamaServerInstance(
        Process process,
        string ggufPath,
        int port,
        string logPath,
        ChildProcessGroup children,
        LlamaLaunchOptions options)
    {
        Process = process;
        GgufPath = ggufPath;
        Port = port;
        LogPath = logPath;
        Options = options;
        _children = children;
    }

    /// <summary>The running child process.</summary>
    public Process Process { get; }

    /// <summary>The model this server was started for.</summary>
    public string GgufPath { get; }

    /// <summary>The loopback port the server is listening on.</summary>
    public int Port { get; }

    /// <summary>
    /// What this server was actually started with.
    /// </summary>
    /// <remarks>
    /// Kept because a load parameter is fixed at start: llama-server allocates the key and value
    /// cache when it comes up, so the context a running server has is the context it was launched
    /// with and nothing said to it afterwards changes that. Somebody who edits the field needs to
    /// be able to see the two values differ rather than find out from a refusal.
    /// </remarks>
    public LlamaLaunchOptions Options { get; }

    /// <summary>Where this server's output is being written.</summary>
    public string LogPath { get; }

    /// <summary>Root of this server's OpenAI compatible API.</summary>
    public string BaseUrl => $"http://127.0.0.1:{Port}/v1";

    /// <summary>The health endpoint polled while the model loads.</summary>
    public string HealthUrl => $"http://127.0.0.1:{Port}/health";

    /// <summary>True while the process is still alive.</summary>
    public bool IsRunning
    {
        get
        {
            try
            {
                return !Process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    /// <summary>Starts pumping the child process output into the log file.</summary>
    public void BeginCapturingOutput()
    {
        _log = new StreamWriter(
            new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.Read),
            System.Text.Encoding.UTF8)
        {
            AutoFlush = true
        };

        Process.OutputDataReceived += OnOutputDataReceived;
        Process.ErrorDataReceived += OnOutputDataReceived;
        Process.BeginOutputReadLine();
        Process.BeginErrorReadLine();
    }

    /// <summary>The last few lines the server produced, used to explain a startup failure.</summary>
    public string GetRecentOutput()
    {
        lock (_sync)
        {
            return string.Join(Environment.NewLine, _recentOutput);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Process.OutputDataReceived -= OnOutputDataReceived;
        Process.ErrorDataReceived -= OnOutputDataReceived;

        _children.Terminate(Process);

        Process.Dispose();
        _log?.Dispose();
        _log = null;
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null)
        {
            return;
        }

        lock (_sync)
        {
            _recentOutput.Enqueue(e.Data);
            while (_recentOutput.Count > RetainedLogLines)
            {
                _recentOutput.Dequeue();
            }

            try
            {
                _log?.WriteLine(e.Data);
            }
            catch (IOException)
            {
                // Losing a log line must never take down a run.
            }
        }
    }
}
