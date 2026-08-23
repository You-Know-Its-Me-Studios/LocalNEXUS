namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// How this install's mesh node is started.
/// </summary>
/// <remarks>
/// Private by default and deliberately so: with no invite token and no publication, the node
/// creates a mesh only the holder of its token can join, and LAN discovery keeps the engine
/// off public relays entirely. Publishing is a separate, explicit choice.
/// </remarks>
public sealed record MeshLaunchOptions
{
    /// <summary>Port the OpenAI compatible API listens on.</summary>
    public const int DefaultApiPort = 9337;

    /// <summary>Port the management API listens on.</summary>
    public const int DefaultConsolePort = 3131;

    /// <summary>Port the OpenAI compatible API listens on. Model nodes send requests here.</summary>
    public int ApiPort { get; init; } = DefaultApiPort;

    /// <summary>Port the management API answers on. Everything the Network tab renders is read from here.</summary>
    public int ConsolePort { get; init; } = DefaultConsolePort;

    /// <summary>True when this machine offers its own compute rather than only routing requests.</summary>
    public bool Contribute { get; init; }

    /// <summary>
    /// The models this machine serves while contributing, in the order they were chosen. Empty
    /// means it offers capacity without a model of its own.
    /// </summary>
    /// <remarks>
    /// Only the first is a launch argument, because the engine takes one startup model on the
    /// command line. The rest are loaded into the node once it is up, which is the engine's own
    /// mechanism for it and keeps this from having to know how many models a node can hold.
    /// </remarks>
    public IReadOnlyList<string> OfferedModelPaths { get; init; } = Array.Empty<string>();

    /// <summary>The model passed on the command line, or blank when none was chosen.</summary>
    public string StartupModelPath => OfferedModelPaths.FirstOrDefault() ?? string.Empty;

    /// <summary>The models loaded after the node answers, which is everything after the first.</summary>
    public IReadOnlyList<string> AdditionalModelPaths => OfferedModelPaths.Skip(1).ToList();

    /// <summary>Cap on the memory this machine offers, in GB. Zero lets the engine decide.</summary>
    public double MaxVramGb { get; init; }

    /// <summary>
    /// Invite tokens of meshes to join. Empty means this node hosts its own private mesh.
    /// </summary>
    /// <remarks>
    /// Several, because the engine's join argument repeats and a machine can be in more than one
    /// mesh at a time. It was one, so joining a second silently replaced the first.
    /// </remarks>
    public IReadOnlyList<string> JoinTokens { get; init; } = Array.Empty<string>();

    /// <summary>Friendly name for the mesh this node hosts.</summary>
    public string MeshName { get; init; } = "LocalNEXUS";

    /// <summary>
    /// Advertises this mesh for public discovery. Off by default, and the only setting that
    /// causes the engine to talk to anything outside the local network.
    /// </summary>
    public bool Publish { get; init; }

    /// <summary>Builds the argument list for the node process.</summary>
    public IReadOnlyList<string> BuildArguments(string nodeName)
    {
        var arguments = new List<string>
        {
            Contribute ? "serve" : "client",
            "--headless",
            "--port",
            ApiPort.ToString(),
            "--console",
            ConsolePort.ToString(),
            "--name",
            nodeName
        };

        if (Contribute)
        {
            if (!string.IsNullOrWhiteSpace(StartupModelPath))
            {
                arguments.Add("--gguf");
                arguments.Add(StartupModelPath);
            }

            if (MaxVramGb > 0)
            {
                arguments.Add("--max-vram");
                arguments.Add(MaxVramGb.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            }

            arguments.Add("--mesh-name");
            arguments.Add(MeshName);
        }

        foreach (var token in JoinTokens.Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            arguments.Add("--join");
            arguments.Add(token.Trim());
        }

        if (Publish)
        {
            arguments.Add("--publish");
        }
        else
        {
            // LAN scoped discovery: no public relays, no public address probing. This is what
            // makes "private by default" a property of the transport rather than a promise.
            arguments.Add("--mesh-discovery-mode");
            arguments.Add("mdns");
        }

        return arguments;
    }
}
