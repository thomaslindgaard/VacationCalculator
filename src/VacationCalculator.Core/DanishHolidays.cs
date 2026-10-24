namespace VacationCalculator.Core;

/// <summary>An automatically calculated Danish public holiday (helligdag) with its Danish name.</summary>
public sealed record DanishHoliday(DateOnly Date, string Name);

/// <summary>
/// A public holiday as shown to the user: automatic (Danish), manual (entered by the user), or both on the same date.
/// </summary>
/// <param name="Name">The Danish name of the automatic holiday; null for a manual-only holiday (manual holidays have no name).</param>
/// <param name="IsAutomatic">Whether the date is a Danish public holiday (<see cref="DanishHolidays"/>); these can't be removed.</param>
/// <param name="IsManual">Whether the user entered the date in that year's manual public holidays; these can be removed.</param>
public sealed record PublicHoliday(DateOnly Date, string? Name, bool IsAutomatic, bool IsManual);

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
    private static readonly (int Offset, string Name)[] EasterBased =
    [
        (-3, "Skærtorsdag"),
        (-2, "Langfredag"),
        (0, "Påskedag"),
        (1, "2. påskedag"),
        (39, "Kristi himmelfartsdag"),
        (49, "Pinsedag"),
        (50, "2. pinsedag"),
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
        var list = new List<DanishHoliday>(12) { new(new DateOnly(year, 1, 1), "Nytårsdag") };
        foreach (var (offset, name) in EasterBased)
            list.Add(new DanishHoliday(easter.AddDays(offset), name));
        list.Add(new DanishHoliday(new DateOnly(year, 12, 24), "Juleaftensdag"));
        list.Add(new DanishHoliday(new DateOnly(year, 12, 25), "Juledag"));
        list.Add(new DanishHoliday(new DateOnly(year, 12, 26), "2. juledag"));
        list.Add(new DanishHoliday(new DateOnly(year, 12, 31), "Nytårsaftensdag"));
        return list;
    }

    /// <summary>Whether <paramref name="date"/> is a Danish public holiday. Never throws.</summary>
    public static bool IsHoliday(DateOnly date) => NameOf(date) is not null;

    /// <summary>The Danish name of the holiday on <paramref name="date"/>, or null if it isn't one. Never throws.</summary>
    public static string? NameOf(DateOnly date)
    {
        switch (date.Month, date.Day)
        {
            case (1, 1): return "Nytårsdag";
            case (12, 24): return "Juleaftensdag";
            case (12, 25): return "Juledag";
            case (12, 26): return "2. juledag";
            case (12, 31): return "Nytårsaftensdag";
        }
        // Easter-based holidays fall between 19 March (Easter - 3) and 14 June (Easter + 50).
        if (date.Month is < 3 or > 6)
            return null;
        var offset = date.DayNumber - EasterDayNumber(date.Year);
        foreach (var (o, name) in EasterBased)
        {
            if (o == offset)
                return name;
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
