using System.Globalization;

namespace VacationCalculator.Core;

/// <summary>
/// What a calendar day is, for colouring. When several apply, the first in this order wins: a Danish public holiday,
/// then a manual public holiday, then a weekend. <see cref="CalendarDay"/> also exposes each flag on its own.
/// </summary>
public enum CalendarDayKind
{
    WorkingDay,
    Weekend,
    /// <summary>An automatic Danish public holiday (<see cref="DanishHolidays"/>), possibly also manual or on a weekend.</summary>
    PublicHoliday,
    /// <summary>A manual public holiday entered by the user that is not a Danish one, possibly on a weekend.</summary>
    ManualHoliday,
}

/// <summary>
/// One day drawn in a month of a <see cref="CalendarYear"/>. Days outside the month (<see cref="IsInMonth"/> false) pad
/// the first and last week to 7 days; they carry their full state too, so the UI may draw them dimmed or blank.
/// </summary>
/// <param name="IsToday">Whether the date is today.</param>
/// <param name="IsPast">Whether the date is before today.</param>
/// <param name="IsDanishHoliday">Whether the date is an automatic Danish public holiday.</param>
/// <param name="IsManualHoliday">Whether the date is in the user's manual public holidays of its own calendar year.</param>
/// <param name="HolidayKind">Which automatic Danish holiday the date is (the App names it); null otherwise (manual holidays have no name).</param>
/// <param name="Booking">
/// The booked record on the date (the first one if the file has several), or null. It is stored even on a non-working
/// day, where it doesn't count (see <see cref="IsConflict"/>).
/// </param>
/// <param name="Allocations">
/// The holiday years the booked day counts against (vacation or Feriefri, by <see cref="Booking"/>'s type), oldest first:
/// one entry, or two halves when a full day is split. Empty when nothing is booked, for a conflict, or for an amount of 0.
/// </param>
public sealed record CalendarDay(
    DateOnly Date,
    bool IsInMonth,
    bool IsToday,
    bool IsPast,
    bool IsWeekend,
    bool IsDanishHoliday,
    bool IsManualHoliday,
    DanishHolidayKind? HolidayKind,
    BookedDay? Booking,
    IReadOnlyList<AllocatedDay> Allocations)
{
    /// <summary>Whether the date is a public holiday of either kind.</summary>
    public bool IsPublicHoliday => IsDanishHoliday || IsManualHoliday;

    /// <summary>Whether the date is a working day (as <see cref="BalanceCalculator.IsWorkingDay"/>): not a weekend and not a holiday.</summary>
    public bool IsWorkingDay => !IsWeekend && !IsPublicHoliday;

    public CalendarDayKind Kind =>
        IsDanishHoliday ? CalendarDayKind.PublicHoliday
        : IsManualHoliday ? CalendarDayKind.ManualHoliday
        : IsWeekend ? CalendarDayKind.Weekend
        : CalendarDayKind.WorkingDay;

    public bool IsBooked => Booking is not null;

    /// <summary>The booked type, or null when nothing is booked.</summary>
    public DayType? BookedType => Booking?.Type;

    /// <summary>The booked amount (1 or 0.5), or 0 when nothing is booked.</summary>
    public decimal BookedAmount => Booking?.Amount ?? 0;

    public bool IsFullDay => Booking is { Amount: >= BookedDay.FullDay };

    public bool IsHalfDay => Booking is { Amount: > 0 and < BookedDay.FullDay };

    /// <summary>The booking's description, or "" when nothing is booked.</summary>
    public string Description => Booking?.Description ?? "";

    /// <summary>A booking stored on a non-working day (weekend or public holiday); it is not counted.</summary>
    public bool IsConflict => Booking is not null && !IsWorkingDay;

    /// <summary>The holiday years of <see cref="Allocations"/>, e.g. "2025/26" or "2025/26, 2026/27" for a split day; "" if none.</summary>
    public string HolidayYearsText => string.Join(", ", Allocations.Select(a => a.HolidayYear).Distinct());
}

/// <summary>A Monday-to-Sunday week of a month, always 7 days.</summary>
/// <param name="WeekNumber">The ISO 8601 week number (1..53).</param>
/// <param name="WeekYear">
/// The ISO 8601 week-numbering year the week belongs to, which can differ from the calendar year: Fri 2027-01-01 is in
/// week 53 of 2026, Mon 2029-12-31 in week 1 of 2030.
/// </param>
public sealed record CalendarWeek(int WeekNumber, int WeekYear, IReadOnlyList<CalendarDay> Days);

/// <summary>
/// A month of a <see cref="CalendarYear"/> as whole weeks (4..6), Monday first. It carries no name: the App formats
/// <see cref="Month"/> in the user's language.
/// </summary>
/// <param name="Month">The month number, 1..12.</param>
public sealed record CalendarMonth(int Year, int Month, IReadOnlyList<CalendarWeek> Weeks)
{
    /// <summary>The month's own days (no padding), in date order.</summary>
    public IEnumerable<CalendarDay> Days => Weeks.SelectMany(w => w.Days).Where(d => d.IsInMonth);
}

/// <summary>A whole calendar year laid out for display: 12 months of Monday-first weeks with each day's state.</summary>
public sealed record CalendarYear(int Year, IReadOnlyList<CalendarMonth> Months)
{
    /// <summary>Smallest calendar year <see cref="Build"/> accepts.</summary>
    public const int MinYear = 1;

    /// <summary>Largest calendar year <see cref="Build"/> accepts (December 9999 would need padding days after 9999-12-31).</summary>
    public const int MaxYear = 9998;

    /// <summary>
    /// Lays out calendar year <paramref name="year"/> as of <paramref name="today"/>. Day state comes from the same rules
    /// as the calculators: <see cref="BalanceCalculator.IsWorkingDay"/> with the manual holidays, the first record per
    /// date, and one allocation of all bookings (<see cref="BalanceCalculator.Allocate"/>, <see cref="FeriefriCalculator.Allocate"/>).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="year"/> is outside <see cref="MinYear"/>..<see cref="MaxYear"/>.</exception>
    public static CalendarYear Build(VacationData data, int year, DateOnly today)
    {
        if (year is < MinYear or > MaxYear)
            throw new ArgumentOutOfRangeException(nameof(year), year, $"Year must be between {MinYear} and {MaxYear}.");

        // The whole drawn range: Monday on or before 1 January .. Sunday on or after 31 December.
        var first = StartOfWeek(new DateOnly(year, 1, 1));
        var last = StartOfWeek(new DateOnly(year, 12, 31)).AddDays(6);

        var manual = data.AllPublicHolidays();

        var bookings = new Dictionary<DateOnly, BookedDay>();
        foreach (var day in data.Days ?? [])
        {
            if (day is not null)
                bookings.TryAdd(day.Date, day);
        }

        var allocations = new Dictionary<DateOnly, List<AllocatedDay>>();
        foreach (var a in BalanceCalculator.Allocate(data, today).Concat(FeriefriCalculator.Allocate(data)))
        {
            if (a.Date < first || a.Date > last)
                continue;
            if (!allocations.TryGetValue(a.Date, out var list))
                allocations[a.Date] = list = [];
            list.Add(a);
        }

        var months = new List<CalendarMonth>(12);
        for (var month = 1; month <= 12; month++)
        {
            var monthStart = new DateOnly(year, month, 1);
            var monthEnd = monthStart.AddDays(DateTime.DaysInMonth(year, month) - 1);
            var weeks = new List<CalendarWeek>(6);
            for (var monday = StartOfWeek(monthStart); monday <= monthEnd; monday = monday.AddDays(7))
            {
                var days = new CalendarDay[7];
                for (var i = 0; i < 7; i++)
                {
                    var date = monday.AddDays(i);
                    var booking = bookings.GetValueOrDefault(date);
                    var holidayKind = DanishHolidays.KindOf(date);
                    days[i] = new CalendarDay(
                        date,
                        IsInMonth: date.Month == month && date.Year == year,
                        IsToday: date == today,
                        IsPast: date < today,
                        IsWeekend: date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                        IsDanishHoliday: holidayKind is not null,
                        IsManualHoliday: manual.Contains(date),
                        HolidayKind: holidayKind,
                        Booking: booking,
                        Allocations: allocations.TryGetValue(date, out var list)
                            ? list.OrderBy(a => a.HolidayYear.StartYear).ToList()
                            : []);
                }
                var dt = monday.ToDateTime(TimeOnly.MinValue);
                weeks.Add(new CalendarWeek(ISOWeek.GetWeekOfYear(dt), ISOWeek.GetYear(dt), days));
            }
            months.Add(new CalendarMonth(year, month, weeks));
        }

        return new CalendarYear(year, months);
    }

    /// <summary>The Monday on or before <paramref name="date"/>.</summary>
    private static DateOnly StartOfWeek(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
