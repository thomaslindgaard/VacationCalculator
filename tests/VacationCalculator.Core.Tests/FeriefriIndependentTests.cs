using System.Text.Json;
using VacationCalculator.Core;
using static VacationCalculator.Core.Tests.TestData;

namespace VacationCalculator.Core.Tests;

/// <summary>
/// Independent tests for feature 3 (Feriefri), with expected values counted by hand from docs/features.md section 3.
/// Calendar: 2026-01-01 is a Thursday, so Mon 2026-01-05/12/19, Mon 2026-10-05/12/19, Sat 2026-10-24,
/// Mon 2026-12-28, Thu 2026-12-31, Fri 2027-01-01, Mon 2027-01-04/11. Holiday year 2025 = 1 Sep 2025 .. 31 Aug 2026
/// (deadline 31 Dec 2026), holiday year 2026 = 1 Sep 2026 .. 31 Aug 2027. No manual public holidays unless stated; the
/// automatic Danish holidays (e.g. Thu 2026-12-24, Fri 2026-12-25, Thu 2026-12-31, Fri 2027-01-01) always apply.
/// </summary>
public class FeriefriIndependentTests
{
    private static readonly HolidayYear Hy2024 = new(2024);
    private static readonly HolidayYear Hy2025 = new(2025);
    private static readonly HolidayYear Hy2026 = new(2026);
    private static readonly HolidayYear Hy2027 = new(2027);
    private static readonly DateOnly Oct24 = D("2026-10-24");
    private static readonly DateOnly Jan4_2027 = D("2027-01-04");

    private static BookedDay FF(string date, decimal amount = 1) => Day(date, amount, DayType.Feriefri);

    private static List<BookedDay> FFRange(string start, string end) =>
        Bookings.BookRange([], D(start), D(end), DayType.Feriefri, "", new HashSet<DateOnly>());

    private static VacationData WithFF(params IEnumerable<BookedDay>[] days) => Data(days.SelectMany(d => d));

    private static FeriefriBalance Bal(VacationData d, HolidayYear y, DateOnly today) =>
        FeriefriCalculator.CalculateHolidayYear(d, y, today);

    private static FeriefriYearEnd YE(VacationData d, int year, DateOnly today) =>
        FeriefriCalculator.CalculateYearEnd(d, year, today);

    // ---------- allowance ----------

    [Fact]
    public void Allowance_Clamped_On_Set_And_Read()
    {
        var data = new VacationData();
        data.SetFeriefriAllowance(Hy2025, 25m);
        Assert.Equal(20m, data.GetFeriefriAllowance(Hy2025).Days);
        data.SetFeriefriAllowance(Hy2025, -3m);
        Assert.Equal(0m, data.GetFeriefriAllowance(Hy2025).Days);
        data.SetFeriefriAllowance(Hy2025, 7.5m);
        Assert.Equal(7.5m, data.GetFeriefriAllowance(Hy2025).Days);
        data.SetFeriefriAllowance(Hy2025, 20m);
        Assert.Equal(20m, data.GetFeriefriAllowance(Hy2025).Days);

        // hand-edited file
        var edited = new VacationData { HolidayYears = [new HolidayYearSettings { StartYear = 2025, FeriefriAllowanceDays = 30m }] };
        Assert.Equal(20m, edited.GetFeriefriAllowance(Hy2025).Days);
        edited.HolidayYears[0].FeriefriAllowanceDays = -4m;
        Assert.Equal(0m, edited.GetFeriefriAllowance(Hy2025).Days);
    }

    [Fact]
    public void Allowance_Inheritance_Chain_AndClear()
    {
        var data = new VacationData();
        data.SetTransferLimit(Hy2025, 9m); // other per-year settings must not count as a Feriefri value
        data.SetFeriefriAllowance(Hy2024, 8m);

        var a26 = data.GetFeriefriAllowance(Hy2026);
        Assert.Equal(8m, a26.Days);
        Assert.True(a26.IsInherited);
        Assert.Equal(Hy2024, a26.From);
        Assert.Equal(5m, data.GetFeriefriAllowance(new HolidayYear(2023)).Days); // earlier than any value: default

        data.SetFeriefriAllowance(Hy2026, 6m);
        Assert.True(data.GetFeriefriAllowance(Hy2026).IsOwn);
        Assert.Equal(8m, data.GetFeriefriAllowance(Hy2025).Days); // earlier year unchanged
        var a27 = data.GetFeriefriAllowance(Hy2027);
        Assert.Equal(6m, a27.Days);
        Assert.Equal(Hy2026, a27.From);

        data.ClearFeriefriAllowance(Hy2026);
        Assert.Equal(8m, data.GetFeriefriAllowance(Hy2026).Days);
        Assert.True(data.GetFeriefriAllowance(Hy2026).IsInherited);

        data.ClearFeriefriAllowance(Hy2027); // nothing stored: no-op
        Assert.DoesNotContain(data.HolidayYears, h => h.StartYear == 2027);
    }

    [Fact]
    public void OldJsonWithoutFeriefriKey_LoadsAsInherited_AndRoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vc-ff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "d.json");
            File.WriteAllText(path, """{"HolidayYears":[{"StartYear":2025,"TransferLimitDays":8}],"Days":[{"Date":"2026-01-05","Type":"Vacation","Amount":1,"Description":"x"}]}""");
            var store = new DataStore(path);

            var data = store.Load();
            Assert.Null(data.HolidayYears[0].FeriefriAllowanceDays);
            Assert.Equal(5m, data.GetFeriefriAllowance(Hy2025).Days);
            Assert.Equal(8m, data.GetDeadlineSettings(Hy2025).TransferLimitDays); // old settings kept
            Assert.Single(data.Days);

            data.SetFeriefriAllowance(Hy2026, 7.5m);
            data.Days.Add(FF("2026-10-05", 0.5m));
            store.Save(data);
            var again = store.Load();
            Assert.Equal(7.5m, again.GetFeriefriAllowance(Hy2026).Days);
            Assert.Equal(DayType.Feriefri, again.Days.Single(d => d.Date == D("2026-10-05")).Type);
            Assert.Equal(0.5m, again.Days.Single(d => d.Date == D("2026-10-05")).Amount);
            Assert.Equal(8m, again.GetDeadlineSettings(Hy2025).TransferLimitDays);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ---------- oldest first ----------

    [Fact]
    public void SepToDec_OldestFirst_SixthDayGoesToNewYear()
    {
        // Mon 10-05..Fri 10-09 = 5 days, Mon 10-12 = 6th. HY2025 has 5 -> takes all 5; 6th -> HY2026.
        var data = WithFF(FFRange("2026-10-05", "2026-10-12"));
        Assert.Equal(5m, Bal(data, Hy2025, Oct24).UsedDays);
        Assert.Equal(1m, Bal(data, Hy2026, Oct24).UsedDays);
    }

    [Fact]
    public void SepToDec_PartlyUsedOldYear_ThreeLeft_Goes3Then2()
    {
        // 2 booked in Jan -> HY2025 has 3 left; Oct 5-9 (5 days): 3 to HY2025, 2 to HY2026.
        var data = WithFF([FF("2026-01-05"), FF("2026-01-06")], FFRange("2026-10-05", "2026-10-09"));
        Assert.Equal(5m, Bal(data, Hy2025, Oct24).UsedDays);
        Assert.Equal(2m, Bal(data, Hy2026, Oct24).UsedDays);
    }

    [Fact]
    public void Sep1_TakesOldYearWhileItHasDays_ElseNewYear()
    {
        // Tue 2026-09-01 and Mon 2026-08-31.
        var open = WithFF([FF("2026-09-01")]);
        Assert.Equal(1m, Bal(open, Hy2025, Oct24).UsedDays);
        Assert.Equal(0m, Bal(open, Hy2026, Oct24).UsedDays);

        var exhausted = WithFF(FFRange("2026-01-05", "2026-01-09"), [FF("2026-09-01")]);
        Assert.Equal(5m, Bal(exhausted, Hy2025, Oct24).UsedDays);
        Assert.Equal(1m, Bal(exhausted, Hy2026, Oct24).UsedDays);

        var aug = WithFF(FFRange("2026-01-05", "2026-01-09"), [FF("2026-08-31")]);
        Assert.Equal(6m, Bal(aug, Hy2025, Oct24).UsedDays); // Aug is still HY2025 (shown as over-allowance)
    }

    [Fact]
    public void AfterNewYear_OldYearIsGone_JanBookingIsNewYears()
    {
        // Mon 2027-01-04 (the first working day; Fri 01-01 is Nytårsdag) is holiday year 2026 even though HY2025 has
        // 5 unused days.
        var data = WithFF([FF("2027-01-04")]);
        Assert.Equal(0m, Bal(data, Hy2025, D("2027-02-01")).UsedDays);
        Assert.Equal(1m, Bal(data, Hy2026, D("2027-02-01")).UsedDays);
    }

    [Fact]
    public void HalfDay_SplitsAcrossYearsWhenOldYearHasHalfLeft()
    {
        // 4.5 in Jan (Mon 01-05..Thu 01-08 full + Fri 01-09 half), then a full day Mon 10-05: 0.5 + 0.5.
        var data = WithFF([FF("2026-01-05"), FF("2026-01-06"), FF("2026-01-07"), FF("2026-01-08"), FF("2026-01-09", 0.5m), FF("2026-10-05")]);
        Assert.Equal(5m, Bal(data, Hy2025, Oct24).UsedDays);
        Assert.Equal(0.5m, Bal(data, Hy2026, Oct24).UsedDays);
    }

    [Fact]
    public void LeapDay_2028_IsHolidayYear2027()
    {
        var data = WithFF([FF("2028-02-29")]); // a Tuesday
        Assert.Equal(1m, Bal(data, Hy2027, D("2028-03-01")).UsedDays);
        Assert.Equal(0m, Bal(data, new HolidayYear(2028), D("2028-03-01")).UsedDays);
    }

    [Fact]
    public void WeekendAndHolidayFeriefri_AreIgnored()
    {
        // Sat 2026-01-10 and a user-entered holiday Tue 2026-01-06.
        var data = WithFF([FF("2026-01-10"), FF("2026-01-06"), FF("2026-01-07")]).WithHolidays("2026-01-06");
        Assert.Equal(1m, Bal(data, Hy2025, Oct24).UsedDays);
        Assert.Empty(FeriefriCalculator.Allocate(new VacationData()));
    }

    [Fact]
    public void UsedVsBookedAhead_SplitsAtToday()
    {
        // today Sat 10-24: Fri 10-23 used, Mon 10-26 booked ahead.
        var data = WithFF([FF("2026-10-23"), FF("2026-10-26"), FF("2026-10-24")]);
        var b = Bal(data, Hy2025, Oct24);
        Assert.Equal(1m, b.UsedDays);
        Assert.Equal(1m, b.BookedAheadDays);
        Assert.Equal(3m, b.RemainingDays);
    }

    // ---------- 31 December ----------

    [Fact]
    public void YearEnd_NoBookings_PaysOutPreviousFirst()
    {
        // open 5 + 5 = 10, excess 5 taken from previous: paid 5 (prev), current keeps 5.
        var e = YE(new VacationData(), 2026, Oct24);
        Assert.Equal(5m, e.PreviousLeftoverDays);
        Assert.Equal(5m, e.CurrentRemainingDays);
        Assert.Equal(5m, e.PaidOutFromPreviousDays);
        Assert.Equal(0m, e.PaidOutFromCurrentDays);
        Assert.Equal(0m, e.LostDays);
        Assert.Equal(5m, e.CarriedDays);
        Assert.True(e.IsProjected);
        Assert.Equal(D("2026-12-31"), e.Date);
    }

    [Fact]
    public void YearEnd_PartiallyUsedPrevious()
    {
        // 3 used in Jan: leftover 2, rem 5, open 7, excess 2 -> paid 2 from previous, lost 0.
        var e = YE(WithFF(FFRange("2026-01-05", "2026-01-07")), 2026, Oct24);
        Assert.Equal(2m, e.PreviousLeftoverDays);
        Assert.Equal(2m, e.PaidOutFromPreviousDays);
        Assert.Equal(0m, e.LostDays);
        Assert.Equal(5m, e.CarriedDays);
    }

    [Fact]
    public void YearEnd_OpenExactlyFive_NothingPaid()
    {
        // prev leftover 0 (5 used in Jan), current 5 -> open 5.
        var e = YE(WithFF(FFRange("2026-01-05", "2026-01-09")), 2026, Oct24);
        Assert.Equal(0m, e.PaidOutDays);
        Assert.Equal(0m, e.LostDays);
        Assert.Equal(0m, e.ExpiringDays);
        Assert.Equal(5m, e.CarriedDays);
    }

    [Fact]
    public void YearEnd_OpenBelowFive_PreviousLeftoverIsLost()
    {
        // HY2026 allowance 2, prev leftover 5 -> open 7, excess 2: paid 2 from previous, 3 lost, current keeps 2.
        var data = new VacationData();
        data.SetFeriefriAllowance(Hy2026, 2m);
        var e = YE(data, 2026, Oct24);
        Assert.Equal(2m, e.PaidOutFromPreviousDays);
        Assert.Equal(0m, e.PaidOutFromCurrentDays);
        Assert.Equal(3m, e.LostDays);
        Assert.Equal(2m, e.CarriedDays);

        // allowance 0: open 5, nothing paid, whole leftover (5) lost.
        var zero = new VacationData();
        zero.SetFeriefriAllowance(Hy2026, 0m);
        var z = YE(zero, 2026, Oct24);
        Assert.Equal(0m, z.PaidOutDays);
        Assert.Equal(5m, z.LostDays);
        Assert.Equal(0m, z.CarriedDays);
    }

    [Fact]
    public void YearEnd_ExcessReachesIntoCurrentYear()
    {
        // 2 used in Jan: prev leftover 3, HY2026 allowance 8 -> open 11, excess 6: 3 from prev, 3 from current; lost 0; carried 5.
        var data = WithFF([FF("2026-01-05"), FF("2026-01-06")]);
        data.SetFeriefriAllowance(Hy2026, 8m);
        var e = YE(data, 2026, Oct24);
        Assert.Equal(3m, e.PaidOutFromPreviousDays);
        Assert.Equal(3m, e.PaidOutFromCurrentDays);
        Assert.Equal(6m, e.PaidOutDays);
        Assert.Equal(0m, e.LostDays);
        Assert.Equal(5m, e.CarriedDays);
    }

    [Fact]
    public void YearEnd_BookingOnLastWorkingDayCounts_BookingInJanuaryDoesNot()
    {
        // Wed 12-30 goes to HY2025 (leftover 5 -> 4): open 9, paid 4. Mon 2027-01-04 is HY2026 and not counted: paid 5.
        var on30 = YE(WithFF([FF("2026-12-30")]), 2026, Oct24);
        Assert.Equal(4m, on30.PreviousLeftoverDays);
        Assert.Equal(4m, on30.PaidOutDays);

        var jan4 = YE(WithFF([FF("2027-01-04")]), 2026, Oct24);
        Assert.Equal(5m, jan4.PreviousLeftoverDays);
        Assert.Equal(5m, jan4.CurrentRemainingDays);
        Assert.Equal(5m, jan4.PaidOutDays);

        // A stored record on Thu 12-31 (Nytårsaftensdag) is not counted at all: paid 5.
        var on31 = YE(WithFF([FF("2026-12-31")]), 2026, Oct24);
        Assert.Equal(5m, on31.PreviousLeftoverDays);
        Assert.Equal(5m, on31.PaidOutDays);
    }

    [Fact]
    public void YearEnd_HalfDaysAndDecember()
    {
        // Mon 12-28 half: prev leftover 4.5, open 9.5, excess 4.5, paid 4.5 from previous.
        var e = YE(WithFF([FF("2026-12-28", 0.5m)]), 2026, Oct24);
        Assert.Equal(4.5m, e.PreviousLeftoverDays);
        Assert.Equal(4.5m, e.PaidOutFromPreviousDays);
        Assert.Equal(5m, e.CarriedDays);
    }

    [Fact]
    public void YearEnd_DecemberBookingsBeyondPrevious_ReduceCurrentRemaining()
    {
        // 5 used in Jan (prev leftover 0). Mon 12-21..Wed 12-30 = 8 weekdays, less Thu 24 and Fri 25 (holidays) = 6,
        // go to HY2026 (allowance 6): rem 0 at 31 Dec.
        var data = WithFF(FFRange("2026-01-05", "2026-01-09"), FFRange("2026-12-21", "2026-12-30"));
        data.SetFeriefriAllowance(Hy2026, 6m);
        var e = YE(data, 2026, Oct24);
        Assert.Equal(0m, e.CurrentRemainingDays);
        Assert.Equal(0m, e.PaidOutDays);
        Assert.Equal(0m, e.CarriedDays);
    }

    [Fact]
    public void YearEnd_PassedVsProjected()
    {
        var data = new VacationData();
        Assert.True(YE(data, 2026, D("2026-12-31")).IsProjected); // on the day itself still projected
        var passed = YE(data, 2026, Jan4_2027);
        Assert.False(passed.IsProjected);
        Assert.True(passed.HasPassed);
        Assert.Equal(5m, passed.PaidOutDays); // same figures
    }

    // ---------- holiday year balance ----------

    [Fact]
    public void Balance_FirstYearEnd_PayoutWithAllowance8_AndDeadlinePayout()
    {
        // HY2025 allowance 8 (inherited by HY2026). 31 Dec 2025: prev (HY2024, 5) + 8 = 13, excess 8: 5 prev, 3 from HY2025.
        // Nothing booked: at the deadline 31 Dec 2026: HY2025 leftover 8-3 = 5, HY2026 (8) -> open 13, excess 8, 5 paid from HY2025.
        var data = new VacationData();
        data.SetFeriefriAllowance(Hy2025, 8m);

        var b = Bal(data, Hy2025, Oct24);
        Assert.Equal(8m, b.AllowanceDays);
        Assert.Equal(3m, b.PaidOutAtFirstYearEnd);
        Assert.True(b.FirstYearEndPassed);
        Assert.False(b.DeadlinePassed);
        Assert.Equal(5m, b.CarriedDays);
        Assert.Equal(5m, b.PaidOutAtDeadline);
        Assert.Equal(0m, b.LostAtDeadline);
        Assert.Equal(5m, b.RemainingDays); // 8 - 0 booked - 3 really paid out on 31 Dec 2025; the deadline payout is only projected
    }

    [Fact]
    public void Balance_Remaining_SubtractsPayoutsOnlyOncePassed()
    {
        // allowance 5, 2 used: at 2026-10-24 remaining 3. HY2026 allowance 0 -> the 3 are lost at 31 Dec 2026.
        var data = WithFF([FF("2026-01-05"), FF("2026-01-06")]);
        data.SetFeriefriAllowance(Hy2026, 0m);

        var before = Bal(data, Hy2025, Oct24);
        Assert.Equal(3m, before.RemainingDays);
        Assert.Equal(3m, before.LostAtDeadline);
        Assert.Equal(0m, before.PaidOutAtDeadline);

        var after = Bal(data, Hy2025, Jan4_2027);
        Assert.True(after.DeadlinePassed);
        Assert.Equal(0m, after.RemainingDays);
    }

    [Fact]
    public void Balance_Deadline_PaidOut_WhenNextYearHasRoom()
    {
        // 2 used, 3 leftover; HY2026 default 5 -> open 8, excess 3 -> the 3 are paid out, none lost.
        var b = Bal(WithFF([FF("2026-01-05"), FF("2026-01-06")]), Hy2025, Oct24);
        Assert.Equal(3m, b.PaidOutAtDeadline);
        Assert.Equal(0m, b.LostAtDeadline);
        Assert.Equal(2m, b.BookedDays);
    }

    [Fact]
    public void Balance_OverAllowanceStoredData_IsShown()
    {
        // 7 weekdays Mon 01-05..Tue 01-13 stored in HY2025 (allowance 5).
        var b = Bal(WithFF(FFRange("2026-01-05", "2026-01-13")), Hy2025, Oct24);
        Assert.Equal(7m, b.UsedDays);
        Assert.Equal(2m, b.ExceedsAllowanceBy);
        Assert.Equal(-2m, b.RemainingDays);
    }

    [Fact]
    public void FeriefriDoesNotAffectVacationFigures_AndTrackedYearIsIrrelevant()
    {
        var vac = Range("2026-02-02", "2026-02-06"); // 5 vacation days
        var onlyVac = Data(vac, firstTracked: 2026);
        var both = Data(vac.Concat(FFRange("2026-01-05", "2026-01-09")), firstTracked: 2026);

        var a = BalanceCalculator.CalculateHolidayYear(onlyVac, Hy2025, Oct24);
        var b = BalanceCalculator.CalculateHolidayYear(both, Hy2025, Oct24);
        Assert.Equal(a.UsedDays, b.UsedDays);
        Assert.Equal(5m, b.UsedDays);
        Assert.Equal(a.UnbookedDays, b.UnbookedDays);
        // and Feriefri still counts for HY2025 although it is before the first tracked year
        Assert.Equal(5m, Bal(both, Hy2025, Oct24).UsedDays);
    }

    // ---------- booking ----------

    [Fact]
    public void Book_FullDayFits()
    {
        var r = Bookings.BookFeriefri(new VacationData(), D("2026-01-05"), D("2026-01-05"), 1m, "d");
        Assert.True(r.IsComplete);
        Assert.False(r.IsRefused);
        Assert.Null(r.Limit);
        var day = Assert.Single(r.Days);
        Assert.Equal((DayType.Feriefri, 1m, "d"), (day.Type, day.Amount, day.Description));
    }

    [Fact]
    public void Book_FullDayBecomesHalf_WhenOnlyHalfLeft()
    {
        // 4.5 used (Mon 01-05..Thu 01-08 + Fri 01-09 half); request full Mon 01-12.
        var data = WithFF([FF("2026-01-05"), FF("2026-01-06"), FF("2026-01-07"), FF("2026-01-08"), FF("2026-01-09", 0.5m)]);
        var r = Bookings.BookFeriefri(data, D("2026-01-12"), D("2026-01-12"), 1m, "");
        Assert.Equal(0.5m, r.BookedDays);
        Assert.Equal(1m, r.RequestedDays);
        Assert.Equal(0.5m, r.LeftOutDays);
        Assert.Equal([D("2026-01-12")], r.HalfDayDates);
        Assert.Empty(r.LeftOutDates);
        Assert.False(r.IsRefused);
        Assert.False(r.IsComplete);
        Assert.Equal(0.5m, r.Days.Single(d => d.Date == D("2026-01-12")).Amount);
        Assert.Equal(Hy2025, r.Limit!.HolidayYear);
        Assert.Equal(5m, r.Limit.AllowanceDays);
    }

    [Fact]
    public void Book_HalfDayFitsInHalf_NoMessageNeeded()
    {
        var data = WithFF(FFRange("2026-01-05", "2026-01-08"), [FF("2026-01-09", 0.5m)]);
        var r = Bookings.BookFeriefri(data, D("2026-01-12"), D("2026-01-12"), 0.5m, "");
        Assert.True(r.IsComplete);
        Assert.Empty(r.HalfDayDates);
        Assert.Null(r.Limit);
    }

    [Fact]
    public void Book_Refused_WhenAllUsed_KeepsExistingRecordOnTheDay()
    {
        var existing = new BookedDay { Date = D("2026-01-12"), Type = DayType.Vacation, Amount = 1, Description = "v" };
        var data = WithFF(FFRange("2026-01-05", "2026-01-09"), [existing]);
        var before = data.Days.Count;

        var full = Bookings.BookFeriefri(data, D("2026-01-12"), D("2026-01-12"), 1m, "");
        Assert.True(full.IsRefused);
        Assert.Equal(1m, full.RequestedDays);
        Assert.Equal([D("2026-01-12")], full.LeftOutDates);
        Assert.Equal(before, full.Days.Count);
        Assert.Equal(DayType.Vacation, full.Days.Single(d => d.Date == D("2026-01-12")).Type);
        Assert.Equal(5m, full.Limit!.AllowanceDays);
        Assert.Equal(Hy2025, full.Limit.HolidayYear);

        var half = Bookings.BookFeriefri(data, D("2026-01-12"), D("2026-01-12"), 0.5m, "");
        Assert.True(half.IsRefused);
    }

    [Fact]
    public void Book_RangeStopsAtAllowance_CountsAndDates()
    {
        // 3 used. Range Mon 01-12..Fri 01-23 = 10 weekdays: 2 fit (01-12, 01-13), 8 left out (01-14..01-16, 01-19..01-23).
        var data = WithFF(FFRange("2026-01-05", "2026-01-07"));
        var r = Bookings.BookFeriefri(data, D("2026-01-12"), D("2026-01-23"), 1m, "");
        Assert.Equal(10m, r.RequestedDays);
        Assert.Equal(2m, r.BookedDays);
        Assert.Equal(8m, r.LeftOutDays);
        Assert.Equal(8, r.LeftOutDates.Count);
        Assert.Equal(D("2026-01-14"), r.LeftOutDates[0]);
        Assert.Equal(D("2026-01-23"), r.LeftOutDates[^1]);
        Assert.Empty(r.HalfDayDates);
        Assert.False(r.IsRefused);
        Assert.Equal(5, r.Days.Count);
    }

    [Fact]
    public void Book_Range_HalfThenLeftOut()
    {
        // 4.5 used; Mon 01-12..Wed 01-14: first becomes a half day, other two are left out. Booked 0.5, left out 2.5.
        var data = WithFF(FFRange("2026-01-05", "2026-01-08"), [FF("2026-01-09", 0.5m)]);
        var r = Bookings.BookFeriefri(data, D("2026-01-12"), D("2026-01-14"), 1m, "");
        Assert.Equal(0.5m, r.BookedDays);
        Assert.Equal(2.5m, r.LeftOutDays);
        Assert.Equal([D("2026-01-12")], r.HalfDayDates);
        Assert.Equal(2, r.LeftOutDates.Count);
    }

    [Fact]
    public void Book_SkipsWeekendsAndHolidays_AndAcceptsReversedRange()
    {
        // Fri 01-09..Mon 01-12 (reversed) = 2 working days. With Mon 01-12 a user holiday: 1.
        var plain = Bookings.BookFeriefri(new VacationData(), D("2026-01-12"), D("2026-01-09"), 1m, "");
        Assert.Equal(2m, plain.RequestedDays);
        Assert.Equal(2, plain.Days.Count);

        var hol = WithFF().WithHolidays("2026-01-12");
        var r = Bookings.BookFeriefri(hol, D("2026-01-09"), D("2026-01-12"), 1m, "");
        Assert.Equal(1m, r.RequestedDays);
        Assert.Single(r.Days);
    }

    [Fact]
    public void Book_ReplacesVacation_AndVacationReplacesFeriefri()
    {
        var data = WithFF([Day("2026-01-06")]); // vacation Tue
        var r = Bookings.BookFeriefri(data, D("2026-01-06"), D("2026-01-06"), 1m, "");
        Assert.Equal(DayType.Feriefri, Assert.Single(r.Days).Type);
        data.Days = r.Days;
        Assert.Equal(0m, BalanceCalculator.CalculateHolidayYear(data, Hy2025, Oct24).UsedDays);
        Assert.Equal(1m, Bal(data, Hy2025, Oct24).UsedDays);

        var back = Bookings.BookRange(data.Days, D("2026-01-06"), D("2026-01-06"), DayType.Vacation, "", new HashSet<DateOnly>());
        data.Days = back;
        Assert.Equal(0m, Bal(data, Hy2025, Oct24).UsedDays);
        Assert.Equal(1m, BalanceCalculator.CalculateHolidayYear(data, Hy2025, Oct24).UsedDays);
    }

    [Fact]
    public void Book_RebookingSameDay_FreesItFirst()
    {
        var data = WithFF(FFRange("2026-01-05", "2026-01-09")); // all 5 used
        var full = Bookings.BookFeriefri(data, D("2026-01-05"), D("2026-01-05"), 1m, "new");
        Assert.True(full.IsComplete);
        Assert.Equal("new", full.Days.Single(d => d.Date == D("2026-01-05")).Description);

        var half = Bookings.BookFeriefri(data, D("2026-01-05"), D("2026-01-05"), 0.5m, "");
        Assert.True(half.IsComplete);
        data.Days = half.Days;
        Assert.Equal(4.5m, Bal(data, Hy2025, Oct24).UsedDays);
    }

    [Fact]
    public void Book_AutumnSpansTwoYears_TenFit_ElevenDoNot()
    {
        // Mon 10-05..Mon 10-19 = 11 weekdays; 5 (HY2025) + 5 (HY2026) = 10 fit, Mon 10-19 left out; limit year is HY2026.
        var r = Bookings.BookFeriefri(new VacationData(), D("2026-10-05"), D("2026-10-19"), 1m, "");
        Assert.Equal(11m, r.RequestedDays);
        Assert.Equal(10m, r.BookedDays);
        Assert.Equal([D("2026-10-19")], r.LeftOutDates);
        Assert.Equal(Hy2026, r.Limit!.HolidayYear);
        Assert.False(r.IsRefused);
    }

    [Fact]
    public void Book_AllowanceZero_Refused()
    {
        var data = new VacationData();
        data.SetFeriefriAllowance(Hy2025, 0m);
        var r = Bookings.BookFeriefri(data, D("2026-01-05"), D("2026-01-05"), 1m, "");
        Assert.True(r.IsRefused);
        Assert.Equal(0m, r.Limit!.AllowanceDays);
    }

    [Fact]
    public void Book_DecimalAllowance_FourAndAHalf()
    {
        var data = new VacationData();
        data.SetFeriefriAllowance(Hy2025, 4.5m);
        var r = Bookings.BookFeriefri(data, D("2026-01-05"), D("2026-01-09"), 1m, "");
        Assert.Equal(4.5m, r.BookedDays);
        Assert.Equal([D("2026-01-09")], r.HalfDayDates);
        data.Days = r.Days;
        Assert.Equal(0m, Bal(data, Hy2025, Oct24).RemainingDays);
    }

    [Fact]
    public void Book_DaysPaidOutAtFirstYearEnd_CannotBeBookedInJanuary()
    {
        // HY2025 used up by 5 days in Jan 2026; HY2026 allowance 8 (own). At 31 Dec 2026: rem 8, open 8, 3 paid out.
        // So only 5 can be booked Mon 2027-01-04..Fri 01-08; Mon 01-11 is refused and the limit reports 3 paid out.
        var data = WithFF(FFRange("2026-01-05", "2026-01-09"));
        data.SetFeriefriAllowance(Hy2026, 8m);
        var r = Bookings.BookFeriefri(data, D("2027-01-04"), D("2027-01-11"), 1m, ""); // 6 weekdays
        Assert.Equal(6m, r.RequestedDays);
        Assert.Equal(5m, r.BookedDays);
        Assert.Equal([D("2027-01-11")], r.LeftOutDates);
        Assert.Equal(Hy2026, r.Limit!.HolidayYear);
        Assert.Equal(3m, r.Limit.PaidOutAtFirstYearEnd);
    }

    [Fact]
    public void Book_DecemberBookingsReducePayout_SoJanuaryHasMore()
    {
        // Same, but Mon 12-28..Wed 12-30 (3 days) are booked first -> HY2026 rem 5 at 31 Dec, open 5, nothing paid out.
        // January then has 8 - 3 = 5 days: 5 fit, the 6th is refused with 0 paid out.
        var data = WithFF(FFRange("2026-01-05", "2026-01-09"), FFRange("2026-12-28", "2026-12-30"));
        data.SetFeriefriAllowance(Hy2026, 8m);
        var r = Bookings.BookFeriefri(data, D("2027-01-04"), D("2027-01-11"), 1m, "");
        Assert.Equal(5m, r.BookedDays);
        Assert.Equal(0m, r.Limit!.PaidOutAtFirstYearEnd);
    }

    [Fact]
    public void Book_OverAllowanceElsewhere_DoesNotBlockOtherYear()
    {
        // 7 stored in HY2025; a booking in Jan 2027 (HY2026) is unaffected.
        var data = WithFF(FFRange("2026-01-05", "2026-01-13"));
        var r = Bookings.BookFeriefri(data, D("2027-01-04"), D("2027-01-04"), 1m, "");
        Assert.True(r.IsComplete);
        // but another Jan 2026 day is refused
        Assert.True(Bookings.BookFeriefri(data, D("2026-01-14"), D("2026-01-14"), 1m, "").IsRefused);
    }

    [Fact]
    public void Book_RangeCap_OneYear_AndBadAmounts()
    {
        var data = new VacationData();
        // Latest end for start 2026-01-05 is 2027-01-04.
        Assert.NotNull(Bookings.BookFeriefri(data, D("2026-01-05"), D("2027-01-04"), 1m, ""));
        Assert.Throws<ArgumentException>(() => Bookings.BookFeriefri(data, D("2026-01-05"), D("2027-01-05"), 1m, ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bookings.BookFeriefri(data, D("2026-01-05"), D("2026-01-05"), 0.3m, ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bookings.BookFeriefri(data, D("2026-01-05"), D("2026-01-05"), 2m, ""));
    }

    [Fact]
    public void Book_DoesNotModifyInput()
    {
        var data = WithFF(FFRange("2026-01-05", "2026-01-07"));
        var snapshot = JsonSerializer.Serialize(data.Days);
        Bookings.BookFeriefri(data, D("2026-01-12"), D("2026-01-14"), 1m, "");
        Assert.Equal(snapshot, JsonSerializer.Serialize(data.Days));
    }

    [Fact]
    public void Book_OnlyHolidaysInRange_RequestedZero_NotRefused()
    {
        var r = Bookings.BookFeriefri(new VacationData(), D("2026-01-10"), D("2026-01-11"), 1m, ""); // Sat+Sun
        Assert.Equal(0m, r.RequestedDays);
        Assert.False(r.IsRefused);
        Assert.Empty(r.Days);
    }
}

