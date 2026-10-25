using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VacationCalculator.App.Behaviors;

/// <summary>
/// Attached behaviour for a ScrollViewer (or a control containing one, such as a DataGrid) nested in another scroll area:
/// when the inner area is already at its top or bottom, the mouse wheel is passed on to the outer area
/// instead of being swallowed.
/// </summary>
public static class ScrollChaining
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(ScrollChaining),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.PreviewMouseWheel -= OnPreviewMouseWheel;
        if ((bool)e.NewValue) element.PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not DependencyObject root) return;
        var scroll = root as ScrollViewer ?? FindScrollViewer(root);
        if (scroll is null) return;

        var atTop = scroll.VerticalOffset <= 0;
        var atBottom = scroll.VerticalOffset >= scroll.ScrollableHeight;
        if (!(e.Delta > 0 && atTop) && !(e.Delta < 0 && atBottom)) return;

        // Hand the wheel to whatever contains this control; the outer ScrollViewer then scrolls as usual.
        if (VisualTreeHelper.GetParent(root) is not UIElement parent) return;
        e.Handled = true;
        parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender,
        });
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv) return sv;
            if (FindScrollViewer(child) is { } found) return found;
        }
        return null;
    }
}
