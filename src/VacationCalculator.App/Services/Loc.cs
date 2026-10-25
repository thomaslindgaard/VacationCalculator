using System.ComponentModel;
using System.Globalization;
using VacationCalculator.Core;

namespace VacationCalculator.App.Services;

/// <summary>
/// All user-facing text. One string table per language (<see cref="StringTables"/>) with identical keys; placeholders are
/// <see cref="string.Format(string, object[])"/> style. XAML binds to the indexer through <see cref="StrExtension"/>
/// (<c>{svc:Str Key}</c>); switching the language raises <c>Item[]</c> so every such binding refreshes at once.
/// <para>
/// The current language is thread-static and defaults to English. The app only ever uses the UI thread; the point is that
/// unit tests running in parallel never see each other's language, and anything that was not told otherwise is English
/// (never the Windows language of the machine). <c>MainViewModel</c> sets it from its theme/preferences service on creation.
/// </para>
/// Numbers keep following the Windows regional format (<see cref="CultureInfo.CurrentCulture"/>).
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    [ThreadStatic]
    private static AppLanguage? _current;

    /// <summary>The language text is produced in right now on this thread (English until set).</summary>
    public static AppLanguage Current => _current ?? AppLanguage.English;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Switches the language and refreshes every XAML binding made with <see cref="StrExtension"/>.</summary>
    public static void SetLanguage(AppLanguage language)
    {
        _current = language;
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));
    }

    /// <summary>The text of <paramref name="key"/> in the current language (indexer for XAML bindings).</summary>
    public string this[string key] => Get(key);

    /// <summary>The text for <paramref name="key"/>; falls back to English, then to the key in brackets so a gap is visible.</summary>
    public static string Get(string key) => Get(Current, key);

    public static string Get(AppLanguage language, string key)
    {
        if (StringTables.For(language).TryGetValue(key, out var text))
            return text;
        return StringTables.For(AppLanguage.English).TryGetValue(key, out var english) ? english : $"[{key}]";
    }

    /// <summary>The text for <paramref name="key"/> with placeholders filled in, numbers in the Windows regional format.</summary>
    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>A number as shown to the user: up to two decimals, Windows regional format ("2,5" or "2.5").</summary>
    public static string Num(decimal value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    /// <summary>"1 day", "2 days", "0.5 days"; Romanian "1 zi", "2 zile", "20 de zile". Uses <c>baseKey.One/.Few/.Other</c>.</summary>
    public static string Plural(string baseKey, decimal count, params object[] more)
    {
        var category = Category(Current, count);
        var key = baseKey + "." + category;
        if (!StringTables.For(Current).ContainsKey(key))
            key = baseKey + ".Other";
        return Format(key, [Num(count), .. more]);
    }

    /// <summary>Which plural form of a count the language uses: "One", "Few" or "Other".</summary>
    public static string Category(AppLanguage language, decimal count)
    {
        count = Math.Abs(count);
        if (count == 1)
            return "One";
        if (language != AppLanguage.Romanian)
            return "Other";

        // Romanian: 1 zi; 0, decimals and 2..19 (also 102..119) zile; 20 and up "de zile".
        if (count != decimal.Truncate(count))
            return "Few";
        var whole = (long)(count % 100);
        return count == 0 || (whole >= 1 && whole <= 19) ? "Few" : "Other";
    }

    /// <summary>Picks <c>baseKey.One</c> or <c>baseKey.Other</c> by whether the count is exactly one (agreement of verbs and pronouns).</summary>
    public static string Number(string baseKey, decimal count, params object[] args) =>
        Format(baseKey + (count == 1 ? ".One" : ".Other"), args);

    /// <summary>Picks <c>baseKey.Future</c> or <c>.Past</c>, then <c>.One</c> (exactly one day) or <c>.Other</c>.</summary>
    public static string Tense(string baseKey, decimal count, bool past, params object[] args) =>
        Format(baseKey + (past ? ".Past" : ".Future") + (count == 1 ? ".One" : ".Other"), args);

    public static string Days(decimal count) => Plural("Unit.Day", count);

    public static string FeriefriDays(decimal count) => Plural("Unit.FeriefriDay", count);

    /// <summary>The month name as written inside a date ("December" / "december").</summary>
    public static string MonthName(int month) => Get("Month." + month.ToString(CultureInfo.InvariantCulture));

    /// <summary>The month name as a card title (first letter upper case).</summary>
    public static string MonthTitle(int month) => UpperFirst(MonthName(month));

    public static string WeekdayName(DayOfWeek day) => Get("Weekday." + ((int)day).ToString(CultureInfo.InvariantCulture));

    public static string HolidayName(DanishHolidayKind kind) => Get("Holiday." + kind);

    /// <summary>Capitalises the first letter, for a sentence that starts with a date or a month ("torsdag den 24. december" at the start of a sentence).</summary>
    public static string UpperFirst(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];
}
