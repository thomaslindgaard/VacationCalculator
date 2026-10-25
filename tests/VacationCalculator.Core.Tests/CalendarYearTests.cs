using VacationCalculator.Core;
using static VacationCalculator.Core.Tests.TestData;

namespace VacationCalculator.Core.Tests;

/// <summary>
/// Feature 5: <see cref="CalendarYear.Build"/> lays out a calendar year for display. 2026: Thu 01-01, Tue 12-01,
/// Easter Sun 04-05, Thu 12-24, Fri 12-25, Sat 12-26, Thu 12-31. Default today Sun 2026-10-25.
/// </summary>
public class CalendarYearTests
{
    private static readonly DateOnly Today = D("2026-10-25"); // Sunday

    private static CalendarYear Build(VacationData data, int year, string? today = null) =>
        CalendarYear.Build(data, year, today is null ? Today : D(today));

    private static CalendarDay DayOf(CalendarYear cal, string date)
    {
        var d = D(date);
        return cal.Months[d.Month - 1].Days.Single(x => x.Date == d);
    }

    private static BookedDay Booking(string date, decimal amount, DayType type, string description = "") =>
        new() { Date = D(date), Amount = amount, Type = type, Description = description };

    // ---- Layout ----

    [Fact]
    public void January2026_StartsOnThursdayInWeek1()
    {
        // Thu 2026-01-01; Mondays 2025-12-29, 01-05, 01-12, 01-19, 01-26; Sat 01-31 ends the last week (Sun 02-01).
        var jan = Build(Data(), 2026).Months[0];

        Assert.Equal(Enumerable.Range(1, 12), Build(Data(), 2026).Months.Select(m => m.Month)); // no names: the App formats Month
        Assert.Equal(1, jan.Month);
        Assert.Equal(2026, jan.Year);
        Assert.Equal([1, 2, 3, 4, 5], jan.Weeks.Select(w => w.WeekNumber));
        Assert.All(jan.Weeks, w => Assert.Equal(2026, w.WeekYear));

        var firstWeek = jan.Weeks[0].Days;
        Assert.Equal(D("2025-12-29"), firstWeek[0].Date); // Monday padding
        Assert.Equal([false, false, false, true, true, true, true], firstWeek.Select(d => d.IsInMonth));
        Assert.Equal(DayOfWeek.Thursday, firstWeek[3].Date.DayOfWeek);
        Assert.Equal(D("2026-02-01"), jan.Weeks[4].Days[6].Date); // Sunday padding
        Assert.False(jan.Weeks[4].Days[6].IsInMonth);
        Assert.Equal(31, jan.Days.Count());
    }

    [Fact]
    public void December2026_IsWeeks49To53_AndNewYear2027IsWeek53Of2026()
    {
        // Tue 2026-12-01; Mondays 11-30, 12-07, 12-14, 12-21, 12-28; Fri 2027-01-01 in the week of 12-28.
        var dec = Build(Data(), 2026).Months[11];

        Assert.Equal([49, 50, 51, 52, 53], dec.Weeks.Select(w => w.WeekNumber));
        Assert.All(dec.Weeks, w => Assert.Equal(2026, w.WeekYear));
        var last = dec.Weeks[4].Days;
        Assert.Equal(D("2026-12-28"), last[0].Date);
        Assert.Equal(D("2027-01-01"), last[4].Date);
        Assert.False(last[4].IsInMonth);
        Assert.Equal(DanishHolidayKind.NewYearsDay, last[4].HolidayKind); // padding days carry their state too
    }

    [Fact]
    public void January2027_FirstWeekIsWeek53Of2026()
    {
        // Fri 2027-01-01; the week of Mon 2026-12-28 is week 53 of 2026; Mon 01-04 starts week 1 of 2027.
        var jan = Build(Data(), 2027).Months[0];

        Assert.Equal((53, 2026), (jan.Weeks[0].WeekNumber, jan.Weeks[0].WeekYear));
        Assert.Equal((1, 2027), (jan.Weeks[1].WeekNumber, jan.Weeks[1].WeekYear));
        Assert.Equal(D("2026-12-28"), jan.Weeks[0].Days[0].Date);
    }

    [Fact]
    public void Year2020HasWeek53_And2021_01_03IsInIt()
    {
        // Wed 2020-01-01 in a leap year -> 53 weeks. Thu 2020-12-31, Sun 2021-01-03.
        var dec2020 = Build(Data(), 2020).Months[11];
        Assert.Equal(53, dec2020.Weeks[^1].WeekNumber);
        Assert.Equal(2020, dec2020.Weeks[^1].WeekYear);

        // Fri 2021-01-01; Mondays 2020-12-28, 01-04, 01-11, 01-18, 01-25; Sun 01-31 ends the month.
        var jan2021 = Build(Data(), 2021).Months[0];
        Assert.Equal([53, 1, 2, 3, 4], jan2021.Weeks.Select(w => w.WeekNumber));
        Assert.Equal(2020, jan2021.Weeks[0].WeekYear);
        Assert.Equal(D("2021-01-03"), jan2021.Weeks[0].Days[6].Date);
        Assert.True(jan2021.Weeks[0].Days[6].IsInMonth);
    }

    [Fact]
    public void LastWeekOf2029_IsWeek1Of2030()
    {
        // Mon 2029-12-31: its Thursday is 2030-01-03, so it is week 1 of 2030.
        var dec = Build(Data(), 2029).Months[11];
        Assert.Equal(D("2029-12-31"), dec.Weeks[^1].Days[0].Date);
        Assert.Equal((1, 2030), (dec.Weeks[^1].WeekNumber, dec.Weeks[^1].WeekYear));
        Assert.Equal(6, dec.Weeks.Count); // Sat 2029-12-01 -> weeks of 11-26, 12-03, 10, 17, 24, 31
    }

    [Fact]
    public void LeapFebruary2028_Has29Days_Weeks5To9()
    {
        // Sat 2028-01-01 is in week 52 of 2027; week 1 starts Mon 01-03. Tue 02-01 .. Tue 02-29;
        // Mondays 01-31 (week 5), 02-07, 02-14, 02-21, 02-28 (week 9).
        var feb = Build(Data(), 2028).Months[1];

        Assert.Equal(29, feb.Days.Count());
        Assert.Equal(D("2028-02-29"), feb.Days.Last().Date);
        Assert.Equal([5, 6, 7, 8, 9], feb.Weeks.Select(w => w.WeekNumber));
        Assert.Equal(D("2028-01-31"), feb.Weeks[0].Days[0].Date);
        Assert.Equal(D("2028-03-05"), feb.Weeks[^1].Days[6].Date);
    }

    [Fact]
    public void February2026_HasExactlyFourWeeks()
    {
        // Sun 2026-02-01 and Sat 02-28: Mondays 01-26, 02-02, 02-09, 02-16, 02-23.
        var feb = Build(Data(), 2026).Months[1];
        Assert.Equal(5, feb.Weeks.Count);
        Assert.Equal(28, feb.Days.Count());

        // Mon 2021-02-01 .. Sun 02-28: exactly four weeks, no padding.
        var feb2021 = Build(Data(), 2021).Months[1];
        Assert.Equal(4, feb2021.Weeks.Count);
        Assert.All(feb2021.Weeks.SelectMany(w => w.Days), d => Assert.True(d.IsInMonth));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(1900)] // not a leap year
    [InlineData(2000)] // leap year
    [InlineData(2024)]
    [InlineData(2026)]
    [InlineData(2028)]
    [InlineData(9997)]
    [InlineData(9998)]
    public void EveryMonth_IsWholeMondayFirstWeeks_CoveringEachDayOnce(int year)
    {
        var cal = CalendarYear.Build(Data(), year, D("2026-10-25"));

        Assert.Equal(year, cal.Year);
        Assert.Equal(Enumerable.Range(1, 12), cal.Months.Select(m => m.Month));
        foreach (var month in cal.Months)
        {
            Assert.InRange(month.Weeks.Count, 4, 6);
            foreach (var week in month.Weeks)
            {
                Assert.Equal(7, week.Days.Count);
                Assert.Equal(DayOfWeek.Monday, week.Days[0].Date.DayOfWeek);
                for (var i = 1; i < 7; i++)
                    Assert.Equal(week.Days[i - 1].Date.AddDays(1), week.Days[i].Date);
                Assert.InRange(week.WeekNumber, 1, 53);
            }
            var days = month.Days.Select(d => d.Date).ToList();
            Assert.Equal(DateTime.DaysInMonth(year, month.Month), days.Count);
            Assert.Equal(new DateOnly(year, month.Month, 1), days[0]);
        }
        var all = cal.Months.SelectMany(m => m.Days).Select(d => d.Date).ToList();
        Assert.Equal(DateTime.IsLeapYear(year) ? 366 : 365, all.Distinct().Count());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9999)]
    [InlineData(-1)]
    public void YearsOutsideTheRange_Throw(int year) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CalendarYear.Build(Data(), year, Today));

    [Fact]
    public void ExtremeYears_BuildWithBookings()
    {
        // Year 2: Mon 0002-01-07 booked. Year 9998: Mon 9998-12-21 booked; its holiday year 9998 is the last representable.
        var data = Data([Day("0002-01-07"), Day("9998-12-21"), Day("9998-09-01", type: DayType.Feriefri)]);

        var y2 = CalendarYear.Build(data, 2, D("2026-10-25"));
        Assert.True(DayOf(y2, "0002-01-07").IsBooked);
        Assert.True(DayOf(y2, "0002-01-07").IsPast);

        var y9998 = CalendarYear.Build(data, 9998, D("2026-10-25"));
        var dec21 = DayOf(y9998, "9998-12-21");
        Assert.True(dec21.IsBooked);
        Assert.False(dec21.IsPast);
        Assert.NotEmpty(dec21.Allocations);
        // 9999-01-01 .. 9999-01-03 (Fri..Sun) pad the last week.
        Assert.Equal(D("9999-01-03"), y9998.Months[11].Weeks[^1].Days[6].Date);
    }

    // ---- Today / past ----

    [Fact]
    public void TodayAndPastFlags()
    {
        var cal = Build(Data(), 2026); // today Sun 2026-10-25

        Assert.True(DayOf(cal, "2026-10-25").IsToday);
        Assert.False(DayOf(cal, "2026-10-25").IsPast);
        Assert.True(DayOf(cal, "2026-10-24").IsPast);
        Assert.False(DayOf(cal, "2026-10-24").IsToday);
        Assert.False(DayOf(cal, "2026-10-26").IsPast);
        Assert.True(DayOf(cal, "2026-01-01").IsPast);
        Assert.Single(cal.Months.SelectMany(m => m.Days), d => d.IsToday);
    }

    [Fact]
    public void TodayInAnotherYear()
    {
        // Year after today: nothing past, not even the December 2026 padding of January 2027.
        var next = Build(Data(), 2027);
        Assert.DoesNotContain(next.Months.SelectMany(m => m.Weeks).SelectMany(w => w.Days), d => d.IsPast || d.IsToday);

        // Year before today: every day past.
        var prev = Build(Data(), 2025);
        Assert.All(prev.Months.SelectMany(m => m.Days), d => Assert.True(d.IsPast));
        Assert.DoesNotContain(prev.Months.SelectMany(m => m.Days), d => d.IsToday);
    }

    [Fact]
    public void TodayOnNewYearsEve_ShowsTodayInDecemberAndInJanuaryPadding()
    {
        // Thu 2026-12-31 is today; Fri 2027-01-01 is the next day.
        var cal = Build(Data(), 2026, "2026-12-31");
        Assert.True(DayOf(cal, "2026-12-31").IsToday);
        var padding = cal.Months[11].Weeks[^1].Days[4];
        Assert.Equal(D("2027-01-01"), padding.Date);
        Assert.False(padding.IsPast);

        // In January 2027's layout, the padding 2026-12-31 is today.
        var jan = Build(Data(), 2027, "2026-12-31").Months[0];
        Assert.True(jan.Weeks[0].Days[3].IsToday);
        Assert.False(jan.Weeks[0].Days[3].IsInMonth);
    }

    // ---- Kinds ----

    [Fact]
    public void Kinds_WorkingDayWeekendDanishAndManualHolidays()
    {
        // Manual: Fri 2026-06-05 (Grundlovsdag, not automatic), Sat 2026-06-06, and Thu 12-24 (also Danish).
        var data = Data().WithHolidays("2026-06-05", "2026-06-06", "2026-12-24");
        var cal = Build(data, 2026);

        var mon = DayOf(cal, "2026-06-01"); // Monday
        Assert.Equal(CalendarDayKind.WorkingDay, mon.Kind);
        Assert.True(mon.IsWorkingDay);
        Assert.Null(mon.HolidayKind);

        var sun = DayOf(cal, "2026-06-07"); // Sunday
        Assert.Equal(CalendarDayKind.Weekend, sun.Kind);
        Assert.True(sun.IsWeekend);
        Assert.False(sun.IsWorkingDay);

        var langfredag = DayOf(cal, "2026-04-03"); // Friday
        Assert.Equal(CalendarDayKind.PublicHoliday, langfredag.Kind);
        Assert.Equal(DanishHolidayKind.GoodFriday, langfredag.HolidayKind);
        Assert.True(langfredag.IsDanishHoliday);
        Assert.False(langfredag.IsManualHoliday);

        var grundlov = DayOf(cal, "2026-06-05"); // Friday, manual only
        Assert.Equal(CalendarDayKind.ManualHoliday, grundlov.Kind);
        Assert.Null(grundlov.HolidayKind);
        Assert.True(grundlov.IsManualHoliday);
        Assert.False(grundlov.IsDanishHoliday);
        Assert.False(grundlov.IsWorkingDay);

        var manualSat = DayOf(cal, "2026-06-06"); // Saturday, manual
        Assert.Equal(CalendarDayKind.ManualHoliday, manualSat.Kind);
        Assert.True(manualSat.IsWeekend);

        var xmasEve = DayOf(cal, "2026-12-24"); // Thursday, Danish and manual
        Assert.Equal(CalendarDayKind.PublicHoliday, xmasEve.Kind);
        Assert.True(xmasEve.IsDanishHoliday && xmasEve.IsManualHoliday);
        Assert.Equal(DanishHolidayKind.ChristmasEve, xmasEve.HolidayKind);

        var secondXmas = DayOf(cal, "2026-12-26"); // Saturday, Danish
        Assert.Equal(CalendarDayKind.PublicHoliday, secondXmas.Kind);
        Assert.True(secondXmas.IsWeekend);
        Assert.Equal(DanishHolidayKind.SecondChristmasDay, secondXmas.HolidayKind);

        Assert.Equal(DanishHolidayKind.NewYearsDay, DayOf(cal, "2026-01-01").HolidayKind);
        Assert.Equal(DanishHolidayKind.NewYearsEve, DayOf(cal, "2026-12-31").HolidayKind);
    }

    [Theory]
    [InlineData(2026)] // Easter Sun 04-05
    [InlineData(2027)] // Easter Sun 03-28; January's first week is padded with Mon 2026-12-28 .. Thu 2026-12-31 (NewYearsEve)
    [InlineData(CalendarYear.MinYear)]
    [InlineData(CalendarYear.MaxYear)]
    public void HolidayKind_IsSetExactlyOnDanishHolidays_IncludingPaddingDays(int year)
    {
        // Manual Fri 2026-06-05 (not Danish) keeps a null kind; manual Thu 2026-12-24 keeps ChristmasEve.
        var cal = Build(Data().WithHolidays("2026-06-05", "2026-12-24"), year, "2026-10-24");

        var days = cal.Months.SelectMany(m => m.Weeks).SelectMany(w => w.Days).ToList();
        Assert.All(days, d => Assert.Equal(DanishHolidays.KindOf(d.Date), d.HolidayKind));
        Assert.All(days, d => Assert.Equal(d.HolidayKind is not null, d.IsDanishHoliday));
        Assert.Equal(12, days.Where(d => d.IsInMonth && d.HolidayKind is not null).Count());
        if (year == 2026)
        {
            Assert.Null(DayOf(cal, "2026-06-05").HolidayKind);
            Assert.True(DayOf(cal, "2026-06-05").IsManualHoliday);
        }
    }

    [Fact]
    public void ManualHolidayStoredUnderTheWrongYear_IsIgnored()
    {
        // Fri 2026-06-05 stored in the settings of 2027: not a holiday (as everywhere else).
        var data = Data();
        data.Years.Add(new YearSettings { Year = 2027, PublicHolidays = [D("2026-06-05")] });
        var day = DayOf(Build(data, 2026), "2026-06-05");
        Assert.False(day.IsManualHoliday);
        Assert.True(day.IsWorkingDay);
    }

    [Fact]
    public void WorkingDayFlag_MatchesBalanceCalculator()
    {
        var data = Data().WithHolidays("2026-06-05", "2026-11-02");
        var manual = data.AllPublicHolidays();
        foreach (var day in Build(data, 2026).Months.SelectMany(m => m.Weeks).SelectMany(w => w.Days))
            Assert.Equal(BalanceCalculator.IsWorkingDay(day.Date, manual), day.IsWorkingDay);
    }

    // ---- Bookings ----

    [Fact]
    public void Bookings_MappedToTheirDays()
    {
        var data = Data(
        [
            Booking("2026-10-26", 1, DayType.Vacation, "Autumn"),     // Mon
            Booking("2026-10-27", 0.5m, DayType.Vacation),            // Tue
            Booking("2026-10-28", 1, DayType.Feriefri, "Free"),       // Wed
            Booking("2026-10-29", 0.5m, DayType.Feriefri),            // Thu
        ]);
        var cal = Build(data, 2026);

        var mon = DayOf(cal, "2026-10-26");
        Assert.Equal(DayType.Vacation, mon.BookedType);
        Assert.Equal(1m, mon.BookedAmount);
        Assert.True(mon.IsFullDay);
        Assert.False(mon.IsHalfDay);
        Assert.Equal("Autumn", mon.Description);
        Assert.False(mon.IsConflict);
        // Holiday year 2025 (older, 25 days left) takes an October 2026 vacation day.
        Assert.Equal("2025/26", mon.HolidayYearsText);
        Assert.Equal([new AllocatedDay(D("2026-10-26"), new HolidayYear(2025), 1m)], mon.Allocations);

        var tue = DayOf(cal, "2026-10-27");
        Assert.Equal(DayType.Vacation, tue.BookedType);
        Assert.Equal(0.5m, tue.BookedAmount);
        Assert.True(tue.IsHalfDay);
        Assert.False(tue.IsFullDay);

        var wed = DayOf(cal, "2026-10-28");
        Assert.Equal(DayType.Feriefri, wed.BookedType);
        Assert.True(wed.IsFullDay);
        Assert.Equal("Free", wed.Description);
        Assert.Equal("2025/26", wed.HolidayYearsText);

        var thu = DayOf(cal, "2026-10-29");
        Assert.Equal(DayType.Feriefri, thu.BookedType);
        Assert.True(thu.IsHalfDay);

        var fri = DayOf(cal, "2026-10-30");
        Assert.False(fri.IsBooked);
        Assert.Null(fri.BookedType);
        Assert.Equal(0m, fri.BookedAmount);
        Assert.Equal("", fri.Description);
        Assert.Empty(fri.Allocations);
        Assert.Equal("", fri.HolidayYearsText);
    }

    [Fact]
    public void Conflicts_BookingsOnHolidaysAndWeekends()
    {
        // Thu 2026-12-24 (Danish), Sat 2026-10-31 (weekend), Fri 2026-06-05 (manual), Mon 2026-10-26 (fine).
        var data = Data([Day("2026-12-24"), Day("2026-10-31", 0.5m), Day("2026-06-05", type: DayType.Feriefri), Day("2026-10-26")])
            .WithHolidays("2026-06-05");
        var cal = Build(data, 2026);

        foreach (var date in new[] { "2026-12-24", "2026-10-31", "2026-06-05" })
        {
            var day = DayOf(cal, date);
            Assert.True(day.IsBooked, date);
            Assert.True(day.IsConflict, date);
            Assert.Empty(day.Allocations); // not counted
        }
        Assert.False(DayOf(cal, "2026-10-26").IsConflict);
        Assert.Equal(
            data.BookedDaysOnHolidays(2026).Select(d => d.Date).Append(D("2026-10-31")).Order(),
            cal.Months.SelectMany(m => m.Days).Where(d => d.IsConflict).Select(d => d.Date));
    }

    [Fact]
    public void DuplicateRecords_FirstWins()
    {
        var data = Data([Day("2026-10-26", 0.5m, DayType.Feriefri), Day("2026-10-26", 1, DayType.Vacation)]);
        var day = DayOf(Build(data, 2026), "2026-10-26");
        Assert.Equal(DayType.Feriefri, day.BookedType);
        Assert.Equal(0.5m, day.BookedAmount);
        Assert.Equal([new HolidayYear(2025)], day.Allocations.Select(a => a.HolidayYear));
    }

    [Fact]
    public void SplitDay_ShowsBothHolidayYears()
    {
        // 24.5 days from Mon 2026-01-05 leave holiday year 2025 half a day; Mon 2026-10-26 is split 0.5 / 0.5.
        var data = Data(Fill(M("24.5")).Append(Day("2026-10-26")));
        var day = DayOf(Build(data, 2026), "2026-10-26");

        Assert.Equal(
            [new AllocatedDay(D("2026-10-26"), new HolidayYear(2025), 0.5m), new AllocatedDay(D("2026-10-26"), new HolidayYear(2026), 0.5m)],
            day.Allocations);
        Assert.Equal("2025/26, 2026/27", day.HolidayYearsText);
    }

    [Fact]
    public void BookingsAcrossNewYear_ShowInPaddingAndOwnYear()
    {
        // Records stored on every weekday Mon 2026-12-28 .. Fri 2027-01-01 (e.g. from an old file): Thu 12-31 and
        // Fri 01-01 are Danish holidays (conflicts).
        var data = Data([Day("2026-12-28"), Day("2026-12-29"), Day("2026-12-30"), Day("2026-12-31"), Day("2027-01-01")]);
        var dec = Build(data, 2026).Months[11];
        var lastWeek = dec.Weeks[^1].Days;
        Assert.True(lastWeek[0].IsBooked && !lastWeek[0].IsConflict); // Mon 12-28
        Assert.True(lastWeek[3].IsConflict); // Thu 12-31
        Assert.True(lastWeek[4].IsConflict && !lastWeek[4].IsInMonth); // Fri 2027-01-01, padding

        var jan2027 = Build(data, 2027).Months[0];
        Assert.True(jan2027.Weeks[0].Days[0].IsBooked); // padding Mon 2026-12-28
        Assert.Equal("2025/26", jan2027.Weeks[0].Days[0].HolidayYearsText);
        Assert.True(DayOf(Build(data, 2027), "2027-01-01").IsConflict);
    }

    [Fact]
    public void NullCollectionsInData_DoNotThrow()
    {
        var data = new VacationData { Days = null!, Years = null!, HolidayYears = null! };
        var cal = Build(data, 2026);
        Assert.Equal(12, cal.Months.Count);
        Assert.DoesNotContain(cal.Months.SelectMany(m => m.Days), d => d.IsBooked);
    }
}
