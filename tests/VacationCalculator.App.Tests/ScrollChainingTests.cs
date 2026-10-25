using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VacationCalculator.App.Behaviors;

namespace VacationCalculator.App.Tests;

/// <summary>
/// Nested scroll areas: the inner one (200 high viewport over 600 content) scrolls first; at its top/bottom the wheel goes to the outer one.
/// </summary>
public class ScrollChainingTests
{
    private static void Sta(Action work)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { work(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception("STA work failed", error);
    }

    private static (ScrollViewer Outer, ScrollViewer Inner) Build(bool enabled)
    {
        var inner = new ScrollViewer
        {
            Height = 200,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Height = 600 },
        };
        if (enabled) ScrollChaining.SetIsEnabled(inner, true);
        var panel = new StackPanel();
        panel.Children.Add(new Border { Height = 300 });
        panel.Children.Add(inner);
        var outer = new ScrollViewer { Width = 300, Height = 250, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
        outer.Measure(new Size(300, 250));
        outer.Arrange(new Rect(0, 0, 300, 250));
        outer.UpdateLayout();
        return (outer, inner);
    }

    private static void Wheel(UIElement target, int delta)
    {
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
        {
            RoutedEvent = UIElement.PreviewMouseWheelEvent,
            Source = target,
        };
        target.RaiseEvent(args);
        // Real input raises the bubbling MouseWheel after the preview event unless it was handled.
        if (!args.Handled)
            target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = target,
            });
        // Scroll offsets are applied by the next layout pass.
        UIElement root = target;
        while (System.Windows.Media.VisualTreeHelper.GetParent(root) is UIElement p) root = p;
        root.UpdateLayout();
    }

    [Fact]
    public void Wheel_scrolls_inner_first_and_does_not_move_outer()
    {
        Sta(() =>
        {
            var (outer, inner) = Build(true);
            Wheel(inner, -120);Assert.True(inner.VerticalOffset > 0);
            Assert.Equal(0, outer.VerticalOffset);
        });
    }

    [Fact]
    public void Wheel_down_at_inner_bottom_scrolls_outer()
    {
        Sta(() =>
        {
            var (outer, inner) = Build(true);
            inner.ScrollToBottom();
            inner.UpdateLayout();
            var before = inner.VerticalOffset;
            Wheel(inner, -120);
            Assert.Equal(before, inner.VerticalOffset);
            Assert.True(outer.VerticalOffset > 0);
        });
    }

    [Fact]
    public void Wheel_up_at_inner_top_scrolls_outer_up()
    {
        Sta(() =>
        {
            var (outer, inner) = Build(true);
            outer.ScrollToBottom();
            outer.UpdateLayout();
            var before = outer.VerticalOffset;
            Assert.True(before > 0);
            Wheel(inner, 120);
            Assert.True(outer.VerticalOffset < before);
            Assert.Equal(0, inner.VerticalOffset);
        });
    }

    [Fact]
    public void Wheel_up_in_the_middle_of_inner_does_not_move_outer()
    {
        Sta(() =>
        {
            var (outer, inner) = Build(true);
            inner.ScrollToVerticalOffset(200);
            inner.UpdateLayout();
            outer.ScrollToBottom();
            outer.UpdateLayout();
            var outerBefore = outer.VerticalOffset;
            Wheel(inner, 120);
            Assert.True(inner.VerticalOffset < 200);
            Assert.Equal(outerBefore, outer.VerticalOffset);
        });
    }

    [Fact]
    public void Without_the_behaviour_the_inner_area_swallows_the_wheel_at_its_end()
    {
        Sta(() =>
        {
            var (outer, inner) = Build(false);
            inner.ScrollToBottom();
            inner.UpdateLayout();
            Wheel(inner, -120);
            Assert.Equal(0, outer.VerticalOffset);
        });
    }

    [Fact]
    public void Works_on_a_DataGrid_containing_a_ScrollViewer()
    {
        Sta(() =>
        {
            var grid = new DataGrid { Height = 200, AutoGenerateColumns = false, ItemsSource = Enumerable.Range(0, 50).Select(i => new { N = i }).ToList() };
            grid.Columns.Add(new DataGridTextColumn { Binding = new System.Windows.Data.Binding("N") });
            ScrollChaining.SetIsEnabled(grid, true);
            var panel = new StackPanel();
            panel.Children.Add(new Border { Height = 300 });
            panel.Children.Add(grid);
            var outer = new ScrollViewer { Width = 300, Height = 250, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
            outer.Measure(new Size(300, 250));
            outer.Arrange(new Rect(0, 0, 300, 250));
            outer.UpdateLayout();

            var sv0 = FindSv(grid);
            Assert.True(sv0 is { ScrollableHeight: > 0 });
            Wheel(sv0!, -120); // the wheel starts at the element under the mouse, inside the grid
            Assert.Equal(0, outer.VerticalOffset); // grid at its top scrolls itself first

            // Scroll the grid's own viewer to the end, then the wheel must reach the outer area.
            var sv = FindSv(grid)!;
            sv.ScrollToBottom();
            grid.UpdateLayout();
            Wheel(sv0!, -120); // the wheel starts at the element under the mouse, inside the grid
            Assert.True(outer.VerticalOffset > 0);
        });
    }

    private static ScrollViewer? FindSv(DependencyObject d)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
            if (c is ScrollViewer s) return s;
            if (FindSv(c) is { } f) return f;
        }
        return null;
    }
}



