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
/// <param name="RequestedDays">
/// Working days in the range times the requested amount, not counting <see cref="UnchangedDates"/>.
/// </param>
/// <param name="BookedDays">Feriefri actually booked (not counting <see cref="UnchangedDates"/>).</param>
/// <param name="HalfDayDates">Days requested as full days and booked as half days because only 0.5 fitted.</param>
/// <param name="LeftOutDates">Days not booked at all; their existing records are unchanged.</param>
/// <param name="Limit">The first holiday year whose allowance ran out for a reported day; null when everything fitted.</param>
public sealed record FeriefriBookingResult(
    List<BookedDay> Days,
    decimal RequestedDays,
    decimal BookedDays,
    IReadOnlyList<DateOnly> HalfDayDates,
    IReadOnlyList<DateOnly> LeftOutDates,
    FeriefriLimit? Limit)
{
    /// <summary>
    /// Working days whose existing record already was what this booking would give them (Feriefri with the amount that
    /// fits and, for <see cref="Bookings.BookFeriefri"/>, the same description). They are kept as they were and are in
    /// none of the other counts or lists, so a booking where every day is unchanged has 0 requested and isn't refused.
    /// </summary>
    public IReadOnlyList<DateOnly> UnchangedDates { get; init; } = [];

    /// <summary>Requested minus booked (a full day booked as a half day leaves 0.5 out).</summary>
    public decimal LeftOutDays => RequestedDays - BookedDays;

    /// <summary>Whether nothing could be booked although working days were requested.</summary>
    public bool IsRefused => RequestedDays > 0 && BookedDays == 0;

    /// <summary>Whether everything requested was booked.</summary>
    public bool IsComplete => LeftOutDays == 0;
}

/// <summary>What <see cref="Bookings.CycleDay"/> did to the clicked date.</summary>
public enum DayCycleOutcome
{
    /// <summary>Weekend or public holiday (Danish or manual): nothing changed, even if a record is on the date.</summary>
    NotAWorkingDay,
    /// <summary>A free day was booked as a full day of the clicked type.</summary>
    Booked,
    /// <summary>A free day was booked as a half Feriefri day because a full day didn't fit the allowance.</summary>
    ConvertedToHalf,
    /// <summary>A full day of the clicked type became a half day (description kept).</summary>
    ChangedToHalf,
    /// <summary>A half day of the clicked type was removed; the day is free.</summary>
    Removed,
    /// <summary>A day booked with the other type was replaced by a full day of the clicked type.</summary>
    Replaced,
    /// <summary>
    /// A day booked with the other type (vacation) was replaced by a half Feriefri day because a full day didn't fit the allowance.
    /// </summary>
    ReplacedAsHalf,
    /// <summary>
    /// Feriefri: not even a half day fits the allowance. Nothing changed; an existing booking on the date stays.
    /// <see cref="DayCycleResult.Limit"/> says which holiday year's allowance is used up.
    /// </summary>
    Refused,
}

/// <summary>Result of <see cref="Bookings.CycleDay"/>.</summary>
/// <param name="Days">The new list of booked days, sorted by date. Equal to the input when nothing changed.</param>
/// <param name="Date">The clicked date.</param>
/// <param name="Type">The clicked type.</param>
/// <param name="Previous">The record on the date before the click, or null if the day was free.</param>
/// <param name="Current">The record on the date after the click, or null if the day is free.</param>
/// <param name="Limit">The holiday year whose Feriefri allowance forced a half day or a refusal; otherwise null.</param>
public sealed record DayCycleResult(
    List<BookedDay> Days,
    DateOnly Date,
    DayType Type,
    DayCycleOutcome Outcome,
    BookedDay? Previous,
    BookedDay? Current,
    FeriefriLimit? Limit)
{
    /// <summary>Whether the day list changed (anything except <see cref="DayCycleOutcome.NotAWorkingDay"/> and <see cref="DayCycleOutcome.Refused"/>).</summary>
    public bool Changed => Outcome is not (DayCycleOutcome.NotAWorkingDay or DayCycleOutcome.Refused);
}

/// <summary>What <see cref="Bookings.BookTypedRange"/> did.</summary>
public enum RangeBookingOutcome
{
    /// <summary>
    /// Every working day in the range was booked as a full day, or already had what the range gives it
    /// (<see cref="RangeBookingResult.UnchangedDates"/>). Nothing changed if every day was unchanged; see <see cref="RangeBookingResult.Changed"/>.
    /// </summary>
    Booked,
    /// <summary>Feriefri only: some days became half days or were left out because the allowance ran out.</summary>
    Partial,
    /// <summary>Feriefri only: nothing fitted the allowance. Nothing changed.</summary>
    Refused,
    /// <summary>The range has no working days (only weekends and holidays). Nothing changed.</summary>
    NoWorkingDays,
    /// <summary>The range is longer than <see cref="Bookings.MaxRangeYears"/> year. Nothing changed.</summary>
    TooLong,
}

/// <summary>Result of <see cref="Bookings.BookTypedRange"/>.</summary>
/// <param name="Days">The new list of booked days, sorted by date. Equal to the input when nothing changed.</param>
/// <param name="Start">The earlier date of the range.</param>
/// <param name="End">The later date of the range.</param>
/// <param name="WorkingDays">Working days in the range (0 when <see cref="RangeBookingOutcome.TooLong"/>, which isn't counted).</param>
/// <param name="BookedDays">
/// Days whose record was actually created or changed (Feriefri half-day fallbacks count 0.5); <see cref="UnchangedDates"/>
/// don't count.
/// </param>
/// <param name="HalfDayDates">Feriefri days booked as half days because only 0.5 fitted (not counting unchanged days).</param>
/// <param name="LeftOutDates">Feriefri days not booked at all; their existing records are unchanged.</param>
/// <param name="Limit">The first holiday year whose Feriefri allowance ran out for a reported day; null when everything fitted.</param>
/// <param name="UnchangedDates">
/// Working days that already had a record of <paramref name="Type"/> with the amount the range gives them (a full vacation
/// day; Feriefri: the amount that fits). They are kept as they were, description included, and aren't booked, half or left out.
/// </param>
/// <param name="LatestAllowedEnd">For <see cref="RangeBookingOutcome.TooLong"/>: the latest end allowed for <paramref name="Start"/>; otherwise null.</param>
public sealed record RangeBookingResult(
    List<BookedDay> Days,
    DayType Type,
    DateOnly Start,
    DateOnly End,
    RangeBookingOutcome Outcome,
    int WorkingDays,
    decimal BookedDays,
    IReadOnlyList<DateOnly> HalfDayDates,
    IReadOnlyList<DateOnly> LeftOutDates,
    FeriefriLimit? Limit,
    DateOnly? LatestAllowedEnd,
    IReadOnlyList<DateOnly> UnchangedDates)
{
    /// <summary>
    /// Requested (full days on every working day that isn't unchanged) minus booked; a half-day fallback leaves 0.5 out.
    /// </summary>
    public decimal LeftOutDays => WorkingDays - UnchangedDates.Count - BookedDays;

    /// <summary>
    /// Whether the day list changed. False for <see cref="RangeBookingOutcome.Booked"/> when every working day was
    /// already booked as the range would book it (all in <see cref="UnchangedDates"/>, <see cref="BookedDays"/> 0).
    /// </summary>
    public bool Changed => (Outcome is RangeBookingOutcome.Booked or RangeBookingOutcome.Partial) && BookedDays > 0;
}

/// <summary>
/// Pure booking operations on a list of <see cref="BookedDay"/> records. Each returns a new list
/// sorted by date with at most one record per date; the input is not modified.
/// </summary>
/// <remarks>
/// <c>holidays</c> is the set of manual public holidays across all relevant years
/// (see <see cref="VacationData.AllPublicHolidays"/>), so ranges can cross New Year. The automatic Danish holidays
/// (<see cref="DanishHolidays"/>) always apply on top of it, via <see cref="BalanceCalculator.IsWorkingDay"/>.
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
    /// Weekends and public holidays are skipped and don't count as requested. A day whose record already is Feriefri with
    /// the amount that fits and the same <paramref name="description"/> is left as it is and only listed in
    /// <see cref="FeriefriBookingResult.UnchangedDates"/> (not requested, booked, half or left out).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is not 1 or 0.5.</exception>
    /// <exception cref="ArgumentException">The range is longer than <see cref="MaxRangeYears"/> year.</exception>
    public static FeriefriBookingResult BookFeriefri(
        VacationData data, DateOnly start, DateOnly end, decimal amount, string description) =>
        BookFeriefri(data, start, end, amount, description, ignoreDescription: false);

    /// <param name="ignoreDescription">
    /// Whether an existing Feriefri record with the fitted amount counts as unchanged whatever its description (it is
    /// then kept, description included). Otherwise the description must match as well, and a different one is replaced.
    /// </param>
    private static FeriefriBookingResult BookFeriefri(
        VacationData data, DateOnly start, DateOnly end, decimal amount, string description, bool ignoreDescription)
    {
        description ??= "";
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
        var unchanged = new List<DateOnly>();
        HolidayYear? limitYear = null;

        for (var n = start.DayNumber; n <= end.DayNumber; n++)
        {
            var d = DateOnly.FromDayNumber(n);
            if (!BalanceCalculator.IsWorkingDay(d, holidays))
                continue;

            byDate.TryGetValue(d, out var existing);
            byDate.Remove(d);
            var baseline = FeriefriCalculator.Overflow(data, byDate.Values, holidays, allowances);

            decimal fitted = 0;
            HolidayYear? dayLimit = null;
            foreach (var a in tries)
            {
                byDate[d] = new BookedDay { Date = d, Type = DayType.Feriefri, Amount = a, Description = description };
                var over = FeriefriCalculator.Overflow(data, byDate.Values, holidays, allowances);
                var grown = over.Where(o => o.Value > baseline.GetValueOrDefault(o.Key)).Select(o => o.Key).ToList();
                if (grown.Count == 0)
                {
                    fitted = a;
                    break;
                }
                dayLimit ??= new HolidayYear(grown.Min(y => y.StartYear));
            }

            // Already booked exactly as this booking would book it: keep the record and don't report the day at all.
            if (fitted > 0 && existing is { Type: DayType.Feriefri } && existing.Amount == fitted &&
                (ignoreDescription || string.Equals(existing.Description, description, StringComparison.Ordinal)))
            {
                byDate[d] = existing;
                unchanged.Add(d);
                continue;
            }

            requested += amount;
            if (dayLimit is not null)
                limitYear ??= dayLimit;
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
        return new FeriefriBookingResult(days, requested, booked, halfDays, leftOut, limit) { UnchangedDates = unchanged };
    }

    /// <summary>
    /// One click on <paramref name="date"/> in the calendar with <paramref name="type"/> (left-click Vacation, right-click
    /// Feriefri): free → full day → half day → free; a day booked with the other type is replaced by a full day of
    /// <paramref name="type"/>. Weekends and public holidays (Danish or manual) never change. A new Feriefri day follows
    /// the allowance rules of <see cref="BookFeriefri"/>: a full day if it fits, else a half day, else refused (the
    /// existing record, if any, stays). Going from full to half keeps the day's description; a new or replacing booking
    /// has an empty description. <paramref name="data"/> is not modified.
    /// </summary>
    public static DayCycleResult CycleDay(VacationData data, DateOnly date, DayType type)
    {
        var days = data.Days ?? [];
        var byDate = ToDictionary(days);
        byDate.TryGetValue(date, out var previous);

        if (!BalanceCalculator.IsWorkingDay(date, data.AllPublicHolidays()))
            return new DayCycleResult(Sorted(byDate), date, type, DayCycleOutcome.NotAWorkingDay, previous, previous, null);

        if (previous is not null && previous.Type == type)
        {
            // A half day (or a hand-edited smaller amount) is the last step of the cycle.
            if (previous.Amount <= BookedDay.HalfDay)
            {
                byDate.Remove(date);
                return new DayCycleResult(Sorted(byDate), date, type, DayCycleOutcome.Removed, previous, null, null);
            }
            // Full (or a hand-edited larger amount) → half. Shrinking a day never needs the allowance check.
            var half = previous with { Amount = BookedDay.HalfDay };
            byDate[date] = half;
            return new DayCycleResult(Sorted(byDate), date, type, DayCycleOutcome.ChangedToHalf, previous, half, null);
        }

        var replacing = previous is not null;
        if (type == DayType.Vacation)
        {
            var full = new BookedDay { Date = date, Type = DayType.Vacation, Amount = BookedDay.FullDay, Description = "" };
            byDate[date] = full;
            return new DayCycleResult(
                Sorted(byDate), date, type, replacing ? DayCycleOutcome.Replaced : DayCycleOutcome.Booked, previous, full, null);
        }

        var r = BookFeriefri(data, date, date, BookedDay.FullDay, "");
        if (r.BookedDays == 0)
            return new DayCycleResult(r.Days, date, type, DayCycleOutcome.Refused, previous, previous, r.Limit);
        var current = r.Days.First(d => d.Date == date);
        var outcome = r.BookedDays == BookedDay.FullDay
            ? (replacing ? DayCycleOutcome.Replaced : DayCycleOutcome.Booked)
            : (replacing ? DayCycleOutcome.ReplacedAsHalf : DayCycleOutcome.ConvertedToHalf);
        return new DayCycleResult(r.Days, date, type, outcome, previous, current, r.Limit);
    }

    /// <summary>
    /// Books the inclusive range <paramref name="from"/>..<paramref name="to"/> (either order) as full days of
    /// <paramref name="type"/>, for dragging or Shift+clicking in the calendar. Weekends and public holidays are skipped.
    /// Vacation: every working day becomes a full vacation day, replacing any record (like <see cref="BookRange"/>).
    /// Feriefri: <see cref="BookFeriefri"/> in date order (full, else half, else left out with its record kept).
    /// A day that already has a record of <paramref name="type"/> with the amount the range gives it is kept as it was,
    /// description included, and is only listed in <see cref="RangeBookingResult.UnchangedDates"/>; when every working day
    /// is like that, the outcome is <see cref="RangeBookingOutcome.Booked"/> with 0 booked days and
    /// <see cref="RangeBookingResult.Changed"/> false. New and replaced records have an empty description. A range longer than <see cref="MaxRangeYears"/> year is not booked and
    /// gives <see cref="RangeBookingOutcome.TooLong"/> instead of throwing. <paramref name="data"/> is not modified.
    /// </summary>
    public static RangeBookingResult BookTypedRange(VacationData data, DateOnly from, DateOnly to, DayType type)
    {
        var (start, end) = from <= to ? (from, to) : (to, from);
        var days = data.Days ?? [];

        if (!IsRangeWithinLimit(start, end))
            return new RangeBookingResult(
                Sorted(ToDictionary(days)), type, start, end, RangeBookingOutcome.TooLong, 0, 0, [], [], null, LatestAllowedEnd(start), []);

        var holidays = data.AllPublicHolidays();
        var workingDays = 0;
        for (var n = start.DayNumber; n <= end.DayNumber; n++)
        {
            if (BalanceCalculator.IsWorkingDay(DateOnly.FromDayNumber(n), holidays))
                workingDays++;
        }
        if (workingDays == 0)
            return new RangeBookingResult(
                Sorted(ToDictionary(days)), type, start, end, RangeBookingOutcome.NoWorkingDays, 0, 0, [], [], null, null, []);

        if (type == DayType.Vacation)
        {
            var byDate = ToDictionary(days);
            var unchanged = new List<DateOnly>();
            for (var n = start.DayNumber; n <= end.DayNumber; n++)
            {
                var d = DateOnly.FromDayNumber(n);
                if (!BalanceCalculator.IsWorkingDay(d, holidays))
                    continue;
                if (byDate.TryGetValue(d, out var existing) &&
                    existing is { Type: DayType.Vacation, Amount: BookedDay.FullDay })
                {
                    unchanged.Add(d);
                    continue;
                }
                byDate[d] = new BookedDay { Date = d, Type = DayType.Vacation, Amount = BookedDay.FullDay, Description = "" };
            }
            return new RangeBookingResult(
                Sorted(byDate), type, start, end, RangeBookingOutcome.Booked, workingDays, workingDays - unchanged.Count,
                [], [], null, null, unchanged);
        }

        var r = BookFeriefri(data, start, end, BookedDay.FullDay, "", ignoreDescription: true);
        var outcome = r.IsComplete ? RangeBookingOutcome.Booked
            : r.IsRefused ? RangeBookingOutcome.Refused
            : RangeBookingOutcome.Partial;
        return new RangeBookingResult(
            r.Days, type, start, end, outcome, workingDays, r.BookedDays, r.HalfDayDates, r.LeftOutDates, r.Limit, null,
            r.UnchangedDates);
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
