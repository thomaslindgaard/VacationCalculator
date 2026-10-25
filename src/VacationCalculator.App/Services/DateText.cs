using System.Globalization;
using System.Windows.Data;

namespace VacationCalculator.App.Services;

/// <summary>
/// Dates written inside sentences, headings and lists, in the current language (<see cref="Loc.Current"/>), whatever the
/// Windows regional settings are: English "31 December 2026", Danish "31. december 2026", Romanian "31 decembrie 2026".
/// Editable inputs (date pickers, grids) keep the regional format.
/// </summary>
public static class DateText
{
    /// <summary>"31 December 2026" / "31. december 2026" / "31 decembrie 2026".</summary>
    public static string Long(DateOnly date) =>
        Loc.Format("Date.Long", Day(date), Loc.MonthName(date.Month), Year(date));

    /// <summary>"Thursday 24 December 2026" / "torsdag den 24. december 2026" / "joi, 24 decembrie 2026".</summary>
    public static string WithWeekday(DateOnly date) =>
        Loc.Format("Date.WithWeekday", Loc.WeekdayName(date.DayOfWeek), Day(date), Loc.MonthName(date.Month), Year(date));

    /// <summary>A long date for a date picker value.</summary>
    public static string Long(DateTime date) => Long(DateOnly.FromDateTime(date));

    private static string Day(DateOnly date) => date.Day.ToString(CultureInfo.InvariantCulture);

    private static string Year(DateOnly date) => date.Year.ToString("D4", CultureInfo.InvariantCulture);
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
