using VacationCalculator.Core;
using static VacationCalculator.Core.Tests.TestData;

namespace VacationCalculator.Core.Tests;

/// <summary>
/// Feriefri (feature 3): allowance per holiday year with inheritance, oldest-first allocation, the 31 December payout rule,
/// and booking within the allowance. No public holidays unless given. Calendar used:
/// Mon 2026-01-05, Mon 2026-02-02, Tue 2026-09-01, Mon 2026-10-05, Sat 2026-10-24, Mon 2026-11-02, Mon 2026-12-28,
/// Thu 2026-12-31, Mon 2027-01-04, Mon 2027-02-01.
/// </summary>
public class FeriefriTests
{
    private static readonly HolidayYear Hy2024 = new(2024);
    private static readonly HolidayYear Hy2025 = new(2025);
    private static readonly HolidayYear Hy2026 = new(2026);
    private static readonly HolidayYear Hy2027 = new(2027);
    private static readonly DateOnly Oct24 = D("2026-10-24"); // Sat

    private static BookedDay FF(string date, decimal amount = 1) => Day(date, amount, DayType.Feriefri);

    /// <summary>Full Feriefri days on every weekday of the inclusive range.</summary>
    private static List<BookedDay> FFRange(string start, string end) =>
        Bookings.BookRange([], D(start), D(end), DayType.Feriefri, "", new HashSet<DateOnly>());

    private static VacationData Ff(params IEnumerable<BookedDay>[] days) => Data(days.SelectMany(d => d));

    // ---------- allowance ----------

    [Fact]
    public void Allowance_Default_Is5_AndReadingAddsNothing()
    {
        var data = new VacationData();

        var a = data.GetFeriefriAllowance(Hy2025);

        Assert.Equal(5m, a.Days);
        Assert.Null(a.From);
        Assert.True(a.IsDefault);
        Assert.False(a.IsOwn);
        Assert.False(a.IsInherited);
        Assert.Empty(data.HolidayYears);
    }

    [Fact]
    public void Allowance_Own()
    {
        var data = new VacationData();
        data.SetFeriefriAllowance(Hy2025, 8m);

        var a = data.GetFeriefriAllowance(Hy2025);

        Assert.Equal(8m, a.Days);
        Assert.True(a.IsOwn);
        Assert.False(a.IsDefault);
        Assert.Equal(5m, data.GetFeriefriAllowance(Hy2024).Days); // earlier years unaffected
    }

    [Fact]
    public void Allowance_InheritedFromNearestEarlierYear_OtherSettingsDontInterfere()
    {
        var data = new VacationData();
        data.SetFeriefriAllowance(Hy2024, 3m);
        data.SetFeriefriAllowance(Hy2025, 7m);
        data.SetTransferLimit(Hy2026, 8m); // an entry for 2026 without its own allowance

        var a = data.GetFeriefriAllowance(Hy2027);

        Assert.Equal(7m, a.Days);
        Assert.Equal(Hy2025, a.From);
        Assert.True(a.IsInherited);
        Assert.Equal(7m, data.GetFeriefriAllowance(Hy2026).Days);
        Assert.Equal(3, data.HolidayYears.Count);
    }

    [Fact]
    public void Allowance_Cleared_InheritsAgain()
    {
        var data = new VacationData();
        data.SetFeriefriAllowance(Hy2024, 3m);
        data.SetFeriefriAllowance(Hy2025, 7m);

        data.ClearFeriefriAllowance(Hy2025);
        data.ClearFeriefriAllowance(Hy2027); // nothing stored: does nothing

        var a = data.GetFeriefriAllowance(Hy2025);
        Assert.Equal(3m, a.Days);
        Assert.Equal(Hy2024, a.From);
        Assert.Null(data.FindHolidayYear(2025)!.FeriefriAllowanceDays);
        Assert.Null(data.FindHolidayYear(2027));
    }

    [Theory]
    [InlineData("25", "20")]
    [InlineData("-1", "0")]
    [InlineData("0", "0")]
    [InlineData("2.5", "2.5")]
    public void Allowance_IsStoredWithin0To20(string set, string stored)
    {
        var data = new VacationData();
        data.SetFeriefriAllowance(Hy2025, M(set));

        Assert.Equal(M(stored), data.FindHolidayYear(2025)!.FeriefriAllowanceDays);
        Assert.Equal(M(stored), data.GetFeriefriAllowance(Hy2025).Days);
    }

    [Fact]
    public void Allowance_HandEditedOutOfRange_ReadsClamped()
    {
        var data = new VacationData { HolidayYears = [new() { StartYear = 2025, FeriefriAllowanceDays = 30 }] };

        Assert.Equal(20m, data.GetFeriefriAllowance(Hy2025).Days);
    }

    // ---------- balances ----------

    [Fact]
    public void FullYear_With5Days_IsUsedUp_NothingPaidOrLost()
    {
        // Mon 2026-01-05 .. Fri 2026-01-09: 5 days in 2025/26
        var data = Ff(FFRange("2026-01-05", "2026-01-09"));
        var today = D("2026-03-02"); // Mon

        var b = FeriefriCalculator.CalculateHolidayYear(data, Hy2025, today);
        var yearEnd = FeriefriCalculator.CalculateYearEnd(data, 2026, today);

        Assert.Equal(5m, b.AllowanceDays);
        Assert.Equal(5m, b.UsedDays);
        Assert.Equal(0m, b.BookedAheadDays);
        Assert.Equal(0m, b.RemainingDays);
        Assert.Equal(0m, b.UnbookedDays);
        Assert.Equal(0m, b.ExceedsAllowanceBy);
        Assert.Equal(0m, yearEnd.PreviousLeftoverDays);
        Assert.Equal(5m, yearEnd.CurrentRemainingDays); // 2026/27 untouched
        Assert.Equal(0m, yearEnd.PaidOutDays);
        Assert.Equal(0m, yearEnd.LostDays);
        Assert.Equal(5m, yearEnd.CarriedDays);
    }

    [Fact]
    public void HalfDays_CountAsHalf_UsedAndBookedAheadSplitAtToday()
    {
        // Mon 2026-01-05 .. Thu 2026-01-08 full, Fri 2026-01-09 half
        var data = Ff(FFRange("2026-01-05", "2026-01-08"), [FF("2026-01-09", 0.5m)]);

        var b = FeriefriCalculator.CalculateHolidayYear(data, Hy2025, D("2026-01-07")); // Wed

        Assert.Equal(3m, b.UsedDays);        // Mon, Tue, Wed
        Assert.Equal(1.5m, b.BookedAheadDays); // Thu + half Fri
        Assert.Equal(0.5m, b.RemainingDays);
    }

    [Fact]
    public void BookedOnWeekendOrHoliday_DoesNotCount()
    {
        // Sat 2026-01-10 is a weekend, Mon 2026-01-12 a public holiday
        var data = Ff([FF("2026-01-10"), FF("2026-01-12"), FF("2026-01-13")]).WithHolidays("2026-01-12");

        var b = FeriefriCalculator.CalculateHolidayYear(data, Hy2025, Oct24);

        Assert.Equal(1m, b.UsedDays); // Tue 2026-01-13 only
    }

    [Fact]
    public void TodayBeforeTheYear_EverythingIsBookedAhead_AndTheYearEndIsProjected()
    {
        var data = Ff(FFRange("2026-01-05", "2026-01-07")); // Mon..Wed
        var today = D("2025-06-02"); // Mon, before 2025/26 starts

        var b = FeriefriCalculator.CalculateHolidayYear(data, Hy2025, today);

        Assert.Equal(0m, b.UsedDays);
        Assert.Equal(3m, b.BookedAheadDays);
        Assert.Equal(2m, b.RemainingDays);
        Assert.False(b.IsOpenOn(today));
        Assert.True(FeriefriCalculator.CalculateYearEnd(data, 2026, today).IsProjected);
    }

    [Fact]
    public void LeapDay_CountsAsAWorkingDayOf2027_28()
    {
        // Mon 2028-02-28, Tue 2028-02-29
        var data = Ff([FF("2028-02-28"), FF("2028-02-29")]);

        var b = FeriefriCalculator.CalculateHolidayYear(data, Hy2027, D("2028-03-01"));

        Assert.Equal(2m, b.UsedDays);
        Assert.Equal(3m, b.RemainingDays);
    }

    // ---------- oldest first ----------

    [Fact]
    public void SeptemberToDecember_UsesThePreviousYearsLeftoverFirst()
    {
        // 2025/26: Mon..Wed 2026-01-05..07 -> 2 left. Then Mon..Fri 2026-10-05..09.
        var data = Ff(FFRange("2026-01-05", "2026-01-07"), FFRange("2026-10-05", "2026-10-09"));

        var balances = FeriefriCalculator.CalculateHolidayYears(data, [Hy2025, Hy2026], Oct24);

        Assert.Equal(5m, balances[0].UsedDays);
        Assert.Equal(0m, balances[0].RemainingDays);
        Assert.Equal(3m, balances[1].UsedDays); // Wed 10-07 .. Fri 10-09
        Assert.Equal(2m, balances[1].RemainingDays);
        var october = FeriefriCalculator.Allocate(data).Where(a => a.Date.Month == 10).ToList();
        Assert.Equal(
            [
                new AllocatedDay(D("2026-10-05"), Hy2025, 1), new AllocatedDay(D("2026-10-06"), Hy2025, 1),
                new AllocatedDay(D("2026-10-07"), Hy2026, 1), new AllocatedDay(D("2026-10-08"), Hy2026, 1),
                new AllocatedDay(D("2026-10-09"), Hy2026, 1),
            ],
            october);
    }

    [Fact]
    public void SeptemberToDecember_AFullDaySplitsWhenThePreviousYearHasHalfADayLeft()
    {
        // 2025/26: Mon..Wed 2026-01-05..07 full + half Thu 01-08 -> 1.5 left. Then Mon 10-05 and Tue 10-06.
        var data = Ff(FFRange("2026-01-05", "2026-01-07"), [FF("2026-01-08", 0.5m), FF("2026-10-05"), FF("2026-10-06")]);

        var autumn = FeriefriCalculator.Allocate(data).Where(a => a.Date.Month == 10).ToList();

        Assert.Equal(
            [
                new AllocatedDay(D("2026-10-05"), Hy2025, 1),
                new AllocatedDay(D("2026-10-06"), Hy2025, 0.5m),
                new AllocatedDay(D("2026-10-06"), Hy2026, 0.5m),
            ],
            autumn);
    }

    [Fact]
    public void JanuaryToAugust_OnlyTheCurrentYearIsOpen()
    {
        // 2025/26 has 5 left, but Mon 2027-02-01 belongs to 2026/27 and 2025/26 closed on 31 Dec 2026
        var data = Ff([FF("2027-02-01")]);

        Assert.Equal([new AllocatedDay(D("2027-02-01"), Hy2026, 1)], FeriefriCalculator.Allocate(data));
    }

    // ---------- 31 December ----------

    [Fact]
    public void YearEnd_OpenAtMost5_NothingPaidOut_PreviousLeftoverLost()
    {
        // 2025/26: Mon..Wed 2026-01-05..07 -> leftover 2. 2026/27: allowance 3, nothing booked -> 3 remaining.
        // (Autumn bookings would use the leftover first, so a lower allowance is what keeps the open days at 5.)
        var data = Ff(FFRange("2026-01-05", "2026-01-07"));
        data.SetFeriefriAllowance(Hy2026, 3m);

        var y = FeriefriCalculator.CalculateYearEnd(data, 2026, Oct24);

        Assert.Equal(Hy2025, y.Previous);
        Assert.Equal(Hy2026, y.Current);
        Assert.Equal(D("2026-12-31"), y.Date);
        Assert.Equal(2m, y.PreviousLeftoverDays);
        Assert.Equal(3m, y.CurrentRemainingDays);
        Assert.Equal(5m, y.OpenDays);
        Assert.Equal(0m, y.PaidOutDays);
        Assert.Equal(2m, y.LostDays);
        Assert.Equal(3m, y.CarriedDays);
        Assert.Equal(2m, y.ExpiringDays);
        Assert.True(y.IsProjected);
        var b = FeriefriCalculator.CalculateHolidayYear(data, Hy2025, Oct24);
        Assert.Equal(0m, b.PaidOutAtDeadline);
        Assert.Equal(2m, b.LostAtDeadline);
    }

    [Fact]
    public void YearEnd_OpenAbove5_ExcessPaidOutFromThePreviousYearFirst()
    {
        // 2025/26 leftover 2 (Mon..Wed 2026-01-05..07), 2026/27 untouched: open 7 -> 2 paid out, all from 2025/26
        var data = Ff(FFRange("2026-01-05", "2026-01-07"));

        var y = FeriefriCalculator.CalculateYearEnd(data, 2026, Oct24);

        Assert.Equal(7m, y.OpenDays);
        Assert.Equal(2m, y.PaidOutFromPreviousDays);
        Assert.Equal(0m, y.PaidOutFromCurrentDays);
        Assert.Equal(0m, y.LostDays);
        Assert.Equal(5m, y.CarriedDays);
    }

    [Fact]
    public void YearEnd_PreviousLeftoverPartlyPaidPartlyLost()
    {
        // 2025/26 leftover 4 (Mon 2026-01-05), 2026/27 allowance 3 unbooked: open 7 -> 2 paid, 2 lost
        var data = Ff([FF("2026-01-05")]);
        data.SetFeriefriAllowance(Hy2026, 3m);

        var y = FeriefriCalculator.CalculateYearEnd(data, 2026, Oct24);

        Assert.Equal(2m, y.PaidOutFromPreviousDays);
        Assert.Equal(0m, y.PaidOutFromCurrentDays);
        Assert.Equal(2m, y.LostDays);
        Assert.Equal(3m, y.CarriedDays);
    }

    [Fact]
    public void YearEnd_AnAutumnBookingUsesThePreviousLeftover_AndReducesThePayout()
    {
        // 2025/26 leftover 2 (Mon..Wed 2026-01-05..07); Mon 2026-11-02 uses one of them: open 1 + 5 = 6 -> 1 paid
        var data = Ff(FFRange("2026-01-05", "2026-01-07"), [FF("2026-11-02")]);

        var y = FeriefriCalculator.CalculateYearEnd(data, 2026, Oct24);

        Assert.Equal(1m, y.PreviousLeftoverDays);
        Assert.Equal(5m, y.CurrentRemainingDays);
        Assert.Equal(1m, y.PaidOutFromPreviousDays);
        Assert.Equal(0m, y.LostDays);
    }

    [Fact]
    public void YearEnd_ExcessBeyondThePreviousLeftover_IsPaidFromTheCurrentYear()
    {
        // 2025/26 leftover 2, 2026/27 allowance 8 untouched: open 10 -> 5 paid: 2 from 2025/26, 3 from 2026/27
        var data = Ff(FFRange("2026-01-05", "2026-01-07"));
        data.SetFeriefriAllowance(Hy2026, 8m);

        var y = FeriefriCalculator.CalculateYearEnd(data, 2026, Oct24);
        var b = FeriefriCalculator.CalculateHolidayYear(data, Hy2026, Oct24);

        Assert.Equal(2m, y.PaidOutFromPreviousDays);
        Assert.Equal(3m, y.PaidOutFromCurrentDays);
        Assert.Equal(5m, y.PaidOutDays);
        Assert.Equal(0m, y.LostDays);
        Assert.Equal(5m, y.CarriedDays);
        Assert.Equal(3m, b.PaidOutAtFirstYearEnd);
        Assert.Equal(5m, b.CarriedDays);
        Assert.False(b.FirstYearEndPassed);
        Assert.Equal(8m, b.RemainingDays); // projected payout doesn't reduce it: the days can still be booked in 2026
        Assert.Equal(5m, b.UnbookedDays);
    }

    [Fact]
    public void YearEnd_OnlyBookingsUpTo31DecemberCount()
    {
        // 2026/27 allowance 8: 3 days Mon..Wed 2026-12-28..30 before New Year, 2 days Mon, Tue 2027-02-01..02 after.
        // 2025/26 fully used (Mon..Fri 2026-01-05..09). Open at 31 Dec 2026 = 0 + 5 -> nothing paid.
        var data = Ff(FFRange("2026-01-05", "2026-01-09"), FFRange("2026-12-28", "2026-12-30"), FFRange("2027-02-01", "2027-02-02"));
        data.SetFeriefriAllowance(Hy2026, 8m);

        var y = FeriefriCalculator.CalculateYearEnd(data, 2026, Oct24);

        Assert.Equal(5m, y.CurrentRemainingDays);
        Assert.Equal(0m, y.PaidOutDays);
        Assert.Equal(5m, y.CarriedDays);
    }

    [Fact]
    public void AfterYearEnd_FiguresAreActual_AndPaidOutDaysAreGone()
    {
        // As above (2025/26 leftover 2, 2026/27 allowance 8 untouched), today Mon 2027-01-04
        var data = Ff(FFRange("2026-01-05", "2026-01-07"));
        data.SetFeriefriAllowance(Hy2026, 8m);
        var today = D("2027-01-04");

        var y = FeriefriCalculator.CalculateYearEnd(data, 2026, today);
        var balances = FeriefriCalculator.CalculateHolidayYears(data, [Hy2025, Hy2026], today);

        Assert.False(y.IsProjected);
        Assert.True(y.HasPassed);
        Assert.Equal(5m, y.PaidOutDays);
        Assert.True(balances[0].DeadlinePassed);
        Assert.Equal(2m, balances[0].PaidOutAtDeadline);
        Assert.Equal(0m, balances[0].RemainingDays);
        Assert.True(balances[1].FirstYearEndPassed);
        Assert.Equal(5m, balances[1].RemainingDays); // 8 - 3 paid out
    }

    [Fact]
    public void AfterYearEnd_BookingCannotUsePaidOutDays()
    {
        // 2026/27 allowance 8, nothing booked in 2026: 3 paid out, 5 left from January.
        // Mon 2027-02-01 .. Fri 2027-02-12: 10 working days requested.
        var data = new VacationData();
        data.SetFeriefriAllowance(Hy2026, 8m);
        data.Days = FFRange("2026-01-05", "2026-01-09"); // 2025/26 used up

        var r = Bookings.BookFeriefri(data, D("2027-02-01"), D("2027-02-12"), 1, "");

        Assert.Equal(10m, r.RequestedDays);
        Assert.Equal(5m, r.BookedDays); // Mon..Fri 02-01..05
        Assert.Equal(5m, r.LeftOutDays);
        Assert.Equal(Hy2026, r.Limit!.HolidayYear);
        Assert.Equal(8m, r.Limit.AllowanceDays);
        Assert.Equal(3m, r.Limit.PaidOutAtFirstYearEnd);
    }

    [Fact]
    public void Deadline_OfTheCurrentYearAYearLater()
    {
        // 2026/27 allowance 8 (2027/28 inherits 8), carried 5 after 31 Dec 2026; 2 used Mon, Tue 2027-02-01..02 -> leftover 3.
        // 31 Dec 2027: open 3 + 8 = 11 -> 6 paid: 3 from 2026/27, 3 from 2027/28.
        var data = Ff(FFRange("2026-01-05", "2026-01-09"), FFRange("2027-02-01", "2027-02-02"));
        data.SetFeriefriAllowance(Hy2026, 8m);

        var y = FeriefriCalculator.CalculateYearEnd(data, 2027, Oct24);
        var b = FeriefriCalculator.CalculateHolidayYear(data, Hy2026, Oct24);

        Assert.Equal(3m, y.PreviousLeftoverDays);
        Assert.Equal(8m, y.CurrentRemainingDays);
        Assert.Equal(3m, y.PaidOutFromPreviousDays);
        Assert.Equal(3m, y.PaidOutFromCurrentDays);
        Assert.Equal(3m, b.PaidOutAtFirstYearEnd);
        Assert.Equal(3m, b.PaidOutAtDeadline);
        Assert.Equal(6m, b.PaidOutDays);
        Assert.Equal(0m, b.LostAtDeadline);
    }

    [Fact]
    public void FirstTrackedYear_DoesNotApplyToFeriefri()
    {
        // First tracked 2026/27 with an opening balance: 2025/26 still has its allowance and its leftover still counts.
        var data = Data(FFRange("2026-01-05", "2026-01-07"), firstTracked: 2026, openingBalance: 4);

        var y = FeriefriCalculator.CalculateYearEnd(data, 2026, Oct24);
        var b = FeriefriCalculator.CalculateHolidayYear(data, Hy2026, Oct24);

        Assert.Equal(2m, y.PreviousLeftoverDays);
        Assert.Equal(2m, y.PaidOutFromPreviousDays);
        Assert.Equal(5m, b.AllowanceDays); // no opening balance
    }

    [Fact]
    public void LoweredAllowance_ShowsTheExcess()
    {
        var data = Ff(FFRange("2026-01-05", "2026-01-09")); // 5 days in 2025/26
        data.SetFeriefriAllowance(Hy2025, 3m);

        var b = FeriefriCalculator.CalculateHolidayYear(data, Hy2025, Oct24);

        Assert.Equal(-2m, b.RemainingDays);
        Assert.Equal(2m, b.ExceedsAllowanceBy);
        Assert.Equal(0m, FeriefriCalculator.CalculateYearEnd(data, 2026, Oct24).PreviousLeftoverDays);
    }

    // ---------- booking ----------

    [Fact]
    public void Book_SingleDayThatFits_IsAFullDay()
    {
        var data = Ff(FFRange("2026-01-05", "2026-01-08")); // 4 used

        var r = Bookings.BookFeriefri(data, D("2026-01-12"), D("2026-01-12"), 1, "Dentist"); // Mon

        Assert.True(r.IsComplete);
        Assert.Null(r.Limit);
        Assert.Equal(1m, r.BookedDays);
        Assert.Contains(new BookedDay { Date = D("2026-01-12"), Type = DayType.Feriefri, Amount = 1, Description = "Dentist" }, r.Days);
        Assert.Equal(4, data.Days.Count); // input not modified
    }

    [Fact]
    public void Book_BeyondTheAllowance_FullThenHalfThenRefused()
    {
        // 4.5 used: Mon..Thu 2026-01-05..08 + half Fri 01-09
        var data = Ff(FFRange("2026-01-05", "2026-01-08"), [FF("2026-01-09", 0.5m)]);

        var half = Bookings.BookFeriefri(data, D("2026-01-12"), D("2026-01-12"), 1, ""); // Mon
        Assert.Equal(0.5m, half.BookedDays);
        Assert.Equal(0.5m, half.LeftOutDays);
        Assert.Equal([D("2026-01-12")], half.HalfDayDates);
        Assert.Empty(half.LeftOutDates);
        Assert.False(half.IsRefused);
        Assert.Equal(Hy2025, half.Limit!.HolidayYear);
        Assert.Equal(5m, half.Limit.AllowanceDays);
        Assert.Equal(0.5m, half.Days.Single(d => d.Date == D("2026-01-12")).Amount);

        data.Days = half.Days;
        var refused = Bookings.BookFeriefri(data, D("2026-01-13"), D("2026-01-13"), 1, ""); // Tue
        Assert.True(refused.IsRefused);
        Assert.Equal(0m, refused.BookedDays);
        Assert.Equal([D("2026-01-13")], refused.LeftOutDates);
        Assert.Equal(Hy2025, refused.Limit!.HolidayYear);
        Assert.Equal(data.Days, refused.Days);
    }

    [Fact]
    public void Book_HalfDayRequested_WhenNothingIsLeft_IsRefused()
    {
        var data = Ff(FFRange("2026-01-05", "2026-01-09"));

        var r = Bookings.BookFeriefri(data, D("2026-01-12"), D("2026-01-12"), 0.5m, "");

        Assert.True(r.IsRefused);
        Assert.Equal(0.5m, r.RequestedDays);
    }

    [Fact]
    public void Book_ZeroAllowance_IsRefused()
    {
        var data = new VacationData();
        data.SetFeriefriAllowance(Hy2025, 0);

        var r = Bookings.BookFeriefri(data, D("2026-01-05"), D("2026-01-05"), 1, "");

        Assert.True(r.IsRefused);
        Assert.Equal(0m, r.Limit!.AllowanceDays);
        Assert.Empty(r.Days);
    }

    [Fact]
    public void Book_RangeRunsOutMidway_BooksInDateOrder_AndCountsTheRest()
    {
        // 2 used (Mon, Tue 2026-01-05..06). Mon 2026-02-02 .. Fri 2026-02-13: 10 working days.
        var data = Ff(FFRange("2026-01-05", "2026-01-06"));

        var r = Bookings.BookFeriefri(data, D("2026-02-13"), D("2026-02-02"), 1, "Winter"); // either order

        Assert.Equal(10m, r.RequestedDays);
        Assert.Equal(3m, r.BookedDays); // Mon..Wed 02-02..04
        Assert.Equal(7m, r.LeftOutDays);
        Assert.Equal(
            [D("2026-02-05"), D("2026-02-06"), D("2026-02-09"), D("2026-02-10"), D("2026-02-11"), D("2026-02-12"), D("2026-02-13")],
            r.LeftOutDates);
        Assert.Equal(Hy2025, r.Limit!.HolidayYear);
        Assert.Equal(5, r.Days.Count);
    }

    [Fact]
    public void Book_RangeRunsOutOnAHalfDay()
    {
        // 2.5 used (Mon, Tue 2026-01-05..06 full, Wed 01-07 half). Mon 02-02 .. Fri 02-06.
        var data = Ff([FF("2026-01-05"), FF("2026-01-06"), FF("2026-01-07", 0.5m)]);

        var r = Bookings.BookFeriefri(data, D("2026-02-02"), D("2026-02-06"), 1, "");

        Assert.Equal(2.5m, r.BookedDays); // Mon, Tue full, Wed half
        Assert.Equal(2.5m, r.LeftOutDays);
        Assert.Equal([D("2026-02-04")], r.HalfDayDates);
        Assert.Equal([D("2026-02-05"), D("2026-02-06")], r.LeftOutDates);
    }

    [Fact]
    public void Book_WeekendsAndHolidaysAtTheEnds_AreSkippedAndNotRequested()
    {
        // Sat 2026-01-31 .. Sun 2026-02-08, Mon 02-02 a public holiday: Tue..Fri 02-03..06 are requested
        var data = new VacationData().WithHolidays("2026-02-02");

        var r = Bookings.BookFeriefri(data, D("2026-01-31"), D("2026-02-08"), 1, "");

        Assert.Equal(4m, r.RequestedDays);
        Assert.Equal(4m, r.BookedDays);
        Assert.Equal([D("2026-02-03"), D("2026-02-04"), D("2026-02-05"), D("2026-02-06")], r.Days.Select(d => d.Date));
    }

    [Fact]
    public void Book_SingleWeekendDay_BooksNothing_AndIsNotRefused()
    {
        var r = Bookings.BookFeriefri(new VacationData(), D("2026-01-10"), D("2026-01-10"), 1, ""); // Sat

        Assert.Equal(0m, r.RequestedDays);
        Assert.False(r.IsRefused);
        Assert.Empty(r.Days);
    }

    [Fact]
    public void Book_RangeOverNewYear_UsesTheCurrentYearOnBothSides()
    {
        // 2025/26 used up. Mon 2026-12-28 .. Fri 2027-01-08 with Fri 2027-01-01 a holiday: 9 working days.
        // 2026/27 has 5: Mon..Thu 12-28..31 and Mon 2027-01-04.
        var data = Ff(FFRange("2026-01-05", "2026-01-09")).WithHolidays("2027-01-01");

        var r = Bookings.BookFeriefri(data, D("2026-12-28"), D("2027-01-08"), 1, "");

        Assert.Equal(9m, r.RequestedDays);
        Assert.Equal(5m, r.BookedDays);
        Assert.Equal([D("2027-01-05"), D("2027-01-06"), D("2027-01-07"), D("2027-01-08")], r.LeftOutDates);
        Assert.Equal(Hy2026, r.Limit!.HolidayYear);
    }

    [Fact]
    public void Book_SeptemberToDecember_UsesThePreviousLeftover_ThenTheCurrentYear()
    {
        // 2025/26: 3 used -> 2 left; 2026/27: 5. Mon 2026-10-05 .. Fri 2026-10-16: 10 working days -> 7 fit.
        var data = Ff(FFRange("2026-01-05", "2026-01-07"));

        var r = Bookings.BookFeriefri(data, D("2026-10-05"), D("2026-10-16"), 1, "");
        data.Days = r.Days;

        Assert.Equal(7m, r.BookedDays); // Mon 10-05 .. Tue 10-13
        Assert.Equal([D("2026-10-14"), D("2026-10-15"), D("2026-10-16")], r.LeftOutDates);
        Assert.Equal(Hy2026, r.Limit!.HolidayYear);
        var balances = FeriefriCalculator.CalculateHolidayYears(data, [Hy2025, Hy2026], Oct24);
        Assert.Equal(0m, balances[0].RemainingDays);
        Assert.Equal(0m, balances[1].RemainingDays);
    }

    [Fact]
    public void Book_ConsidersLaterBookingsThatWouldShiftToAFullYear()
    {
        // 2025/26: 4 used in January + Mon 2026-11-02 (its last day). 2026/27: Mon..Fri 2026-12-07..11 (all 5).
        // Booking Mon 2026-10-05 would take 2025/26's last day and push 11-02 into the full 2026/27: refused.
        var data = Ff(FFRange("2026-01-05", "2026-01-08"), [FF("2026-11-02")], FFRange("2026-12-07", "2026-12-11"));

        var r = Bookings.BookFeriefri(data, D("2026-10-05"), D("2026-10-05"), 1, "");

        Assert.True(r.IsRefused);
        Assert.Equal(Hy2026, r.Limit!.HolidayYear);
    }

    [Fact]
    public void Book_ExistingExcessInOneYear_DoesNotBlockAnotherYear()
    {
        // 2025/26 lowered to 3 with 5 booked (2 over). Mon 2026-09-07 goes to 2026/27, Mon 2026-02-02 would go to 2025/26.
        var data = Ff(FFRange("2026-01-05", "2026-01-09"));
        data.SetFeriefriAllowance(Hy2025, 3m);
        data.SetFeriefriAllowance(Hy2026, 5m);

        Assert.Equal(1m, Bookings.BookFeriefri(data, D("2026-09-07"), D("2026-09-07"), 1, "").BookedDays);
        Assert.True(Bookings.BookFeriefri(data, D("2026-02-02"), D("2026-02-02"), 1, "").IsRefused);
    }

    [Fact]
    public void Book_RebookingAFeriefriDay_FreesItFirst()
    {
        // All 5 used Mon..Fri 2026-01-05..09; rebooking Wed 01-07 with a new description fits
        var data = Ff(FFRange("2026-01-05", "2026-01-09"));

        var r = Bookings.BookFeriefri(data, D("2026-01-07"), D("2026-01-07"), 1, "Moved");

        Assert.True(r.IsComplete);
        Assert.Equal("Moved", r.Days.Single(d => d.Date == D("2026-01-07")).Description);
        Assert.Equal(5, r.Days.Count);
    }

    [Fact]
    public void Book_FeriefriOverAVacationDay_ReplacesIt()
    {
        // Vacation Mon..Fri 2026-01-05..09; Feriefri on Wed 01-07
        var data = Data(Range("2026-01-05", "2026-01-09"));

        var r = Bookings.BookFeriefri(data, D("2026-01-07"), D("2026-01-07"), 1, "");
        data.Days = r.Days;

        Assert.Equal(DayType.Feriefri, r.Days.Single(d => d.Date == D("2026-01-07")).Type);
        Assert.Equal(4m, BalanceCalculator.CalculateHolidayYear(data, Hy2025, Oct24).UsedDays);
        Assert.Equal(1m, FeriefriCalculator.CalculateHolidayYear(data, Hy2025, Oct24).UsedDays);
    }

    [Fact]
    public void Book_RefusedFeriefriOverAVacationDay_KeepsTheVacationDay()
    {
        // Feriefri used up Mon..Fri 2026-01-05..09; vacation on Mon 2026-02-02
        var data = Ff(FFRange("2026-01-05", "2026-01-09"), [Day("2026-02-02")]);

        var r = Bookings.BookFeriefri(data, D("2026-02-02"), D("2026-02-02"), 1, "");

        Assert.True(r.IsRefused);
        Assert.Equal(DayType.Vacation, r.Days.Single(d => d.Date == D("2026-02-02")).Type);
    }

    [Fact]
    public void Book_VacationOverAFeriefriDay_ReplacesIt_AndFreesTheAllowance()
    {
        var data = Ff(FFRange("2026-01-05", "2026-01-09"));

        data.Days = Bookings.BookRange(data.Days, D("2026-01-07"), D("2026-01-07"), DayType.Vacation, "", new HashSet<DateOnly>());

        Assert.Equal(4m, FeriefriCalculator.CalculateHolidayYear(data, Hy2025, Oct24).UsedDays);
        Assert.Equal(1m, BalanceCalculator.CalculateHolidayYear(data, Hy2025, Oct24).UsedDays);
        Assert.True(Bookings.BookFeriefri(data, D("2026-02-02"), D("2026-02-02"), 1, "").IsComplete);
    }

    [Fact]
    public void Book_InvalidAmountOrTooLongRange_Throws()
    {
        var data = new VacationData();

        Assert.Throws<ArgumentOutOfRangeException>(() => Bookings.BookFeriefri(data, D("2026-01-05"), D("2026-01-05"), 2, ""));
        // Thu 2026-01-01 .. Fri 2027-01-01 is one day too long
        Assert.Throws<ArgumentException>(() => Bookings.BookFeriefri(data, D("2026-01-01"), D("2027-01-01"), 1, ""));
    }

    // ---------- vacation unaffected ----------

    [Fact]
    public void FeriefriBookings_LeaveVacationFiguresUnchanged_ButTakeFreeWorkingDays()
    {
        var vacation = Range("2026-03-02", "2026-03-13"); // Mon..Fri x2: 10 days
        var withoutFf = Data(vacation);
        var withFf = Data(vacation.Concat(FFRange("2026-10-05", "2026-10-09")).Concat(FFRange("2026-01-05", "2026-01-09")));

        var a = BalanceCalculator.CalculateHolidayYear(withoutFf, Hy2025, Oct24);
        var b = BalanceCalculator.CalculateHolidayYear(withFf, Hy2025, Oct24);
        var sa = BalanceCalculator.CalculateCalendarYear(withoutFf, 2026, Oct24);
        var sb = BalanceCalculator.CalculateCalendarYear(withFf, 2026, Oct24);

        Assert.Equal(a, b);
        Assert.Equal(a, sb.HolidayYear);
        Assert.Equal(sa.WorkingDaysLeft, sb.WorkingDaysLeft); // the October Feriefri days are before today
        Assert.Equal(BalanceCalculator.Allocate(withoutFf, Oct24), BalanceCalculator.Allocate(withFf, Oct24));

        // A Feriefri day after today is not a free working day: Mon 2026-11-02
        var withFutureFf = Data(vacation.Append(FF("2026-11-02")));
        Assert.Equal(sa.WorkingDaysLeft - 1, BalanceCalculator.CalculateCalendarYear(withFutureFf, 2026, Oct24).WorkingDaysLeft);
    }
}
