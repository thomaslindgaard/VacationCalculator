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

/// <summary>The holiday year whose Feriefri allowance stopped a booking, for the message to the user.</summary>
/// <param name="PaidOutAtFirstYearEnd">
/// Of the allowance, the days paid out at the year's first 31 December (projected from the bookings), which can't be
/// booked after that day. Usually 0; non-zero only with an allowance above 5.
/// </param>
public sealed record FeriefriLimit(FeriefriAllowance Allowance, decimal PaidOutAtFirstYearEnd)
{
    public HolidayYear HolidayYear => Allowance.HolidayYear;
    public decimal AllowanceDays => Allowance.Days;
}

/// <summary>Result of <see cref="Bookings.BookFeriefri"/>.</summary>
/// <param name="Days">The new list of booked days, sorted by date. Equal to the input when nothing was booked.</param>
/// <param name="RequestedDays">Working days in the range times the requested amount.</param>
/// <param name="BookedDays">Feriefri actually booked.</param>
/// <param name="HalfDayDates">Days requested as full days and booked as half days because only 0.5 fitted.</param>
/// <param name="LeftOutDates">Days not booked at all; their existing records are unchanged.</param>
/// <param name="Limit">The first holiday year whose allowance ran out; null when everything fitted.</param>
public sealed record FeriefriBookingResult(
    List<BookedDay> Days,
    decimal RequestedDays,
    decimal BookedDays,
    IReadOnlyList<DateOnly> HalfDayDates,
    IReadOnlyList<DateOnly> LeftOutDates,
    FeriefriLimit? Limit)
{
    /// <summary>Requested minus booked (a full day booked as a half day leaves 0.5 out).</summary>
    public decimal LeftOutDays => RequestedDays - BookedDays;

    /// <summary>Whether nothing could be booked although working days were requested.</summary>
    public bool IsRefused => RequestedDays > 0 && BookedDays == 0;

    /// <summary>Whether everything requested was booked.</summary>
    public bool IsComplete => LeftOutDays == 0;
}

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
    /// <summary>The longest range <see cref="BookRange"/> accepts, in years (inclusive of both ends).</summary>
    public const int MaxRangeYears = 1;

    /// <summary>
    /// The last date a range starting on <paramref name="start"/> may end on: the day before the same
    /// date one year later, e.g. 2026-01-01 → 2026-12-31 and 2026-03-15 → 2027-03-14.
    /// A start on 29 February ends on 28 February the next year (e.g. 2028-02-29 → 2029-02-28),
    /// as if the same date next year were 1 March. Clamped to <see cref="DateOnly.MaxValue"/> in year 9999.
    /// </summary>
    public static DateOnly LatestAllowedEnd(DateOnly start)
    {
        if (start.Year + MaxRangeYears > DateOnly.MaxValue.Year)
            return DateOnly.MaxValue;
        if (start.Month == 2 && start.Day == 29)
            return new DateOnly(start.Year + MaxRangeYears, 2, 28);
        return start.AddYears(MaxRangeYears).AddDays(-1);
    }

    /// <summary>
    /// Whether the inclusive range (in either order) covers at most <see cref="MaxRangeYears"/> year,
    /// i.e. the later date is on or before <see cref="LatestAllowedEnd"/> of the earlier one.
    /// </summary>
    public static bool IsRangeWithinLimit(DateOnly start, DateOnly end)
    {
        if (start > end)
            (start, end) = (end, start);
        return end <= LatestAllowedEnd(start);
    }

    /// <summary>
    /// Books every working day in the inclusive range as a full day of <paramref name="type"/>.
    /// Weekends and holidays are skipped. Dates already booked are replaced. The range may be
    /// given in either order.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The range is longer than <see cref="MaxRangeYears"/> year (see <see cref="IsRangeWithinLimit"/>).
    /// </exception>
    public static List<BookedDay> BookRange(
        IEnumerable<BookedDay> days, DateOnly start, DateOnly end, DayType type, string description, ISet<DateOnly> holidays)
    {
        if (start > end)
            (start, end) = (end, start);

        if (!IsRangeWithinLimit(start, end))
            throw new ArgumentException(
                $"A booked range can cover at most {MaxRangeYears} year: {start:yyyy-MM-dd} .. {end:yyyy-MM-dd} is longer. " +
                $"The latest allowed end for this start is {LatestAllowedEnd(start):yyyy-MM-dd}.",
                nameof(end));

        var byDate = ToDictionary(days);
        // Day numbers, so that an end of 9999-12-31 doesn't step past DateOnly.MaxValue.
        for (var n = start.DayNumber; n <= end.DayNumber; n++)
        {
            var d = DateOnly.FromDayNumber(n);
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

    /// <summary>
    /// Books Feriefri on the working days of the inclusive range (either order) without exceeding any holiday year's
    /// Feriefri allowance, and returns the new list of <paramref name="data"/>'s days (which is not modified).
    /// Days are booked in date order with <paramref name="amount"/> (1 or 0.5) each. A day that doesn't fit with
    /// <paramref name="amount"/> 1 becomes a half day if 0.5 fits; otherwise it is left out and its existing record (if any)
    /// is kept. Whether a day fits is checked against the whole allocation (<see cref="FeriefriCalculator.Allocate"/>):
    /// oldest-first, all other bookings, and days paid out at a first 31 December no longer being available after it.
    /// An existing record on the day (vacation or Feriefri) is freed first; a booked Feriefri day replaces it.
    /// Bookings that already exceed an allowance (e.g. after lowering it) don't block days in other holiday years.
    /// Weekends and public holidays are skipped and don't count as requested.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is not 1 or 0.5.</exception>
    /// <exception cref="ArgumentException">The range is longer than <see cref="MaxRangeYears"/> year.</exception>
    public static FeriefriBookingResult BookFeriefri(
        VacationData data, DateOnly start, DateOnly end, decimal amount, string description)
    {
        if (amount is not (BookedDay.FullDay or BookedDay.HalfDay))
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be 1 or 0.5.");
        if (start > end)
            (start, end) = (end, start);
        if (!IsRangeWithinLimit(start, end))
            throw new ArgumentException(
                $"A booked range can cover at most {MaxRangeYears} year: {start:yyyy-MM-dd} .. {end:yyyy-MM-dd} is longer. " +
                $"The latest allowed end for this start is {LatestAllowedEnd(start):yyyy-MM-dd}.",
                nameof(end));

        var holidays = data.AllPublicHolidays();
        var allowances = new Dictionary<HolidayYear, decimal>();
        var byDate = ToDictionary(data.Days ?? []);
        var tries = amount == BookedDay.FullDay ? new[] { BookedDay.FullDay, BookedDay.HalfDay } : [BookedDay.HalfDay];

        decimal requested = 0, booked = 0;
        var halfDays = new List<DateOnly>();
        var leftOut = new List<DateOnly>();
        HolidayYear? limitYear = null;

        for (var n = start.DayNumber; n <= end.DayNumber; n++)
        {
            var d = DateOnly.FromDayNumber(n);
            if (!BalanceCalculator.IsWorkingDay(d, holidays))
                continue;
            requested += amount;

            byDate.TryGetValue(d, out var existing);
            byDate.Remove(d);
            var baseline = FeriefriCalculator.Overflow(data, byDate.Values, holidays, allowances);

            decimal fitted = 0;
            foreach (var a in tries)
            {
                byDate[d] = new BookedDay { Date = d, Type = DayType.Feriefri, Amount = a, Description = description ?? "" };
                var over = FeriefriCalculator.Overflow(data, byDate.Values, holidays, allowances);
                var grown = over.Where(o => o.Value > baseline.GetValueOrDefault(o.Key)).Select(o => o.Key).ToList();
                if (grown.Count == 0)
                {
                    fitted = a;
                    break;
                }
                limitYear ??= new HolidayYear(grown.Min(y => y.StartYear));
            }

            if (fitted == 0)
            {
                byDate.Remove(d);
                if (existing is not null)
                    byDate[d] = existing;
                leftOut.Add(d);
                continue;
            }
            booked += fitted;
            if (fitted < amount)
                halfDays.Add(d);
        }

        var days = Sorted(byDate);
        FeriefriLimit? limit = null;
        if (limitYear is { } ly)
        {
            limit = new FeriefriLimit(
                data.GetFeriefriAllowance(ly),
                FeriefriCalculator.PaidOutAtFirstYearEnd(data, days, holidays, allowances, ly));
        }
        return new FeriefriBookingResult(days, requested, booked, halfDays, leftOut, limit);
    }

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
        // Day numbers, like BookRange, so no date past DateOnly.MaxValue is ever formed.
        for (var n = previous.Date.DayNumber + 1; n < next.Date.DayNumber; n++)
        {
            if (BalanceCalculator.IsWorkingDay(DateOnly.FromDayNumber(n), holidays))
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
