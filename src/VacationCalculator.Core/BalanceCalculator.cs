namespace VacationCalculator.Core;

public enum BalanceStatus
{
    /// <summary>Nothing will be paid out or lost: every day is used or booked, or the unbooked rest will be transferred.</summary>
    AllPlanned,
    /// <summary>Unbooked days that won't be transferred remain and there is still time to book them.</summary>
    DaysToPlan,
    /// <summary>
    /// Some days will be paid out or lost even if every free working day left in the calendar year (today included)
    /// is booked: more unbooked days remain than free working days plus what can be transferred.
    /// </summary>
    DaysWillBeLost,
    /// <summary>More days are booked than the holiday year can earn by 31 August (plus transferred-in days).</summary>
    OverBooked,
    /// <summary>The deadline has passed and some days were paid out or lost.</summary>
    Expired,
}

/// <summary>
/// Part of a booked vacation day assigned to a holiday year. A full day can be split into two halves
/// when the older holiday year has only half a day left.
/// </summary>
public sealed record AllocatedDay(DateOnly Date, HolidayYear HolidayYear, decimal Amount);

/// <summary>
/// Balance of one holiday year as of a given day. Allocated days are projected from all bookings, and the
/// split of unused days at the deadline (transferred, paid out, lost) is projected from them too: a projection
/// before the deadline, what happened after it.
/// </summary>
/// <param name="EarnedSoFar">Own days usable as of today (completed months only).</param>
/// <param name="EarnableDays">Own days earned by 31 August, i.e. for the full holiday year (25 when employed all year).</param>
/// <param name="TransferredIn">
/// Days transferred in: the opening balance for the first tracked holiday year, the previous holiday year's
/// transfer at its deadline for later years, 0 for years before the first tracked one. Until the previous year's
/// deadline has passed this is a projection (see <paramref name="TransferredInIsProjected"/>); it is used in full for
/// the allocation and the deadline split, but not in <see cref="RemainingDays"/>.
/// </param>
/// <param name="UsedDays">Vacation allocated to this year on or before today.</param>
/// <param name="BookedAheadDays">Vacation allocated to this year after today.</param>
/// <param name="FifthWeekDays">The year's own 5th-week days: own earnings above 20 (at most 5). Transferred-in days are not 5th-week days.</param>
/// <param name="UnusedFifthWeekDays">Own 5th-week days not used or booked, before any transfer (bookings use transferred-in days, then the first 20).</param>
/// <param name="AtDeadline">The split of the unused days at the deadline: transferred, paid out, lost, with their parts.</param>
/// <param name="Settings">The transfer limit and payout setting that apply at this year's deadline.</param>
/// <param name="TransferredInIsProjected">
/// Whether <paramref name="TransferredIn"/> is a projection: the year receives the previous holiday year's transfer and
/// today is on or before that year's deadline, so those days still count (and can still be booked) in the previous
/// year. False for the first tracked year (the opening balance is actual from the start), for untracked years and
/// with transfer off.
/// </param>
public sealed record HolidayYearBalance(
    HolidayYear HolidayYear,
    decimal EarnedSoFar,
    decimal EarnableDays,
    decimal TransferredIn,
    decimal UsedDays,
    decimal BookedAheadDays,
    decimal FifthWeekDays,
    decimal UnusedFifthWeekDays,
    DeadlineOutcome AtDeadline,
    DeadlineSettings Settings,
    bool TransferredInIsProjected = false)
{
    public DateOnly Deadline => HolidayYear.Deadline;

    /// <summary>Own unused days transferred to the next holiday year at the deadline (5th week first).</summary>
    public decimal TransferredToNext => AtDeadline.TransferredDays;

    /// <summary>Unused days paid out at the deadline: own 5th week not transferred, plus other days under the payout setting.</summary>
    public decimal PaidOutDays => AtDeadline.PaidOutDays;

    /// <summary>Unused days lost at the deadline: unused transferred-in days, plus other days neither transferred nor paid out.</summary>
    public decimal LostDays => AtDeadline.LostDays;

    /// <summary>Transferred-in days not used or booked; they are lost at the deadline.</summary>
    public decimal UnusedTransferredInDays => AtDeadline.LostTransferredInDays;

    /// <summary>Everything the year can provide: <see cref="EarnableDays"/> + <see cref="TransferredIn"/>.</summary>
    public decimal TotalDays => EarnableDays + TransferredIn;

    public decimal BookedDays => UsedDays + BookedAheadDays;

    /// <summary>
    /// Transferred-in days that are actually here now: <see cref="TransferredIn"/>, or 0 while it is only a projection
    /// (<see cref="TransferredInIsProjected"/>).
    /// </summary>
    public decimal ActualTransferredIn => TransferredInIsProjected ? 0 : TransferredIn;

    /// <summary>
    /// Days available now that are not used or booked: earned so far + actual transferred in - used - booked ahead.
    /// Negative = advance holiday. A projected transfer out does not reduce it (the days can still be booked), and a
    /// projected transfer in does not add to it (those days still count in the previous year).
    /// </summary>
    public decimal RemainingDays => EarnedSoFar + ActualTransferredIn - BookedDays;

    /// <summary>By how much bookings exceed the days earned so far plus actual transferred-in days (0 if they don't).</summary>
    public decimal ExceedsEarnedSoFarBy => Math.Max(0, -RemainingDays);

    /// <summary>
    /// Days not booked before the deadline: <see cref="TotalDays"/> - booked. Negative when more is booked than the
    /// year can earn; otherwise equal to <see cref="TransferredToNext"/> + <see cref="PaidOutDays"/> + <see cref="LostDays"/>.
    /// </summary>
    public decimal UnbookedDays => TotalDays - BookedDays;

    /// <summary>Unbooked days that are not transferred, i.e. paid out or lost at the deadline: <see cref="PaidOutDays"/> + <see cref="LostDays"/>.</summary>
    public decimal ExpiringDays => PaidOutDays + LostDays;

    /// <summary>By how much bookings exceed what can be earned by 31 August plus transferred-in days (0 if they don't).</summary>
    public decimal ExceedsEarnableBy => Math.Max(0, BookedDays - TotalDays);

    /// <summary>Whether <paramref name="today"/> is in the holiday-taking period.</summary>
    public bool IsOpenOn(DateOnly today) => HolidayYear.IsInTakingPeriod(today);
}

/// <summary>
/// Status of calendar year <see cref="Year"/>, which concerns the holiday year whose deadline is 31 December
/// of that year (the one that started 1 September of the year before).
/// </summary>
/// <param name="WorkingDaysLeft">
/// Free working days from today (or 1 January if later) through 31 December: 1 per unbooked working day,
/// 0.5 per half-booked one. 0 once the year is over.
/// </param>
/// <param name="AtRisk">
/// How <see cref="DaysAtRisk"/> would be split at the deadline if every free working day left were booked
/// (see <see cref="DeadlineSettings.Split"/>).
/// </param>
public sealed record CalendarYearStatus(
    int Year,
    HolidayYearBalance HolidayYear,
    decimal WorkingDaysLeft,
    BalanceStatus Status,
    DeadlineOutcome AtRisk)
{
    public DateOnly Deadline => HolidayYear.Deadline;

    /// <summary>The holiday year that transferred days go to.</summary>
    public HolidayYear NextHolidayYear => HolidayYear.HolidayYear.Next;

    /// <inheritdoc cref="HolidayYearBalance.UnbookedDays"/>
    public decimal UnbookedDays => HolidayYear.UnbookedDays;

    /// <summary>Days that cannot be fitted before the deadline, even taking every free working day left.</summary>
    public decimal DaysAtRisk => Math.Max(0, UnbookedDays - WorkingDaysLeft);

    /// <summary>Unused days transferred to <see cref="NextHolidayYear"/> at the deadline if nothing more is booked.</summary>
    public decimal TransferredToNext => HolidayYear.TransferredToNext;

    /// <summary>Unused days paid out at the deadline if nothing more is booked.</summary>
    public decimal PaidOutDays => HolidayYear.PaidOutDays;

    /// <summary>Unused days lost at the deadline if nothing more is booked.</summary>
    public decimal LostDays => HolidayYear.LostDays;

    /// <summary>Of <see cref="DaysAtRisk"/>, the days that would be transferred even if every free working day left were booked.</summary>
    public decimal AtRiskTransferredDays => AtRisk.TransferredDays;

    /// <summary>Of <see cref="DaysAtRisk"/>, the days that would be paid out even if every free working day left were booked.</summary>
    public decimal AtRiskPaidOutDays => AtRisk.PaidOutDays;

    /// <summary>Of <see cref="DaysAtRisk"/>, the days that would be lost even if every free working day left were booked.</summary>
    public decimal AtRiskLostDays => AtRisk.LostDays;
}

public static class BalanceCalculator
{
    public static bool IsWorkingDay(DateOnly date, ISet<DateOnly> holidays) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !holidays.Contains(date);

    /// <summary>Counts working days in the inclusive range [start, end].</summary>
    public static int CountWorkingDays(DateOnly start, DateOnly end, ISet<DateOnly> holidays)
    {
        var count = 0;
        // Day numbers, so that an end of 9999-12-31 doesn't step past DateOnly.MaxValue.
        for (var n = start.DayNumber; n <= end.DayNumber; n++)
        {
            if (IsWorkingDay(DateOnly.FromDayNumber(n), holidays))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Assigns every booked <see cref="DayType.Vacation"/> day on a working day to a holiday year, in date order.
    /// From January to August only the holiday year that started the previous September is open. From
    /// September to December the older open year takes the day while it has days left (earnable for the
    /// full year plus transferred in), otherwise the newer one does; a full day may be split 0.5 / 0.5.
    /// Days are never refused: the newest open year takes anything that doesn't fit.
    /// If <see cref="VacationData.Days"/> has several records for a date, the first wins.
    /// <paramref name="today"/> only matters when <see cref="VacationData.FirstTrackedHolidayYear"/> is not stored
    /// (see <see cref="CalculateHolidayYear"/>).
    /// </summary>
    public static IReadOnlyList<AllocatedDay> Allocate(VacationData data, DateOnly today) => new Ledger(data, today).Allocations;

    /// <summary>
    /// Balance of <paramref name="year"/> as of <paramref name="today"/>.
    /// Transfers start at the first tracked holiday year (<see cref="VacationData.EffectiveFirstTrackedHolidayYear"/>):
    /// its transferred-in days are <see cref="VacationData.OpeningBalanceDays"/>, and each later year's are the days the
    /// year before transfers. Earlier years have nothing transferred in and transfer nothing.
    /// </summary>
    public static HolidayYearBalance CalculateHolidayYear(VacationData data, HolidayYear year, DateOnly today) =>
        new Ledger(data, today).Balance(year, today);

    /// <summary>Balances of several holiday years as of <paramref name="today"/>, in the given order.</summary>
    public static IReadOnlyList<HolidayYearBalance> CalculateHolidayYears(
        VacationData data, IEnumerable<HolidayYear> years, DateOnly today)
    {
        var ledger = new Ledger(data, today);
        return years.Select(y => ledger.Balance(y, today)).ToList();
    }

    /// <summary>
    /// Status of calendar year <paramref name="year"/>: the balance of the holiday year with its deadline on
    /// 31 December <paramref name="year"/>, compared with the free working days left in <paramref name="year"/>.
    /// Free working days run from today (inclusive) through 31 December, the whole year if today is before it
    /// and none if after it; a half-booked day counts 0.5 and a fully booked day (either type) 0.
    /// </summary>
    public static CalendarYearStatus CalculateCalendarYear(VacationData data, int year, DateOnly today)
    {
        var holidayYear = HolidayYear.WithDeadlineIn(year);
        var ledger = new Ledger(data, today);
        var balance = ledger.Balance(holidayYear, today);
        var holidays = data.AllPublicHolidays();

        var byDate = new Dictionary<DateOnly, BookedDay>();
        foreach (var day in data.Days ?? [])
            byDate.TryAdd(day.Date, day);

        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);
        var firstOpenDay = today < yearStart ? yearStart : today;
        decimal workingDaysLeft = 0;
        for (var n = firstOpenDay.DayNumber; n <= yearEnd.DayNumber; n++)
        {
            var d = DateOnly.FromDayNumber(n);
            if (!IsWorkingDay(d, holidays))
                continue;
            var booked = byDate.TryGetValue(d, out var day) ? day.Amount : 0;
            workingDaysLeft += Math.Clamp(1 - booked, 0, 1);
        }

        // Booking every free working day left uses this holiday year (it is the older open year and has days
        // left), so what can't fit is the deadline split with those days booked as well.
        var atRisk = balance.UnbookedDays <= workingDaysLeft
            ? default
            : balance.Settings.Split(
                balance.TransferredIn, balance.EarnableDays, balance.BookedDays + workingDaysLeft,
                ledger.TransfersOut(holidayYear));

        var status = balance.UnbookedDays switch
        {
            < 0 => BalanceStatus.OverBooked,
            _ when balance.ExpiringDays == 0 => BalanceStatus.AllPlanned,
            _ when today > balance.Deadline => BalanceStatus.Expired,
            _ when atRisk.PaidOutDays + atRisk.LostDays > 0 => BalanceStatus.DaysWillBeLost,
            _ => BalanceStatus.DaysToPlan,
        };

        return new CalendarYearStatus(year, balance, workingDaysLeft, status, atRisk);
    }

    /// <summary>Allocation of all bookings plus the per-holiday-year totals and deadline splits derived from it.</summary>
    private sealed class Ledger
    {
        private readonly VacationData _data;
        private readonly Dictionary<HolidayYear, decimal> _allocated = [];
        private readonly Dictionary<HolidayYear, DeadlineSettings> _settings = [];
        private readonly Dictionary<int, decimal> _transferredOut = [];

        /// <summary>Start year of the first tracked holiday year: it receives the opening balance; earlier years transfer nothing.</summary>
        private readonly int _firstTracked;

        public List<AllocatedDay> Allocations { get; } = [];

        public Ledger(VacationData data, DateOnly today)
        {
            _data = data;
            _firstTracked = data.EffectiveFirstTrackedHolidayYear(today).StartYear;
            var holidays = data.AllPublicHolidays();

            var byDate = new Dictionary<DateOnly, BookedDay>();
            foreach (var day in data.Days ?? [])
                byDate.TryAdd(day.Date, day);

            var vacation = byDate.Values
                .Where(d => d.Type == DayType.Vacation && d.Amount > 0 && IsWorkingDay(d.Date, holidays))
                // Dates whose holiday year can't be represented (before 0001-09-01, from 9999-09-01) are ignored.
                .Where(d => HolidayYear.Containing(d.Date).StartYear is >= HolidayYear.MinStartYear and <= HolidayYear.MaxStartYear)
                .OrderBy(d => d.Date)
                .ToList();

            foreach (var day in vacation)
            {
                var current = HolidayYear.Containing(day.Date);
                var amount = day.Amount;
                if (day.Date.Month >= 9 && current.StartYear > HolidayYear.MinStartYear)
                {
                    // Two years are open; the older one goes first. Its total depends only on transfers from
                    // years whose deadlines have passed (all their days are allocated), so it is final here.
                    var older = current.Previous;
                    var take = Math.Clamp(Total(older) - Allocated(older), 0, amount);
                    if (take > 0)
                        Add(day.Date, older, take);
                    amount -= take;
                }
                if (amount > 0)
                    Add(day.Date, current, amount);
            }
        }

        public HolidayYearBalance Balance(HolidayYear year, DateOnly today)
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

            var outcome = Outcome(year);

            return new HolidayYearBalance(
                year,
                EarnedSoFar: Accrual.EarnedAsOf(year, _data.EmployedSince, today),
                EarnableDays: Earnable(year),
                TransferredIn: TransferredIn(year),
                UsedDays: used,
                BookedAheadDays: ahead,
                FifthWeekDays: Math.Min(Math.Max(0, Earnable(year) - Accrual.FourWeeks), Accrual.DaysPerFullYear - Accrual.FourWeeks),
                // Without transfer, all unused own 5th-week days are paid out.
                UnusedFifthWeekDays: Outcome(year, transferAllowed: false).PaidOutFifthWeekDays,
                AtDeadline: outcome,
                Settings: Settings(year),
                TransferredInIsProjected: ReceivesTransfer(year) && today <= year.Previous.Deadline);
        }

        private DeadlineOutcome Outcome(HolidayYear year, bool? transferAllowed = null) =>
            Settings(year).Split(TransferredIn(year), Earnable(year), Allocated(year), transferAllowed ?? TransfersOut(year));

        /// <summary>Whether <paramref name="year"/> receives the previous year's transfer (as opposed to the opening balance).</summary>
        private bool ReceivesTransfer(HolidayYear year) => year.StartYear > _firstTracked && TransfersOut(year.Previous);

        /// <summary>Whether <paramref name="year"/> transfers at its deadline: transfer is on and the year is tracked.</summary>
        public bool TransfersOut(HolidayYear year) => _data.TransferAllowed && year.StartYear >= _firstTracked;

        private void Add(DateOnly date, HolidayYear year, decimal amount)
        {
            Allocations.Add(new AllocatedDay(date, year, amount));
            _allocated[year] = Allocated(year) + amount;
        }

        private decimal Allocated(HolidayYear year) => _allocated.GetValueOrDefault(year);

        private decimal Earnable(HolidayYear year) => Accrual.EarnableDays(year, _data.EmployedSince);

        private DeadlineSettings Settings(HolidayYear year)
        {
            if (!_settings.TryGetValue(year, out var settings))
                _settings[year] = settings = _data.GetDeadlineSettings(year);
            return settings;
        }

        /// <summary>
        /// Days transferred into <paramref name="year"/>: the opening balance for the first tracked year, the year
        /// before's transfer for later years, nothing for earlier years.
        /// </summary>
        private decimal TransferredIn(HolidayYear year) =>
            year.StartYear < _firstTracked ? 0
            : year.StartYear == _firstTracked ? _data.OpeningBalanceDays
            : TransferredOut(year.Previous);

        private decimal Total(HolidayYear year) => Earnable(year) + TransferredIn(year);

        /// <summary>
        /// Days <paramref name="year"/> transfers to the next year at its deadline: its own unused days up to its limit
        /// (bookings use transferred-in days first, so these depend on the transfer into it).
        /// Resolved chronologically from the first tracked year (iteratively, no recursion), so each year's
        /// transfer depends only on earlier years. During allocation it is only needed for years whose deadline
        /// is before the day being allocated, whose allocations are therefore complete.
        /// </summary>
        private decimal TransferredOut(HolidayYear year)
        {
            if (!TransfersOut(year))
                return 0;
            if (_transferredOut.TryGetValue(year.StartYear, out var cached))
                return cached;

            var from = _firstTracked;
            while (from < year.StartYear && _transferredOut.ContainsKey(from))
                from++;

            decimal result = 0;
            for (var y = from; y <= year.StartYear; y++)
            {
                var hy = new HolidayYear(y);
                result = Outcome(hy, transferAllowed: true).TransferredDays;
                _transferredOut[y] = result;
            }
            return result;
        }
    }
}
