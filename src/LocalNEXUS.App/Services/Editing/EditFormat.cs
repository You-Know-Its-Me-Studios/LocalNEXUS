namespace LocalNEXUS.App.Services.Editing;

/// <summary>
/// How a model is asked to express a change to a file.
/// </summary>
/// <remarks>
/// The format is not a detail. The same model scores very differently depending on which one it
/// is asked for, and the ordering is not the same for every model: search and replace blocks suit
/// large models, while a line tagged diff scores best for the smaller ones. The models this runs
/// on locally are the smaller ones, so the diff default is the line tagged form.
///
/// It is per model node because the right answer depends on the model behind that node, and one
/// graph can have a large hosted planner beside a small local coder.
/// </remarks>
public enum EditFormat
{
    /// <summary>
    /// Whole file for a new file or a small one, a line tagged diff for changes to larger files.
    /// The default, because rewriting a two hundred line file to change one method wastes most of
    /// a small context window on lines that were never in question.
    /// </summary>
    Automatic,

    /// <summary>Always return the complete file. Simplest, and the most tokens.</summary>
    WholeFile,

    /// <summary>Always return a line tagged diff, even for a new file.</summary>
    LineTaggedDiff
}

/// <summary>
/// Whether the model behind a node is known to handle producing a diff.
/// </summary>
/// <remarks>
/// Two states and neither of them is "cannot", because that is not a thing this can establish. It
/// is one thing to know a model is a frontier hosted one and another to prove a local seven billion
/// parameter model is bad at something, and the measurements say the second is true: JetBrains'
/// Diff-XYZ benchmark scores Qwen2.5-Coder-7B-Instruct at 0.59 exact match applying a supplied diff
/// and 0.03 generating one, and Aider's polyglot run for the 32B scored 8.0 percent in diff format
/// against 16.4 percent whole file, with 148 malformed replies.
///
/// So unknown is the honest answer for a local model and it is the one that leans towards sending
/// the whole file, which is the safer way to be wrong: a whole file that does not fit is refused
/// loudly, and a diff a model could not write is applied to the wrong lines or not at all.
/// </remarks>
public enum EditCapability
{
    /// <summary>Nothing establishes that this model writes diffs well, so assume it does not.</summary>
    Unknown,

    /// <summary>A hosted frontier model, which the published benchmarks put in the band that does.</summary>
    HandlesDiffs
}
