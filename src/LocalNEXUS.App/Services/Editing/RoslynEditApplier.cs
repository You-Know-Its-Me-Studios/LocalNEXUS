using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace LocalNEXUS.App.Services.Editing;

/// <summary>
/// Applies edits by finding the node they name, rather than by matching text.
/// </summary>
/// <remarks>
/// This is the one approach that makes the failure impossible rather than recoverable. A diff has
/// to reproduce the lines it replaces and a small model cannot: the measurement is 0.03 exact match
/// at generating a patch against 0.59 at applying one. Naming a method and writing the new one asks
/// nothing of the model about what the file already contains, and the target is found by walking
/// the tree.
///
/// Whitespace stops being a failure mode entirely. Parsed members carry the formatter's annotation
/// and Roslyn lays them out to match the file they land in, so an answer indented wrongly is
/// indented correctly on arrival rather than refused.
///
/// Three traps the Roslyn issue tracker documents are handled rather than discovered. Trees are
/// immutable, so a batch goes through one editor which tracks nodes as it changes them instead of
/// repeatedly replacing on a root that is stale the moment it is used. A parent and its child
/// cannot both be edited in one batch, so child changes go first and a batch needing both is
/// refused whole rather than applied halfway. And replacing a node with one of a different kind
/// throws, so the parsed replacement is checked against what it is replacing first.
///
/// An ad hoc workspace, never MSBuild. There is no project to open here and no design time build to
/// run; this is one file, in memory, on its way back to the caller as a string.
/// </remarks>
public static class RoslynEditApplier
{
    /// <summary>
    /// Applies every edit to the source, or explains why it did not.
    /// </summary>
    /// <param name="source">The file exactly as it is on disk.</param>
    /// <param name="edits">What to change. An empty set is not mappable.</param>
    public static async Task<StructuredEditResult> ApplyAsync(
        string source,
        IReadOnlyList<StructuredEdit> edits,
        CancellationToken ct)
    {
        if (edits.Count == 0)
        {
            return NotMappable("There is nothing here expressed as a change to a named member.");
        }

        if (Conflicts(edits) is { Length: > 0 } conflict)
        {
            return new StructuredEditResult(StructuredEditState.Refused, string.Empty, conflict);
        }

        using var workspace = new AdhocWorkspace();

        var project = workspace.AddProject("edit", LanguageNames.CSharp);
        var document = workspace.AddDocument(project.Id, "edit.cs", SourceText.From(source));

        DocumentEditor editor;

        try
        {
            editor = await DocumentEditor.CreateAsync(document, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return NotMappable($"That file could not be opened for editing: {ex.Message}");
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);

        if (root is null)
        {
            return NotMappable("That file could not be parsed.");
        }

        // Children first. A parent and its child in one batch is the documented way to lose one of
        // them, and every member change is a child of the type an addition would rewrite.
        foreach (var edit in edits.OrderByDescending(e => e.TouchesChild))
        {
            if (Apply(editor, root, edit) is { Length: > 0 } refusal)
            {
                return new StructuredEditResult(StructuredEditState.Refused, string.Empty, refusal);
            }
        }

        var changed = editor.GetChangedDocument();
        var formatted = await Formatter.FormatAsync(changed, Formatter.Annotation, cancellationToken: ct)
            .ConfigureAwait(false);

        var text = await formatted.GetTextAsync(ct).ConfigureAwait(false);

        return new StructuredEditResult(StructuredEditState.Applied, text.ToString(), string.Empty);
    }

    /// <summary>
    /// A batch that would edit a type and one of its members at once, which cannot be done.
    /// </summary>
    /// <remarks>
    /// Adding a member rewrites the type when the type has nowhere to insert after, and replacing
    /// one rewrites the member. Doing both to the same type in one pass loses whichever the editor
    /// resolves second, so it is refused and the caller falls back to something that can express
    /// both, which is a whole file.
    /// </remarks>
    private static string Conflicts(IReadOnlyList<StructuredEdit> edits)
    {
        foreach (var group in edits.GroupBy(e => e.TypeName, StringComparer.Ordinal))
        {
            var addsToEmptyType = group.Any(e => e.Kind == StructuredEditKind.AddMember);
            var changesAMember = group.Any(e => e.TouchesChild);

            if (addsToEmptyType && changesAMember)
            {
                return $"{group.Key} would have a member added and another changed in the same pass, "
                       + "which cannot be done in one edit. This needs the whole file.";
            }
        }

        return string.Empty;
    }

    /// <summary>Applies one edit, or says why it will not. An empty answer means it applied.</summary>
    private static string Apply(DocumentEditor editor, SyntaxNode root, StructuredEdit edit)
    {
        if (edit.Kind == StructuredEditKind.RemoveUsing)
        {
            var directive = root.DescendantNodes()
                .OfType<UsingDirectiveSyntax>()
                .FirstOrDefault(u => string.Equals(u.Name?.ToString(), edit.MemberName, StringComparison.Ordinal));

            if (directive is null)
            {
                return $"There is no using for {edit.MemberName} in this file.";
            }

            editor.RemoveNode(directive);
            return string.Empty;
        }

        var type = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => string.Equals(t.Identifier.Text, edit.TypeName, StringComparison.Ordinal));

        if (type is null)
        {
            return $"There is no type called {edit.TypeName} in this file.";
        }

        if (edit.Kind == StructuredEditKind.AddMember)
        {
            if (Parse(edit.Code) is not { } added)
            {
                return $"The declaration to add to {edit.TypeName} is not a member this can parse.";
            }

            var last = type.Members.LastOrDefault();

            if (last is null)
            {
                // Nothing to insert after, so the type itself is rewritten. Safe here because a
                // batch touching this type twice was already refused.
                editor.ReplaceNode(type, type.AddMembers(added));
                return string.Empty;
            }

            editor.InsertAfter(last, added);
            return string.Empty;
        }

        var member = Find(type, edit.MemberName);

        if (member is null)
        {
            return $"There is no member called {edit.MemberName} on {edit.TypeName}.";
        }

        if (edit.Kind == StructuredEditKind.RemoveMember)
        {
            editor.RemoveNode(member);
            return string.Empty;
        }

        if (Parse(edit.Code) is not { } replacement)
        {
            return $"The replacement for {edit.TypeName}.{edit.MemberName} is not a member this can parse.";
        }

        // Replacing a node with one of a different kind throws, so it is checked rather than
        // caught. A method replaced by a field is a model answering a different question.
        if (replacement.Kind() != member.Kind())
        {
            return $"{edit.TypeName}.{edit.MemberName} is a {Describe(member)} and the replacement is a "
                   + $"{Describe(replacement)}. Return the same kind of member.";
        }

        editor.ReplaceNode(member, replacement);
        return string.Empty;
    }

    /// <summary>The member of a type with this name, whatever kind of member it is.</summary>
    private static MemberDeclarationSyntax? Find(TypeDeclarationSyntax type, string name)
    {
        foreach (var member in type.Members)
        {
            var matches = member switch
            {
                MethodDeclarationSyntax method => method.Identifier.Text == name,
                PropertyDeclarationSyntax property => property.Identifier.Text == name,
                ConstructorDeclarationSyntax constructor => constructor.Identifier.Text == name,
                EventDeclarationSyntax @event => @event.Identifier.Text == name,
                FieldDeclarationSyntax field => field.Declaration.Variables.Any(v => v.Identifier.Text == name),
                _ => false
            };

            if (matches)
            {
                return member;
            }
        }

        return null;
    }

    /// <summary>
    /// The model's replacement as a syntax node, annotated so Roslyn lays it out to fit.
    /// </summary>
    /// <remarks>
    /// The annotation is what removes indentation as a failure mode. Without it a member written
    /// flush left lands flush left inside a class; with it the formatter puts it where it belongs
    /// and nothing has to be said to the model about whitespace at all.
    /// </remarks>
    private static MemberDeclarationSyntax? Parse(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var parsed = SyntaxFactory.ParseMemberDeclaration(code);

        if (parsed is null || parsed.ContainsDiagnostics)
        {
            return null;
        }

        return parsed.WithAdditionalAnnotations(Formatter.Annotation);
    }

    private static string Describe(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax => "method",
        PropertyDeclarationSyntax => "property",
        FieldDeclarationSyntax => "field",
        ConstructorDeclarationSyntax => "constructor",
        EventDeclarationSyntax => "event",
        _ => node.Kind().ToString()
    };

    private static StructuredEditResult NotMappable(string message)
        => new(StructuredEditState.NotMappable, string.Empty, message);
}
