using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LocalNEXUS.App.Services.Inference;

namespace LocalNEXUS.App.Services.Models;

/// <summary>A repository of models somebody could download from.</summary>
/// <param name="Id">The owner and name, as Hugging Face writes it.</param>
/// <param name="Downloads">How many times it has been downloaded, for ordering.</param>
/// <param name="Likes">How many people marked it.</param>
public sealed record ModelRepository(
    string Id,
    long Downloads,
    long Likes,
    DateTimeOffset? LastModified = null,
    IReadOnlyList<string>? Tags = null,
    string? PipelineTag = null)
{
    /// <summary>The owner, which is often the only signal of whose build this is.</summary>
    public string Owner => Id.Contains('/', StringComparison.Ordinal)
        ? Id[..Id.IndexOf('/', StringComparison.Ordinal)]
        : string.Empty;

    /// <summary>The repository name without its owner.</summary>
    public string Name => Id.Contains('/', StringComparison.Ordinal)
        ? Id[(Id.IndexOf('/', StringComparison.Ordinal) + 1)..]
        : Id;

    /// <summary>Downloads and likes, as one line, because they are read together.</summary>
    public string CountsText => $"{Downloads:N0} downloads, {Likes:N0} likes";

    /// <summary>Where somebody would go to read about it.</summary>
    public string PageUrl => $"https://huggingface.co/{Id}";

    /// <summary>
    /// When it last changed, as a phrase rather than a date.
    /// </summary>
    /// <remarks>
    /// A model card is not a news article and the exact minute is never the question. What is
    /// being asked is whether this is current, which an age answers and a timestamp does not.
    /// </remarks>
    public string UpdatedText
    {
        get
        {
            if (LastModified is not { } when)
            {
                return "not reported";
            }

            var days = (DateTimeOffset.Now - when).TotalDays;

            return days switch
            {
                < 1 => "updated today",
                < 2 => "updated yesterday",
                < 31 => $"updated {days:0} days ago",
                < 365 => $"updated {days / 30:0} months ago",
                _ => $"updated {days / 365:0.0} years ago"
            };
        }
    }
}

/// <summary>One downloadable file inside a repository.</summary>
/// <param name="Repository">Which repository it belongs to.</param>
/// <param name="Path">The file name inside the repository.</param>
/// <param name="SizeBytes">How large it is, which is what a fit estimate is built from.</param>
/// <param name="Sha256">The content hash, when the repository provides one.</param>
public sealed record ModelFileOption(string Repository, string Path, long SizeBytes, string? Sha256)
{
    /// <summary>The quantization, read from the file name the same way the catalogue reads it.</summary>
    public string Quantisation => QuantisationLabel.Read(Path);

    /// <summary>Size in gigabytes, which is the unit every decision here is made in.</summary>
    public double SizeGb => SizeBytes / 1024d / 1024d / 1024d;

    /// <summary>Where the bytes come from.</summary>
    public string DownloadUrl => $"https://huggingface.co/{Repository}/resolve/main/{Uri.EscapeDataString(Path)}?download=true";

    /// <summary>
    /// True when this is one piece of a model split across several files.
    /// </summary>
    /// <remarks>
    /// A large model is often published as numbered parts. Downloading one of them alone produces
    /// a file that is not a model, so they are named as what they are rather than offered as if
    /// each were a choice.
    /// </remarks>
    public bool IsOnePartOfSeveral =>
        System.Text.RegularExpressions.Regex.IsMatch(
            Path, @"-\d{5}-of-\d{5}\.gguf$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>True when the repository published a hash that a download can be checked against.</summary>
    public bool CanBeVerified => !string.IsNullOrWhiteSpace(Sha256);
}

/// <summary>A repository that will not serve its files without an account.</summary>
public sealed class GatedRepositoryException : Exception
{
    public GatedRepositoryException(string repository)
        : base($"{repository} is gated. Its owner requires you to accept terms on the Hugging Face "
               + "website before it can be downloaded. This application has no account and does not "
               + "work around that.")
        => Repository = repository;

    /// <summary>Which repository refused.</summary>
    public string Repository { get; }

    /// <summary>Where somebody would go to accept the terms.</summary>
    public string PageUrl => $"https://huggingface.co/{Repository}";
}

/// <summary>Anything else that went wrong reaching Hugging Face.</summary>
public sealed class CatalogueUnavailableException : Exception
{
    public CatalogueUnavailableException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Searches Hugging Face for models this application can serve.
/// </summary>
/// <remarks>
/// The public read API, with no token and no account. Everything here is a plain GET against
/// endpoints that answer for anybody, which is a deliberate limit rather than a gap: a gated
/// repository is reported as gated with a link to it, and nothing here tries to get around one.
///
/// What it uses, verified against the live service rather than taken from documentation:
///
/// <list type="bullet">
/// <item>Search is <c>/api/models</c> with <c>search</c>, <c>filter=gguf</c> and <c>limit</c>,
/// and answers with the repository id, download and like counts, and tags.</item>
/// <item>Files are <c>/api/models/{repo}/tree/main?expand=true</c>, which answers per file with
/// its path, its size in bytes, and for a large file an <c>lfs.oid</c> that is the content's
/// SHA-256. That hash is the only verifiable one: the plain <c>oid</c> is a git object id, which
/// is a hash of the file plus a header and is not what a downloaded file hashes to.</item>
/// <item>The bytes come from <c>/{repo}/resolve/main/{file}</c>, which redirects to a content
/// server that answers a byte range with 206 and advertises <c>accept-ranges: bytes</c>. That is
/// what makes resuming a partial download possible.</item>
/// </list>
///
/// Only GGUF is offered, because that is what the bundled runtime serves without a Python
/// environment being built first. Safetensors would attach here, as a second format filter and a
/// folder of files rather than one file, and is deliberately not built.
/// </remarks>
public sealed class HuggingFaceCatalogue
{
    /// <summary>How many repositories one search returns.</summary>
    private const int SearchLimit = 25;

    /// <summary>How much of a model card is worth putting in a side panel.</summary>
    private const int CardLimit = 24000;

    /// <summary>
    /// What is said when every attempt was interrupted.
    /// </summary>
    /// <remarks>
    /// Three facts, deliberately. That it already tried more than once, so trying again by hand is
    /// not the obvious next move. That the interruption is on the way to Hugging Face rather than
    /// in here, because the first version of this sentence said check the connection and sent
    /// people to look at a connection that was working. And that there is a log, because the
    /// detail exists now and is worth nothing if nobody knows where it is.
    /// </remarks>
    private const string Unreachable =
        "Hugging Face could not be reached after 3 attempts. The connection was interrupted, "
        + "which is usually the network between this machine and Hugging Face rather than "
        + "anything here. The detail is in hub.log, beside the other logs.";

    private readonly HttpClient _http;

    public HuggingFaceCatalogue(HttpClient http)
    {
        _http = http;

        // Asked for by the service, and it is polite to say who is calling.
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("LocalNEXUS");
        }
    }

    /// <summary>
    /// Finds repositories holding GGUF models whose name matches what was typed.
    /// </summary>
    /// <exception cref="CatalogueUnavailableException">Hugging Face could not be reached or did not answer usefully.</exception>
    public async Task<IReadOnlyList<ModelRepository>> SearchAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<ModelRepository>();
        }

        var url = "https://huggingface.co/api/models"
            + $"?search={Uri.EscapeDataString(query.Trim())}"
            + $"&filter=gguf&limit={SearchLimit}&sort=downloads&direction=-1&full=true";

        var found = await ReadAsync<List<SearchRow>>(url, ct).ConfigureAwait(false);

        return found
            .Where(row => !string.IsNullOrWhiteSpace(row.Id) && !row.Private)
            .Select(Describe)
            .ToList();
    }

    /// <summary>
    /// Lists the GGUF files in a repository, largest choice first.
    /// </summary>
    /// <exception cref="GatedRepositoryException">The repository requires an account.</exception>
    /// <exception cref="CatalogueUnavailableException">It could not be read.</exception>
    public async Task<IReadOnlyList<ModelFileOption>> FilesAsync(string repository, CancellationToken ct)
    {
        var url = $"https://huggingface.co/api/models/{repository}/tree/main?expand=true&recursive=true";

        var entries = await ReadAsync<List<TreeRow>>(url, ct, repository).ConfigureAwait(false);

        return entries
            .Where(entry => entry.Path is { Length: > 0 }
                && entry.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
                && entry.Size > 0)
            .Select(entry => new ModelFileOption(repository, entry.Path!, entry.Size, entry.Lfs?.Oid))
            .OrderBy(file => file.SizeBytes)
            .ToList();
    }

    /// <summary>
    /// The repository's model card, or null when it does not have a readable one.
    /// </summary>
    /// <remarks>
    /// Null rather than an exception, and null rather than an apology dressed as content. A card
    /// is the author describing their own work and most repositories have one, but a repository
    /// without one is perfectly usable and the file list is the part that matters. So a missing
    /// card is a sentence above the files rather than an error over them.
    ///
    /// Capped, because a model card can be enormous and nobody reads forty thousand words in a
    /// side panel. What is cut is said rather than trimmed silently.
    /// </remarks>
    public async Task<string?> CardAsync(string repository, CancellationToken ct)
    {
        var url = $"https://huggingface.co/{repository}/resolve/main/README.md";

        try
        {
            using var response = await HubRetry
                .SendAsync(token => _http.GetAsync(url, token), ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            return text.Length <= CardLimit
                ? text
                : text[..CardLimit] + Environment.NewLine + Environment.NewLine
                  + "The rest of this card is longer than is worth showing here. Open the page to read it.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            HubTransport.LogFailure($"Model card could not be read: {url}", ex);
            return null;
        }
    }

    private static ModelRepository Describe(SearchRow row)
        => new(row.Id!, row.Downloads, row.Likes, row.LastModified, row.Tags, row.PipelineTag);

    private async Task<T> ReadAsync<T>(string url, CancellationToken ct, string? repository = null)
        where T : new()
    {
        HttpResponseMessage response;

        try
        {
            response = await HubRetry
                .SendAsync(token => _http.GetAsync(url, token), ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // The detail goes to the log rather than to the person, who cannot act on a socket
            // error. It used to go nowhere at all, which is why diagnosing this needed curl.
            HubTransport.LogFailure($"Catalogue request failed after {HubRetry.Attempts} attempts: {url}", ex);

            throw new CatalogueUnavailableException(Unreachable, ex);
        }

        using (response)
        {
            if (repository is not null
                && response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new GatedRepositoryException(repository);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new CatalogueUnavailableException(
                    repository is null
                        ? "Hugging Face did not recognise that request."
                        : $"{repository} was not found. It may have been renamed or removed.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new CatalogueUnavailableException(
                    $"Hugging Face answered {(int)response.StatusCode}. Try again shortly.");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(ct).ConfigureAwait(false) ?? new T();
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or HttpRequestException)
            {
                throw new CatalogueUnavailableException(
                    "Hugging Face answered with something this could not read.", ex);
            }
        }
    }

    private sealed class SearchRow
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("downloads")]
        public long Downloads { get; set; }

        [JsonPropertyName("likes")]
        public long Likes { get; set; }

        [JsonPropertyName("private")]
        public bool Private { get; set; }

        [JsonPropertyName("lastModified")]
        public DateTimeOffset? LastModified { get; set; }

        [JsonPropertyName("trendingScore")]
        public double TrendingScore { get; set; }

        [JsonPropertyName("tags")]
        public List<string>? Tags { get; set; }

        [JsonPropertyName("pipeline_tag")]
        public string? PipelineTag { get; set; }
    }

    private sealed class TreeRow
    {
        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("lfs")]
        public LfsRow? Lfs { get; set; }
    }

    private sealed class LfsRow
    {
        /// <summary>The content's SHA-256, which is what a finished download is checked against.</summary>
        [JsonPropertyName("oid")]
        public string? Oid { get; set; }
    }
}
