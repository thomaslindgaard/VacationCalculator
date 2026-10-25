using VacationCalculator.Core;
using static VacationCalculator.Core.Tests.TestData;

namespace VacationCalculator.Core.Tests;

/// <summary>
/// Feature 6: <see cref="Bookings.CycleDay"/> (click cycle) and <see cref="Bookings.BookTypedRange"/> (drag / Shift+click).
/// Feriefri allowance is the default 5 per holiday year; January 2026 days count against holiday year 2025/26.
/// </summary>
public class BookingsCycleTests
{
    private static readonly HolidayYear Hy2025 = new(2025);

    private static BookedDay FF(string date, decimal amount = 1, string description = "") =>
        new() { Date = D(date), Type = DayType.Feriefri, Amount = amount, Description = description };

    private static BookedDay Vac(string date, decimal amount = 1, string description = "") =>
        new() { Date = D(date), Type = DayType.Vacation, Amount = amount, Description = description };

    /// <summary>Full Feriefri on every weekday of the range (no holidays in the ranges used here).</summary>
    private static List<BookedDay> FFRange(string start, string end) =>
        Bookings.BookRange([], D(start), D(end), DayType.Feriefri, "", new HashSet<DateOnly>());

    private static VacationData With(params IEnumerable<BookedDay>[] days) => Data(days.SelectMany(d => d));

    /// <summary>5 Feriefri days used: Mon 2026-01-05 .. Fri 2026-01-09.</summary>
    private static List<BookedDay> AllFeriefriUsed() => FFRange("2026-01-05", "2026-01-09");

    /// <summary>4.5 Feriefri days used: Mon..Thu 2026-01-05..08 + half Fri 01-09.</summary>
    private static List<BookedDay> FourAndAHalfUsed() => [.. FFRange("2026-01-05", "2026-01-08"), FF("2026-01-09", 0.5m)];

    // ---------- vacation cycle (left-click) ----------

    [Fact]
    public void Vacation_FreeDay_BecomesFullDay_WithEmptyDescription()
    {
        var data = Data();

        var r = Bookings.CycleDay(data, D("2026-01-12"), DayType.Vacation); // Mon

        Assert.Equal(DayCycleOutcome.Booked, r.Outcome);
        Assert.True(r.Changed);
        Assert.Null(r.Previous);
        Assert.Equal(Vac("2026-01-12"), r.Current);
        Assert.Equal([Vac("2026-01-12")], r.Days);
        Assert.Null(r.Limit);
        Assert.Empty(data.Days); // input not modified
    }

    [Fact]
    public void Vacation_FullDay_BecomesHalfDay_KeepingTheDescription()
    {
        var data = Data([Vac("2026-01-12", 1, "Trip")]); // Mon

        var r = Bookings.CycleDay(data, D("2026-01-12"), DayType.Vacation);

        Assert.Equal(DayCycleOutcome.ChangedToHalf, r.Outcome);
        Assert.Equal(Vac("2026-01-12", 1, "Trip"), r.Previous);
        Assert.Equal([Vac("2026-01-12", 0.5m, "Trip")], r.Days);
        Assert.Equal(1m, data.Days[0].Amount); // input not modified
    }

    [Fact]
    public void Vacation_HalfDay_IsRemoved()
    {
        var data = Data([Vac("2026-01-12", 0.5m, "Trip"), Vac("2026-01-13")]); // Mon, Tue

        var r = Bookings.CycleDay(data, D("2026-01-12"), DayType.Vacation);

        Assert.Equal(DayCycleOutcome.Removed, r.Outcome);
        Assert.Null(r.Current);
        Assert.Equal([Vac("2026-01-13")], r.Days);
    }

    [Fact]
    public void Vacation_ThreeClicks_GoFreeFullHalfFree()
    {
        var data = Data();
        var date = D("2026-01-14"); // Wed

        var outcomes = new List<DayCycleOutcome>();
        var amounts = new List<decimal?>();
        for (var i = 0; i < 3; i++)
        {
            var r = Bookings.CycleDay(data, date, DayType.Vacation);
            outcomes.Add(r.Outcome);
            amounts.Add(r.Current?.Amount);
            data.Days = r.Days;
        }

        Assert.Equal([DayCycleOutcome.Booked, DayCycleOutcome.ChangedToHalf, DayCycleOutcome.Removed], outcomes);
        Assert.Equal([1m, 0.5m, null], amounts);
        Assert.Empty(data.Days);
    }

    // ---------- Feriefri cycle (right-click) ----------

    [Fact]
    public void Feriefri_ThreeClicks_GoFreeFullHalfFree()
    {
        var data = Data();
        var date = D("2026-01-14"); // Wed

        var outcomes = new List<DayCycleOutcome>();
        var current = new List<BookedDay?>();
        for (var i = 0; i < 3; i++)
        {
            var r = Bookings.CycleDay(data, date, DayType.Feriefri);
            outcomes.Add(r.Outcome);
            current.Add(r.Current);
            Assert.Null(r.Limit);
            data.Days = r.Days;
        }

        Assert.Equal([DayCycleOutcome.Booked, DayCycleOutcome.ChangedToHalf, DayCycleOutcome.Removed], outcomes);
        Assert.Equal([FF("2026-01-14"), FF("2026-01-14", 0.5m), null], current);
        Assert.Empty(data.Days);
    }

    [Fact]
    public void Feriefri_FullDay_BecomesHalf_EvenWhenTheAllowanceIsFullyUsed()
    {
        // 5 used, the clicked Fri 2026-01-09 being one of them, with a description.
        var days = FFRange("2026-01-05", "2026-01-08");
        days.Add(FF("2026-01-09", 1, "Dentist"));
        var data = Data(days);

        var r = Bookings.CycleDay(data, D("2026-01-09"), DayType.Feriefri);

        Assert.Equal(DayCycleOutcome.ChangedToHalf, r.Outcome);
        Assert.Equal(FF("2026-01-09", 0.5m, "Dentist"), r.Current);
        Assert.Null(r.Limit);
    }

    [Fact]
    public void Feriefri_FreeDay_WhenOnlyHalfFits_IsBookedAsHalf_AndTheNextClickFreesIt()
    {
        var data = Data(FourAndAHalfUsed());

        var half = Bookings.CycleDay(data, D("2026-01-12"), DayType.Feriefri); // Mon

        Assert.Equal(DayCycleOutcome.ConvertedToHalf, half.Outcome);
        Assert.True(half.Changed);
        Assert.Null(half.Previous);
        Assert.Equal(FF("2026-01-12", 0.5m), half.Current);
        Assert.Equal(Hy2025, half.Limit!.HolidayYear);
        Assert.Equal(5m, half.Limit.AllowanceDays);

        data.Days = half.Days;
        var removed = Bookings.CycleDay(data, D("2026-01-12"), DayType.Feriefri);

        Assert.Equal(DayCycleOutcome.Removed, removed.Outcome);
        Assert.Equal(FourAndAHalfUsed(), removed.Days);
    }

    [Fact]
    public void Feriefri_FreeDay_WhenNothingFits_IsRefused_WithTheLimit()
    {
        var data = Data(AllFeriefriUsed());

        var r = Bookings.CycleDay(data, D("2026-01-12"), DayType.Feriefri); // Mon

        Assert.Equal(DayCycleOutcome.Refused, r.Outcome);
        Assert.False(r.Changed);
        Assert.Null(r.Previous);
        Assert.Null(r.Current);
        Assert.Equal(AllFeriefriUsed(), r.Days);
        Assert.Equal(Hy2025, r.Limit!.HolidayYear);
        Assert.Equal(5m, r.Limit.AllowanceDays);
    }

    [Fact]
    public void Feriefri_AtTheLimit_31AugustIsRefused_But1SeptemberUsesTheNewHolidayYear()
    {
        var data = Data(AllFeriefriUsed()); // holiday year 2025/26 used up in January 2026

        var aug31 = Bookings.CycleDay(data, D("2026-08-31"), DayType.Feriefri); // Mon, last day of 2025/26
        var sep1 = Bookings.CycleDay(data, D("2026-09-01"), DayType.Feriefri); // Tue, first day of 2026/27

        Assert.Equal(DayCycleOutcome.Refused, aug31.Outcome);
        Assert.Equal(Hy2025, aug31.Limit!.HolidayYear);
        Assert.Equal(DayCycleOutcome.Booked, sep1.Outcome);
        Assert.Equal(FF("2026-09-01"), sep1.Current);
    }

    // ---------- other type: replace ----------

    [Fact]
    public void RightClick_OnVacation_ReplacesItWithFullFeriefri_AndClearsTheDescription()
    {
        var data = Data([Vac("2026-01-12", 1, "Trip")]); // Mon

        var r = Bookings.CycleDay(data, D("2026-01-12"), DayType.Feriefri);

        Assert.Equal(DayCycleOutcome.Replaced, r.Outcome);
        Assert.Equal(Vac("2026-01-12", 1, "Trip"), r.Previous);
        Assert.Equal([FF("2026-01-12")], r.Days);
    }

    [Fact]
    public void LeftClick_OnHalfFeriefri_ReplacesItWithFullVacation()
    {
        var data = Data([FF("2026-01-12", 0.5m, "Dentist")]); // Mon

        var r = Bookings.CycleDay(data, D("2026-01-12"), DayType.Vacation);

        Assert.Equal(DayCycleOutcome.Replaced, r.Outcome);
        Assert.Equal([Vac("2026-01-12")], r.Days);
    }

    [Fact]
    public void RightClick_OnHalfVacation_ReplacesItWithFullFeriefri()
    {
        var data = Data([Vac("2026-01-12", 0.5m)]); // Mon

        var r = Bookings.CycleDay(data, D("2026-01-12"), DayType.Feriefri);

        Assert.Equal(DayCycleOutcome.Replaced, r.Outcome);
        Assert.Equal([FF("2026-01-12")], r.Days);
    }

    [Fact]
    public void RightClick_OnVacation_WhenOnlyHalfFits_ReplacesItWithHalfFeriefri()
    {
        var data = With(FourAndAHalfUsed(), [Vac("2026-01-12", 1, "Trip")]); // Mon

        var r = Bookings.CycleDay(data, D("2026-01-12"), DayType.Feriefri);

        Assert.Equal(DayCycleOutcome.ReplacedAsHalf, r.Outcome);
        Assert.Equal(FF("2026-01-12", 0.5m), r.Current);
        Assert.Equal(Hy2025, r.Limit!.HolidayYear);
        Assert.DoesNotContain(r.Days, d => d.Type == DayType.Vacation);
    }

    [Fact]
    public void RightClick_OnVacation_WhenNothingFits_IsRefused_AndTheVacationStays()
    {
        var data = With(AllFeriefriUsed(), [Vac("2026-01-12", 1, "Trip")]); // Mon

        var r = Bookings.CycleDay(data, D("2026-01-12"), DayType.Feriefri);

        Assert.Equal(DayCycleOutcome.Refused, r.Outcome);
        Assert.Equal(Vac("2026-01-12", 1, "Trip"), r.Previous);
        Assert.Equal(Vac("2026-01-12", 1, "Trip"), r.Current);
        Assert.Contains(Vac("2026-01-12", 1, "Trip"), r.Days);
        Assert.Equal(6, r.Days.Count);
        Assert.Equal(Hy2025, r.Limit!.HolidayYear);
    }

    // ---------- non-working days ----------

    [Theory]
    [InlineData("2026-01-10", DayType.Vacation)] // Sat
    [InlineData("2026-01-11", DayType.Feriefri)] // Sun
    [InlineData("2026-01-01", DayType.Vacation)] // Thu, Nytårsdag
    [InlineData("2026-12-24", DayType.Feriefri)] // Thu, Juleaftensdag
    [InlineData("2026-12-31", DayType.Vacation)] // Thu, Nytårsaftensdag
    [InlineData("2026-06-05", DayType.Vacation)] // Fri, manual holiday (Grundlovsdag)
    [InlineData("2026-06-05", DayType.Feriefri)]
    public void NonWorkingDay_IsUnchanged(string date, DayType type)
    {
        var data = Data([Vac("2026-01-12")]).WithHolidays("2026-06-05");

        var r = Bookings.CycleDay(data, D(date), type);

        Assert.Equal(DayCycleOutcome.NotAWorkingDay, r.Outcome);
        Assert.False(r.Changed);
        Assert.Null(r.Current);
        Assert.Equal([Vac("2026-01-12")], r.Days);
    }

    [Fact]
    public void BookingOnAManualHoliday_IsNotCycled()
    {
        // Fri 2026-06-05 was booked before it was added as a manual holiday (a conflict).
        var data = Data([Vac("2026-06-05")]).WithHolidays("2026-06-05");

        var r = Bookings.CycleDay(data, D("2026-06-05"), DayType.Vacation);

        Assert.Equal(DayCycleOutcome.NotAWorkingDay, r.Outcome);
        Assert.Equal(Vac("2026-06-05"), r.Previous);
        Assert.Equal(Vac("2026-06-05"), r.Current);
        Assert.Equal([Vac("2026-06-05")], r.Days);
    }

    [Fact]
    public void Cycle_LastSupportedWorkingDay_Works()
    {
        // Thu 9999-12-30 is a working day; Fri 9999-12-31 is Nytårsaftensdag.
        var r = Bookings.CycleDay(Data(), D("9999-12-30"), DayType.Feriefri);
        var last = Bookings.CycleDay(Data(), D("9999-12-31"), DayType.Vacation);

        Assert.Equal(DayCycleOutcome.Booked, r.Outcome);
        Assert.Equal(DayCycleOutcome.NotAWorkingDay, last.Outcome);
    }

    // ---------- ranges: vacation ----------

    [Fact]
    public void VacationRange_SkipsWeekendsAndChristmas_AcrossNewYear()
    {
        // Mon 2026-12-21 .. Fri 2027-01-01: Mon..Wed 21..23, (Thu 24, Fri 25 holidays, Sat/Sun 26/27),
        // Mon..Wed 28..30, (Thu 31, Fri 2027-01-01 holidays) = 6 working days.
        var r = Bookings.BookTypedRange(Data(), D("2026-12-21"), D("2027-01-01"), DayType.Vacation);

        Assert.Equal(RangeBookingOutcome.Booked, r.Outcome);
        Assert.True(r.Changed);
        Assert.Equal(6, r.WorkingDays);
        Assert.Equal(6m, r.BookedDays);
        Assert.Equal(0m, r.LeftOutDays);
        Assert.Equal(
            ["2026-12-21", "2026-12-22", "2026-12-23", "2026-12-28", "2026-12-29", "2026-12-30"],
            r.Days.Select(d => d.Date.ToString("yyyy-MM-dd")));
        Assert.All(r.Days, d => Assert.Equal(Vac(d.Date.ToString("yyyy-MM-dd")), d));
    }

    [Fact]
    public void VacationRange_ReplacesExistingDays_AsFullVacation_AndSkipsAManualHoliday()
    {
        // Mon 2026-01-12 .. Fri 01-16. Tue 13 Feriefri, Wed 14 half vacation "Trip", Fri 16 manual holiday.
        // Outside the range: Fri 01-09 Feriefri stays.
        var data = Data([FF("2026-01-09"), FF("2026-01-13", 1, "Dentist"), Vac("2026-01-14", 0.5m, "Trip")])
            .WithHolidays("2026-01-16");

        var r = Bookings.BookTypedRange(data, D("2026-01-12"), D("2026-01-16"), DayType.Vacation);

        Assert.Equal(RangeBookingOutcome.Booked, r.Outcome);
        Assert.Equal(4, r.WorkingDays);
        Assert.Equal(
            [FF("2026-01-09"), Vac("2026-01-12"), Vac("2026-01-13"), Vac("2026-01-14"), Vac("2026-01-15")],
            r.Days);
        Assert.Equal(3, data.Days.Count); // input not modified
    }

    [Fact]
    public void Range_ReversedOrder_GivesTheSameResult()
    {
        var forward = Bookings.BookTypedRange(Data(), D("2026-01-12"), D("2026-01-16"), DayType.Vacation);
        var backward = Bookings.BookTypedRange(Data(), D("2026-01-16"), D("2026-01-12"), DayType.Vacation); // Fri → Mon

        Assert.Equal(forward.Days, backward.Days);
        Assert.Equal(D("2026-01-12"), backward.Start);
        Assert.Equal(D("2026-01-16"), backward.End);
        Assert.Equal(5, backward.WorkingDays);
    }

    [Fact]
    public void Range_SingleDay_BooksThatDay()
    {
        var r = Bookings.BookTypedRange(Data(), D("2026-01-12"), D("2026-01-12"), DayType.Vacation); // Mon

        Assert.Equal(RangeBookingOutcome.Booked, r.Outcome);
        Assert.Equal(1, r.WorkingDays);
        Assert.Equal([Vac("2026-01-12")], r.Days);
    }

    [Fact]
    public void Range_OnlyWeekendAndHolidays_HasNoWorkingDays()
    {
        // Thu 2026-12-24 .. Sun 12-27: two holidays and a weekend.
        var data = Data([Vac("2026-01-12")]);

        var weekendOnly = Bookings.BookTypedRange(data, D("2026-01-10"), D("2026-01-10"), DayType.Feriefri); // Sat
        var christmas = Bookings.BookTypedRange(data, D("2026-12-24"), D("2026-12-27"), DayType.Vacation);

        Assert.Equal(RangeBookingOutcome.NoWorkingDays, weekendOnly.Outcome);
        Assert.Equal(RangeBookingOutcome.NoWorkingDays, christmas.Outcome);
        Assert.False(christmas.Changed);
        Assert.Equal([Vac("2026-01-12")], christmas.Days);
        Assert.Equal(0, christmas.WorkingDays);
    }

    [Fact]
    public void Range_ExactlyOneYear_IsBooked()
    {
        // Calendar year 2026: 261 weekdays (52 weeks + Thu 01-01) minus 9 Danish holidays on weekdays
        // (Thu 01-01, Thu 04-02, Fri 04-03, Mon 04-06, Thu 05-14, Mon 05-25, Thu 12-24, Fri 12-25, Thu 12-31) = 252.
        var r = Bookings.BookTypedRange(Data(), D("2026-01-01"), D("2026-12-31"), DayType.Vacation);

        Assert.Equal(RangeBookingOutcome.Booked, r.Outcome);
        Assert.Equal(252, r.WorkingDays);
        Assert.Equal(252, r.Days.Count);
        Assert.Null(r.LatestAllowedEnd);
    }

    [Theory]
    [InlineData(DayType.Vacation)]
    [InlineData(DayType.Feriefri)]
    public void Range_OneDayTooLong_IsRefusedWithoutThrowing(DayType type)
    {
        var data = Data([Vac("2026-01-12")]);

        var r = Bookings.BookTypedRange(data, D("2027-01-01"), D("2026-01-01"), type); // reversed, 1 day too long

        Assert.Equal(RangeBookingOutcome.TooLong, r.Outcome);
        Assert.False(r.Changed);
        Assert.Equal(D("2026-01-01"), r.Start);
        Assert.Equal(D("2026-12-31"), r.LatestAllowedEnd);
        Assert.Equal([Vac("2026-01-12")], r.Days);
        Assert.Equal(0, r.WorkingDays);
    }

    [Fact]
    public void Range_FromLeapDay_LatestEndIs28FebruaryNextYear()
    {
        // Tue 2028-02-29 .. Thu 2029-03-01 is one day too long.
        var r = Bookings.BookTypedRange(Data(), D("2028-02-29"), D("2029-03-01"), DayType.Vacation);
        var ok = Bookings.BookTypedRange(Data(), D("2028-02-29"), D("2029-02-28"), DayType.Vacation);

        Assert.Equal(RangeBookingOutcome.TooLong, r.Outcome);
        Assert.Equal(D("2029-02-28"), r.LatestAllowedEnd);
        Assert.Equal(RangeBookingOutcome.Booked, ok.Outcome);
    }

    [Fact]
    public void Range_AtTheEndOfTheSupportedRange_Works()
    {
        // Mon 9999-12-27 .. Fri 9999-12-31 (Nytårsaftensdag): 4 working days.
        var r = Bookings.BookTypedRange(Data(), D("9999-12-27"), D("9999-12-31"), DayType.Vacation);

        Assert.Equal(RangeBookingOutcome.Booked, r.Outcome);
        Assert.Equal(4, r.WorkingDays);
    }

    // ---------- ranges: Feriefri ----------

    [Fact]
    public void FeriefriRange_RunsOutMidway_FullThenHalfThenLeftOut()
    {
        // 2.5 used: Mon, Tue 2026-01-05..06 + half Wed 01-07. Range Mon 2026-02-02 .. Fri 02-06 (5 working days):
        // Mon 02 full (3.5), Tue 03 full (4.5), Wed 04 half (5), Thu 05 and Fri 06 left out.
        // Fri 02-06 has a vacation day that stays.
        var data = With(FFRange("2026-01-05", "2026-01-06"), [FF("2026-01-07", 0.5m), Vac("2026-02-06", 1, "Trip")]);

        var r = Bookings.BookTypedRange(data, D("2026-02-06"), D("2026-02-02"), DayType.Feriefri); // reversed

        Assert.Equal(RangeBookingOutcome.Partial, r.Outcome);
        Assert.True(r.Changed);
        Assert.Equal(5, r.WorkingDays);
        Assert.Equal(2.5m, r.BookedDays);
        Assert.Equal(2.5m, r.LeftOutDays);
        Assert.Equal([D("2026-02-04")], r.HalfDayDates);
        Assert.Equal([D("2026-02-05"), D("2026-02-06")], r.LeftOutDates);
        Assert.Equal(Hy2025, r.Limit!.HolidayYear);
        Assert.Equal(5m, r.Limit.AllowanceDays);
        Assert.Contains(FF("2026-02-02"), r.Days);
        Assert.Contains(FF("2026-02-03"), r.Days);
        Assert.Contains(FF("2026-02-04", 0.5m), r.Days);
        Assert.DoesNotContain(r.Days, d => d.Date == D("2026-02-05"));
        Assert.Contains(Vac("2026-02-06", 1, "Trip"), r.Days);
    }

    [Fact]
    public void FeriefriRange_ReplacesVacationAndMakesHalfFeriefriFull()
    {
        // Mon 2026-01-12 .. Wed 01-14: Mon vacation, Tue half Feriefri "Dentist", Wed free. 3 used afterwards.
        var data = Data([Vac("2026-01-12"), FF("2026-01-13", 0.5m, "Dentist")]);

        var r = Bookings.BookTypedRange(data, D("2026-01-12"), D("2026-01-14"), DayType.Feriefri);

        Assert.Equal(RangeBookingOutcome.Booked, r.Outcome);
        Assert.Equal(3m, r.BookedDays);
        Assert.Equal([FF("2026-01-12"), FF("2026-01-13"), FF("2026-01-14")], r.Days);
        Assert.Null(r.Limit);
    }

    [Fact]
    public void FeriefriRange_WhenNothingFits_IsRefused_AndNothingChanges()
    {
        var data = With(AllFeriefriUsed(), [Vac("2026-01-13")]);

        var r = Bookings.BookTypedRange(data, D("2026-01-12"), D("2026-01-16"), DayType.Feriefri); // Mon..Fri

        Assert.Equal(RangeBookingOutcome.Refused, r.Outcome);
        Assert.False(r.Changed);
        Assert.Equal(5, r.WorkingDays);
        Assert.Equal(0m, r.BookedDays);
        Assert.Equal(5, r.LeftOutDates.Count);
        Assert.Equal(Hy2025, r.Limit!.HolidayYear);
        Assert.Equal(data.Days.OrderBy(d => d.Date), r.Days);
    }

    [Fact]
    public void FeriefriRange_Across31August_UsesTheNewHolidayYearFrom1September()
    {
        // 2025/26 used up. Mon 2026-08-31 left out; Tue 09-01 and Wed 09-02 booked from 2026/27.
        var data = Data(AllFeriefriUsed());

        var r = Bookings.BookTypedRange(data, D("2026-08-31"), D("2026-09-02"), DayType.Feriefri);

        Assert.Equal(RangeBookingOutcome.Partial, r.Outcome);
        Assert.Equal(3, r.WorkingDays);
        Assert.Equal(2m, r.BookedDays);
        Assert.Equal([D("2026-08-31")], r.LeftOutDates);
        Assert.Empty(r.HalfDayDates);
        Assert.Equal(Hy2025, r.Limit!.HolidayYear);
    }

    // ---------- ranges: days already booked as the range would book them ----------

    private static readonly HolidayYear Hy2026 = new(2026);

    /// <summary>2025/26 gets 0 Feriefri (so its leftover can't be used in autumn 2026) and 2026/27 gets <paramref name="allowance"/>.</summary>
    private static VacationData Autumn2026(decimal allowance, params BookedDay[] days)
    {
        var data = Data(days);
        data.SetFeriefriAllowance(Hy2025, 0m);
        data.SetFeriefriAllowance(Hy2026, allowance);
        return data;
    }

    [Fact]
    public void FeriefriRange_EveryDayAlreadyAsBooked_ChangesNothing_AndReportsNothing()
    {
        // Allowance 2, all used: Mon 2026-11-02 half (outside), Tue 11-03 half "Dentist", Wed 11-04 full.
        // Range Tue 3 .. Wed 4: Tue can only be half (1.5 others + 0.5) = as it is; Wed fits full = as it is.
        var data = Autumn2026(2m, FF("2026-11-02", 0.5m), FF("2026-11-03", 0.5m, "Dentist"), FF("2026-11-04"));

        var r = Bookings.BookTypedRange(data, D("2026-11-03"), D("2026-11-04"), DayType.Feriefri);

        Assert.Equal(RangeBookingOutcome.Booked, r.Outcome);
        Assert.False(r.Changed);
        Assert.Equal(2, r.WorkingDays);
        Assert.Equal(0m, r.BookedDays);
        Assert.Equal(0m, r.LeftOutDays);
        Assert.Empty(r.HalfDayDates);
        Assert.Empty(r.LeftOutDates);
        Assert.Equal([D("2026-11-03"), D("2026-11-04")], r.UnchangedDates);
        Assert.Null(r.Limit);
        Assert.Equal(data.Days, r.Days); // "Dentist" kept
    }

    [Fact]
    public void FeriefriRange_ReviewScenario_HalfDayIsMadeFull_FullDayIsUnchanged()
    {
        // Allowance 2: Tue 2026-11-03 half, Wed 11-04 full (1.5 used). Range Tue 3 .. Wed 4:
        // Tue: 1 other + 1 = 2 fits → becomes full (a change); Wed: 1 other + 1 = 2 fits → as it is.
        var data = Autumn2026(2m, FF("2026-11-03", 0.5m), FF("2026-11-04"));

        var r = Bookings.BookTypedRange(data, D("2026-11-03"), D("2026-11-04"), DayType.Feriefri);

        Assert.Equal(RangeBookingOutcome.Booked, r.Outcome);
        Assert.True(r.Changed);
        Assert.Equal(1m, r.BookedDays);
        Assert.Equal(0m, r.LeftOutDays);
        Assert.Empty(r.HalfDayDates);
        Assert.Equal([D("2026-11-04")], r.UnchangedDates);
        Assert.Equal([FF("2026-11-03"), FF("2026-11-04")], r.Days);
    }

    [Fact]
    public void FeriefriRange_UnchangedHalfDay_WithANewDayLeftOut_IsRefused()
    {
        // Allowance 2, all used: Mon 2026-11-02 half (outside), Tue 03 half, Wed 04 full. Range Tue 3 .. Thu 5:
        // Tue and Wed as they are; Thu 05 doesn't fit at all. Nothing changed, so Refused.
        var data = Autumn2026(2m, FF("2026-11-02", 0.5m), FF("2026-11-03", 0.5m), FF("2026-11-04"));

        var r = Bookings.BookTypedRange(data, D("2026-11-03"), D("2026-11-05"), DayType.Feriefri);

        Assert.Equal(RangeBookingOutcome.Refused, r.Outcome);
        Assert.False(r.Changed);
        Assert.Equal(3, r.WorkingDays);
        Assert.Equal(0m, r.BookedDays);
        Assert.Equal(1m, r.LeftOutDays);
        Assert.Empty(r.HalfDayDates);
        Assert.Equal([D("2026-11-05")], r.LeftOutDates);
        Assert.Equal([D("2026-11-03"), D("2026-11-04")], r.UnchangedDates);
        Assert.Equal(Hy2026, r.Limit!.HolidayYear);
        Assert.Equal(data.Days, r.Days);
    }

    [Fact]
    public void FeriefriRange_UnchangedDays_WithANewHalfDay_IsPartial_ReportingOnlyTheNewDay()
    {
        // Allowance 3: Mon 2026-11-02 half (outside), Tue 03 full, Wed 04 full (2.5 used). Range Tue 3 .. Thu 5:
        // Tue: others 1.5 + 1 = 2.5 fits → as it is. Wed: same → as it is. Thu: 2.5 + 1 = 3.5 over, + 0.5 = 3 fits → half.
        var data = Autumn2026(3m, FF("2026-11-02", 0.5m), FF("2026-11-03"), FF("2026-11-04"));

        var r = Bookings.BookTypedRange(data, D("2026-11-03"), D("2026-11-05"), DayType.Feriefri);

        Assert.Equal(RangeBookingOutcome.Partial, r.Outcome);
        Assert.True(r.Changed);
        Assert.Equal(3, r.WorkingDays);
        Assert.Equal(0.5m, r.BookedDays);
        Assert.Equal(0.5m, r.LeftOutDays);
        Assert.Equal([D("2026-11-05")], r.HalfDayDates);
        Assert.Empty(r.LeftOutDates);
        Assert.Equal([D("2026-11-03"), D("2026-11-04")], r.UnchangedDates);
        Assert.Equal(Hy2026, r.Limit!.HolidayYear);
        Assert.Contains(FF("2026-11-05", 0.5m), r.Days);
    }

    [Fact]
    public void FeriefriRange_UnchangedFullDays_KeepTheirDescription_NewDaysAreBooked()
    {
        // Default allowances. Mon 2026-01-12 full Feriefri "Dentist"; range Mon 12 .. Tue 13: Mon as it is, Tue new.
        var data = Data([FF("2026-01-12", 1, "Dentist")]);

        var r = Bookings.BookTypedRange(data, D("2026-01-12"), D("2026-01-13"), DayType.Feriefri);

        Assert.Equal(RangeBookingOutcome.Booked, r.Outcome);
        Assert.True(r.Changed);
        Assert.Equal(1m, r.BookedDays);
        Assert.Equal([D("2026-01-12")], r.UnchangedDates);
        Assert.Equal([FF("2026-01-12", 1, "Dentist"), FF("2026-01-13")], r.Days);
    }

    [Fact]
    public void VacationRange_EveryDayAlreadyFullVacation_ChangesNothing_AndKeepsDescriptions()
    {
        // Mon 2026-01-12 full "Trip", Tue 01-13 full; range Mon 12 .. Tue 13.
        var data = Data([Vac("2026-01-12", 1, "Trip"), Vac("2026-01-13")]);

        var r = Bookings.BookTypedRange(data, D("2026-01-12"), D("2026-01-13"), DayType.Vacation);

        Assert.Equal(RangeBookingOutcome.Booked, r.Outcome);
        Assert.False(r.Changed);
        Assert.Equal(2, r.WorkingDays);
        Assert.Equal(0m, r.BookedDays);
        Assert.Equal(0m, r.LeftOutDays);
        Assert.Equal([D("2026-01-12"), D("2026-01-13")], r.UnchangedDates);
        Assert.Equal(data.Days, r.Days);
    }

    [Fact]
    public void VacationRange_MixedExistingDays_CountsOnlyTheChangedOnes()
    {
        // Mon 2026-01-12 full "Trip" (as it is), Tue 13 half vacation (→ full), Wed 14 Feriefri (→ vacation), Thu 15 free.
        var data = Data([Vac("2026-01-12", 1, "Trip"), Vac("2026-01-13", 0.5m), FF("2026-01-14")]);

        var r = Bookings.BookTypedRange(data, D("2026-01-12"), D("2026-01-15"), DayType.Vacation);

        Assert.Equal(RangeBookingOutcome.Booked, r.Outcome);
        Assert.True(r.Changed);
        Assert.Equal(4, r.WorkingDays);
        Assert.Equal(3m, r.BookedDays);
        Assert.Equal(0m, r.LeftOutDays);
        Assert.Equal([D("2026-01-12")], r.UnchangedDates);
        Assert.Equal([Vac("2026-01-12", 1, "Trip"), Vac("2026-01-13"), Vac("2026-01-14"), Vac("2026-01-15")], r.Days);
    }

    [Fact]
    public void FeriefriRange_RefusedDayWithAnExistingHalfDay_OverTheAllowance_IsLeftOutNotUnchanged()
    {
        // Allowance lowered to 0 for 2026/27 after Tue 2026-11-03 was booked as half Feriefri: not even 0.5 fits,
        // so the day is left out (its record stays), as before.
        var data = Autumn2026(0m, FF("2026-11-03", 0.5m));

        var r = Bookings.BookTypedRange(data, D("2026-11-03"), D("2026-11-03"), DayType.Feriefri);

        Assert.Equal(RangeBookingOutcome.Refused, r.Outcome);
        Assert.False(r.Changed);
        Assert.Equal([D("2026-11-03")], r.LeftOutDates);
        Assert.Empty(r.UnchangedDates);
        Assert.Equal(data.Days, r.Days);
    }

    // ---------- BookFeriefri (List tab form) with days already as booked ----------

    [Fact]
    public void BookFeriefri_EveryDayAlreadyAsBooked_WithTheSameDescription_RequestsNothing()
    {
        // Same data as the range case above; the form books Tue 3 .. Wed 4 as full days with the existing descriptions.
        var data = Autumn2026(2m, FF("2026-11-02", 0.5m), FF("2026-11-03", 0.5m, "Dentist"), FF("2026-11-04", 1, "Dentist"));

        var r = Bookings.BookFeriefri(data, D("2026-11-03"), D("2026-11-04"), 1, "Dentist");

        Assert.Equal(0m, r.RequestedDays);
        Assert.Equal(0m, r.BookedDays);
        Assert.Equal(0m, r.LeftOutDays);
        Assert.False(r.IsRefused);
        Assert.True(r.IsComplete);
        Assert.Empty(r.HalfDayDates);
        Assert.Empty(r.LeftOutDates);
        Assert.Equal([D("2026-11-03"), D("2026-11-04")], r.UnchangedDates);
        Assert.Null(r.Limit);
        Assert.Equal(data.Days, r.Days);
    }

    [Fact]
    public void BookFeriefri_SameAmountButANewDescription_IsAChange_AndIsReported()
    {
        // As above, but the form's description is "Doctor": Tue 03 stays half with the new description (reported as a
        // half day, 0.5 left out), Wed 04 stays full with the new description.
        var data = Autumn2026(2m, FF("2026-11-02", 0.5m), FF("2026-11-03", 0.5m, "Dentist"), FF("2026-11-04", 1, "Dentist"));

        var r = Bookings.BookFeriefri(data, D("2026-11-03"), D("2026-11-04"), 1, "Doctor");

        Assert.Equal(2m, r.RequestedDays);
        Assert.Equal(1.5m, r.BookedDays);
        Assert.Equal([D("2026-11-03")], r.HalfDayDates);
        Assert.Empty(r.UnchangedDates);
        Assert.Equal(Hy2026, r.Limit!.HolidayYear);
        Assert.Equal([FF("2026-11-02", 0.5m), FF("2026-11-03", 0.5m, "Doctor"), FF("2026-11-04", 1, "Doctor")], r.Days);
    }

    [Fact]
    public void BookFeriefri_HalfDayRequest_OnAnIdenticalHalfDay_IsUnchanged()
    {
        // Default allowances. Mon 2026-01-12 half Feriefri "Dentist"; the form books it again as a half day "Dentist".
        var data = Data([FF("2026-01-12", 0.5m, "Dentist")]);

        var r = Bookings.BookFeriefri(data, D("2026-01-12"), D("2026-01-12"), 0.5m, "Dentist");

        Assert.Equal(0m, r.RequestedDays);
        Assert.Equal(0m, r.BookedDays);
        Assert.False(r.IsRefused);
        Assert.Equal([D("2026-01-12")], r.UnchangedDates);
        Assert.Equal(data.Days, r.Days);
    }
}
