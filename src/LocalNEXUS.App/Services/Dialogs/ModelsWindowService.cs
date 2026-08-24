using System.Windows;
using LocalNEXUS.App.Views;

namespace LocalNEXUS.App.Services.Dialogs;

/// <summary>
/// Owns the one model browser window and where it sits.
/// </summary>
/// <remarks>
/// One window, reused. Opening it twice brings the existing one forward rather than stacking a
/// second copy of the same search, which matters more here than elsewhere: a second copy would
/// have its own idea of what is downloading.
///
/// It is not modal, so the graph stays workable while it is open. That is the point of it being a
/// window: a download runs for minutes and nobody should have to sit and watch it.
///
/// Where it sits is remembered for the session and not written to disk. Reopening it puts it back
/// where it was, and a restart starts it centred again, which is the behaviour of a window that
/// belongs to a piece of work rather than to the application.
/// </remarks>
public sealed class ModelsWindowService : IModelsWindow
{
    private ModelsWindow? _window;

    private double? _left;
    private double? _top;
    private double _width = 880d;
    private double _height = 720d;

    /// <inheritdoc />
    public void Show(object viewModel)
    {
        if (_window is not null)
        {
            if (_window.WindowState == WindowState.Minimized)
            {
                _window.WindowState = WindowState.Normal;
            }

            _window.Activate();
            return;
        }

        var window = new ModelsWindow
        {
            DataContext = viewModel,
            Owner = Application.Current?.MainWindow,
            Width = _width,
            Height = _height
        };

        if (_left is { } left && _top is { } top)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = left;
            window.Top = top;
        }

        window.Closing += (_, _) =>
        {
            // Read the placement back before it goes, because a closed window reports nothing.
            if (window.WindowState == WindowState.Normal)
            {
                _left = window.Left;
                _top = window.Top;
                _width = window.Width;
                _height = window.Height;
            }

            _window = null;
        };

        _window = window;
        window.Show();
    }

    /// <inheritdoc />
    public void Close()
    {
        _window?.Close();
        _window = null;
    }
}
