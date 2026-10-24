using System.Windows;
using System.Windows.Controls;

namespace VacationCalculator.App.Controls;

/// <summary>
/// Lays children out in equal-width columns. The column count is the largest divisor of the child count that still
/// gives every column at least <see cref="MinItemWidth"/> (12 months: 4, 3, 2 or 1 columns), so the last row is never ragged.
/// Assumptions: children are laid out row by row, rows are as tall as their tallest child, and the width is bounded
/// (it fills the available width); with an unbounded width it lays out at most 4 columns of <see cref="MinItemWidth"/>.
/// </summary>
public sealed class AdaptiveUniformGrid : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty =
        DependencyProperty.Register(nameof(MinItemWidth), typeof(double), typeof(AdaptiveUniformGrid),
            new FrameworkPropertyMetadata(180.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(AdaptiveUniformGrid),
            new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>Columns used when the available width is unbounded (e.g. inside a horizontally scrolling parent).</summary>
    private const int UnboundedColumns = 4;

    /// <summary>
    /// Pure function of width and child count, so measure and arrange always agree: the width measure reports for an
    /// unbounded width (<see cref="UnboundedColumns"/> columns of <see cref="MinItemWidth"/>) maps back to the same count.
    /// </summary>
    private int Columns(double width, int count)
    {
        if (count == 0)
            return 1;

        var fit = double.IsInfinity(width)
            ? UnboundedColumns
            : Math.Max(1, (int)Math.Floor((width + Spacing) / (MinItemWidth + Spacing) + 1e-9));
        for (var columns = Math.Min(fit, count); columns > 1; columns--)
        {
            if (count % columns == 0)
                return columns;
        }
        return 1;
    }

    protected override Size MeasureOverride(Size available)
    {
        var count = InternalChildren.Count;
        var columns = Columns(available.Width, count);
        var itemWidth = double.IsInfinity(available.Width)
            ? MinItemWidth
            : Math.Max(0, (available.Width - Spacing * (columns - 1)) / columns);

        var rows = (count + columns - 1) / columns;
        var height = 0.0;
        for (var row = 0; row < rows; row++)
        {
            var rowHeight = 0.0;
            for (var i = row * columns; i < Math.Min(count, (row + 1) * columns); i++)
            {
                InternalChildren[i].Measure(new Size(itemWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, InternalChildren[i].DesiredSize.Height);
            }
            height += rowHeight;
        }
        height += Spacing * Math.Max(0, rows - 1);
        var width = double.IsInfinity(available.Width) ? itemWidth * columns + Spacing * (columns - 1) : available.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = InternalChildren.Count;
        var columns = Columns(finalSize.Width, count);
        var itemWidth = Math.Max(0, (finalSize.Width - Spacing * (columns - 1)) / columns);

        var rows = (count + columns - 1) / columns;
        var y = 0.0;
        for (var row = 0; row < rows; row++)
        {
            var rowHeight = 0.0;
            for (var i = row * columns; i < Math.Min(count, (row + 1) * columns); i++)
                rowHeight = Math.Max(rowHeight, InternalChildren[i].DesiredSize.Height);

            for (var i = row * columns; i < Math.Min(count, (row + 1) * columns); i++)
                InternalChildren[i].Arrange(new Rect((i - row * columns) * (itemWidth + Spacing), y, itemWidth, rowHeight));
            y += rowHeight + Spacing;
        }
        return finalSize;
    }
}
