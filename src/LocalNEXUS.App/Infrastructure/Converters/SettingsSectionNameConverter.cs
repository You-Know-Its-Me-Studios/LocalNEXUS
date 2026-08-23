using System.Globalization;
using System.Text;
using System.Windows.Data;

namespace LocalNEXUS.App.Infrastructure.Converters;

/// <summary>
/// A settings section as a person would read it rather than as it is spelled in code.
/// </summary>
/// <remarks>
/// The rail printed the enum value, which was fine for as long as every one of them was a single
/// word and became ApiKeys the moment one was not. Splitting on the capitals and lowercasing what
/// follows the first word gives sentence case, which is what everything else in this application
/// is written in.
///
/// Sentence case is wrong for an initialism, though, and splitting gave the rail "Api keys" while
/// the page it opened said "API keys". Anything the rule cannot spell is written out here instead,
/// which is one line per exception rather than a rule that tries to recognise one.
/// </remarks>
public sealed class SettingsSectionNameConverter : IValueConverter
{
    /// <summary>Sections whose name is not something splitting on capitals can spell.</summary>
    private static readonly Dictionary<string, string> Spelled = new(StringComparer.Ordinal)
    {
        ["ApiKeys"] = "API keys"
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value?.ToString() is not { Length: > 0 } name)
        {
            return string.Empty;
        }

        if (Spelled.TryGetValue(name, out var spelled))
        {
            return spelled;
        }

        var text = new StringBuilder();

        foreach (var c in name)
        {
            if (char.IsUpper(c) && text.Length > 0)
            {
                text.Append(' ').Append(char.ToLowerInvariant(c));
                continue;
            }

            text.Append(c);
        }

        return text.ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("SettingsSectionNameConverter is a one way converter.");
}
