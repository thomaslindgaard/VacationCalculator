namespace VacationCalculator.Core;

public enum BalanceStatus
{
    /// <summary>Every day of the holiday year is used, booked or transferred.</summary>
    AllPlanned,
    /// <summary>Unbooked days remain and there is still time to book them.</summary>
    DaysToPlan,
    /// <summary>More unbooked days remain than there are free working days left in the calendar year (today included).</summary>
    DaysWillBeLost,
    /// <summary>More days are booked than the holiday year can earn by 31 August (plus transferred-in days).</summary>
    OverBooked,
    /// <summary>The deadline has passed and some days were never used.</summary>
    Expired,
}

/// <summary>
/// Part of a booked vacation day assigned to a holiday year. A full day can be split into two halves
/// when the older holiday year has only half a day left.
/// </summary>
public sealed record AllocatedDay(DateOnly Date, HolidayYear HolidayYear, decimal Amount);

/// <summary>Balance of one holiday year as of a given day. Allocated days are projected from all bookings.</summary>
/// <param name="EarnedSoFar">Own days usable as of today (completed months only).</param>
/// <param name="EarnableDays">Own days earned by 31 August, i.e. for the full holiday year (25 when employed all year).</param>
/// <param name="TransferredIn">5th-week days transferred in from the previous holiday year.</param>
/// <param name="UsedDays">Vacation allocated to this year on or before today.</param>
/// <param name="BookedAheadDays">Vacation allocated to this year after today.</param>
/// <param name="FifthWeekDays">The year's 5th-week days: own earnings above 20 plus transferred-in days.</param>
/// <param name="UnusedFifthWeekDays">5th-week days not used or booked (before any transfer out).</param>
/// <param name="MaxTransferToNext">The most that may be transferred to the next year: <see cref="UnusedFifthWeekDays"/> when the setting is on, else 0.</param>
/// <param name="TransferredToNext">Transferred to the next year: the stored value clamped to [0, <see cref="MaxTransferToNext"/>]; 0 when the setting is off.</param>
/// <param name="PaidOutDays">Unused days paid out at the deadline if nothing more is booked (unused 5th week not transferred).</param>
/// <param name="LostDays">Unused days lost at the deadline if nothing more is booked (unused days within the first 20).</param>
public sealed record HolidayYearBalance(
    HolidayYear HolidayYear,
    decimal EarnedSoFar,
    decimal EarnableDays,
    decimal TransferredIn,
    decimal UsedDays,
    decimal BookedAheadDays,
    decimal FifthWeekDays,
    decimal UnusedFifthWeekDays,
    decimal MaxTransferToNext,
    decimal TransferredToNext,
    decimal PaidOutDays,
    decimal LostDays)
{
    public DateOnly Deadline => HolidayYear.Deadline;

    /// <summary>Everything the year can provide: <see cref="EarnableDays"/> + <see cref="TransferredIn"/>.</summary>
    public decimal TotalDays => EarnableDays + TransferredIn;

    public decimal BookedDays => UsedDays + BookedAheadDays;

    /// <summary>
    /// Days available now that are not used, booked or transferred:
    /// earned so far + transferred in - transferred out - used - booked ahead. Negative = advance holiday.
    /// </summary>
    public decimal RemainingDays => EarnedSoFar + TransferredIn - TransferredToNext - BookedDays;

    /// <summary>By how much bookings exceed the days earned so far (0 if they don't).</summary>
    public decimal ExceedsEarnedSoFarBy => Math.Max(0, -RemainingDays);

    /// <summary>
    /// Days left to book before the deadline: <see cref="TotalDays"/> - booked - transferred out.
    /// Negative when more is booked than the year can earn; otherwise equal to paid out + lost.
    /// </summary>
    public decimal UnbookedDays => TotalDays - BookedDays - TransferredToNext;

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
/// <param name="TransferOptionApplies">
/// Whether transferring 5th-week days is an option: the setting is on, some 5th-week days are unused
/// and today is on or before the deadline. Check <see cref="HolidayYearBalance.TransferredToNext"/>
/// to see whether a transfer is already registered.
/// </param>
public sealed record CalendarYearStatus(
    int Year,
    HolidayYearBalance HolidayYear,
    decimal WorkingDaysLeft,
    BalanceStatus Status,
    bool TransferOptionApplies)
{
    public DateOnly Deadline => HolidayYear.Deadline;

    /// <inheritdoc cref="HolidayYearBalance.UnbookedDays"/>
    public decimal UnbookedDays => HolidayYear.UnbookedDays;

    /// <summary>Days that cannot be fitted before the deadline, even taking every free working day left.</summary>
    public decimal DaysAtRisk => Math.Max(0, UnbookedDays - WorkingDaysLeft);

    /// <summary>Unused days paid out at the deadline if nothing more is booked.</summary>
    public decimal PaidOutDays => HolidayYear.PaidOutDays;

    /// <summary>Unused days lost at the deadline if nothing more is booked.</summary>
    public decimal LostDays => HolidayYear.LostDays;

    /// <summary>
    /// Of <see cref="DaysAtRisk"/>, the days that would be paid out (5th week) even if every free working day
    /// left were booked. Booked days use the first 20 days before the 5th week.
    /// </summary>
    public decimal AtRiskPaidOutDays => AtRiskFifthWeek() - HolidayYear.TransferredToNext;

    /// <summary>Of <see cref="DaysAtRisk"/>, the days that would be lost even if every free working day left were booked.</summary>
    public decimal AtRiskLostDays => DaysAtRisk - AtRiskPaidOutDays;

    private decimal AtRiskFifthWeek() =>
        Math.Min(DaysAtRisk + HolidayYear.TransferredToNext, HolidayYear.FifthWeekDays);
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
    /// Assigns every booked <see cref="DayType.Vacation"/> day on a working day to a holiday year, in date order.
    /// From January to August only the holiday year that started the previous September is open. From
    /// September to December the older open year takes the day while it has days left (earnable for the
    /// full year plus transferred in), otherwise the newer one does; a full day may be split 0.5 / 0.5.
    /// Days are never refused: the newest open year takes anything that doesn't fit.
    /// If <see cref="VacationData.Days"/> has several records for a date, the first wins.
    /// </summary>
    public static IReadOnlyList<AllocatedDay> Allocate(VacationData data) => new Ledger(data).Allocations;

    /// <summary>Balance of <paramref name="year"/> as of <paramref name="today"/>.</summary>
    public static HolidayYearBalance CalculateHolidayYear(VacationData data, HolidayYear year, DateOnly today) =>
        new Ledger(data).Balance(year, today);

    /// <summary>Balances of several holiday years as of <paramref name="today"/>, in the given order.</summary>
    public static IReadOnlyList<HolidayYearBalance> CalculateHolidayYears(
        VacationData data, IEnumerable<HolidayYear> years, DateOnly today)
    {
        var ledger = new Ledger(data);
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
        var balance = new Ledger(data).Balance(holidayYear, today);
        var holidays = data.AllPublicHolidays();

        var byDate = new Dictionary<DateOnly, BookedDay>();
        foreach (var day in data.Days ?? [])
            byDate.TryAdd(day.Date, day);

        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);
        var firstOpenDay = today < yearStart ? yearStart : today;
        decimal workingDaysLeft = 0;
        for (var d = firstOpenDay; d <= yearEnd; d = d.AddDays(1))
        {
            if (!IsWorkingDay(d, holidays))
                continue;
            var booked = byDate.TryGetValue(d, out var day) ? day.Amount : 0;
            workingDaysLeft += Math.Clamp(1 - booked, 0, 1);
        }

        var unbooked = balance.UnbookedDays;
        var status = unbooked switch
        {
            < 0 => BalanceStatus.OverBooked,
            0 => BalanceStatus.AllPlanned,
            _ when today > balance.Deadline => BalanceStatus.Expired,
            _ when unbooked > workingDaysLeft => BalanceStatus.DaysWillBeLost,
            _ => BalanceStatus.DaysToPlan,
        };

        var transferApplies = data.FifthWeekTransferAllowed && balance.UnusedFifthWeekDays > 0 && today <= balance.Deadline;
        return new CalendarYearStatus(year, balance, workingDaysLeft, status, transferApplies);
    }

    /// <summary>Allocation of all bookings plus the per-holiday-year totals derived from it.</summary>
    private sealed class Ledger
    {
        private readonly VacationData _data;
        private readonly Dictionary<HolidayYear, decimal> _allocated = [];
        private readonly Dictionary<HolidayYear, decimal> _transferredOut = [];

        public List<AllocatedDay> Allocations { get; } = [];

        public Ledger(VacationData data)
        {
            _data = data;
            var holidays = data.AllPublicHolidays();

            var byDate = new Dictionary<DateOnly, BookedDay>();
            foreach (var day in data.Days ?? [])
                byDate.TryAdd(day.Date, day);

            var vacation = byDate.Values
                .Where(d => d.Type == DayType.Vacation && d.Amount > 0 && IsWorkingDay(d.Date, holidays))
                .OrderBy(d => d.Date);

            foreach (var day in vacation)
            {
                var current = HolidayYear.Containing(day.Date);
                var amount = day.Amount;
                if (day.Date.Month >= 9)
                {
                    // Two years are open; the older one goes first. Its total depends only on years
                    // whose deadlines have passed, so it is final here.
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

            var unusedFifthWeek = UnusedFifthWeek(year);
            var transferredOut = TransferredOut(year);
            var unused = Math.Max(0, Total(year) - Allocated(year));

            return new HolidayYearBalance(
                year,
                EarnedSoFar: Accrual.EarnedAsOf(year, _data.EmployedSince, today),
                EarnableDays: Earnable(year),
                TransferredIn: TransferredIn(year),
                UsedDays: used,
                BookedAheadDays: ahead,
                FifthWeekDays: FifthWeek(year),
                UnusedFifthWeekDays: unusedFifthWeek,
                MaxTransferToNext: MaxTransfer(year),
                TransferredToNext: transferredOut,
                PaidOutDays: unusedFifthWeek - transferredOut,
                LostDays: unused - unusedFifthWeek);
        }

        private void Add(DateOnly date, HolidayYear year, decimal amount)
        {
            Allocations.Add(new AllocatedDay(date, year, amount));
            _allocated[year] = Allocated(year) + amount;
        }

        private decimal Allocated(HolidayYear year) => _allocated.GetValueOrDefault(year);

        private decimal Earnable(HolidayYear year) => Accrual.EarnableDays(year, _data.EmployedSince);

        private decimal TransferredIn(HolidayYear year) => TransferredOut(year.Previous);

        private decimal Total(HolidayYear year) => Earnable(year) + TransferredIn(year);

        /// <summary>Own earnings above 20 plus transferred-in days (which are 5th-week days themselves).</summary>
        private decimal FifthWeek(HolidayYear year) =>
            Math.Max(0, Earnable(year) - Accrual.FourWeeks) + TransferredIn(year);

        /// <summary>Booked days use the first 20 days first, so the unused days are 5th-week days first.</summary>
        private decimal UnusedFifthWeek(HolidayYear year) =>
            Math.Min(Math.Max(0, Total(year) - Allocated(year)), FifthWeek(year));

        private decimal MaxTransfer(HolidayYear year) =>
            _data.FifthWeekTransferAllowed ? UnusedFifthWeek(year) : 0;

        /// <summary>
        /// The stored transfer clamped to [0, max]. Recurses only through consecutive years with a stored
        /// transfer, and is only called (during allocation) for years whose deadline has passed.
        /// </summary>
        private decimal TransferredOut(HolidayYear year)
        {
            if (!_data.FifthWeekTransferAllowed)
                return 0;
            if (_transferredOut.TryGetValue(year, out var cached))
                return cached;

            var stored = _data.FindHolidayYear(year.StartYear)?.TransferredToNext ?? 0;
            var result = stored <= 0 ? 0 : Math.Min(stored, MaxTransfer(year));
            _transferredOut[year] = result;
            return result;
        }
    }
}
