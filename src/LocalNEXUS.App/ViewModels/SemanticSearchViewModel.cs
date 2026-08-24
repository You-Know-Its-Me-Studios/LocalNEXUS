using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.History;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.Services.Search;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// Turning semantic search on, and indexing what is already there.
/// </summary>
/// <remarks>
/// The opt in, and the only place the feature is arranged. Off is the shipped state and costs
/// nothing: no model on disk, no server started, and a keyword search that behaves exactly as it
/// always did.
///
/// Choosing a model is choosing a file, the same way every other model is chosen, so an embedding
/// model can come from anywhere a model can. What the panel adds is a recommendation with its
/// size, because "pick an embedding model" is not a useful instruction to somebody who has never
/// needed one.
/// </remarks>
public sealed partial class SemanticSearchViewModel : ObservableObject
{
    /// <summary>What is suggested to somebody who has no embedding model.</summary>
    public const string RecommendedRepository = "CompendiumLabs/bge-small-en-v1.5-gguf";

    /// <summary>The file within it, and roughly what it weighs.</summary>
    public const string RecommendedFile = "bge-small-en-v1.5-q8_0.gguf";

    private readonly AppConfig _config;
    private readonly RunHistoryStore _history;
    private readonly LlamaServerManager _servers;
    private readonly IDialogService _dialogs;
    private readonly HttpClient _http;

    private CancellationTokenSource? _working;

    /// <summary>Where the embedding model is, or empty when the feature is off.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOn))]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(ModelName))]
    private string _modelPath = string.Empty;

    /// <summary>How many runs have no vector yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private int _outstanding;

    /// <summary>How many runs are indexed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private int _indexed;

    /// <summary>True while a backfill is running.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isIndexing;

    /// <summary>What the last thing that happened was.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    private string _note = string.Empty;

    public SemanticSearchViewModel(
        AppConfig config,
        RunHistoryStore history,
        LlamaServerManager servers,
        IDialogService dialogs,
        HttpClient http)
    {
        _config = config;
        _history = history;
        _servers = servers;
        _dialogs = dialogs;
        _http = http;

        _modelPath = config.EmbeddingModelPath ?? string.Empty;
    }

    /// <summary>True when an embedding model has been chosen.</summary>
    public bool IsOn => ModelPath.Length > 0;

    /// <summary>Nothing is running right now.</summary>
    public bool IsIdle => !IsIndexing;

    /// <summary>True when there is something to say.</summary>
    public bool HasNote => Note.Length > 0;

    /// <summary>The model, by name rather than by path.</summary>
    public string ModelName => ModelPath.Length > 0
        ? Path.GetFileNameWithoutExtension(ModelPath)
        : string.Empty;

    /// <summary>What is set up and what is left to do.</summary>
    public string StateText
    {
        get
        {
            if (!IsOn)
            {
                return "Off. History is searched by keyword, which finds the words that were "
                    + "actually written and needs no model.";
            }

            if (Outstanding == 0)
            {
                return $"On, using {ModelName}. All {Indexed} recorded run(s) are indexed.";
            }

            return $"On, using {ModelName}. {Indexed} run(s) indexed, {Outstanding} still to do. "
                + "Runs recorded from now on are indexed as they finish.";
        }
    }

    /// <summary>What to suggest to somebody who has no embedding model at all.</summary>
    public string Recommendation =>
        $"If you have no embedding model, {RecommendedFile} is a good small one at about 35 MB: "
        + $"search for {RecommendedRepository} under Get a model, on the bar down the left side "
        + "of the window.";

    /// <summary>Picks the embedding model, which is what turns the feature on.</summary>
    [RelayCommand]
    private async Task ChooseAsync()
    {
        var picked = _dialogs.PickOpenFile(
            "Choose an embedding model",
            "GGUF models|*.gguf|All files|*.*",
            AppPaths.ModelsEmbeddings);

        if (string.IsNullOrWhiteSpace(picked))
        {
            return;
        }

        ModelPath = picked;
        _config.EmbeddingModelPath = picked;
        _config.Save();

        Note = "Chosen. Nothing is indexed yet: runs from now on are indexed as they finish, and "
            + "Index the history covers what is already recorded.";

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Turns it off, leaving keyword search exactly as it was.</summary>
    [RelayCommand]
    private void TurnOff()
    {
        _working?.Cancel();

        ModelPath = string.Empty;
        _config.EmbeddingModelPath = null;
        _config.Save();

        Note = "Off. Searches are by keyword again. The vectors are kept, so turning it back on "
            + "with the same model does not mean indexing everything twice.";
    }

    /// <summary>Throws away every vector, for somebody who wants the space back.</summary>
    [RelayCommand]
    private async Task ForgetAsync()
    {
        _history.ClearVectors();

        Note = "Every vector was deleted. Indexing again rebuilds them.";

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Counts what is indexed and what is not.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (Build() is not { } indexer)
        {
            Indexed = 0;
            Outstanding = 0;
            return;
        }

        Indexed = await _history.VectorCountAsync(indexer.ModelId, CancellationToken.None).ConfigureAwait(true);
        Outstanding = await indexer.Indexer.OutstandingAsync(CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>The one time pass over everything already recorded.</summary>
    [RelayCommand]
    private async Task BackfillAsync()
    {
        if (Build() is not { } built || IsIndexing)
        {
            return;
        }

        _working?.Cancel();
        _working = new CancellationTokenSource();

        IsIndexing = true;
        Note = "Indexing. The first run also starts the embedding model, which takes a moment.";

        try
        {
            var result = await built.Indexer
                .BackfillAsync(
                    new Progress<(int Done, int Total)>(p => Note = $"Indexed {p.Done} of {p.Total}."),
                    _working.Token)
                .ConfigureAwait(true);

            Note = result.Indexed == 0
                ? $"Nothing was indexed. {result.Failed} run(s) could not be embedded, which "
                  + "usually means the file chosen is not an embedding model."
                : $"Indexed {result.Indexed} run(s) in {result.Elapsed.TotalSeconds:0.0} seconds, "
                  + $"about {result.Each.TotalMilliseconds:0} ms each."
                  + (result.Failed > 0 ? $" {result.Failed} could not be embedded." : string.Empty);
        }
        catch (OperationCanceledException)
        {
            Note = "Stopped. What was indexed is kept, and indexing again carries on from there.";
        }
        finally
        {
            IsIndexing = false;
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Stops a backfill, keeping what it has done.</summary>
    [RelayCommand]
    private void Stop() => _working?.Cancel();

    /// <summary>
    /// The indexer and the search for the chosen model, or null when the feature is off.
    /// </summary>
    /// <remarks>
    /// Built per use rather than held, because the model can change and a stale embedder would go
    /// on writing vectors labelled with a model nobody is using any more.
    /// </remarks>
    public (HistoryIndexer Indexer, SemanticHistorySearch Search, string ModelId)? Build()
    {
        if (!IsOn)
        {
            return null;
        }

        var embedder = new LocalEmbedder(_servers, _http, ModelPath);

        return (new HistoryIndexer(_history, embedder),
                new SemanticHistorySearch(_history, embedder),
                embedder.ModelId);
    }
}
