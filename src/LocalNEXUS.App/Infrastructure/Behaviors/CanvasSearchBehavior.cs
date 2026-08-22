using System.Windows;
using System.Windows.Input;
using LocalNEXUS.App.ViewModels;
using Nodify;

namespace LocalNEXUS.App.Infrastructure.Behaviors;

/// <summary>
/// Opens the node search where the canvas was double clicked, at the point that was clicked.
/// </summary>
/// <remarks>
/// Attached behaviour rather than code behind, which stays a call to InitializeComponent. What it
/// needs is the two things a view model cannot see: that a double click happened, and where on the
/// canvas it happened in the coordinates nodes are positioned in rather than in screen pixels.
///
/// Only a double click on the canvas itself opens it. One on a node belongs to the node, and the
/// test for that is a walk up from whatever was under the cursor looking for a node container, a
/// connection or a connector.
///
/// It was tested the other way round, by asking whether the editor itself was the original source,
/// and that is never true: the editor is a control with a template, so the deepest element under
/// the pointer is whichever part of that template was hit and never the editor. Every double click
/// on empty canvas was read as landing on a node and dropped.
///
/// The other half of the same failure is which event it listened to. The editor handles the mouse
/// button going down, for the selection rectangle and for panning, and a handled button press never
/// reaches the class handler that raises MouseDoubleClick, so that event was not arriving either.
/// The preview runs before the editor sees it, which is why the count is read from there.
/// </remarks>
public static class CanvasSearchBehavior
{
    public static readonly DependencyProperty SearchProperty = DependencyProperty.RegisterAttached(
        "Search",
        typeof(NodeSearchViewModel),
        typeof(CanvasSearchBehavior),
        new PropertyMetadata(null, OnSearchChanged));

    public static void SetSearch(DependencyObject element, NodeSearchViewModel? value)
        => element.SetValue(SearchProperty, value);

    public static NodeSearchViewModel? GetSearch(DependencyObject element)
        => (NodeSearchViewModel?)element.GetValue(SearchProperty);

    private static void OnSearchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not NodifyEditor editor)
        {
            return;
        }

        editor.PreviewMouseDown -= OnDoubleClick;
        editor.PreviewMouseUp -= OnMouseUp;

        if (e.NewValue is NodeSearchViewModel)
        {
            editor.PreviewMouseDown += OnDoubleClick;
            editor.PreviewMouseUp += OnMouseUp;
        }
    }

    /// <summary>
    /// Remembers where the pointer was, so a wire released over nothing knows where it landed.
    /// </summary>
    /// <remarks>
    /// The released wire reaches the view model as a pin and nothing else, because that is what the
    /// canvas hands to the completion command. Rather than change what a pending connection carries
    /// so a position could ride along with it, the position is written here, on the way up, from
    /// the only object that knows it.
    /// </remarks>
    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is NodifyEditor editor
            && editor.DataContext is MainViewModel main)
        {
            main.LastCanvasPoint = editor.MouseLocation;
        }
    }

    private static void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not NodifyEditor editor
            || e.ChangedButton != MouseButton.Left
            || e.ClickCount != 2
            || GetSearch(editor) is not { } search)
        {
            return;
        }

        // A double click that landed on a node belongs to the node.
        if (LandedOnSomething(e.OriginalSource as DependencyObject, editor))
        {
            return;
        }

        // The editor reports the point in its own space, which is the space node locations are in,
        // so nothing has to be converted for the placed node to appear under the cursor.
        var point = editor.MouseLocation;

        search.Open(point.X, point.Y);
        e.Handled = true;
    }

    /// <summary>
    /// True when the click landed on something the canvas already holds.
    /// </summary>
    /// <remarks>
    /// A walk up rather than a single comparison, because a node is made of many elements and any
    /// of them can be what was hit. It stops at the editor so nothing outside the canvas is
    /// considered, and it steps through the content tree as well as the visual one so a click that
    /// landed on text inside a node is still recognised as being that node's.
    /// </remarks>
    private static bool LandedOnSomething(DependencyObject? source, NodifyEditor editor)
    {
        var current = source;

        while (current is not null && !ReferenceEquals(current, editor))
        {
            if (current is ItemContainer or BaseConnection or Connector)
            {
                return true;
            }

            current = current switch
            {
                System.Windows.Media.Visual => System.Windows.Media.VisualTreeHelper.GetParent(current),
                FrameworkContentElement content => content.Parent,
                _ => null
            };
        }

        return false;
    }
}
