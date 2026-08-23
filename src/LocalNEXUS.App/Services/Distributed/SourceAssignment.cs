namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// Which source currently holds which section, how far along it is, and how much slack stands
/// behind it.
/// </summary>
/// <remarks>
/// Every fact here comes from the mesh: the placement from the reported stage topology, the
/// readiness from the stage's own state word, the slack from how many usable peers are not
/// already holding a stage of this model. Nothing here is planned by this install.
///
/// Readiness is a tri-state rather than a covered flag on purpose. A section that has not
/// finished loading is not a section that failed, and collapsing the two is what made an
/// ordinary startup look like a broken network.
/// </remarks>
/// <param name="Section">The slot being filled.</param>
/// <param name="Source">The source holding it, or null when the mesh has not placed it.</param>
/// <param name="Readiness">How far the mesh has got with this section.</param>
/// <param name="StateText">The engine's own word for the stage state, shown when it is not ready.</param>
/// <param name="SpareSources">Usable peers not already holding a stage of this model, which is the slack the mesh could rebalance onto.</param>
/// <param name="Explanation">
/// What this section is actually waiting on, when the plain reading of the state would leave
/// somebody none the wiser. Null when the state speaks for itself.
/// </param>
public sealed record SourceAssignment(
    ModelSection Section,
    InferenceSource? Source,
    StageReadiness Readiness,
    string StateText,
    int SpareSources,
    string? Explanation = null)
{
    /// <summary>True when a source holds this section and the engine reports it serving.</summary>
    public bool IsCovered => Readiness == StageReadiness.Ready;

    /// <summary>True when this section is a reason the model cannot run, rather than one still arriving.</summary>
    public bool IsBlocking => Readiness is StageReadiness.Missing or StageReadiness.Failed;

    /// <summary>Coverage depth of this section, for the chain's colour and strength bars.</summary>
    public SectionCoverage Coverage => Readiness switch
    {
        StageReadiness.Ready => SpareSources >= 1 ? SectionCoverage.Healthy : SectionCoverage.Thin,
        StageReadiness.Missing or StageReadiness.Failed => SectionCoverage.Uncovered,
        _ => SectionCoverage.Starting
    };

    /// <summary>Label for the coverage chain in the panel.</summary>
    public string SourceText => Source?.DisplayName ?? (IsBlocking ? "no source" : "waiting for a source");

    /// <summary>The state word under the segment's strength bars.</summary>
    public string CoverageText => Readiness switch
    {
        StageReadiness.Ready => SpareSources >= 1
            ? SpareSources == 1 ? "1 spare source" : $"{SpareSources} spare sources"
            : "no spare source",
        StageReadiness.Pending => "not placed yet",
        StageReadiness.Loading => string.IsNullOrWhiteSpace(StateText) ? "loading" : StateText,
        StageReadiness.Missing => "not in the mesh",
        _ => string.IsNullOrWhiteSpace(StateText) ? "failed" : StateText
    };

    /// <summary>One sentence naming what this section is doing, for the status line above the chain.</summary>
    /// <remarks>
    /// A section nobody has been given is the one state where the plain reading is useless. "Not
    /// placed yet" is true of a section that will be placed in two seconds and of one that will
    /// never be placed because there is nobody to place it on, and those are not the same news.
    /// Where the mesh's own situation says which it is, that is said instead.
    /// </remarks>
    public string StatusDetail => Readiness switch
    {
        StageReadiness.Ready => $"{Section.Label} is serving on {SourceText}.",
        StageReadiness.Pending => Explanation ?? $"The mesh has not placed {Section.Label} yet.",
        StageReadiness.Loading => $"{Section.Label} is coming up on {SourceText} ({CoverageText}).",
        StageReadiness.Missing => $"No source in the mesh holds {Section.Label}.",
        _ => $"{Section.Label} is on {SourceText} but the mesh reports it {CoverageText}."
    };

    /// <summary>First strength bar: the section is held and serving.</summary>
    public bool Depth1 => IsCovered;

    /// <summary>Second strength bar: a spare source exists for the mesh to move this stage to.</summary>
    public bool Depth2 => IsCovered && SpareSources >= 1;

    /// <summary>Third strength bar: more than one spare source.</summary>
    public bool Depth3 => IsCovered && SpareSources >= 2;
}
