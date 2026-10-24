namespace VacationCalculator.Core;

/// <summary>A booked period of vacation, inclusive of both start and end dates.</summary>
public sealed class VacationEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
    public string Description { get; set; } = "";
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
    public List<VacationEntry> Entries { get; set; } = [];
    public List<YearSettings> Years { get; set; } = [];
}
