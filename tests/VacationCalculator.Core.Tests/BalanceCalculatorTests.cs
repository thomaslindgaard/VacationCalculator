using VacationCalculator.Core;

namespace VacationCalculator.Core.Tests;

public class BalanceCalculatorTests
{
    private static readonly HashSet<DateOnly> NoHolidays = [];

    private static DateOnly D(string date) => DateOnly.Parse(date);

    /// <summary>Full vacation days on every weekday of the inclusive range.</summary>
    private static List<BookedDay> Range(string start, string end, DayType type = DayType.Vacation) =>
        Bookings.BookRange([], D(start), D(end), type, "", NoHolidays);

    private static BookedDay Day(string date, decimal amount = 1, DayType type = DayType.Vacation) =>
        new() { Date = D(date), Amount = amount, Type = type };

    private static YearSettings Settings(decimal allowance, decimal carryOver = 0, params string[] holidays) =>
        Settings(2026, allowance, carryOver, holidays);

    private static YearSettings Settings(int year, decimal allowance, decimal carryOver = 0, params string[] holidays) =>
        new()
        {
            Year = year,
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
    public void Calculate_SplitsUsedAndPlannedAroundToday()
    {
        // Mon 2026-07-06 .. Fri 2026-07-17 = 10 days, past; Mon 2026-12-21 .. Wed 2026-12-23 = 3 days, future
        var days = Range("2026-07-06", "2026-07-17").Concat(Range("2026-12-21", "2026-12-23"));

        var balance = BalanceCalculator.Calculate(Settings(25), days, today: new(2026, 10, 24)); // Sat

        Assert.Equal(10, balance.UsedDays);
        Assert.Equal(3, balance.PlannedDays);
        Assert.Equal(12, balance.RemainingDays);
        Assert.Equal(BalanceStatus.DaysToPlan, balance.Status);
    }

    [Fact]
    public void Calculate_DayOnTodayCountsAsUsed()
    {
        // Mon 2026-10-19 .. Wed 2026-10-21, today Tue 2026-10-20
        var balance = BalanceCalculator.Calculate(Settings(25), Range("2026-10-19", "2026-10-21"), today: new(2026, 10, 20));

        Assert.Equal(2, balance.UsedDays);
        Assert.Equal(1, balance.PlannedDays);
    }

    [Fact]
    public void Calculate_IncludesCarryOver()
    {
        var balance = BalanceCalculator.Calculate(Settings(25, carryOver: 5), [], today: new(2026, 1, 1));
        Assert.Equal(30, balance.TotalDays);
        Assert.Equal(30, balance.RemainingDays);
    }

    [Fact]
    public void Calculate_HalfDaysCountAsHalfInUsedPlannedAndRemaining()
    {
        var days = new[]
        {
            Day("2026-07-06", 0.5m), // Mon, past
            Day("2026-12-21", 0.5m), // Mon, future
            Day("2026-12-22"),       // Tue, future
        };

        var balance = BalanceCalculator.Calculate(Settings(25, carryOver: 0.5m), days, today: new(2026, 10, 24));

        Assert.Equal(0.5m, balance.UsedDays);
        Assert.Equal(1.5m, balance.PlannedDays);
        Assert.Equal(23.5m, balance.RemainingDays);
    }

    [Fact]
    public void Calculate_IgnoresFeriefriDaysInUsedAndPlanned()
    {
        var days = new[]
        {
            Day("2026-07-06", type: DayType.Feriefri), // Mon, past
            Day("2026-12-21", type: DayType.Feriefri), // Mon, future
            Day("2026-12-22"),                         // Tue, future vacation
        };

        var balance = BalanceCalculator.Calculate(Settings(25), days, today: new(2026, 10, 24));

        Assert.Equal(0, balance.UsedDays);
        Assert.Equal(1, balance.PlannedDays);
    }

    [Fact]
    public void Calculate_IgnoresRecordsOnWeekendsAndHolidays()
    {
        var days = new[]
        {
            Day("2026-07-11"), // Sat
            Day("2026-12-24"), // Thu, holiday
            Day("2026-12-23"), // Wed, counts
        };

        var balance = BalanceCalculator.Calculate(Settings(25, 0, "2026-12-24"), days, today: new(2026, 10, 24));

        Assert.Equal(0, balance.UsedDays);
        Assert.Equal(1, balance.PlannedDays);
    }

    [Fact]
    public void Calculate_OnlyCountsDaysInsideTheYear()
    {
        // Mon 2026-12-28 .. Fri 2027-01-08: 4 working days in 2026, 6 in 2027
        var days = Range("2026-12-28", "2027-01-08");

        var b2026 = BalanceCalculator.Calculate(Settings(2026, 25), days, today: new(2026, 10, 24));
        var b2027 = BalanceCalculator.Calculate(Settings(2027, 25), days, today: new(2026, 10, 24));

        Assert.Equal(4, b2026.PlannedDays);
        Assert.Equal(6, b2027.PlannedDays);
    }

    [Fact]
    public void Calculate_CountsDuplicateRecordsForADateOnce()
    {
        var days = new[] { Day("2026-08-03"), Day("2026-08-03") }; // Mon

        var balance = BalanceCalculator.Calculate(Settings(25), days, today: new(2026, 10, 24));

        Assert.Equal(1, balance.UsedDays);
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
    public void Calculate_DaysAtRiskCanBeHalf()
    {
        // From Fri 2026-12-18: 9 free working days left, 9.5 remaining
        var balance = BalanceCalculator.Calculate(Settings(9.5m), [], today: new(2026, 12, 18));

        Assert.Equal(BalanceStatus.DaysWillBeLost, balance.Status);
        Assert.Equal(0.5m, balance.DaysAtRisk);
    }

    [Fact]
    public void Calculate_WorkingDaysLeftExcludesHolidaysAndBookedDays()
    {
        var settings = Settings(25, 0, "2026-12-24", "2026-12-25", "2026-12-31");

        // Mon 2026-12-21 .. Wed 2026-12-23 booked
        var balance = BalanceCalculator.Calculate(settings, Range("2026-12-21", "2026-12-23"), today: new(2026, 12, 18));

        // 21,22,23 booked; 24,25,31 holidays; leaves Mon 28, Tue 29, Wed 30
        Assert.Equal(3, balance.WorkingDaysLeft);
    }

    [Fact]
    public void Calculate_HalfBookedDayLeavesHalfAFreeWorkingDay()
    {
        var settings = Settings(25, 0, "2026-12-24", "2026-12-25", "2026-12-31");
        var days = new[]
        {
            Day("2026-12-21", 0.5m), // Mon, half
            Day("2026-12-22"),       // Tue, full
        };

        var balance = BalanceCalculator.Calculate(settings, days, today: new(2026, 12, 18)); // Fri

        // 21 = 0.5, 22 = 0, 23 = 1, 28/29/30 = 3
        Assert.Equal(4.5m, balance.WorkingDaysLeft);
    }

    [Fact]
    public void Calculate_FeriefriDayIsNotAFreeWorkingDay()
    {
        var days = new[]
        {
            Day("2026-12-28", type: DayType.Feriefri),       // Mon, full
            Day("2026-12-29", 0.5m, type: DayType.Feriefri), // Tue, half
        };

        var balance = BalanceCalculator.Calculate(Settings(25), days, today: new(2026, 12, 25)); // Fri

        // Free: 28 = 0, 29 = 0.5, Wed 30 = 1, Thu 31 = 1
        Assert.Equal(2.5m, balance.WorkingDaysLeft);
        Assert.Equal(0, balance.PlannedDays);
    }

    [Fact]
    public void Calculate_ReportsOverBooking()
    {
        var balance = BalanceCalculator.Calculate(Settings(5), Range("2026-07-06", "2026-07-17"), today: new(2026, 10, 24));

        Assert.Equal(-5, balance.RemainingDays);
        Assert.Equal(BalanceStatus.OverBooked, balance.Status);
        Assert.Equal(0, balance.DaysAtRisk);
    }

    [Fact]
    public void Calculate_ReportsOverBookingByHalfADay()
    {
        // Mon 2026-07-06 .. Fri 2026-07-10 full, plus half on Mon 2026-07-13
        var days = Range("2026-07-06", "2026-07-10").Append(Day("2026-07-13", 0.5m));

        var balance = BalanceCalculator.Calculate(Settings(5), days, today: new(2026, 10, 24));

        Assert.Equal(-0.5m, balance.RemainingDays);
        Assert.Equal(BalanceStatus.OverBooked, balance.Status);
    }

    [Fact]
    public void Calculate_ReportsAllPlanned()
    {
        // Mon 2026-07-06 .. Fri 2026-07-10
        var balance = BalanceCalculator.Calculate(Settings(5), Range("2026-07-06", "2026-07-10"), today: new(2026, 10, 24));

        Assert.Equal(BalanceStatus.AllPlanned, balance.Status);
    }

    [Fact]
    public void Calculate_ReportsAllPlannedWithHalfDays()
    {
        // Mon 2026-07-06 .. Thu 2026-07-09 full, Fri 2026-07-10 half = 4.5
        var days = Range("2026-07-06", "2026-07-09").Append(Day("2026-07-10", 0.5m));

        var balance = BalanceCalculator.Calculate(Settings(4.5m), days, today: new(2026, 10, 24));

        Assert.Equal(0, balance.RemainingDays);
        Assert.Equal(BalanceStatus.AllPlanned, balance.Status);
    }

    [Fact]
    public void Calculate_ZeroAllowanceWithoutBookingsIsAllPlanned()
    {
        var balance = BalanceCalculator.Calculate(Settings(0), [], today: new(2026, 10, 24));

        Assert.Equal(0, balance.RemainingDays);
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
    public void Calculate_OnLastDayOfYearNoWorkingDaysAreLeft()
    {
        // Thu 2026-12-31 is today; nothing is left after it
        var balance = BalanceCalculator.Calculate(Settings(1), [], today: new(2026, 12, 31));

        Assert.Equal(0, balance.WorkingDaysLeft);
        Assert.Equal(BalanceStatus.DaysWillBeLost, balance.Status);
    }

    [Fact]
    public void Calculate_FutureYearCountsAllWorkingDaysAsLeft()
    {
        var balance = BalanceCalculator.Calculate(Settings(25), [], today: new(2025, 6, 1));

        Assert.Equal(261, balance.WorkingDaysLeft); // 2026 has 261 weekdays
        Assert.Equal(BalanceStatus.DaysToPlan, balance.Status);
    }

    [Fact]
    public void Calculate_LeapYearCountsFebruary29()
    {
        // 2028 is a leap year: Sat 2028-01-01 .. Sun 2028-12-31, 260 weekdays. Tue 2028-02-29 half booked.
        var days = new[] { Day("2028-02-29", 0.5m) };

        var balance = BalanceCalculator.Calculate(Settings(2028, 25), days, today: new(2027, 12, 31));

        Assert.Equal(0.5m, balance.PlannedDays);
        Assert.Equal(259.5m, balance.WorkingDaysLeft);
    }
}
