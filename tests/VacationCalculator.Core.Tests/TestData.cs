using System.Globalization;
using VacationCalculator.Core;

namespace VacationCalculator.Core.Tests;

/// <summary>
/// Builders shared by the holiday-year tests. No manual public holidays unless given; the automatic Danish holidays
/// (<see cref="DanishHolidays"/>) always apply.
/// </summary>
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
    /// 23 = .. Wed 02-04, 25 = .. Fri 02-06. Only weekends are skipped, so a <paramref name="from"/> near a Danish
    /// holiday would put records on it (which then don't count); the default range has none.
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

    /// <summary>
    /// <see cref="BalanceCalculator.Allocate"/> with today Sat 2026-10-24. Today only picks the default first tracked
    /// holiday year, so it doesn't matter with no transfer and no opening balance.
    /// </summary>
    public static IReadOnlyList<AllocatedDay> AllocateOn2026_10_24(VacationData data) =>
        BalanceCalculator.Allocate(data, D("2026-10-24"));

    public static VacationData Data(
        IEnumerable<BookedDay>? days = null,
        DateOnly? employedSince = null,
        int? firstTracked = null,
        decimal openingBalance = 0) =>
        new()
        {
            Days = (days ?? []).ToList(),
            EmployedSince = employedSince,
            FirstTrackedHolidayYear = firstTracked,
            OpeningBalanceDays = openingBalance,
        };

    /// <summary>
    /// Sets transfer limit 0 ("no transfer") on the holiday year starting in <paramref name="startYear"/>; later years
    /// without their own limit inherit it.
    /// </summary>
    public static VacationData NoTransferFrom(this VacationData data, int startYear) => data.WithLimit(startYear, 0);

    /// <summary>Sets the transfer limit of the holiday year starting in <paramref name="startYear"/>.</summary>
    public static VacationData WithLimit(this VacationData data, int startYear, decimal days)
    {
        data.SetTransferLimit(new HolidayYear(startYear), days);
        return data;
    }

    /// <summary>Sets the payout setting of the holiday year starting in <paramref name="startYear"/>.</summary>
    public static VacationData WithPayout(this VacationData data, int startYear, PayoutMode mode, decimal days = 0)
    {
        data.SetPayout(new HolidayYear(startYear), mode, days);
        return data;
    }

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
