using VacationCalculator.Core;

namespace VacationCalculator.Core.Tests;

public class BalanceCalculatorTests
{
    private static readonly HashSet<DateOnly> NoHolidays = [];

    private static VacationEntry Entry(string start, string end) =>
        new() { Start = DateOnly.Parse(start), End = DateOnly.Parse(end) };

    private static YearSettings Settings(decimal allowance, decimal carryOver = 0, params string[] holidays) =>
        new()
        {
            Year = 2026,
            AllowanceDays = allowance,
            CarryOverDays = carryOver,
            PublicHolidays = holidays.Select(DateOnly.Parse).ToList(),
        };

    [Fact]
    public void CountWorkingDays_SkipsWeekends()
    {
        // Mon 2026-10-19 .. Sun 2026-10-25
        var days = BalanceCalculator.CountWorkingDays(new(2026, 10, 19), new(2026, 10, 25), NoHolidays);
        Assert.Equal(5, days);
    }

    [Fact]
    public void CountWorkingDays_SkipsHolidays()
    {
        var holidays = new HashSet<DateOnly> { new(2026, 12, 24), new(2026, 12, 25) };
        // Mon 2026-12-21 .. Fri 2026-12-25
        var days = BalanceCalculator.CountWorkingDays(new(2026, 12, 21), new(2026, 12, 25), holidays);
        Assert.Equal(3, days);
    }

    [Fact]
    public void CountWorkingDaysInYear_OnlyCountsDaysInsideTheYear()
    {
        // Mon 2026-12-28 .. Fri 2027-01-08: 4 working days in 2026, 6 in 2027
        var entry = Entry("2026-12-28", "2027-01-08");
        Assert.Equal(4, BalanceCalculator.CountWorkingDaysInYear(entry, 2026, NoHolidays));
        Assert.Equal(6, BalanceCalculator.CountWorkingDaysInYear(entry, 2027, NoHolidays));
    }

    [Fact]
    public void Calculate_SplitsUsedAndPlannedAroundToday()
    {
        var entries = new[]
        {
            Entry("2026-07-06", "2026-07-17"), // 10 days, past
            Entry("2026-12-21", "2026-12-23"), // 3 days, future
        };

        var balance = BalanceCalculator.Calculate(Settings(25), entries, today: new(2026, 10, 24));

        Assert.Equal(10, balance.UsedDays);
        Assert.Equal(3, balance.PlannedDays);
        Assert.Equal(12, balance.RemainingDays);
        Assert.Equal(BalanceStatus.DaysToPlan, balance.Status);
    }

    [Fact]
    public void Calculate_IncludesCarryOver()
    {
        var balance = BalanceCalculator.Calculate(Settings(25, carryOver: 5), [], today: new(2026, 1, 1));
        Assert.Equal(30, balance.TotalDays);
        Assert.Equal(30, balance.RemainingDays);
    }

    [Fact]
    public void Calculate_DoesNotDoubleCountOverlappingEntries()
    {
        var entries = new[]
        {
            Entry("2026-08-03", "2026-08-07"),
            Entry("2026-08-05", "2026-08-11"),
        };

        var balance = BalanceCalculator.Calculate(Settings(25), entries, today: new(2026, 10, 24));

        // Mon 08-03 .. Tue 08-11 = 7 distinct working days
        Assert.Equal(7, balance.UsedDays);
    }

    [Fact]
    public void Calculate_FlagsDaysThatCannotFitBeforeYearEnd()
    {
        // From Fri 2026-12-18, working days left: 21,22,23,24,25,28,29,30,31 = 9
        var balance = BalanceCalculator.Calculate(Settings(25), [], today: new(2026, 12, 18));

        Assert.Equal(9, balance.WorkingDaysLeft);
        Assert.Equal(BalanceStatus.DaysWillBeLost, balance.Status);
        Assert.Equal(16, balance.DaysAtRisk);
    }

    [Fact]
    public void Calculate_WorkingDaysLeftExcludesHolidaysAndBookedDays()
    {
        var entries = new[] { Entry("2026-12-21", "2026-12-23") };
        var settings = Settings(25, 0, "2026-12-24", "2026-12-25", "2026-12-31");

        var balance = BalanceCalculator.Calculate(settings, entries, today: new(2026, 12, 18));

        // 21,22,23 booked; 24,25,31 holidays; leaves 28,29,30
        Assert.Equal(3, balance.WorkingDaysLeft);
    }

    [Fact]
    public void Calculate_ReportsOverBooking()
    {
        var entries = new[] { Entry("2026-07-06", "2026-07-17") };
        var balance = BalanceCalculator.Calculate(Settings(5), entries, today: new(2026, 10, 24));

        Assert.Equal(-5, balance.RemainingDays);
        Assert.Equal(BalanceStatus.OverBooked, balance.Status);
    }

    [Fact]
    public void Calculate_ReportsAllPlanned()
    {
        var entries = new[] { Entry("2026-07-06", "2026-07-10") };
        var balance = BalanceCalculator.Calculate(Settings(5), entries, today: new(2026, 10, 24));

        Assert.Equal(BalanceStatus.AllPlanned, balance.Status);
    }

    [Fact]
    public void Calculate_PastYearWithUnusedDaysIsExpired()
    {
        var balance = BalanceCalculator.Calculate(Settings(25), [], today: new(2027, 2, 1));

        Assert.Equal(BalanceStatus.Expired, balance.Status);
        Assert.Equal(0, balance.WorkingDaysLeft);
    }

    [Fact]
    public void Calculate_FutureYearCountsAllWorkingDaysAsLeft()
    {
        var balance = BalanceCalculator.Calculate(Settings(25), [], today: new(2025, 6, 1));

        Assert.Equal(261, balance.WorkingDaysLeft); // 2026 has 261 weekdays
        Assert.Equal(BalanceStatus.DaysToPlan, balance.Status);
    }
}
