using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VacationCalculator.App.Controls;

namespace VacationCalculator.App.Tests;

/// <summary>
/// The month grid's column count is the largest divisor of the child count that fits at least MinItemWidth (172) per column
/// with Spacing (10) between: n columns need 172 n + 10 (n - 1) = 182 n - 10 width.
/// </summary>
public class AdaptiveUniformGridTests
{
    private static T Sta<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
            throw new Exception("STA work failed", error);
        return result;
    }

    private static (AdaptiveUniformGrid Grid, List<Border> Items) Make(int count)
    {
        var grid = new AdaptiveUniformGrid { MinItemWidth = 172, Spacing = 10 };
        var items = new List<Border>();
        for (var i = 0; i < count; i++)
        {
            var b = new Border { Height = 20 };
            items.Add(b);
            grid.Children.Add(b);
        }
        return (grid, items);
    }

    private static int ColumnsAtArrange(double width, int count = 12)
    {
        return Sta(() =>
        {
            var (grid, items) = Make(count);
            grid.Measure(new Size(width, double.PositiveInfinity));
            grid.Arrange(new Rect(0, 0, width, grid.DesiredSize.Height));
            return items.Select(i => Math.Round(VisualTreeHelper.GetOffset(i).X, 3)).Distinct().Count();
        });
    }

    [Theory]
    [InlineData(718, 4)]    // 4 * 172 + 3 * 10 exactly
    [InlineData(900, 4)]
    [InlineData(1081, 4)]   // 5 would fit but 12 % 5 != 0; 6 needs 1082
    [InlineData(717, 3)]    // one short of four columns
    [InlineData(536, 3)]    // 3 * 172 + 2 * 10 exactly
    [InlineData(535, 2)]
    [InlineData(354, 2)]    // 2 * 172 + 10 exactly
    [InlineData(353, 1)]
    [InlineData(172, 1)]
    [InlineData(100, 1)]
    [InlineData(0, 1)]
    [InlineData(1082, 6)]   // 6 * 172 + 5 * 10: 6 divides 12
    public void ColumnCount_FollowsWidth_ForTwelveMonths(double width, int expectedColumns) =>
        Assert.Equal(expectedColumns, ColumnsAtArrange(width));

    [Fact]
    public void InfiniteWidth_MeasuresFourColumns_AndArrangeOfThatWidthAgrees()
    {
        var (desired, columns) = Sta(() =>
        {
            var (grid, items) = Make(12);
            grid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var d = grid.DesiredSize;
            grid.Arrange(new Rect(0, 0, d.Width, d.Height));
            return (d, items.Select(i => Math.Round(VisualTreeHelper.GetOffset(i).X, 3)).Distinct().Count());
        });
        Assert.Equal(718, desired.Width);   // 4 columns of 172 + 3 gaps of 10
        Assert.Equal(3 * 20 + 2 * 10, desired.Height); // 3 rows of 20 + 2 gaps
        Assert.Equal(4, columns);
    }

    [Fact]
    public void ZeroWidth_DoesNotThrow_AndZeroChildrenIsEmpty()
    {
        var size = Sta(() =>
        {
            var (grid, _) = Make(0);
            grid.Measure(new Size(500, double.PositiveInfinity));
            grid.Arrange(new Rect(0, 0, 500, 0));
            return grid.DesiredSize;
        });
        Assert.Equal(0, size.Height);

        Assert.Equal(1, ColumnsAtArrange(0));
    }

    [Fact]
    public void MeasureAndArrange_AgreeOnRows_AtFiniteWidth()
    {
        // 800 wide: 4 columns -> 3 rows (20 each) + 2 gaps: desired height 80, items in 3 distinct y offsets.
        var (height, rows) = Sta(() =>
        {
            var (grid, items) = Make(12);
            grid.Measure(new Size(800, double.PositiveInfinity));
            grid.Arrange(new Rect(0, 0, 800, grid.DesiredSize.Height));
            return (grid.DesiredSize.Height, items.Select(i => VisualTreeHelper.GetOffset(i).Y).Distinct().Count());
        });
        Assert.Equal(80, height);
        Assert.Equal(3, rows);
    }

    [Fact]
    public void ItemWidth_FillsAvailableWidth()
    {
        // 900 wide, 4 columns, gap 10: (900 - 30) / 4 = 217.5 per item.
        var widths = Sta(() =>
        {
            var (grid, items) = Make(12);
            grid.Measure(new Size(900, double.PositiveInfinity));
            grid.Arrange(new Rect(0, 0, 900, grid.DesiredSize.Height));
            return items.Select(i => i.RenderSize.Width).Distinct().ToList();
        });
        Assert.Equal([217.5], widths);
    }
}

public class AddNumberConverterTests
{
    [Fact]
    public void AddsParameterToNumber_AndTreatsNonNumberAsZero()
    {
        var c = new VacationCalculator.App.Services.AddNumberConverter();
        var inv = System.Globalization.CultureInfo.GetCultureInfo("da-DK"); // comma culture must not matter
        Assert.Equal(512.5, c.Convert(400.0, typeof(double), "112.5", inv));
        Assert.Equal(112.0, c.Convert(null, typeof(double), "112", inv));
        Assert.Equal(-5.0, c.Convert(0.0, typeof(double), "-5", inv));
        Assert.Equal(7.0, c.Convert(7.0, typeof(double), null, inv));
    }
}
