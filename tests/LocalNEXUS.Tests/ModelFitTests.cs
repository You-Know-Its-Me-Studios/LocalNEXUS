using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// Whether a model will fit, and whether the answer says what it assumed.
/// </summary>
/// <remarks>
/// The figure is an estimate, so what is worth holding it to is not a number but a set of
/// properties: bigger models need more, more context needs more, and the error points the cheap
/// way. Being told something will not fit when it would costs a smaller model. Being told it
/// fits when it does not costs a long download and a failed load.
///
/// The context assumption is asserted because a verdict that does not name it is not a verdict:
/// the same model fits at 4k and does not at 128k.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class ModelFitTests
{
    [Fact]
    public void NothingIsKnownWithoutACard()
    {
        Assert.Equal(FitVerdict.Unknown, ModelFit.Verdict(4d, 8192, null));
        Assert.Contains("not known", ModelFit.Describe(4d, 8192, null), StringComparison.Ordinal);
    }

    [Fact]
    public void ASmallModelOnALargeCardFits()
    {
        Assert.Equal(FitVerdict.Fits, ModelFit.Verdict(4d, 8192, 24d));
    }

    [Fact]
    public void AModelLargerThanTheCardSpillsOrWorse()
    {
        var verdict = ModelFit.Verdict(20d, 8192, 12d);

        Assert.True(
            verdict is FitVerdict.Spills or FitVerdict.TooLarge,
            $"a 20 GB model on a 12 GB card was called {verdict}");
    }

    [Fact]
    public void AModelFarLargerThanTheCardIsCalledTooLarge()
    {
        Assert.Equal(FitVerdict.TooLarge, ModelFit.Verdict(60d, 8192, 12d));
    }

    /// <summary>A model that only just fits is called out rather than reported as comfortable.</summary>
    /// <remarks>
    /// The card is derived from the estimate rather than typed, so this asserts the property that
    /// a fit inside the headroom margin is called tight, and keeps saying so if the constants
    /// behind the estimate are ever retuned.
    /// </remarks>
    [Fact]
    public void AModelThatOnlyJustFitsIsCalledTight()
    {
        const double size = 10d;
        const int context = 4096;

        // Just above what it needs, so it fits, but not by the margin a comfortable fit wants.
        var card = ModelFit.EstimateGb(size, context) + 0.25d;

        Assert.Equal(FitVerdict.Tight, ModelFit.Verdict(size, context, card));
        Assert.Contains("only just", ModelFit.Describe(size, context, card), StringComparison.Ordinal);
    }

    /// <summary>More context costs more memory, which is the reason the context is stated.</summary>
    [Fact]
    public void MoreContextNeedsMoreMemory()
    {
        var small = ModelFit.EstimateGb(8d, 4096);
        var large = ModelFit.EstimateGb(8d, 131072);

        Assert.True(large > small, $"128k context ({large}) did not cost more than 4k ({small})");
    }

    /// <summary>A bigger model needs more, cache included.</summary>
    [Fact]
    public void ABiggerModelNeedsMore()
    {
        Assert.True(ModelFit.EstimateGb(16d, 8192) > ModelFit.EstimateGb(4d, 8192));
    }

    /// <summary>The estimate is never below the weights, which are known exactly.</summary>
    [Fact]
    public void TheEstimateIsNeverLessThanTheFile()
    {
        foreach (var size in new[] { 0.5d, 4d, 20d, 60d })
        {
            Assert.True(ModelFit.EstimateGb(size, 8192) > size,
                $"a {size} GB model was estimated to need less than it weighs");
        }
    }

    /// <summary>The context it assumed is in the answer, because the answer depends on it.</summary>
    [Fact]
    public void TheDescriptionNamesTheContextItAssumed()
    {
        Assert.Contains("8k context", ModelFit.Describe(4d, 8192, 24d), StringComparison.Ordinal);
        Assert.Contains("32k context", ModelFit.Describe(4d, 32768, 24d), StringComparison.Ordinal);
    }

    /// <summary>It reads as an estimate rather than a measurement.</summary>
    [Fact]
    public void TheDescriptionSaysItIsApproximate()
    {
        Assert.Contains("about", ModelFit.Describe(4d, 8192, 24d), StringComparison.Ordinal);
    }

    [Fact]
    public void AModelOfNoSizeIsNotJudged()
    {
        Assert.Equal(FitVerdict.Unknown, ModelFit.Verdict(0d, 8192, 24d));
    }

    /// <summary>
    /// A node set to a hosted provider says nothing about this card, because it is not using it.
    /// </summary>
    [Fact]
    public void AHostedNodeHasNoFitAnswer()
    {
        using var services = TestServices.Create();
        var model = (ModelNode)services.Factory.Create("Model", 0, 0);

        model.Provider = ModelProvider.Cloud;

        Assert.False(model.HasFitAnswer);
        Assert.False(model.WillNotFit);
    }

    /// <summary>
    /// A node switched back to Local stops reporting the provider it was pointed at.
    /// </summary>
    /// <remarks>
    /// The provider id was read whatever the node was set to, so choosing a hosted provider and
    /// then switching back left the node still naming it. Anything asking what the next call
    /// would cost was answered about a call that is not going to happen, and the cost warning
    /// sits directly on that answer.
    ///
    /// The stored choice is kept rather than cleared, so going back to Cloud finds it again
    /// instead of making somebody pick twice.
    /// </remarks>
    [Fact]
    public void SwitchingBackToLocalStopsReportingACloudProvider()
    {
        using var services = TestServices.Create();
        var model = (ModelNode)services.Factory.Create("Model", 0, 0);

        var paid = ProviderCatalog.All.First(p => RunCost.HasRates(p));

        model.Provider = ModelProvider.Cloud;
        model.CloudProviderId = paid.Id;

        Assert.NotNull(model.CloudProvider);

        model.Provider = ModelProvider.Local;

        Assert.Null(model.CloudProvider);

        // The choice is remembered, so returning to Cloud does not ask for it again.
        model.Provider = ModelProvider.Cloud;

        Assert.NotNull(model.CloudProvider);
        Assert.Equal(paid.Id, model.CloudProvider!.Id);
    }

    /// <summary>Every provider that is not billed answers with no provider.</summary>
    [Theory]
    [InlineData(ModelProvider.Local)]
    [InlineData(ModelProvider.Network)]
    [InlineData(ModelProvider.SelfHosted)]
    public void AProviderThatIsNotBilledReportsNone(ModelProvider provider)
    {
        using var services = TestServices.Create();
        var model = (ModelNode)services.Factory.Create("Model", 0, 0);

        model.CloudProviderId = ProviderCatalog.All.First(p => RunCost.HasRates(p)).Id;
        model.Provider = provider;

        Assert.Null(model.CloudProvider);
    }
}
