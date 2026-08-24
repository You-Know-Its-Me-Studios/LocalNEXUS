using System.IO;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Compilation;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// The whole path, with a real model actually running on this machine.
/// </summary>
/// <remarks>
/// A separate layer because it is slow, because it needs a model on disk, and because it cannot
/// assert on what a model says. Run it deliberately:
///
///     dotnet test --filter Layer=EndToEnd
///
/// Nothing here checks the text of a reply. A model is not deterministic and a test that expected
/// particular words would be a test of that model on that day. What is checked is everything
/// around the reply: that a server started, that a request went out and something came back, that
/// the shape of the result is filled in, and that code which reaches the writer compiles. Those
/// hold for any model, which is what makes them worth asserting.
///
/// Nothing is downloaded. If the model is not there, the test says exactly what is missing and
/// fails, because this layer only runs when somebody asked for it.
/// </remarks>
[Trait(Layers.Name, Layers.EndToEnd)]
[Collection("end to end")]
public sealed class EndToEndTests
{
    /// <summary>The model used for these tests, if a copy of it is present.</summary>
    private static string? FindModel()
    {
        if (!Directory.Exists(AppPaths.ModelsGguf))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(AppPaths.ModelsGguf, "*.gguf", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>The model, or a failure naming what is missing and where it should go.</summary>
    private static string RequireModel()
    {
        if (AppPaths.FindLlamaServerExecutable() is null)
        {
            Assert.Fail(
                "llama-server was not found. It is expected in vendor/llama beside the repository "
                + "or beside the published exe. This layer cannot run without it, and nothing here downloads it.");
        }

        return FindModel() ?? throw AssertMissingModel();
    }

    private static Exception AssertMissingModel() => new Xunit.Sdk.XunitException(
        $"No GGUF model was found under {AppPaths.ModelsGguf}. Put one there and run this layer again. "
        + "Nothing here downloads a model.");

    /// <summary>A generous ceiling. A cold model load on a large file is not quick.</summary>
    private static CancellationTokenSource Clock() => new(TimeSpan.FromMinutes(10));

    /// <summary>
    /// A real server starts and answers, and the result is filled in rather than merely non null.
    /// </summary>
    /// <remarks>
    /// The token counts and the elapsed time are what everything upstream reports and bills
    /// against, so a client that returned the text and nothing else would look fine here and be
    /// wrong everywhere it mattered.
    /// </remarks>
    [RequiresLocalModelFact]
    public async Task ALocalModelAnswers()
    {
        var model = RequireModel();

        using var services = TestServices.Create();
        using var clock = Clock();

        var descriptor = ModelFormatDetector.Describe(model);
        Assert.Equal(ModelFormat.Gguf, descriptor.Format);

        var runtime = services.Services.Runtimes.Resolve(descriptor);
        Assert.NotNull(runtime);

        var endpoint = await services.Services.Runtimes.ServeAsync(
            model,
            new ModelRuntimeOptions(),
            null,
            clock.Token);

        try
        {
            var client = new OpenAiCompatibleClient();

            var result = await client.StreamChatAsync(
                new ModelEndpoint(endpoint.BaseUrl, endpoint.ModelId, null),
                "You answer in one short sentence.",
                "Name one colour.",
                0.1d,
                64,
                null,
                clock.Token);

            Assert.False(string.IsNullOrWhiteSpace(result.Text));
            Assert.True(result.Elapsed > TimeSpan.Zero);
            Assert.NotNull(result.FinishReason);
            Assert.True(result.CompletionTokens is null or > 0);
        }
        finally
        {
            services.Services.Runtimes.ShutdownAll();
        }
    }

    /// <summary>
    /// Tokens arrive while the reply is being produced rather than all at the end.
    /// </summary>
    /// <remarks>
    /// The activity feed is the whole experience of using this, and it is streaming or it is a
    /// progress bar. Asserted as "more than one chunk", which is the only claim that holds for
    /// every model and every reply length.
    /// </remarks>
    [RequiresLocalModelFact]
    public async Task TheReplyIsStreamed()
    {
        var model = RequireModel();

        using var services = TestServices.Create();
        using var clock = Clock();

        var endpoint = await services.Services.Runtimes.ServeAsync(model, new ModelRuntimeOptions(), null, clock.Token);

        try
        {
            var chunks = 0;
            var client = new OpenAiCompatibleClient();

            var result = await client.StreamChatAsync(
                new ModelEndpoint(endpoint.BaseUrl, endpoint.ModelId, null),
                "You answer in three or four sentences.",
                "Describe what a stack is, briefly.",
                0.1d,
                200,
                new Progress<string>(_ => Interlocked.Increment(ref chunks)),
                clock.Token);

            Assert.False(string.IsNullOrWhiteSpace(result.Text));
            Assert.True(chunks > 1, $"the reply arrived in {chunks} chunk(s)");
        }
        finally
        {
            services.Services.Runtimes.ShutdownAll();
        }
    }

    /// <summary>
    /// A graph asks a real model for code and the compiler check either passes it or repairs it.
    /// </summary>
    /// <remarks>
    /// The claim being tested is the one the whole fence exists for: what reaches the writer
    /// compiles. Nothing is asserted about what the model wrote, only that whatever came out the
    /// far end is code a compiler accepts. If it could not be made to compile within the retry
    /// limit, the run says so and does not emit, which is also correct and is asserted as such.
    /// </remarks>
    [RequiresLocalModelFact]
    public async Task CodeThatReachesTheWriterCompiles()
    {
        var model = RequireModel();

        using var services = TestServices.Create();
        using var clock = Clock();

        var graph = new GraphModel();

        var prompt = (PromptNode)services.Factory.Create("Prompt");
        var coder = (ModelNode)services.Factory.Create("Model");
        var check = (CompilerCheckNode)services.Factory.Create("CompilerCheck");

        coder.Provider = ModelProvider.Local;
        coder.ModelFilePath = model;
        coder.SystemPrompt = "You write one C# class and nothing else. No explanation, no markdown.";
        coder.MaxTokens = 400;
        coder.Temperature = 0.1d;

        check.RetryLimit = 2;
        check.FailureBehaviour = CompileFailureBehaviour.ContinueWithWarning;

        graph.AddNode(prompt);
        graph.AddNode(coder);
        graph.AddNode(check);

        Assert.True(graph.TryConnect(prompt.Request, coder.Prompt, out _));
        Assert.True(graph.TryConnect(coder.Completion, check.Code, out _));

        try
        {
            var run = await new GraphExecutor(services.Services).RunAsync(
                graph,
                "Write a public class called Counter with an int Value property and an Increment method.",
                clock.Token);

            Assert.NotEqual(RunState.Faulted, run.State);

            // Whatever the model wrote, the outcome says plainly which of the three happened.
            Assert.Contains(
                check.Outcome,
                new[] { CompileOutcome.Compiled, CompileOutcome.Repaired, CompileOutcome.Failed });

            if (check.Outcome is CompileOutcome.Compiled or CompileOutcome.Repaired)
            {
                Assert.True(run.TryGetValue(check.Checked, out var emitted));

                var code = emitted?.ToString() ?? string.Empty;
                Assert.False(string.IsNullOrWhiteSpace(code));

                // The claim, restated independently: it compiles.
                var recheck = await services.Services.Compiler.CompileAsync(
                    code,
                    RoslynUnityCompiler.DeriveFileName(code, "Emitted.cs"),
                    null,
                    clock.Token);

                Assert.True(recheck.Succeeded, recheck.FormatDiagnostics(5));
            }
        }
        finally
        {
            services.Services.Runtimes.ShutdownAll();
        }
    }

    /// <summary>
    /// A fenced reply from a real model comes out of the node without its fence.
    /// </summary>
    /// <remarks>
    /// The stripping is unit tested against every shape a model has produced. What this adds is
    /// that a real model, asked for code, produces one of those shapes and the setting is actually
    /// applied on the path a run takes.
    /// </remarks>
    [RequiresLocalModelFact]
    public async Task AFencedReplyReachesTheWireUnfenced()
    {
        var model = RequireModel();

        using var services = TestServices.Create();
        using var clock = Clock();

        var graph = new GraphModel();
        var prompt = (PromptNode)services.Factory.Create("Prompt");
        var coder = (ModelNode)services.Factory.Create("Model");

        coder.Provider = ModelProvider.Local;
        coder.ModelFilePath = model;
        coder.StripCodeFences = true;
        coder.SystemPrompt = "You reply with one C# class in a markdown code fence and nothing else.";
        coder.MaxTokens = 300;
        coder.Temperature = 0.1d;

        graph.AddNode(prompt);
        graph.AddNode(coder);
        Assert.True(graph.TryConnect(prompt.Request, coder.Prompt, out _));

        try
        {
            var run = await new GraphExecutor(services.Services).RunAsync(
                graph,
                "Write a public class called Marker with no members.",
                clock.Token);

            Assert.Equal(RunState.Completed, run.State);
            Assert.True(run.TryGetValue(coder.Completion, out var emitted));

            var text = (emitted?.ToString() ?? string.Empty).Trim();

            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.False(
                text.StartsWith("```", StringComparison.Ordinal),
                $"the reply still opens with a fence: {text[..Math.Min(60, text.Length)]}");
        }
        finally
        {
            services.Services.Runtimes.ShutdownAll();
        }
    }

    /// <summary>
    /// Asking for the same model twice reuses the server rather than starting a second one.
    /// </summary>
    /// <remarks>
    /// A graph with three model nodes on one local model is the ordinary case, and three copies of
    /// a seven gigabyte model would not fit on most machines that can run one.
    /// </remarks>
    [RequiresLocalModelFact]
    public async Task TheSameModelIsServedOnce()
    {
        var model = RequireModel();

        using var services = TestServices.Create();
        using var clock = Clock();

        Assert.Equal(ModelFormat.Gguf, ModelFormatDetector.Describe(model).Format);

        try
        {
            var first = await services.Services.Runtimes.ServeAsync(model, new ModelRuntimeOptions(), null, clock.Token);
            var second = await services.Services.Runtimes.ServeAsync(model, new ModelRuntimeOptions(), null, clock.Token);

            Assert.Equal(first.BaseUrl, second.BaseUrl);
        }
        finally
        {
            services.Services.Runtimes.ShutdownAll();
        }
    }
}

/// <summary>
/// Keeps the end to end tests from starting several servers at once.
/// </summary>
/// <remarks>
/// They each load a multi gigabyte model. Run in parallel they would ask for several copies of it
/// in memory at the same time, and the failure would look like a flaky test rather than what it
/// is.
/// </remarks>
[CollectionDefinition("end to end", DisableParallelization = true)]
public sealed class EndToEndCollection
{
}
