using VacationCalculator.Core;
using static VacationCalculator.Core.Tests.TestData;

namespace VacationCalculator.Core.Tests;

/// <summary>Feature 5, independently counted layouts and states for <see cref="CalendarYear.Build"/>.</summary>
public class CalendarYearIndependentTests
{
    private static readonly DateOnly Today = D("2026-10-25");

    private static CalendarYear Build(VacationData data, int year, string? today = null) =>
        CalendarYear.Build(data, year, today is null ? Today : D(today));

    private static CalendarDay DayOf(CalendarYear cal, string date)
    {
        var d = D(date);
        return cal.Months[d.Month - 1].Days.Single(x => x.Date == d);
    }

    [Fact]
    public void December2024_EndsInWeek1Of2025()
    {
        // Sun 2024-12-01. Mondays 11-25 (week 48), 12-02, 09, 16, 23 (week 52), 12-30 (Thursday 2025-01-02 -> week 1 of 2025).
        var dec = Build(Data(), 2024).Months[11];
        Assert.Equal([48, 49, 50, 51, 52, 1], dec.Weeks.Select(w => w.WeekNumber));
        Assert.Equal(2025, dec.Weeks[^1].WeekYear);
        Assert.Equal(2024, dec.Weeks[0].WeekYear);
        Assert.Equal(D("2025-01-05"), dec.Weeks[^1].Days[6].Date);
    }

    [Fact]
    public void January2024_StartsOnMondayWeek1_NoPadding()
    {
        // Mon 2024-01-01, Mondays 01, 08, 15, 22, 29 -> weeks 1..5; Wed 01-31 so the last week is padded with Thu..Sun.
        var jan = Build(Data(), 2024).Months[0];
        Assert.Equal([1, 2, 3, 4, 5], jan.Weeks.Select(w => w.WeekNumber));
        Assert.True(jan.Weeks[0].Days.All(d => d.IsInMonth));
        Assert.Equal([true, true, true, false, false, false, false], jan.Weeks[4].Days.Select(d => d.IsInMonth));
    }

    [Fact]
    public void January2023_StartsInWeek52Of2022()
    {
        // Sun 2023-01-01 belongs to the week of Mon 2022-12-26 (week 52 of 2022). Mondays 01-02 .. 01-30 are weeks 1..5; 6 rows.
        var jan = Build(Data(), 2023).Months[0];
        Assert.Equal([52, 1, 2, 3, 4, 5], jan.Weeks.Select(w => w.WeekNumber));
        Assert.Equal(2022, jan.Weeks[0].WeekYear);
        Assert.Equal([false, false, false, false, false, false, true], jan.Weeks[0].Days.Select(d => d.IsInMonth));
    }

    [Fact]
    public void Year2015_Has53Weeks()
    {
        // Thu 2015-01-01 -> 53-week year. Dec 2015: Mondays 11-30 (49), 12-07, 14, 21, 28 (53); Thu 12-31 ends in week 53.
        var dec = Build(Data(), 2015).Months[11];
        Assert.Equal([49, 50, 51, 52, 53], dec.Weeks.Select(w => w.WeekNumber));
        Assert.Equal(2015, dec.Weeks[^1].WeekYear);
        Assert.Equal(D("2015-12-31"), dec.Weeks[^1].Days[3].Date);
    }

    [Fact]
    public void Year2026_DoesNotRepeatWeek53InTheMiddle()
    {
        var weeks = Build(Data(), 2026).Months.SelectMany(m => m.Weeks).Where(w => w.WeekYear == 2026).Select(w => w.WeekNumber).Distinct().Order().ToList();
        Assert.Equal(Enumerable.Range(1, 53), weeks);
    }

    [Fact]
    public void LeapFebruary2000_Has29DaysAndNonLeap1900Has28()
    {
        // Tue 2000-02-01 .. Tue 02-29; Mon 2000-01-03 is week 1, so Mondays 01-31 (5), 02-07, 14, 21, 28 (9).
        var feb = Build(Data(), 2000).Months[1];
        Assert.Equal(29, feb.Days.Count());
        Assert.Equal([5, 6, 7, 8, 9], feb.Weeks.Select(w => w.WeekNumber));
        Assert.Equal(28, Build(Data(), 1900).Months[1].Days.Count());
    }

    [Fact]
    public void EasterSunday_IsHolidayKind_AndBookedConflict_EasterMondayHasName()
    {
        // Easter 2026 is Sun 04-05; Easter Monday Mon 04-06; Maundy Thursday Thu 04-02.
        var data = Data([Day("2026-04-05", type: DayType.Feriefri), Day("2026-04-02")]);
        var cal = Build(data, 2026);
        var sunday = DayOf(cal, "2026-04-05");
        Assert.True(sunday.IsWeekend);
        Assert.True(sunday.IsDanishHoliday);
        Assert.Equal(CalendarDayKind.PublicHoliday, sunday.Kind);
        Assert.True(sunday.IsConflict);
        Assert.Empty(sunday.Allocations);
        Assert.True(DayOf(cal, "2026-04-02").IsConflict);
        Assert.NotNull(DayOf(cal, "2026-04-06").HolidayName);
        Assert.False(DayOf(cal, "2026-04-07").IsPublicHoliday); // Tuesday after
    }

    [Fact]
    public void ZeroAmountBooking_IsNotAConflictOnAWorkingDay_AndHasNoAllocation()
    {
        var data = Data([Day("2026-10-26", amount: 0)]);
        var day = DayOf(Build(data, 2026), "2026-10-26");
        Assert.True(day.IsBooked);
        Assert.False(day.IsConflict);
        Assert.Empty(day.Allocations);
    }

    [Fact]
    public void Today_BeforeOnAfterABooking()
    {
        var data = Data([Day("2026-10-26")]); // Monday
        foreach (var (today, past, isToday) in new[] { ("2026-10-25", false, false), ("2026-10-26", false, true), ("2026-10-27", true, false) })
        {
            var day = DayOf(Build(data, 2026, today), "2026-10-26");
            Assert.Equal(past, day.IsPast);
            Assert.Equal(isToday, day.IsToday);
        }
    }

    [Fact]
    public void BookingOutsideTheYear_DoesNotShowInOtherYearsMonths()
    {
        var data = Data([Day("2026-06-02")]);
        var cal = Build(data, 2025);
        Assert.DoesNotContain(cal.Months.SelectMany(m => m.Weeks).SelectMany(w => w.Days), d => d.IsBooked);
    }

    [Fact]
    public void EmptyData_NoBookingsNoAllocations()
    {
        var cal = Build(Data(), 2026);
        Assert.Equal(12, cal.Months.Count);
        Assert.DoesNotContain(cal.Months.SelectMany(m => m.Days), d => d.IsBooked || d.Allocations.Count > 0);
        // Weekday Danish holidays 2026: 01-01, 04-02, 04-03, 04-06, 05-14, 05-25, 12-24, 12-25, 12-31 (9).
        Assert.Equal(9, cal.Months.SelectMany(m => m.Days).Count(d => d.IsDanishHoliday && !d.IsWeekend));
    }
}
