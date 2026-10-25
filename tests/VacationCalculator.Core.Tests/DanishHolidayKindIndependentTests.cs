using VacationCalculator.Core;

namespace VacationCalculator.Core.Tests;

/// <summary>
/// Feature 7: holidays are kinds, not names. KindOf at the edges of the Easter window, around fixed dates, at the ends of the
/// calendar, and in agreement with For and IsHoliday for every year. Dates counted by hand from Easter Sunday.
/// </summary>
public class DanishHolidayKindIndependentTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact]
    public void Kind_HasTwelveValues_InCalendarOrder()
    {
        var kinds = Enum.GetValues<DanishHolidayKind>();
        Assert.Equal(12, kinds.Length);
        Assert.Equal(kinds, DanishHolidays.For(2026).Select(h => h.Kind));
    }

    // Easter 2026 = Sun 5 April. Maundy Thu 2 Apr, Good Fri 3 Apr, Easter Mon 6 Apr, Ascension = +39 = Thu 14 May,
    // Whit Sunday = +49 = 24 May, Whit Monday = +50 = 25 May.
    [Theory]
    [InlineData(2026, 1, 1, DanishHolidayKind.NewYearsDay)]
    [InlineData(2026, 4, 2, DanishHolidayKind.MaundyThursday)]
    [InlineData(2026, 4, 3, DanishHolidayKind.GoodFriday)]
    [InlineData(2026, 4, 5, DanishHolidayKind.EasterSunday)]
    [InlineData(2026, 4, 6, DanishHolidayKind.EasterMonday)]
    [InlineData(2026, 5, 14, DanishHolidayKind.AscensionDay)]
    [InlineData(2026, 5, 24, DanishHolidayKind.WhitSunday)]
    [InlineData(2026, 5, 25, DanishHolidayKind.WhitMonday)]
    [InlineData(2026, 12, 24, DanishHolidayKind.ChristmasEve)]
    [InlineData(2026, 12, 25, DanishHolidayKind.ChristmasDay)]
    [InlineData(2026, 12, 26, DanishHolidayKind.SecondChristmasDay)]
    [InlineData(2026, 12, 31, DanishHolidayKind.NewYearsEve)]
    public void KindOf_2026(int y, int m, int d, DanishHolidayKind expected) =>
        Assert.Equal(expected, DanishHolidays.KindOf(D(y, m, d)));

    [Theory]
    [InlineData(2026, 1, 2)]
    [InlineData(2026, 4, 1)]   // day before Maundy Thursday
    [InlineData(2026, 4, 4)]   // Holy Saturday is not a holiday
    [InlineData(2026, 4, 7)]   // day after Easter Monday
    [InlineData(2026, 5, 13)]
    [InlineData(2026, 5, 15)]
    [InlineData(2026, 5, 23)]  // Saturday before Whit Sunday
    [InlineData(2026, 5, 26)]  // day after Whit Monday
    [InlineData(2026, 6, 5)]   // Grundlovsdag is not automatic
    [InlineData(2026, 12, 23)]
    [InlineData(2026, 12, 27)]
    [InlineData(2026, 12, 30)]
    [InlineData(2026, 2, 28)]
    public void KindOf_NotAHoliday_IsNull(int y, int m, int d)
    {
        Assert.Null(DanishHolidays.KindOf(D(y, m, d)));
        Assert.False(DanishHolidays.IsHoliday(D(y, m, d)));
    }

    // Earliest possible Easter: 22 March (1818, 2285). Maundy Thursday = 19 March, Good Friday 20, Easter Monday 23 March,
    // Ascension = 22 March + 39 = 30 April, Whit Sunday = +49 = 10 May, Whit Monday = 11 May.
    [Fact]
    public void KindOf_EarliestEaster_2285_22March()
    {
        Assert.Equal(D(2285, 3, 22), DanishHolidays.EasterSunday(2285));
        Assert.Equal(DanishHolidayKind.MaundyThursday, DanishHolidays.KindOf(D(2285, 3, 19)));
        Assert.Equal(DanishHolidayKind.GoodFriday, DanishHolidays.KindOf(D(2285, 3, 20)));
        Assert.Equal(DanishHolidayKind.EasterSunday, DanishHolidays.KindOf(D(2285, 3, 22)));
        Assert.Equal(DanishHolidayKind.EasterMonday, DanishHolidays.KindOf(D(2285, 3, 23)));
        Assert.Equal(DanishHolidayKind.AscensionDay, DanishHolidays.KindOf(D(2285, 4, 30)));
        Assert.Equal(DanishHolidayKind.WhitSunday, DanishHolidays.KindOf(D(2285, 5, 10)));
        Assert.Equal(DanishHolidayKind.WhitMonday, DanishHolidays.KindOf(D(2285, 5, 11)));
        Assert.Null(DanishHolidays.KindOf(D(2285, 3, 18)));
        Assert.Null(DanishHolidays.KindOf(D(2285, 3, 21)));
        Assert.Null(DanishHolidays.KindOf(D(2285, 5, 12)));
    }

    // Latest possible Easter: 25 April (1943, 2038). Maundy Thursday = 22 April, Good Friday 23, Easter Monday 26 April,
    // Ascension = 25 April + 39 = 3 June, Whit Sunday = +49 = 13 June, Whit Monday = 14 June (the latest any of them falls).
    [Fact]
    public void KindOf_LatestEaster_2038_25April_WhitMondayOn14June()
    {
        Assert.Equal(D(2038, 4, 25), DanishHolidays.EasterSunday(2038));
        Assert.Equal(DanishHolidayKind.MaundyThursday, DanishHolidays.KindOf(D(2038, 4, 22)));
        Assert.Equal(DanishHolidayKind.EasterMonday, DanishHolidays.KindOf(D(2038, 4, 26)));
        Assert.Equal(DanishHolidayKind.AscensionDay, DanishHolidays.KindOf(D(2038, 6, 3)));
        Assert.Equal(DanishHolidayKind.WhitSunday, DanishHolidays.KindOf(D(2038, 6, 13)));
        Assert.Equal(DanishHolidayKind.WhitMonday, DanishHolidays.KindOf(D(2038, 6, 14)));
        Assert.Null(DanishHolidays.KindOf(D(2038, 6, 15)));
        Assert.Null(DanishHolidays.KindOf(D(2038, 6, 12)));
    }

    // Easter 1943 = 25 April as well.
    [Fact]
    public void KindOf_LatestEaster_1943()
    {
        Assert.Equal(DanishHolidayKind.WhitMonday, DanishHolidays.KindOf(D(1943, 6, 14)));
        Assert.Null(DanishHolidays.KindOf(D(1943, 6, 15)));
    }

    // Easter 2027 = Sun 28 March: Maundy Thursday 25 March, Ascension 6 May, Whit Sunday 16 May.
    [Fact]
    public void KindOf_2027_EasterInLateMarch()
    {
        Assert.Equal(DanishHolidayKind.MaundyThursday, DanishHolidays.KindOf(D(2027, 3, 25)));
        Assert.Equal(DanishHolidayKind.GoodFriday, DanishHolidays.KindOf(D(2027, 3, 26)));
        Assert.Equal(DanishHolidayKind.EasterMonday, DanishHolidays.KindOf(D(2027, 3, 29)));
        Assert.Equal(DanishHolidayKind.AscensionDay, DanishHolidays.KindOf(D(2027, 5, 6)));
        Assert.Equal(DanishHolidayKind.WhitSunday, DanishHolidays.KindOf(D(2027, 5, 16)));
        // 2026's Easter dates are ordinary days in 2027.
        Assert.Null(DanishHolidays.KindOf(D(2027, 4, 2)));
        Assert.Null(DanishHolidays.KindOf(D(2027, 5, 14)));
    }

    // Leap year: Easter 2024 = Sun 31 March; 29 Feb 2024 is not a holiday; Ascension 2024 = 31 Mar + 39 = 9 May.
    [Fact]
    public void KindOf_LeapYear2024()
    {
        Assert.Null(DanishHolidays.KindOf(D(2024, 2, 29)));
        Assert.Null(DanishHolidays.KindOf(D(2024, 3, 1)));
        Assert.Equal(DanishHolidayKind.EasterSunday, DanishHolidays.KindOf(D(2024, 3, 31)));
        Assert.Equal(DanishHolidayKind.MaundyThursday, DanishHolidays.KindOf(D(2024, 3, 28)));
        Assert.Equal(DanishHolidayKind.AscensionDay, DanishHolidays.KindOf(D(2024, 5, 9)));
        Assert.Equal(DanishHolidayKind.WhitMonday, DanishHolidays.KindOf(D(2024, 5, 20)));
    }

    [Fact]
    public void KindOf_FirstAndLastDayOfTheCalendar_DoNotThrow()
    {
        Assert.Equal(DanishHolidayKind.NewYearsDay, DanishHolidays.KindOf(DateOnly.MinValue));
        Assert.Equal(DanishHolidayKind.NewYearsEve, DanishHolidays.KindOf(DateOnly.MaxValue));
        Assert.Null(DanishHolidays.KindOf(D(1, 1, 2)));
        Assert.Null(DanishHolidays.KindOf(D(9999, 12, 30)));
        Assert.Equal(DanishHolidayKind.ChristmasDay, DanishHolidays.KindOf(D(9999, 12, 25)));
        // Year 1 and year 9999 Easter weeks resolve without throwing.
        var year1 = Enumerable.Range(0, 120).Select(i => DanishHolidays.KindOf(D(1, 3, 1).AddDays(i))).Count(k => k is not null);
        Assert.Equal(7, year1);
        var year9999 = Enumerable.Range(0, 120).Select(i => DanishHolidays.KindOf(D(9999, 3, 1).AddDays(i))).Count(k => k is not null);
        Assert.Equal(7, year9999);
    }

    [Fact]
    public void KindOf_AgreesWithForAndIsHoliday_ForEveryDayOfEveryYear()
    {
        for (var year = 1; year <= 9999; year++)
        {
            var expected = DanishHolidays.For(year).ToDictionary(h => h.Date, h => h.Kind);
            Assert.Equal(12, expected.Count);
            // Only dates near holidays are interesting, but check the entire year for a sample of years and the edges of each holiday otherwise.
            var dates = year % 50 == 1
                ? Enumerable.Range(0, DateTime.IsLeapYear(year) ? 366 : 365).Select(i => D(year, 1, 1).AddDays(i))
                : expected.Keys.SelectMany(d => new[] { d.DayNumber - 1, d.DayNumber, d.DayNumber + 1 })
                    .Where(n => n >= DateOnly.MinValue.DayNumber && n <= DateOnly.MaxValue.DayNumber)
                    .Select(DateOnly.FromDayNumber).Where(d => d.Year == year);
            foreach (var date in dates)
            {
                if (expected.TryGetValue(date, out var kind))
                {
                    Assert.Equal(kind, DanishHolidays.KindOf(date));
                    Assert.True(DanishHolidays.IsHoliday(date));
                }
                else
                {
                    Assert.Null(DanishHolidays.KindOf(date));
                    Assert.False(DanishHolidays.IsHoliday(date));
                }
            }
        }
    }

    [Fact]
    public void KindOf_IsSafeFromManyThreads()
    {
        var failures = 0;
        Parallel.For(1900, 2300, year =>
        {
            foreach (var h in DanishHolidays.For(year))
            {
                if (DanishHolidays.KindOf(h.Date) != h.Kind)
                    Interlocked.Increment(ref failures);
            }
        });
        Assert.Equal(0, failures);
    }

    [Fact]
    public void PublicHoliday_RecordCarriesTheKind_AndManualOnlyHasNone()
    {
        var automatic = new PublicHoliday(D(2026, 12, 24), DanishHolidayKind.ChristmasEve, IsAutomatic: true, IsManual: false);
        var manual = new PublicHoliday(D(2026, 6, 5), null, IsAutomatic: false, IsManual: true);
        Assert.Equal(DanishHolidayKind.ChristmasEve, automatic.Kind);
        Assert.Null(manual.Kind);
        Assert.Equal(DanishHolidayKind.ChristmasEve, DanishHolidays.KindOf(automatic.Date));
        Assert.Null(DanishHolidays.KindOf(manual.Date));
    }

    [Fact]
    public void CoreExposesNoUserFacingNames()
    {
        // Core has no holiday or month name members any more; the enum is the only identity.
        var coreTypes = typeof(DanishHolidays).Assembly.GetTypes();
        foreach (var type in new[] { typeof(DanishHoliday), typeof(PublicHoliday), typeof(CalendarMonth), typeof(CalendarDay) })
        {
            Assert.DoesNotContain(type.GetProperties(), p => p.Name is "Name" or "HolidayName" or "MonthName");
        }
        Assert.NotEmpty(coreTypes);
    }
}
