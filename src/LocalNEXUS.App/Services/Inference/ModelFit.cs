namespace LocalNEXUS.App.Services.Inference;

/// <summary>How a model is expected to sit on the hardware that would run it.</summary>
public enum FitVerdict
{
    /// <summary>Nothing is known about the hardware, so nothing is claimed.</summary>
    Unknown,

    /// <summary>Expected to sit on the card with room to spare.</summary>
    Fits,

    /// <summary>Expected to fit, but close enough that it may not on a busy card.</summary>
    Tight,

    /// <summary>Expected to overflow onto system memory, which works and is slow.</summary>
    Spills,

    /// <summary>Too large to be worth attempting on this machine.</summary>
    TooLarge
}

/// <summary>
/// Whether a model will fit on this machine, said before anybody waits to find out.
/// </summary>
/// <remarks>
/// The number is an estimate and everything here is arranged so that it reads as one. Two things
/// are being added: the weights, which are known exactly because they are a file size, and the
/// cache the run keeps for the tokens it has seen, which is not, because working it out properly
/// needs the layer and head counts out of the model's own metadata and the reader here does not
/// go that deep.
///
/// So the cache is approximated, the approximation is stated wherever the answer is shown, and it
/// is rounded up rather than down. Being told something will not fit when it would costs somebody
/// a smaller model. Being told it will fit when it does not costs them a long download and a
/// failed load, so the error is pointed the cheaper way.
///
/// The context size is part of the answer rather than a hidden constant, because the same model
/// fits at 4k and does not at 128k, and a verdict that does not say which one it assumed is not a
/// verdict.
/// </remarks>
public static class ModelFit
{
    /// <summary>
    /// Gigabytes of cache per 1024 tokens of context, for a model of <see cref="ReferenceSizeGb"/>.
    /// </summary>
    /// <remarks>
    /// Taken from what a 7B class model at a four bit quantization actually uses, and scaled by
    /// file size from there. It is a rough figure standing in for an exact one, which is why every
    /// place that shows a verdict says the word about.
    /// </remarks>
    private const double CacheGbPer1KAtReference = 0.12d;

    /// <summary>The model size the cache figure above was measured against.</summary>
    private const double ReferenceSizeGb = 4.0d;

    /// <summary>
    /// What the runtime itself occupies before any weights are loaded.
    /// </summary>
    /// <remarks>
    /// The context, the compute buffers and the driver's own working set. Small next to the
    /// weights and not nothing, and leaving it out is how an estimate says a model fits exactly.
    /// </remarks>
    private const double RuntimeOverheadGb = 0.6d;

    /// <summary>Under this much headroom, a fit is called tight rather than comfortable.</summary>
    private const double TightHeadroomGb = 1.0d;

    /// <summary>
    /// Roughly what running this model would occupy, in gigabytes.
    /// </summary>
    /// <param name="modelSizeGb">The weights, which is the file on disk.</param>
    /// <param name="contextTokens">How much context the run is configured for.</param>
    public static double EstimateGb(double modelSizeGb, int contextTokens)
    {
        if (modelSizeGb <= 0d)
        {
            return 0d;
        }

        var scale = modelSizeGb / ReferenceSizeGb;
        var cache = Math.Max(0, contextTokens) / 1024d * CacheGbPer1KAtReference * scale;

        return modelSizeGb + cache + RuntimeOverheadGb;
    }

    /// <summary>
    /// How this model is expected to sit on a card of the given size.
    /// </summary>
    /// <param name="modelSizeGb">The weights.</param>
    /// <param name="contextTokens">The context the run is configured for.</param>
    /// <param name="availableGb">What the card holds, or null when nothing is known about it.</param>
    public static FitVerdict Verdict(double modelSizeGb, int contextTokens, double? availableGb)
    {
        if (availableGb is not { } available || available <= 0d || modelSizeGb <= 0d)
        {
            return FitVerdict.Unknown;
        }

        var needed = EstimateGb(modelSizeGb, contextTokens);

        if (needed <= available - TightHeadroomGb)
        {
            return FitVerdict.Fits;
        }

        if (needed <= available)
        {
            return FitVerdict.Tight;
        }

        // Past roughly half again the card, offloading the remainder stops being slow and starts
        // being pointless: most of the work is happening on the processor at that point.
        return needed <= available * 1.5d ? FitVerdict.Spills : FitVerdict.TooLarge;
    }

    /// <summary>
    /// The verdict in words, including the context it assumed and that it is an estimate.
    /// </summary>
    /// <remarks>
    /// The assumption travels with the answer everywhere, because the same model fits at one
    /// context and does not at another, and somebody reading a verdict without knowing which was
    /// assumed is being told something that might not apply to their settings.
    /// </remarks>
    public static string Describe(double modelSizeGb, int contextTokens, double? availableGb)
    {
        var verdict = Verdict(modelSizeGb, contextTokens, availableGb);

        if (verdict == FitVerdict.Unknown)
        {
            return "No graphics card was detected, so how this will run is not known. It will "
                + "work on the processor, slowly.";
        }

        var needed = EstimateGb(modelSizeGb, contextTokens);
        var about = $"about {needed:0.0} GB at {Describe(contextTokens)} context, "
            + $"against {availableGb:0.0} GB on the card";

        return verdict switch
        {
            FitVerdict.Fits => $"Fits: {about}.",

            FitVerdict.Tight => $"Fits, but only just: {about}. Something else using the card may "
                + "push it over.",

            FitVerdict.Spills => $"Will not all fit: {about}. The rest runs on the processor, "
                + "which works and is several times slower.",

            _ => $"Too large for this machine: {about}. A smaller quantization or a smaller model "
                + "would run properly."
        };
    }

    /// <summary>A context length as somebody would say it rather than as a number of tokens.</summary>
    private static string Describe(int contextTokens)
        => contextTokens >= 1024 && contextTokens % 1024 == 0
            ? $"{contextTokens / 1024}k"
            : contextTokens.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
