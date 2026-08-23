using System.Windows;

namespace LocalNEXUS.App.Infrastructure.Behaviors;

/// <summary>
/// What an empty input box says when nobody has typed in it yet.
/// </summary>
/// <remarks>
/// An attached property rather than a control of its own, because the thing that has to change is
/// what an ordinary <see cref="System.Windows.Controls.TextBox"/> draws when it is empty, and every
/// box in the application already gets its appearance from the theme's template. One line in that
/// template, read from here, covers the lot; a wrapper control would have to be adopted box by box
/// and would be missed exactly where it mattered.
///
/// It is for the shape of the answer, never for the question. A label says what a field is and a
/// hint underneath says why; this says what a valid value looks like, which is the part that cannot
/// be guessed and is the whole reason somebody stares at an empty box. Three unlabelled boxes in a
/// row under one heading is the case it was written for: an address, a model id and a key are not
/// distinguishable from each other by anything except what goes in them.
///
/// Nothing here is bound to. It is read by the template and drawn over the top, so the box's own
/// value is untouched and an empty box stays empty rather than acquiring the placeholder as text,
/// which is the failure mode of doing this by writing into the field.
/// </remarks>
public static class PlaceholderBehavior
{
    /// <summary>What to show while the box is empty.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text",
        typeof(string),
        typeof(PlaceholderBehavior),
        new PropertyMetadata(string.Empty));

    /// <summary>Reads the placeholder set on an element.</summary>
    public static string GetText(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string)element.GetValue(TextProperty);
    }

    /// <summary>Sets the placeholder on an element.</summary>
    public static void SetText(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TextProperty, value);
    }
}
