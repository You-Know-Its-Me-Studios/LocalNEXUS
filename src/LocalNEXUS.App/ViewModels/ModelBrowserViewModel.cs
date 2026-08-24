using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.Models;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.Services.Python;

namespace LocalNEXUS.App.ViewModels;

/// <summary>One downloadable file, and everything worth knowing before choosing it.</summary>
public sealed partial class ModelFileViewModel : ObservableObject
{
    private readonly double? _cardGb;

    /// <summary>How far a download of this file has got.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private DownloadProgress _progress;

    /// <summary>True while this file is being fetched.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(ActionLabel))]
    private bool _isDownloading;

    /// <summary>What happened, once something has.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    private string _note = string.Empty;

    public ModelFileViewModel(ModelFileOption file, double? cardGb, int contextTokens)
    {
        File = file;
        _cardGb = cardGb;
        ContextTokens = contextTokens;
    }

    /// <summary>What this is on Hugging Face.</summary>
    public ModelFileOption File { get; }

    /// <summary>The context the fit estimate assumed.</summary>
    public int ContextTokens { get; }

    /// <summary>The quantization, which is the thing being chosen between.</summary>
    public string Quantisation => File.Quantisation;

    /// <summary>Size on disk, as it will be.</summary>
    public string SizeLabel => $"{File.SizeGb:0.0} GB";

    /// <summary>Whether it will run here, in words, including what the estimate assumed.</summary>
    public string FitText => ModelFit.Describe(File.SizeGb, ContextTokens, _cardGb);

    /// <summary>True when it is not expected to run well, so the row is marked.</summary>
    public bool WillNotFit =>
        ModelFit.Verdict(File.SizeGb, ContextTokens, _cardGb) is FitVerdict.Spills or FitVerdict.TooLarge;

    /// <summary>Whether the repository published something to check the download against.</summary>
    public string VerificationText => File.CanBeVerified
        ? "The repository publishes a hash, so the download is checked."
        : "The repository publishes no hash, so the download cannot be checked.";

    /// <summary>True when this file is one piece of a model split across several.</summary>
    public bool IsOnePartOfSeveral => File.IsOnePartOfSeveral;

    /// <summary>Nothing is happening to this file right now.</summary>
    public bool IsIdle => !IsDownloading;

    /// <summary>True when there is something to say about what happened.</summary>
    public bool HasNote => Note.Length > 0;

    /// <summary>What the button says, which changes once something is part way through.</summary>
    public string ActionLabel => IsDownloading ? "Downloading" : "Download";

    /// <summary>Progress as a person reads it: how far, how fast, how long left.</summary>
    public string ProgressText
    {
        get
        {
            if (Progress.TotalBytes <= 0)
            {
                return string.Empty;
            }

            var done = Progress.BytesSoFar / 1024d / 1024d / 1024d;
            var total = Progress.TotalBytes / 1024d / 1024d / 1024d;
            var rate = Progress.BytesPerSecond / 1024d / 1024d;

            var line = $"{done:0.00} of {total:0.00} GB";

            if (rate > 0.01d)
            {
                line += $", {rate:0.0} MB/s";
            }

            if (Progress.Remaining is { } left && left.TotalSeconds > 1)
            {
                line += left.TotalMinutes >= 1
                    ? $", about {left.TotalMinutes:0} min left"
                    : $", about {left.TotalSeconds:0} s left";
            }

            return line;
        }
    }
}

/// <summary>
/// Finding a model on Hugging Face and getting it onto this machine.
/// </summary>
/// <remarks>
/// The first thing anybody has to do is get a model, and until now that happened outside the
/// application: find a repository in a browser, work out which of fifteen quantizations the
/// machine can hold, and put the file in the right folder. The middle step is the one people get
/// wrong, and they find out after the download rather than before it.
///
/// So the fit estimate leads. Every file says what it would need and whether that fits, with the
/// context it assumed named, because the same file fits at one context and not another.
///
/// Nothing here reaches into how models are served. The file lands in the folder the catalogue
/// already watches and the catalogue is asked to look again, which is the whole of the
/// integration: no resolver, no runtime and no catalogue behaviour changes because a file arrived
/// by download rather than by being copied in.
/// </remarks>
public sealed partial class ModelBrowserViewModel : ObservableObject
{
    private readonly HuggingFaceCatalogue _catalogue;
    private readonly ModelDownloader _downloader;
    private readonly ModelCatalogViewModel _installed;
    private readonly Services.Dialogs.IDialogService _dialogs;

    private CancellationTokenSource? _downloading;

    /// <summary>What was typed into the search box.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _query = string.Empty;

    /// <summary>True while a search is running.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private bool _isSearching;

    /// <summary>The repository whose files are shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ModelRepository? _selectedRepository;

    /// <summary>What went wrong, or what is worth saying about the last thing that happened.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = string.Empty;

    /// <summary>A link worth offering alongside the status, for a gated repository.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLink))]
    private string _link = string.Empty;

    public ModelBrowserViewModel(
        HttpClient http,
        ModelCatalogViewModel installed,
        Services.Dialogs.IDialogService dialogs)
    {
        _catalogue = new HuggingFaceCatalogue(http);
        _downloader = new ModelDownloader(http);
        _installed = installed;
        _dialogs = dialogs;
    }

    /// <summary>What the search found.</summary>
    public ObservableCollection<ModelRepository> Results { get; } = new();

    /// <summary>The files inside the selected repository.</summary>
    public ObservableCollection<ModelFileViewModel> Files { get; } = new();

    /// <summary>True when a repository is selected and its files are worth showing.</summary>
    public bool HasSelection => SelectedRepository is not null;

    /// <summary>True when there is something to say.</summary>
    public bool HasStatus => Status.Length > 0;

    /// <summary>True when a link is worth offering.</summary>
    public bool HasLink => Link.Length > 0;

    /// <summary>What is known about the card the estimates are made against.</summary>
    public string CardSummary => AcceleratorProbe.DetectMemory() is { } card
        ? $"Estimates are against {card.GpuName}, {card.TotalGb:0.0} GB, at {ContextTokens / 1024}k context."
        : "No graphics card was detected, so nothing is claimed about what will fit.";

    /// <summary>
    /// The context the fit estimates assume.
    /// </summary>
    /// <remarks>
    /// The application default rather than a node's setting, because this is a decision about
    /// which file to keep on disk rather than about one graph. It is named in the summary so the
    /// assumption is visible rather than buried.
    /// </remarks>
    public static int ContextTokens => LlamaLaunchOptions.DefaultContextSize;

    private bool CanSearch => !IsSearching && !string.IsNullOrWhiteSpace(Query);

    /// <summary>Asks Hugging Face what it has.</summary>
    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        IsSearching = true;
        Status = string.Empty;
        Link = string.Empty;
        Results.Clear();
        Files.Clear();
        SelectedRepository = null;

        try
        {
            var found = await _catalogue.SearchAsync(Query, CancellationToken.None).ConfigureAwait(true);

            foreach (var repository in found)
            {
                Results.Add(repository);
            }

            Status = found.Count == 0
                ? $"Nothing on Hugging Face matched {Query} among models published as GGUF."
                : string.Empty;
        }
        catch (CatalogueUnavailableException ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>Lists what a repository holds, with a verdict per file.</summary>
    [RelayCommand]
    private async Task OpenAsync(ModelRepository? repository)
    {
        if (repository is null)
        {
            return;
        }

        SelectedRepository = repository;
        Files.Clear();
        Status = string.Empty;
        Link = string.Empty;

        var card = AcceleratorProbe.DetectMemory()?.TotalGb;

        try
        {
            var files = await _catalogue.FilesAsync(repository.Id, CancellationToken.None).ConfigureAwait(true);

            foreach (var file in files)
            {
                Files.Add(new ModelFileViewModel(file, card, ContextTokens));
            }

            if (files.Count == 0)
            {
                Status = $"{repository.Id} is tagged GGUF and has no GGUF files in it.";
            }
        }
        catch (GatedRepositoryException ex)
        {
            Status = ex.Message;
            Link = ex.PageUrl;
        }
        catch (CatalogueUnavailableException ex)
        {
            Status = ex.Message;
        }
    }

    /// <summary>Opens the page for whatever the status is about.</summary>
    [RelayCommand]
    private void OpenLink()
    {
        if (Link.Length > 0)
        {
            _dialogs.OpenUrl(Link);
        }
    }

    /// <summary>Fetches one file into the models folder.</summary>
    [RelayCommand]
    private async Task DownloadAsync(ModelFileViewModel? row)
    {
        if (row is null || row.IsDownloading)
        {
            return;
        }

        var destination = Path.Combine(AppPaths.ModelsGguf, Path.GetFileName(row.File.Path));

        _downloading?.Cancel();
        _downloading = new CancellationTokenSource();

        row.IsDownloading = true;
        row.Note = string.Empty;
        Status = string.Empty;
        Link = string.Empty;

        try
        {
            var outcome = await _downloader
                .DownloadAsync(
                    row.File,
                    destination,
                    new Progress<DownloadProgress>(p => row.Progress = p),
                    _downloading.Token)
                .ConfigureAwait(true);

            row.Note = outcome == DownloadOutcome.Verified
                ? "Downloaded and checked against the published hash."
                : "Downloaded. The repository published no hash, so it could not be checked.";

            // The one line of integration. The file is in the folder the catalogue watches, so it
            // is asked to look again through the same command the Rescan button uses, and the
            // model appears without a restart.
            _installed.RefreshCommand.Execute(null);
        }
        catch (OperationCanceledException)
        {
            row.Note = "Stopped. What arrived is kept, so starting again resumes from there.";
        }
        catch (GatedRepositoryException ex)
        {
            Status = ex.Message;
            Link = ex.PageUrl;
        }
        catch (DownloadFailedException ex)
        {
            row.Note = ex.Message;
        }
        finally
        {
            row.IsDownloading = false;
        }
    }

    /// <summary>Stops the download in progress, keeping what arrived so it can resume.</summary>
    [RelayCommand]
    private void Stop() => _downloading?.Cancel();

    /// <summary>Stops and throws away the partial file, so nothing large is left behind.</summary>
    [RelayCommand]
    private void Discard(ModelFileViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        _downloading?.Cancel();

        ModelDownloader.DiscardPartial(Path.Combine(AppPaths.ModelsGguf, Path.GetFileName(row.File.Path)));

        row.Progress = default;
        row.Note = "Discarded, and the partly downloaded file was deleted.";
    }
}
