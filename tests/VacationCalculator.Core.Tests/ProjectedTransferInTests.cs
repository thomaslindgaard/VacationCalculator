using System.Diagnostics;
using VacationCalculator.Core;
using static VacationCalculator.Core.Tests.TestData;

namespace VacationCalculator.Core.Tests;

/// <summary>
/// Feature 2b fix round: a transfer into the newer open holiday year is only a projection until the older year's
/// deadline has passed, so it must not count in "remaining now" (the same days still count in the older year).
/// Also: hand-edited first tracked years outside 1..9998 must not crash or loop for ages.
/// Employed throughout unless stated. 25/12 per month: 1 month = 2.08, 3 = 6.25, 4 = 8.33.
/// </summary>
public class ProjectedTransferInTests
{
    private static readonly HolidayYear Hy2025 = new(2025);
    private static readonly HolidayYear Hy2026 = new(2026);

    private static HolidayYearBalance Bal(VacationData data, HolidayYear year, string today) =>
        BalanceCalculator.CalculateHolidayYear(data, year, D(today));

    /// <summary>Transfer on, default limit 5, first tracked 2025/26, 20 days used Mon 2026-01-05 .. Fri 2026-01-30.</summary>
    private static VacationData Scenario(IEnumerable<BookedDay>? extra = null) =>
        Data(Fill(20).Concat(extra ?? []), firstTracked: 2025);

    // ------------------------------------------------------------ the scenario from the review

    [Fact]
    public void SepToDec_ProjectedTransferIn_IsNotCountedTwice()
    {
        // Today Sat 2026-10-24: both 2025/26 and 2026/27 are open.
        var data = Scenario();

        var older = Bal(data, Hy2025, "2026-10-24");
        var newer = Bal(data, Hy2026, "2026-10-24");

        Assert.Equal(5m, older.RemainingDays);          // 25 - 20
        Assert.Equal(5m, older.TransferredToNext);
        Assert.False(older.TransferredInIsProjected);  // first tracked year: opening balance (0) is actual

        Assert.Equal(5m, newer.TransferredIn);          // projection still available for display
        Assert.True(newer.TransferredInIsProjected);
        Assert.Equal(0m, newer.ActualTransferredIn);
        Assert.Equal(M("2.08"), newer.EarnedSoFar);     // September 2026 only
        Assert.Equal(M("2.08"), newer.RemainingDays);   // was 7.08 (2.08 + projected 5)
        Assert.Equal(0m, newer.ExceedsEarnedSoFarBy);

        // Totals for allocation and the deadline split still include the full projected transfer.
        Assert.Equal(30m, newer.TotalDays);
        Assert.Equal(30m, newer.UnbookedDays);
        Assert.Equal(5m, newer.UnusedTransferredInDays);

        // The card's total remaining is what really exists: 5 + 2.08.
        Assert.Equal(M("7.08"), older.RemainingDays + newer.RemainingDays);
    }

    [Fact]
    public void SepToDec_BookingTheOlderYearsRest_RemovesTheProjection_TotalDropsByExactlyFive()
    {
        // + Mon 2026-11-02 .. Fri 2026-11-06 = 5 days; they go to the older year 2025/26. Today Sat 2026-10-24.
        var data = Scenario(Range("2026-11-02", "2026-11-06"));

        var older = Bal(data, Hy2025, "2026-10-24");
        var newer = Bal(data, Hy2026, "2026-10-24");

        Assert.Equal(5m, older.BookedAheadDays);
        Assert.Equal(0m, older.RemainingDays);
        Assert.Equal(0m, older.TransferredToNext);
        Assert.Equal(0m, newer.TransferredIn);
        Assert.Equal(M("2.08"), newer.RemainingDays);   // unchanged by the booking in the older year
        Assert.Equal(M("2.08"), older.RemainingDays + newer.RemainingDays); // 7.08 - 5
    }

    [Theory]
    // Before 2026/27 has started: the projected transfer is not here yet.
    [InlineData("2026-08-15", true, "0")]
    // Sat 2026-10-24, inside both taking periods.
    [InlineData("2026-10-24", true, "2.08")]
    // Thu 2026-12-31 is 2025/26's deadline: still projected (days can be taken that day). Sep-Nov earned.
    [InlineData("2026-12-31", true, "6.25")]
    // Fri 2027-01-01: the deadline has passed, the transfer is actual. Sep-Dec earned 8.33 + 5.
    [InlineData("2027-01-01", false, "13.33")]
    // Thu 2027-12-30: long after; 2026/27 has earned everything: 25 + 5.
    [InlineData("2027-12-30", false, "30")]
    public void TransferIn_IsProjectedUntilThePreviousDeadlineHasPassed(string today, bool projected, string remaining)
    {
        var b = Bal(Scenario(), Hy2026, today);

        Assert.Equal(5m, b.TransferredIn);
        Assert.Equal(projected, b.TransferredInIsProjected);
        Assert.Equal(projected ? 0m : 5m, b.ActualTransferredIn);
        Assert.Equal(M(remaining), b.RemainingDays);
        Assert.Equal(30m, b.TotalDays);
    }

    [Fact]
    public void AdvanceHolidayInTheNewerYear_IsVisibleWhileTheTransferIsProjected()
    {
        // + Mon 2027-01-04 .. Thu 2027-01-07 = 4 days in 2026/27 (January: only 2026/27 open). Today Sat 2026-10-24.
        var data = Scenario(Range("2027-01-04", "2027-01-07"));

        var newer = Bal(data, Hy2026, "2026-10-24");

        Assert.Equal(4m, newer.BookedAheadDays);
        Assert.Equal(M("-1.92"), newer.RemainingDays);      // 2.08 - 4 (was +3.08 with the projected 5 counted)
        Assert.Equal(M("1.92"), newer.ExceedsEarnedSoFarBy);
        // The deadline split still uses the full projected transfer: the 4 bookings use transferred-in days first.
        Assert.Equal(1m, newer.UnusedTransferredInDays);
        Assert.Equal(5m, newer.TransferredToNext);           // own 5th week, limit 5
        Assert.Equal(0m, newer.ExceedsEarnableBy);
        Assert.Equal(26m, newer.UnbookedDays);
    }

    [Fact]
    public void AdvanceHolidayInTheNewerYear_CoveredOnceTheTransferIsActual()
    {
        // Same 4 days Mon 2027-01-04 .. Thu 2027-01-07; today Fri 2027-01-01 (after 2025/26's deadline).
        var data = Scenario(Range("2027-01-04", "2027-01-07"));

        var newer = Bal(data, Hy2026, "2027-01-01");

        Assert.False(newer.TransferredInIsProjected);
        Assert.Equal(M("9.33"), newer.RemainingDays);       // 8.33 + 5 - 4
        Assert.Equal(0m, newer.ExceedsEarnedSoFarBy);
    }

    [Fact]
    public void FirstTrackedYear_OpeningBalance_IsActualFromTheStart()
    {
        // First tracked 2026/27 with an opening balance of 3; today Sat 2026-10-24 (before 2025/26's deadline).
        var data = Data(Fill(20), firstTracked: 2026, openingBalance: 3);

        var newer = Bal(data, Hy2026, "2026-10-24");
        var older = Bal(data, Hy2025, "2026-10-24");

        Assert.Equal(3m, newer.TransferredIn);
        Assert.False(newer.TransferredInIsProjected);
        Assert.Equal(3m, newer.ActualTransferredIn);
        Assert.Equal(M("5.08"), newer.RemainingDays);      // 2.08 + 3
        Assert.Equal(0m, older.TransferredToNext);          // untracked year transfers nothing
        Assert.False(older.TransferredInIsProjected);
    }

    [Fact]
    public void LimitZero_NothingIsProjected()
    {
        // 2c: transfer off is limit 0 on the transferring year (2025)
        var data = Data(Fill(20), firstTracked: 2025).NoTransferFrom(2025);

        var newer = Bal(data, Hy2026, "2026-10-24");

        Assert.Equal(0m, newer.TransferredIn);
        Assert.False(newer.TransferredInIsProjected);
        Assert.Equal(M("2.08"), newer.RemainingDays);
    }

    [Fact]
    public void CalendarYearStatus_IsUnchangedByTheProjection()
    {
        // 2027 concerns 2026/27; its unbooked total still includes the projected transfer. Today Sat 2026-10-24.
        var s = BalanceCalculator.CalculateCalendarYear(Scenario(), 2027, D("2026-10-24"));

        Assert.Equal(30m, s.UnbookedDays);
        Assert.Equal(5m, s.HolidayYear.TransferredIn);
        Assert.True(s.HolidayYear.TransferredInIsProjected);
    }

    // ------------------------------------------------------------ bad stored first tracked years

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(-2_000_000_000, 1)]
    [InlineData(int.MinValue, 1)]
    [InlineData(1, 1)]
    [InlineData(9998, 9998)]
    [InlineData(9999, 9998)]
    [InlineData(10000, 9998)]
    [InlineData(int.MaxValue, 9998)]
    public void EffectiveFirstTrackedYear_IsClamped_StoredValueKept(int stored, int effective)
    {
        var data = new VacationData { FirstTrackedHolidayYear = stored };

        Assert.Equal(new HolidayYear(effective), data.EffectiveFirstTrackedHolidayYear(D("2026-10-24")));
        Assert.Equal(stored, data.FirstTrackedHolidayYear);
        Assert.False(data.EnsureFirstTrackedHolidayYear(D("2026-10-24")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(int.MinValue)]
    [InlineData(10000)]
    [InlineData(int.MaxValue)]
    public void BadFirstTrackedYear_WithTransferOn_DoesNotThrowAndIsFast(int stored)
    {
        var data = Data(Fill(20), firstTracked: stored, openingBalance: 2);
        var today = D("2026-10-24");
        var watch = Stopwatch.StartNew();

        var years = BalanceCalculator.CalculateHolidayYears(data, HolidayYear.InCalendarYear(2026), today);
        var status = BalanceCalculator.CalculateCalendarYear(data, 2026, today);
        var allocations = BalanceCalculator.Allocate(data, today);

        watch.Stop();
        Assert.Equal(2, years.Count);
        Assert.Equal(20m, allocations.Sum(a => a.Amount));
        Assert.Equal(20m, status.HolidayYear.UsedDays);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
    }

    [Fact]
    public void FirstTrackedYearZero_ClampedToYearOne_LaterYearsReceiveTransfers()
    {
        // Clamped to 0001/02: every year from then on is tracked. 2024/25 has no bookings, so it transfers its
        // 5th week (5) into 2025/26, which is actual on Sat 2026-10-24 (2024/25's deadline 2025-12-31 has passed).
        var data = Data(Fill(20), firstTracked: 0);

        var b = Bal(data, Hy2025, "2026-10-24");

        Assert.Equal(5m, b.TransferredIn);
        Assert.False(b.TransferredInIsProjected);
        Assert.Equal(10m, b.RemainingDays); // 25 + 5 - 20
    }

    [Fact]
    public void FirstTrackedYearAboveRange_NothingIsTracked()
    {
        var data = Data(Fill(20), firstTracked: 10000, openingBalance: 3);

        var b = Bal(data, Hy2025, "2026-10-24");

        Assert.Equal(0m, b.TransferredIn);
        Assert.Equal(0m, b.TransferredToNext);
        Assert.Equal(5m, b.PaidOutDays); // untracked: the unused 5th week is paid out
    }

    [Fact]
    public void FarFutureSelectedYear_FromFirstTrackedYearOne_IsFast()
    {
        var data = Data(Fill(20), firstTracked: 1);
        var today = D("2026-10-24");
        var watch = Stopwatch.StartNew();

        var last = BalanceCalculator.CalculateHolidayYears(data, HolidayYear.InCalendarYear(9998), today);
        var status = BalanceCalculator.CalculateCalendarYear(data, 9999, today);

        watch.Stop();
        Assert.Equal(new HolidayYear(9998), last[1].HolidayYear);
        Assert.Equal(5m, last[1].TransferredIn);  // every untouched year transfers its 5th week
        Assert.True(last[1].TransferredInIsProjected);
        Assert.Equal(new HolidayYear(9998), status.HolidayYear.HolidayYear);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
    }

    [Fact]
    public void BookingsAtTheEdgesOfDateOnly_DoNotThrow()
    {
        // Mon 0001-01-01 (holiday year 0, unrepresentable: ignored), Mon 0001-09-03 (holiday year 1, no older year),
        // Thu 9999-09-02 (holiday year 9999, unrepresentable: ignored).
        var data = Data([Day("0001-01-01"), Day("0001-09-03"), Day("9999-09-02")], firstTracked: 1);

        var allocations = BalanceCalculator.Allocate(data, D("2026-10-24"));

        var only = Assert.Single(allocations);
        Assert.Equal(new HolidayYear(1), only.HolidayYear);
        Assert.Equal(D("0001-09-03"), only.Date);
    }
}
