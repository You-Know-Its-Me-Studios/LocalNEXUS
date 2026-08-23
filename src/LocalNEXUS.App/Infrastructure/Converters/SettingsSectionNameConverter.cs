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
/// </remarks>
public sealed class SettingsSectionNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value?.ToString() is not { Length: > 0 } name)
        {
            return string.Empty;
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
