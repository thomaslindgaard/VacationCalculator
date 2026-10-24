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
        AllocateOn2026_10_24(data)
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
    public void Allocate_LastWorkingDayOfDecemberStillGoesToTheExpiringYear()
    {
        // Wed 2026-12-30 is holiday year 2025's last working day (Thu 12-31 is Nytårsaftensdag); Mon 2027-01-04 is the
        // first of 2027 (Fri 01-01 is Nytårsdag). Stored records on the two holidays aren't allocated.
        var data = Data([Day("2026-12-30"), Day("2026-12-31"), Day("2027-01-01"), Day("2027-01-04")]);

        Assert.Equal([(D("2026-12-30"), 2025, 1m), (D("2027-01-04"), 2026, 1m)], AllocationsFrom(data, "2026-01-01"));
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
        // Mon 2026-12-28 .. Fri 2027-01-08, no manual holidays: Mon 28 .. Wed 30 Dec (3) to 2025,
        // Mon 4 .. Fri 8 Jan (5) to 2026 (Thu 31 Dec and Fri 1 Jan are Danish holidays)
        var data = Data(Range("2026-12-28", "2027-01-08"));

        var balance2025 = Balance(data, Hy2025, "2026-10-24");
        var balance2026 = Balance(data, Hy2026, "2026-10-24");

        Assert.Equal(3m, balance2025.BookedAheadDays);
        Assert.Equal(5m, balance2026.BookedAheadDays);
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
        var data = Data(Range("2027-01-04", "2027-02-08")).NoTransferFrom(2025); // nothing transferred in

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
        // 2c: 2025 and 2026 (tracked from 2025, nothing booked) each transfer their 5th week by default, so 2027
        // receives a projected 5 from 2026
        Assert.Equal(5m, b.TransferredIn);
        Assert.True(b.TransferredInIsProjected);
        Assert.Equal(30m, b.UnbookedDays);
    }

    // ---- Deadline split, defaults (2c: no switch, the default limit 5 transfers the 5th week) ----

    [Fact]
    public void Deadline_Defaults_FifthWeekTransferred_RestLost()
    {
        // 15 used Mon 2026-01-05 .. Fri 2026-01-23; today Fri 2027-01-15, after the deadline. 2025 tracked.
        var data = Data(Fill(15), firstTracked: 2025);
        var b = Balance(data, Hy2025, "2027-01-15");

        Assert.Equal(10m, b.UnbookedDays);
        Assert.Equal(5m, b.FifthWeekDays);
        Assert.Equal(5m, b.UnusedFifthWeekDays);
        Assert.Equal(5m, b.TransferredToNext);
        Assert.Equal(0m, b.PaidOutDays);
        Assert.Equal(5m, b.LostDays);
        Assert.Equal(5m, b.Settings.TransferLimitDays);
        Assert.Null(b.Settings.TransferLimitFrom); // the default, nothing stored
        Assert.Equal(PayoutMode.None, b.Settings.PayoutMode);
        var next = Balance(data, Hy2026, "2027-01-15");
        Assert.Equal(5m, next.TransferredIn);
        Assert.False(next.TransferredInIsProjected); // the deadline has passed
        Assert.Empty(data.HolidayYears); // reading the default stores nothing
    }

    [Fact]
    public void Deadline_Defaults_FifthWeekTransferProjected_BeforeTheDeadline()
    {
        // Same bookings, today Sat 2026-10-24 (before the 2025 deadline, default first tracked year 2025)
        var data = Data(Fill(15));

        Assert.Equal(5m, Balance(data, Hy2025, "2026-10-24").TransferredToNext);
        var next = Balance(data, Hy2026, "2026-10-24");
        Assert.Equal(5m, next.TransferredIn);
        Assert.True(next.TransferredInIsProjected);
    }

    // ---- Deadline split, transfer off (limit 0) ----

    [Fact]
    public void Deadline_LimitZero_FifthWeekPaidOut_RestLost()
    {
        // 15 used Mon 2026-01-05 .. Fri 2026-01-23; today Fri 2027-01-15, after the deadline
        var data = Data(Fill(15), firstTracked: 2025).NoTransferFrom(2025);
        var b = Balance(data, Hy2025, "2027-01-15");

        Assert.Equal(10m, b.UnbookedDays);
        Assert.Equal(5m, b.UnusedFifthWeekDays);
        Assert.Equal(0m, b.TransferredToNext);
        Assert.Equal(5m, b.PaidOutDays);
        Assert.Equal(5m, b.AtDeadline.PaidOutFifthWeekDays);
        Assert.Equal(5m, b.LostDays);
        Assert.Equal(10m, b.ExpiringDays);
        Assert.Equal(0m, b.Settings.TransferLimitDays);
        Assert.Equal(0m, Balance(data, Hy2026, "2027-01-15").TransferredIn);
        Assert.False(Balance(data, Hy2026, "2026-10-24").TransferredInIsProjected);
    }

    [Fact]
    public void Deadline_LimitZero_IsInheritedByLaterYears_UntilOneSetsItsOwn()
    {
        // 2025 sets 0; 2026 and 2027 inherit it; 2028 sets 5 again. Nothing booked; today Mon 2031-01-06.
        var data = Data(firstTracked: 2025).NoTransferFrom(2025).WithLimit(2028, 5);
        const string today = "2031-01-06";

        foreach (var y in new[] { 2025, 2026, 2027 })
        {
            var b = Balance(data, new HolidayYear(y), today);
            Assert.Equal(0m, b.Settings.TransferLimitDays);
            Assert.Equal(0m, b.TransferredToNext);
            Assert.Equal(5m, b.PaidOutDays); // the unused 5th week is paid out
            Assert.Equal(20m, b.LostDays);
            Assert.Equal(0m, Balance(data, new HolidayYear(y + 1), today).TransferredIn);
        }
        Assert.Equal(5m, Balance(data, new HolidayYear(2028), today).TransferredToNext);
        Assert.Equal(5m, Balance(data, new HolidayYear(2029), today).TransferredIn);
    }

    [Fact]
    public void Deadline_LimitZeroOnOneYearOnly_StopsOnlyThatYearsTransfer()
    {
        // 2025 default 5, 2026 set 0, 2027 set 5. Nothing booked; today Mon 2030-01-07.
        var data = Data(firstTracked: 2025).NoTransferFrom(2026).WithLimit(2027, 5);
        const string today = "2030-01-07";

        Assert.Equal(5m, Balance(data, Hy2025, today).TransferredToNext);
        var b2026 = Balance(data, Hy2026, today);
        Assert.Equal(5m, b2026.TransferredIn);
        Assert.Equal(5m, b2026.UnusedTransferredInDays); // lost
        Assert.Equal(0m, b2026.TransferredToNext);
        Assert.Equal(5m, b2026.PaidOutDays);
        Assert.Equal(0m, Balance(data, new HolidayYear(2027), today).TransferredIn);
        Assert.Equal(5m, Balance(data, new HolidayYear(2027), today).TransferredToNext);
    }

    [Theory]
    // The 5th week (5) is always paid out; the setting covers the other 5 unused days
    [InlineData(PayoutMode.None, "0", "5", "5")]
    [InlineData(PayoutMode.Some, "3", "8", "2")]   // fewer than the 5 other days
    [InlineData(PayoutMode.Some, "5", "10", "0")]  // exactly them
    [InlineData(PayoutMode.Some, "12", "10", "0")] // more: only the unused are paid
    [InlineData(PayoutMode.All, "0", "10", "0")]
    public void Deadline_LimitZero_PayoutSetting(PayoutMode mode, string n, string paidOut, string lost)
    {
        // 15 used -> 10 unused: 5 5th week + 5 of the first 20, none transferred
        var b = Balance(Data(Fill(15), firstTracked: 2025).NoTransferFrom(2025).WithPayout(2025, mode, M(n)), Hy2025, "2027-01-15");

        Assert.Equal(0m, b.TransferredToNext);
        Assert.Equal(M(paidOut), b.PaidOutDays);
        Assert.Equal(M(lost), b.LostDays);
    }

    [Fact]
    public void Deadline_LimitZero_NothingMovesToTheNextYear()
    {
        var data = Data(Fill(15)).WithLimit(2025, 0m); // 2c: was "switch off, limit 10 ignored"; today picks first tracked 2025

        Assert.Equal(0m, Balance(data, Hy2025, "2026-11-02").TransferredToNext);
        Assert.Equal(25m, Balance(data, Hy2026, "2026-11-02").TotalDays);
    }

    [Fact]
    public void Deadline_HalfDaysSplitCorrectly()
    {
        // 19.5 used -> 5.5 unused; transfer 5, payout some 0.25 of the remaining 0.5
        var data = Data(Fill(19.5m), firstTracked: 2025).WithPayout(2025, PayoutMode.Some, 0.25m);
        var b = Balance(data, Hy2025, "2027-01-15");

        Assert.Equal(5m, b.TransferredToNext);
        Assert.Equal(0.25m, b.PaidOutDays);
        Assert.Equal(0.25m, b.LostDays);
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
    public void Deadline_EverythingUsed_NothingTransferredPaidOrLost()
    {
        var b = Balance(Data(Fill(25), firstTracked: 2025).WithPayout(2025, PayoutMode.All), Hy2025, "2027-01-15");

        Assert.Equal(0m, b.UnbookedDays);
        Assert.Equal(0m, b.TransferredToNext);
        Assert.Equal(0m, b.PaidOutDays);
        Assert.Equal(0m, b.LostDays);
    }

    [Fact]
    public void Deadline_OverBooked_NothingTransferredPaidOrLost()
    {
        var b = Balance(Data(Fill(26), firstTracked: 2025).WithPayout(2025, PayoutMode.All), Hy2025, "2027-01-15");

        Assert.Equal(-1m, b.UnbookedDays);
        Assert.Equal(0m, b.UnusedFifthWeekDays);
        Assert.Equal(0m, b.TransferredToNext);
        Assert.Equal(0m, b.PaidOutDays);
        Assert.Equal(0m, b.LostDays);
        Assert.Equal(0m, Balance(Data(Fill(26), firstTracked: 2025), Hy2026, "2027-01-15").TransferredIn);
    }

    // ---- Deadline split, transfer on ----

    [Theory]
    [InlineData("22", "3", "0")]  // 3 unused, fewer than the 5 5th-week days: all transferred
    [InlineData("20", "5", "0")]  // exactly the 5th week
    [InlineData("15", "5", "5")]  // 10 unused: the 5th week goes, the other 5 are lost
    [InlineData("0", "5", "20")]
    public void Transfer_DefaultLimit5_TransfersUnusedDaysUpTo5(string used, string transferred, string lost)
    {
        // Today Mon 2026-11-02, before the deadline: a projection
        var data = Data(Fill(M(used)), firstTracked: 2025);

        var b2025 = Balance(data, Hy2025, "2026-11-02");
        var b2026 = Balance(data, Hy2026, "2026-11-02");

        Assert.Equal(M(transferred), b2025.TransferredToNext);
        Assert.Equal(0m, b2025.PaidOutDays);
        Assert.Equal(M(lost), b2025.LostDays);
        Assert.Equal(25m - M(used), b2025.UnbookedDays); // still bookable: transfer doesn't reduce it
        Assert.Equal(25m - M(used), b2025.RemainingDays);
        Assert.Equal(M(transferred), b2026.TransferredIn);
        Assert.Equal(25m + M(transferred), b2026.TotalDays);
    }

    [Theory]
    [InlineData("8", "8", "2")]   // 5 5th-week days + 3 of the first 20
    [InlineData("10", "10", "0")]
    [InlineData("12", "10", "0")] // only 10 unused
    [InlineData("0", "0", "5")]   // limit 0: nothing moves; the 5th week is paid out, the other 5 lost
    [InlineData("3", "3", "5")]   // part of the 5th week moves, 2 paid out
    public void Transfer_PersonalLimit_TakesFirst20DaysAfterThe5thWeek(string limit, string transferred, string lost)
    {
        // 15 used -> 10 unused, 5 of them 5th week
        var data = Data(Fill(15), firstTracked: 2025).WithLimit(2025, M(limit));

        var b = Balance(data, Hy2025, "2026-11-02");

        Assert.Equal(M(transferred), b.TransferredToNext);
        Assert.Equal(Math.Max(0, 5m - M(transferred)), b.PaidOutDays); // 5th week not transferred
        Assert.Equal(M(lost), b.LostDays);
        Assert.Equal(M(transferred), Balance(data, Hy2026, "2026-11-02").TransferredIn);
    }

    [Theory]
    [InlineData(PayoutMode.Some, "3", "3", "7")]   // smaller than the 10 left after the transfer
    [InlineData(PayoutMode.Some, "15", "10", "0")] // larger: only the 10 left are paid
    [InlineData(PayoutMode.All, "0", "10", "0")]
    [InlineData(PayoutMode.None, "0", "0", "10")]
    public void Transfer_ThenPayout_ThenLost(PayoutMode mode, string n, string paidOut, string lost)
    {
        // 10 used -> 15 unused: 5 transferred, 10 left for payout
        var data = Data(Fill(10), firstTracked: 2025).WithPayout(2025, mode, M(n));

        var b = Balance(data, Hy2025, "2027-01-15");

        Assert.Equal(5m, b.TransferredToNext);
        Assert.Equal(M(paidOut), b.PaidOutDays);
        Assert.Equal(M(lost), b.LostDays);
    }

    [Theory]
    [InlineData("5", "0", "16.67")]  // no 5th week: the Holiday Act's 5 never reach the first 20
    [InlineData("8", "3", "13.67")]  // only the 3 above 5 (a personal agreement) move first-20 days
    [InlineData("0", "0", "16.67")]
    public void Transfer_PartialYearOf20OrLess_OnlyTheLimitAbove5Transfers(string limit, string transferred, string lost)
    {
        // Employed from Thu 2026-01-01: 16.67 earnable, no 5th week. Today Fri 2026-12-18.
        var data = Data(employedSince: D("2026-01-01"), firstTracked: 2025).WithLimit(2025, M(limit));

        var b = Balance(data, Hy2025, "2026-12-18");

        Assert.Equal(0m, b.FifthWeekDays);
        Assert.Equal(M(transferred), b.TransferredToNext);
        Assert.Equal(M(lost), b.LostDays);
    }

    [Fact]
    public void Transfer_AutumnBookingsUseTheOldYearFirst_AndReduceTheTransfer()
    {
        // 20 used in Jan 2026, then Mon 2026-10-19 .. Wed 2026-10-21 (3): oldest first, so they go to 2025,
        // leaving 2 to transfer
        var data = Data(Fill(20).Concat(Range("2026-10-19", "2026-10-21")), firstTracked: 2025);

        Assert.Equal(
            [(D("2026-10-19"), 2025, 1m), (D("2026-10-20"), 2025, 1m), (D("2026-10-21"), 2025, 1m)],
            AllocationsFrom(data, "2026-09-01"));
        Assert.Equal(2m, Balance(data, Hy2025, "2026-10-24").TransferredToNext);
        Assert.Equal(27m, Balance(data, Hy2026, "2026-10-24").TotalDays);
    }

    [Fact]
    public void Transfer_RangeAcrossNewYear()
    {
        // 20 used, then Mon 2026-12-28 .. Fri 2027-01-08: Mon 28 .. Wed 30 Dec (3) to 2025 -> 2 unused, transferred.
        // Mon 4 .. Fri 8 Jan (5) to 2026 (Thu 31 Dec and Fri 1 Jan are Danish holidays).
        var data = Data(Fill(20).Concat(Range("2026-12-28", "2027-01-08")), firstTracked: 2025);

        var b2025 = Balance(data, Hy2025, "2026-10-24");
        var b2026 = Balance(data, Hy2026, "2026-10-24");

        Assert.Equal(2m, b2025.TransferredToNext);
        Assert.Equal(2m, b2026.TransferredIn);
        Assert.Equal(27m, b2026.TotalDays);
        Assert.Equal(5m, b2026.BookedAheadDays);
    }

    [Fact]
    public void TransferredIn_UnusedIsLost_OnlyOwnDaysTransfer()
    {
        // 2025 -> 2026: 5. 2026 has 30, nothing booked: the 5 transferred in are lost (never transferred twice);
        // of its own 25, the 5th week goes to 2027 and the other 20 are lost.
        var data = Data(Fill(15), firstTracked: 2025);

        var b2026 = Balance(data, Hy2026, "2027-11-01");
        var b2027 = Balance(data, new HolidayYear(2027), "2027-11-01");

        Assert.Equal(30m, b2026.TotalDays);
        Assert.Equal(5m, b2026.FifthWeekDays); // own only
        Assert.Equal(5m, b2026.UnusedTransferredInDays);
        Assert.Equal(5m, b2026.TransferredToNext);
        Assert.Equal(0m, b2026.PaidOutDays);
        Assert.Equal(25m, b2026.LostDays);
        Assert.Equal(5m, b2027.TransferredIn);
        Assert.Equal(30m, b2027.TotalDays);
    }

    [Fact]
    public void TransferredIn_IsLostEvenWithPayoutAll()
    {
        // 2026 pays out all, transfer off for its deadline (limit 0): transferred-in 5 lost, own 25 paid out
        var data = Data(Fill(15), firstTracked: 2025).WithLimit(2026, 0m).WithPayout(2026, PayoutMode.All);

        var b = Balance(data, Hy2026, "2028-01-15");

        Assert.Equal(25m, b.PaidOutDays);
        Assert.Equal(5m, b.LostDays);
        Assert.Equal(5m, b.AtDeadline.LostTransferredInDays);
    }

    [Fact]
    public void TransferredIn_IsUsedFirst()
    {
        // 2026: 5 transferred in + 25 own. 4 booked Mon 2027-01-04 .. Thu 2027-01-07 use 4 of the transferred-in days,
        // so 1 transferred-in day is lost and all 25 own days are unused: 5 transferred, 20 lost.
        var data = Data(Fill(15).Concat(Range("2027-01-04", "2027-01-07")), firstTracked: 2025);

        var b = Balance(data, Hy2026, "2027-11-01");

        Assert.Equal(4m, b.UsedDays);
        Assert.Equal(1m, b.UnusedTransferredInDays);
        Assert.Equal(5m, b.UnusedFifthWeekDays);
        Assert.Equal(5m, b.TransferredToNext);
        Assert.Equal(21m, b.LostDays);
    }

    [Fact]
    public void TransferredIn_WithAHigherLimitNextYear_OnlyOwnDaysMoveOn()
    {
        // 2026's own limit 10: 10 of its own 25 move to 2027 (5th week + 5 others); the 5 transferred in are lost
        var data = Data(Fill(15), firstTracked: 2025).WithLimit(2026, 10m);

        var b2026 = Balance(data, Hy2026, "2027-11-01");

        Assert.Equal(5m, Balance(data, Hy2025, "2027-11-01").TransferredToNext); // 2025 keeps the default 5
        Assert.Equal(10m, b2026.TransferredToNext);
        Assert.Equal(20m, b2026.LostDays); // 5 transferred in + 15 own
        Assert.Equal(35m, Balance(data, new HolidayYear(2027), "2027-11-01").TotalDays);
    }

    [Fact]
    public void TransferredIn_RaisesTheOldYearsCapacityInTheOverlap()
    {
        // 2026 total 30 (5 transferred in). 28 used Mon 2027-01-04 .. Wed 2027-02-10.
        // Mon 2027-09-06 .. Fri 2027-09-10: 6th and 7th to 2026, 8th to 10th to 2027.
        var days = Fill(15).Concat(Range("2027-01-04", "2027-02-10")).Concat(Range("2027-09-06", "2027-09-10"));
        var data = Data(days, firstTracked: 2025);

        Assert.Equal(
            [
                (D("2027-09-06"), 2026, 1m), (D("2027-09-07"), 2026, 1m),
                (D("2027-09-08"), 2027, 1m), (D("2027-09-09"), 2027, 1m), (D("2027-09-10"), 2027, 1m),
            ],
            AllocationsFrom(data, "2027-09-01"));
        Assert.Equal(0m, Balance(data, Hy2026, "2027-10-01").UnbookedDays);
        Assert.Equal(0m, Balance(data, Hy2026, "2027-10-01").TransferredToNext);
    }

    [Fact]
    public void Transfer_InheritedLimitAppliesToLaterYears_OwnLimitOverrides()
    {
        // 2025 limit 8 (2026 inherits it); 15 used -> 2025 transfers 8. 2026: 33, nothing booked -> 8.
        var data = Data(Fill(15), firstTracked: 2025).WithLimit(2025, 8m);
        Assert.Equal(8m, Balance(data, Hy2025, "2027-11-01").TransferredToNext);
        Assert.Equal(33m, Balance(data, Hy2026, "2027-11-01").TotalDays);
        Assert.Equal(8m, Balance(data, Hy2026, "2027-11-01").TransferredToNext);

        // 2026 own limit 0: 2025 still transfers 8, 2026 nothing
        data.WithLimit(2026, 0m);
        Assert.Equal(8m, Balance(data, Hy2025, "2027-11-01").TransferredToNext);
        Assert.Equal(0m, Balance(data, Hy2026, "2027-11-01").TransferredToNext);
        Assert.Equal(25m, Balance(data, new HolidayYear(2027), "2027-11-01").TotalDays);
    }

    [Fact]
    public void Transfer_PerYearPayout_AppliesOnlyAtThatYearsDeadline()
    {
        // 2025 pays all, 2026 sets none again. 15 used in 2025: 5 transferred, 5 paid out.
        // 2026: 30, nothing booked: 5 transferred, 25 lost.
        var data = Data(Fill(15), firstTracked: 2025)
            .WithPayout(2025, PayoutMode.All)
            .WithPayout(2026, PayoutMode.None);

        var b2025 = Balance(data, Hy2025, "2027-11-01");
        var b2026 = Balance(data, Hy2026, "2027-11-01");

        Assert.Equal(5m, b2025.PaidOutDays);
        Assert.Equal(0m, b2025.LostDays);
        Assert.Equal(0m, b2026.PaidOutDays);
        Assert.Equal(25m, b2026.LostDays);
    }

    [Fact]
    public void Transfer_ChainThroughLeapYear()
    {
        // 2025 -> 2026: 5; 2026 nothing booked -> 5 of its own to 2027. 2027: 30, Mon 2028-02-28 .. Wed 2028-03-01
        // incl. Tue 29 Feb (3) use 3 of the 5 transferred in: 2 of those lost; own 25: 5 transferred, 20 lost.
        var data = Data(Fill(15).Concat(Range("2028-02-28", "2028-03-01")), firstTracked: 2025);

        var b = Balance(data, new HolidayYear(2027), "2028-03-02");

        Assert.Equal(5m, b.TransferredIn);
        Assert.Equal(3m, b.UsedDays);
        Assert.Equal(5m, b.TransferredToNext);
        Assert.Equal(22m, b.LostDays);
    }

    // ---- Transfer limit: the first 20 only through the part above 5 ----

    [Fact]
    public void Transfer_LimitAbove5_WithFewer5thWeekDays_TakesThe5thWeekPlusOnlyTheExtraFromTheFirst20()
    {
        // Employed from Wed 2025-10-01: 11 months = 22.92 earnable, so 2.92 5th-week days. Nothing booked, limit 10:
        // 2.92 5th week + 5 (10 - 5) of the first 20 = 7.92 transferred; the other 15 of the first 20 are lost.
        var data = Data(employedSince: D("2025-10-01"), firstTracked: 2025).WithLimit(2025, 10m);

        var b = Balance(data, Hy2025, "2026-12-18");

        Assert.Equal(M("22.92"), b.EarnableDays);
        Assert.Equal(M("2.92"), b.FifthWeekDays);
        Assert.Equal(M("7.92"), b.TransferredToNext);
        Assert.Equal(0m, b.PaidOutDays);
        Assert.Equal(15m, b.LostDays);
    }

    [Fact]
    public void Transfer_DefaultLimit_FullYearNothingBooked_OnlyThe5thWeekMoves()
    {
        // 25 unused: 5 5th week transferred, all 20 of the first 20 lost
        var b = Balance(Data(firstTracked: 2025), Hy2025, "2026-12-18");

        Assert.Equal(5m, b.TransferredToNext);
        Assert.Equal(20m, b.LostDays);
    }

    // ---- First tracked holiday year and opening balance ----

    [Fact]
    public void OpeningBalance_Default0_FirstTrackedYearHasNothingTransferredIn()
    {
        // First tracked 2025, no opening balance: 2025 has 25, transfers 5 of them to 2026. 2024 transfers nothing.
        var data = Data(firstTracked: 2025);

        var b2024 = Balance(data, new HolidayYear(2024), "2026-10-24");
        var b2025 = Balance(data, Hy2025, "2026-10-24");
        var b2026 = Balance(data, Hy2026, "2026-10-24");

        Assert.Equal(0m, b2024.TransferredIn);
        Assert.Equal(0m, b2024.TransferredToNext);
        Assert.Equal(0m, b2025.TransferredIn);
        Assert.Equal(25m, b2025.TotalDays);
        Assert.Equal(5m, b2025.TransferredToNext);
        Assert.Equal(5m, b2026.TransferredIn);
        Assert.Equal(30m, b2026.TotalDays);
    }

    [Fact]
    public void OpeningBalance_IsTheFirstTrackedYearsTransferredIn_AndIsUsedFirst()
    {
        // 2025 opens with 3. 15 booked = the 3 transferred in + 12 own, so own unused 13 = 5 5th week + 8 first-20:
        // 5 transferred, 8 lost, nothing of the opening balance lost.
        var data = Data(Fill(15), firstTracked: 2025, openingBalance: 3);

        var b = Balance(data, Hy2025, "2027-01-15");

        Assert.Equal(3m, b.TransferredIn);
        Assert.Equal(28m, b.TotalDays);
        Assert.Equal(13m, b.UnbookedDays);
        Assert.Equal(0m, b.UnusedTransferredInDays);
        Assert.Equal(5m, b.UnusedFifthWeekDays);
        Assert.Equal(5m, b.TransferredToNext);
        Assert.Equal(0m, b.PaidOutDays);
        Assert.Equal(8m, b.LostDays);
        Assert.Equal(5m, Balance(data, Hy2026, "2027-01-15").TransferredIn);
    }

    [Fact]
    public void OpeningBalance_UnusedIsLost_NeverTransferredAgainOrPaidOut()
    {
        // 2025 opens with 4, nothing booked, limit 30, payout All: the 4 are lost; own 25 all move (5 + 20).
        // 2026 gets 25, not 29.
        var data = Data(firstTracked: 2025, openingBalance: 4)
            .WithLimit(2025, 30m)
            .WithPayout(2025, PayoutMode.All);

        var b = Balance(data, Hy2025, "2027-01-15");

        Assert.Equal(4m, b.UnusedTransferredInDays);
        Assert.Equal(25m, b.TransferredToNext);
        Assert.Equal(0m, b.PaidOutDays);
        Assert.Equal(4m, b.LostDays);
        Assert.Equal(25m, Balance(data, Hy2026, "2027-01-15").TransferredIn);
    }

    [Fact]
    public void OpeningBalance_AppliesWithLimitZero()
    {
        // Limit 0 (2c), 2025 opens with 2. 1 booked uses 1 of the 2; the other is lost. Own 25: 5th week paid out,
        // 20 lost. Nothing moves to 2026.
        var data = Data(Fill(1), firstTracked: 2025, openingBalance: 2).NoTransferFrom(2025);

        var b = Balance(data, Hy2025, "2027-01-15");

        Assert.Equal(2m, b.TransferredIn);
        Assert.Equal(1m, b.UnusedTransferredInDays);
        Assert.Equal(0m, b.TransferredToNext);
        Assert.Equal(5m, b.PaidOutDays);
        Assert.Equal(21m, b.LostDays);
        Assert.Equal(0m, Balance(data, Hy2026, "2027-01-15").TransferredIn);
    }

    [Fact]
    public void OpeningBalance_ChainsFromTheFirstTrackedYear()
    {
        // 2025 opens with 5; 20 booked = 5 in + 15 own -> own unused 10: 5 transferred, 5 lost.
        // 2026: 5 in, nothing booked: in lost, own 5th week transferred, 20 lost. 2027: 5 in.
        var data = Data(Fill(20), firstTracked: 2025, openingBalance: 5);

        var b2025 = Balance(data, Hy2025, "2029-01-02");
        var b2026 = Balance(data, Hy2026, "2029-01-02");

        Assert.Equal(5m, b2025.TransferredToNext);
        Assert.Equal(5m, b2025.LostDays);
        Assert.Equal(5m, b2026.TransferredIn);
        Assert.Equal(5m, b2026.TransferredToNext);
        Assert.Equal(25m, b2026.LostDays);
        Assert.Equal(5m, Balance(data, new HolidayYear(2027), "2029-01-02").TransferredIn);
    }

    [Fact]
    public void OpeningBalance_NegativeIsStoredAs0()
    {
        var data = new VacationData { OpeningBalanceDays = -3 };

        Assert.Equal(0m, data.OpeningBalanceDays);
    }

    [Fact]
    public void OpeningBalance_RaisesTheFirstTrackedYearsCapacityInTheOverlap()
    {
        // 2025 opens with 2 (total 27), 25 used in Jan/Feb 2026. Mon 2026-10-19 .. Wed 10-21: two go to 2025, one to 2026.
        var data = Data(Fill(25).Concat(Range("2026-10-19", "2026-10-21")), firstTracked: 2025, openingBalance: 2);

        Assert.Equal(
            [(D("2026-10-19"), 2025, 1m), (D("2026-10-20"), 2025, 1m), (D("2026-10-21"), 2026, 1m)],
            BalanceCalculator.Allocate(data, D("2026-10-24"))
                .Where(a => a.Date >= D("2026-09-01"))
                .Select(a => (a.Date, a.HolidayYear.StartYear, a.Amount)));
    }

    [Fact]
    public void OpeningBalance_UnusedAfterTheDeadline_IsExpired()
    {
        // Calendar year 2026 (holiday year 2025), limit 0 (2c), 2025 opens with 3; 1 booked. Today Mon 2027-01-04:
        // 2 of the opening balance lost, own 5th week paid out, own 20 lost.
        var s = BalanceCalculator.CalculateCalendarYear(Data(Fill(1), firstTracked: 2025, openingBalance: 3).NoTransferFrom(2025), 2026, D("2027-01-04"));

        Assert.Equal(BalanceStatus.Expired, s.Status);
        Assert.Equal(2m, s.HolidayYear.AtDeadline.LostTransferredInDays);
        Assert.Equal(22m, s.LostDays);
        Assert.Equal(5m, s.PaidOutDays);
    }

    [Fact]
    public void YearsBeforeTheFirstTrackedYear_TransferNothing_EvenWithBookingsAndUnusedDays()
    {
        // First tracked 2026, transfer on. 2025 has 15 booked and 10 unused (5 5th week + 5 first-20): no transfer,
        // the 5th week is paid out and the rest lost. 2026 starts at the opening balance (0), not 2025's 5.
        var data = Data(Fill(15), firstTracked: 2026);

        var b2025 = Balance(data, Hy2025, "2027-01-15");
        var b2026 = Balance(data, Hy2026, "2027-01-15");

        Assert.Equal(15m, b2025.UsedDays);
        Assert.Equal(0m, b2025.TransferredIn);
        Assert.Equal(0m, b2025.TransferredToNext);
        Assert.Equal(5m, b2025.PaidOutDays);
        Assert.Equal(5m, b2025.LostDays);
        Assert.Equal(0m, b2026.TransferredIn);
        Assert.Equal(25m, b2026.TotalDays);
        Assert.Equal(0m, Balance(data, new HolidayYear(2024), "2027-01-15").TransferredToNext);
    }

    [Fact]
    public void FirstTrackedYearAfterTheBookings_GetsTheOpeningBalance_NotACalculatedTransfer()
    {
        // As above with an opening balance of 3: 2026 has exactly 3 transferred in.
        var data = Data(Fill(15), firstTracked: 2026, openingBalance: 3);

        Assert.Equal(3m, Balance(data, Hy2026, "2027-01-15").TransferredIn);
        Assert.Equal(0m, Balance(data, Hy2025, "2027-01-15").TransferredIn); // the opening balance is not 2025's
    }

    [Fact]
    public void UserSample_BookingInDecember2025_DoesNotMakeHolidayYear2024Transfer()
    {
        // Mon 2025-12-01 booked, transfer on, no first year stored, today Sat 2026-10-24: the first tracked year is
        // 2025/26 (oldest open today). The December day uses 2024/25 (older open year), which transfers nothing,
        // so 2025/26 has 25, not 30.
        var data = Data([Day("2025-12-01")]);

        var b2024 = Balance(data, new HolidayYear(2024), "2026-10-24");
        var b2025 = Balance(data, Hy2025, "2026-10-24");

        Assert.Equal(1m, b2024.UsedDays);
        Assert.Equal(0m, b2024.TransferredToNext);
        Assert.Equal(0m, b2025.TransferredIn);
        Assert.Equal(25m, b2025.TotalDays);
    }

    [Theory]
    [InlineData("2026-10-24", 2025)] // Sat; 2025/26 and 2026/27 open
    [InlineData("2026-08-31", 2025)] // Mon; only 2025/26 open
    [InlineData("2026-09-01", 2025)] // Tue; 2026/27 opens, 2025/26 still open
    [InlineData("2026-12-31", 2025)] // Thu; 2025/26's deadline
    [InlineData("2027-01-01", 2026)] // Fri; 2025/26 has expired
    [InlineData("2028-02-29", 2027)] // Tue, leap day
    public void FirstTrackedYear_NotStored_IsTheOldestHolidayYearOpenToday(string today, int expected)
    {
        var data = new VacationData();

        Assert.Equal(new HolidayYear(expected), VacationData.DefaultFirstTrackedHolidayYear(D(today)));
        Assert.Equal(new HolidayYear(expected), data.EffectiveFirstTrackedHolidayYear(D(today)));
        Assert.Null(data.FirstTrackedHolidayYear); // reading never stores it
    }

    [Fact]
    public void FirstTrackedYear_Stored_WinsOverToday()
    {
        var data = new VacationData { FirstTrackedHolidayYear = 2023 };

        Assert.Equal(new HolidayYear(2023), data.EffectiveFirstTrackedHolidayYear(D("2026-10-24")));
    }

    [Fact]
    public void FirstTrackedYear_NotStored_MovesWithToday_TheOpeningBalanceGoesWithIt()
    {
        // No first year stored, opening balance 2, 15 booked in 2025, transfer on.
        var data = Data(Fill(15), openingBalance: 2);

        // Sat 2026-10-24: first tracked 2025 gets the 2.
        Assert.Equal(2m, Balance(data, Hy2025, "2026-10-24").TransferredIn);

        // Fri 2027-01-15: first tracked is 2026 now, so 2025 has nothing transferred in and transfers nothing,
        // and 2026 gets the 2 instead of 2025's transfer. This is why the App stores the first year.
        Assert.Equal(0m, Balance(data, Hy2025, "2027-01-15").TransferredIn);
        Assert.Equal(0m, Balance(data, Hy2025, "2027-01-15").TransferredToNext);
        Assert.Equal(2m, Balance(data, Hy2026, "2027-01-15").TransferredIn);
    }

    [Fact]
    public void FirstTrackedYear_Changing_ChangesWhereTheOpeningBalanceGoesAndTheChain()
    {
        // 20 booked in 2025, nothing else, transfer on, opening balance 1. Today Fri 2027-01-15.
        var data = Data(Fill(20), firstTracked: 2025, openingBalance: 1);
        // 2025: 1 in + 25 own, 20 booked = 1 in + 19 own -> own unused 6 = 5th week 5 + 1: 5 transferred.
        Assert.Equal(1m, Balance(data, Hy2025, "2027-01-15").TransferredIn);
        Assert.Equal(5m, Balance(data, Hy2026, "2027-01-15").TransferredIn);

        // Later: 2026. 2025 transfers nothing (5th week paid out); 2026 gets the 1.
        data.FirstTrackedHolidayYear = 2026;
        Assert.Equal(0m, Balance(data, Hy2025, "2027-01-15").TransferredIn);
        Assert.Equal(0m, Balance(data, Hy2025, "2027-01-15").TransferredToNext);
        Assert.Equal(5m, Balance(data, Hy2025, "2027-01-15").PaidOutDays);
        Assert.Equal(1m, Balance(data, Hy2026, "2027-01-15").TransferredIn);

        // Earlier: 2024. 2024 gets the 1, nothing booked: 1 lost, its 5th week to 2025. 2025: 5 in + 25 own,
        // 20 booked = 5 in + 15 own -> own unused 10: 5 transferred.
        data.FirstTrackedHolidayYear = 2024;
        Assert.Equal(1m, Balance(data, new HolidayYear(2024), "2027-01-15").TransferredIn);
        Assert.Equal(5m, Balance(data, Hy2025, "2027-01-15").TransferredIn);
        Assert.Equal(5m, Balance(data, Hy2026, "2027-01-15").TransferredIn);
    }

    [Fact]
    public void EnsureFirstTrackedYear_StoresTheDefaultOnce()
    {
        var data = new VacationData();

        Assert.True(data.EnsureFirstTrackedHolidayYear(D("2026-10-24"))); // Sat; oldest open 2025/26
        Assert.Equal(2025, data.FirstTrackedHolidayYear);

        Assert.False(data.EnsureFirstTrackedHolidayYear(D("2028-01-03"))); // Mon; would be 2027, but it is stored
        Assert.Equal(2025, data.FirstTrackedHolidayYear);
        Assert.Equal(new HolidayYear(2025), data.EffectiveFirstTrackedHolidayYear(D("2028-01-03")));
    }

    [Fact]
    public void EnsureFirstTrackedYear_KeepsAStoredYear()
    {
        var data = new VacationData { FirstTrackedHolidayYear = 2023 };

        Assert.False(data.EnsureFirstTrackedHolidayYear(D("2026-10-24")));
        Assert.Equal(2023, data.FirstTrackedHolidayYear);
    }

    [Fact]
    public void Transfer_FarFutureYear_ResolvesWithoutRecursion()
    {
        // Every year from 2025 transfers 5 with nothing booked; a year far ahead still resolves
        var b = Balance(Data(firstTracked: 2025), new HolidayYear(5000), "2026-10-24");

        Assert.Equal(5m, b.TransferredIn);
        Assert.Equal(30m, b.TotalDays);
    }

    [Fact]
    public void CalculateHolidayYears_ReturnsTheGivenYearsInOrder()
    {
        var balances = BalanceCalculator.CalculateHolidayYears(Data(Fill(3)), HolidayYear.InCalendarYear(2026), D("2026-10-24"));

        Assert.Equal([Hy2025, Hy2026], balances.Select(b => b.HolidayYear));
        Assert.Equal(3m, balances[0].UsedDays);
    }
}
