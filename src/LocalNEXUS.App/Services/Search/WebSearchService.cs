using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Services.Credentials;
using LocalNEXUS.App.Services.Inference;

namespace LocalNEXUS.App.Services.Search;

/// <summary>One result, as much of it as a snippet carries.</summary>
/// <param name="Title">What the page calls itself.</param>
/// <param name="Url">Where it is, so a person can go and read it.</param>
/// <param name="Snippet">The extract the index returned. Never the page.</param>
public sealed record SearchResult(string Title, string Url, string Snippet);

/// <summary>Why a search could not be offered, when it could not.</summary>
public enum SearchAvailability
{
    /// <summary>No key, so there is nothing to offer and no toggle to show.</summary>
    NoKey,

    /// <summary>A key is present and search can be offered.</summary>
    Available
}

/// <summary>
/// Web search, offered to a model as a tool it may call.
/// </summary>
/// <remarks>
/// Brave, because it runs its own index of about thirty billion pages rather than reselling
/// somebody else's, which matters now that Bing's public search API was retired in August 2025 and
/// Google's JSON API is capped, closed to new customers and retiring in January 2027. It is also
/// what Anthropic's own web search runs on.
///
/// The key is the user's. Bundling one would bill every installation's searches to this project,
/// which is unbounded and unrecoverable, so it lives in the credential store beside the model keys
/// and the settings entry says where to get one.
///
/// Snippets, never pages. What comes back is what the index extracted, which is a sentence or two
/// per result. Fetching the page behind a result and putting it in the context that writes files is
/// a larger decision than this one: it would mean arbitrary web content reaching the coder, and the
/// place it would attach is here, as a second tool beside this one, deliberately not built.
/// </remarks>
public sealed partial class WebSearchService : ObservableObject
{
    /// <summary>What the credential store files the key under.</summary>
    public const string ProviderId = "brave-search";

    /// <summary>Where a key comes from, shown in settings.</summary>
    public const string KeyUrl = "https://brave.com/search/api/";

    /// <summary>The name the model calls it by.</summary>
    public const string ToolName = "web_search";

    /// <summary>
    /// The owner recorded on the tool, which is this application rather than an extension.
    /// </summary>
    /// <remarks>
    /// Tools carry the id of whatever provides them so a call can be routed back. This one is
    /// provided here, so it carries a name no extension can have and the model node routes it here
    /// rather than to the extension host.
    /// </remarks>
    public const string OwnerId = "localnexus.websearch";

    private const string Endpoint = "https://api.search.brave.com/res/v1/web/search";

    /// <summary>How many results one search returns.</summary>
    /// <remarks>
    /// Enough to answer a question about an API and few enough not to spend a small model's whole
    /// context on one call.
    /// </remarks>
    public const int ResultCount = 5;

    private readonly ICredentialStore _credentials;
    private readonly HttpClient _http;

    /// <summary>
    /// True when this run may search.
    /// </summary>
    /// <remarks>
    /// Per send rather than per node, because most requests do not need search and whoever is
    /// typing knows which do. It applies to every Model node in the run, which the request box says
    /// rather than leaving it to be discovered.
    /// </remarks>
    [ObservableProperty]
    private bool _enabledForThisRun;

    public WebSearchService(ICredentialStore credentials, HttpClient http)
    {
        _credentials = credentials;
        _http = http;
    }

    /// <summary>
    /// Stores or clears the key.
    /// </summary>
    /// <remarks>
    /// Here rather than in the settings panel, because this already holds the store and a second
    /// route to the same secret is a second place to get it wrong. Write only: nothing reads a key
    /// back into the interface, because a box showing one is a key on somebody's screen.
    /// </remarks>
    public void SetKey(string? key)
    {
        _credentials.Set(ProviderId, key);

        OnPropertyChanged(nameof(HasKey));
        OnPropertyChanged(nameof(Availability));
        OnPropertyChanged(nameof(IsOfferedThisRun));
    }

    /// <summary>Whether search can be offered at all.</summary>
    public SearchAvailability Availability
        => _credentials.Has(ProviderId) ? SearchAvailability.Available : SearchAvailability.NoKey;

    /// <summary>True when a key exists, which is what shows the toggle.</summary>
    public bool HasKey => Availability == SearchAvailability.Available;

    /// <summary>True when this run should offer the tool.</summary>
    public bool IsOfferedThisRun => HasKey && EnabledForThisRun;

    /// <summary>The tool as the model is offered it.</summary>
    public static ToolDefinition Tool { get; } = new(
        ToolName,
        "Search the web for current information. Use it when the answer depends on something that "
        + "may have changed since you were trained: a library's current API, whether a method is "
        + "deprecated, a version number, or an error message you do not recognise. Returns a few "
        + "results with a title, a link and a short extract each. It does not fetch pages.",
        new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["query"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "What to search for, as you would type it into a search box."
                }
            },
            ["required"] = new JsonArray("query")
        },
        OwnerId);

    /// <summary>
    /// Runs one search and returns what came back.
    /// </summary>
    /// <exception cref="SearchException">There is no key, or the search could not be run.</exception>
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new SearchException("A search needs something to search for.");
        }

        if (_credentials.Get(ProviderId) is not { Length: > 0 } key)
        {
            throw new SearchException(
                $"There is no search key. Add one in Settings under API keys, Search providers; you can get one at {KeyUrl}.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{Endpoint}?q={Uri.EscapeDataString(query.Trim())}&count={ResultCount}");

        request.Headers.Add("X-Subscription-Token", key);
        request.Headers.Add("Accept", "application/json");

        HttpResponseMessage response;

        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new SearchException($"The search could not be sent: {ex.Message}", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new SearchException(Explain(response.StatusCode, body));
            }

            return Read(body);
        }
    }

    /// <summary>What went wrong, in terms of what to do about it.</summary>
    private static string Explain(System.Net.HttpStatusCode status, string body) => status switch
    {
        System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden =>
            "The search key was refused. Check it in Settings under Models.",

        System.Net.HttpStatusCode.TooManyRequests =>
            "The search plan's rate limit was reached. The free tier allows one request a second.",

        _ => $"The search failed with {(int)status}: {Summarise(body)}"
    };

    private static IReadOnlyList<SearchResult> Read(string body)
    {
        try
        {
            if (JsonNode.Parse(body) is not JsonObject payload
                || payload["web"] is not JsonObject web
                || web["results"] is not JsonArray results)
            {
                return Array.Empty<SearchResult>();
            }

            return results
                .OfType<JsonObject>()
                .Select(r => new SearchResult(
                    Text(r, "title") ?? "untitled",
                    Text(r, "url") ?? string.Empty,
                    Text(r, "description") ?? string.Empty))
                .ToList();
        }
        catch (JsonException ex)
        {
            throw new SearchException($"The search answered with something that could not be read: {ex.Message}", ex);
        }
    }

    private static string? Text(JsonObject payload, string name)
        => payload[name]?.GetValueKind() == JsonValueKind.String ? payload[name]!.GetValue<string>() : null;

    private static string Summarise(string value)
    {
        var flat = value.ReplaceLineEndings(" ").Trim();
        return flat.Length <= 200 ? flat : flat[..200] + "...";
    }

    /// <summary>Results as the model reads them, which is text.</summary>
    public static string Format(string query, IReadOnlyList<SearchResult> results)
    {
        if (results.Count == 0)
        {
            return $"No results for '{query}'.";
        }

        var text = new System.Text.StringBuilder();

        text.AppendLine($"{results.Count} result(s) for '{query}':");

        foreach (var result in results)
        {
            text.AppendLine();
            text.AppendLine(result.Title);
            text.AppendLine(result.Url);
            text.AppendLine(result.Snippet);
        }

        return text.ToString().TrimEnd();
    }
}

/// <summary>A search that could not be run, worded for a person.</summary>
public sealed class SearchException : Exception
{
    public SearchException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
