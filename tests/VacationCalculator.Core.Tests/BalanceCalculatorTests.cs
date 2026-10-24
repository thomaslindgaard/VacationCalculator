using VacationCalculator.Core;
using static VacationCalculator.Core.Tests.TestData;

namespace VacationCalculator.Core.Tests;

public class BalanceCalculatorTests
{
    private static readonly HashSet<DateOnly> NoHolidays = [];
    private static readonly HolidayYear Hy2025 = new(2025); // 1 Sep 2025 .. 31 Aug 2026, deadline 31 Dec 2026
    private static readonly HolidayYear Hy2026 = new(2026); // 1 Sep 2026 .. 31 Aug 2027, deadline 31 Dec 2027

    private static HolidayYearBalance Balance(VacationData data, HolidayYear year, string today) =>
        BalanceCalculator.CalculateHolidayYear(data, year, D(today));

    private static List<(DateOnly, int, decimal)> AllocationsFrom(VacationData data, string from) =>
        BalanceCalculator.Allocate(data)
            .Where(a => a.Date >= D(from))
            .Select(a => (a.Date, a.HolidayYear.StartYear, a.Amount))
            .ToList();

    // ---- Working days ----

    [Fact]
    public void CountWorkingDays_SkipsWeekends()
    {
        // Mon 2026-10-19 .. Sun 2026-10-25
        Assert.Equal(5, BalanceCalculator.CountWorkingDays(D("2026-10-19"), D("2026-10-25"), NoHolidays));
    }

    [Fact]
    public void CountWorkingDays_SkipsHolidays()
    {
        var holidays = new HashSet<DateOnly> { D("2026-12-24"), D("2026-12-25") };
        // Mon 2026-12-21 .. Fri 2026-12-25
        Assert.Equal(3, BalanceCalculator.CountWorkingDays(D("2026-12-21"), D("2026-12-25"), holidays));
    }

    // ---- Allocation ----

    [Fact]
    public void Allocate_31AugustGoesToTheOnlyOpenYearEvenWhenFull_1SeptemberSpillsToTheNewYear()
    {
        // Holiday year 2025 full (25 days in Jan/Feb 2026); Mon 2026-08-31, Tue 2026-09-01
        var data = Data(Fill(25).Append(Day("2026-08-31")).Append(Day("2026-09-01")));

        Assert.Equal([(D("2026-08-31"), 2025, 1m), (D("2026-09-01"), 2026, 1m)], AllocationsFrom(data, "2026-08-31"));
    }

    [Fact]
    public void Allocate_SeptemberToDecember_FillsTheOldYearFirstThenTheNewOne()
    {
        // 23 days used in holiday year 2025, so 2 are left. Then Mon 2026-10-19 .. Fri 2026-10-23.
        var data = Data(Fill(23).Concat(Range("2026-10-19", "2026-10-23")));

        Assert.Equal(
            [
                (D("2026-10-19"), 2025, 1m), (D("2026-10-20"), 2025, 1m),
                (D("2026-10-21"), 2026, 1m), (D("2026-10-22"), 2026, 1m), (D("2026-10-23"), 2026, 1m),
            ],
            AllocationsFrom(data, "2026-09-01"));
    }

    [Fact]
    public void Allocate_HalfDaysAllocateHalf()
    {
        // 23.5 used in 2025 (half on Thu 2026-02-05), 1.5 left.
        // Mon 2026-10-19 full -> 2025 (0.5 left), Tue 10-20 half -> 2025 (0 left), Wed 10-21 full -> 2026
        var days = Fill(23.5m).Concat([Day("2026-10-19"), Day("2026-10-20", 0.5m), Day("2026-10-21")]);

        Assert.Equal(
            [(D("2026-10-19"), 2025, 1m), (D("2026-10-20"), 2025, 0.5m), (D("2026-10-21"), 2026, 1m)],
            AllocationsFrom(Data(days), "2026-09-01"));
    }

    [Fact]
    public void Allocate_FullDayWithHalfLeftInOldYear_IsSplit()
    {
        // 24.5 used in 2025 (half on Fri 2026-02-06); Mon 2026-10-19 full
        var data = Data(Fill(24.5m).Append(Day("2026-10-19")));

        Assert.Equal([(D("2026-10-19"), 2025, 0.5m), (D("2026-10-19"), 2026, 0.5m)], AllocationsFrom(data, "2026-09-01"));
        Assert.Equal(0.5m, Balance(data, Hy2026, "2026-10-24").UsedDays);
        Assert.Equal(0m, Balance(data, Hy2025, "2026-10-24").UnbookedDays);
    }

    [Fact]
    public void Allocate_JanuaryAfterTheDeadline_GoesToTheNewYearEvenIfTheOldHasDaysLeft()
    {
        // Mon 2027-01-04; holiday year 2025 expired on Thu 2026-12-31
        var data = Data([Day("2027-01-04")]);

        Assert.Equal([(D("2027-01-04"), 2026, 1m)], AllocationsFrom(data, "2026-01-01"));
        Assert.Equal(25m, Balance(data, Hy2025, "2027-01-15").UnbookedDays);
    }

    [Fact]
    public void Allocate_31DecemberStillGoesToTheExpiringYear()
    {
        // Thu 2026-12-31 is holiday year 2025's last day; Fri 2027-01-01 is not
        var data = Data([Day("2026-12-31"), Day("2027-01-01")]);

        Assert.Equal([(D("2026-12-31"), 2025, 1m), (D("2027-01-01"), 2026, 1m)], AllocationsFrom(data, "2026-01-01"));
    }

    [Fact]
    public void Allocate_SkipsFeriefriWeekendsAndHolidays()
    {
        var data = Data(
        [
            Day("2026-10-19", type: DayType.Feriefri), // Mon
            Day("2026-10-24"),                         // Sat
            Day("2026-12-24"),                         // Thu, holiday
            Day("2026-12-23"),                         // Wed, counts
        ]).WithHolidays("2026-12-24");

        Assert.Equal([(D("2026-12-23"), 2025, 1m)], AllocationsFrom(data, "2026-01-01"));
    }

    [Fact]
    public void Allocate_CountsDuplicateRecordsForADateOnce()
    {
        var data = Data([Day("2026-08-03"), Day("2026-08-03", 0.5m)]); // Mon; first record wins

        Assert.Equal([(D("2026-08-03"), 2025, 1m)], AllocationsFrom(data, "2026-01-01"));
    }

    [Fact]
    public void Allocate_EmployedAfterTheOldYear_AutumnDaysGoToTheNewYear()
    {
        // Employed from Tue 2026-09-01, so holiday year 2025 earns nothing. Mon 2026-10-19.
        var data = Data([Day("2026-10-19")], employedSince: D("2026-09-01"));

        Assert.Equal([(D("2026-10-19"), 2026, 1m)], AllocationsFrom(data, "2026-01-01"));
    }

    [Fact]
    public void Allocate_RangeAcrossNewYear_SplitsByHolidayYear()
    {
        // Mon 2026-12-28 .. Fri 2027-01-08, no holidays: Mon 28 .. Thu 31 Dec (4) to 2025,
        // Fri 1 Jan + Mon 4 .. Fri 8 Jan (6) to 2026
        var data = Data(Range("2026-12-28", "2027-01-08"));

        var balance2025 = Balance(data, Hy2025, "2026-10-24");
        var balance2026 = Balance(data, Hy2026, "2026-10-24");

        Assert.Equal(4m, balance2025.BookedAheadDays);
        Assert.Equal(6m, balance2026.BookedAheadDays);
    }

    // ---- Holiday-year balance ----

    [Fact]
    public void Balance_SplitsUsedAndBookedAheadAroundToday()
    {
        // Mon 2026-07-06 .. Fri 2026-07-17 = 10 past; Mon 2026-12-21 .. Wed 2026-12-23 = 3 ahead. Today Sat 2026-10-24.
        var data = Data(Range("2026-07-06", "2026-07-17").Concat(Range("2026-12-21", "2026-12-23")));

        var b = Balance(data, Hy2025, "2026-10-24");

        Assert.Equal(25m, b.EarnedSoFar);
        Assert.Equal(25m, b.EarnableDays);
        Assert.Equal(10m, b.UsedDays);
        Assert.Equal(3m, b.BookedAheadDays);
        Assert.Equal(12m, b.RemainingDays);
        Assert.Equal(12m, b.UnbookedDays);
        Assert.Equal(D("2026-12-31"), b.Deadline);
        Assert.True(b.IsOpenOn(D("2026-10-24")));
        Assert.False(b.IsOpenOn(D("2027-01-01")));
    }

    [Fact]
    public void Balance_DayOnTodayCountsAsUsed()
    {
        // Mon 2026-10-19 .. Wed 2026-10-21, today Tue 2026-10-20
        var b = Balance(Data(Range("2026-10-19", "2026-10-21")), Hy2025, "2026-10-20");

        Assert.Equal(2m, b.UsedDays);
        Assert.Equal(1m, b.BookedAheadDays);
    }

    [Fact]
    public void Balance_AdvanceHoliday_RemainingGoesNegative()
    {
        // Employed from Tue 2026-09-01. Today Sat 2026-10-24: only September usable (2.08).
        // Mon 2026-10-19 .. Fri 2026-10-23 taken = 5.
        var data = Data(Range("2026-10-19", "2026-10-23"), employedSince: D("2026-09-01"));

        var b = Balance(data, Hy2026, "2026-10-24");

        Assert.Equal(M("2.08"), b.EarnedSoFar);
        Assert.Equal(5m, b.UsedDays);
        Assert.Equal(M("-2.92"), b.RemainingDays);
        Assert.Equal(M("2.92"), b.ExceedsEarnedSoFarBy);
        Assert.Equal(20m, b.UnbookedDays);
        Assert.Equal(0m, b.ExceedsEarnableBy);
    }

    [Fact]
    public void Balance_BookingsBeyondWhatCanBeEarnedBy31August()
    {
        // Mon 2027-01-04 .. Mon 2027-02-08 = 20 + 5 + 1 = 26 days for holiday year 2026. Today Sat 2026-10-24.
        var data = Data(Range("2027-01-04", "2027-02-08"));

        var b = Balance(data, Hy2026, "2026-10-24");

        Assert.Equal(26m, b.BookedAheadDays);
        Assert.Equal(-1m, b.UnbookedDays);
        Assert.Equal(1m, b.ExceedsEarnableBy);
        Assert.Equal(M("-23.92"), b.RemainingDays); // 2.08 earned so far - 26
        Assert.Equal(0m, b.PaidOutDays);
        Assert.Equal(0m, b.LostDays);
    }

    [Fact]
    public void Balance_FutureHolidayYearHasEarnedNothingYet()
    {
        var b = Balance(Data(), new HolidayYear(2027), "2026-10-24");

        Assert.Equal(0m, b.EarnedSoFar);
        Assert.Equal(25m, b.EarnableDays);
        Assert.Equal(25m, b.UnbookedDays);
    }

    // ---- Paid out vs lost at the deadline ----

    [Fact]
    public void Deadline_25Earned15Used_5PaidOut5Lost()
    {
        // 15 used Mon 2026-01-05 .. Fri 2026-01-23; today Fri 2027-01-15, after the deadline
        var b = Balance(Data(Fill(15)), Hy2025, "2027-01-15");

        Assert.Equal(10m, b.UnbookedDays);
        Assert.Equal(5m, b.FifthWeekDays);
        Assert.Equal(5m, b.UnusedFifthWeekDays);
        Assert.Equal(5m, b.PaidOutDays);
        Assert.Equal(5m, b.LostDays);
    }

    [Fact]
    public void Deadline_UsedDaysComeFromTheFirst20_SoTheRestIsPaidOut()
    {
        // 22 used: the 3 unused are 5th-week days
        var b = Balance(Data(Fill(22)), Hy2025, "2027-01-15");

        Assert.Equal(3m, b.PaidOutDays);
        Assert.Equal(0m, b.LostDays);
    }

    [Fact]
    public void Deadline_ProjectsFromPlannedDaysToo()
    {
        // Today Sat 2026-10-24: 15 used, Mon 2026-12-14 .. Fri 2026-12-18 = 5 planned -> 5 unused, all 5th week
        var b = Balance(Data(Fill(15).Concat(Range("2026-12-14", "2026-12-18"))), Hy2025, "2026-10-24");

        Assert.Equal(5m, b.PaidOutDays);
        Assert.Equal(0m, b.LostDays);
    }

    [Fact]
    public void Deadline_HalfDaysSplitCorrectly()
    {
        // 19.5 used: 5.5 unused = 5 paid out + 0.5 lost
        var b = Balance(Data(Fill(19.5m)), Hy2025, "2027-01-15");

        Assert.Equal(5m, b.PaidOutDays);
        Assert.Equal(0.5m, b.LostDays);
    }

    [Fact]
    public void Deadline_PartialYearOf20OrLess_HasNoFifthWeek_AllLost()
    {
        // Employed from Thu 2026-01-01: Jan .. Aug = 8 months = 16.6667
        var b = Balance(Data(employedSince: D("2026-01-01")), Hy2025, "2027-01-15");

        Assert.Equal(M("16.67"), b.EarnableDays);
        Assert.Equal(0m, b.FifthWeekDays);
        Assert.Equal(0m, b.PaidOutDays);
        Assert.Equal(M("16.67"), b.LostDays);
    }

    [Fact]
    public void Deadline_PartialYearAbove20_OnlyTheExcessIsPaidOut()
    {
        // Employed from Wed 2025-10-01: 11 months = 22.92, 5th week 2.92
        var b = Balance(Data(employedSince: D("2025-10-01")), Hy2025, "2027-01-15");

        Assert.Equal(M("2.92"), b.PaidOutDays);
        Assert.Equal(20m, b.LostDays);
    }

    [Fact]
    public void Deadline_EverythingUsed_NothingPaidOrLost()
    {
        var b = Balance(Data(Fill(25)), Hy2025, "2027-01-15");

        Assert.Equal(0m, b.UnbookedDays);
        Assert.Equal(0m, b.PaidOutDays);
        Assert.Equal(0m, b.LostDays);
    }

    [Fact]
    public void Deadline_OverBooked_NothingPaidOrLost()
    {
        var b = Balance(Data(Fill(26)), Hy2025, "2027-01-15");

        Assert.Equal(-1m, b.UnbookedDays);
        Assert.Equal(0m, b.UnusedFifthWeekDays);
        Assert.Equal(0m, b.PaidOutDays);
        Assert.Equal(0m, b.LostDays);
    }

    // ---- Transfer of the 5th week ----

    [Fact]
    public void Transfer_SettingOff_StoredTransferIsIgnored()
    {
        var data = Data(Fill(15), transferAllowed: false, transfers: (2025, 5m));

        var b2025 = Balance(data, Hy2025, "2027-01-15");
        var b2026 = Balance(data, Hy2026, "2027-01-15");

        Assert.Equal(0m, b2025.MaxTransferToNext);
        Assert.Equal(0m, b2025.TransferredToNext);
        Assert.Equal(5m, b2025.UnusedFifthWeekDays);
        Assert.Equal(5m, b2025.PaidOutDays);
        Assert.Equal(5m, b2025.LostDays);
        Assert.Equal(0m, b2026.TransferredIn);
        Assert.Equal(25m, b2026.TotalDays);
    }

    [Theory]
    [InlineData("3", "3", "2")]
    [InlineData("5", "5", "0")]
    [InlineData("8", "5", "0")]  // capped at the 5 unused 5th-week days
    [InlineData("-2", "0", "5")] // negative ignored
    [InlineData("0", "0", "5")]
    public void Transfer_SettingOn_IsClampedToUnusedFifthWeek(string stored, string effective, string paidOut)
    {
        // 15 used -> 10 unused, 5 of them 5th week
        var data = Data(Fill(15), transferAllowed: true, transfers: (2025, M(stored)));

        var b2025 = Balance(data, Hy2025, "2026-11-02");
        var b2026 = Balance(data, Hy2026, "2026-11-02");

        Assert.Equal(5m, b2025.MaxTransferToNext);
        Assert.Equal(M(effective), b2025.TransferredToNext);
        Assert.Equal(M(paidOut), b2025.PaidOutDays);
        Assert.Equal(5m, b2025.LostDays);
        Assert.Equal(10m - M(effective), b2025.UnbookedDays);
        Assert.Equal(M(effective), b2026.TransferredIn);
        Assert.Equal(25m + M(effective), b2026.TotalDays);
    }

    [Fact]
    public void Transfer_MaximumIsLimitedByUnusedDays()
    {
        // 22 used -> only 3 unused
        var b = Balance(Data(Fill(22), transferAllowed: true, transfers: (2025, 5m)), Hy2025, "2026-11-02");

        Assert.Equal(3m, b.MaxTransferToNext);
        Assert.Equal(3m, b.TransferredToNext);
        Assert.Equal(0m, b.PaidOutDays);
        Assert.Equal(0m, b.UnbookedDays);
    }

    [Fact]
    public void Transfer_MaximumIsZeroWhenEverythingIsUsedOrNoFifthWeek()
    {
        var allUsed = Balance(Data(Fill(25), transferAllowed: true), Hy2025, "2026-11-02");
        var partial = Balance(Data(employedSince: D("2026-01-01"), transferAllowed: true), Hy2025, "2026-11-02");

        Assert.Equal(0m, allUsed.MaxTransferToNext);
        Assert.Equal(0m, partial.MaxTransferToNext);
    }

    [Fact]
    public void Transfer_BookingMoreLaterShrinksTheEffectiveTransfer()
    {
        // 5 transferred, but then 23 days are booked in total: only 2 unused remain
        var b = Balance(Data(Fill(23), transferAllowed: true, transfers: (2025, 5m)), Hy2025, "2026-11-02");

        Assert.Equal(2m, b.TransferredToNext);
        Assert.Equal(0m, b.UnbookedDays);
    }

    [Fact]
    public void TransferredIn_IsPaidOutAtTheNextDeadline_NotLost()
    {
        // 5 transferred from 2025 to 2026; nothing booked in 2026. Today Sat 2028-01-15.
        var data = Data(Fill(15), transferAllowed: true, transfers: (2025, 5m));

        var b = Balance(data, Hy2026, "2028-01-15");

        Assert.Equal(30m, b.TotalDays);
        Assert.Equal(10m, b.FifthWeekDays);
        Assert.Equal(10m, b.PaidOutDays);
        Assert.Equal(20m, b.LostDays);
    }

    [Fact]
    public void TransferredIn_WithMostDaysUsed_RemainderIsPaidOut()
    {
        // 2026 total 30; 22 used Mon 2027-01-04 .. Tue 2027-02-02 -> 8 unused, all within the 10 5th-week days
        var data = Data(Fill(15).Concat(Range("2027-01-04", "2027-02-02")), transferAllowed: true, transfers: (2025, 5m));

        var b = Balance(data, Hy2026, "2028-01-15");

        Assert.Equal(22m, b.UsedDays);
        Assert.Equal(8m, b.PaidOutDays);
        Assert.Equal(0m, b.LostDays);
    }

    [Fact]
    public void TransferredIn_RaisesTheOldYearsCapacityInTheOverlap()
    {
        // 2026 total 30 (5 transferred in). 28 used Mon 2027-01-04 .. Wed 2027-02-10.
        // Mon 2027-09-06 .. Fri 2027-09-10: 6th and 7th to 2026, 8th to 10th to 2027.
        var days = Fill(15).Concat(Range("2027-01-04", "2027-02-10")).Concat(Range("2027-09-06", "2027-09-10"));
        var data = Data(days, transferAllowed: true, transfers: (2025, 5m));

        Assert.Equal(
            [
                (D("2027-09-06"), 2026, 1m), (D("2027-09-07"), 2026, 1m),
                (D("2027-09-08"), 2027, 1m), (D("2027-09-09"), 2027, 1m), (D("2027-09-10"), 2027, 1m),
            ],
            AllocationsFrom(data, "2027-09-01"));
    }

    [Fact]
    public void TransferredIn_CanBeTransferredAgain()
    {
        // 2025 -> 2026: 5. 2026 unused: 30, 5th week 10. 2026 -> 2027: 10 stored.
        var data = Data(Fill(15), transferAllowed: true, transfers: [(2025, 5m), (2026, 10m)]);

        var b2026 = Balance(data, Hy2026, "2027-11-01");
        var b2027 = Balance(data, new HolidayYear(2027), "2027-11-01");

        Assert.Equal(10m, b2026.MaxTransferToNext);
        Assert.Equal(10m, b2026.TransferredToNext);
        Assert.Equal(0m, b2026.PaidOutDays);
        Assert.Equal(20m, b2026.LostDays);
        Assert.Equal(10m, b2027.TransferredIn);
        Assert.Equal(35m, b2027.TotalDays);
    }

    [Fact]
    public void CalculateHolidayYears_ReturnsTheGivenYearsInOrder()
    {
        var balances = BalanceCalculator.CalculateHolidayYears(Data(Fill(3)), HolidayYear.InCalendarYear(2026), D("2026-10-24"));

        Assert.Equal([Hy2025, Hy2026], balances.Select(b => b.HolidayYear));
        Assert.Equal(3m, balances[0].UsedDays);
    }
}
