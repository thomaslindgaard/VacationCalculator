namespace VacationCalculator.Core;

/// <summary>
/// What happens to open Feriefri days on 31 December of calendar year <see cref="Year"/>. The open days are the
/// leftover of <see cref="Previous"/> (whose last day is this 31 December) plus what <see cref="Current"/> (started
/// 1 September of this year) has not booked by this 31 December. Anything above
/// <see cref="FeriefriCalculator.MaxOpenAtYearEnd"/> is paid out in January, taken from the previous year's leftover
/// first; the rest of that leftover is lost; the current year keeps the remainder (<see cref="CarriedDays"/>) until its
/// own deadline. Only bookings dated on or before this 31 December count, used or booked ahead.
/// </summary>
/// <param name="PreviousLeftoverDays">
/// <see cref="Previous"/>'s days not booked and not paid out at its own first 31 December (never negative).
/// </param>
/// <param name="CurrentRemainingDays">
/// <see cref="Current"/>'s allowance minus its days booked on or before this 31 December (never negative).
/// </param>
/// <param name="IsProjected">Whether this 31 December is today or later: the figures are a projection from the bookings.</param>
public sealed record FeriefriYearEnd(
    int Year,
    HolidayYear Previous,
    HolidayYear Current,
    decimal PreviousLeftoverDays,
    decimal CurrentRemainingDays,
    decimal PaidOutFromPreviousDays,
    decimal PaidOutFromCurrentDays,
    decimal LostDays,
    bool IsProjected)
{
    public DateOnly Date => new(Year, 12, 31);

    /// <summary>Open days on 31 December: previous leftover + current remaining.</summary>
    public decimal OpenDays => PreviousLeftoverDays + CurrentRemainingDays;

    /// <summary>Days paid out in January: max(0, open - 5).</summary>
    public decimal PaidOutDays => PaidOutFromPreviousDays + PaidOutFromCurrentDays;

    /// <summary>The current year's days that stay usable after this 31 December (until 31 December next year).</summary>
    public decimal CarriedDays => CurrentRemainingDays - PaidOutFromCurrentDays;

    /// <summary>Days paid out or lost; they must be used by this 31 December to avoid it.</summary>
    public decimal ExpiringDays => PaidOutDays + LostDays;

    /// <summary>Whether the 31 December has passed, so the figures are what happened.</summary>
    public bool HasPassed => !IsProjected;
}

/// <summary>
/// Feriefri balance of one holiday year as of a given day. Feriefri days are allocated oldest-first, like vacation.
/// A holiday year meets two 31 Decembers: the first (31 Dec of its start year), where its own days above 5 are paid out
/// (see <see cref="FeriefriYearEnd"/>), and its deadline a year later, where its leftover is paid out or lost.
/// Figures for a 31 December are projected from all bookings until it has passed.
/// </summary>
/// <param name="UsedDays">Feriefri allocated to this year on or before today.</param>
/// <param name="BookedAheadDays">Feriefri allocated to this year after today.</param>
/// <param name="PaidOutAtFirstYearEnd">This year's days paid out in January after its first 31 December.</param>
/// <param name="CarriedDays">This year's days still unbooked after its first 31 December and not paid out, by bookings up to then.</param>
/// <param name="PaidOutAtDeadline">This year's leftover paid out in January after its deadline.</param>
/// <param name="LostAtDeadline">This year's leftover lost at its deadline.</param>
/// <param name="FirstYearEndPassed">Whether today is after the first 31 December.</param>
/// <param name="DeadlinePassed">Whether today is after the deadline.</param>
public sealed record FeriefriBalance(
    HolidayYear HolidayYear,
    FeriefriAllowance Allowance,
    decimal UsedDays,
    decimal BookedAheadDays,
    decimal PaidOutAtFirstYearEnd,
    decimal CarriedDays,
    decimal PaidOutAtDeadline,
    decimal LostAtDeadline,
    bool FirstYearEndPassed,
    bool DeadlinePassed)
{
    public decimal AllowanceDays => Allowance.Days;

    /// <summary>31 December of the start year: the first payout check.</summary>
    public DateOnly FirstYearEnd => FeriefriCalculator.FirstYearEnd(HolidayYear);

    /// <summary>Last day the year's Feriefri days can be used (31 December of the following year).</summary>
    public DateOnly Deadline => HolidayYear.Deadline;

    public decimal BookedDays => UsedDays + BookedAheadDays;

    /// <summary>
    /// Days still usable now: allowance - booked, less what has actually been paid out or lost (31 Decembers that have
    /// passed). Projected payouts don't reduce it: those days can still be booked before that 31 December.
    /// </summary>
    public decimal RemainingDays =>
        AllowanceDays - BookedDays
        - (FirstYearEndPassed ? PaidOutAtFirstYearEnd : 0)
        - (DeadlinePassed ? PaidOutAtDeadline + LostAtDeadline : 0);

    /// <summary>Allowance - paid out at the first 31 December - booked. Negative when more is booked than the year provides.</summary>
    public decimal UnbookedDays => AllowanceDays - PaidOutAtFirstYearEnd - BookedDays;

    /// <summary>By how much the bookings exceed what the year provides (0 if they don't). Only possible in stored data, e.g. after lowering the allowance.</summary>
    public decimal ExceedsAllowanceBy => Math.Max(0, -UnbookedDays);

    /// <summary>All of the year's days paid out (at either 31 December).</summary>
    public decimal PaidOutDays => PaidOutAtFirstYearEnd + PaidOutAtDeadline;

    /// <summary>Whether <paramref name="today"/> is in the year's period of use, 1 Sep .. 31 Dec of the following year.</summary>
    public bool IsOpenOn(DateOnly today) => HolidayYear.IsInTakingPeriod(today);
}

/// <summary>
/// Feriefri arithmetic. Feriefri days follow the holiday year, its full allowance is available from 1 September, and they
/// never count toward vacation. Every holiday year starts with its own allowance: there is no opening balance and the
/// first tracked holiday year doesn't apply.
/// </summary>
public static class FeriefriCalculator
{
    /// <summary>Most Feriefri days that may be open on 31 December without being paid out.</summary>
    public const decimal MaxOpenAtYearEnd = 5m;

    /// <summary>31 December of the holiday year's start year.</summary>
    public static DateOnly FirstYearEnd(HolidayYear year) => new(year.StartYear, 12, 31);

    /// <summary>
    /// Assigns every booked <see cref="DayType.Feriefri"/> day on a working day to a holiday year, in date order.
    /// From September to December the older open year takes the day while it has days left (allowance - paid out at its
    /// first 31 December - allocated), otherwise the newer one does; a full day may be split 0.5 / 0.5. From January a
    /// year's days paid out at its first 31 December are no longer available. Stored days are never refused: the newest
    /// open year takes anything that doesn't fit. If several records share a date, the first wins.
    /// </summary>
    public static IReadOnlyList<AllocatedDay> Allocate(VacationData data) =>
        new Ledger(data, data.Days ?? [], data.AllPublicHolidays(), []).Allocations;

    /// <summary>Feriefri balance of <paramref name="year"/> as of <paramref name="today"/>.</summary>
    public static FeriefriBalance CalculateHolidayYear(VacationData data, HolidayYear year, DateOnly today) =>
        new Ledger(data, data.Days ?? [], data.AllPublicHolidays(), []).Balance(year, today);

    /// <summary>Feriefri balances of several holiday years as of <paramref name="today"/>, in the given order.</summary>
    public static IReadOnlyList<FeriefriBalance> CalculateHolidayYears(
        VacationData data, IEnumerable<HolidayYear> years, DateOnly today)
    {
        var ledger = new Ledger(data, data.Days ?? [], data.AllPublicHolidays(), []);
        return years.Select(y => ledger.Balance(y, today)).ToList();
    }

    /// <summary>
    /// The Feriefri outcome of 31 December <paramref name="calendarYear"/> (see <see cref="FeriefriYearEnd"/>): what the
    /// banner for that calendar year needs. A projection while <paramref name="today"/> is on or before that day.
    /// </summary>
    public static FeriefriYearEnd CalculateYearEnd(VacationData data, int calendarYear, DateOnly today) =>
        new Ledger(data, data.Days ?? [], data.AllPublicHolidays(), []).YearEnd(calendarYear, today);

    /// <summary>
    /// Feriefri amount allocated beyond what each holiday year provides, per holiday year (only years with overflow).
    /// Used by <see cref="Bookings.BookFeriefri"/> to check that a booking fits.
    /// </summary>
    internal static Dictionary<HolidayYear, decimal> Overflow(
        VacationData data, IEnumerable<BookedDay> days, ISet<DateOnly> holidays, Dictionary<HolidayYear, decimal> allowanceCache) =>
        new Ledger(data, days, holidays, allowanceCache).OverflowByYear;

    /// <summary>The 31 December paid-out days of <paramref name="year"/> with the given bookings (for refusal messages).</summary>
    internal static decimal PaidOutAtFirstYearEnd(
        VacationData data, IEnumerable<BookedDay> days, ISet<DateOnly> holidays, Dictionary<HolidayYear, decimal> allowanceCache, HolidayYear year) =>
        new Ledger(data, days, holidays, allowanceCache).PaidOutAtFirstYearEnd(year);

    private sealed class Ledger
    {
        private readonly VacationData _data;
        private readonly Dictionary<HolidayYear, decimal> _allowance;
        private readonly Dictionary<HolidayYear, decimal> _allocated = [];
        /// <summary>Allocated with a date on or before the year's first 31 December.</summary>
        private readonly Dictionary<HolidayYear, decimal> _allocatedFirstAutumn = [];

        public List<AllocatedDay> Allocations { get; } = [];
        public Dictionary<HolidayYear, decimal> OverflowByYear { get; } = [];

        public Ledger(VacationData data, IEnumerable<BookedDay> days, ISet<DateOnly> holidays, Dictionary<HolidayYear, decimal> allowanceCache)
        {
            _data = data;
            _allowance = allowanceCache;

            var byDate = new Dictionary<DateOnly, BookedDay>();
            foreach (var day in days)
                byDate.TryAdd(day.Date, day);

            var feriefri = byDate.Values
                .Where(d => d.Type == DayType.Feriefri && d.Amount > 0 && BalanceCalculator.IsWorkingDay(d.Date, holidays))
                // Dates whose holiday year can't be represented are ignored, as for vacation.
                .Where(d => HolidayYear.Containing(d.Date).StartYear is >= HolidayYear.MinStartYear and <= HolidayYear.MaxStartYear)
                .OrderBy(d => d.Date)
                .ToList();

            foreach (var day in feriefri)
            {
                var current = HolidayYear.Containing(day.Date);
                var amount = day.Amount;
                if (day.Date.Month >= 9 && current.StartYear > HolidayYear.MinStartYear)
                {
                    // Two years are open; the older one goes first. Its first 31 December has passed, so its payout is final.
                    var older = current.Previous;
                    var take = Math.Clamp(Available(older, day.Date), 0, amount);
                    if (take > 0)
                        Add(day.Date, older, take);
                    amount -= take;
                }
                if (amount > 0)
                {
                    var over = amount - Math.Clamp(Available(current, day.Date), 0, amount);
                    if (over > 0)
                        OverflowByYear[current] = OverflowByYear.GetValueOrDefault(current) + over;
                    Add(day.Date, current, amount);
                }
            }
        }

        public FeriefriBalance Balance(HolidayYear year, DateOnly today)
        {
            decimal used = 0, ahead = 0;
            foreach (var a in Allocations)
            {
                if (a.HolidayYear != year)
                    continue;
                if (a.Date <= today)
                    used += a.Amount;
                else
                    ahead += a.Amount;
            }

            var first = YearEnd(year.StartYear, today);
            var deadline = YearEnd(year.StartYear + 1, today);

            return new FeriefriBalance(
                year,
                _data.GetFeriefriAllowance(year),
                UsedDays: used,
                BookedAheadDays: ahead,
                PaidOutAtFirstYearEnd: first.PaidOutFromCurrentDays,
                CarriedDays: first.CarriedDays,
                PaidOutAtDeadline: deadline.PaidOutFromPreviousDays,
                LostAtDeadline: deadline.LostDays,
                FirstYearEndPassed: today > FirstYearEnd(year),
                DeadlinePassed: today > year.Deadline);
        }

        public FeriefriYearEnd YearEnd(int calendarYear, DateOnly today)
        {
            var previous = new HolidayYear(calendarYear - 1);
            var current = new HolidayYear(calendarYear);

            var leftover = InRange(previous) ? Leftover(previous) : 0;
            var remaining = InRange(current) ? RemainingAtFirstYearEnd(current) : 0;
            var excess = Math.Max(0, leftover + remaining - MaxOpenAtYearEnd);
            var paidPrevious = Math.Min(leftover, excess);

            return new FeriefriYearEnd(
                calendarYear,
                previous,
                current,
                PreviousLeftoverDays: leftover,
                CurrentRemainingDays: remaining,
                PaidOutFromPreviousDays: paidPrevious,
                PaidOutFromCurrentDays: excess - paidPrevious,
                LostDays: leftover - paidPrevious,
                IsProjected: today <= new DateOnly(calendarYear, 12, 31));
        }

        /// <summary>Days of <paramref name="year"/> paid out at its first 31 December: its remaining then above 5.</summary>
        public decimal PaidOutAtFirstYearEnd(HolidayYear year) => Math.Max(0, RemainingAtFirstYearEnd(year) - MaxOpenAtYearEnd);

        private static bool InRange(HolidayYear year) =>
            year.StartYear is >= HolidayYear.MinStartYear and <= HolidayYear.MaxStartYear;

        private decimal RemainingAtFirstYearEnd(HolidayYear year) =>
            Math.Max(0, Allowance(year) - _allocatedFirstAutumn.GetValueOrDefault(year));

        /// <summary>What <paramref name="year"/> has left at its deadline: allowance - paid out at its first 31 December - allocated.</summary>
        private decimal Leftover(HolidayYear year) =>
            Math.Max(0, Allowance(year) - PaidOutAtFirstYearEnd(year) - _allocated.GetValueOrDefault(year));

        /// <summary>
        /// Days of <paramref name="year"/> still available for a booking on <paramref name="date"/>. Only called with dates
        /// in date order, so a first 31 December before <paramref name="date"/> has all its allocations.
        /// </summary>
        private decimal Available(HolidayYear year, DateOnly date) =>
            Allowance(year) - _allocated.GetValueOrDefault(year)
            - (date > FirstYearEnd(year) ? PaidOutAtFirstYearEnd(year) : 0);

        private void Add(DateOnly date, HolidayYear year, decimal amount)
        {
            Allocations.Add(new AllocatedDay(date, year, amount));
            _allocated[year] = _allocated.GetValueOrDefault(year) + amount;
            if (date <= FirstYearEnd(year))
                _allocatedFirstAutumn[year] = _allocatedFirstAutumn.GetValueOrDefault(year) + amount;
        }

        private decimal Allowance(HolidayYear year)
        {
            if (!_allowance.TryGetValue(year, out var days))
                _allowance[year] = days = _data.GetFeriefriAllowance(year).Days;
            return days;
        }
    }
}
