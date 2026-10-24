namespace VacationCalculator.Core;

public enum BalanceStatus
{
    /// <summary>Every available day is used or booked.</summary>
    AllPlanned,
    /// <summary>Unbooked days remain and there is still time to book them.</summary>
    DaysToPlan,
    /// <summary>More unbooked days remain than there are working days left in the year.</summary>
    DaysWillBeLost,
    /// <summary>More days are booked than the year's allowance.</summary>
    OverBooked,
    /// <summary>The year is over and some days were never used.</summary>
    Expired,
}

public sealed record YearBalance(
    int Year,
    decimal AllowanceDays,
    decimal CarryOverDays,
    int UsedDays,
    int PlannedDays,
    int WorkingDaysLeft,
    BalanceStatus Status)
{
    public decimal TotalDays => AllowanceDays + CarryOverDays;

    /// <summary>Days neither taken nor booked. Negative when over-booked.</summary>
    public decimal RemainingDays => TotalDays - UsedDays - PlannedDays;

    /// <summary>Days that cannot be fitted into the rest of the year, even taking every remaining working day off.</summary>
    public decimal DaysAtRisk => Math.Max(0, RemainingDays - WorkingDaysLeft);
}

public static class BalanceCalculator
{
    public static bool IsWorkingDay(DateOnly date, ISet<DateOnly> holidays) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !holidays.Contains(date);

    /// <summary>Counts working days in the inclusive range [start, end].</summary>
    public static int CountWorkingDays(DateOnly start, DateOnly end, ISet<DateOnly> holidays)
    {
        var count = 0;
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (IsWorkingDay(d, holidays))
                count++;
        }
        return count;
    }

    /// <summary>Working days within <paramref name="year"/> covered by <paramref name="entry"/>.</summary>
    public static int CountWorkingDaysInYear(VacationEntry entry, int year, ISet<DateOnly> holidays)
    {
        var (start, end) = Clamp(entry.Start, entry.End, year);
        return start > end ? 0 : CountWorkingDays(start, end, holidays);
    }

    public static YearBalance Calculate(YearSettings settings, IEnumerable<VacationEntry> entries, DateOnly today)
    {
        var year = settings.Year;
        var holidays = settings.PublicHolidays.ToHashSet();

        // Collect distinct dates so overlapping entries are not counted twice.
        var vacationDays = new HashSet<DateOnly>();
        foreach (var entry in entries)
        {
            var (start, end) = Clamp(entry.Start, entry.End, year);
            for (var d = start; d <= end; d = d.AddDays(1))
            {
                if (IsWorkingDay(d, holidays))
                    vacationDays.Add(d);
            }
        }

        var used = vacationDays.Count(d => d <= today);
        var planned = vacationDays.Count - used;

        var yearEnd = new DateOnly(year, 12, 31);
        var firstOpenDay = today < new DateOnly(year, 1, 1) ? new DateOnly(year, 1, 1) : today.AddDays(1);
        var workingDaysLeft = 0;
        for (var d = firstOpenDay; d <= yearEnd; d = d.AddDays(1))
        {
            if (IsWorkingDay(d, holidays) && !vacationDays.Contains(d))
                workingDaysLeft++;
        }

        var remaining = settings.AllowanceDays + settings.CarryOverDays - used - planned;
        var status = remaining switch
        {
            < 0 => BalanceStatus.OverBooked,
            0 => BalanceStatus.AllPlanned,
            _ when today > yearEnd => BalanceStatus.Expired,
            _ when remaining > workingDaysLeft => BalanceStatus.DaysWillBeLost,
            _ => BalanceStatus.DaysToPlan,
        };

        return new YearBalance(year, settings.AllowanceDays, settings.CarryOverDays, used, planned, workingDaysLeft, status);
    }

    private static (DateOnly Start, DateOnly End) Clamp(DateOnly start, DateOnly end, int year)
    {
        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);
        return (start < yearStart ? yearStart : start, end > yearEnd ? yearEnd : end);
    }
}
