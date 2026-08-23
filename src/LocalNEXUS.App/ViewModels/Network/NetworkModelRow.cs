using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Services.Distributed;

namespace LocalNEXUS.App.ViewModels.Network;

/// <summary>
/// One row of the model table: a model the mesh knows about, in the columns the table sorts and
/// filters on.
/// </summary>
/// <remarks>
/// A wrapper rather than properties on the mesh type, because <see cref="NetworkServedModel"/> is
/// what the engine reports and has no business carrying a sort key or a column string.
///
/// Four columns say "not reported" rather than a number, and that is deliberate. The engine does
/// not tell us a model's size on disk or its throughput, and inventing either would make the
/// table lie in exactly the place a person would trust it. Last verified is real: it is the most
/// recent time the mesh saw any source holding a piece of this model.
/// </remarks>
public sealed partial class NetworkModelRow : ObservableObject, INetworkRow, IDisposable
{
    /// <summary>What a GGUF quantization label looks like, which is how format is inferred.</summary>
    private static readonly Regex GgufQuantisation = new(
        @"^(Q\d|IQ\d|F16|F32|BF16|MOSTLY_)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly Func<bool> _meshIsPublic;

    private bool _disposed;

    public NetworkModelRow(NetworkServedModel model, Func<bool> meshIsPublic)
    {
        Model = model;
        _meshIsPublic = meshIsPublic;

        model.PropertyChanged += OnModelChanged;
    }

    /// <summary>The model as the engine reports it. Updated in place, so this row is always current.</summary>
    public NetworkServedModel Model { get; }

    /// <summary>The readable tail of the model id, which is what the row leads with.</summary>
    public string Name => Model.Name;

    /// <summary>The full identifier, which the name is a readable tail of.</summary>
    public string ModelId => Model.ModelId;

    /// <summary>Colour of the weakest link in the chain.</summary>
    public SectionCoverage Strength => Model.Strength;

    /// <summary>False. This is something the mesh reports, not something from the directory.</summary>
    public bool IsDiscovered => false;

    /// <summary>The model itself, which is what the inspector shows for this row.</summary>
    public object InspectorTarget => Model;

    /// <summary>Quantization label as the engine reports it.</summary>
    public string Quantisation => Model.Quantization;

    /// <summary>Whether the mesh can serve this model right now.</summary>
    public ModelAvailability Availability => Model.Availability;

    /// <summary>The one word status badge.</summary>
    public string StatusText => Model.StatusText;

    /// <summary>The sections of the pipeline, which is what the coverage bar draws a segment for.</summary>
    public IReadOnlyList<SourceAssignment> Sections => Model.Plan?.Assignments ?? Array.Empty<SourceAssignment>();

    /// <summary>True once the mesh has reported how this model is assembled.</summary>
    public bool HasSections => Sections.Count > 0;

    /// <summary>How many distinct sources hold pieces of this model.</summary>
    public int SourceCount => Model.PeerCount;

    /// <summary>Usable sources standing spare behind the weakest section.</summary>
    public int SpareCount => Model.WeakestSpare;

    /// <summary>Context window the model was loaded with, or a dash when it was not reported.</summary>
    public string ContextText => Model.ContextLength > 0
        ? Model.ContextLength.ToString("N0", CultureInfo.CurrentCulture)
        : Unreported;

    /// <summary>Parameter count as the engine words it.</summary>
    public string ParametersText => string.IsNullOrWhiteSpace(Model.ParameterSize) ? Unreported : Model.ParameterSize;

    /// <summary>Layer count, which is what gets divided into sections.</summary>
    public string LayersText => Model.LayerCount > 0
        ? Model.LayerCount.ToString(CultureInfo.CurrentCulture)
        : Unreported;

    /// <summary>Size on disk. The mesh does not report it, so this says so rather than guessing.</summary>
    public string SizeText => Unreported;

    /// <summary>Tokens per second. Not reported either, and not inferable from anything that is.</summary>
    public string ThroughputText => Unreported;

    /// <summary>
    /// When the mesh last saw a source holding part of this model, which is the closest thing to
    /// a verification the engine actually performs.
    /// </summary>
    public string LastVerifiedText
    {
        get
        {
            var seen = Sections
                .Select(a => a.Source?.LastSeenUtc)
                .OfType<DateTimeOffset>()
                .DefaultIfEmpty()
                .Max();

            if (seen == default)
            {
                return Unreported;
            }

            var ago = DateTimeOffset.UtcNow - seen;

            return ago switch
            {
                { TotalSeconds: < 45 } => "now",
                { TotalMinutes: < 60 } => $"{(int)ago.TotalMinutes}m ago",
                { TotalHours: < 24 } => $"{(int)ago.TotalHours}h ago",
                _ => $"{(int)ago.TotalDays}d ago"
            };
        }
    }

    /// <summary>
    /// Who can see this model. A private mesh makes everything in it invite only, which is the
    /// default posture and the only one that keeps the engine off public relays.
    /// </summary>
    public ModelSharing Sharing => _meshIsPublic() ? ModelSharing.Public : ModelSharing.InviteOnly;

    /// <summary>True when this row is listed but reachable only with an invite.</summary>
    public bool IsInviteOnly => Sharing == ModelSharing.InviteOnly;

    /// <summary>
    /// True when the quantization label is one a GGUF file carries, which is as close to a format
    /// as the mesh reports. Anything else is left as unknown rather than assumed.
    /// </summary>
    public bool LooksLikeGguf => !string.IsNullOrWhiteSpace(Model.Quantization)
                                 && GgufQuantisation.IsMatch(Model.Quantization);

    /// <summary>What a column shows when the engine does not report it.</summary>
    private const string Unreported = "not reported";

    /// <summary>Sort key for one column, so ordering is done on values rather than on the strings.</summary>
    public IComparable? SortKey(ModelColumn column) => column switch
    {
        ModelColumn.Name => Name,
        ModelColumn.Coverage => (int)Availability,
        ModelColumn.Sources => SourceCount,
        ModelColumn.Spare => SpareCount,
        ModelColumn.Status => StatusText,
        ModelColumn.Context => Model.ContextLength,
        ModelColumn.Parameters => Model.ParameterSize,
        ModelColumn.Layers => Model.LayerCount,
        _ => Name
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Model.PropertyChanged -= OnModelChanged;
    }

    /// <summary>Re-reads what depends on the mesh rather than on the model.</summary>
    public void RefreshMeshState()
    {
        OnPropertyChanged(nameof(Sharing));
        OnPropertyChanged(nameof(IsInviteOnly));
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The engine updates a model in place, so a row republishes everything rather than
        // mapping each of the engine's property names onto the handful of columns it feeds.
        OnPropertyChanged(string.Empty);
    }
}
