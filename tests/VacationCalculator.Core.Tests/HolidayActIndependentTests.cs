using VacationCalculator.Core;
using static VacationCalculator.Core.Tests.TestData;

namespace VacationCalculator.Core.Tests;

/// <summary>
/// Independent, hand-counted tests of feature 2 (Danish Holiday Act). Expected values come from the requirement
/// in docs/features.md and from counting on a calendar, not from the implementation.
/// Calendar anchors: Thu 2026-01-01, Mon 2026-01-05, Mon 2026-08-31, Tue 2026-09-01, Mon 2026-10-05,
/// Fri 2026-12-18, Thu 2026-12-31, Fri 2027-01-01, Mon 2027-01-04, Sat 2028-01-01, Tue 2028-02-29.
/// Holiday year 2025 = "2025/26": earns 1 Sep 2025 .. 31 Aug 2026, deadline 31 Dec 2026.
/// </summary>
public class HolidayActIndependentTests
{
    private static HolidayYearBalance Bal(VacationData data, int startYear, string today) =>
        BalanceCalculator.CalculateHolidayYear(data, new HolidayYear(startYear), D(today));

    private static VacationData Dat(IEnumerable<BookedDay>? days, bool transferAllowed, params (int StartYear, decimal Days)[] transfers) =>
        Data(days, null, transferAllowed, transfers);

    private static CalendarYearStatus Cal(VacationData data, int year, string today) =>
        BalanceCalculator.CalculateCalendarYear(data, year, D(today));

    // ---------------------------------------------------------------- boundaries

    [Fact]
    public void HolidayYear_Dates()
    {
        var y = new HolidayYear(2025);
        Assert.Equal(D("2025-09-01"), y.Start);
        Assert.Equal(D("2026-08-31"), y.End);
        Assert.Equal(D("2026-12-31"), y.Deadline); // 16 months after the start
        Assert.Equal("2025/26", y.ToString());
        Assert.Equal("2099/00", new HolidayYear(2099).ToString());
    }

    [Theory]
    [InlineData("2025-08-31", 2024)]
    [InlineData("2025-09-01", 2025)]
    [InlineData("2025-12-31", 2025)]
    [InlineData("2026-01-01", 2025)]
    [InlineData("2026-08-31", 2025)]
    [InlineData("2026-09-01", 2026)]
    [InlineData("2028-02-29", 2027)]
    public void Containing_SwitchesOn1September(string date, int startYear)
    {
        Assert.Equal(new HolidayYear(startYear), HolidayYear.Containing(D(date)));
    }

    [Theory]
    [InlineData("2026-08-31", new[] { 2025 })]
    [InlineData("2026-09-01", new[] { 2025, 2026 })]
    [InlineData("2026-12-31", new[] { 2025, 2026 })]
    [InlineData("2027-01-01", new[] { 2026 })]
    [InlineData("2027-08-31", new[] { 2026 })]
    public void OpenOn_OneYearJanToAug_TwoYearsSepToDec(string date, int[] startYears)
    {
        Assert.Equal(startYears, HolidayYear.OpenOn(D(date)).Select(y => y.StartYear));
    }

    [Fact]
    public void WithDeadlineIn_And_InCalendarYear()
    {
        Assert.Equal(new HolidayYear(2025), HolidayYear.WithDeadlineIn(2026));
        Assert.Equal([2025, 2026], HolidayYear.InCalendarYear(2026).Select(y => y.StartYear));
    }

    // ---------------------------------------------------------------- accrual

    [Theory]
    // n completed months of 25/12 each, rounded once to 2 decimals. A month is usable from the 1st of the next one.
    [InlineData("2025-08-01", "0")]     // before the holiday year
    [InlineData("2025-09-01", "0")]     // first day: September has not ended
    [InlineData("2025-09-30", "0")]     // last day of September: still not usable
    [InlineData("2025-10-01", "2.08")]  // 25/12 = 2.0833
    [InlineData("2025-11-01", "4.17")]  // 4.1667
    [InlineData("2025-12-01", "6.25")]
    [InlineData("2026-01-01", "8.33")]  // 8.3333
    [InlineData("2026-02-01", "10.42")] // 10.4167
    [InlineData("2026-03-01", "12.5")]
    [InlineData("2026-04-01", "14.58")] // 14.5833
    [InlineData("2026-05-01", "16.67")] // 16.6667
    [InlineData("2026-06-01", "18.75")]
    [InlineData("2026-07-01", "20.83")] // 20.8333
    [InlineData("2026-08-01", "22.92")] // 22.9167
    [InlineData("2026-08-31", "22.92")] // August not yet complete
    [InlineData("2026-09-01", "25")]    // 12 months = exactly 25 (not 12 * 2.08 = 24.96)
    [InlineData("2027-06-01", "25")]    // never more than 25
    public void EarnedAsOf_FullEmployment_MonthByMonth(string date, string expected)
    {
        Assert.Equal(M(expected), Accrual.EarnedAsOf(new HolidayYear(2025), null, D(date)));
    }

    [Fact]
    public void EarnableDays_FullYear_Is25()
    {
        Assert.Equal(25m, Accrual.EarnableDays(new HolidayYear(2025), null));
        Assert.Equal(25m, Accrual.EarnableDays(new HolidayYear(2025), D("2020-01-01"))); // long before
        Assert.Equal(25m, Accrual.EarnableDays(new HolidayYear(2025), D("2025-09-01"))); // first day of the year
    }

    [Fact]
    public void EmployedSince_AfterTheHolidayYear_EarnsNothing()
    {
        Assert.Equal(0m, Accrual.EarnableDays(new HolidayYear(2025), D("2026-09-01")));
    }

    [Fact]
    public void EmployedSince_LastDayOfHolidayYear_EarnsOneDayOfAugust()
    {
        // 1 of 31 days of August: 25 * 1 / (12 * 31) = 0.0672
        Assert.Equal(0.07m, Accrual.EarnableDays(new HolidayYear(2025), D("2026-08-31")));
    }

    [Fact]
    public void EmployedSince_MidSeptember_ProRatesTheFirstMonth()
    {
        // 16..30 Sep = 15 of 30 days = half a month; the 11 later months are full: 11.5 months = 25 * 11.5 / 12 = 23.9583.
        var since = D("2025-09-16");
        Assert.Equal(23.96m, Accrual.EarnableDays(new HolidayYear(2025), since));
        // As of 1 Oct only half of September is usable: 25 * 0.5 / 12 = 1.0417.
        Assert.Equal(1.04m, Accrual.EarnedAsOf(new HolidayYear(2025), since, D("2025-10-01")));
        // The part-month is not usable before the month has ended.
        Assert.Equal(0m, Accrual.EarnedAsOf(new HolidayYear(2025), since, D("2025-09-30")));
    }

    [Fact]
    public void EmployedSince_LastDayOfSeptember_OneThirtiethOfAMonth()
    {
        // 1 of 30 days: 25 / 360 = 0.0694 usable on 1 Oct; full year: (11 * 30 + 1) / 360 * 25 = 22.9861.
        var since = D("2025-09-30");
        Assert.Equal(0.07m, Accrual.EarnedAsOf(new HolidayYear(2025), since, D("2025-10-01")));
        Assert.Equal(22.99m, Accrual.EarnableDays(new HolidayYear(2025), since));
    }

    [Fact]
    public void EmployedSince_MidHolidayYear_SkipsEarlierMonths()
    {
        // 15 Dec: 15..31 Dec = 17 of 31 days; Jan..Aug = 8 full months. 25 * (8 * 31 + 17) / (12 * 31) = 17.8091.
        var since = D("2025-12-15");
        Assert.Equal(17.81m, Accrual.EarnableDays(new HolidayYear(2025), since));
        Assert.Equal(0m, Accrual.EarnedAsOf(new HolidayYear(2025), since, D("2025-12-31"))); // December not over
        Assert.Equal(1.14m, Accrual.EarnedAsOf(new HolidayYear(2025), since, D("2026-01-01"))); // 25 * 17 / 372 = 1.1425
    }

    [Fact]
    public void EmployedSince_1stOfAMonth_IsAFullMonth()
    {
        // Employed since 1 March 2026: Mar..Aug = 6 full months = 12.5.
        Assert.Equal(12.5m, Accrual.EarnableDays(new HolidayYear(2025), D("2026-03-01")));
    }

    [Fact]
    public void EmployedSince_LeapYearFebruary_UsesTwentyNineDays()
    {
        // Holiday year 2027/28 contains 29 Feb 2028. Since 15 Feb 2028: 15..29 = 15 of 29 days; Mar..Aug = 6 full months.
        // 25 * (6 * 29 + 15) / (12 * 29) = 25 * 189 / 348 = 13.5776.
        var year = new HolidayYear(2027);
        var since = D("2028-02-15");
        Assert.Equal(13.58m, Accrual.EarnableDays(year, since));
        // Usable on 1 March: 25 * 15 / 348 = 1.0776.
        Assert.Equal(1.08m, Accrual.EarnedAsOf(year, since, D("2028-03-01")));
    }

    [Fact]
    public void EmployedSince_NonLeapFebruary_UsesTwentyEightDays()
    {
        // Since 15 Feb 2027 (holiday year 2026/27): 14 of 28 days = half a month + 6 months = 6.5 months. 25 * 6.5 / 12 = 13.5417.
        Assert.Equal(13.54m, Accrual.EarnableDays(new HolidayYear(2026), D("2027-02-15")));
    }

    [Fact]
    public void EmployedSince_Exactly20Earnable()
    {
        // Since 13 Nov 2025: 18 of 30 days = 0.6 month + Dec..Aug 9 months = 9.6 months = exactly 20.00.
        Assert.Equal(20m, Accrual.EarnableDays(new HolidayYear(2025), D("2025-11-13")));
    }

    // ---------------------------------------------------------------- allocation (Sep-Dec overlap)

    [Fact]
    public void Allocate_SeptemberToDecember_OldestYearFirst()
    {
        // Mon 2026-10-05, nothing else booked: holiday year 2025/26 still has days.
        var alloc = Assert.Single(BalanceCalculator.Allocate(Data([Day("2026-10-05")])));
        Assert.Equal(new HolidayYear(2025), alloc.HolidayYear);
        Assert.Equal(1m, alloc.Amount);
    }

    [Fact]
    public void Allocate_OldYearFull_GoesToNewYear()
    {
        // 25 days in Jan/Feb 2026 use up 2025/26; Oct 5 is then taken from 2026/27.
        var alloc = BalanceCalculator.Allocate(Data(Fill(25).Append(Day("2026-10-05"))))
            .Single(a => a.Date == D("2026-10-05"));
        Assert.Equal(new HolidayYear(2026), alloc.HolidayYear);
    }

    [Fact]
    public void Allocate_FullDayWhenOldYearHasHalfLeft_IsSplitInHalves()
    {
        // 24 full days (Mon 01-05 .. Thu 02-05) + a half day on Fri 02-06 = 24.5 used: 0.5 left in 2025/26.
        var days = Fill(24).Append(Day("2026-02-06", 0.5m)).Append(Day("2026-10-05"));
        var parts = BalanceCalculator.Allocate(Data(days)).Where(a => a.Date == D("2026-10-05")).ToList();

        Assert.Equal(2, parts.Count);
        Assert.Equal(0.5m, parts.Single(p => p.HolidayYear == new HolidayYear(2025)).Amount);
        Assert.Equal(0.5m, parts.Single(p => p.HolidayYear == new HolidayYear(2026)).Amount);
    }

    [Fact]
    public void Allocate_HalfDayWhenOldYearHasHalfLeft_FitsInOldYear()
    {
        var days = Fill(24).Append(Day("2026-02-06", 0.5m)).Append(Day("2026-10-05", 0.5m));
        var alloc = BalanceCalculator.Allocate(Data(days)).Single(a => a.Date == D("2026-10-05"));

        Assert.Equal(new HolidayYear(2025), alloc.HolidayYear);
        Assert.Equal(0.5m, alloc.Amount);
    }

    [Fact]
    public void Allocate_31AugustBelongsToOldYear_1SeptemberToOldYearWhileItHasDays()
    {
        // Mon 2026-08-31: only 2025/26 is open (it is full -> over-booked). Tue 2026-09-01: 2025/26 is full, so 2026/27.
        var days = Fill(25).Append(Day("2026-08-31")).Append(Day("2026-09-01"));
        var alloc = BalanceCalculator.Allocate(Data(days));

        Assert.Equal(new HolidayYear(2025), alloc.Single(a => a.Date == D("2026-08-31")).HolidayYear);
        Assert.Equal(new HolidayYear(2026), alloc.Single(a => a.Date == D("2026-09-01")).HolidayYear);
    }

    [Fact]
    public void Allocate_JanuaryAfterDeadline_BelongsToCurrentYear_EvenIfOldYearHadDaysLeft()
    {
        // Mon 2027-01-04: 2025/26 expired on 31 Dec 2026 and cannot take it.
        var alloc = Assert.Single(BalanceCalculator.Allocate(Data([Day("2027-01-04")])));
        Assert.Equal(new HolidayYear(2026), alloc.HolidayYear);
    }

    [Fact]
    public void Allocate_31December_StillUsesOldYear()
    {
        var alloc = Assert.Single(BalanceCalculator.Allocate(Data([Day("2026-12-31")])));
        Assert.Equal(new HolidayYear(2025), alloc.HolidayYear);
    }

    [Fact]
    public void Allocate_LeapDay_IsAWorkingDayInHolidayYear2027()
    {
        // Mon 2028-02-28 .. Wed 2028-03-01 incl. Tue 29 Feb: 3 working days, all in 2027/28.
        var data = Data(Range("2028-02-28", "2028-03-01"));
        Assert.Equal(3m, Bal(data, 2027, "2028-03-02").UsedDays);
    }

    [Fact]
    public void Allocate_IgnoresHolidaysWeekendsAndFeriefri()
    {
        var data = Data([Day("2026-10-03"), Day("2026-12-24"), Day("2026-10-05", 1, DayType.Feriefri)])
            .WithHolidays("2026-12-24");
        Assert.Empty(BalanceCalculator.Allocate(data));
    }

    [Fact]
    public void Allocate_OverbookingBothYears_ExtraGoesToNewestYear()
    {
        // 2025/26 full (25), then 26 days Mon 2026-10-05 .. Mon 2026-11-09 -> all in 2026/27, one more than its 25.
        var data = Data(Fill(25).Concat(Fill(26, "2026-10-05")));
        var b = Bal(data, 2026, "2026-12-18");

        Assert.Equal(26m, b.UsedDays);
        Assert.Equal(1m, b.ExceedsEarnableBy);
        Assert.Equal(-1m, b.UnbookedDays);
    }

    // ---------------------------------------------------------------- advance holiday

    [Fact]
    public void AdvanceHoliday_BookedBeforeEarned_GoesNegativeButIsNotBlocked()
    {
        // Today Tue 2026-09-15: nothing of 2026/27 is usable yet. 3 days Mon 2027-01-04 .. Wed 01-06 are booked.
        var b = Bal(Data(Range("2027-01-04", "2027-01-06")), 2026, "2026-09-15");

        Assert.Equal(0m, b.EarnedSoFar);
        Assert.Equal(3m, b.BookedAheadDays);
        Assert.Equal(-3m, b.RemainingDays);
        Assert.Equal(3m, b.ExceedsEarnedSoFarBy);
        Assert.Equal(0m, b.ExceedsEarnableBy); // still within the 25 earnable by 31 August
        Assert.Equal(22m, b.UnbookedDays);
    }

    [Fact]
    public void AdvanceHoliday_MoreThanEarnableBy31August_IsFlagged()
    {
        // Employed since 2027-03-01: 2026/27 earns 12.5. Booking 13 days (Mon 2027-04-05 .. Wed 04-21) exceeds it by 0.5.
        var data = Data(Fill(13, "2027-04-05"), employedSince: D("2027-03-01"));
        var b = Bal(data, 2026, "2027-02-01");

        Assert.Equal(12.5m, b.EarnableDays);
        Assert.Equal(0.5m, b.ExceedsEarnableBy);
        Assert.Equal(BalanceStatus.OverBooked, Cal(data, 2027, "2027-02-01").Status);
    }

    [Fact]
    public void ExactlyEarnable_IsNotOverBooked()
    {
        var data = Data(Fill(25, "2027-01-04"));
        var status = Cal(data, 2027, "2026-12-18");

        Assert.Equal(0m, status.UnbookedDays);
        Assert.Equal(BalanceStatus.AllPlanned, status.Status);
    }

    // ---------------------------------------------------------------- paid out vs lost

    [Theory]
    // used days (all in holiday year 2025/26 with 25 earned) -> paid out, lost. Used days consume the first 20 first.
    [InlineData(0, 5, 20)]
    [InlineData(15, 5, 5)]      // the example from the requirement: 25 earned, 15 used -> 10 unused = 5 paid + 5 lost
    [InlineData(19, 5, 1)]
    [InlineData(20, 5, 0)]
    [InlineData(22, 3, 0)]
    [InlineData(24.5, 0.5, 0)]
    [InlineData(25, 0, 0)]
    public void PaidOutAndLost_FullYear(decimal used, decimal paidOut, decimal lost)
    {
        var b = Bal(Data(Fill(used)), 2025, "2027-01-04");

        Assert.Equal(paidOut, b.PaidOutDays);
        Assert.Equal(lost, b.LostDays);
        Assert.Equal(25m - used, b.UnbookedDays);
    }

    [Fact]
    public void PaidOutAndLost_SameBeforeTheDeadline()
    {
        var b = Bal(Data(Fill(15)), 2025, "2026-12-18");

        Assert.Equal(5m, b.PaidOutDays);
        Assert.Equal(5m, b.LostDays);
    }

    [Fact]
    public void PaidOutAndLost_20OrLessEarned_AllLost()
    {
        // Employed since 2026-03-01: 12.5 earned, none used -> everything unused is within the first 20: lost.
        var b = Bal(Data(employedSince: D("2026-03-01")), 2025, "2027-01-04");

        Assert.Equal(0m, b.PaidOutDays);
        Assert.Equal(12.5m, b.LostDays);
        Assert.Equal(0m, b.MaxTransferToNext);
    }

    [Fact]
    public void PaidOutAndLost_Exactly20Earned_AllLost()
    {
        var b = Bal(Data(employedSince: D("2025-11-13")), 2025, "2027-01-04");

        Assert.Equal(20m, b.EarnableDays);
        Assert.Equal(0m, b.PaidOutDays);
        Assert.Equal(20m, b.LostDays);
    }

    [Fact]
    public void PaidOutAndLost_JustOver20Earned_FifthWeekIsTheExcess()
    {
        // Since 12 Nov 2025: (9 * 30 + 19) / 360 * 25 = 20.0694 -> 20.07. Unused 20.07: 0.07 paid out, 20 lost.
        var b = Bal(Data(employedSince: D("2025-11-12")), 2025, "2027-01-04");

        Assert.Equal(20.07m, b.EarnableDays);
        Assert.Equal(0.07m, b.PaidOutDays);
        Assert.Equal(20m, b.LostDays);
    }

    [Fact]
    public void PaidOutAndLost_10Used_OnlyTheFifthWeekIsPaidOut()
    {
        // 10 used, 15 unused: the 5th week (5) is paid out, the other 10 lost -- not 15 paid out.
        var b = Bal(Data(Fill(10)), 2025, "2027-01-04");

        Assert.Equal(5m, b.PaidOutDays);
        Assert.Equal(10m, b.LostDays);
    }

    // ---------------------------------------------------------------- transfer

    [Fact]
    public void Transfer_SettingOn_MovesFifthWeekDaysToNextYear()
    {
        // 20 used, 5 unused 5th-week days, all 5 transferred.
        var data = Dat(Fill(20), true, (2025, 5m));
        var old = Bal(data, 2025, "2026-12-18");
        var next = Bal(data, 2026, "2026-12-18");

        Assert.Equal(5m, old.MaxTransferToNext);
        Assert.Equal(5m, old.TransferredToNext);
        Assert.Equal(0m, old.PaidOutDays);
        Assert.Equal(0m, old.LostDays);
        Assert.Equal(0m, old.UnbookedDays);
        Assert.Equal(0m, old.RemainingDays); // 25 earned - 20 used - 5 transferred
        Assert.Equal(5m, next.TransferredIn);
        Assert.Equal(30m, next.TotalDays);
        Assert.Equal(10m, next.FifthWeekDays); // its own 5 + the transferred 5
    }

    [Theory]
    [InlineData(2, 2, 3)]   // part of the 5 -> 3 paid out
    [InlineData(5, 5, 0)]
    [InlineData(8, 5, 0)]   // capped at the 5 unused 5th-week days
    [InlineData(-3, 0, 5)]  // negative is ignored
    [InlineData(0, 0, 5)]
    public void Transfer_IsCappedAndNeverNegative(decimal stored, decimal applied, decimal paidOut)
    {
        var b = Bal(Dat(Fill(20), true, (2025, stored)), 2025, "2026-12-18");

        Assert.Equal(applied, b.TransferredToNext);
        Assert.Equal(paidOut, b.PaidOutDays);
    }

    [Fact]
    public void Transfer_SettingOff_IgnoresStoredValue()
    {
        var data = Dat(Fill(20), false, (2025, 5m));
        var old = Bal(data, 2025, "2026-12-18");

        Assert.Equal(0m, old.MaxTransferToNext);
        Assert.Equal(0m, old.TransferredToNext);
        Assert.Equal(5m, old.PaidOutDays);
        Assert.Equal(0m, Bal(data, 2026, "2026-12-18").TransferredIn);
    }

    [Theory]
    [InlineData(15, 5)]   // 10 unused = 5 fifth week + 5 first-20: only the 5th week may move
    [InlineData(22, 3)]   // only 3 unused at all
    [InlineData(25, 0)]
    [InlineData(0, 5)]    // 25 unused, still only the 5th week (5)
    public void Transfer_MaxIsUnusedFifthWeekDays_NotUnusedDays(decimal used, decimal max)
    {
        var b = Bal(Data(Fill(used), transferAllowed: true), 2025, "2026-12-18");

        Assert.Equal(max, b.MaxTransferToNext);
    }

    [Fact]
    public void Transfer_FirstFourWeeksCannotBeTransferred_RemainLost()
    {
        // 15 used, transfer the max (5): the other 5 unused days are still lost.
        var b = Bal(Dat(Fill(15), true, (2025, 10m)), 2025, "2027-01-04");

        Assert.Equal(5m, b.TransferredToNext);
        Assert.Equal(0m, b.PaidOutDays);
        Assert.Equal(5m, b.LostDays);
    }

    [Fact]
    public void TransferredInDays_AreNotLost_TheyArePaidOutAtTheNextDeadline()
    {
        // 2026/27 got 5 transferred in (25 + 5 = 30) and nothing was used. After its deadline (2027-12-31)
        // the unused 5th-week days (own 5 + transferred 5 = 10) are paid out, the other 20 are lost.
        var data = Dat(Fill(20), true, (2025, 5m));
        var b = Bal(data, 2026, "2028-01-03");

        Assert.Equal(30m, b.UnbookedDays);
        Assert.Equal(10m, b.PaidOutDays);
        Assert.Equal(20m, b.LostDays);
    }

    [Fact]
    public void TransferredInDays_UsedFirst_LeaveThe5thWeekToBePaidOut()
    {
        // 30 available in 2026/27 and 25 used (Mon 2027-01-04 .. Fri 2027-02-05 ... 25 weekdays) -> 5 unused, paid out.
        var data = Dat(Fill(20).Concat(Fill(25, "2027-01-04")), true, (2025, 5m));
        var b = Bal(data, 2026, "2028-01-03");

        Assert.Equal(25m, b.UsedDays);
        Assert.Equal(5m, b.PaidOutDays);
        Assert.Equal(0m, b.LostDays);
    }

    [Fact]
    public void Transfer_Chain_NextYearCanTransferOnwardWhenItHasUnused5thWeekDays()
    {
        var data = Dat(Fill(20), true, (2025, 5m), (2026, 7m));
        var b = Bal(data, 2026, "2027-12-01");

        Assert.Equal(10m, b.MaxTransferToNext); // 10 5th-week days (5 own + 5 transferred in), nothing used
        Assert.Equal(7m, b.TransferredToNext);
        Assert.Equal(7m, Bal(data, 2027, "2027-12-01").TransferredIn);
    }

    [Fact]
    public void Transfer_BookingMoreDays_LowersTheCap()
    {
        // 5 transferred while 5 were unused; after booking 2 more days in 2026 only 3 can still be moved.
        var data = Dat(Fill(22), true, (2025, 5m));
        Assert.Equal(3m, Bal(data, 2025, "2026-12-18").TransferredToNext);
    }

    // ---------------------------------------------------------------- used / ahead split

    [Fact]
    public void TodayIsUsed_TomorrowIsBookedAhead()
    {
        // Fri 2026-12-18 booked (today) and Mon 2026-12-21 booked.
        var b = Bal(Data([Day("2026-12-18"), Day("2026-12-21")]), 2025, "2026-12-18");

        Assert.Equal(1m, b.UsedDays);
        Assert.Equal(1m, b.BookedAheadDays);
    }

    // ---------------------------------------------------------------- calendar-year status

    [Fact]
    public void Calendar_JanToAug_ConcernsHolidayYearStartedLastSeptember()
    {
        // Tue 2026-03-10: months Sep..Feb are complete = 6 -> 12.5 usable.
        var s = Cal(Data(), 2026, "2026-03-10");

        Assert.Equal(new HolidayYear(2025), s.HolidayYear.HolidayYear);
        Assert.Equal(12.5m, s.HolidayYear.EarnedSoFar);
        Assert.Equal(25m, s.HolidayYear.EarnableDays);
        Assert.Equal(D("2026-12-31"), s.Deadline);
    }

    [Fact]
    public void Calendar_SepToDec_StillConcernsTheExpiringYear()
    {
        var s = Cal(Data(), 2026, "2026-10-15");

        Assert.Equal(new HolidayYear(2025), s.HolidayYear.HolidayYear);
        Assert.Equal(25m, s.HolidayYear.EarnedSoFar);
        Assert.Equal(new HolidayYear(2026), Cal(Data(), 2027, "2026-10-15").HolidayYear.HolidayYear);
    }

    [Fact]
    public void Calendar_FutureYear_HasNothingEarned_AndAllWeekdaysFree()
    {
        // 2028 starts on a Saturday and is a leap year: 366 = 52 weeks + Sat + Sun -> 260 weekdays.
        var s = Cal(Data(), 2028, "2026-03-10");

        Assert.Equal(new HolidayYear(2027), s.HolidayYear.HolidayYear);
        Assert.Equal(0m, s.HolidayYear.EarnedSoFar);
        Assert.Equal(260m, s.WorkingDaysLeft);
        Assert.Equal(BalanceStatus.DaysToPlan, s.Status);
    }

    [Fact]
    public void Calendar_FullYear_CountsWeekdaysMinusHolidays()
    {
        // 2026 starts on Thursday, 365 days = 52 weeks + 1 Thursday -> 261 weekdays; minus Thu 01-01 and Fri 12-25.
        var data = Data().WithHolidays("2026-01-01", "2026-12-25");
        Assert.Equal(259m, Cal(data, 2026, "2026-01-01").WorkingDaysLeft);
    }

    [Fact]
    public void Calendar_PastYear_IsExpired_WithNoWorkingDaysLeft()
    {
        // 2025: holiday year 2024/25 with nothing booked -> all 25 unused: 5 paid out, 20 lost.
        var s = Cal(Data(), 2025, "2026-03-10");

        Assert.Equal(BalanceStatus.Expired, s.Status);
        Assert.Equal(0m, s.WorkingDaysLeft);
        Assert.Equal(5m, s.PaidOutDays);
        Assert.Equal(20m, s.LostDays);
    }

    [Fact]
    public void Calendar_DeadlineDayItself_IsNotExpired_DayAfterIs()
    {
        var data = Data(Fill(15));
        var onDeadline = Cal(data, 2026, "2026-12-31");
        var after = Cal(data, 2026, "2027-01-01");

        Assert.Equal(1m, onDeadline.WorkingDaysLeft); // Thu 12-31
        Assert.Equal(BalanceStatus.DaysWillBeLost, onDeadline.Status); // 10 unbooked > 1 free
        Assert.Equal(BalanceStatus.Expired, after.Status);
        Assert.Equal(0m, after.WorkingDaysLeft);
    }

    [Fact]
    public void Calendar_UnbookedEqualToFreeDays_IsDaysToPlan_HalfMoreIsWillBeLost()
    {
        // Thu 2026-12-31: one free day. 24 used -> 1 unbooked -> fits. 23.5 used -> 1.5 unbooked, 0.5 can't fit.
        var fits = Cal(Data(Fill(24)), 2026, "2026-12-31");
        var half = Cal(Data(Fill(23.5m)), 2026, "2026-12-31");

        Assert.Equal(BalanceStatus.DaysToPlan, fits.Status);
        Assert.Equal(BalanceStatus.DaysWillBeLost, half.Status);
        Assert.Equal(0.5m, half.DaysAtRisk);
    }

    [Fact]
    public void Calendar_AllPlanned_WhenEverythingUsed_OrTransferred()
    {
        Assert.Equal(BalanceStatus.AllPlanned, Cal(Data(Fill(25)), 2026, "2026-12-18").Status);
        Assert.Equal(BalanceStatus.AllPlanned,
            Cal(Dat(Fill(20), true, (2025, 5m)), 2026, "2026-12-18").Status);
        // After the deadline with nothing unbooked it is still AllPlanned, not Expired.
        Assert.Equal(BalanceStatus.AllPlanned, Cal(Data(Fill(25)), 2026, "2027-02-01").Status);
    }

    [Fact]
    public void Calendar_AtRiskSplit_SettingOff()
    {
        // Fri 2026-12-18, nothing booked: free 18, 21-25, 28-31 = 10; 25 - 10 = 15 can't fit.
        // Booking all 10 leaves 15 unused: 5 of the 5th week (paid out) and 10 within the first 20 (lost).
        var s = Cal(Data(), 2026, "2026-12-18");

        Assert.Equal(10m, s.WorkingDaysLeft);
        Assert.Equal(15m, s.DaysAtRisk);
        Assert.Equal(5m, s.AtRiskPaidOutDays);
        Assert.Equal(10m, s.AtRiskLostDays);
    }

    [Fact]
    public void Calendar_AtRiskSplit_WithTransfer()
    {
        // Setting on, 5 transferred: 20 unbooked, 10 free -> 10 at risk. Booking 10 uses 10 of the first 20, so the
        // 10 left are all within the first 20 (the 5th week is transferred): 0 paid out, 10 lost.
        var s = Cal(Dat(null, true, (2025, 5m)), 2026, "2026-12-18");

        Assert.Equal(20m, s.UnbookedDays);
        Assert.Equal(10m, s.DaysAtRisk);
        Assert.Equal(0m, s.AtRiskPaidOutDays);
        Assert.Equal(10m, s.AtRiskLostDays);
    }

    [Fact]
    public void Calendar_TransferOptionApplies_OnlyWhenOnAndUnused5thWeekAndNotAfterDeadline()
    {
        Assert.True(Cal(Data(Fill(20), transferAllowed: true), 2026, "2026-12-31").TransferOptionApplies);
        Assert.False(Cal(Data(Fill(20), transferAllowed: false), 2026, "2026-12-18").TransferOptionApplies);
        Assert.False(Cal(Data(Fill(25), transferAllowed: true), 2026, "2026-12-18").TransferOptionApplies); // none unused
        Assert.False(Cal(Data(Fill(20), transferAllowed: true), 2026, "2027-01-01").TransferOptionApplies); // too late
    }

    [Fact]
    public void Calendar_EmptyData_DoesNotThrow()
    {
        var s = Cal(new VacationData(), 2026, "2026-06-15");

        Assert.Equal(25m, s.UnbookedDays);
        Assert.Equal(BalanceStatus.DaysToPlan, s.Status); // Jun 15 .. Dec 31 has far more than 25 free weekdays
    }

    [Fact]
    public void Calendar_DuplicateDateRecords_CountOnce()
    {
        var data = Data([Day("2026-02-02"), Day("2026-02-02")]);

        Assert.Equal(1m, Bal(data, 2025, "2026-12-18").UsedDays);
    }
}
