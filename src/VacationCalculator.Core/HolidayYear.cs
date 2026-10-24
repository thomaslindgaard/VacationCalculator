namespace VacationCalculator.Core;

/// <summary>
/// A holiday year (ferieår) under the Danish Holiday Act, identified by the calendar year it starts in.
/// Holiday year 2025 earns days from 1 Sep 2025 to 31 Aug 2026, and its days can be taken from
/// 1 Sep 2025 until the deadline 31 Dec 2026 (the holiday-taking period, ferieafholdelsesperiode).
/// </summary>
public readonly record struct HolidayYear(int StartYear)
{
    /// <summary>Smallest start year whose dates (1 Sep .. deadline) are all representable as <see cref="DateOnly"/>.</summary>
    public const int MinStartYear = 1;

    /// <summary>Largest start year whose dates (1 Sep .. deadline 31 Dec of the next year) are all representable as <see cref="DateOnly"/>.</summary>
    public const int MaxStartYear = 9998;

    /// <summary>First day of the holiday year and of its holiday-taking period (1 September).</summary>
    public DateOnly Start => new(StartYear, 9, 1);

    /// <summary>Last day on which days are earned (31 August of the following year).</summary>
    public DateOnly End => new(StartYear + 1, 8, 31);

    /// <summary>Last day the year's days can be taken (31 December of the following year).</summary>
    public DateOnly Deadline => new(StartYear + 1, 12, 31);

    public HolidayYear Previous => new(StartYear - 1);
    public HolidayYear Next => new(StartYear + 1);

    /// <summary>Whether <paramref name="date"/> is in the earning period, 1 Sep .. 31 Aug.</summary>
    public bool Contains(DateOnly date) => date >= Start && date <= End;

    /// <summary>Whether <paramref name="date"/> is in the holiday-taking period, 1 Sep .. 31 Dec of the following year.</summary>
    public bool IsInTakingPeriod(DateOnly date) => date >= Start && date <= Deadline;

    /// <summary>The holiday year whose earning period contains <paramref name="date"/>.</summary>
    public static HolidayYear Containing(DateOnly date) => new(date.Month >= 9 ? date.Year : date.Year - 1);

    /// <summary>The holiday year whose deadline is 31 December of <paramref name="calendarYear"/>.</summary>
    public static HolidayYear WithDeadlineIn(int calendarYear) => new(calendarYear - 1);

    /// <summary>
    /// The holiday years whose holiday-taking period contains <paramref name="date"/>, oldest first:
    /// one from January to August, two from September to December.
    /// </summary>
    public static IReadOnlyList<HolidayYear> OpenOn(DateOnly date)
    {
        var current = Containing(date);
        return date.Month >= 9 ? [current.Previous, current] : [current];
    }

    /// <summary>
    /// The two holiday years whose holiday-taking period overlaps <paramref name="calendarYear"/>, oldest first:
    /// the one with its deadline that year and the one starting that September.
    /// </summary>
    public static IReadOnlyList<HolidayYear> InCalendarYear(int calendarYear) =>
        [new(calendarYear - 1), new(calendarYear)];

    /// <summary>E.g. "2025/26".</summary>
    public override string ToString() => $"{StartYear}/{(StartYear + 1) % 100:00}";
}

/// <summary>Accrual of vacation days (optjening): 25/12 days per month of employment in a holiday year.</summary>
public static class Accrual
{
    public const decimal DaysPerFullYear = 25m;

    /// <summary>Days of a year's own earnings within the first four weeks; anything above is the 5th week.</summary>
    public const decimal FourWeeks = 20m;

    /// <summary>The most 5th-week days a year can have (25 - 20); also the part of a transfer limit the Holiday Act gives.</summary>
    public const decimal FifthWeek = DaysPerFullYear - FourWeeks;

    /// <summary>Days the holiday year earns in total by 31 August (25 for a full year).</summary>
    public static decimal EarnableDays(HolidayYear year, DateOnly? employedSince) =>
        EarnedAsOf(year, employedSince, year.End.AddDays(1));

    /// <summary>
    /// Days of <paramref name="year"/> that are usable on <paramref name="date"/>: the accrual of every month
    /// of the holiday year that ended before <paramref name="date"/> (days earned in a month are usable
    /// from the 1st of the next month). With <paramref name="employedSince"/>, months before it earn
    /// nothing and its own month earns in proportion to the days employed in it.
    /// The exact amount is rounded once to 2 decimals (midpoint away from zero), so n whole months
    /// give round(25 n / 12, 2) and a full year gives exactly 25.
    /// </summary>
    public static decimal EarnedAsOf(HolidayYear year, DateOnly? employedSince, DateOnly date)
    {
        var fullMonths = 0;
        int partialDays = 0, partialMonthLength = 1;

        for (var i = 0; i < 12; i++)
        {
            var monthStart = year.Start.AddMonths(i);
            var nextMonth = monthStart.AddMonths(1);
            if (nextMonth > date)
                break;

            if (employedSince is not { } since || since <= monthStart)
            {
                fullMonths++;
            }
            else if (since < nextMonth)
            {
                partialMonthLength = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
                partialDays = nextMonth.DayNumber - since.DayNumber;
            }
        }

        var exact = DaysPerFullYear * (fullMonths * partialMonthLength + partialDays) / (12m * partialMonthLength);
        return Math.Round(exact, 2, MidpointRounding.AwayFromZero);
    }
}
