using System.Globalization;
using System.Windows.Data;

namespace VacationCalculator.App.Services;

/// <summary>
/// Dates written inside sentences, headings and lists are always English ("31 December 2026"),
/// whatever the Windows regional settings are. Editable inputs (date pickers, grids) keep the regional format.
/// </summary>
public static class DateText
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-GB");

    /// <summary>"31 December 2026".</summary>
    public static string Long(DateOnly date) => date.ToString("d MMMM yyyy", English);

    /// <summary>"Thursday 24 December 2026".</summary>
    public static string WithWeekday(DateOnly date) => date.ToString("dddd d MMMM yyyy", English);

    /// <summary>"31 December 2026" for a date picker value.</summary>
    public static string Long(DateTime date) => Long(DateOnly.FromDateTime(date));
}

/// <summary>XAML access to <see cref="DateText"/>; <c>ConverterParameter=Weekday</c> includes the weekday.</summary>
public sealed class DateTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DateOnly d when parameter is "Weekday" => DateText.WithWeekday(d),
        DateOnly d => DateText.Long(d),
        DateTime dt when parameter is "Weekday" => DateText.WithWeekday(DateOnly.FromDateTime(dt)),
        DateTime dt => DateText.Long(dt),
        _ => string.Empty,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
