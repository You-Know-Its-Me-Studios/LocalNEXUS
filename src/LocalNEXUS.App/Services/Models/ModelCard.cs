using System.Text;
using System.Text.RegularExpressions;

namespace LocalNEXUS.App.Services.Models;

/// <summary>What one line of a model card is.</summary>
public enum CardBlockKind
{
    /// <summary>Ordinary prose.</summary>
    Paragraph,

    /// <summary>A heading, at the depth the card asked for.</summary>
    Heading,

    /// <summary>A line of a list.</summary>
    Bullet,

    /// <summary>Preformatted, shown in the monospace face and never wrapped.</summary>
    Code,

    /// <summary>A rule between sections.</summary>
    Rule
}

/// <summary>One renderable piece of a model card.</summary>
/// <param name="Kind">What it is, which decides how it is drawn.</param>
/// <param name="Text">The text, with inline markup already removed.</param>
/// <param name="Level">Heading depth, one to six. Zero for everything else.</param>
public sealed record CardBlock(CardBlockKind Kind, string Text, int Level = 0);

/// <summary>
/// Turns a Hugging Face model card into something that can be drawn.
/// </summary>
/// <remarks>
/// Deliberately small, and deliberately not a markdown library. What is being rendered is one
/// well known kind of document, read only, in a side panel: headings, paragraphs, lists, code and
/// rules. Taking a dependency to render that would be carrying a general parser to do a specific
/// job, and every model card that has ever appeared here is inside this subset.
///
/// What it does not do is as deliberate. No HTML, which model cards do contain and which would be
/// a way to draw whatever an author wanted inside this application: tags are stripped rather than
/// interpreted. No image loading, because the card is a description and fetching whatever it
/// points at is a request this application did not decide to make. Links keep their text and lose
/// their target, because a link nobody can click cannot mislead anybody about where it goes.
///
/// The front matter is kept rather than hidden. It is where the licence and the base model are
/// declared, which is exactly what somebody about to download several gigabytes is entitled to
/// see, and it renders as code because that is what it is.
/// </remarks>
public static class ModelCard
{
    private static readonly Regex Heading = new(@"^(?<hashes>#{1,6})\s+(?<text>.*)$", RegexOptions.Compiled);
    private static readonly Regex Bullet = new(@"^\s*([-*+]|\d+\.)\s+(?<text>.*)$", RegexOptions.Compiled);
    private static readonly Regex Rule = new(@"^\s*([-*_])\1{2,}\s*$", RegexOptions.Compiled);

    private static readonly Regex Link = new(@"\[(?<text>[^\]]*)\]\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex Image = new(@"!\[(?<alt>[^\]]*)\]\([^)]*\)", RegexOptions.Compiled);
    /// <summary>
    /// Any tag, of any length, across any number of lines.
    /// </summary>
    /// <remarks>
    /// Length bounded at two hundred characters first, which looked reasonable and was wrong: a
    /// real card carried an img tag with a signed URL in it, well past that, and the whole tag
    /// was rendered as prose. Found by looking at what a live card produced rather than by reading
    /// this back. There is nothing to bound here: a tag is a run of characters that are not angle
    /// brackets, and matching one is linear whatever its length.
    /// </remarks>
    private static readonly Regex Html = new(
        @"<[^<>]*>",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Emphasis = new(@"(\*\*|__|\*|_|`)", RegexOptions.Compiled);

    /// <summary>
    /// Breaks a card into blocks, in the order they appear.
    /// </summary>
    /// <remarks>
    /// Blank lines separate paragraphs and nothing else; a paragraph broken across three lines in
    /// the source is one paragraph here, because that is how markdown reads and how the card was
    /// meant to look.
    /// </remarks>
    public static IReadOnlyList<CardBlock> Parse(string? card)
    {
        var blocks = new List<CardBlock>();

        if (string.IsNullOrWhiteSpace(card))
        {
            return blocks;
        }

        var lines = card.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var paragraph = new StringBuilder();
        var fenced = new StringBuilder();
        var inFence = false;

        // Front matter is a fence in everything but name, and opens on the first line only.
        var inFrontMatter = lines.Length > 0 && lines[0].Trim() == "---";

        void FlushParagraph()
        {
            if (paragraph.Length == 0)
            {
                return;
            }

            blocks.Add(new CardBlock(CardBlockKind.Paragraph, paragraph.ToString().Trim()));
            paragraph.Clear();
        }

        void FlushFence()
        {
            if (fenced.Length == 0)
            {
                return;
            }

            blocks.Add(new CardBlock(CardBlockKind.Code, fenced.ToString().TrimEnd()));
            fenced.Clear();
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmed = line.Trim();

            if (inFrontMatter)
            {
                if (index > 0 && trimmed == "---")
                {
                    inFrontMatter = false;
                    FlushFence();
                }
                else if (index > 0)
                {
                    fenced.AppendLine(line);
                }

                continue;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                if (inFence)
                {
                    FlushFence();
                }
                else
                {
                    FlushParagraph();
                }

                inFence = !inFence;
                continue;
            }

            if (inFence)
            {
                fenced.AppendLine(line);
                continue;
            }

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            if (Rule.IsMatch(trimmed))
            {
                FlushParagraph();
                blocks.Add(new CardBlock(CardBlockKind.Rule, string.Empty));
                continue;
            }

            if (Heading.Match(trimmed) is { Success: true } heading)
            {
                FlushParagraph();

                blocks.Add(new CardBlock(
                    CardBlockKind.Heading,
                    Inline(heading.Groups["text"].Value),
                    heading.Groups["hashes"].Value.Length));

                continue;
            }

            if (Bullet.Match(line) is { Success: true } bullet)
            {
                FlushParagraph();
                blocks.Add(new CardBlock(CardBlockKind.Bullet, Inline(bullet.Groups["text"].Value)));
                continue;
            }

            if (paragraph.Length > 0)
            {
                paragraph.Append(' ');
            }

            paragraph.Append(Inline(trimmed));
        }

        FlushParagraph();
        FlushFence();

        return blocks;
    }

    /// <summary>
    /// Strips inline markup, keeping the words.
    /// </summary>
    /// <remarks>
    /// Images go first and become their alt text, because an image whose alt text is empty leaves
    /// nothing rather than a stray bracket. Then links keep their text. Then any HTML tag is
    /// removed. Emphasis markers go last, once nothing that could contain them is left.
    /// </remarks>
    public static string Inline(string text)
    {
        var work = Image.Replace(text, match => match.Groups["alt"].Value);
        work = Link.Replace(work, match => match.Groups["text"].Value);
        work = Html.Replace(work, string.Empty);
        work = Emphasis.Replace(work, string.Empty);

        return work.Trim();
    }
}
