namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// The per launch settings of a llama-server process.
/// </summary>
/// <remarks>
/// Two launches of the same GGUF with different options are different servers, so these values
/// are part of the key the manager tracks servers under. These options describe a purely local
/// launch: anything that spans machines is the mesh node's to arrange, not this process's. The
/// defaults reproduce the behaviour the application shipped with: an 8192 token context and
/// every layer offloaded to the GPU.
/// </remarks>
public sealed record LlamaLaunchOptions
{
    /// <summary>Context window used when a node does not override it.</summary>
    public const int DefaultContextSize = 8192;

    /// <summary>
    /// Default GPU layer count. Deliberately larger than any real model's layer count, which
    /// llama.cpp treats as "offload everything".
    /// </summary>
    public const int DefaultGpuLayers = 999;

    /// <summary>Context window passed to the server with <c>-c</c>.</summary>
    public int ContextSize { get; init; } = DefaultContextSize;

    /// <summary>Layers offloaded to the GPU, passed with <c>-ngl</c>.</summary>
    public int GpuLayers { get; init; } = DefaultGpuLayers;

    /// <summary>
    /// The multimodal projector to load beside the weights, passed with <c>--mmproj</c>.
    /// </summary>
    /// <remarks>
    /// Null for every ordinary model, which is all of them but a vision one. It is part of the key
    /// because the same weights loaded with and without a projector are two different servers: one
    /// can see and one answers 400 to every image.
    /// </remarks>
    public string? ProjectorPath { get; init; }

    /// <summary>
    /// The key a server started with these options is tracked under, so one entry exists per
    /// model and configuration pair.
    /// </summary>
    /// <summary>
    /// The command line a server launched with these options is given.
    /// </summary>
    /// <remarks>
    /// Here rather than inside the manager because these are the options' own business, and
    /// because an argument list that can only be seen by starting a process is one nothing can
    /// check. The manager adds nothing to it.
    /// </remarks>
    public IReadOnlyList<string> BuildArguments(string fullModelPath, int port)
    {
        var arguments = new List<string>
        {
            "-m", fullModelPath,
            "--host", "127.0.0.1",
            "--port", port.ToString(),
            "-c", ContextSize.ToString(),
            "-ngl", GpuLayers.ToString()
        };

        // The one argument that makes a server able to see. Without it a vision GGUF loads
        // perfectly well and then refuses every image, which is why it is found for the user
        // rather than asked of them.
        if (ProjectorPath is { Length: > 0 } projector)
        {
            arguments.Add("--mmproj");
            arguments.Add(projector);
        }

        return arguments;
    }

    public string BuildServerKey(string fullModelPath)
    {
        var key = $"{fullModelPath}|c{ContextSize}|ngl{GpuLayers}";

        return ProjectorPath is { Length: > 0 } projector ? $"{key}|mmproj{projector}" : key;
    }
}

/// <summary>
/// What a server is actually running with, as opposed to what a node asks for.
/// </summary>
/// <remarks>
/// The two are the same until somebody edits a load parameter, and then they differ until the next
/// run restarts the server. Showing the live values is what keeps that gap visible rather than
/// leaving it to be discovered by a refusal.
/// </remarks>
/// <param name="ContextSize">The context the key and value cache was allocated for.</param>
/// <param name="GpuLayers">How many layers were offloaded.</param>
/// <param name="Port">Where it is listening, which is useful when something else wants to look.</param>
public sealed record RunningServer(int ContextSize, int GpuLayers, int Port);

/// <summary>
/// What a local model is doing right now, as the node draws it.
/// </summary>
/// <remarks>
/// Four states because there are four, and the two in the middle are the ones that used to be
/// invisible. Starting is not failed and is not idle: a model coming up holds the run for tens of
/// seconds and a node that says nothing during it looks broken. Restarting is not starting either,
/// because it happens for a reason somebody caused and the reason is worth saying.
/// </remarks>
public enum LocalModelState
{
    /// <summary>No server is up for this model. The ordinary state before a first run.</summary>
    NotLoaded,

    /// <summary>A server is coming up and the run is waiting for it.</summary>
    Starting,

    /// <summary>A server is up and answering.</summary>
    Running,

    /// <summary>A server is being stopped because a load parameter changed, and restarted.</summary>
    Restarting
}
