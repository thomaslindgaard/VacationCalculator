namespace VacationCalculator.Core;

/// <summary>
/// A run of consecutive booked days with the same type and description, for display.
/// Weekends and public holidays between booked days don't break a period; a free working day does.
/// </summary>
public sealed record BookedPeriod(
    DateOnly Start,
    DateOnly End,
    DayType Type,
    string Description,
    decimal Amount,
    IReadOnlyList<DateOnly> Dates);

/// <summary>
/// Pure booking operations on a list of <see cref="BookedDay"/> records. Each returns a new list
/// sorted by date with at most one record per date; the input is not modified.
/// </summary>
/// <remarks>
/// <c>holidays</c> is the set of public holidays across all relevant years
/// (see <see cref="VacationData.AllPublicHolidays"/>), so ranges can cross New Year.
/// </remarks>
public static class Bookings
{
    /// <summary>
    /// Books every working day in the inclusive range as a full day of <paramref name="type"/>.
    /// Weekends and holidays are skipped. Dates already booked are replaced. The range may be
    /// given in either order.
    /// </summary>
    public static List<BookedDay> BookRange(
        IEnumerable<BookedDay> days, DateOnly start, DateOnly end, DayType type, string description, ISet<DateOnly> holidays)
    {
        if (start > end)
            (start, end) = (end, start);

        var byDate = ToDictionary(days);
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (BalanceCalculator.IsWorkingDay(d, holidays))
                byDate[d] = new BookedDay { Date = d, Type = type, Amount = BookedDay.FullDay, Description = description ?? "" };
        }
        return Sorted(byDate);
    }

    /// <summary>
    /// Books a single day with the given <paramref name="amount"/> (1 or 0.5), replacing any
    /// existing record on that date. Does nothing if the date is a weekend or holiday.
    /// </summary>
    public static List<BookedDay> BookDay(
        IEnumerable<BookedDay> days, DateOnly date, DayType type, decimal amount, string description, ISet<DateOnly> holidays)
    {
        if (amount is not (BookedDay.FullDay or BookedDay.HalfDay))
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be 1 or 0.5.");

        var byDate = ToDictionary(days);
        if (BalanceCalculator.IsWorkingDay(date, holidays))
            byDate[date] = new BookedDay { Date = date, Type = type, Amount = amount, Description = description ?? "" };
        return Sorted(byDate);
    }

    /// <summary>Books a single day as a half day (0.5). See <see cref="BookDay"/>.</summary>
    public static List<BookedDay> BookHalfDay(
        IEnumerable<BookedDay> days, DateOnly date, DayType type, string description, ISet<DateOnly> holidays) =>
        BookDay(days, date, type, BookedDay.HalfDay, description, holidays);

    /// <summary>Removes the records on the given dates. Dates without a record are ignored.</summary>
    public static List<BookedDay> RemoveDays(IEnumerable<BookedDay> days, IEnumerable<DateOnly> dates)
    {
        var byDate = ToDictionary(days);
        foreach (var d in dates)
            byDate.Remove(d);
        return Sorted(byDate);
    }

    /// <summary>
    /// Groups booked days into display periods and returns those with at least one date in
    /// <paramref name="year"/>, ordered by start date. Consecutive records with the same type and
    /// description form one period as long as only weekends or holidays lie between them; a free
    /// working day ends the period. Half and full days may share a period.
    /// A period is not cut at New Year: <see cref="BookedPeriod.Start"/>, <see cref="BookedPeriod.End"/>
    /// and <see cref="BookedPeriod.Dates"/> cover the whole period (so deleting it removes all of it),
    /// while <see cref="BookedPeriod.Amount"/> sums only the working days inside <paramref name="year"/>.
    /// </summary>
    public static List<BookedPeriod> GroupPeriods(IEnumerable<BookedDay> days, int year, ISet<DateOnly> holidays)
    {
        var sorted = Sorted(ToDictionary(days));
        var groups = new List<List<BookedDay>>();
        List<BookedDay>? current = null;

        foreach (var day in sorted)
        {
            if (current is not null && Continues(current[^1], day, holidays))
                current.Add(day);
            else
                groups.Add(current = [day]);
        }

        var result = new List<BookedPeriod>();
        foreach (var g in groups)
        {
            if (!g.Any(d => d.Date.Year == year))
                continue;
            var amount = g.Where(d => d.Date.Year == year && BalanceCalculator.IsWorkingDay(d.Date, holidays)).Sum(d => d.Amount);
            result.Add(new BookedPeriod(g[0].Date, g[^1].Date, g[0].Type, g[0].Description, amount, g.Select(d => d.Date).ToList()));
        }
        return result;
    }

    private static bool Continues(BookedDay previous, BookedDay next, ISet<DateOnly> holidays)
    {
        if (previous.Type != next.Type || !string.Equals(previous.Description, next.Description, StringComparison.Ordinal))
            return false;
        for (var d = previous.Date.AddDays(1); d < next.Date; d = d.AddDays(1))
        {
            if (BalanceCalculator.IsWorkingDay(d, holidays))
                return false;
        }
        return true;
    }

    /// <summary>Indexes records by date; if the input has several records for a date, the first wins.</summary>
    private static Dictionary<DateOnly, BookedDay> ToDictionary(IEnumerable<BookedDay> days)
    {
        var byDate = new Dictionary<DateOnly, BookedDay>();
        foreach (var day in days)
            byDate.TryAdd(day.Date, day);
        return byDate;
    }

    private static List<BookedDay> Sorted(Dictionary<DateOnly, BookedDay> byDate) =>
        byDate.Values.OrderBy(d => d.Date).ToList();
}
