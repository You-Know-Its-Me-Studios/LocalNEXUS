namespace LocalNEXUS.App.Services.Models;

/// <summary>
/// What a repository is for, read off the labels its author already applied.
/// </summary>
/// <remarks>
/// Mechanical, and that is the constraint rather than an implementation detail. Every line this
/// produces is a restatement of a label the author of the repository chose: the pipeline tag says
/// what the model does, and a handful of tags say what it was tuned for. Nothing here reads a
/// model card, guesses from a name, or asks a model to summarise anything.
///
/// The reason is that the alternative is worse than nothing. A description invented here would be
/// indistinguishable, on screen, from one the author wrote, and would be wrong for exactly the
/// models nobody here has heard of, which are most of them. So a repository whose labels say
/// nothing usable gets no line at all rather than a sentence somebody might act on.
///
/// The mapping is a table rather than a chain of conditions, so adding a capability is a row.
/// </remarks>
public static class ModelUseCase
{
    /// <summary>
    /// What each pipeline tag means, in words rather than in Hugging Face's vocabulary.
    /// </summary>
    /// <remarks>
    /// The pipeline tag is the one label Hugging Face asks every author to set and validates
    /// against a fixed list, which is what makes it worth leading with. Anything not on this list
    /// is left alone rather than reworded into something that sounds close.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> Pipelines =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["text-generation"] = "Writes text",
            ["text2text-generation"] = "Rewrites text",
            ["image-text-to-text"] = "Reads images and answers in text",
            ["visual-question-answering"] = "Answers questions about images",
            ["feature-extraction"] = "Makes embeddings",
            ["sentence-similarity"] = "Makes embeddings",
            ["fill-mask"] = "Fills in missing words",
            ["summarization"] = "Summarises",
            ["translation"] = "Translates",
            ["automatic-speech-recognition"] = "Turns speech into text",
            ["text-to-speech"] = "Turns text into speech",
            ["text-to-image"] = "Makes images",
            ["token-classification"] = "Labels words in text",
            ["text-classification"] = "Sorts text into categories",
            ["question-answering"] = "Answers questions about a passage"
        };

    /// <summary>
    /// Tags that say what a model was tuned for, and what each one is called here.
    /// </summary>
    /// <remarks>
    /// Ordered, because the line reads better when the strongest signal comes first and because a
    /// dictionary would leave the order to chance. Only tags that describe a use are listed:
    /// quantization formats, licences, base models and architecture names all appear in the same
    /// list on Hugging Face and none of them answers what is this for.
    /// </remarks>
    private static readonly (string Tag, string Says)[] Qualifiers =
    {
        ("code", "code"),
        ("coding", "code"),
        ("vision", "images"),
        ("multimodal", "images"),
        ("embeddings", "embeddings"),
        ("sentence-transformers", "embeddings"),
        ("reasoning", "reasoning"),
        ("math", "maths"),
        ("conversational", "chat"),
        ("instruct", "following instructions"),
        ("agent", "tool use"),
        ("function-calling", "tool use"),
        ("tool-use", "tool use")
    };

    /// <summary>
    /// One line saying what this is for, or nothing when the labels do not say.
    /// </summary>
    /// <param name="pipelineTag">The repository's pipeline tag, when it set one.</param>
    /// <param name="tags">Every tag on the repository, including ones this ignores.</param>
    public static string Describe(string? pipelineTag, IReadOnlyList<string>? tags)
    {
        var what = pipelineTag is { Length: > 0 } pipeline && Pipelines.TryGetValue(pipeline, out var said)
            ? said
            : null;

        var qualifiers = Recognised(tags);

        if (what is null && qualifiers.Count == 0)
        {
            return string.Empty;
        }

        if (what is null)
        {
            // No pipeline tag, but the tags said something. Lead with what they said rather than
            // assuming text generation, which is the guess that would be right most of the time
            // and silently wrong for every embedding model.
            return Capitalise($"for {Join(qualifiers)}");
        }

        return qualifiers.Count == 0
            ? what
            : $"{what}, for {Join(qualifiers)}";
    }

    /// <summary>The qualifier tags this repository actually carries, in table order, deduplicated.</summary>
    private static List<string> Recognised(IReadOnlyList<string>? tags)
    {
        var found = new List<string>();

        if (tags is null || tags.Count == 0)
        {
            return found;
        }

        foreach (var (tag, says) in Qualifiers)
        {
            if (found.Contains(says, StringComparer.Ordinal))
            {
                continue;
            }

            if (tags.Any(carried => Matches(carried, tag)))
            {
                found.Add(says);
            }
        }

        // Three is where a line stops being a summary and starts being the tag list again.
        return found.Count > 3 ? found.GetRange(0, 3) : found;
    }

    /// <summary>
    /// True when a tag on the repository is this qualifier.
    /// </summary>
    /// <remarks>
    /// A whole tag rather than a substring. Hugging Face tags are namespaced with colons, as in
    /// base_model:quantized:Qwen/Qwen3-Coder, and a substring test on "code" matches that and
    /// reports a model as being for code because of the name of something it was quantized from.
    /// </remarks>
    private static bool Matches(string carried, string tag)
    {
        if (carried.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(carried, tag, StringComparison.OrdinalIgnoreCase);
    }

    private static string Join(List<string> parts)
        => parts.Count switch
        {
            1 => parts[0],
            2 => $"{parts[0]} and {parts[1]}",
            _ => $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}"
        };

    private static string Capitalise(string text)
        => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
