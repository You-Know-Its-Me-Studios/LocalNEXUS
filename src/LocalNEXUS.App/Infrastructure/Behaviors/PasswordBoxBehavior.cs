using System.Windows;
using System.Windows.Controls;

namespace LocalNEXUS.App.Infrastructure.Behaviors;

/// <summary>
/// Makes what was typed into a password box readable by a binding.
/// </summary>
/// <remarks>
/// <see cref="PasswordBox.Password"/> is a plain property and not a dependency property, on purpose:
/// a bound one would leave the password sitting in the binding engine. So it cannot be bound, and a
/// binding written against it does not fail loudly. It resolves to nothing and the command it feeds
/// is handed null.
///
/// That is what was happening to every key in this application. Save was wired to
/// <c>{Binding Password, ElementName=KeyBox}</c>, which meant Save was storing null, which
/// <c>SetKey</c> reads as clear it. Somebody pasted a key, pressed Save, and watched the status keep
/// saying there was no key, because there was no key: pressing Save had removed it.
///
/// An attached property mirrors the value out, which is the standard way round this and keeps the
/// code behind a call to InitializeComponent. The mirror is one way out of the box and one way back
/// in only when something else sets it, so clearing the bound value clears the box and typing does
/// not fight the binding.
/// </remarks>
public static class PasswordBoxBehavior
{
    /// <summary>Turns the mirroring on for a box.</summary>
    public static readonly DependencyProperty IsBoundProperty = DependencyProperty.RegisterAttached(
        "IsBound",
        typeof(bool),
        typeof(PasswordBoxBehavior),
        new PropertyMetadata(false, OnIsBoundChanged));

    public static void SetIsBound(DependencyObject element, bool value) => element.SetValue(IsBoundProperty, value);

    public static bool GetIsBound(DependencyObject element) => (bool)element.GetValue(IsBoundProperty);

    /// <summary>What is currently in the box, for a command parameter to read.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text",
        typeof(string),
        typeof(PasswordBoxBehavior),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnTextChanged));

    public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);

    public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);

    /// <summary>Guards the two directions from chasing each other.</summary>
    private static readonly DependencyProperty UpdatingProperty = DependencyProperty.RegisterAttached(
        "Updating",
        typeof(bool),
        typeof(PasswordBoxBehavior),
        new PropertyMetadata(false));

    private static void OnIsBoundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box)
        {
            return;
        }

        box.PasswordChanged -= OnPasswordChanged;

        if (e.NewValue is true)
        {
            box.PasswordChanged += OnPasswordChanged;
        }
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box)
        {
            return;
        }

        box.SetValue(UpdatingProperty, true);
        SetText(box, box.Password);
        box.SetValue(UpdatingProperty, false);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Only when something other than the box itself set it, which is how a Clear empties the
        // box without a keystroke putting the old value back a moment later.
        if (d is not PasswordBox box || (bool)box.GetValue(UpdatingProperty))
        {
            return;
        }

        var wanted = e.NewValue as string ?? string.Empty;

        if (!string.Equals(box.Password, wanted, StringComparison.Ordinal))
        {
            box.Password = wanted;
        }
    }
}
