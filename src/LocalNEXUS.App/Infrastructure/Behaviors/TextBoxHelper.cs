using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using LocalNEXUS.App.Services.Theming;

namespace LocalNEXUS.App.Infrastructure.Behaviors;

/// <summary>
/// Placeholder text for an input that has none of its own.
/// </summary>
/// <remarks>
/// An attached property and an adorner, which is the way WPF does this. The adorner is a visual
/// bound to the control and rendered in the adorner layer above it, so the control's own text is
/// untouched: an empty box is genuinely empty, the value that reaches a view model is never the
/// example, and nothing has to be cleared out before typing.
///
/// It is for the shape of the answer and never a repeat of the question. A label says what a field
/// is; this says what a valid value looks like, which is the part that cannot be guessed and is the
/// whole reason somebody stares at an empty box. Three unlabelled boxes in a row under one heading
/// was the case it was written for: an address, a model id and a key are told apart by what goes in
/// them and by nothing else.
///
/// Password boxes are covered as well as text boxes. They are the input where the shape of the
/// value is least guessable and, because the characters are masked, the one where an example can
/// only ever be shown this way.
/// </remarks>
public static class TextBoxHelper
{
    /// <summary>The example shown while the input is empty.</summary>
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder",
        typeof(string),
        typeof(TextBoxHelper),
        new FrameworkPropertyMetadata(defaultValue: null, propertyChangedCallback: OnPlaceholderChanged));

    /// <summary>Reads the placeholder set on an input.</summary>
    public static string GetPlaceholder(DependencyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return (string)obj.GetValue(PlaceholderProperty);
    }

    /// <summary>Sets the placeholder on an input.</summary>
    public static void SetPlaceholder(DependencyObject obj, string value)
    {
        ArgumentNullException.ThrowIfNull(obj);
        obj.SetValue(PlaceholderProperty, value);
    }

    /// <summary>
    /// Hooks the input up the first time a placeholder is put on it.
    /// </summary>
    /// <remarks>
    /// Called both when the property is first attached and whenever it changes, which is why the
    /// handlers are removed before being added. The adorner cannot be made here on a control that
    /// has not loaded, because until the template is applied there is no adorner layer to put it
    /// in, so that case waits for Loaded and unhooks itself once it has run.
    /// </remarks>
    private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control input || input is not (TextBox or PasswordBox))
        {
            return;
        }

        if (!input.IsLoaded)
        {
            input.Loaded -= OnLoaded;
            input.Loaded += OnLoaded;
        }

        if (input is TextBox textBox)
        {
            textBox.TextChanged -= OnTextChanged;
            textBox.TextChanged += OnTextChanged;
        }
        else if (input is PasswordBox passwordBox)
        {
            passwordBox.PasswordChanged -= OnPasswordChanged;
            passwordBox.PasswordChanged += OnPasswordChanged;
        }

        if (TryGetAdorner(input, out var adorner))
        {
            adorner.Visibility = IsEmpty(input) ? Visibility.Visible : Visibility.Hidden;
            adorner.InvalidateVisual();
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Control input)
        {
            input.Loaded -= OnLoaded;

            // Asked at load as well as on every change, because a box that arrives with a value
            // already in it never raises a change and would wear the example over the top of it.
            Show(input, IsEmpty(input));
        }
    }

    /// <summary>True when there is nothing in the input, whichever kind it is.</summary>
    private static bool IsEmpty(Control input) => input switch
    {
        TextBox textBox => textBox.Text.Length == 0,
        PasswordBox passwordBox => passwordBox.Password.Length == 0,
        _ => false
    };

    private static void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            Show(textBox, textBox.Text.Length == 0);
        }
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
        {
            Show(passwordBox, passwordBox.Password.Length == 0);
        }
    }

    /// <summary>Shows the example while the input is empty, and takes it away the moment it is not.</summary>
    private static void Show(Control input, bool empty)
    {
        if (TryGetAdorner(input, out var adorner))
        {
            adorner.Visibility = empty ? Visibility.Visible : Visibility.Hidden;
        }
    }

    /// <summary>
    /// Finds the input's adorner, adding one the first time.
    /// </summary>
    /// <remarks>
    /// Returns false rather than throwing when there is no adorner layer. An attached property is
    /// applied before the control's template has built its visual tree, so at that point there is
    /// nothing to add an adorner to, and a template that leaves the layer out is allowed to. A
    /// missing example is a smaller problem than a window that will not open.
    /// </remarks>
    private static bool TryGetAdorner(Control input, out PlaceholderAdorner adorner)
    {
        var layer = AdornerLayer.GetAdornerLayer(input);

        if (layer is null)
        {
            adorner = null!;
            return false;
        }

        var existing = layer.GetAdorners(input)?.OfType<PlaceholderAdorner>().FirstOrDefault();

        if (existing is null)
        {
            existing = new PlaceholderAdorner(input);
            layer.Add(existing);
        }

        adorner = existing;
        return true;
    }

    /// <summary>
    /// Draws the example over an empty input, in the input's own face and where its own text goes.
    /// </summary>
    /// <remarks>
    /// Positioned from PART_ContentHost rather than from the control's padding alone, so the
    /// example sits exactly where the first typed character will and does not shift when it is
    /// replaced by real text.
    ///
    /// The colour comes from the palette rather than from a system brush or a literal. A theme
    /// change repaints the live brush in place, and this reads it at render time, so an example
    /// drawn under one theme is not left in the previous theme's grey.
    /// </remarks>
    public sealed class PlaceholderAdorner : Adorner
    {
        public PlaceholderAdorner(Control input)
            : base(input) => IsHitTestVisible = false;

        protected override void OnRender(DrawingContext drawingContext)
        {
            ArgumentNullException.ThrowIfNull(drawingContext);

            var input = (Control)AdornedElement;
            var placeholder = GetPlaceholder(input);

            if (string.IsNullOrEmpty(placeholder))
            {
                return;
            }

            var text = new FormattedText(
                placeholder,
                CultureInfo.CurrentCulture,
                input.FlowDirection,
                new Typeface(input.FontFamily, input.FontStyle, input.FontWeight, input.FontStretch),
                input.FontSize,
                ThemePalette.Get("Text.Muted.Brush") ?? SystemColors.GrayTextBrush,
                VisualTreeHelper.GetDpi(input).PixelsPerDip);

            text.MaxTextWidth = Math.Max(input.ActualWidth - input.Padding.Left - input.Padding.Right, 10d);
            text.MaxTextHeight = Math.Max(input.ActualHeight, 10d);

            var offset = new Point(input.Padding.Left, input.Padding.Top);

            if (input.Template?.FindName("PART_ContentHost", input) is FrameworkElement part)
            {
                var position = part.TransformToAncestor(input).Transform(new Point(0d, 0d));

                offset.X += position.X;
                offset.Y += position.Y;

                text.MaxTextWidth = Math.Max(part.ActualWidth - input.Padding.Left - input.Padding.Right, 10d);
                text.MaxTextHeight = Math.Max(part.ActualHeight, 10d);
            }

            drawingContext.DrawText(text, offset);
        }
    }
}
