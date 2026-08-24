namespace LocalNEXUS.App.Services.Models;

/// <summary>What is known about a model's tool calling before it has ever been run.</summary>
public enum ToolCallingExpectation
{
    /// <summary>Nothing is known, which is the honest answer for most names.</summary>
    Unknown,

    /// <summary>Its family is trained for tool calling, so it probably does.</summary>
    Likely,

    /// <summary>Its family is not, so it probably does not.</summary>
    Unlikely
}

/// <summary>
/// A guess at whether a model calls tools, made from its name before it is downloaded.
/// </summary>
/// <remarks>
/// The real answer comes from handing a model a tool and seeing whether it calls one, and that
/// needs the model running, which needs it downloaded, which is the thing somebody is deciding
/// about. So this is a note rather than an answer, and it is written as one everywhere it is
/// shown.
///
/// It is deliberately short and deliberately cautious. Only families where the whole line is
/// trained one way are listed, and anything not recognised is Unknown, which is a real answer and
/// not a failure to have one. Overclaiming here is worse than saying nothing: somebody downloads
/// eighteen gigabytes on the strength of it and finds the Agent cannot use it.
///
/// Two things reliably mean no, whatever the base model is. A model published for text completion
/// rather than instruction following has no chat template to put a tool call in, and a model
/// whose name says base is that model before any of the training that would teach it.
/// </remarks>
public static class ToolCallingFamilies
{
    /// <summary>Families whose instruction tuned releases are trained to emit tool calls.</summary>
    private static readonly string[] Likely =
    {
        "qwen2.5", "qwen3", "llama-3.1", "llama-3.2", "llama-3.3", "llama3.1", "llama3.2",
        "mistral", "mixtral", "ministral", "hermes", "functionary", "firefunction",
        "command-r", "granite", "watt-tool", "gorilla"
    };

    /// <summary>Names that mean this is not an instruction following model at all.</summary>
    private static readonly string[] Unlikely =
    {
        "-base", "base-", "completion", "text-davinci", "starcoder", "santacoder",
        "replit-code", "codegen", "stable-code-3b"
    };

    /// <summary>What can be said about this repository or file name.</summary>
    public static ToolCallingExpectation Expect(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ToolCallingExpectation.Unknown;
        }

        var lowered = name.ToLowerInvariant();

        // Checked first, because a base build of a family that otherwise calls tools is still a
        // base build and the family match would otherwise claim it does.
        if (Unlikely.Any(marker => lowered.Contains(marker, StringComparison.Ordinal)))
        {
            return ToolCallingExpectation.Unlikely;
        }

        // An instruction tuned build is the one that gets the training, so a family match only
        // counts alongside a sign that this is that build.
        var instructed = lowered.Contains("instruct", StringComparison.Ordinal)
            || lowered.Contains("-it", StringComparison.Ordinal)
            || lowered.Contains("chat", StringComparison.Ordinal)
            || lowered.Contains("hermes", StringComparison.Ordinal)
            || lowered.Contains("functionary", StringComparison.Ordinal);

        return instructed && Likely.Any(family => lowered.Contains(family, StringComparison.Ordinal))
            ? ToolCallingExpectation.Likely
            : ToolCallingExpectation.Unknown;
    }

    /// <summary>The note as it is shown, which says how firm it is.</summary>
    public static string Describe(string? name) => Expect(name) switch
    {
        ToolCallingExpectation.Likely =>
            "Its family usually calls tools, which the Agent node needs. Check it on the Model "
            + "node once it is downloaded, because that is the only way to know.",

        ToolCallingExpectation.Unlikely =>
            "Probably does not call tools, so the Agent node will not work with it. It is still "
            + "fine for the pipeline, which does not need them.",

        _ => "Whether it calls tools is not known from the name. The Model node can check once it "
             + "is downloaded."
    };
}
