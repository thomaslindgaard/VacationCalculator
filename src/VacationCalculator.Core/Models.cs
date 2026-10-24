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

/// <summary>Non-working days for a single calendar year.</summary>
public sealed class YearSettings
{
    public int Year { get; set; }
    public List<DateOnly> PublicHolidays { get; set; } = [];
}

/// <summary>
/// User-entered settings for one holiday year (ferieår), identified by the calendar year it starts in:
/// holiday year 2025 runs 1 Sep 2025 to 31 Aug 2026 (see <see cref="HolidayYear"/>).
/// </summary>
public sealed class HolidayYearSettings
{
    public int StartYear { get; set; }

    /// <summary>
    /// 5th-week days transferred to the next holiday year by agreement. Only takes effect when
    /// <see cref="VacationData.FifthWeekTransferAllowed"/> is on, and is capped at the year's unused
    /// 5th-week days (see <see cref="HolidayYearBalance.MaxTransferToNext"/>).
    /// </summary>
    public decimal TransferredToNext { get; set; }
}

public sealed class VacationData
{
    /// <summary>Booked days, at most one per date.</summary>
    public List<BookedDay> Days { get; set; } = [];

    /// <summary>Per calendar year settings (public holidays).</summary>
    public List<YearSettings> Years { get; set; } = [];

    /// <summary>
    /// First day of employment; accrual starts on this date. Null means employed since before any
    /// holiday year being tracked.
    /// </summary>
    public DateOnly? EmployedSince { get; set; }

    /// <summary>Whether unused 5th-week days may be transferred to the next holiday year by agreement.</summary>
    public bool FifthWeekTransferAllowed { get; set; }

    /// <summary>Per holiday year settings, at most one per <see cref="HolidayYearSettings.StartYear"/>.</summary>
    public List<HolidayYearSettings> HolidayYears { get; set; } = [];

    /// <summary>
    /// Every public holiday across all years, each taken only from the settings of the year it
    /// falls in. Suitable for booking ranges that cross New Year.
    /// </summary>
    public HashSet<DateOnly> AllPublicHolidays() =>
        (Years ?? []).SelectMany(y => (y.PublicHolidays ?? []).Where(h => h.Year == y.Year)).ToHashSet();

    /// <summary>The settings for the holiday year starting in <paramref name="startYear"/>, or null if none are stored.</summary>
    public HolidayYearSettings? FindHolidayYear(int startYear) =>
        (HolidayYears ?? []).FirstOrDefault(h => h.StartYear == startYear);

    /// <summary>The settings for the holiday year starting in <paramref name="startYear"/>, adding them if missing.</summary>
    public HolidayYearSettings GetOrAddHolidayYear(int startYear)
    {
        HolidayYears ??= [];
        var existing = FindHolidayYear(startYear);
        if (existing is not null)
            return existing;
        var settings = new HolidayYearSettings { StartYear = startYear };
        HolidayYears.Add(settings);
        return settings;
    }
}
