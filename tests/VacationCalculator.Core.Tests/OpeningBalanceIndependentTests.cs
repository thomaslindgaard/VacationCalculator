using VacationCalculator.Core;
using static VacationCalculator.Core.Tests.TestData;

namespace VacationCalculator.Core.Tests;

/// <summary>
/// Feature 2b opening balance and the "limit above 5" transfer rule, hand-counted from docs/features.md section 2b.
/// Fill(n) books n weekdays from Mon 2026-01-05 (holiday year 2025); Fill(n, "2027-01-04") books in holiday year 2026.
/// Own days of a full year: 25 = the first 20 + the 5th week (5). "Late" is after every deadline involved.
/// </summary>
public class OpeningBalanceIndependentTests
{
    private const string Late = "2029-01-02";

    private static HolidayYearBalance Bal(VacationData data, int startYear, string today = Late) =>
        BalanceCalculator.CalculateHolidayYear(data, new HolidayYear(startYear), D(today));

    // ------------------------------------------------------------ opening balance into the first tracked year

    [Fact]
    public void Opening_ReceivedByFirstTrackedYear_NotByEarlierOrLater()
    {
        // First tracked 2026, opening 3, nothing booked, transfer off (2c: limit 0 on 2026).
        var data = Data(firstTracked: 2026, openingBalance: 3).NoTransferFrom(2026);

        Assert.Equal(0m, Bal(data, 2025).TransferredIn);
        Assert.Equal(3m, Bal(data, 2026).TransferredIn);
        Assert.Equal(28m, Bal(data, 2026).TotalDays);
        Assert.Equal(0m, Bal(data, 2027).TransferredIn); // limit 0: nothing flows on from 2026
    }

    [Fact]
    public void Opening_LimitZero_UnusedIsLost_5thWeekPaid()
    {
        // 2026: 3 opening days unused -> lost. Own 25 unused: 5th week 5 paid out (always), first 20 lost.
        // Lost = 3 + 20 = 23, paid out 5, transferred 0.
        var b = Bal(Data(firstTracked: 2026, openingBalance: 3).NoTransferFrom(2026), 2026); // 2c: limit 0

        Assert.Equal(0m, b.TransferredToNext);
        Assert.Equal(5m, b.PaidOutDays);
        Assert.Equal(23m, b.LostDays);
        Assert.Equal(3m, b.UnusedTransferredInDays);
        Assert.Equal(28m, b.TransferredToNext + b.PaidOutDays + b.LostDays);
    }

    [Fact]
    public void Opening_TransferOn_OpeningIsLostIfUnused_AndNeverTransferredAgain()
    {
        // Opening 3, nothing booked, limit 100 so everything own could move: own 25 all move, the 3 opening days do not.
        // 2027 therefore gets 25 (not 28). Lost in 2026 = the 3 opening days only.
        var data = Data(firstTracked: 2026, openingBalance: 3).WithLimit(2026, 100);

        var b = Bal(data, 2026);

        Assert.Equal(25m, b.TransferredToNext);
        Assert.Equal(3m, b.LostDays);
        Assert.Equal(25m, Bal(data, 2027).TransferredIn);
    }

    [Fact]
    public void Opening_TransferOn_DefaultLimit_Gives5NotOpeningPlus5()
    {
        var data = Data(firstTracked: 2026, openingBalance: 3);

        var b = Bal(data, 2026);

        Assert.Equal(5m, b.TransferredToNext);
        Assert.Equal(5m, Bal(data, 2027).TransferredIn);
        Assert.Equal(23m, b.LostDays); // 3 opening + 20 first-20 days
    }

    [Theory]
    // booked in 2026, expected unused opening (lost), expected own transferred, expected lost
    [InlineData("0", "3", "5", "23")]  // none booked: 3 opening lost, own 25: 5 moved, 20 lost
    [InlineData("2", "1", "5", "21")]  // 2 of the 3 opening used first -> 1 lost; own untouched: 5 moved, 20 lost
    [InlineData("3", "0", "5", "20")]  // exactly the opening
    [InlineData("5", "0", "5", "18")]  // 3 opening + 2 own (first 20): own unused 23 = 5th week 5 + 18 other
    [InlineData("23", "0", "5", "0")]  // 3 opening + 20 own: only the 5th week left, moved
    [InlineData("26", "0", "2", "0")]  // 3 opening + 23 own: 2 5th-week days left, moved
    public void Opening_IsUsedFirst(string booked, string unusedOpening, string transferred, string lost)
    {
        var data = Data(Fill(M(booked), "2027-01-04"), firstTracked: 2026, openingBalance: 3);

        var b = Bal(data, 2026);

        Assert.Equal(M(unusedOpening), b.UnusedTransferredInDays);
        Assert.Equal(M(transferred), b.TransferredToNext);
        Assert.Equal(M(lost), b.LostDays);
        Assert.Equal(0m, b.PaidOutDays);
    }

    [Fact]
    public void Opening_OverBooking_UsesOpeningThenOwn()
    {
        // Capacity 28; 29 booked -> over-booked by 1, nothing transferred.
        var b = Bal(Data(Fill(29, "2027-01-04"), firstTracked: 2026, openingBalance: 3), 2026);

        Assert.Equal(1m, b.ExceedsEarnableBy);
        Assert.Equal(0m, b.TransferredToNext);
        Assert.Equal(0m, b.LostDays);
    }

    [Fact]
    public void Opening_Decimal_IsKept()
    {
        var b = Bal(Data(firstTracked: 2026, openingBalance: 2.5m), 2026);

        Assert.Equal(2.5m, b.TransferredIn);
        Assert.Equal(27.5m, b.TotalDays);
        Assert.Equal(22.5m, b.LostDays); // 2.5 + 20
    }

    [Fact]
    public void Opening_Negative_IsStoredAsZero()
    {
        var data = Data(firstTracked: 2026, openingBalance: -4);

        Assert.Equal(0m, data.OpeningBalanceDays);
        Assert.Equal(0m, Bal(data, 2026).TransferredIn);
    }

    [Fact]
    public void Opening_Zero_ChangesNothing()
    {
        var b = Bal(Data(firstTracked: 2026), 2026);

        Assert.Equal(0m, b.TransferredIn);
        Assert.Equal(25m, b.TotalDays);
    }

    [Fact]
    public void Opening_IsUsedByBookingsInTheOlderYear_BeforeTheNewerYearIsTouched()
    {
        // First tracked 2025, opening 4: 2025 holds 25 + 4 = 29. Fill(25) fills the 25 own days only if opening
        // isn't counted; here the opening goes first, so 4 of 2025's days are left for Mon 2026-11-02 (2025 still
        // open until 31 Dec 2026) and the booking lands in 2025, not 2026.
        var data = Data(Fill(25).Append(Day("2026-11-02")), firstTracked: 2025, openingBalance: 4);

        var allocation = BalanceCalculator.Allocate(data, D("2026-10-24")).Single(a => a.Date == D("2026-11-02"));
        Assert.Equal(new HolidayYear(2025), allocation.HolidayYear);

        // Without the opening balance 2025 is full (25) and the same booking goes to 2026.
        var without = Data(Fill(25).Append(Day("2026-11-02")), firstTracked: 2025);
        Assert.Equal(new HolidayYear(2026),
            BalanceCalculator.Allocate(without, D("2026-10-24")).Single(a => a.Date == D("2026-11-02")).HolidayYear);
    }

    [Fact]
    public void Opening_AppliesToAnyFirstYear_EvenWhenBookingsExistInEarlierYears()
    {
        // First tracked 2026 with bookings in 2025 (Jan 2026): they use 2025's own days only.
        var data = Data(Fill(20), firstTracked: 2026, openingBalance: 3);

        Assert.Equal(20m, Bal(data, 2025).UsedDays);
        Assert.Equal(3m, Bal(data, 2026).TransferredIn);
        Assert.Equal(0m, Bal(data, 2026).UsedDays);
    }

    // ------------------------------------------------------------ chain from the first tracked year

    [Fact]
    public void Chain_StartsAtFirstTrackedYear_WithOpening_ThenCalculatedTransfers()
    {
        // First tracked 2025, opening 4, nothing booked, transfer on, limit 5.
        // 2025: in 4 (lost), out 5. 2026: in 5 (lost), out 5. 2027: in 5.
        var data = Data(firstTracked: 2025, openingBalance: 4);

        Assert.Equal(4m, Bal(data, 2025).TransferredIn);
        Assert.Equal(5m, Bal(data, 2025).TransferredToNext);
        Assert.Equal(5m, Bal(data, 2026).TransferredIn);
        Assert.Equal(5m, Bal(data, 2026).TransferredToNext);
        Assert.Equal(5m, Bal(data, 2027).TransferredIn);
        Assert.Equal(4m, Bal(data, 2025).UnusedTransferredInDays);
        Assert.Equal(5m, Bal(data, 2026).UnusedTransferredInDays);
    }

    [Fact]
    public void Chain_ThreeYears_UsedFirstEachYear()
    {
        // First tracked 2025, opening 2. 2025: Fill(22) books 2 opening + 20 own: own unused 5 (5th week) -> 5 to 2026.
        // 2026: Fill(7, 2027-01-04): 5 transferred-in + 2 own: own unused 23 = 5 (5th) + 18 -> 5 to 2027.
        // 2027: Fill(1, 2028-01-03): 5 in, 1 used, 4 lost; own 25 unused -> 5 out.
        var data = Data(Fill(22).Concat(Fill(7, "2027-01-04")).Concat(Fill(1, "2028-01-03")),
            firstTracked: 2025, openingBalance: 2);

        Assert.Equal(0m, Bal(data, 2025).UnusedTransferredInDays);
        Assert.Equal(5m, Bal(data, 2025).TransferredToNext);
        Assert.Equal(5m, Bal(data, 2026).TransferredIn);
        Assert.Equal(0m, Bal(data, 2026).UnusedTransferredInDays);
        Assert.Equal(5m, Bal(data, 2026).TransferredToNext);
        Assert.Equal(18m, Bal(data, 2026).LostDays);
        Assert.Equal(5m, Bal(data, 2027).TransferredIn);
        Assert.Equal(4m, Bal(data, 2027).UnusedTransferredInDays);
        Assert.Equal(5m, Bal(data, 2027).TransferredToNext);
    }

    [Fact]
    public void Chain_Opening_ThenNoTransferAfterwardsWhenLimitZero()
    {
        var data = Data(firstTracked: 2025, openingBalance: 4).NoTransferFrom(2025); // 2c: was the employment-wide switch off

        Assert.Equal(4m, Bal(data, 2025).TransferredIn);
        Assert.Equal(0m, Bal(data, 2026).TransferredIn);
    }

    // ------------------------------------------------------------ years before the first tracked year

    [Fact]
    public void BeforeFirstTracked_NothingTransferred_5thWeekPaid_OthersPerPayoutSetting()
    {
        // First tracked 2026; 2025 has 10 used, so 15 unused = 5th week 5 + 10 other. Transfer on, but 2025 is before
        // the first tracked year: nothing moves. Payout None: 5 paid out, 10 lost. Payout All: 15 paid, 0 lost.
        var none = Bal(Data(Fill(10), firstTracked: 2026), 2025);
        Assert.Equal(0m, none.TransferredToNext);
        Assert.Equal(5m, none.PaidOutDays);
        Assert.Equal(10m, none.LostDays);
        Assert.Equal(0m, none.TransferredIn);

        var all = Bal(Data(Fill(10), firstTracked: 2026).WithPayout(2025, PayoutMode.All), 2025);
        Assert.Equal(15m, all.PaidOutDays);
        Assert.Equal(0m, all.LostDays);
    }

    [Fact]
    public void BeforeFirstTracked_DoesNotFeedTheFirstTrackedYear()
    {
        // 2025 has 5 unused days + transfer on would give 5 to 2026 if it were tracked. It isn't: 2026 gets only the
        // opening balance (1).
        var data = Data(Fill(20), firstTracked: 2026, openingBalance: 1);

        Assert.Equal(1m, Bal(data, 2026).TransferredIn);
    }

    [Fact]
    public void BeforeFirstTracked_BookingsStillCountAsUsedForThatYear()
    {
        var b = Bal(Data(Fill(12), firstTracked: 2030), 2025);

        Assert.Equal(12m, b.UsedDays);
        Assert.Equal(0m, b.TransferredToNext);
        Assert.Equal(0m, b.TransferredIn);
    }

    [Fact]
    public void FirstTrackedYear_ItselfTransfers_YearBeforeDoesNot_Boundary()
    {
        // First tracked 2025: 2025 transfers 5 (nothing booked), 2024 transfers nothing.
        var data = Data(firstTracked: 2025);

        Assert.Equal(5m, Bal(data, 2025).TransferredToNext);
        Assert.Equal(0m, Bal(data, 2024).TransferredToNext);
        Assert.Equal(5m, Bal(data, 2026).TransferredIn);
    }

    [Fact]
    public void CalendarYearStatus_UsesTheOpeningBalance()
    {
        // 2026 deadline year = holiday year 2025. First tracked 2025, opening 4, 21 booked in Jan/Feb 2026 (used by
        // 2026-10-24): 4 opening + 17 own. Own unused 8 = 5th week 5 + 3 other. Limit 0 (2c): 5 paid out, 3 lost.
        var status = BalanceCalculator.CalculateCalendarYear(
            Data(Fill(21), firstTracked: 2025, openingBalance: 4).NoTransferFrom(2025), 2026, D("2026-10-24"));

        Assert.Equal(29m, status.HolidayYear.TotalDays);
        Assert.Equal(8m, status.UnbookedDays);
        Assert.Equal(5m, status.PaidOutDays);
        Assert.Equal(3m, status.LostDays);
    }

    // ------------------------------------------------------------ default / effective first tracked year

    [Theory]
    // today, expected start year of the oldest holiday year open on that date
    [InlineData("2026-08-31", 2025)] // Aug: only 2025/26 is open
    [InlineData("2026-09-01", 2025)] // 1 Sep: 2025/26 (until 31 Dec 2026) and 2026/27 are open; oldest 2025
    [InlineData("2026-10-24", 2025)]
    [InlineData("2026-12-31", 2025)] // deadline day of 2025/26: still open
    [InlineData("2027-01-01", 2026)] // 2025/26 expired; only 2026/27 open
    [InlineData("2027-08-31", 2026)]
    [InlineData("2027-09-01", 2026)]
    [InlineData("2027-12-31", 2026)]
    [InlineData("2028-01-01", 2027)]
    [InlineData("2024-02-29", 2023)] // leap day: only 2023/24 open
    [InlineData("2024-12-31", 2023)]
    public void DefaultFirstTrackedYear_IsTheOldestHolidayYearOpenOnToday(string today, int expected)
    {
        Assert.Equal(new HolidayYear(expected), VacationData.DefaultFirstTrackedHolidayYear(D(today)));
        Assert.Equal(new HolidayYear(expected), new VacationData().EffectiveFirstTrackedHolidayYear(D(today)));
    }

    [Fact]
    public void Effective_StoredValueWins_OverTheDefault()
    {
        var data = Data(firstTracked: 2019);

        Assert.Equal(new HolidayYear(2019), data.EffectiveFirstTrackedHolidayYear(D("2026-10-24")));
        Assert.Equal(new HolidayYear(2019), data.EffectiveFirstTrackedHolidayYear(D("2031-01-01")));
    }

    [Fact]
    public void Ensure_StoresTheDefaultOnce_AndThenNeverMoves()
    {
        var data = new VacationData();

        Assert.True(data.EnsureFirstTrackedHolidayYear(D("2026-12-31")));
        Assert.Equal(2025, data.FirstTrackedHolidayYear);
        Assert.False(data.EnsureFirstTrackedHolidayYear(D("2027-01-01")));
        Assert.Equal(2025, data.FirstTrackedHolidayYear);
        Assert.Equal(new HolidayYear(2025), data.EffectiveFirstTrackedHolidayYear(D("2030-05-05")));
    }

    [Fact]
    public void Ensure_KeepsAnExplicitValue_EvenAnOldOne()
    {
        var data = Data(firstTracked: 2010);

        Assert.False(data.EnsureFirstTrackedHolidayYear(D("2026-10-24")));
        Assert.Equal(2010, data.FirstTrackedHolidayYear);
    }

    [Fact]
    public void NotStored_ThenAnUnstoredFileFollowsTodayUntilEnsured()
    {
        // A file without a stored first tracked year uses the default of 'today' in every calculation.
        var data = Data(Fill(20), openingBalance: 5);

        // Late in 2026: default 2025 -> opening 5 lands in 2025.
        Assert.Equal(5m, Bal(data, 2025, "2026-12-18").TransferredIn);
        // On 2027-01-04: default 2026 -> 2025 is before the first tracked year, opening lands in 2026.
        Assert.Equal(0m, Bal(data, 2025, "2027-01-04").TransferredIn);
        Assert.Equal(5m, Bal(data, 2026, "2027-01-04").TransferredIn);
    }

    // ------------------------------------------------------------ the limit above 5 rule (Split directly)

    private static DeadlineOutcome Split(
        decimal limit, decimal transferredIn, decimal own, decimal booked, bool transfer = true,
        PayoutMode mode = PayoutMode.None, decimal payoutDays = 0)
    {
        var data = Data().WithLimit(2025, limit).WithPayout(2025, mode, payoutDays);
        return data.GetDeadlineSettings(new HolidayYear(2025)).Split(transferredIn, own, booked, transfer);
    }

    [Fact]
    public void Limit_Default5_MovesOnlyTheFifthWeek_NeverDaysFromTheFirst20()
    {
        // Own 25, 10 booked: unused 15 = 5th week 5 + 10 first-20. Limit 5 -> 5 moved; 10 lost.
        var o = Split(5, 0, 25, 10);

        Assert.Equal(5m, o.TransferredDays);
        Assert.Equal(0m, o.PaidOutDays);
        Assert.Equal(10m, o.LostDays);
    }

    [Fact]
    public void Limit_8_Moves5Plus3()
    {
        var o = Split(8, 0, 25, 10); // unused 5 + 10; 5th week 5 + (8-5)=3 of the other 10

        Assert.Equal(8m, o.TransferredDays);
        Assert.Equal(0m, o.PaidOutDays);
        Assert.Equal(7m, o.LostDays);
    }

    [Fact]
    public void Limit_3_MovesOnly3FifthWeekDays_RestOfFifthWeekPaid()
    {
        var o = Split(3, 0, 25, 10); // 3 of the 5 5th-week days moved, 2 paid out, 10 other lost

        Assert.Equal(3m, o.TransferredDays);
        Assert.Equal(2m, o.PaidOutFifthWeekDays);
        Assert.Equal(10m, o.LostDays);
    }

    [Fact]
    public void Limit_10_WithOnly2FifthWeekDaysUnused_Moves2Plus5()
    {
        // Own 22 (20 + a 2-day 5th week), 15 booked: the booked days use the first 20, leaving 5 first-20 days and the
        // 2 5th-week days. Limit 10: 2 + (10 - 5) = 7, everything unused.
        var o = Split(10, 0, 22, 15);

        Assert.Equal(7m, o.TransferredDays);
        Assert.Equal(0m, o.PaidOutDays);
        Assert.Equal(0m, o.LostDays);
    }

    [Fact]
    public void Limit_10_With2FifthWeekDaysAnd8OtherUnused_Moves2Plus5_Loses3()
    {
        // Own 22, 12 booked: 8 first-20 + 2 5th-week unused. Limit 10 -> 2 + 5 = 7 moved, 3 lost.
        var o = Split(10, 0, 22, 12);

        Assert.Equal(7m, o.TransferredDays);
        Assert.Equal(3m, o.LostDays);
    }

    [Fact]
    public void Limit_FractionAbove5_5point5_MovesHalfDayOfTheOthers()
    {
        var o = Split(5.5m, 0, 25, 10);

        Assert.Equal(5.5m, o.TransferredDays);
        Assert.Equal(9.5m, o.LostDays);
    }

    [Fact]
    public void Limit_Below5_PartialYearWithNo5thWeek_MovesNothing()
    {
        // Own 12.5 (no 5th week), 1 booked, limit 4: nothing to move under the 5th-week rule, 11.5 lost.
        var o = Split(4, 0, 12.5m, 1);

        Assert.Equal(0m, o.TransferredDays);
        Assert.Equal(11.5m, o.LostDays);
    }

    [Fact]
    public void Limit_8_PartialYearNoFifthWeek_Moves3()
    {
        var o = Split(8, 0, 12.5m, 1);

        Assert.Equal(3m, o.TransferredDays);
        Assert.Equal(8.5m, o.LostDays);
    }

    [Fact]
    public void Limit_DoesNotMoveTransferredInDays_EvenWithLargeLimit()
    {
        // 4 transferred in, own 25, 2 booked: 2 transferred-in used, 2 lost; own all unused: limit 100 -> 25 moved.
        var o = Split(100, 4, 25, 2);

        Assert.Equal(25m, o.TransferredDays);
        Assert.Equal(2m, o.LostTransferredInDays);
        Assert.Equal(0m, o.LostOtherDays);
    }

    [Fact]
    public void Limit_Above5_FollowedByPayout_PaysOnlyTheRest()
    {
        // Own 25, nothing booked, limit 8: 5 + 3 moved; 17 other left, Some 4 -> 4 paid, 13 lost.
        var o = Split(8, 0, 25, 0, mode: PayoutMode.Some, payoutDays: 4);

        Assert.Equal(8m, o.TransferredDays);
        Assert.Equal(4m, o.PaidOutDays);
        Assert.Equal(13m, o.LostDays);
    }
}
