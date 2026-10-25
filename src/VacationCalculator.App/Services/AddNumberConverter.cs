using System.Globalization;
using System.Windows.Data;

namespace VacationCalculator.App.Services;

/// <summary>Adds the number in <c>ConverterParameter</c> to a bound number (layout minimums that follow another element's size).</summary>
public sealed class AddNumberConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is double d ? d : 0.0) + double.Parse(parameter as string ?? "0", CultureInfo.InvariantCulture);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
