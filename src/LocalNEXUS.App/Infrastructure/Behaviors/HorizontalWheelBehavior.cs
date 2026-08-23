using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace LocalNEXUS.App.Infrastructure.Behaviors;

/// <summary>
/// Lets a scroll viewer be scrolled sideways by the wheel.
/// </summary>
/// <remarks>
/// Two ways, because there are two things a person means by scrolling sideways with the wheel.
///
/// Shift and the wheel is the one every application has, and WPF does not do it: the wheel is
/// handled for vertical scrolling and the modifier is ignored, so shift scrolls up and down like
/// everything else.
///
/// Tilting the wheel left or right is the other, and WPF cannot see it at all. The tilt arrives as
/// WM_MOUSEHWHEEL, which the framework does not translate into any routed event, so the only way to
/// read it is to hook the window's messages. That is why this exists rather than being four lines
/// in a handler.
///
/// A table wider than the space it has is unusable without one of the two, and a horizontal
/// scrollbar at the very bottom of a tall table is a long way from wherever somebody is reading.
/// </remarks>
public static class HorizontalWheelBehavior
{
    /// <summary>Windows says the wheel was tilted with this.</summary>
    private const int WmMouseHorizontalWheel = 0x020E;

    /// <summary>What one notch of the wheel moves, in device independent pixels.</summary>
    /// <remarks>
    /// Three lines' worth, which is what the vertical wheel does by default, so sideways feels the
    /// same as up and down rather than like a different control.
    /// </remarks>
    private const double Notch = 48d;

    /// <summary>Turns the behaviour on for one scroll viewer.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(HorizontalWheelBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Reads whether the behaviour is on.</summary>
    public static bool GetIsEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsEnabledProperty);
    }

    /// <summary>Turns the behaviour on or off.</summary>
    public static void SetIsEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsEnabledProperty, value);
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer)
        {
            return;
        }

        viewer.PreviewMouseWheel -= OnWheel;
        viewer.Loaded -= OnLoaded;
        viewer.Unloaded -= OnUnloaded;

        if (e.NewValue is not true)
        {
            Unhook(viewer);
            return;
        }

        viewer.PreviewMouseWheel += OnWheel;
        viewer.Loaded += OnLoaded;
        viewer.Unloaded += OnUnloaded;

        if (viewer.IsLoaded)
        {
            Hook(viewer);
        }
    }

    /// <summary>Shift and the wheel, which WPF otherwise sends to the vertical scrollbar.</summary>
    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer || Keyboard.Modifiers != ModifierKeys.Shift)
        {
            return;
        }

        Scroll(viewer, e.Delta);
        e.Handled = true;
    }

    /// <summary>
    /// Moves the viewer sideways by one wheel delta.
    /// </summary>
    /// <remarks>
    /// A positive delta is away from the person, which scrolls left, matching what the same gesture
    /// does in a browser and a file list.
    /// </remarks>
    private static void Scroll(ScrollViewer viewer, int delta)
        => viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset - delta / 120d * Notch);

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer viewer)
        {
            Hook(viewer);
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer viewer)
        {
            Unhook(viewer);
        }
    }

    /// <summary>Every viewer currently listening, so a message can be given to the right one.</summary>
    /// <remarks>
    /// The hook is per window and the message carries a screen position rather than an element, so
    /// the viewer under the pointer is worked out at the moment the tilt arrives. Two tables in one
    /// window would otherwise both move.
    /// </remarks>
    private static readonly Dictionary<HwndSource, List<ScrollViewer>> Hooked = new();

    private static void Hook(ScrollViewer viewer)
    {
        if (PresentationSource.FromVisual(viewer) is not HwndSource source)
        {
            return;
        }

        if (!Hooked.TryGetValue(source, out var viewers))
        {
            viewers = new List<ScrollViewer>();
            Hooked[source] = viewers;
            source.AddHook(OnWindowMessage);
        }

        if (!viewers.Contains(viewer))
        {
            viewers.Add(viewer);
        }
    }

    private static void Unhook(ScrollViewer viewer)
    {
        foreach (var (source, viewers) in Hooked.ToList())
        {
            if (!viewers.Remove(viewer) || viewers.Count > 0)
            {
                continue;
            }

            source.RemoveHook(OnWindowMessage);
            Hooked.Remove(source);
        }
    }

    /// <summary>Reads the tilt out of the window's messages and gives it to whatever is under the pointer.</summary>
    private static IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmMouseHorizontalWheel)
        {
            return IntPtr.Zero;
        }

        // The delta is the high word of wParam, signed. Tilting right is positive, which is the
        // opposite sign from the vertical wheel, so it is negated to reach the same Scroll.
        var delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);

        // The window is looked up rather than assumed. A hook stays attached for as long as the
        // window lives, and a message can arrive while it is going away.
        if (delta == 0
            || HwndSource.FromHwnd(hwnd) is not { } source
            || !Hooked.TryGetValue(source, out var viewers))
        {
            return IntPtr.Zero;
        }

        foreach (var viewer in viewers)
        {
            if (!viewer.IsLoaded || !IsUnderPointer(viewer))
            {
                continue;
            }

            Scroll(viewer, -delta);
            handled = true;
            break;
        }

        return IntPtr.Zero;
    }

    /// <summary>True when the pointer is over this viewer right now.</summary>
    private static bool IsUnderPointer(ScrollViewer viewer)
    {
        var point = Mouse.GetPosition(viewer);

        return point.X >= 0 && point.Y >= 0
            && point.X <= viewer.ActualWidth && point.Y <= viewer.ActualHeight;
    }
}
