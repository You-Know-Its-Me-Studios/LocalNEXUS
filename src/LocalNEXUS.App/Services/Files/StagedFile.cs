namespace LocalNEXUS.App.Services.Files;

/// <summary>Why a file is waiting rather than written.</summary>
/// <remarks>
/// Three states rather than a flag, and they read differently on purpose. A file that does not
/// compile is a file the model has not finished. A file the project rules refused is a file that
/// compiles perfectly and would have broken a scene, which is a different conversation and a
/// different fix. A write that failed is neither: the disk said no.
/// </remarks>
public enum StagedReason
{
    /// <summary>The compile check could not get it to compile within its retry limit.</summary>
    DidNotCompile,

    /// <summary>A Unity binding rule refused it. It compiles; writing it would break something.</summary>
    RefusedByProjectRules,

    /// <summary>The write itself failed, so the file on disk is whatever it was.</summary>
    WriteFailed,

    /// <summary>
    /// The coder kept asking to change lines that are not in the file, so nothing was ever built
    /// to write.
    /// </summary>
    /// <remarks>
    /// Its own state rather than the compile one. Nothing here was ever compiled, because there
    /// was never a file to compile: the model invented the lines it claimed to be replacing and
    /// could not be talked out of it within its retry limit. Calling that a compile failure would
    /// tell somebody to go looking for a compiler error that does not exist.
    /// </remarks>
    EditDidNotApply,

    /// <summary>
    /// The file could not be read, so no change to it was ever proposed.
    /// </summary>
    /// <remarks>
    /// A model is never asked to edit a file it has not just been shown, so a file that cannot be
    /// read is a file that cannot be edited. It has moved, been deleted, or is locked by something
    /// else, and every one of those is a plan made against a project that has changed underneath
    /// it rather than anything the model did.
    /// </remarks>
    CouldNotBeRead
}

/// <summary>
/// One file a run meant to write and did not, kept with enough about it to pick up later.
/// </summary>
/// <remarks>
/// What was intended, not a snapshot of the project. By the time somebody comes back to this the
/// project may have moved underneath it: the file may have been written by hand, the type it
/// needed may now exist, the whole idea may have been abandoned. Recording the intention and the
/// reason keeps this useful in all three cases, where recording the project's state at the moment
/// of failure would be wrong in all three.
/// </remarks>
/// <param name="RelativePath">Where it was going, relative to the project root.</param>
/// <param name="TypeName">The main type it declares.</param>
/// <param name="IsNewFile">True when it would have created a file rather than changed one.</param>
/// <param name="Intent">What it was for, in the planner's words.</param>
/// <param name="Content">The best attempt reached, so the work is not thrown away.</param>
/// <param name="Reason">Why it is here.</param>
/// <param name="Detail">The compiler errors, or the refusal, in full.</param>
/// <param name="StagedAt">When the run gave up on it.</param>
public sealed record StagedFile(
    string RelativePath,
    string TypeName,
    bool IsNewFile,
    string Intent,
    string Content,
    StagedReason Reason,
    string Detail,
    DateTimeOffset StagedAt)
{
    /// <summary>One line for a list, saying what happened rather than only that it failed.</summary>
    public string Summary => Reason switch
    {
        StagedReason.RefusedByProjectRules => $"{RelativePath} was refused by the project rules",
        StagedReason.WriteFailed => $"{RelativePath} could not be written",
        StagedReason.EditDidNotApply => $"{RelativePath} could not be changed as asked",
        StagedReason.CouldNotBeRead => $"{RelativePath} could not be read",
        _ => $"{RelativePath} does not compile yet"
    };

    /// <summary>What this file is waiting for, in the words a person would use.</summary>
    public string ReasonText => Reason switch
    {
        StagedReason.RefusedByProjectRules =>
            "Refused. It compiles, and writing it would have broken something Unity binds by more than a name.",
        StagedReason.WriteFailed => "The write failed, so the file on disk is untouched.",
        StagedReason.EditDidNotApply =>
            "The coder kept asking to replace lines that are not in this file, so nothing was written. "
            + "The file on disk is untouched.",
        StagedReason.CouldNotBeRead =>
            "It could not be read, and nothing is asked to change a file it has not been shown, so no "
            + "change was proposed. The file on disk is untouched.",
        _ => "Still has compiler errors after the repair limit was spent."
    };
}
