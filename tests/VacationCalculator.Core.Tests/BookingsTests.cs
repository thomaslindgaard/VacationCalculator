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
        // Mon 9999-12-27 .. Fri 9999-12-31 (DateOnly.MaxValue, Nytårsaftensdag, skipped)
        var days = Bookings.BookRange([], D("9999-12-27"), DateOnly.MaxValue, DayType.Vacation, "End of time", NoHolidays);

        Assert.Equal(
            [D("9999-12-27"), D("9999-12-28"), D("9999-12-29"), D("9999-12-30")],
            days.Select(d => d.Date));
    }

    [Fact]
    public void BookRange_SingleDayOnMaxValue_IsAHolidayAndBooksNothing()
    {
        // Fri 9999-12-31 (Nytårsaftensdag) in both From and To, as typed in the UI: no throw, nothing booked
        var days = Bookings.BookRange([], DateOnly.MaxValue, DateOnly.MaxValue, DayType.Vacation, "", NoHolidays);

        Assert.Empty(days);
    }

    [Fact]
    public void BookRange_ReversedRangeEndingOnMaxValue()
    {
        // Fri 9999-12-31 .. Thu 9999-12-30, given in reverse order; Fri is Nytårsaftensdag
        var days = Bookings.BookRange([], DateOnly.MaxValue, D("9999-12-30"), DayType.Vacation, "", NoHolidays);

        Assert.Equal([D("9999-12-30")], days.Select(d => d.Date));
    }

    [Fact]
    public void BookRange_MaxValueAsHolidayIsSkipped()
    {
        // Thu 9999-12-30 .. Fri 9999-12-31, Fri is a holiday
        var days = Bookings.BookRange([], D("9999-12-30"), DateOnly.MaxValue, DayType.Vacation, "", Holidays("9999-12-31"));

        Assert.Equal([D("9999-12-30")], days.Select(d => d.Date));
    }

    [Fact]
    public void BookHalfDay_OnMaxValue_IsAHolidayAndBooksNothing()
    {
        // Fri 9999-12-31 (Nytårsaftensdag): no throw, nothing booked
        var days = Bookings.BookHalfDay([], DateOnly.MaxValue, DayType.Vacation, "Half", NoHolidays);

        Assert.Empty(days);
    }

    [Fact]
    public void BookHalfDay_OnLastWorkingDayBeforeMaxValue()
    {
        // Thu 9999-12-30
        var day = Assert.Single(Bookings.BookHalfDay([], D("9999-12-30"), DayType.Vacation, "Half", NoHolidays));

        Assert.Equal(D("9999-12-30"), day.Date);
        Assert.Equal(0.5m, day.Amount);
    }

    [Fact]
    public void GroupPeriods_PeriodEndingOnMaxValue()
    {
        // Thu 9999-12-23 .. Thu 9999-12-30 booked except Fri 12-24 (Juleaftensdag) and the weekend 12-25/26, plus a
        // stored half-day record on Fri 9999-12-31 (Nytårsaftensdag, e.g. from an old file): it stays in the period but
        // isn't counted
        var days = Bookings.BookRange([], D("9999-12-23"), DateOnly.MaxValue, DayType.Vacation, "Xmas", NoHolidays);
        days.Add(new BookedDay { Date = DateOnly.MaxValue, Amount = 0.5m, Description = "Xmas" });

        var period = Assert.Single(Bookings.GroupPeriods(days, 9999, NoHolidays));

        Assert.Equal(D("9999-12-23"), period.Start);
        Assert.Equal(DateOnly.MaxValue, period.End);
        Assert.Equal(5m, period.Amount); // 23, 27, 28, 29, 30 full; 31 is a holiday
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
        // Mon 9999-12-27 .. Thu 9999-12-30 booked plus a stored record on Fri 9999-12-31 (a holiday), and an unrelated
        // Mon 9999-12-20
        var days = Bookings.BookRange(
            [Day("9999-12-20"), Day("9999-12-31")], D("9999-12-27"), DateOnly.MaxValue, DayType.Vacation, "", NoHolidays);
        var period = Bookings.GroupPeriods(days, 9999, NoHolidays).Single(p => p.End == DateOnly.MaxValue);

        var remaining = Bookings.RemoveDays(days, period.Dates);

        Assert.Equal([D("9999-12-20")], remaining.Select(d => d.Date));
    }

    [Fact]
    public void BookRange_StartingOnMinValue()
    {
        // Mon 0001-01-01 (DateOnly.MinValue, Nytårsdag, skipped) .. Sun 0001-01-07
        var days = Bookings.BookRange([], D("0001-01-07"), DateOnly.MinValue, DayType.Vacation, "", NoHolidays);

        Assert.Equal(
            [D("0001-01-02"), D("0001-01-03"), D("0001-01-04"), D("0001-01-05")],
            days.Select(d => d.Date));

        var period = Assert.Single(Bookings.GroupPeriods(days, 1, NoHolidays));
        Assert.Equal(D("0001-01-02"), period.Start);
        Assert.Equal(4m, period.Amount);
        Assert.Empty(Bookings.RemoveDays(days, period.Dates));
    }

    // ---- Range limit: at most one year ----

    [Fact]
    public void MaxRangeYears_IsOne()
    {
        Assert.Equal(1, Bookings.MaxRangeYears);
    }

    [Theory]
    [InlineData("2026-01-01", "2026-12-31")]
    [InlineData("2026-03-15", "2027-03-14")]
    [InlineData("2027-03-01", "2028-02-29")] // day before 2028-03-01, includes the leap day
    [InlineData("2028-02-29", "2029-02-28")] // leap-day start: 28 Feb is the latest end
    [InlineData("2028-03-01", "2029-02-28")]
    [InlineData("0001-01-01", "0001-12-31")]
    [InlineData("9998-12-31", "9999-12-30")]
    [InlineData("9999-01-01", "9999-12-31")]
    [InlineData("9999-12-31", "9999-12-31")]
    public void LatestAllowedEnd_IsTheDayBeforeTheSameDateNextYear(string start, string latest)
    {
        Assert.Equal(D(latest), Bookings.LatestAllowedEnd(D(start)));
    }

    [Theory]
    [InlineData("2026-01-01", "2026-12-31", true)]
    [InlineData("2026-01-01", "2027-01-01", false)]
    [InlineData("2026-03-15", "2027-03-14", true)]
    [InlineData("2026-03-15", "2027-03-15", false)]
    [InlineData("2027-03-14", "2026-03-15", true)]   // reversed
    [InlineData("2027-03-15", "2026-03-15", false)]  // reversed
    [InlineData("2028-02-29", "2029-02-28", true)]
    [InlineData("2028-02-29", "2029-03-01", false)]
    [InlineData("2026-07-06", "2026-07-06", true)]   // single day
    [InlineData("0001-01-01", "0001-12-31", true)]
    [InlineData("0001-01-01", "0002-01-01", false)]
    [InlineData("9999-01-01", "9999-12-31", true)]
    [InlineData("9998-12-31", "9999-12-31", false)]
    [InlineData("0001-01-01", "9999-12-31", false)]
    [InlineData("9999-12-31", "0001-01-01", false)]
    public void IsRangeWithinLimit(string start, string end, bool expected)
    {
        Assert.Equal(expected, Bookings.IsRangeWithinLimit(D(start), D(end)));
    }

    [Fact]
    public void BookRange_ExactlyOneCalendarYearIsBooked()
    {
        // Thu 2026-01-01 .. Thu 2026-12-31: 365 days = 52 weeks + one extra Thu -> 260 + 1 = 261 weekdays, less 9 Danish
        // holidays on weekdays (Thu 01-01, Thu 04-02, Fri 04-03, Mon 04-06, Thu 05-14, Mon 05-25, Thu 12-24, Fri 12-25,
        // Thu 12-31) -> 252
        var days = Bookings.BookRange([], D("2026-01-01"), D("2026-12-31"), DayType.Vacation, "", NoHolidays);

        Assert.Equal(252, days.Count);
        Assert.Equal(D("2026-01-02"), days[0].Date);
        Assert.Equal(D("2026-12-30"), days[^1].Date);
    }

    [Fact]
    public void BookRange_OneYearAcrossNewYearIsBooked()
    {
        // Sun 2026-03-15 .. Sun 2027-03-14: 365 days = 52 weeks + one extra Sun -> 260 weekdays, less 9 Danish holidays
        // on weekdays (2026: 04-02, 04-03, 04-06, 05-14, 05-25, 12-24, 12-25, 12-31; 2027: Fri 01-01) -> 251
        var days = Bookings.BookRange([], D("2026-03-15"), D("2027-03-14"), DayType.Vacation, "", NoHolidays);

        Assert.Equal(251, days.Count);
        Assert.Equal(D("2026-03-16"), days[0].Date);  // Mon
        Assert.Equal(D("2027-03-12"), days[^1].Date); // Fri
    }

    [Fact]
    public void BookRange_OneYearReversedIsBooked()
    {
        // Sun 2027-03-14 back to Sun 2026-03-15, same 251 working days
        var days = Bookings.BookRange([], D("2027-03-14"), D("2026-03-15"), DayType.Vacation, "", NoHolidays);

        Assert.Equal(251, days.Count);
    }

    [Fact]
    public void BookRange_FromLeapDayToFeb28IsBooked()
    {
        // Tue 2028-02-29 .. Wed 2029-02-28: 366 days = 52 weeks + Tue, Wed -> 262 weekdays, less 8 Danish holidays on
        // weekdays (2028: Thu 04-13, Fri 04-14, Mon 04-17, Thu 05-25, Mon 06-05, Mon 12-25, Tue 12-26; 2029: Mon 01-01) -> 254
        var days = Bookings.BookRange([], D("2028-02-29"), D("2029-02-28"), DayType.Vacation, "", NoHolidays);

        Assert.Equal(254, days.Count);
        Assert.Equal(D("2028-02-29"), days[0].Date);
        Assert.Equal(D("2029-02-28"), days[^1].Date);
    }

    [Theory]
    [InlineData("2026-01-01", "2027-01-01")] // Thu .. Fri, one day too many
    [InlineData("2027-01-01", "2026-01-01")] // reversed
    [InlineData("2026-03-15", "2027-03-15")] // Sun .. Mon
    [InlineData("2028-02-29", "2029-03-01")] // Tue .. Thu
    [InlineData("0001-01-01", "0002-01-01")] // Mon .. Tue
    [InlineData("9998-12-31", "9999-12-31")] // Fri .. Fri (DateOnly.MaxValue)
    public void BookRange_OneDayMoreThanAYearIsRefused(string start, string end)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Bookings.BookRange([], D(start), D(end), DayType.Vacation, "", NoHolidays));

        Assert.Contains("at most 1 year", ex.Message);
    }

    [Fact]
    public void BookRange_WholeYear9999EndingOnMaxValueIsBooked()
    {
        // Fri 9999-01-01 .. Fri 9999-12-31 (DateOnly.MaxValue): 52 weeks + one extra Fri -> 261 weekdays. Easter 9999 is
        // Sun 03-28, so 8 Danish holidays fall on weekdays (Fri 01-01, Thu 03-25, Fri 03-26, Mon 03-29, Thu 05-06,
        // Mon 05-17, Fri 12-24, Fri 12-31) -> 253, the last on Thu 12-30
        var days = Bookings.BookRange([], D("9999-01-01"), DateOnly.MaxValue, DayType.Vacation, "", NoHolidays);

        Assert.Equal(253, days.Count);
        Assert.Equal(D("9999-12-30"), days[^1].Date);
    }

    [Fact]
    public void BookRange_WholeYear1StartingOnMinValueIsBooked()
    {
        // Mon 0001-01-01 (DateOnly.MinValue) .. Mon 0001-12-31: 52 weeks + one extra Mon -> 261 weekdays. Easter 1 is
        // Sun 04-01 (proleptic Gregorian), so 10 Danish holidays fall on weekdays (Mon 01-01, Thu 03-29, Fri 03-30,
        // Mon 04-02, Thu 05-10, Mon 05-21, Mon 12-24, Tue 12-25, Wed 12-26, Mon 12-31) -> 251, the first on Tue 01-02
        var days = Bookings.BookRange([], DateOnly.MinValue, D("0001-12-31"), DayType.Vacation, "", NoHolidays);

        Assert.Equal(251, days.Count);
        Assert.Equal(D("0001-01-02"), days[0].Date);
    }

    [Fact]
    public void BookRange_WholeDateRangeIsRefusedQuicklyAndInputIsUntouched()
    {
        var existing = new List<BookedDay> { Day("2026-07-07") }; // Tue
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Assert.Throws<ArgumentException>(() =>
            Bookings.BookRange(existing, DateOnly.MinValue, DateOnly.MaxValue, DayType.Vacation, "", NoHolidays));
        Assert.Throws<ArgumentException>(() =>
            Bookings.BookRange(existing, DateOnly.MaxValue, DateOnly.MinValue, DayType.Vacation, "", NoHolidays));

        watch.Stop();
        Assert.True(watch.ElapsedMilliseconds < 1000, $"Took {watch.ElapsedMilliseconds} ms");
        Assert.Equal([Day("2026-07-07")], existing);
    }
}
