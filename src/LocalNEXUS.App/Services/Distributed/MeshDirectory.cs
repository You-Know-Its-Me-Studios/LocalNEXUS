using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.Services.Processes;

namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// Asks the public directory which meshes exist, and gets a token for joining one.
/// </summary>
/// <remarks>
/// This is the one thing in the application that contacts anything beyond the local network
/// without a model being run, so it happens only when somebody asks for it. Nothing here runs at
/// startup, on a timer, or as a side effect of opening the Network tab: browsing is an act, the
/// same way publishing is.
///
/// It runs the bundled engine rather than speaking to the relays directly. The directory is the
/// engine's own concept, the relay list is the engine's to change, and a second implementation
/// here would be a second thing to keep in step with a protocol nobody here owns.
///
/// The listing is parsed out of the engine's console output, which is a real cost and is worth
/// naming: there is no machine readable form of it, and the json log format changes how the
/// engine's own events are printed rather than how this command answers. A line that stops
/// matching yields one fewer mesh rather than an error, because a directory that lists six of
/// seven is still useful and a parser that refuses everything is not.
///
/// Tokens are fetched separately. The listing truncates them for display, so nothing in it can be
/// joined with; asking again with a filter and --auto is what produces one that works.
/// </remarks>
public sealed class MeshDirectory
{
    /// <summary>How long the engine is given to answer the relays before this gives up.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(45);

    /// <summary>
    /// One listing line, which carries everything about a mesh except its token.
    /// </summary>
    /// <remarks>
    /// Anchored on the index in brackets and the node count, which are the two parts of the format
    /// that carry meaning rather than decoration. The rest is read out of the line by label, so a
    /// field appearing, disappearing or moving costs that field and not the mesh.
    /// </remarks>
    private static readonly Regex Listing = new(
        @"^\s*\[\d+\]\s+(?<name>.+?)\s\s+(?<nodes>\d+)\s+node\(s\),\s*(?<capacity>[\d.]+)GB",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IActivityFeed _feed;
    private readonly ChildProcessGroup _children;

    public MeshDirectory(IActivityFeed feed, ChildProcessGroup children)
    {
        _feed = feed;
        _children = children;
    }

    /// <summary>
    /// Lists the meshes the public directory currently knows about.
    /// </summary>
    /// <returns>What it found, or an empty list when it found nothing or could not ask.</returns>
    /// <remarks>
    /// Never throws. Being unable to reach a directory of other people's machines is a thing that
    /// happens, and it is not a reason to fault anything the person was actually doing.
    /// </remarks>
    public async Task<IReadOnlyList<DiscoveredMesh>> ListAsync(CancellationToken ct)
    {
        var output = await RunAsync(new[] { "discover" }, ct).ConfigureAwait(false);

        if (output is null)
        {
            return Array.Empty<DiscoveredMesh>();
        }

        var found = new List<DiscoveredMesh>();

        foreach (var line in output.Split('\n'))
        {
            if (Parse(line) is { } mesh)
            {
                found.Add(mesh);
            }
        }

        return found;
    }

    /// <summary>
    /// Gets a usable invite token for the best mesh matching what was asked for.
    /// </summary>
    /// <param name="name">The mesh's own name, when it has one.</param>
    /// <param name="model">A model it serves, which is how an unnamed mesh is asked for.</param>
    /// <returns>The token, or null when nothing matched or the directory could not be reached.</returns>
    /// <remarks>
    /// Best match rather than that exact row, and that is a real limitation rather than a
    /// simplification. The directory addresses a mesh by name or by model and most of them do not
    /// name themselves, so asking for one particular unnamed mesh is not something it can express.
    /// Where a mesh has a name this is exact; where it does not, it is the best mesh serving that
    /// model, which may not be the row that was clicked.
    /// </remarks>
    public async Task<string?> ResolveTokenAsync(string? name, string? model, CancellationToken ct)
    {
        var arguments = new List<string> { "discover", "--auto" };

        if (!string.IsNullOrWhiteSpace(name))
        {
            arguments.Add("--name");
            arguments.Add(name);
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            arguments.Add("--model");
            arguments.Add(model);
        }

        var output = await RunAsync(arguments, ct).ConfigureAwait(false);

        if (output is null)
        {
            return null;
        }

        // The token is printed alone on the last line, after a run line that also contains it.
        // Taking the last standalone one avoids picking up the copy embedded in prose.
        var token = output
            .Split('\n')
            .Select(l => l.Trim())
            .LastOrDefault(l => l.Length > 40 && l.StartsWith("eyJ", StringComparison.Ordinal));

        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    /// <summary>Reads one listing line, or null when it is not one.</summary>
    private static DiscoveredMesh? Parse(string line)
    {
        var match = Listing.Match(line);

        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups["name"].Value.Trim();

        if (string.Equals(name, "(unnamed)", StringComparison.Ordinal))
        {
            name = string.Empty;
        }

        return new DiscoveredMesh(
            name,
            int.TryParse(match.Groups["nodes"].Value, out var nodes) ? nodes : 0,
            double.TryParse(match.Groups["capacity"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var gb) ? gb : 0d,
            ReadList(line, "serving:"),
            ReadList(line, "wanted:"),
            ReadList(line, "on disk:"),
            ReadNumber(line, @"score:\s*(\d+)"),
            ReadFreshness(line),
            ReadNumber(line, @"(\d+)\s+clients?"));
    }

    /// <summary>
    /// Reads one of the comma separated model lists out of a listing line.
    /// </summary>
    /// <remarks>
    /// Bounded by the next label rather than by the end of the line, because the labels run
    /// together on one line and a greedy read of "serving:" would swallow "wanted:" whole.
    /// </remarks>
    private static IReadOnlyList<string> ReadList(string line, string label)
    {
        var start = line.IndexOf(label, StringComparison.Ordinal);

        if (start < 0)
        {
            return Array.Empty<string>();
        }

        start += label.Length;

        var end = line.Length;

        foreach (var next in new[] { "wanted:", "on disk:", "(score:", "token:" })
        {
            var at = line.IndexOf(next, start, StringComparison.Ordinal);

            if (at >= 0 && at < end)
            {
                end = at;
            }
        }

        var body = line[start..end].Trim();

        if (body.Length == 0 || body.StartsWith("(no ", StringComparison.Ordinal))
        {
            return Array.Empty<string>();
        }

        return body
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static int ReadNumber(string line, string pattern)
    {
        var match = Regex.Match(line, pattern, RegexOptions.CultureInvariant);

        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : 0;
    }

    private static string ReadFreshness(string line)
        => Regex.Match(line, @"score:\s*\d+,\s*(?<state>[a-z]+)", RegexOptions.CultureInvariant) is { Success: true } m
            ? m.Groups["state"].Value
            : string.Empty;

    /// <summary>Runs the bundled engine and returns everything it printed, or null when it could not run.</summary>
    private async Task<string?> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var executable = AppPaths.FindMeshExecutable();

        if (executable is null)
        {
            _feed.Error(
                "Mesh directory unavailable",
                $"{AppPaths.MeshExecutableName} was not found, so there is nothing to ask.");

            return null;
        }

        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start);

            if (process is null)
            {
                return null;
            }

            _children.Track(process, "mesh directory");

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(Timeout);

            var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var stderr = process.StandardError.ReadToEndAsync(deadline.Token);

            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);

            return await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _feed.Info("Mesh directory did not answer", $"Nothing came back within {Timeout.TotalSeconds:0} seconds.");
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            _feed.Error("Mesh directory could not be asked", ex.Message);
            return null;
        }
    }
}
