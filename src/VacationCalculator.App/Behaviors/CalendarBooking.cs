using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Behaviors;

/// <summary>
/// Attached behaviour for the element that contains the calendar's month cards: turns mouse presses on day cells into
/// <see cref="CalendarGesture"/>s. Left button = Vacation, right button = Feriefri. While the button is held and the
/// pointer is over another day, <see cref="PreviewCommand"/> gets the gesture (null to clear it); on release
/// <see cref="GestureCommand"/> gets it. Released outside a day, or Escape, cancels. No booking logic here.
/// </summary>
public static class CalendarBooking
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(CalendarBooking),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty GestureCommandProperty =
        DependencyProperty.RegisterAttached("GestureCommand", typeof(ICommand), typeof(CalendarBooking));

    public static readonly DependencyProperty PreviewCommandProperty =
        DependencyProperty.RegisterAttached("PreviewCommand", typeof(ICommand), typeof(CalendarBooking));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);
    public static ICommand? GetGestureCommand(DependencyObject d) => (ICommand?)d.GetValue(GestureCommandProperty);
    public static void SetGestureCommand(DependencyObject d, ICommand? value) => d.SetValue(GestureCommandProperty, value);
    public static ICommand? GetPreviewCommand(DependencyObject d) => (ICommand?)d.GetValue(PreviewCommandProperty);
    public static void SetPreviewCommand(DependencyObject d, ICommand? value) => d.SetValue(PreviewCommandProperty, value);

    /// <summary>The press in progress (only one at a time).</summary>
    private sealed class Press
    {
        public required UIElement Host { get; init; }
        public required DateOnly Anchor { get; init; }
        public required MouseButton Button { get; init; }
        public required DayType Type { get; init; }
        public Window? Window { get; set; }
        public DateOnly? Previewed { get; set; }
        public bool Moved { get; set; }
    }

    private static Press? _press;

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement host) return;
        host.PreviewMouseDown -= OnMouseDown;
        host.PreviewMouseMove -= OnMouseMove;
        host.PreviewMouseUp -= OnMouseUp;
        host.LostMouseCapture -= OnLostCapture;
        if (!(bool)e.NewValue) return;
        host.PreviewMouseDown += OnMouseDown;
        host.PreviewMouseMove += OnMouseMove;
        host.PreviewMouseUp += OnMouseUp;
        host.LostMouseCapture += OnLostCapture;
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_press is not null || e.ChangedButton is not (MouseButton.Left or MouseButton.Right)) return;
        var host = (UIElement)sender;
        if (CellAt(host, e.GetPosition(host)) is not { } cell) return;

        _press = new Press
        {
            Host = host,
            Anchor = cell.Date,
            Button = e.ChangedButton,
            Type = e.ChangedButton == MouseButton.Left ? DayType.Vacation : DayType.Feriefri,
        };
        _press.Window = Window.GetWindow(host);
        if (_press.Window is not null)
            _press.Window.PreviewKeyDown += OnKeyDown;
        if (!host.CaptureMouse())
        {
            End();
            return;
        }
        e.Handled = true;
    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_press is not { } press) return;
        var pressed = press.Button == MouseButton.Left ? e.LeftButton : e.RightButton;
        if (pressed != MouseButtonState.Pressed)
        {
            Cancel();
            return;
        }

        var cell = CellAt(press.Host, e.GetPosition(press.Host));
        if (cell is null)
        {
            // Over nothing: no highlight, but the press stays alive in case the pointer comes back.
            if (press.Previewed is not null)
            {
                press.Previewed = null;
                GetPreviewCommand(press.Host)?.Execute(null);
            }
            return;
        }

        if (!press.Moved && cell.Date == press.Anchor) return;

        press.Moved = true;
        if (press.Previewed == cell.Date) return;
        press.Previewed = cell.Date;
        GetPreviewCommand(press.Host)?.Execute(new CalendarGesture(press.Anchor, cell.Date, press.Type, false));
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_press is not { } press || e.ChangedButton != press.Button) return;
        var cell = CellAt(press.Host, e.GetPosition(press.Host));
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        End();
        e.Handled = true;
        if (cell is not null)
            GetGestureCommand(press.Host)?.Execute(new CalendarGesture(press.Anchor, cell.Date, press.Type, shift));
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_press is null) return;
        if (e.Key == Key.Escape)
            Cancel();
        else if (e.Key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.System)
            return;
        // Any other key during a press is swallowed, so e.g. Space on a focused button can't change the year under the pointer.
        e.Handled = true;
    }

    private static void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (_press is not null && ReferenceEquals(_press.Host, sender))
            Cancel();
    }

    private static void Cancel() => End();

    /// <summary>Finishes the press: releases the mouse, stops listening for Escape and clears the highlight.</summary>
    private static void End()
    {
        if (_press is not { } press) return;
        _press = null;
        if (press.Window is not null)
            press.Window.PreviewKeyDown -= OnKeyDown;
        if (press.Host.IsMouseCaptured)
            press.Host.ReleaseMouseCapture();
        if (press.Previewed is not null || press.Moved)
            GetPreviewCommand(press.Host)?.Execute(null);
    }

    /// <summary>The day cell under the point, or null (padding, outside, scroll bars).</summary>
    private static DayCell? CellAt(UIElement host, Point point)
    {
        var hit = VisualTreeHelper.HitTest(host, point)?.VisualHit;
        while (hit is not null)
        {
            if (hit is FrameworkElement { DataContext: DayCell { IsBlank: false } cell })
                return cell;
            hit = hit is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(hit) : null;
        }
        return null;
    }
}
