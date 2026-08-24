using LocalNEXUS.App.Services.Models;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// Saying what a model is for, out of the labels its author already applied.
/// </summary>
/// <remarks>
/// The rule this holds is a refusal: nothing is invented. Every line is a restatement of a label
/// on the repository, so a repository whose labels say nothing usable gets no line at all. A
/// description written here would look, on screen, exactly like one the author wrote, and would be
/// wrong for precisely the models nobody has heard of, which are most of them.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class ModelUseCaseTests
{
    [Fact]
    public void ThePipelineTagCarriesTheLine()
    {
        Assert.Equal("Writes text", ModelUseCase.Describe("text-generation", null));
        Assert.Equal("Makes embeddings", ModelUseCase.Describe("feature-extraction", null));
        Assert.Equal("Reads images and answers in text", ModelUseCase.Describe("image-text-to-text", null));
        Assert.Equal("Turns speech into text", ModelUseCase.Describe("automatic-speech-recognition", null));
    }

    [Fact]
    public void TagsNarrowWhatThePipelineSaid()
    {
        Assert.Equal(
            "Writes text, for code",
            ModelUseCase.Describe("text-generation", new[] { "gguf", "code", "llama" }));

        Assert.Equal(
            "Writes text, for code and chat",
            ModelUseCase.Describe("text-generation", new[] { "code", "conversational" }));
    }

    /// <summary>
    /// Nothing usable means no line, not a guess.
    /// </summary>
    /// <remarks>
    /// These are real tags off real repositories. Every one of them describes the file rather than
    /// what the model is for: a quantization format, a licence, a base model, an architecture.
    /// </remarks>
    [Fact]
    public void LabelsThatSayNothingProduceNothing()
    {
        Assert.Equal(string.Empty, ModelUseCase.Describe(null, null));
        Assert.Equal(string.Empty, ModelUseCase.Describe(null, Array.Empty<string>()));
        Assert.Equal(string.Empty, ModelUseCase.Describe("some-future-pipeline", null));

        Assert.Equal(
            string.Empty,
            ModelUseCase.Describe(null, new[] { "gguf", "qwen3", "license:apache-2.0", "region:us", "imatrix" }));
    }

    /// <summary>
    /// A namespaced tag is not a claim about what the model is for.
    /// </summary>
    /// <remarks>
    /// The case that made this a whole tag comparison rather than a substring one:
    /// base_model:quantized:Qwen/Qwen3-Coder contains the word code, and a substring test reports
    /// the repository as being for code because of the name of something it was quantized from.
    /// </remarks>
    [Fact]
    public void ANamespacedTagIsNotReadAsACapability()
    {
        Assert.Equal(
            "Writes text",
            ModelUseCase.Describe("text-generation", new[] { "base_model:quantized:Qwen/Qwen3-Coder" }));

        Assert.Equal(
            string.Empty,
            ModelUseCase.Describe(null, new[] { "base_model:Qwen/Qwen3-Coder", "license:mit" }));
    }

    /// <summary>Tags alone still say something, and lead the line when there is no pipeline tag.</summary>
    [Fact]
    public void TagsAloneAreEnough()
    {
        Assert.Equal("For code", ModelUseCase.Describe(null, new[] { "code" }));
        Assert.Equal("For embeddings", ModelUseCase.Describe(null, new[] { "sentence-transformers" }));
    }

    /// <summary>The same meaning twice is said once.</summary>
    [Fact]
    public void TwoTagsMeaningTheSameThingAreNotSaidTwice()
    {
        Assert.Equal(
            "Writes text, for code",
            ModelUseCase.Describe("text-generation", new[] { "code", "coding" }));

        Assert.Equal(
            "Writes text, for images",
            ModelUseCase.Describe("text-generation", new[] { "vision", "multimodal" }));
    }

    /// <summary>A line stops being a summary somewhere, and this is where.</summary>
    [Fact]
    public void ALineIsNotJustTheTagListAgain()
    {
        var line = ModelUseCase.Describe(
            "text-generation",
            new[] { "code", "vision", "reasoning", "math", "conversational", "agent" });

        // Three qualifiers at most, so the line stays readable.
        Assert.Equal("Writes text, for code, images and reasoning", line);
    }
}
