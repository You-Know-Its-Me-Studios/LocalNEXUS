using System.Text;

namespace LocalNEXUS.App.Services.Editing;

/// <summary>
/// Reads a reply written as named changes rather than as a diff.
/// </summary>
/// <remarks>
/// The format asks a model for the two things it can actually produce: the name of the thing it is
/// changing, and the new version of that thing. There are no context lines to reproduce, no line
/// prefixes to keep straight, and no indentation to match, which between them are most of what a
/// small model gets wrong about a diff.
///
/// <code>
/// @replace Basket.Total
/// public decimal Total { get; set; }
/// @add Basket.Clear
/// public void Clear() { Count = 0; }
/// @remove Basket.Legacy
/// @remove-using System.Linq
/// </code>
///
/// A reply that is not in this shape is not an error. It is a reply in another format, and the
/// caller goes on to treat it as a whole file or a diff exactly as before.
/// </remarks>
public static class StructuredEditParser
{
    private const string Replace = "@replace";
    private const string Add = "@add";
    private const string Remove = "@remove";
    private const string RemoveUsing = "@remove-using";

    /// <summary>True when a reply is written as named changes.</summary>
    public static bool LooksStructured(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return false;
        }

        foreach (var raw in reply.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.TrimStart();

            if (line.StartsWith(Replace, StringComparison.OrdinalIgnoreCase)
                || line.StartsWith(Add, StringComparison.OrdinalIgnoreCase)
                || line.StartsWith(RemoveUsing, StringComparison.OrdinalIgnoreCase)
                || line.StartsWith(Remove, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The edits a reply names, in the order it named them.
    /// </summary>
    /// <remarks>
    /// A directive naming nothing is skipped rather than guessed at, and a reply of nothing but
    /// unusable directives comes back empty, which the applier reports as nothing it can map.
    /// </remarks>
    public static IReadOnlyList<StructuredEdit> Parse(string reply)
    {
        var edits = new List<StructuredEdit>();

        StructuredEditKind? kind = null;
        var type = string.Empty;
        var member = string.Empty;
        var code = new StringBuilder();

        void Flush()
        {
            if (kind is not { } current)
            {
                return;
            }

            edits.Add(new StructuredEdit(current, type, member, code.ToString().Trim()));

            kind = null;
            code.Clear();
        }

        foreach (var raw in reply.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            var trimmed = line.TrimStart();

            if (!trimmed.StartsWith('@'))
            {
                if (kind is not null)
                {
                    code.AppendLine(line);
                }

                continue;
            }

            Flush();

            // Longest first, so a using removal is not read as a member removal.
            if (Directive(trimmed, RemoveUsing) is { } usingName)
            {
                edits.Add(new StructuredEdit(StructuredEditKind.RemoveUsing, string.Empty, usingName, string.Empty));
                continue;
            }

            if (Directive(trimmed, Replace) is { } replaced && Split(replaced) is var (rt, rm) && rm.Length > 0)
            {
                kind = StructuredEditKind.ReplaceMember;
                type = rt;
                member = rm;
                continue;
            }

            if (Directive(trimmed, Add) is { } addedTo && Split(addedTo) is var (at, am) && at.Length > 0)
            {
                kind = StructuredEditKind.AddMember;
                type = at;
                member = am;
                continue;
            }

            if (Directive(trimmed, Remove) is { } removed && Split(removed) is var (dt, dm) && dm.Length > 0)
            {
                edits.Add(new StructuredEdit(StructuredEditKind.RemoveMember, dt, dm, string.Empty));
            }
        }

        Flush();

        return edits;
    }

    /// <summary>The argument of a directive, or null when the line is a different one.</summary>
    private static string? Directive(string line, string name)
    {
        if (!line.StartsWith(name, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = line[name.Length..];

        // A directive has to be followed by a space, so @remove-using is never read as @remove.
        return rest.Length == 0 || char.IsWhiteSpace(rest[0]) ? rest.Trim() : null;
    }

    /// <summary>Splits Type.Member, tolerating a namespace in front of the type.</summary>
    private static (string Type, string Member) Split(string target)
    {
        var at = target.LastIndexOf('.');

        if (at <= 0 || at == target.Length - 1)
        {
            return (target.Trim(), string.Empty);
        }

        var type = target[..at].Trim();
        var dot = type.LastIndexOf('.');

        return (dot >= 0 ? type[(dot + 1)..] : type, target[(at + 1)..].Trim());
    }
}
