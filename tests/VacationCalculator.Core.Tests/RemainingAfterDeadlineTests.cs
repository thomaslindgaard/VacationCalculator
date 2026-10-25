using VacationCalculator.Core;
using static VacationCalculator.Core.Tests.TestData;

namespace VacationCalculator.Core.Tests;

/// <summary>
/// Feature 8 review fix: <see cref="HolidayYearBalance.RemainingDays"/> is "remaining today". On or before the deadline
/// (31 December of the following year, still a usable day) it is earned so far + actual transferred in - booked; after
/// the deadline the days that left the year (transferred, paid out, lost) are removed too, like
/// <see cref="FeriefriBalance.RemainingDays"/>. So it is 0 after the deadline unless the year is over-booked.
/// Employed throughout, default limit 5 and payout None unless stated. 25/12 per month: 3 months = 6.25, 4 = 8.33.
/// Fill(n) books n weekdays from Mon 2026-01-05 (20 = .. Fri 01-30, 22 = .. Tue 02-03), all in 2025/26.
/// </summary>
public class RemainingAfterDeadlineTests
{
    private static readonly HolidayYear Hy2025 = new(2025);
    private static readonly HolidayYear Hy2026 = new(2026);

    private static HolidayYearBalance Bal(VacationData data, HolidayYear year, string today) =>
        BalanceCalculator.CalculateHolidayYear(data, year, D(today));

    // ------------------------------------------------------------ the scenario from the review

    [Fact]
    public void ReviewScenario_TransferredDaysAreNotRemainingTwice()
    {
        // 25 earned, 22 used in 2025/26, limit 5: 3 transferred. Today Tue 2027-01-05, calendar year 2026's rows.
        var data = Data(Fill(22), firstTracked: 2025);

        var rows = BalanceCalculator.CalculateHolidayYears(data, HolidayYear.InCalendarYear(2026), D("2027-01-05"));
        var older = rows[0];
        var newer = rows[1];

        Assert.True(older.DeadlinePassed);
        Assert.Equal(3m, older.TransferredToNext);
        Assert.Equal(0m, older.RemainingDays);              // was 3: the 3 days left the year on Thu 2026-12-31

        Assert.False(newer.DeadlinePassed);
        Assert.Equal(3m, newer.ActualTransferredIn);
        Assert.Equal(M("11.33"), newer.RemainingDays);      // Sep-Dec 8.33 + 3
        Assert.Equal(M("11.33"), older.RemainingDays + newer.RemainingDays);
    }

    [Theory]
    // Wed 2026-12-30: before the deadline. 2026/27 has Sep-Nov (6.25) and the 3 are only projected into it.
    [InlineData("2026-12-30", false, "3", "6.25")]
    // Thu 2026-12-31 is the deadline: still usable, so still open (same convention as Feriefri: passed = today > deadline).
    [InlineData("2026-12-31", false, "3", "6.25")]
    // Fri 2027-01-01: passed. The 3 left 2025/26 and are actual in 2026/27 (Sep-Dec 8.33 + 3).
    [InlineData("2027-01-01", true, "0", "11.33")]
    // Long after: Fri 2027-12-31 is 2026/27's own deadline (25 + 3 earned and transferred in, nothing booked).
    [InlineData("2027-12-31", true, "0", "28")]
    public void Transferred_RemainingUntilTheDeadline_ZeroAfter(string today, bool passed, string older, string newer)
    {
        var data = Data(Fill(22), firstTracked: 2025);

        var o = Bal(data, Hy2025, today);
        var n = Bal(data, Hy2026, today);

        Assert.Equal(passed, o.DeadlinePassed);
        Assert.Equal(M(older), o.RemainingDays);
        Assert.Equal(M(newer), n.RemainingDays);
        Assert.Equal(M(older) + M(newer), o.RemainingDays + n.RemainingDays); // each day counted once
        Assert.Equal(3m, o.TransferredToNext);              // the split itself doesn't change
        Assert.Equal(3m, o.UnbookedDays);
    }

    [Fact]
    public void DeadlinePassed_MatchesFeriefri()
    {
        var data = Data(Fill(22), firstTracked: 2025);
        foreach (var today in new[] { "2026-12-30", "2026-12-31", "2027-01-01" })
        {
            Assert.Equal(
                FeriefriCalculator.CalculateHolidayYear(data, Hy2025, D(today)).DeadlinePassed,
                Bal(data, Hy2025, today).DeadlinePassed);
        }
    }

    [Fact]
    public void HalfDayTransferred_ZeroAfter()
    {
        // 22.5 booked (half day Wed 2026-02-04): 2.5 of the 5th week transferred.
        var data = Data(Fill(M("22.5")), firstTracked: 2025);

        Assert.Equal(M("2.5"), Bal(data, Hy2025, "2026-12-31").RemainingDays);
        var after = Bal(data, Hy2025, "2027-01-01");
        Assert.Equal(M("2.5"), after.TransferredToNext);
        Assert.Equal(0m, after.RemainingDays);
    }

    // ------------------------------------------------------------ paid out and lost

    [Fact]
    public void PaidOut_FifthWeekWithoutTransfer_ZeroAfter()
    {
        // Limit 0 on 2025/26: the 3 unused 5th-week days are paid out.
        var data = Data(Fill(22), firstTracked: 2025).NoTransferFrom(2025);

        Assert.Equal(3m, Bal(data, Hy2025, "2026-12-31").RemainingDays);
        var after = Bal(data, Hy2025, "2027-01-05");
        Assert.Equal(0m, after.TransferredToNext);
        Assert.Equal(3m, after.PaidOutDays);
        Assert.Equal(0m, after.RemainingDays);
        Assert.Equal(0m, Bal(data, Hy2026, "2027-01-05").TransferredIn);
    }

    [Theory]
    // 15 booked: 10 unused = 5 5th week (transferred) + 5 other days.
    [InlineData(PayoutMode.None, 0, "0", "5")]
    [InlineData(PayoutMode.Some, 2, "2", "3")]
    [InlineData(PayoutMode.All, 0, "5", "0")]
    public void OtherDays_PaidOutOrLost_ZeroAfter(PayoutMode mode, int payoutDays, string paid, string lost)
    {
        var data = Data(Fill(15), firstTracked: 2025).WithPayout(2025, mode, payoutDays);

        Assert.Equal(10m, Bal(data, Hy2025, "2026-12-31").RemainingDays);
        var after = Bal(data, Hy2025, "2027-01-01");
        Assert.Equal(5m, after.TransferredToNext);
        Assert.Equal(M(paid), after.PaidOutDays);
        Assert.Equal(M(lost), after.LostDays);
        Assert.Equal(0m, after.RemainingDays);
    }

    [Fact]
    public void NothingBooked_EverythingLeaves_ZeroAfter()
    {
        // 25 unused: 5 transferred, 20 lost (payout None).
        var data = Data(firstTracked: 2025);

        Assert.Equal(25m, Bal(data, Hy2025, "2026-12-31").RemainingDays);
        var after = Bal(data, Hy2025, "2027-01-01");
        Assert.Equal(5m, after.TransferredToNext);
        Assert.Equal(20m, after.LostDays);
        Assert.Equal(0m, after.RemainingDays);
    }

    // ------------------------------------------------------------ over-booking

    [Fact]
    public void OverBooked_StaysNegativeAfterTheDeadline()
    {
        // 27 booked Mon 2026-01-05 .. Tue 2026-02-10 (Jan-Aug: only 2025/26 open), 25 earnable: 2 too many.
        var data = Data(Fill(27), firstTracked: 2025);

        var before = Bal(data, Hy2025, "2026-12-31");
        var after = Bal(data, Hy2025, "2027-01-01");

        Assert.Equal(-2m, before.RemainingDays);
        Assert.Equal(-2m, after.RemainingDays);
        Assert.Equal(0m, after.AtDeadline.UnusedDays);
        Assert.Equal(2m, after.ExceedsEarnedSoFarBy);
        Assert.Equal(2m, after.ExceedsEarnableBy);
    }

    [Fact]
    public void OverBooked_WithOpeningBalance_StaysNegativeAfterTheDeadline()
    {
        // Opening balance 1, 27 booked: 26 available, 1 too many. Nothing is unused.
        var data = Data(Fill(27), firstTracked: 2025, openingBalance: 1);

        Assert.Equal(-1m, Bal(data, Hy2025, "2026-12-31").RemainingDays);
        Assert.Equal(-1m, Bal(data, Hy2025, "2027-01-01").RemainingDays);
    }

    // ------------------------------------------------------------ first tracked year and earlier years

    [Fact]
    public void FirstTrackedYear_OpeningBalance_UnusedTransferredInIsLost_ZeroAfter()
    {
        // Opening balance 4 into 2025/26, 2 booked (Mon 2026-01-05, Tue 01-06): they use the opening balance.
        // At the deadline: 2 transferred-in days lost, 5 own 5th-week days transferred, 20 own days lost.
        var data = Data(Fill(2), firstTracked: 2025, openingBalance: 4);

        Assert.Equal(27m, Bal(data, Hy2025, "2026-12-31").RemainingDays); // 25 + 4 - 2
        var after = Bal(data, Hy2025, "2027-01-01");
        Assert.Equal(2m, after.UnusedTransferredInDays);
        Assert.Equal(5m, after.TransferredToNext);
        Assert.Equal(22m, after.LostDays);
        Assert.Equal(0m, after.RemainingDays);
    }

    [Theory]
    // First tracked 2026/27: 2025/26 transfers nothing, so its 3 unused 5th-week days are paid out.
    [InlineData("2026-12-31", "3")]
    [InlineData("2027-01-05", "0")]
    public void YearBeforeTheFirstTracked_PaidOutAfterTheDeadline(string today, string remaining)
    {
        var data = Data(Fill(22), firstTracked: 2026, openingBalance: 3);

        var b = Bal(data, Hy2025, today);
        Assert.Equal(0m, b.TransferredIn);
        Assert.Equal(0m, b.TransferredToNext);
        Assert.Equal(3m, b.PaidOutDays);
        Assert.Equal(M(remaining), b.RemainingDays);
    }

    [Fact]
    public void YearBeforeTheFirstTracked_OtherDaysLost_FirstTrackedKeepsItsOpeningBalance()
    {
        // First tracked 2026/27 with opening balance 3; 2025/26 has 15 booked: 5 5th week paid out, 5 other lost.
        var data = Data(Fill(15), firstTracked: 2026, openingBalance: 3);
        var rows = BalanceCalculator.CalculateHolidayYears(data, HolidayYear.InCalendarYear(2026), D("2027-01-05"));

        Assert.Equal(5m, rows[0].PaidOutDays);
        Assert.Equal(5m, rows[0].LostDays);
        Assert.Equal(0m, rows[0].RemainingDays);
        Assert.Equal(3m, rows[1].TransferredIn);
        Assert.False(rows[1].TransferredInIsProjected);
        Assert.Equal(M("11.33"), rows[1].RemainingDays);   // Sep-Dec 8.33 + opening 3
    }

    // ------------------------------------------------------------ calendar-year status and range edges

    [Fact]
    public void CalendarYearStatus_AfterTheDeadline_ShowsZeroRemaining_StatusUnchanged()
    {
        // Calendar year 2026 concerns 2025/26; 3 transferred, nothing paid out or lost. Today Tue 2027-01-05.
        var s = BalanceCalculator.CalculateCalendarYear(Data(Fill(22), firstTracked: 2025), 2026, D("2027-01-05"));

        Assert.True(s.HolidayYear.DeadlinePassed);
        Assert.Equal(0m, s.HolidayYear.RemainingDays);
        Assert.Equal(3m, s.UnbookedDays);
        Assert.Equal(BalanceStatus.AllPlanned, s.Status);
    }

    [Fact]
    public void FirstRepresentableYear_ZeroAfterItsDeadline()
    {
        // Holiday year 0001/02, nothing booked, deadline 0002-12-31.
        var data = Data(firstTracked: 1);
        var year = new HolidayYear(HolidayYear.MinStartYear);

        Assert.Equal(25m, Bal(data, year, "0002-12-31").RemainingDays);
        Assert.Equal(0m, Bal(data, year, "0003-01-01").RemainingDays);
    }

    [Fact]
    public void LastRepresentableYear_DeadlineDayIsNotPassed()
    {
        // Holiday year 9998/99, deadline Fri 9999-12-31 = DateOnly.MaxValue: it can never have passed.
        var b = Bal(Data(firstTracked: 9998), new HolidayYear(HolidayYear.MaxStartYear), "9999-12-31");

        Assert.False(b.DeadlinePassed);
        Assert.Equal(25m, b.RemainingDays);
    }

    [Fact]
    public void LeapYearDeadline_PassedOnTheFirstOfJanuary()
    {
        // 2027/28, deadline Sun 2028-12-31 (2028 is a leap year); nothing booked, first tracked 2027.
        var data = Data(firstTracked: 2027);
        var year = new HolidayYear(2027);

        Assert.Equal(25m, Bal(data, year, "2028-12-31").RemainingDays);
        Assert.Equal(0m, Bal(data, year, "2029-01-01").RemainingDays);
    }
}
