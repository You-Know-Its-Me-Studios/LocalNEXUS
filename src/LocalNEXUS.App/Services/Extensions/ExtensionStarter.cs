using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models.Extensions;

namespace LocalNEXUS.App.Services.Extensions;

/// <summary>
/// Asks an extension what it can do, and asks all of them when a project opens.
/// </summary>
/// <remarks>
/// An extension registered against a project loaded as unreachable and stayed that way until
/// somebody opened the panel and pressed Test connect, once per extension, every time the project
/// was opened. Nothing about that was a decision: registering an extension against a project is
/// already the statement that this project uses it, and being asked to say so again on every
/// launch is the application making somebody repeat themselves.
///
/// So the same question is asked automatically when a project opens. It is the same question, and
/// deliberately the same code: the panel's button calls <see cref="ConnectAsync"/> too, so what
/// pressing it does and what opening a project does cannot come apart.
///
/// Each one is started, asked, and stopped again, which is what Test connect always did and what
/// keeps the lazy start intact. Nothing is left running at launch, because an install with a dozen
/// extensions must not cost a dozen processes for a session that uses none of them. What is kept is
/// the answer: the tool list, and whether it answered at all.
///
/// One at a time rather than all at once. A package runner is a launcher that may download before
/// it runs, and a dozen of those racing at the moment a project opens is the worst time for it.
/// </remarks>
public sealed class ExtensionStarter
{
    private readonly ExtensionRegistry _registry;
    private readonly ExtensionHost _host;
    private readonly IActivityFeed _feed;
    private readonly System.Windows.Threading.Dispatcher? _dispatcher;

    private CancellationTokenSource? _pass;

    /// <summary>
    /// Builds a starter.
    /// </summary>
    /// <param name="registry">What this project has installed.</param>
    /// <param name="host">What starts one and talks to it.</param>
    /// <param name="feed">Where what happened is reported.</param>
    /// <param name="dispatcher">
    /// The user interface thread, because the tool list an extension reports is a collection the
    /// extensions window is bound to. The binding engine marshals a property change and never a
    /// collection change, so a list rebuilt from a worker thread throws where a property would have
    /// been fine. Null outside the application, where there is no thread to marshal to.
    /// </param>
    public ExtensionStarter(
        ExtensionRegistry registry,
        ExtensionHost host,
        IActivityFeed feed,
        System.Windows.Threading.Dispatcher? dispatcher = null)
    {
        _registry = registry;
        _host = host;
        _feed = feed;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Connects every enabled extension this project has, in turn.
    /// </summary>
    /// <remarks>
    /// A pass already running is cancelled first, because opening a second project while the first
    /// one's extensions are still being asked would otherwise write the old project's answers onto
    /// the new project's panel.
    ///
    /// Nothing waits on this and nothing fails because of it. An extension that will not answer is
    /// recorded as unreachable with its reason, which is exactly the state it was in before, so the
    /// worst case is what used to happen anyway.
    /// </remarks>
    public async Task ConnectAllAsync()
    {
        var previous = Interlocked.Exchange(ref _pass, new CancellationTokenSource());
        previous?.Cancel();
        previous?.Dispose();

        var pass = _pass!;
        var ct = pass.Token;

        // Copied, because connecting takes seconds and somebody can add or remove one meanwhile.
        var pending = _registry.Extensions.Where(e => e.IsEnabled).ToList();

        if (pending.Count == 0)
        {
            return;
        }

        var answered = 0;

        foreach (var extension in pending)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (await ConnectAsync(extension, ct).ConfigureAwait(true))
            {
                answered++;
            }
        }

        if (ct.IsCancellationRequested)
        {
            return;
        }

        _feed.Info(
            $"{answered} of {pending.Count} extension(s) answered",
            answered == pending.Count
                ? null
                : "The rest are listed as unreachable in Extensions, each with what stopped it.");
    }

    /// <summary>
    /// Starts one extension, reads what it can do, and shuts it down again.
    /// </summary>
    /// <returns>True when it answered.</returns>
    /// <remarks>
    /// The point of this is to move the moment a bad configuration is discovered from the middle of
    /// a run to the moment the project is opened.
    /// </remarks>
    public async Task<bool> ConnectAsync(InstalledExtension extension, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(extension);

        try
        {
            if (extension.Manifest.ProvidesTools)
            {
                var session = await _host
                    .EnsureRunningAsync(extension, ExtensionContract.Mcp, ct)
                    .ConfigureAwait(true);

                var tools = await new McpToolClient(session).ListToolsAsync(ct).ConfigureAwait(true);

                OnDispatcher(() =>
                {
                    extension.DiscoveredTools.Clear();

                    foreach (var tool in tools)
                    {
                        extension.DiscoveredTools.Add(tool);
                    }
                });

                _feed.Info($"{extension.Manifest.Name} answered", $"{tools.Count} tool(s).");
            }

            if (extension.Manifest.ProvidesNodes)
            {
                var session = await _host
                    .EnsureRunningAsync(extension, ExtensionContract.Node, ct)
                    .ConfigureAwait(true);

                var described = await new NodeWorkerClient(session).DescribeAsync(ct).ConfigureAwait(true);

                _feed.Info($"{extension.Manifest.Name} answered", $"{described.Count} node type(s).");
            }

            extension.State = ExtensionState.Running;
            extension.StateDetail = null;

            return true;
        }
        catch (OperationCanceledException)
        {
            // The project was closed or another one was opened. Not a failure of the extension, and
            // saying it failed would leave a red row about a project nobody is looking at.
            extension.State = ExtensionState.Unreachable;
            extension.StateDetail = "Not started yet.";

            return false;
        }
        catch (ExtensionException ex)
        {
            extension.State = ExtensionState.Unreachable;
            extension.StateDetail = ex.Message;
            _feed.Error($"{extension.Manifest.Name} did not answer", ex.Message);

            return false;
        }
        finally
        {
            // Started only to ask. Leaving it running would be a process nobody asked for.
            _host.Stop(extension.Manifest.Id);
            _registry.Save();
        }
    }

    /// <summary>Runs something on the thread that owns the bound collections.</summary>
    private void OnDispatcher(Action work)
    {
        if (_dispatcher is null || _dispatcher.CheckAccess())
        {
            work();
            return;
        }

        _dispatcher.Invoke(work);
    }
}
