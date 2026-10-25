using VacationCalculator.Core;
using static VacationCalculator.Core.Tests.TestData;

namespace VacationCalculator.Core.Tests;

/// <summary>
/// Feature 4 (Danish public holidays), written from the requirement and published Easter tables, not from the code.
/// Holidays: Nytaarsdag, Skaertorsdag, Langfredag, Paaskedag, 2. paaskedag, Kristi himmelfartsdag, Pinsedag, 2. pinsedag,
/// 24, 25, 26 and 31 December. No Store bededag, no Grundlovsdag.
/// </summary>
public class DanishHolidaysIndependentTests
{
    private static readonly HashSet<DateOnly> NoManual = [];

    // ---- Easter ----

    // Western Easter Sundays from the published table (year, month, day).
    public static IEnumerable<object[]> PublishedEaster() =>
    [
        [1900, 4, 15], [1818, 3, 22], [1943, 4, 25], [1954, 4, 18], [1981, 4, 19],
        [2000, 4, 23], [2001, 4, 15], [2002, 3, 31], [2003, 4, 20], [2004, 4, 11], [2005, 3, 27], [2006, 4, 16],
        [2007, 4, 8], [2008, 3, 23], [2009, 4, 12], [2010, 4, 4], [2011, 4, 24], [2012, 4, 8], [2013, 3, 31],
        [2014, 4, 20], [2015, 4, 5], [2016, 3, 27], [2017, 4, 16], [2018, 4, 1], [2019, 4, 21], [2020, 4, 12],
        [2021, 4, 4], [2022, 4, 17], [2023, 4, 9], [2024, 3, 31], [2025, 4, 20], [2026, 4, 5], [2027, 3, 28],
        [2028, 4, 16], [2029, 4, 1], [2030, 4, 21], [2031, 4, 13], [2032, 3, 28], [2033, 4, 17], [2034, 4, 9],
        [2035, 3, 25], [2036, 4, 13], [2037, 4, 5], [2038, 4, 25], [2039, 4, 10], [2040, 4, 1],
        [2285, 3, 22],
    ];

    [Theory]
    [MemberData(nameof(PublishedEaster))]
    public void Easter_MatchesThePublishedTable(int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), DanishHolidays.EasterSunday(year));
    }

    /// <summary>Oudin's (Gauss-style) integer algorithm, a different formulation from the usual Meeus/Jones/Butcher one.</summary>
    private static DateOnly OudinEaster(int y)
    {
        var c = y / 100;
        var n = y - 19 * (y / 19);
        var k = (c - 17) / 25;
        var i = c - c / 4 - (c - k) / 3 + 19 * n + 15;
        i -= 30 * (i / 30);
        i -= (i / 28) * (1 - (i / 28) * (29 / (i + 1)) * ((21 - n) / 11));
        var j = y + y / 4 + i + 2 - c + c / 4;
        j -= 7 * (j / 7);
        var l = i - j;
        var month = 3 + (l + 40) / 44;
        var day = l + 28 - 31 * (month / 4);
        return new DateOnly(y, month, day);
    }

    [Fact]
    public void Easter_AgreesWithAnIndependentAlgorithm_1583To4099()
    {
        for (var y = 1583; y <= 4099; y++)
            Assert.Equal(OudinEaster(y), DanishHolidays.EasterSunday(y));
    }

    [Fact]
    public void EasterRelativeHolidays_AlwaysFallOnTheirWeekday_AndWhitsunAlwaysOnSunday()
    {
        for (var y = 1583; y <= 3000; y++)
        {
            var byKind = DanishHolidays.For(y).ToDictionary(h => h.Kind, h => h.Date);
            Assert.Equal(DayOfWeek.Thursday, byKind[DanishHolidayKind.MaundyThursday].DayOfWeek);
            Assert.Equal(DayOfWeek.Friday, byKind[DanishHolidayKind.GoodFriday].DayOfWeek);
            Assert.Equal(DayOfWeek.Sunday, byKind[DanishHolidayKind.EasterSunday].DayOfWeek);
            Assert.Equal(DayOfWeek.Monday, byKind[DanishHolidayKind.EasterMonday].DayOfWeek);
            Assert.Equal(DayOfWeek.Thursday, byKind[DanishHolidayKind.AscensionDay].DayOfWeek);
            Assert.Equal(DayOfWeek.Sunday, byKind[DanishHolidayKind.WhitSunday].DayOfWeek);
            Assert.Equal(DayOfWeek.Monday, byKind[DanishHolidayKind.WhitMonday].DayOfWeek);
        }
    }

    [Fact]
    public void BruteForce_EveryDayOfManyYears_ExactlyTheTwelveListedDatesAreHolidays()
    {
        foreach (var year in new[] { 1, 1900, 1999, 2000, 2008, 2024, 2026, 2027, 2028, 2038, 2100, 2285, 9998, 9999 })
        {
            var expected = DanishHolidays.For(year).Select(h => h.Date).ToHashSet();
            Assert.Equal(12, expected.Count);
            var found = new HashSet<DateOnly>();
            for (var n = new DateOnly(year, 1, 1).DayNumber; n <= new DateOnly(year, 12, 31).DayNumber; n++)
            {
                var d = DateOnly.FromDayNumber(n);
                Assert.Equal(DanishHolidays.KindOf(d) is not null, DanishHolidays.IsHoliday(d));
                if (DanishHolidays.IsHoliday(d))
                    found.Add(d);
            }
            Assert.Equal(expected.Order(), found.Order());
        }
    }

    // ---- The list with kinds ----

    [Fact]
    public void For2024_Kinds_AndDates()
    {
        // Easter Sun 2024-03-31: Skaertorsdag Thu 03-28, Langfredag Fri 03-29, 2. paaskedag Mon 04-01,
        // Kristi himmelfart Thu 05-09 (31 Mar + 39 d: 30 Apr is +30, 9 May is +39), Pinsedag Sun 05-19, 2. pinsedag Mon 05-20.
        Assert.Equal(
            [
                (D("2024-01-01"), DanishHolidayKind.NewYearsDay), (D("2024-03-28"), DanishHolidayKind.MaundyThursday),
                (D("2024-03-29"), DanishHolidayKind.GoodFriday), (D("2024-03-31"), DanishHolidayKind.EasterSunday),
                (D("2024-04-01"), DanishHolidayKind.EasterMonday), (D("2024-05-09"), DanishHolidayKind.AscensionDay),
                (D("2024-05-19"), DanishHolidayKind.WhitSunday), (D("2024-05-20"), DanishHolidayKind.WhitMonday),
                (D("2024-12-24"), DanishHolidayKind.ChristmasEve), (D("2024-12-25"), DanishHolidayKind.ChristmasDay),
                (D("2024-12-26"), DanishHolidayKind.SecondChristmasDay), (D("2024-12-31"), DanishHolidayKind.NewYearsEve),
            ],
            DanishHolidays.For(2024).Select(h => (h.Date, h.Kind)));
    }

    [Fact]
    public void For2027_Kinds_AndDates()
    {
        // Easter Sun 2027-03-28: Thu 03-25, Fri 03-26, Mon 03-29, Ascension Thu 05-06, Sun 05-16, Mon 05-17.
        Assert.Equal(
            [
                (D("2027-01-01"), DanishHolidayKind.NewYearsDay), (D("2027-03-25"), DanishHolidayKind.MaundyThursday),
                (D("2027-03-26"), DanishHolidayKind.GoodFriday), (D("2027-03-28"), DanishHolidayKind.EasterSunday),
                (D("2027-03-29"), DanishHolidayKind.EasterMonday), (D("2027-05-06"), DanishHolidayKind.AscensionDay),
                (D("2027-05-16"), DanishHolidayKind.WhitSunday), (D("2027-05-17"), DanishHolidayKind.WhitMonday),
                (D("2027-12-24"), DanishHolidayKind.ChristmasEve), (D("2027-12-25"), DanishHolidayKind.ChristmasDay),
                (D("2027-12-26"), DanishHolidayKind.SecondChristmasDay), (D("2027-12-31"), DanishHolidayKind.NewYearsEve),
            ],
            DanishHolidays.For(2027).Select(h => (h.Date, h.Kind)));
    }

    [Fact]
    public void For2008_EarlyEaster_AscensionFallsOnMay1()
    {
        // Easter Sun 2008-03-23: Thu 03-20, Fri 03-21, Mon 03-24; Ascension Thu 05-01; Sun 05-11; Mon 05-12.
        var byKind = DanishHolidays.For(2008).ToDictionary(h => h.Kind, h => h.Date);
        Assert.Equal(D("2008-03-20"), byKind[DanishHolidayKind.MaundyThursday]);
        Assert.Equal(D("2008-03-21"), byKind[DanishHolidayKind.GoodFriday]);
        Assert.Equal(D("2008-03-24"), byKind[DanishHolidayKind.EasterMonday]);
        Assert.Equal(D("2008-05-01"), byKind[DanishHolidayKind.AscensionDay]);
        Assert.Equal(D("2008-05-11"), byKind[DanishHolidayKind.WhitSunday]);
        Assert.Equal(D("2008-05-12"), byKind[DanishHolidayKind.WhitMonday]);
    }

    [Fact]
    public void NotAHoliday_StoreBededag_Grundlovsdag_AndTheDaysAroundChristmas()
    {
        // 2024: Store bededag would have been Fri 04-26 (4th Friday after Easter); Grundlovsdag Wed 06-05.
        Assert.False(DanishHolidays.IsHoliday(D("2024-04-26")));
        Assert.False(DanishHolidays.IsHoliday(D("2024-06-05")));
        // 2023 (last year Store bededag existed): Easter 04-09, Fri 05-05.
        Assert.False(DanishHolidays.IsHoliday(D("2023-05-05")));
        Assert.False(DanishHolidays.IsHoliday(D("2026-12-23")));
        Assert.False(DanishHolidays.IsHoliday(D("2026-12-27")));
        Assert.False(DanishHolidays.IsHoliday(D("2027-01-02")));
        Assert.False(DanishHolidays.IsHoliday(D("2026-12-30")));
        Assert.False(DanishHolidays.IsHoliday(D("2025-12-30")));
    }

    // ---- Working-day counts ----

    [Fact]
    public void WholeYear2026_Has252WorkingDays()
    {
        // 365 days starting Thu = 261 weekdays, less 9 on weekdays: Thu 1 Jan, Thu 2 Apr, Fri 3 Apr, Mon 6 Apr,
        // Thu 14 May, Mon 25 May, Thu 24 Dec, Fri 25 Dec, Thu 31 Dec (Pinsedag/Paaskedag are Sundays, 26 Dec a Saturday).
        Assert.Equal(252, BalanceCalculator.CountWorkingDays(D("2026-01-01"), D("2026-12-31"), NoManual));
    }

    [Fact]
    public void WholeYear2027_Has253WorkingDays()
    {
        // 261 weekdays (starts Fri), less 8: Fri 1 Jan, Thu 25, Fri 26, Mon 29 Mar, Thu 6 May, Mon 17 May, Fri 24 Dec, Fri 31 Dec.
        Assert.Equal(253, BalanceCalculator.CountWorkingDays(D("2027-01-01"), D("2027-12-31"), NoManual));
    }

    [Fact]
    public void WholeYear2028_Has253WorkingDays()
    {
        // Leap year starting Sat: 366 days = 52 weeks + Sat + Sun -> 260 weekdays. Easter Sun 04-16: Thu 04-13,
        // Fri 04-14, Mon 04-17; Ascension Thu 05-25; Whit Mon 06-05; Mon 25 Dec and Tue 26 Dec. 1 Jan is a Saturday,
        // 24 and 31 Dec are Sundays: 7 weekday holidays -> 253.
        Assert.Equal(253, BalanceCalculator.CountWorkingDays(D("2028-01-01"), D("2028-12-31"), NoManual));
    }
    [Fact]
    public void RangeAroundAscension2026_SkipsThursday14May()
    {
        // Mon 2026-05-11 .. Fri 05-15: 5 weekdays less Thu 14 = 4
        Assert.Equal(4, BalanceCalculator.CountWorkingDays(D("2026-05-11"), D("2026-05-15"), NoManual));
        var days = Bookings.BookRange([], D("2026-05-11"), D("2026-05-15"), DayType.Vacation, "", NoManual);
        Assert.Equal([D("2026-05-11"), D("2026-05-12"), D("2026-05-13"), D("2026-05-15")], days.Select(d => d.Date));
    }

    [Fact]
    public void RangeAroundWhitsun2026_SkipsMonday25May_AndSunday()
    {
        // Fri 2026-05-22 .. Tue 05-26: Fri 22 and Tue 26 only (Sat 23, Sun 24 Pinsedag, Mon 25 2. pinsedag)
        var days = Bookings.BookRange([], D("2026-05-22"), D("2026-05-26"), DayType.Vacation, "", NoManual);
        Assert.Equal([D("2026-05-22"), D("2026-05-26")], days.Select(d => d.Date));
    }

    [Fact]
    public void RangeAcrossChristmasAndNewYear2027_To2028()
    {
        // Wed 2027-12-29 .. Tue 2028-01-04: Wed 29, Thu 30, (Fri 31 Dec hol, Sat 1 Jan, Sun 2) Mon 3, Tue 4 = 4
        var days = Bookings.BookRange([], D("2027-12-29"), D("2028-01-04"), DayType.Vacation, "", NoManual);
        Assert.Equal([D("2027-12-29"), D("2027-12-30"), D("2028-01-03"), D("2028-01-04")], days.Select(d => d.Date));
    }

    [Fact]
    public void Christmas2028_FallsOnMondayAndTuesday()
    {
        // Fri 2028-12-22 .. Fri 12-29: Fri 22, (Sat 23, Sun 24, Mon 25 hol, Tue 26 hol), Wed 27, Thu 28, Fri 29 = 4
        Assert.Equal(4, BalanceCalculator.CountWorkingDays(D("2028-12-22"), D("2028-12-29"), NoManual));
    }

    [Fact]
    public void RangeThatIsOnlyHolidays_BooksNothing_AndOneThatEndsOnA31December_Works()
    {
        // Thu 2026-12-24 .. Sat 12-26: all holiday or weekend
        Assert.Empty(Bookings.BookRange([], D("2026-12-24"), D("2026-12-26"), DayType.Vacation, "", NoManual));
        // Wed 2026-12-30 .. Thu 12-31: only the 30th
        Assert.Equal([D("2026-12-30")],
            Bookings.BookRange([], D("2026-12-30"), D("2026-12-31"), DayType.Vacation, "", NoManual).Select(d => d.Date));
        // Single day 31 Dec 2027 (Fri, holiday) and 1 Jan 2028 (Sat)
        Assert.Empty(Bookings.BookDay([], D("2027-12-31"), DayType.Vacation, 1, "", NoManual));
    }

    [Fact]
    public void BookRange_OverExistingRecordOnAHoliday_KeepsTheStoredRecord_ButDoesNotCountIt()
    {
        // An old record on Thu 2026-04-02 (Skaertorsdag) stays in the list; the new range does not replace or add it.
        var old = new List<BookedDay> { Day("2026-04-02", 0.5m) };
        var days = Bookings.BookRange(old, D("2026-03-31"), D("2026-04-07"), DayType.Vacation, "", NoManual);

        // Tue 03-31, Wed 04-01, Tue 04-07 are booked; Thu 02, Fri 03, Mon 06 are holidays; the old record survives.
        Assert.Equal(
            [D("2026-03-31"), D("2026-04-01"), D("2026-04-02"), D("2026-04-07")], days.Select(d => d.Date));
        Assert.Equal(0.5m, days.Single(d => d.Date == D("2026-04-02")).Amount);
        var b = BalanceCalculator.CalculateHolidayYear(Data(days), new HolidayYear(2025), D("2026-10-24"));
        Assert.Equal(3m, b.UsedDays);
    }

    // ---- Balances and used / planned around holidays ----

    [Fact]
    public void UsedAndPlanned_TodayOnAHoliday_Christmas2026()
    {
        // Today Fri 2026-12-25 (Juledag). Booked Wed 12-23 (past -> used), Thu 12-24 and Fri 12-25 (records on holidays,
        // ignored) and Mon 12-28 (future -> planned).
        var data = Data([Day("2026-12-23"), Day("2026-12-24"), Day("2026-12-25"), Day("2026-12-28")]);

        var b = BalanceCalculator.CalculateHolidayYear(data, new HolidayYear(2025), D("2026-12-25"));

        Assert.Equal(1m, b.UsedDays);
        Assert.Equal(1m, b.BookedAheadDays);
        Assert.Equal(23m, b.UnbookedDays);
    }

    [Fact]
    public void WorkingDaysLeft_AroundTheEndOfTheYear()
    {
        // Free working days from today through 31 Dec 2026: only 28, 29, 30 once Dec 24 is reached.
        Assert.Equal(3m, BalanceCalculator.CalculateCalendarYear(Data(), 2026, D("2026-12-24")).WorkingDaysLeft);
        Assert.Equal(3m, BalanceCalculator.CalculateCalendarYear(Data(), 2026, D("2026-12-28")).WorkingDaysLeft);
        // Wed 12-30: 1. Thu 12-31 (a holiday): 0. Fri 2027-01-01 the year is over: 0.
        Assert.Equal(1m, BalanceCalculator.CalculateCalendarYear(Data(), 2026, D("2026-12-30")).WorkingDaysLeft);
        Assert.Equal(0m, BalanceCalculator.CalculateCalendarYear(Data(), 2026, D("2026-12-31")).WorkingDaysLeft);
        Assert.Equal(0m, BalanceCalculator.CalculateCalendarYear(Data(), 2026, D("2027-01-01")).WorkingDaysLeft);
        // Dec 21..23 are free as well from Mon 12-21: 21, 22, 23, 28, 29, 30
        Assert.Equal(6m, BalanceCalculator.CalculateCalendarYear(Data(), 2026, D("2026-12-21")).WorkingDaysLeft);
    }

    [Fact]
    public void DaysAtRisk_UsesOnlyRealWorkingDays()
    {
        // Mon 2026-12-28, nothing booked: 25 unbooked, 3 free (28, 29, 30) -> 22 can't fit.
        var s = BalanceCalculator.CalculateCalendarYear(Data(), 2026, D("2026-12-28"));
        Assert.Equal(3m, s.WorkingDaysLeft);
        Assert.Equal(22m, s.DaysAtRisk);
        Assert.Equal(BalanceStatus.DaysWillBeLost, s.Status);
    }

    [Fact]
    public void WorkingDaysLeft_2027_Easter_FromMarch1()
    {
        // 2027 from Mon 03-01: Mar 1..31 = 23 weekdays less Thu 25, Fri 26, Mon 29 = 20; Apr 22; May 21 less Thu 6, Mon 17 = 19;
        // Jun 22; Jul 22; Aug 22; Sep 22; Oct 21; Nov 22; Dec 23 less Fri 24, Fri 31 = 21.
        // Weekdays per month 2027: Apr 22, May 21, Jun 22, Jul 22, Aug 22, Sep 22, Oct 21, Nov 22, Dec 23.
        // Total = 20 + 22 + 19 + 22 + 22 + 22 + 22 + 21 + 22 + 21 = 213
        var s = BalanceCalculator.CalculateCalendarYear(Data(), 2027, D("2027-03-01"));
        Assert.Equal(213m, s.WorkingDaysLeft);
    }

    [Fact]
    public void ManualHolidayOnAnAutomaticDate_ChangesNothing()
    {
        var plain = Data(Range("2026-12-21", "2026-12-31"));
        var withManual = Data(Range("2026-12-21", "2026-12-31")).WithHolidays("2026-12-24", "2026-12-25", "2026-12-31");

        Assert.Equal(6, plain.Days.Count);
        Assert.Equal(6, withManual.Days.Count);
        var a = BalanceCalculator.CalculateHolidayYear(plain, new HolidayYear(2025), D("2026-12-18"));
        var b = BalanceCalculator.CalculateHolidayYear(withManual, new HolidayYear(2025), D("2026-12-18"));
        Assert.Equal(a.BookedAheadDays, b.BookedAheadDays);
        Assert.Equal(
            BalanceCalculator.CalculateCalendarYear(plain, 2026, D("2026-12-18")).WorkingDaysLeft,
            BalanceCalculator.CalculateCalendarYear(withManual, 2026, D("2026-12-18")).WorkingDaysLeft);
        // Manual set only holds the manual dates, three of them
        Assert.Equal(3, withManual.AllPublicHolidays().Count);
    }

    [Fact]
    public void ManualHolidayOnARegularDay_StillCounts()
    {
        // Mon 2026-12-28 as an extra manual holiday: free days from Mon 12-21 are 21, 22, 23, 29, 30 = 5.
        var data = Data().WithHolidays("2026-12-28");
        Assert.Equal(5m, BalanceCalculator.CalculateCalendarYear(data, 2026, D("2026-12-21")).WorkingDaysLeft);
        Assert.Contains(D("2026-12-28"), data.AllPublicHolidays());
    }

    // ---- Feriefri around holidays ----

    [Fact]
    public void Feriefri_AroundEaster2027_CountsOnlyWorkingDays()
    {
        // Mon 2027-03-22 .. Fri 03-26: Mon, Tue, Wed are working days; Thu 25 and Fri 26 are holidays
        var r = Bookings.BookFeriefri(Data(), D("2027-03-22"), D("2027-03-26"), 1, "");

        Assert.Equal(3m, r.RequestedDays);
        Assert.Equal(3m, r.BookedDays);
        Assert.Equal([D("2027-03-22"), D("2027-03-23"), D("2027-03-24")], r.Days.Select(d => d.Date));
        Assert.Equal(DayType.Feriefri, r.Days[0].Type);
    }

    [Fact]
    public void Feriefri_HalfDayOnAHoliday_IsIgnored_ButOnTheDayAfterWorks()
    {
        // Fri 2026-12-25 is Juledag; Mon 12-28 is not
        var onHoliday = Bookings.BookFeriefri(Data(), D("2026-12-25"), D("2026-12-25"), 0.5m, "");
        Assert.Empty(onHoliday.Days);
        Assert.Equal(0m, onHoliday.RequestedDays);

        var after = Bookings.BookFeriefri(Data(), D("2026-12-28"), D("2026-12-28"), 0.5m, "");
        Assert.Equal(0.5m, Assert.Single(after.Days).Amount);
    }

    [Fact]
    public void Feriefri_RecordOnAHoliday_IsNotUsed()
    {
        var data = Data([Day("2026-05-14", type: DayType.Feriefri), Day("2026-05-13", type: DayType.Feriefri)]);

        var b = FeriefriCalculator.CalculateHolidayYear(data, new HolidayYear(2025), D("2026-10-24"));

        Assert.Equal(1m, b.UsedDays); // Wed 05-13 only; Thu 05-14 is Kristi himmelfartsdag
    }

    [Fact]
    public void Feriefri_FiveAllowanceDays_AreNotConsumedByHolidays()
    {
        // 2026 Easter week: Mon 03-30 .. Tue 04-07 has Mon, Tue, Wed (3) + Tue 04-07 = 4 working days (Thu 2, Fri 3, Mon 6 hol).
        var r = Bookings.BookFeriefri(Data(), D("2026-03-30"), D("2026-04-07"), 1, "");
        Assert.Equal(4m, r.RequestedDays);
        Assert.Equal(4m, r.BookedDays);
        Assert.True(r.IsComplete);
        // One working day left of the default allowance of 5 in 2025/26: Wed 04-08 books, Thu 04-09 doesn't fit
        var more = Bookings.BookFeriefri(Data(r.Days), D("2026-04-08"), D("2026-04-09"), 1, "");
        Assert.Equal(2m, more.RequestedDays);
        Assert.Equal(1m, more.BookedDays);
        Assert.Equal([D("2026-04-09")], more.LeftOutDates);
    }

    // ---- Grouping ----

    [Fact]
    public void GroupPeriods_RecordsEitherSideOfAHolidayBlock_FormOnePeriod()
    {
        // Wed 2026-12-23 and Mon 12-28: Thu 24, Fri 25, Sat, Sun lie between, nothing working.
        var days = new[] { Day("2026-12-23"), Day("2026-12-28") };

        var p = Assert.Single(Bookings.GroupPeriods(days, 2026, NoManual));
        Assert.Equal(D("2026-12-23"), p.Start);
        Assert.Equal(D("2026-12-28"), p.End);
        Assert.Equal(2m, p.Amount);
    }

    [Fact]
    public void GroupPeriods_AFreeWorkingDayBetween_StillSplits()
    {
        // Mon 2026-12-21 and Wed 12-23 with Tue 22 free -> two periods
        var days = new[] { Day("2026-12-21"), Day("2026-12-23") };

        Assert.Equal(2, Bookings.GroupPeriods(days, 2026, NoManual).Count);
    }

    [Fact]
    public void GroupPeriods_Ascension2026_BridgeDayFriday15()
    {
        // Wed 05-13 and Fri 05-15 around Thu 05-14 (Kristi himmelfart): one period of 2 days
        var days = new[] { Day("2026-05-13"), Day("2026-05-15") };

        var p = Assert.Single(Bookings.GroupPeriods(days, 2026, NoManual));
        Assert.Equal(2m, p.Amount);
    }

    // ---- Conflicts ----

    [Fact]
    public void BookedDaysOnHolidays_ManualHolidayAddedAfterBooking_IsReported_AndNotCounted()
    {
        // Booked Mon 2026-06-01 .. Fri 06-05; the user then adds Fri 06-05 (Grundlovsdag) as a manual holiday.
        var data = Data(Range("2026-06-01", "2026-06-05")).WithHolidays("2026-06-05");

        var hit = Assert.Single(data.BookedDaysOnHolidays());
        Assert.Equal(D("2026-06-05"), hit.Date);
        var b = BalanceCalculator.CalculateHolidayYear(data, new HolidayYear(2025), D("2026-10-24"));
        Assert.Equal(4m, b.UsedDays);
    }

    [Fact]
    public void BookedDaysOnHolidays_EmptyData_AndNothingOnHolidays_ReturnNothing()
    {
        Assert.Empty(Data().BookedDaysOnHolidays());
        Assert.Empty(Data(Range("2026-06-01", "2026-06-04")).BookedDaysOnHolidays());
        Assert.Empty(Data(Range("2026-06-01", "2026-06-04")).BookedDaysOnHolidays(2026));
    }

    [Fact]
    public void BookedDaysOnHolidays_YearFilter_HalfDayAndFeriefri_Sorted()
    {
        var data = Data(
        [
            Day("2027-01-01", 0.5m),                           // Nytaarsdag 2027
            Day("2026-12-31", 1, DayType.Feriefri),            // Nytaarsaftensdag 2026
            Day("2026-04-06"),                                 // 2. paaskedag 2026
            Day("2026-04-07"),
        ]);

        Assert.Equal([D("2026-04-06"), D("2026-12-31"), D("2027-01-01")], data.BookedDaysOnHolidays().Select(d => d.Date));
        Assert.Equal([D("2026-04-06"), D("2026-12-31")], data.BookedDaysOnHolidays(2026).Select(d => d.Date));
        Assert.Equal([D("2027-01-01")], data.BookedDaysOnHolidays(2027).Select(d => d.Date));
        Assert.Empty(data.BookedDaysOnHolidays(2028));
    }

    // ---- Lookup for the UI ----

    [Fact]
    public void PublicHolidaysIn_NoManual_IsTheTwelve_ManualOnRegularDayIsThirteen_ManualOnAutomaticStillTwelve()
    {
        Assert.Equal(12, Data().PublicHolidaysIn(2026).Count);
        Assert.Equal(13, Data().WithHolidays("2026-06-05").PublicHolidaysIn(2026).Count);
        Assert.Equal(12, Data().WithHolidays("2026-04-06").PublicHolidaysIn(2026).Count);
        var both = Data().WithHolidays("2026-04-06").PublicHolidaysIn(2026).Single(h => h.Date == D("2026-04-06"));
        Assert.True(both.IsAutomatic);
        Assert.True(both.IsManual);
        Assert.Equal(DanishHolidayKind.EasterMonday, both.Kind);
    }

    [Fact]
    public void PublicHolidaysIn_ExtremeYears()
    {
        Assert.Equal(12, Data().PublicHolidaysIn(1).Count);
        Assert.Equal(12, Data().PublicHolidaysIn(9999).Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => Data().PublicHolidaysIn(0));
    }

    // ---- Persistence ----

    [Fact]
    public void ManualHolidaysThatAreNowAutomatic_AreKeptOnDisk_WhenSaved()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vc-dkind-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "vacation-data.json");
            var store = new DataStore(path);
            store.Save(Data(Range("2026-12-21", "2026-12-23")).WithHolidays("2026-12-24", "2026-12-25", "2026-06-05"));

            var loaded = store.Load();
            store.Save(loaded);
            var again = store.Load();

            Assert.Equal(3, again.Years.Single().PublicHolidays.Count);
            Assert.Equal(3, again.Days.Count);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
