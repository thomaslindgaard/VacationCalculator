namespace VacationCalculator.Core;

public enum BalanceStatus
{
    /// <summary>Every available day is used or booked.</summary>
    AllPlanned,
    /// <summary>Unbooked days remain and there is still time to book them.</summary>
    DaysToPlan,
    /// <summary>More unbooked days remain than there are free working days left in the year.</summary>
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
    decimal UsedDays,
    decimal PlannedDays,
    decimal WorkingDaysLeft,
    BalanceStatus Status)
{
    public decimal TotalDays => AllowanceDays + CarryOverDays;

    /// <summary>Days neither taken nor booked. Negative when over-booked.</summary>
    public decimal RemainingDays => TotalDays - UsedDays - PlannedDays;

    /// <summary>Days that cannot be fitted into the rest of the year, even taking every remaining free working day off.</summary>
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

    /// <summary>
    /// Balance for <paramref name="settings"/>' year. Only <see cref="DayType.Vacation"/> records on
    /// working days inside the year count: used = on or before <paramref name="today"/>, planned = after.
    /// <see cref="YearBalance.WorkingDaysLeft"/> sums, for each working day after today (or the whole
    /// year if today is before it), the part not booked: 1 if free, 0.5 if half booked, 0 if fully
    /// booked (with any day type, since a day has only one type).
    /// </summary>
    public static YearBalance Calculate(YearSettings settings, IEnumerable<BookedDay> days, DateOnly today)
    {
        var year = settings.Year;
        var holidays = (settings.PublicHolidays ?? []).ToHashSet();

        // One record per date; if the input has duplicates, the first wins.
        var byDate = new Dictionary<DateOnly, BookedDay>();
        foreach (var day in days)
        {
            if (day.Date.Year == year && IsWorkingDay(day.Date, holidays))
                byDate.TryAdd(day.Date, day);
        }

        decimal used = 0, planned = 0;
        foreach (var day in byDate.Values)
        {
            if (day.Type != DayType.Vacation)
                continue;
            if (day.Date <= today)
                used += day.Amount;
            else
                planned += day.Amount;
        }

        var yearEnd = new DateOnly(year, 12, 31);
        var firstOpenDay = today < new DateOnly(year, 1, 1) ? new DateOnly(year, 1, 1) : today.AddDays(1);
        decimal workingDaysLeft = 0;
        for (var d = firstOpenDay; d <= yearEnd; d = d.AddDays(1))
        {
            if (!IsWorkingDay(d, holidays))
                continue;
            var booked = byDate.TryGetValue(d, out var day) ? day.Amount : 0;
            workingDaysLeft += Math.Clamp(1 - booked, 0, 1);
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
}
