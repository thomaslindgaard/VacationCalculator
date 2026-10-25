using VacationCalculator.Core;

namespace VacationCalculator.Core.Tests;

/// <summary>
/// Independent edge cases for <see cref="Bookings.CycleDay"/> and <see cref="Bookings.BookTypedRange"/> (feature 6).
/// Calendar 2026: Mon 2 Nov, Fri 6 Nov, Sat 7 Nov; Mon 31 Aug 2026, Tue 1 Sep 2026; Mon 28 Dec 2026; Mon 4 Jan 2027.
/// </summary>
public class BookingsCycleIndependentTests
{
    private static BookedDay Day(DateOnly d, DayType t = DayType.Vacation, decimal a = 1m, string desc = "") =>
        new() { Date = d, Type = t, Amount = a, Description = desc };

    private static VacationData Data(params BookedDay[] days)
    {
        var data = new VacationData();
        data.Days.AddRange(days);
        return data;
    }

    // Unused Feriefri of the previous holiday year is usable until 31 Dec, so zero 2025/26 and set 2026/27 explicitly.
    private static void Allow(VacationData data, decimal current)
    {
        data.SetFeriefriAllowance(new HolidayYear(2025), 0m);
        data.SetFeriefriAllowance(new HolidayYear(2026), current);
    }

    // ---- BookTypedRange ----

    [Fact]
    public void Range_ReversedEqualsForward()
    {
        var data = Data(Day(new(2026, 11, 4), DayType.Feriefri, 0.5m));
        var fwd = Bookings.BookTypedRange(data, new(2026, 11, 2), new(2026, 11, 10), DayType.Vacation);
        var rev = Bookings.BookTypedRange(data, new(2026, 11, 10), new(2026, 11, 2), DayType.Vacation);
        Assert.Equal(fwd.Start, rev.Start);
        Assert.Equal(fwd.End, rev.End);
        Assert.Equal(fwd.Days.Select(d => (d.Date, d.Type, d.Amount)), rev.Days.Select(d => (d.Date, d.Type, d.Amount)));
        // Mon 2 - Fri 6 (5) + Mon 9, Tue 10 (2) = 7 working days
        Assert.Equal(7, fwd.WorkingDays);
        Assert.Equal(7, fwd.Days.Count);
    }

    [Fact]
    public void Range_SingleWorkingDay_AndSingleWeekendDay()
    {
        var data = Data();
        var one = Bookings.BookTypedRange(data, new(2026, 11, 2), new(2026, 11, 2), DayType.Vacation);
        Assert.Equal(RangeBookingOutcome.Booked, one.Outcome);
        Assert.Single(one.Days);
        var weekend = Bookings.BookTypedRange(data, new(2026, 11, 7), new(2026, 11, 7), DayType.Feriefri);
        Assert.Equal(RangeBookingOutcome.NoWorkingDays, weekend.Outcome);
        Assert.False(weekend.Changed);
    }

    [Fact]
    public void Range_AcrossNewYear_SkipsHolidaysAndWeekend()
    {
        // Mon 28 Dec 2026 .. Mon 4 Jan 2027: 28, 29, 30 work; Thu 31 Dec and Fri 1 Jan holidays; Sat/Sun; Mon 4 Jan works.
        var r = Bookings.BookTypedRange(Data(), new(2026, 12, 28), new(2027, 1, 4), DayType.Vacation);
        Assert.Equal(4, r.WorkingDays);
        Assert.Equal([new DateOnly(2026, 12, 28), new(2026, 12, 29), new(2026, 12, 30), new(2027, 1, 4)], r.Days.Select(d => d.Date));
    }

    [Fact]
    public void Range_ManualHolidaySkipped()
    {
        var data = Data();
        data.Years.Add(new YearSettings { Year = 2026, PublicHolidays = [new DateOnly(2026, 11, 4)] });
        var r = Bookings.BookTypedRange(data, new(2026, 11, 2), new(2026, 11, 6), DayType.Vacation);
        Assert.Equal(4, r.WorkingDays);
        Assert.DoesNotContain(r.Days, d => d.Date == new DateOnly(2026, 11, 4));
    }

    [Fact]
    public void Range_Vacation_OverwritesFeriefriAndHalfDaysAsFull_KeepsOutsideRecords()
    {
        var data = Data(Day(new(2026, 11, 2), DayType.Feriefri), Day(new(2026, 11, 3), DayType.Vacation, 0.5m, "half"),
            Day(new(2026, 11, 9), DayType.Vacation, 1m, "outside"));
        var r = Bookings.BookTypedRange(data, new(2026, 11, 2), new(2026, 11, 3), DayType.Vacation);
        Assert.All(r.Days.Where(d => d.Date <= new DateOnly(2026, 11, 3)), d => Assert.Equal((DayType.Vacation, 1m), (d.Type, d.Amount)));
        Assert.Equal("outside", r.Days.Single(d => d.Date == new DateOnly(2026, 11, 9)).Description);
        Assert.Equal(3, r.Days.Count);
    }

    [Fact]
    public void Range_DoesNotModifyInput()
    {
        var data = Data(Day(new(2026, 11, 2), DayType.Feriefri, 0.5m));
        Bookings.BookTypedRange(data, new(2026, 11, 2), new(2026, 11, 6), DayType.Vacation);
        Bookings.CycleDay(data, new(2026, 11, 2), DayType.Feriefri);
        var only = Assert.Single(data.Days);
        Assert.Equal((DayType.Feriefri, 0.5m), (only.Type, only.Amount));
    }

    [Fact]
    public void Range_Feriefri_AcrossHolidayYearBoundary_FirstYearExhausted()
    {
        // Mon 31 Aug 2026 is in holiday year 2025/26 (allowance 0 here), Tue 1 Sep 2026 in 2026/27 (allowance 5).
        var data = Data();
        data.SetFeriefriAllowance(new HolidayYear(2025), 0m);
        data.SetFeriefriAllowance(new HolidayYear(2026), 5m);
        var r = Bookings.BookTypedRange(data, new(2026, 8, 31), new(2026, 9, 1), DayType.Feriefri);
        Assert.Equal(RangeBookingOutcome.Partial, r.Outcome);
        Assert.Equal([new DateOnly(2026, 9, 1)], r.Days.Select(d => d.Date));
        Assert.Equal([new DateOnly(2026, 8, 31)], r.LeftOutDates);
        Assert.Equal(1m, r.BookedDays);
        Assert.Equal(1m, r.LeftOutDays);
        Assert.Equal(new HolidayYear(2025), r.Limit!.HolidayYear);
    }

    [Fact]
    public void Range_Feriefri_AcrossBoundary_BothYearsHaveRoom_Booked()
    {
        var r = Bookings.BookTypedRange(Data(), new(2026, 8, 31), new(2026, 9, 1), DayType.Feriefri);
        Assert.Equal(RangeBookingOutcome.Booked, r.Outcome);
        Assert.Equal(2, r.Days.Count);
        Assert.Null(r.Limit);
    }

    [Fact]
    public void Range_Feriefri_HalfDayLeftover_GivesHalfDayAndPartial()
    {
        // Allowance 4.5: Mon 2 .. Thu 5 full (4), Fri 6 gets the last half.
        var data = Data();
        Allow(data, 4.5m);
        var r = Bookings.BookTypedRange(data, new(2026, 11, 2), new(2026, 11, 6), DayType.Feriefri);
        Assert.Equal(RangeBookingOutcome.Partial, r.Outcome);
        Assert.Equal(4.5m, r.BookedDays);
        Assert.Equal(0.5m, r.LeftOutDays);
        Assert.Equal([new DateOnly(2026, 11, 6)], r.HalfDayDates);
        Assert.Equal(0.5m, r.Days.Single(d => d.Date == new DateOnly(2026, 11, 6)).Amount);
        Assert.Empty(r.LeftOutDates);
    }

    [Fact]
    public void Range_Feriefri_ExistingFeriefriInRange_DoesNotDoubleCount()
    {
        // Mon 2 - Thu 5 already Feriefri (4 of 5). Dragging Mon 2 - Fri 6 resets them to full and adds Fri 6: total 5, complete.
        var data = Data(Day(new(2026, 11, 2), DayType.Feriefri), Day(new(2026, 11, 3), DayType.Feriefri),
            Day(new(2026, 11, 4), DayType.Feriefri), Day(new(2026, 11, 5), DayType.Feriefri));
        var r = Bookings.BookTypedRange(data, new(2026, 11, 2), new(2026, 11, 6), DayType.Feriefri);
        Assert.Equal(RangeBookingOutcome.Booked, r.Outcome);
        Assert.Equal(5, r.Days.Count);
    }

    [Fact]
    public void Range_Feriefri_NoRoom_KeepsExistingVacationRecords()
    {
        var data = Data(Day(new(2026, 11, 2), DayType.Vacation, 1m, "keep"));
        Allow(data, 0m);
        var r = Bookings.BookTypedRange(data, new(2026, 11, 2), new(2026, 11, 3), DayType.Feriefri);
        Assert.Equal(RangeBookingOutcome.Refused, r.Outcome);
        Assert.False(r.Changed);
        var kept = Assert.Single(r.Days);
        Assert.Equal((DayType.Vacation, "keep"), (kept.Type, kept.Description));
    }

    [Theory]
    [InlineData(2026, 1, 1)]
    [InlineData(2026, 3, 15)]
    [InlineData(2027, 2, 28)]
    [InlineData(2028, 2, 29)]  // leap day: latest end 2029-02-28
    [InlineData(2027, 12, 31)]
    public void Range_LengthLimit_ExactBoundary(int y, int m, int d)
    {
        var start = new DateOnly(y, m, d);
        var last = Bookings.LatestAllowedEnd(start);
        Assert.NotEqual(RangeBookingOutcome.TooLong, Bookings.BookTypedRange(Data(), start, last, DayType.Vacation).Outcome);
        var tooLong = Bookings.BookTypedRange(Data(), start, last.AddDays(1), DayType.Vacation);
        Assert.Equal(RangeBookingOutcome.TooLong, tooLong.Outcome);
        Assert.False(tooLong.Changed);
        Assert.Equal(last, tooLong.LatestAllowedEnd);
        // reversed order gives the same verdict
        Assert.Equal(RangeBookingOutcome.TooLong, Bookings.BookTypedRange(Data(), last.AddDays(1), start, DayType.Vacation).Outcome);
    }

    [Fact]
    public void Range_FullYearVacation_Booked()
    {
        // 2026 starts on a Thursday: 261 weekdays. Danish holidays on weekdays: 1 Jan, Maundy Thu 2 Apr, Good Fri 3 Apr,
        // Easter Mon 6 Apr (Easter Sunday 5 Apr), Ascension 14 May, Whit Mon 25 May, 24 Dec, 25 Dec, 31 Dec = 9
        // (26 Dec is a Saturday). 261 - 9 = 252.
        var r = Bookings.BookTypedRange(Data(), new(2026, 1, 1), new(2026, 12, 31), DayType.Vacation);
        Assert.Equal(RangeBookingOutcome.Booked, r.Outcome);
        Assert.Equal(252, r.WorkingDays);
        Assert.Equal(252, r.Days.Count);
    }

    // ---- CycleDay ----

    [Fact]
    public void Cycle_ManualHolidayAndStrayRecord_NoChange()
    {
        var data = Data(Day(new(2026, 11, 4), DayType.Vacation, 1m, "stray"));
        data.Years.Add(new YearSettings { Year = 2026, PublicHolidays = [new DateOnly(2026, 11, 4)] });
        var r = Bookings.CycleDay(data, new(2026, 11, 4), DayType.Vacation);
        Assert.Equal(DayCycleOutcome.NotAWorkingDay, r.Outcome);
        Assert.False(r.Changed);
        Assert.Equal("stray", Assert.Single(r.Days).Description);
    }

    [Fact]
    public void Cycle_FullToHalf_KeepsDescription_HalfToFree()
    {
        var data = Data(Day(new(2026, 11, 2), DayType.Vacation, 1m, "Italy"));
        var half = Bookings.CycleDay(data, new(2026, 11, 2), DayType.Vacation);
        Assert.Equal(DayCycleOutcome.ChangedToHalf, half.Outcome);
        Assert.Equal((0.5m, "Italy"), (half.Days[0].Amount, half.Days[0].Description));
        data.Days = half.Days;
        var free = Bookings.CycleDay(data, new(2026, 11, 2), DayType.Vacation);
        Assert.Equal(DayCycleOutcome.Removed, free.Outcome);
        Assert.Empty(free.Days);
    }

    [Fact]
    public void Cycle_Feriefri_ThreeClicksReturnToStart_FourthBooksAgain()
    {
        var data = Data();
        var date = new DateOnly(2026, 11, 2);
        var outcomes = new List<DayCycleOutcome>();
        for (var i = 0; i < 4; i++)
        {
            var r = Bookings.CycleDay(data, date, DayType.Feriefri);
            outcomes.Add(r.Outcome);
            data.Days = r.Days;
        }
        Assert.Equal([DayCycleOutcome.Booked, DayCycleOutcome.ChangedToHalf, DayCycleOutcome.Removed, DayCycleOutcome.Booked], outcomes);
    }

    [Fact]
    public void Cycle_Feriefri_AllowanceUsed_FullShrinksToHalf_NewDayRefused()
    {
        var data = Data(Day(new(2026, 11, 2), DayType.Feriefri), Day(new(2026, 11, 3), DayType.Feriefri),
            Day(new(2026, 11, 4), DayType.Feriefri), Day(new(2026, 11, 5), DayType.Feriefri), Day(new(2026, 11, 6), DayType.Feriefri));
        Allow(data, 5m);
        Assert.Equal(DayCycleOutcome.ChangedToHalf, Bookings.CycleDay(data, new(2026, 11, 6), DayType.Feriefri).Outcome);
        var refused = Bookings.CycleDay(data, new(2026, 11, 9), DayType.Feriefri);
        Assert.Equal(DayCycleOutcome.Refused, refused.Outcome);
        Assert.Equal(new HolidayYear(2026), refused.Limit!.HolidayYear);
        Assert.Equal(5, refused.Days.Count);
    }

    [Fact]
    public void Cycle_Feriefri_FirstSeptember_UsesNewHolidayYearAllowance()
    {
        // Mon 31 Aug 2026 belongs to 2025/26 (allowance 0 here), Tue 1 Sep 2026 to 2026/27 (allowance 5).
        var data = Data();
        Allow(data, 5m);
        var aug = Bookings.CycleDay(data, new(2026, 8, 31), DayType.Feriefri);
        Assert.Equal(DayCycleOutcome.Refused, aug.Outcome);
        Assert.Equal(new HolidayYear(2025), aug.Limit!.HolidayYear);
        Assert.Equal(DayCycleOutcome.Booked, Bookings.CycleDay(data, new(2026, 9, 1), DayType.Feriefri).Outcome);
    }

    [Fact]
    public void Cycle_Feriefri_PreviousYearLeftoverIsUsableUntilNewYear()
    {
        // Default allowances (5 + 5): 2025/26's unused days can still be used in Nov 2026, so 10 days fit and the 11th doesn't.
        // Mon 2 - Fri 6 Nov, Mon 9 - Fri 13 Nov = 10 working days.
        var data = Data();
        data.SetFeriefriAllowance(new HolidayYear(2025), 5m);
        data.SetFeriefriAllowance(new HolidayYear(2026), 5m);
        var r = Bookings.BookTypedRange(data, new(2026, 11, 2), new(2026, 11, 13), DayType.Feriefri);
        Assert.Equal(RangeBookingOutcome.Booked, r.Outcome);
        Assert.Equal(10, r.Days.Count);
        data.Days = r.Days;
        Assert.Equal(DayCycleOutcome.Refused, Bookings.CycleDay(data, new(2026, 11, 16), DayType.Feriefri).Outcome);
    }

    [Fact]
    public void Cycle_Vacation_NeverRefused_EvenBeyondBalance()
    {
        var r = Bookings.CycleDay(Data(), new(2026, 11, 2), DayType.Vacation);
        Assert.Equal(DayCycleOutcome.Booked, r.Outcome);
        Assert.Null(r.Limit);
    }

    [Fact]
    public void Cycle_VacationHalfDay_ClickedWithFeriefri_ReplacedFull_DescriptionCleared()
    {
        var data = Data(Day(new(2026, 11, 2), DayType.Vacation, 0.5m, "old"));
        var r = Bookings.CycleDay(data, new(2026, 11, 2), DayType.Feriefri);
        Assert.Equal(DayCycleOutcome.Replaced, r.Outcome);
        var d = Assert.Single(r.Days);
        Assert.Equal((DayType.Feriefri, 1m, ""), (d.Type, d.Amount, d.Description));
    }

    [Fact]
    public void Cycle_OtherRecordsStayAndListStaysSorted()
    {
        var data = Data(Day(new(2026, 11, 9)), Day(new(2026, 11, 3)));
        var r = Bookings.CycleDay(data, new(2026, 11, 5), DayType.Vacation);
        Assert.Equal([new DateOnly(2026, 11, 3), new(2026, 11, 5), new(2026, 11, 9)], r.Days.Select(d => d.Date));
    }
}


