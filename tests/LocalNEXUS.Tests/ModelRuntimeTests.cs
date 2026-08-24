using System.IO;
using System.Text;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// Deciding what a path holds, and which runtime can serve it.
/// </summary>
/// <remarks>
/// The settled rule is that format is detected by content and never by extension, so every case
/// here writes real bytes and lets the detector read them. A file named .gguf that is not one, and
/// a GGUF with the wrong extension, are the two cases an extension check gets exactly backwards.
///
/// Nothing here starts a runtime. Bringing a model up needs a model, and the suite does not
/// download one.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class ModelFormatTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "localnexus-tests", Guid.NewGuid().ToString("N"));

    public ModelFormatTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Writes a file with the four magic bytes a GGUF starts with.</summary>
    private string WriteGguf(string fileName)
    {
        var path = Path.Combine(_folder, fileName);
        var bytes = new byte[64];

        Encoding.ASCII.GetBytes("GGUF").CopyTo(bytes, 0);
        File.WriteAllBytes(path, bytes);

        return path;
    }

    [Fact]
    public void AGgufIsRecognisedByItsMagicBytes()
    {
        var described = ModelFormatDetector.Describe(WriteGguf("model.gguf"));

        Assert.Equal(ModelFormat.Gguf, described.Format);
        Assert.True(described.IsServable);
    }

    /// <summary>
    /// A GGUF is still a GGUF under any name, because the extension is not what is read.
    /// </summary>
    [Fact]
    public void AGgufWithTheWrongExtensionIsStillAGguf()
        => Assert.Equal(ModelFormat.Gguf, ModelFormatDetector.Describe(WriteGguf("model.bin")).Format);

    /// <summary>
    /// A file called .gguf that is not one is not a model, and says so rather than being attempted.
    /// </summary>
    /// <remarks>
    /// The failure this prevents is a truncated or half downloaded file being handed to
    /// llama-server, which reports something unhelpful from deep inside itself several seconds
    /// later.
    /// </remarks>
    [Fact]
    public void AFileNamedGgufThatIsNotOneIsRefused()
    {
        var path = Path.Combine(_folder, "notreally.gguf");
        File.WriteAllText(path, "this is a text file");

        var described = ModelFormatDetector.Describe(path);

        Assert.NotEqual(ModelFormat.Gguf, described.Format);
        Assert.False(described.IsServable);
    }

    /// <summary>A folder with a config beside safetensors weights is a model.</summary>
    [Fact]
    public void AFolderOfWeightsWithAConfigIsAModel()
    {
        var folder = Path.Combine(_folder, "some-model");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "config.json"), "{}");
        File.WriteAllBytes(Path.Combine(folder, "model.safetensors"), new byte[128]);

        var described = ModelFormatDetector.Describe(folder);

        Assert.Equal(ModelFormat.Safetensors, described.Format);
        Assert.True(described.IsServable);
    }

    /// <summary>
    /// Weights with no config are their own reported state, not a model to attempt.
    /// </summary>
    /// <remarks>
    /// This is what a partial download looks like, and it is worth naming rather than guessing at.
    /// </remarks>
    [Fact]
    public void WeightsWithNoConfigAreNotAModel()
    {
        var folder = Path.Combine(_folder, "no-config");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "model.safetensors"), new byte[128]);

        var described = ModelFormatDetector.Describe(folder);

        Assert.False(described.IsServable);
        Assert.False(string.IsNullOrWhiteSpace(described.UnsupportedReason));
    }

    /// <summary>A lone safetensors file is a component of a model rather than a model.</summary>
    [Fact]
    public void ALoneSafetensorsFileIsAComponent()
    {
        var path = Path.Combine(_folder, "model-00001-of-00003.safetensors");
        File.WriteAllBytes(path, new byte[128]);

        var described = ModelFormatDetector.Describe(path);

        Assert.False(described.IsServable);
        Assert.False(string.IsNullOrWhiteSpace(described.UnsupportedReason));
    }

    /// <summary>Anything unrecognised is reported as unrecognised rather than guessed at.</summary>
    [Fact]
    public void SomethingElseEntirelyIsUnrecognised()
    {
        var path = Path.Combine(_folder, "readme.txt");
        File.WriteAllText(path, "hello");

        Assert.False(ModelFormatDetector.Describe(path).IsServable);
    }

    /// <summary>A path that is not there is reported rather than throwing.</summary>
    [Fact]
    public void AMissingPathIsReported()
    {
        var described = ModelFormatDetector.Describe(Path.Combine(_folder, "nothing-here.gguf"));

        Assert.False(described.IsServable);
    }

    /// <summary>Every format has a label, because it is shown next to the model in the list.</summary>
    [Fact]
    public void EveryFormatHasALabel()
    {
        foreach (var format in Enum.GetValues<ModelFormat>())
        {
            var descriptor = new ModelDescriptor("some/path", format, "name", 0);

            Assert.False(string.IsNullOrWhiteSpace(descriptor.FormatLabel));
        }
    }
}

/// <summary>
/// Picking the runtime that serves a given format.
/// </summary>
/// <remarks>
/// The whole of the abstraction is that a model node asks for a path and gets an endpoint back, so
/// adding a third runtime is one entry in the resolver and nothing else. These tests use runtimes
/// defined in the test assembly, which is the only way to check that claim: if the resolver knew
/// anything about llama.cpp or Python specifically, a runtime it has never heard of could not win.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class RuntimeResolverTests
{
    [Fact]
    public void TheRuntimeThatCanServeItIsChosen()
    {
        var gguf = new StubRuntime("gguf runtime", ModelFormat.Gguf);
        var safetensors = new StubRuntime("safetensors runtime", ModelFormat.Safetensors);
        var resolver = new RuntimeResolver(gguf, safetensors);

        Assert.Same(gguf, resolver.Resolve(new ModelDescriptor("a.gguf", ModelFormat.Gguf, "a", 1)));
        Assert.Same(safetensors, resolver.Resolve(new ModelDescriptor("b", ModelFormat.Safetensors, "b", 1)));
    }

    /// <summary>The runtimes are asked in order, so the first that answers wins.</summary>
    [Fact]
    public void TheFirstRuntimeThatAnswersWins()
    {
        var first = new StubRuntime("first", ModelFormat.Gguf);
        var second = new StubRuntime("second", ModelFormat.Gguf);

        Assert.Same(first, new RuntimeResolver(first, second)
            .Resolve(new ModelDescriptor("a.gguf", ModelFormat.Gguf, "a", 1)));
    }

    /// <summary>A format nothing serves is refused, and the refusal names the format.</summary>
    [Fact]
    public void AFormatNothingServesIsRefused()
    {
        var resolver = new RuntimeResolver(new StubRuntime("gguf runtime", ModelFormat.Gguf));

        var ex = Assert.Throws<ModelClientException>(() => resolver.Resolve(
            new ModelDescriptor("b", ModelFormat.Safetensors, "b", 1)));

        Assert.Contains("safetensors", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Something that is not a model is refused with the reason the detector gave.</summary>
    [Fact]
    public void SomethingUnservableIsRefusedWithItsReason()
    {
        var resolver = new RuntimeResolver(new StubRuntime("gguf runtime", ModelFormat.Gguf));

        var ex = Assert.Throws<ModelClientException>(() => resolver.Resolve(new ModelDescriptor(
            "half-a-download",
            ModelFormat.Unknown,
            "half a download",
            0,
            unsupportedReason: "The file does not start with the GGUF marker.")));

        Assert.Contains("GGUF marker", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>The real build wires three runtimes and still serves both formats.</summary>
    [Fact]
    public void TheRealBuildServesBothFormats()
    {
        using var services = TestServices.Create();

        var runtimes = services.Services.Runtimes;

        Assert.Equal(3, runtimes.Runtimes.Count);
        Assert.NotNull(runtimes.Resolve(new ModelDescriptor("a.gguf", ModelFormat.Gguf, "a", 1)));
        Assert.NotNull(runtimes.Resolve(new ModelDescriptor("b", ModelFormat.Safetensors, "b", 1)));
    }

    /// <summary>
    /// The distributed runtime is asked before the single machine one.
    /// </summary>
    /// <remarks>
    /// Both answer for safetensors and the resolver takes the first that says yes, so this order
    /// is the difference between the distributed path being reachable and it silently never
    /// activating however the settings are set. Nothing else in the build would fail if these
    /// two were swapped, which is exactly why it is asserted here.
    /// </remarks>
    [Fact]
    public void TheDistributedRuntimeIsAskedBeforeTheSingleMachineOne()
    {
        using var services = TestServices.Create();

        var order = services.Services.Runtimes.Runtimes.Select(r => r.GetType()).ToList();

        var distributed = order.IndexOf(typeof(DistributedRuntimeManager));
        var single = order.IndexOf(typeof(PythonRuntimeManager));

        Assert.True(distributed >= 0, "the distributed runtime is not in the build at all");
        Assert.True(single >= 0, "the single machine Python runtime is not in the build at all");
        Assert.True(
            distributed < single,
            $"the distributed runtime is asked at position {distributed} and the single machine "
            + $"one at {single}. Asked second it can never claim a model.");
    }

    /// <summary>
    /// With the switch off, a safetensors model goes to the single machine runtime.
    /// </summary>
    /// <remarks>
    /// The guarantee that turning nothing on changes nothing. This is what stops the newest and
    /// least proven path from quietly taking over the one people already rely on.
    /// </remarks>
    [Fact]
    public void SafetensorsGoesToThePythonRuntimeUntilDistributionIsSwitchedOn()
    {
        using var services = TestServices.Create();

        Assert.False(services.Config.DistributedInferenceEnabled);

        var chosen = services.Services.Runtimes.Resolve(
            new ModelDescriptor("b", ModelFormat.Safetensors, "b", 1));

        Assert.IsType<PythonRuntimeManager>(chosen);
    }
}
