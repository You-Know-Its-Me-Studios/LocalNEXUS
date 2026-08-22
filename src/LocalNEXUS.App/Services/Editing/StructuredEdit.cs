namespace LocalNEXUS.App.Services.Editing;

/// <summary>What a structured edit does to a file.</summary>
/// <remarks>
/// Four, and every one of them names a syntax node rather than a piece of text. That is the whole
/// point: there is no search text, so there is nothing to get wrong about what the file contains.
/// </remarks>
public enum StructuredEditKind
{
    /// <summary>Replace a member of a type with a new declaration of it.</summary>
    ReplaceMember,

    /// <summary>Add a member to a type.</summary>
    AddMember,

    /// <summary>Remove a member from a type.</summary>
    RemoveMember,

    /// <summary>Remove a using directive from the top of the file.</summary>
    RemoveUsing
}

/// <summary>
/// One change expressed as what to change rather than as what text to find.
/// </summary>
/// <remarks>
/// A model asked for a diff has to reproduce the lines it is replacing from memory, and the
/// measurements say small models cannot: 0.03 exact match at generating a patch. A model asked to
/// name the method it is changing and then write the new one has nothing to reproduce. The target
/// is found by walking the syntax tree, so there is no fuzzy matching anywhere and none is needed.
/// </remarks>
/// <param name="Kind">What is being done.</param>
/// <param name="TypeName">The type holding the member, as written in the file.</param>
/// <param name="MemberName">The member, or the using's name for a using removal.</param>
/// <param name="Code">The new declaration, for the kinds that add one. Empty otherwise.</param>
public sealed record StructuredEdit(
    StructuredEditKind Kind,
    string TypeName,
    string MemberName,
    string Code)
{
    /// <summary>True when this edit changes a member rather than the type that holds it.</summary>
    /// <remarks>
    /// The ordering rule the Roslyn issue tracker documents: a parent and its child cannot both be
    /// edited in one batch, so the child changes are applied first and a batch that would need both
    /// is refused rather than half applied.
    /// </remarks>
    public bool TouchesChild => Kind is StructuredEditKind.ReplaceMember or StructuredEditKind.RemoveMember;

    public override string ToString() => Kind switch
    {
        StructuredEditKind.ReplaceMember => $"replace {TypeName}.{MemberName}",
        StructuredEditKind.AddMember => $"add {MemberName} to {TypeName}",
        StructuredEditKind.RemoveMember => $"remove {TypeName}.{MemberName}",
        _ => $"remove using {MemberName}"
    };
}

/// <summary>Whether a structured edit could be applied, and why not when it could not.</summary>
public enum StructuredEditState
{
    /// <summary>Every edit was applied and the result is the new file.</summary>
    Applied,

    /// <summary>Nothing here maps to a syntax node, so something else should handle it.</summary>
    NotMappable,

    /// <summary>It mapped and the edit itself would not hold together.</summary>
    Refused
}

/// <summary>What applying a set of structured edits produced.</summary>
/// <param name="State">Which of the three answers this is.</param>
/// <param name="Content">The new file, when it applied. Empty otherwise.</param>
/// <param name="Message">What went wrong, worded for a person.</param>
public sealed record StructuredEditResult(StructuredEditState State, string Content, string Message)
{
    /// <summary>True when there is a new file to use.</summary>
    public bool IsApplied => State == StructuredEditState.Applied;
}
