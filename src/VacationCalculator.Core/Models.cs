using System.ComponentModel;
using System.Text.Json.Serialization;

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

    /// <summary>Most days that may be transferred to the next holiday year at this year's deadline (when transfer is allowed).</summary>
    public decimal? TransferLimitDays { get; set; }

    /// <summary>Payout of unused, non-transferred days at this year's deadline. Null = inherited.</summary>
    public PayoutMode? PayoutMode { get; set; }

    /// <summary>The N in "some N days paid out"; only used when <see cref="PayoutMode"/> is <see cref="Core.PayoutMode.Some"/>.</summary>
    public decimal? PayoutDays { get; set; }
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

    /// <summary>
    /// Employment-wide: whether unused days are transferred to the next holiday year at each deadline,
    /// up to that year's transfer limit (see <see cref="GetDeadlineSettings"/>).
    /// </summary>
    public bool TransferAllowed { get; set; }

    /// <summary>
    /// Reads the setting from files written before it was renamed to <see cref="TransferAllowed"/>.
    /// Never written.
    /// </summary>
    [JsonPropertyName("FifthWeekTransferAllowed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool? LegacyFifthWeekTransferAllowed
    {
        get => null;
        set
        {
            if (value == true)
                TransferAllowed = true;
        }
    }

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
    /// Applies whether or not <see cref="TransferAllowed"/> is on (it is a fact about the past).
    /// </summary>
    public decimal OpeningBalanceDays
    {
        get => _openingBalanceDays;
        set => _openingBalanceDays = Math.Max(0, value);
    }

    /// <summary>The default first tracked holiday year: the oldest holiday year open on <paramref name="today"/>.</summary>
    public static HolidayYear DefaultFirstTrackedHolidayYear(DateOnly today) => HolidayYear.OpenOn(today)[0];

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
    /// Every public holiday across all years, each taken only from the settings of the year it
    /// falls in. Suitable for booking ranges that cross New Year.
    /// </summary>
    public HashSet<DateOnly> AllPublicHolidays() =>
        (Years ?? []).SelectMany(y => (y.PublicHolidays ?? []).Where(h => h.Year == y.Year)).ToHashSet();

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
    /// unused transferred-in days are lost; if <paramref name="transferAllowed"/>, own unused 5th-week days up to
    /// <see cref="TransferLimitDays"/> are transferred, plus other own unused days up to the part of the limit above 5
    /// (a personal agreement; the Holiday Act only lets the 5th week move), so the default limit 5 never moves days
    /// from the first 20; own 5th-week days not transferred are paid out; other own unused days are paid out per
    /// <see cref="PayoutMode"/> and the rest lost.
    /// Over-booking (more booked than transferred in + own) gives all zeros.
    /// </summary>
    public DeadlineOutcome Split(decimal transferredInDays, decimal ownDays, decimal bookedDays, bool transferAllowed)
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
