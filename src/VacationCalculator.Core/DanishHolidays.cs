namespace VacationCalculator.Core;

/// <summary>
/// The 12 automatic Danish public holidays (helligdage), in date order within a year. Core never names them; the App
/// translates the kind into the user's language.
/// </summary>
public enum DanishHolidayKind
{
    /// <summary>Nytårsdag, 1 January.</summary>
    NewYearsDay,
    /// <summary>Skærtorsdag, Easter Sunday - 3.</summary>
    MaundyThursday,
    /// <summary>Langfredag, Easter Sunday - 2.</summary>
    GoodFriday,
    /// <summary>Påskedag.</summary>
    EasterSunday,
    /// <summary>2. påskedag, Easter Sunday + 1.</summary>
    EasterMonday,
    /// <summary>Kristi himmelfartsdag, Easter Sunday + 39.</summary>
    AscensionDay,
    /// <summary>Pinsedag, Easter Sunday + 49.</summary>
    WhitSunday,
    /// <summary>2. pinsedag, Easter Sunday + 50.</summary>
    WhitMonday,
    /// <summary>Juleaftensdag, 24 December.</summary>
    ChristmasEve,
    /// <summary>Juledag, 25 December.</summary>
    ChristmasDay,
    /// <summary>2. juledag, 26 December.</summary>
    SecondChristmasDay,
    /// <summary>Nytårsaftensdag, 31 December.</summary>
    NewYearsEve,
}

/// <summary>An automatically calculated Danish public holiday (helligdag) and which one it is.</summary>
public sealed record DanishHoliday(DateOnly Date, DanishHolidayKind Kind);

/// <summary>
/// A public holiday as shown to the user: automatic (Danish), manual (entered by the user), or both on the same date.
/// </summary>
/// <param name="Kind">Which Danish holiday it is (the App names it in the user's language); null for a manual-only holiday (manual holidays have no name).</param>
/// <param name="IsAutomatic">Whether the date is a Danish public holiday (<see cref="DanishHolidays"/>); these can't be removed. True exactly when <paramref name="Kind"/> is set.</param>
/// <param name="IsManual">Whether the user entered the date in that year's manual public holidays; these can be removed.</param>
public sealed record PublicHoliday(DateOnly Date, DanishHolidayKind? Kind, bool IsAutomatic, bool IsManual);

/// <summary>
/// Danish public holidays (helligdage), calculated for any year 1..9999: Nytårsdag, Skærtorsdag, Langfredag, Påskedag,
/// 2. påskedag, Kristi himmelfartsdag, Pinsedag, 2. pinsedag, Juleaftensdag, Juledag, 2. juledag and Nytårsaftensdag.
/// Easter Sunday follows the Gregorian (Western) computus. Store bededag and Grundlovsdag are not included.
/// These holidays always apply: <see cref="BalanceCalculator.IsWorkingDay"/> treats them as non-working days whatever
/// holiday set it is given.
/// </summary>
public static class DanishHolidays
{
    /// <summary>Day numbers of Easter Sunday per year, computed on first use; 0 = not computed (Easter is never 1 Jan 0001).</summary>
    private static readonly int[] EasterDayNumbers = new int[DateOnly.MaxValue.Year + 1];

    /// <summary>Holidays at a fixed offset from Easter Sunday, in date order.</summary>
    private static readonly (int Offset, DanishHolidayKind Kind)[] EasterBased =
    [
        (-3, DanishHolidayKind.MaundyThursday),
        (-2, DanishHolidayKind.GoodFriday),
        (0, DanishHolidayKind.EasterSunday),
        (1, DanishHolidayKind.EasterMonday),
        (39, DanishHolidayKind.AscensionDay),
        (49, DanishHolidayKind.WhitSunday),
        (50, DanishHolidayKind.WhitMonday),
    ];

    /// <summary>
    /// Easter Sunday of <paramref name="year"/> (1..9999) by the anonymous Gregorian algorithm (Meeus/Jones/Butcher).
    /// Always between 22 March and 25 April.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="year"/> is outside 1..9999.</exception>
    public static DateOnly EasterSunday(int year)
    {
        if (year < DateOnly.MinValue.Year || year > DateOnly.MaxValue.Year)
            throw new ArgumentOutOfRangeException(nameof(year), year, "Year must be between 1 and 9999.");
        return DateOnly.FromDayNumber(EasterDayNumber(year));
    }

    /// <summary>The 12 Danish public holidays of <paramref name="year"/> (1..9999), in date order.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="year"/> is outside 1..9999.</exception>
    public static IReadOnlyList<DanishHoliday> For(int year)
    {
        var easter = EasterSunday(year);
        var list = new List<DanishHoliday>(12) { new(new DateOnly(year, 1, 1), DanishHolidayKind.NewYearsDay) };
        foreach (var (offset, kind) in EasterBased)
            list.Add(new DanishHoliday(easter.AddDays(offset), kind));
        list.Add(new DanishHoliday(new DateOnly(year, 12, 24), DanishHolidayKind.ChristmasEve));
        list.Add(new DanishHoliday(new DateOnly(year, 12, 25), DanishHolidayKind.ChristmasDay));
        list.Add(new DanishHoliday(new DateOnly(year, 12, 26), DanishHolidayKind.SecondChristmasDay));
        list.Add(new DanishHoliday(new DateOnly(year, 12, 31), DanishHolidayKind.NewYearsEve));
        return list;
    }

    /// <summary>Whether <paramref name="date"/> is a Danish public holiday. Never throws.</summary>
    public static bool IsHoliday(DateOnly date) => KindOf(date) is not null;

    /// <summary>Which Danish holiday <paramref name="date"/> is, or null if it isn't one. Never throws.</summary>
    public static DanishHolidayKind? KindOf(DateOnly date)
    {
        switch (date.Month, date.Day)
        {
            case (1, 1): return DanishHolidayKind.NewYearsDay;
            case (12, 24): return DanishHolidayKind.ChristmasEve;
            case (12, 25): return DanishHolidayKind.ChristmasDay;
            case (12, 26): return DanishHolidayKind.SecondChristmasDay;
            case (12, 31): return DanishHolidayKind.NewYearsEve;
        }
        // Easter-based holidays fall between 19 March (Easter - 3) and 14 June (Easter + 50).
        if (date.Month is < 3 or > 6)
            return null;
        var offset = date.DayNumber - EasterDayNumber(date.Year);
        foreach (var (o, kind) in EasterBased)
        {
            if (o == offset)
                return kind;
        }
        return null;
    }

    private static int EasterDayNumber(int year)
    {
        var cached = Volatile.Read(ref EasterDayNumbers[year]);
        if (cached != 0)
            return cached;

        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;

        var result = new DateOnly(year, month, day).DayNumber;
        Volatile.Write(ref EasterDayNumbers[year], result);
        return result;
    }
}
