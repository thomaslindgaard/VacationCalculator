namespace VacationCalculator.Core;

/// <summary>The kind of day off a booked day is. A date has at most one type.</summary>
public enum DayType
{
    Vacation,
    Feriefri,
}

/// <summary>One booked day. <see cref="Amount"/> is 1 (full day) or 0.5 (half day).</summary>
public sealed record BookedDay
{
    public const decimal FullDay = 1m;
    public const decimal HalfDay = 0.5m;

    public DateOnly Date { get; init; }
    public DayType Type { get; init; } = DayType.Vacation;
    public decimal Amount { get; init; } = FullDay;
    public string Description { get; init; } = "";
}

/// <summary>Allowance and non-working days for a single calendar year.</summary>
public sealed class YearSettings
{
    public int Year { get; set; }
    public decimal AllowanceDays { get; set; }
    public decimal CarryOverDays { get; set; }
    public List<DateOnly> PublicHolidays { get; set; } = [];
}

public sealed class VacationData
{
    /// <summary>Booked days, at most one per date.</summary>
    public List<BookedDay> Days { get; set; } = [];

    public List<YearSettings> Years { get; set; } = [];

    /// <summary>
    /// Every public holiday across all years, each taken only from the settings of the year it
    /// falls in. Suitable for booking ranges that cross New Year.
    /// </summary>
    public HashSet<DateOnly> AllPublicHolidays() =>
        Years.SelectMany(y => (y.PublicHolidays ?? []).Where(h => h.Year == y.Year)).ToHashSet();
}
