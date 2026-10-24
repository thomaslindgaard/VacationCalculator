using System.Globalization;
using VacationCalculator.Core;

namespace VacationCalculator.Core.Tests;

/// <summary>Builders shared by the holiday-year tests. No public holidays unless given.</summary>
internal static class TestData
{
    public static DateOnly D(string date) => DateOnly.Parse(date, CultureInfo.InvariantCulture);

    public static decimal M(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    public static BookedDay Day(string date, decimal amount = 1, DayType type = DayType.Vacation) =>
        new() { Date = D(date), Amount = amount, Type = type };

    /// <summary>Full vacation days on every weekday of the inclusive range.</summary>
    public static List<BookedDay> Range(string start, string end) =>
        Bookings.BookRange([], D(start), D(end), DayType.Vacation, "", new HashSet<DateOnly>());

    /// <summary>
    /// <paramref name="days"/> full vacation days on consecutive weekdays from Mon 2026-01-05, plus a half day
    /// after them if <paramref name="days"/> has a .5. All in holiday year 2025, and all before any
    /// <c>today</c> after February 2026. E.g. 15 = Mon 01-05 .. Fri 01-23, 20 = .. Fri 01-30,
    /// 23 = .. Wed 02-04, 25 = .. Fri 02-06.
    /// </summary>
    public static List<BookedDay> Fill(decimal days, string from = "2026-01-05")
    {
        var result = new List<BookedDay>();
        var d = D(from);
        for (var left = days; left > 0; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            var amount = left >= 1 ? 1 : left;
            result.Add(new BookedDay { Date = d, Amount = amount });
            left -= amount;
        }
        return result;
    }

    public static VacationData Data(
        IEnumerable<BookedDay>? days = null,
        DateOnly? employedSince = null,
        bool transferAllowed = false,
        params (int StartYear, decimal Days)[] transfers) =>
        new()
        {
            Days = (days ?? []).ToList(),
            EmployedSince = employedSince,
            FifthWeekTransferAllowed = transferAllowed,
            HolidayYears = transfers.Select(t => new HolidayYearSettings { StartYear = t.StartYear, TransferredToNext = t.Days }).ToList(),
        };

    public static VacationData WithHolidays(this VacationData data, params string[] holidays)
    {
        foreach (var h in holidays.Select(D))
        {
            var year = data.Years.Find(y => y.Year == h.Year);
            if (year is null)
                data.Years.Add(year = new YearSettings { Year = h.Year });
            year.PublicHolidays.Add(h);
        }
        return data;
    }
}
