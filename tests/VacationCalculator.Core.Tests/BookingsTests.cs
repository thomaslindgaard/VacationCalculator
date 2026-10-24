using VacationCalculator.Core;

namespace VacationCalculator.Core.Tests;

public class BookingsTests
{
    private static readonly HashSet<DateOnly> NoHolidays = [];

    private static DateOnly D(string date) => DateOnly.Parse(date);

    private static HashSet<DateOnly> Holidays(params string[] dates) => dates.Select(DateOnly.Parse).ToHashSet();

    private static BookedDay Day(string date, decimal amount = 1, DayType type = DayType.Vacation, string description = "") =>
        new() { Date = D(date), Amount = amount, Type = type, Description = description };

    // ---- BookRange ----

    [Fact]
    public void BookRange_SkipsWeekendsAndHolidays()
    {
        // Mon 2026-12-21 .. Sun 2026-12-27, Thu 24 and Fri 25 are holidays
        var days = Bookings.BookRange([], D("2026-12-21"), D("2026-12-27"), DayType.Vacation, "Christmas", Holidays("2026-12-24", "2026-12-25"));

        Assert.Equal([D("2026-12-21"), D("2026-12-22"), D("2026-12-23")], days.Select(d => d.Date));
        Assert.All(days, d =>
        {
            Assert.Equal(1m, d.Amount);
            Assert.Equal(DayType.Vacation, d.Type);
            Assert.Equal("Christmas", d.Description);
        });
    }

    [Fact]
    public void BookRange_AcrossNewYearUsesEachYearsHolidays()
    {
        var data = new VacationData
        {
            Years =
            [
                new() { Year = 2026, PublicHolidays = [D("2026-12-31")] },
                new() { Year = 2027, PublicHolidays = [D("2027-01-01")] },
            ],
        };

        // Mon 2026-12-28 .. Fri 2027-01-08; Thu 12-31 and Fri 01-01 are holidays, 01-02/03 a weekend
        var days = Bookings.BookRange([], D("2026-12-28"), D("2027-01-08"), DayType.Vacation, "", data.AllPublicHolidays());

        Assert.Equal(
            [D("2026-12-28"), D("2026-12-29"), D("2026-12-30"),
             D("2027-01-04"), D("2027-01-05"), D("2027-01-06"), D("2027-01-07"), D("2027-01-08")],
            days.Select(d => d.Date));
    }

    [Fact]
    public void BookRange_AcceptsReversedRange()
    {
        // Wed 2026-07-08 back to Mon 2026-07-06
        var days = Bookings.BookRange([], D("2026-07-08"), D("2026-07-06"), DayType.Vacation, "", NoHolidays);

        Assert.Equal([D("2026-07-06"), D("2026-07-07"), D("2026-07-08")], days.Select(d => d.Date));
    }

    [Fact]
    public void BookRange_ReplacesExistingDaysAndKeepsOthers()
    {
        var existing = new[]
        {
            Day("2026-07-03", description: "Before"),                    // Fri, outside range
            Day("2026-07-07", 0.5m, DayType.Feriefri, "Dentist"),        // Tue, inside range
        };

        // Mon 2026-07-06 .. Wed 2026-07-08
        var days = Bookings.BookRange(existing, D("2026-07-06"), D("2026-07-08"), DayType.Vacation, "Trip", NoHolidays);

        Assert.Equal(4, days.Count);
        Assert.Equal(Day("2026-07-03", description: "Before"), days[0]);
        Assert.Equal(Day("2026-07-07", 1m, DayType.Vacation, "Trip"), days[2]);
    }

    [Fact]
    public void BookRange_DoesNotModifyTheInput()
    {
        var existing = new List<BookedDay> { Day("2026-07-07", 0.5m) }; // Tue

        Bookings.BookRange(existing, D("2026-07-06"), D("2026-07-08"), DayType.Vacation, "", NoHolidays);

        Assert.Equal([Day("2026-07-07", 0.5m)], existing);
    }

    [Fact]
    public void BookRange_OnlyWeekendBooksNothing()
    {
        // Sat 2026-07-11 .. Sun 2026-07-12
        var days = Bookings.BookRange([], D("2026-07-11"), D("2026-07-12"), DayType.Vacation, "", NoHolidays);

        Assert.Empty(days);
    }

    // ---- BookDay / BookHalfDay ----

    [Fact]
    public void BookHalfDay_CreatesHalfDayRecord()
    {
        // Mon 2026-07-06
        var days = Bookings.BookHalfDay([], D("2026-07-06"), DayType.Vacation, "Afternoon off", NoHolidays);

        Assert.Equal([Day("2026-07-06", 0.5m, description: "Afternoon off")], days);
    }

    [Fact]
    public void BookHalfDay_ReplacesFullDayOfAnotherType()
    {
        var existing = new[] { Day("2026-07-06", type: DayType.Feriefri) }; // Mon

        var days = Bookings.BookHalfDay(existing, D("2026-07-06"), DayType.Vacation, "", NoHolidays);

        Assert.Equal([Day("2026-07-06", 0.5m)], days);
    }

    [Fact]
    public void BookHalfDay_OnWeekendOrHolidayDoesNothing()
    {
        var existing = new[] { Day("2026-07-06") }; // Mon

        var onSaturday = Bookings.BookHalfDay(existing, D("2026-07-11"), DayType.Vacation, "", NoHolidays);
        var onHoliday = Bookings.BookHalfDay(existing, D("2026-12-24"), DayType.Vacation, "", Holidays("2026-12-24")); // Thu

        Assert.Equal(existing, onSaturday);
        Assert.Equal(existing, onHoliday);
    }

    [Fact]
    public void BookDay_FullDayReplacesHalfDay()
    {
        var existing = new[] { Day("2026-07-06", 0.5m) }; // Mon

        var days = Bookings.BookDay(existing, D("2026-07-06"), DayType.Vacation, 1m, "x", NoHolidays);

        Assert.Equal([Day("2026-07-06", 1m, description: "x")], days);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.25)]
    [InlineData(2)]
    [InlineData(-1)]
    public void BookDay_RejectsAmountsOtherThanFullOrHalf(double amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Bookings.BookDay([], D("2026-07-06"), DayType.Vacation, (decimal)amount, "", NoHolidays));
    }

    // ---- RemoveDays ----

    [Fact]
    public void RemoveDays_RemovesOnlyTheGivenDates()
    {
        // Mon 2026-07-06 .. Fri 2026-07-10
        var existing = Bookings.BookRange([], D("2026-07-06"), D("2026-07-10"), DayType.Vacation, "", NoHolidays);

        var days = Bookings.RemoveDays(existing, [D("2026-07-07"), D("2026-07-09"), D("2026-08-01")]);

        Assert.Equal([D("2026-07-06"), D("2026-07-08"), D("2026-07-10")], days.Select(d => d.Date));
        Assert.Equal(5, existing.Count);
    }

    // ---- GroupPeriods ----

    [Fact]
    public void GroupPeriods_WeekendDoesNotBreakAPeriod()
    {
        // Thu 2026-07-09 .. Tue 2026-07-14 (weekend 11/12 in between)
        var days = Bookings.BookRange([], D("2026-07-09"), D("2026-07-14"), DayType.Vacation, "Summer", NoHolidays);

        var period = Assert.Single(Bookings.GroupPeriods(days, 2026, NoHolidays));

        Assert.Equal(D("2026-07-09"), period.Start);
        Assert.Equal(D("2026-07-14"), period.End);
        Assert.Equal(DayType.Vacation, period.Type);
        Assert.Equal("Summer", period.Description);
        Assert.Equal(4m, period.Amount);
        Assert.Equal([D("2026-07-09"), D("2026-07-10"), D("2026-07-13"), D("2026-07-14")], period.Dates);
    }

    [Fact]
    public void GroupPeriods_HolidaysAndWeekendDoNotBreakAPeriod()
    {
        // Wed 2026-12-23, then Thu 24 / Fri 25 holidays and the weekend, then Mon 2026-12-28
        var holidays = Holidays("2026-12-24", "2026-12-25");
        var days = new[] { Day("2026-12-23"), Day("2026-12-28") };

        var period = Assert.Single(Bookings.GroupPeriods(days, 2026, holidays));

        Assert.Equal(D("2026-12-23"), period.Start);
        Assert.Equal(D("2026-12-28"), period.End);
        Assert.Equal(2m, period.Amount);
    }

    [Fact]
    public void GroupPeriods_FreeWorkingDaySplitsAPeriod()
    {
        // Mon 2026-07-06 .. Wed 07-08 booked, Thu 07-09 free, Fri 07-10 booked
        var days = Bookings.BookRange([], D("2026-07-06"), D("2026-07-08"), DayType.Vacation, "Summer", NoHolidays)
            .Append(Day("2026-07-10", description: "Summer"));

        var periods = Bookings.GroupPeriods(days, 2026, NoHolidays);

        Assert.Equal(2, periods.Count);
        Assert.Equal((D("2026-07-06"), D("2026-07-08"), 3m), (periods[0].Start, periods[0].End, periods[0].Amount));
        Assert.Equal((D("2026-07-10"), D("2026-07-10"), 1m), (periods[1].Start, periods[1].End, periods[1].Amount));
    }

    [Fact]
    public void GroupPeriods_DifferentTypeOrDescriptionSplitsAPeriod()
    {
        var days = new[]
        {
            Day("2026-07-06", description: "A"),                    // Mon
            Day("2026-07-07", description: "B"),                    // Tue
            Day("2026-07-08", type: DayType.Feriefri, description: "B"), // Wed
        };

        var periods = Bookings.GroupPeriods(days, 2026, NoHolidays);

        Assert.Equal(3, periods.Count);
        Assert.Equal(DayType.Feriefri, periods[2].Type);
    }

    [Fact]
    public void GroupPeriods_HalfDaysJoinThePeriodWithTheirAmount()
    {
        // Mon 2026-07-06 .. Wed 07-08 full, Thu 07-09 half
        var days = Bookings.BookRange([], D("2026-07-06"), D("2026-07-08"), DayType.Vacation, "", NoHolidays)
            .Append(Day("2026-07-09", 0.5m));

        var period = Assert.Single(Bookings.GroupPeriods(days, 2026, NoHolidays));

        Assert.Equal(3.5m, period.Amount);
        Assert.Equal(D("2026-07-09"), period.End);
    }

    [Fact]
    public void GroupPeriods_PeriodAcrossNewYearShowsInBothYearsWithThatYearsAmount()
    {
        // Mon 2026-12-28 .. Fri 2027-01-08; Thu 12-31 and Fri 01-01 holidays
        var holidays = Holidays("2026-12-31", "2027-01-01");
        var days = Bookings.BookRange([], D("2026-12-28"), D("2027-01-08"), DayType.Vacation, "", holidays);

        var p2026 = Assert.Single(Bookings.GroupPeriods(days, 2026, holidays));
        var p2027 = Assert.Single(Bookings.GroupPeriods(days, 2027, holidays));

        Assert.Equal((D("2026-12-28"), D("2027-01-08")), (p2026.Start, p2026.End));
        Assert.Equal(3m, p2026.Amount);
        Assert.Equal(5m, p2027.Amount);
        Assert.Equal(8, p2026.Dates.Count);
        Assert.Equal(p2026.Dates, p2027.Dates);
    }

    [Fact]
    public void GroupPeriods_ExcludesPeriodsOutsideTheYearAndOrdersByStart()
    {
        var days = new[]
        {
            Day("2026-08-03", description: "Aug"), // Mon
            Day("2025-07-07"),                     // Mon, other year
            Day("2026-02-02", description: "Feb"), // Mon
        };

        var periods = Bookings.GroupPeriods(days, 2026, NoHolidays);

        Assert.Equal(["Feb", "Aug"], periods.Select(p => p.Description));
    }

    [Fact]
    public void GroupPeriods_RecordOnHolidayIsListedButNotCounted()
    {
        // Wed 2026-12-23 and Thu 2026-12-24 booked; 24 became a holiday later
        var days = new[] { Day("2026-12-23"), Day("2026-12-24") };

        var period = Assert.Single(Bookings.GroupPeriods(days, 2026, Holidays("2026-12-24")));

        Assert.Equal(1m, period.Amount);
        Assert.Equal([D("2026-12-23"), D("2026-12-24")], period.Dates);
    }

    [Fact]
    public void GroupPeriods_EmptyInputGivesNoPeriods()
    {
        Assert.Empty(Bookings.GroupPeriods([], 2026, NoHolidays));
    }

    // ---- DateOnly.MinValue / MaxValue boundaries ----

    [Fact]
    public void BookRange_EndingOnMaxValueDoesNotThrow()
    {
        // Mon 9999-12-27 .. Fri 9999-12-31 (DateOnly.MaxValue)
        var days = Bookings.BookRange([], D("9999-12-27"), DateOnly.MaxValue, DayType.Vacation, "End of time", NoHolidays);

        Assert.Equal(
            [D("9999-12-27"), D("9999-12-28"), D("9999-12-29"), D("9999-12-30"), D("9999-12-31")],
            days.Select(d => d.Date));
    }

    [Fact]
    public void BookRange_SingleDayOnMaxValue()
    {
        // Fri 9999-12-31 in both From and To, as typed in the UI
        var days = Bookings.BookRange([], DateOnly.MaxValue, DateOnly.MaxValue, DayType.Vacation, "", NoHolidays);

        var day = Assert.Single(days);
        Assert.Equal(DateOnly.MaxValue, day.Date);
        Assert.Equal(1m, day.Amount);
    }

    [Fact]
    public void BookRange_ReversedRangeEndingOnMaxValue()
    {
        // Fri 9999-12-31 .. Thu 9999-12-30, given in reverse order
        var days = Bookings.BookRange([], DateOnly.MaxValue, D("9999-12-30"), DayType.Vacation, "", NoHolidays);

        Assert.Equal([D("9999-12-30"), D("9999-12-31")], days.Select(d => d.Date));
    }

    [Fact]
    public void BookRange_MaxValueAsHolidayIsSkipped()
    {
        // Thu 9999-12-30 .. Fri 9999-12-31, Fri is a holiday
        var days = Bookings.BookRange([], D("9999-12-30"), DateOnly.MaxValue, DayType.Vacation, "", Holidays("9999-12-31"));

        Assert.Equal([D("9999-12-30")], days.Select(d => d.Date));
    }

    [Fact]
    public void BookHalfDay_OnMaxValue()
    {
        // Fri 9999-12-31
        var days = Bookings.BookHalfDay([], DateOnly.MaxValue, DayType.Vacation, "Half", NoHolidays);

        var day = Assert.Single(days);
        Assert.Equal(DateOnly.MaxValue, day.Date);
        Assert.Equal(0.5m, day.Amount);
    }

    [Fact]
    public void GroupPeriods_PeriodEndingOnMaxValue()
    {
        // Thu 9999-12-23 .. Fri 9999-12-31 booked except the weekend 12-25/26; Fri 12-24 is a holiday
        var holidays = Holidays("9999-12-24");
        var days = Bookings.BookRange([], D("9999-12-23"), DateOnly.MaxValue, DayType.Vacation, "Xmas", holidays);
        days = Bookings.BookHalfDay(days, DateOnly.MaxValue, DayType.Vacation, "Xmas", holidays);

        var period = Assert.Single(Bookings.GroupPeriods(days, 9999, holidays));

        Assert.Equal(D("9999-12-23"), period.Start);
        Assert.Equal(DateOnly.MaxValue, period.End);
        Assert.Equal(5.5m, period.Amount); // 23, 27, 28, 29, 30 full + 31 half
        Assert.Equal(
            [D("9999-12-23"), D("9999-12-27"), D("9999-12-28"), D("9999-12-29"), D("9999-12-30"), D("9999-12-31")],
            period.Dates);
    }

    [Fact]
    public void GroupPeriods_FreeWorkingDayBeforeMaxValueSplitsPeriod()
    {
        // Wed 9999-12-29 booked, Thu 12-30 free, Fri 12-31 booked
        var days = new[] { Day("9999-12-29"), Day("9999-12-31") };

        var periods = Bookings.GroupPeriods(days, 9999, NoHolidays);

        Assert.Equal(2, periods.Count);
        Assert.Equal(DateOnly.MaxValue, periods[1].Start);
        Assert.Equal(DateOnly.MaxValue, periods[1].End);
    }

    [Fact]
    public void RemoveDays_PeriodEndingOnMaxValue()
    {
        // Mon 9999-12-27 .. Fri 9999-12-31, plus an unrelated Mon 9999-12-20
        var days = Bookings.BookRange([Day("9999-12-20")], D("9999-12-27"), DateOnly.MaxValue, DayType.Vacation, "", NoHolidays);
        var period = Bookings.GroupPeriods(days, 9999, NoHolidays).Single(p => p.End == DateOnly.MaxValue);

        var remaining = Bookings.RemoveDays(days, period.Dates);

        Assert.Equal([D("9999-12-20")], remaining.Select(d => d.Date));
    }

    [Fact]
    public void BookRange_StartingOnMinValue()
    {
        // Mon 0001-01-01 (DateOnly.MinValue) .. Sun 0001-01-07
        var days = Bookings.BookRange([], D("0001-01-07"), DateOnly.MinValue, DayType.Vacation, "", NoHolidays);

        Assert.Equal(
            [D("0001-01-01"), D("0001-01-02"), D("0001-01-03"), D("0001-01-04"), D("0001-01-05")],
            days.Select(d => d.Date));

        var period = Assert.Single(Bookings.GroupPeriods(days, 1, NoHolidays));
        Assert.Equal(DateOnly.MinValue, period.Start);
        Assert.Equal(5m, period.Amount);
        Assert.Empty(Bookings.RemoveDays(days, period.Dates));
    }
}
