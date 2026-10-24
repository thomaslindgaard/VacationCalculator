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
/// What happens at a holiday year's deadline to its own unused days beyond the 5th week (the first 20) that are not
/// transferred. Unused 5th-week days are always paid out and unused transferred-in days always lost, whatever the mode.
/// </summary>
public enum PayoutMode
{
    /// <summary>Nothing beyond the 5th week is paid out; it is lost.</summary>
    None,
    /// <summary>Up to <see cref="HolidayYearSettings.PayoutDays"/> days beyond the 5th week are paid out; the rest is lost.</summary>
    Some,
    /// <summary>Every unused day beyond the 5th week is paid out.</summary>
    All,
}

/// <summary>
/// User-entered settings for one holiday year (ferieår), identified by the calendar year it starts in:
/// holiday year 2025 runs 1 Sep 2025 to 31 Aug 2026 (see <see cref="HolidayYear"/>).
/// A null value means "not set for this year": the nearest earlier holiday year's value applies, or the
/// default (see <see cref="VacationData.GetDeadlineSettings"/>). Older files may also contain a
/// <c>TransferredToNext</c> value per entry; it is no longer used and is ignored on load.
/// </summary>
public sealed class HolidayYearSettings
{
    public int StartYear { get; set; }

    /// <summary>
    /// Most days that may be transferred to the next holiday year at this year's deadline; 0 = no transfer.
    /// Null = inherited (see <see cref="VacationData.GetDeadlineSettings"/>; default <see cref="VacationData.DefaultTransferLimitDays"/>).
    /// </summary>
    public decimal? TransferLimitDays { get; set; }

    /// <summary>Payout of unused, non-transferred days at this year's deadline. Null = inherited.</summary>
    public PayoutMode? PayoutMode { get; set; }

    /// <summary>The N in "some N days paid out"; only used when <see cref="PayoutMode"/> is <see cref="Core.PayoutMode.Some"/>.</summary>
    public decimal? PayoutDays { get; set; }

    /// <summary>
    /// Feriefri days this holiday year provides, available in full from 1 September. Null = inherited
    /// (see <see cref="VacationData.GetFeriefriAllowance"/>). Missing in older files, which then load as null.
    /// </summary>
    public decimal? FeriefriAllowanceDays { get; set; }
}

public sealed class VacationData
{
    /// <summary>Holiday Act default: up to 5 days (the 5th week) can be transferred.</summary>
    public const decimal DefaultTransferLimitDays = 5m;

    /// <summary>Booked days, at most one per date.</summary>
    public List<BookedDay> Days { get; set; } = [];

    /// <summary>Per calendar year settings (public holidays).</summary>
    public List<YearSettings> Years { get; set; } = [];

    /// <summary>
    /// First day of employment; accrual starts on this date. Null means employed since before any
    /// holiday year being tracked.
    /// </summary>
    public DateOnly? EmployedSince { get; set; }

    // Older files may contain "TransferAllowed" or "FifthWeekTransferAllowed", an employment-wide transfer switch.
    // It was removed: each holiday year's transfer limit alone decides (0 = no transfer). Unknown JSON properties
    // are ignored on load, so those files still load; the value is dropped on the next save.

    /// <summary>Per holiday year settings, at most one per <see cref="HolidayYearSettings.StartYear"/>.</summary>
    public List<HolidayYearSettings> HolidayYears { get; set; } = [];

    /// <summary>
    /// Start year of the first holiday year whose transfers are calculated (e.g. 2025 for 2025/26). Its transferred-in
    /// days are <see cref="OpeningBalanceDays"/>; earlier years have nothing transferred in and transfer nothing.
    /// Null = not stored yet: <see cref="DefaultFirstTrackedHolidayYear"/> of today applies
    /// (see <see cref="EffectiveFirstTrackedHolidayYear"/>, <see cref="EnsureFirstTrackedHolidayYear"/>).
    /// </summary>
    public int? FirstTrackedHolidayYear { get; set; }

    private decimal _openingBalanceDays;

    /// <summary>
    /// Days transferred into the first tracked holiday year (the opening balance), default 0. Like any transferred-in
    /// days they are used first in that year, lost if unused and never transferred again. Negative values store as 0.
    /// Applies whatever the transfer limits are (it is a fact about the past).
    /// </summary>
    public decimal OpeningBalanceDays
    {
        get => _openingBalanceDays;
        set => _openingBalanceDays = Math.Max(0, value);
    }

    /// <summary>
    /// The default first tracked holiday year: the oldest holiday year open on <paramref name="today"/>, clamped into
    /// <see cref="HolidayYear.MinStartYear"/>..<see cref="HolidayYear.MaxStartYear"/> (a clock in year 1 before
    /// 1 September, or any day in September..December of year 1, would otherwise give holiday year 0).
    /// </summary>
    public static HolidayYear DefaultFirstTrackedHolidayYear(DateOnly today) =>
        new(Math.Clamp(HolidayYear.OpenOn(today)[0].StartYear, HolidayYear.MinStartYear, HolidayYear.MaxStartYear));

    /// <summary>
    /// The first tracked holiday year that applies: <see cref="FirstTrackedHolidayYear"/> if stored, otherwise
    /// <see cref="DefaultFirstTrackedHolidayYear"/> of <paramref name="today"/>. A stored value outside
    /// <see cref="HolidayYear.MinStartYear"/>..<see cref="HolidayYear.MaxStartYear"/> (e.g. a hand-edited 0) is clamped
    /// into that range; the stored value itself is left as it is.
    /// </summary>
    public HolidayYear EffectiveFirstTrackedHolidayYear(DateOnly today) =>
        FirstTrackedHolidayYear is { } startYear
            ? new HolidayYear(Math.Clamp(startYear, HolidayYear.MinStartYear, HolidayYear.MaxStartYear))
            : DefaultFirstTrackedHolidayYear(today);

    /// <summary>
    /// Stores <see cref="DefaultFirstTrackedHolidayYear"/> of <paramref name="today"/> as <see cref="FirstTrackedHolidayYear"/>
    /// if none is stored, so the first tracked year no longer moves as time passes. Returns whether it changed anything.
    /// </summary>
    public bool EnsureFirstTrackedHolidayYear(DateOnly today)
    {
        if (FirstTrackedHolidayYear is not null)
            return false;
        FirstTrackedHolidayYear = DefaultFirstTrackedHolidayYear(today).StartYear;
        return true;
    }

    /// <summary>
    /// Every manual (user-entered) public holiday across all years, each taken only from the settings of the year it
    /// falls in. Suitable for booking ranges that cross New Year. The automatic Danish holidays are not in this set:
    /// <see cref="BalanceCalculator.IsWorkingDay"/> always applies them on top of it. To ask whether a date is any kind
    /// of holiday use <see cref="IsPublicHoliday"/> or <see cref="FindPublicHoliday"/>.
    /// </summary>
    public HashSet<DateOnly> AllPublicHolidays() =>
        (Years ?? []).SelectMany(y => (y.PublicHolidays ?? []).Where(h => h.Year == y.Year)).ToHashSet();

    /// <summary>Whether <paramref name="date"/> is in the manual public holidays of its own calendar year.</summary>
    public bool IsManualPublicHoliday(DateOnly date) =>
        (Years ?? []).Any(y => y is not null && y.Year == date.Year && (y.PublicHolidays ?? []).Contains(date));

    /// <summary>Whether <paramref name="date"/> is a public holiday, automatic (Danish) or manual.</summary>
    public bool IsPublicHoliday(DateOnly date) => DanishHolidays.IsHoliday(date) || IsManualPublicHoliday(date);

    /// <summary>
    /// The public holiday on <paramref name="date"/> with its name and kind, or null if the date is neither an
    /// automatic Danish holiday nor a manual one. A date that is both gives one entry with both flags set.
    /// </summary>
    public PublicHoliday? FindPublicHoliday(DateOnly date)
    {
        var name = DanishHolidays.NameOf(date);
        var manual = IsManualPublicHoliday(date);
        return name is null && !manual ? null : new PublicHoliday(date, name, name is not null, manual);
    }

    /// <summary>
    /// All public holidays of calendar year <paramref name="year"/> (1..9999), automatic and manual, one entry per date in
    /// date order. Manual dates stored under a year they don't fall in are ignored, as everywhere else.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="year"/> is outside 1..9999.</exception>
    public IReadOnlyList<PublicHoliday> PublicHolidaysIn(int year)
    {
        var manual = (Years ?? [])
            .Where(y => y is not null && y.Year == year)
            .SelectMany(y => y.PublicHolidays ?? [])
            .Where(d => d.Year == year)
            .ToHashSet();
        var result = new Dictionary<DateOnly, PublicHoliday>();
        foreach (var h in DanishHolidays.For(year))
            result[h.Date] = new PublicHoliday(h.Date, h.Name, IsAutomatic: true, IsManual: manual.Contains(h.Date));
        foreach (var d in manual)
            result.TryAdd(d, new PublicHoliday(d, null, IsAutomatic: false, IsManual: true));
        return result.Values.OrderBy(h => h.Date).ToList();
    }

    /// <summary>
    /// Booked records that fall on a public holiday (automatic or manual), e.g. a day booked before a manual holiday was
    /// added on it; they are not counted as vacation or Feriefri. Only the first record per date is considered (as in
    /// the calculators); sorted by date. With <paramref name="year"/>, only records in that calendar year.
    /// </summary>
    public IReadOnlyList<BookedDay> BookedDaysOnHolidays(int? year = null)
    {
        var manual = AllPublicHolidays();
        var seen = new HashSet<DateOnly>();
        return (Days ?? [])
            .Where(d => d is not null && seen.Add(d.Date))
            .Where(d => year is null || d.Date.Year == year)
            .Where(d => manual.Contains(d.Date) || DanishHolidays.IsHoliday(d.Date))
            .OrderBy(d => d.Date)
            .ToList();
    }

    /// <summary>The settings for the holiday year starting in <paramref name="startYear"/>, or null if none are stored.</summary>
    public HolidayYearSettings? FindHolidayYear(int startYear) =>
        (HolidayYears ?? []).FirstOrDefault(h => h is not null && h.StartYear == startYear);

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

    /// <summary>
    /// The transfer limit and payout setting that apply at <paramref name="year"/>'s deadline. Each of the two
    /// is taken from the year itself if set there, otherwise from the nearest earlier holiday year that sets it,
    /// otherwise the default (<see cref="DefaultTransferLimitDays"/>, <see cref="PayoutMode.None"/>).
    /// Negative stored numbers read as 0. Reading never adds entries.
    /// </summary>
    public DeadlineSettings GetDeadlineSettings(HolidayYear year)
    {
        // First entry per start year wins, as in FindHolidayYear.
        var entries = (HolidayYears ?? [])
            .Where(h => h is not null && h.StartYear <= year.StartYear)
            .GroupBy(h => h.StartYear)
            .Select(g => g.First())
            .OrderByDescending(h => h.StartYear)
            .ToList();

        var limitEntry = entries.FirstOrDefault(h => h.TransferLimitDays is not null);
        var payoutEntry = entries.FirstOrDefault(h => h.PayoutMode is not null);

        var payoutMode = payoutEntry?.PayoutMode ?? PayoutMode.None;
        if (!Enum.IsDefined(payoutMode))
            payoutMode = PayoutMode.None;

        return new DeadlineSettings(
            year,
            TransferLimitDays: Math.Max(0, limitEntry?.TransferLimitDays ?? DefaultTransferLimitDays),
            TransferLimitFrom: limitEntry is null ? null : new HolidayYear(limitEntry.StartYear),
            PayoutMode: payoutMode,
            PayoutDays: payoutMode == PayoutMode.Some ? Math.Max(0, payoutEntry?.PayoutDays ?? 0) : 0,
            PayoutFrom: payoutEntry is null ? null : new HolidayYear(payoutEntry.StartYear));
    }

    /// <summary>
    /// Stores <paramref name="days"/> (negative stored as 0) as the transfer limit of <paramref name="year"/> only.
    /// Earlier years are unaffected; later years without their own value inherit it.
    /// </summary>
    public void SetTransferLimit(HolidayYear year, decimal days) =>
        GetOrAddHolidayYear(year.StartYear).TransferLimitDays = Math.Max(0, days);

    /// <summary>
    /// Stores the payout setting of <paramref name="year"/> only. <paramref name="payoutDays"/> (negative stored
    /// as 0) is kept for <see cref="PayoutMode.Some"/> and ignored otherwise.
    /// </summary>
    public void SetPayout(HolidayYear year, PayoutMode mode, decimal payoutDays = 0)
    {
        var settings = GetOrAddHolidayYear(year.StartYear);
        settings.PayoutMode = mode;
        settings.PayoutDays = mode == PayoutMode.Some ? Math.Max(0, payoutDays) : null;
    }

    /// <summary>Removes <paramref name="year"/>'s own transfer limit, so it inherits again. Does nothing if none is stored.</summary>
    public void ClearTransferLimit(HolidayYear year)
    {
        if (FindHolidayYear(year.StartYear) is { } settings)
            settings.TransferLimitDays = null;
    }

    /// <summary>Removes <paramref name="year"/>'s own payout setting, so it inherits again. Does nothing if none is stored.</summary>
    public void ClearPayout(HolidayYear year)
    {
        if (FindHolidayYear(year.StartYear) is { } settings)
        {
            settings.PayoutMode = null;
            settings.PayoutDays = null;
        }
    }

    /// <summary>Feriefri days per holiday year when no year sets its own value.</summary>
    public const decimal DefaultFeriefriAllowanceDays = 5m;

    /// <summary>Largest Feriefri allowance a holiday year can have.</summary>
    public const decimal MaxFeriefriAllowanceDays = 20m;

    /// <summary>
    /// The Feriefri allowance of <paramref name="year"/>: its own value if set, otherwise the nearest earlier holiday
    /// year's, otherwise <see cref="DefaultFeriefriAllowanceDays"/>. Stored values outside 0..<see cref="MaxFeriefriAllowanceDays"/>
    /// (hand-edited files) read clamped into that range. Reading never adds entries.
    /// </summary>
    public FeriefriAllowance GetFeriefriAllowance(HolidayYear year)
    {
        // First entry per start year wins, as in FindHolidayYear.
        var entry = (HolidayYears ?? [])
            .Where(h => h is not null && h.StartYear <= year.StartYear)
            .GroupBy(h => h.StartYear)
            .Select(g => g.First())
            .Where(h => h.FeriefriAllowanceDays is not null)
            .OrderByDescending(h => h.StartYear)
            .FirstOrDefault();

        return new FeriefriAllowance(
            year,
            Math.Clamp(entry?.FeriefriAllowanceDays ?? DefaultFeriefriAllowanceDays, 0, MaxFeriefriAllowanceDays),
            entry is null ? null : new HolidayYear(entry.StartYear));
    }

    /// <summary>
    /// Stores <paramref name="days"/>, clamped to 0..<see cref="MaxFeriefriAllowanceDays"/>, as the Feriefri allowance of
    /// <paramref name="year"/> only. Earlier years are unaffected; later years without their own value inherit it.
    /// Existing bookings are kept even if they now exceed the allowance.
    /// </summary>
    public void SetFeriefriAllowance(HolidayYear year, decimal days) =>
        GetOrAddHolidayYear(year.StartYear).FeriefriAllowanceDays = Math.Clamp(days, 0, MaxFeriefriAllowanceDays);

    /// <summary>Removes <paramref name="year"/>'s own Feriefri allowance, so it inherits again. Does nothing if none is stored.</summary>
    public void ClearFeriefriAllowance(HolidayYear year)
    {
        if (FindHolidayYear(year.StartYear) is { } settings)
            settings.FeriefriAllowanceDays = null;
    }
}

/// <summary>The Feriefri allowance that applies to <see cref="HolidayYear"/> (see <see cref="VacationData.GetFeriefriAllowance"/>).</summary>
/// <param name="From">The holiday year the value is stored on; null = the default.</param>
public sealed record FeriefriAllowance(HolidayYear HolidayYear, decimal Days, HolidayYear? From)
{
    /// <summary>Whether the value is stored on this holiday year itself.</summary>
    public bool IsOwn => From == HolidayYear;

    /// <summary>Whether the value is inherited from an earlier holiday year.</summary>
    public bool IsInherited => From is not null && From != HolidayYear;

    /// <summary>Whether no year up to this one sets a value, so <see cref="VacationData.DefaultFeriefriAllowanceDays"/> applies.</summary>
    public bool IsDefault => From is null;
}

/// <summary>How the unused days of a holiday year are split at its deadline.</summary>
/// <param name="TransferredDays">
/// Own unused days transferred to the next holiday year: 5th-week days, plus days from the first 20 only through
/// the part of the limit above 5.
/// </param>
/// <param name="PaidOutFifthWeekDays">Own unused 5th-week days not transferred: always paid out (Holiday Act).</param>
/// <param name="PaidOutOtherDays">Own unused days beyond the 5th week paid out under the payout setting.</param>
/// <param name="LostTransferredInDays">Days transferred in from the previous holiday year and not used: always lost.</param>
/// <param name="LostOtherDays">Own unused days beyond the 5th week that are neither transferred nor paid out.</param>
public readonly record struct DeadlineOutcome(
    decimal TransferredDays,
    decimal PaidOutFifthWeekDays,
    decimal PaidOutOtherDays,
    decimal LostTransferredInDays,
    decimal LostOtherDays)
{
    public decimal PaidOutDays => PaidOutFifthWeekDays + PaidOutOtherDays;
    public decimal LostDays => LostTransferredInDays + LostOtherDays;

    /// <summary>All unused days: transferred + paid out + lost.</summary>
    public decimal UnusedDays => TransferredDays + PaidOutDays + LostDays;
}

/// <summary>
/// The effective transfer limit and payout setting for <see cref="HolidayYear"/>'s deadline
/// (see <see cref="VacationData.GetDeadlineSettings"/>).
/// </summary>
/// <param name="TransferLimitFrom">The holiday year the limit is stored on; null = the default.</param>
/// <param name="PayoutDays">The N for <see cref="PayoutMode.Some"/>; 0 for the other modes.</param>
/// <param name="PayoutFrom">The holiday year the payout setting is stored on; null = the default.</param>
public sealed record DeadlineSettings(
    HolidayYear HolidayYear,
    decimal TransferLimitDays,
    HolidayYear? TransferLimitFrom,
    PayoutMode PayoutMode,
    decimal PayoutDays,
    HolidayYear? PayoutFrom)
{
    /// <summary>Whether the transfer limit is stored on this holiday year (not inherited or default).</summary>
    public bool TransferLimitIsOwn => TransferLimitFrom == HolidayYear;

    /// <summary>Whether the payout setting is stored on this holiday year (not inherited or default).</summary>
    public bool PayoutIsOwn => PayoutFrom == HolidayYear;

    /// <summary>
    /// Splits a holiday year's unused days at its deadline. Bookings (<paramref name="bookedDays"/>) use the
    /// <paramref name="transferredInDays"/> first, then the year's own days (<paramref name="ownDays"/>, the
    /// earnable days), and among those the first 20 before the 5th week. Then, in order:
    /// unused transferred-in days are lost; if <paramref name="transferAllowed"/> (the year is tracked), own unused 5th-week days up to
    /// <see cref="TransferLimitDays"/> are transferred, plus other own unused days up to the part of the limit above 5
    /// (a personal agreement; the Holiday Act only lets the 5th week move), so the default limit 5 never moves days
    /// from the first 20; own 5th-week days not transferred are paid out; other own unused days are paid out per
    /// <see cref="PayoutMode"/> and the rest lost.
    /// A limit of 0 transfers nothing. Over-booking (more booked than transferred in + own) gives all zeros.
    /// </summary>
    public DeadlineOutcome Split(decimal transferredInDays, decimal ownDays, decimal bookedDays, bool transferAllowed = true)
    {
        var transferredIn = Math.Max(0, transferredInDays);
        var own = Math.Max(0, ownDays);
        var booked = Math.Max(0, bookedDays);

        var unusedIn = Math.Max(0, transferredIn - booked);
        var ownUnused = Math.Max(0, own - Math.Max(0, booked - transferredIn));
        var ownFifthWeek = Math.Min(Math.Max(0, own - Accrual.FourWeeks), Accrual.FifthWeek);
        var unusedFifthWeek = Math.Min(ownUnused, ownFifthWeek);
        var unusedOther = ownUnused - unusedFifthWeek;

        decimal transferredFifthWeek = 0, transferredOther = 0;
        if (transferAllowed)
        {
            var limit = Math.Max(0, TransferLimitDays);
            transferredFifthWeek = Math.Min(limit, unusedFifthWeek);
            transferredOther = Math.Min(Math.Max(0, limit - Accrual.FifthWeek), unusedOther);
        }
        var transferred = transferredFifthWeek + transferredOther;
        var restOther = unusedOther - transferredOther;

        var paidOther = PayoutMode switch
        {
            PayoutMode.All => restOther,
            PayoutMode.Some => Math.Min(PayoutDays, restOther),
            _ => 0,
        };

        return new DeadlineOutcome(
            TransferredDays: transferred,
            PaidOutFifthWeekDays: unusedFifthWeek - transferredFifthWeek,
            PaidOutOtherDays: paidOther,
            LostTransferredInDays: unusedIn,
            LostOtherDays: restOther - paidOther);
    }
}
