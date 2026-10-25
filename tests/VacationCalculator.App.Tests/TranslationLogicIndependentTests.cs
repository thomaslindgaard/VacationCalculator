using System.Globalization;
using VacationCalculator.App.Services;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>Restores Loc.Current and both cultures; fixes the number format to invariant unless a test sets another.</summary>
public abstract class LocTestBase : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
    private readonly AppLanguage _language = Loc.Current;

    protected LocTestBase()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Loc.SetLanguage(AppLanguage.English);
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
        Loc.SetLanguage(_language);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Feature 7 logic: Languages.Resolve, plurals, dates, month and weekday names, per language. Expected values are counted
/// by hand (calendar facts in the comments).
/// </summary>
public class TranslationLogicIndependentTests : LocTestBase
{
    // ---- Languages.Resolve ----

    [Theory]
    [InlineData("da-DK", AppLanguage.Danish)]
    [InlineData("da", AppLanguage.Danish)]
    [InlineData("da-GL", AppLanguage.Danish)]
    [InlineData("ro-RO", AppLanguage.Romanian)]
    [InlineData("ro", AppLanguage.Romanian)]
    [InlineData("ro-MD", AppLanguage.Romanian)]
    [InlineData("en-US", AppLanguage.English)]
    [InlineData("en-GB", AppLanguage.English)]
    [InlineData("de-DE", AppLanguage.English)]
    [InlineData("nb-NO", AppLanguage.English)] // Norwegian is close to Danish but not Danish
    [InlineData("sv-SE", AppLanguage.English)]
    [InlineData("", AppLanguage.English)] // the invariant culture
    public void System_FollowsTheDisplayLanguage_OtherwiseEnglish(string culture, AppLanguage expected)
    {
        Assert.Equal(expected, Languages.Resolve(LanguageMode.System, CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void System_WithoutACulture_IsEnglish()
    {
        Assert.Equal(AppLanguage.English, Languages.Resolve(LanguageMode.System, null));
    }

    [Theory]
    [InlineData("da-DK")]
    [InlineData("ro-RO")]
    [InlineData("en-US")]
    [InlineData("")]
    public void System_NeverGivesRedneck(string culture)
    {
        Assert.NotEqual(AppLanguage.Redneck, Languages.Resolve(LanguageMode.System, CultureInfo.GetCultureInfo(culture)));
    }

    [Theory]
    [InlineData(LanguageMode.English, AppLanguage.English)]
    [InlineData(LanguageMode.Danish, AppLanguage.Danish)]
    [InlineData(LanguageMode.Romanian, AppLanguage.Romanian)]
    [InlineData(LanguageMode.Redneck, AppLanguage.Redneck)]
    public void ExplicitChoice_WinsOverTheWindowsLanguage(LanguageMode mode, AppLanguage expected)
    {
        foreach (var culture in new[] { "da-DK", "ro-RO", "en-US", "de-DE", "" })
            Assert.Equal(expected, Languages.Resolve(mode, CultureInfo.GetCultureInfo(culture)));
        Assert.Equal(expected, Languages.Resolve(mode, null));
    }

    [Fact]
    public void NativeNames_AreEachLanguageInItself_AndSystemIsNull()
    {
        Assert.Null(Languages.NativeName(LanguageMode.System));
        Assert.Equal("English", Languages.NativeName(LanguageMode.English));
        Assert.Equal("Dansk", Languages.NativeName(LanguageMode.Danish));
        Assert.Equal("Română", Languages.NativeName(LanguageMode.Romanian));
        Assert.Equal("Redneck", Languages.NativeName(LanguageMode.Redneck));
    }

    // ---- Plurals ----

    [Theory]
    [InlineData("0", "0 days")]
    [InlineData("0.5", "0.5 days")]
    [InlineData("1", "1 day")]
    [InlineData("1.0", "1 day")]
    [InlineData("1.5", "1.5 days")]
    [InlineData("2", "2 days")]
    [InlineData("25", "25 days")]
    public void English_Days(string count, string expected)
    {
        Loc.SetLanguage(AppLanguage.English);
        Assert.Equal(expected, Loc.Days(decimal.Parse(count, CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("0", "0 dage")]
    [InlineData("0.5", "0.5 dage")]
    [InlineData("1", "1 dag")]
    [InlineData("1.5", "1.5 dage")]
    [InlineData("2", "2 dage")]
    [InlineData("21", "21 dage")]
    public void Danish_Days(string count, string expected)
    {
        Loc.SetLanguage(AppLanguage.Danish);
        Assert.Equal(expected, Loc.Days(decimal.Parse(count, CultureInfo.InvariantCulture)));
    }

    // Romanian: 1 -> zi; 0, 2..19 and numbers ending 01..19 (101, 119) -> zile; 20..99 and numbers ending 00 or 20..99 -> "de zile".
    [Theory]
    [InlineData("0", "0 zile")]
    [InlineData("0.5", "0.5 zile")]
    [InlineData("1", "1 zi")]
    [InlineData("1.5", "1.5 zile")]
    [InlineData("2", "2 zile")]
    [InlineData("2.5", "2.5 zile")]
    [InlineData("10", "10 zile")]
    [InlineData("19", "19 zile")]
    [InlineData("19.5", "19.5 zile")]
    [InlineData("20", "20 de zile")]
    [InlineData("21", "21 de zile")]
    [InlineData("25", "25 de zile")]
    [InlineData("99", "99 de zile")]
    [InlineData("100", "100 de zile")]
    [InlineData("101", "101 zile")]
    [InlineData("102", "102 zile")]
    [InlineData("119", "119 zile")]
    [InlineData("120", "120 de zile")]
    [InlineData("200", "200 de zile")]
    [InlineData("1001", "1001 zile")]
    public void Romanian_Days(string count, string expected)
    {
        Loc.SetLanguage(AppLanguage.Romanian);
        Assert.Equal(expected, Loc.Days(decimal.Parse(count, CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("1", "1 zi Feriefri")]
    [InlineData("2", "2 zile Feriefri")]
    [InlineData("5", "5 zile Feriefri")]
    [InlineData("20", "20 de zile Feriefri")]
    public void Romanian_FeriefriDays(string count, string expected)
    {
        Loc.SetLanguage(AppLanguage.Romanian);
        Assert.Equal(expected, Loc.FeriefriDays(decimal.Parse(count, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void FeriefriDays_English_Danish()
    {
        Loc.SetLanguage(AppLanguage.English);
        Assert.Equal("1 Feriefri day", Loc.FeriefriDays(1));
        Assert.Equal("5 Feriefri days", Loc.FeriefriDays(5));
        Loc.SetLanguage(AppLanguage.Danish);
        Assert.Equal("1 feriefridag", Loc.FeriefriDays(1));
        Assert.Equal("5 feriefridage", Loc.FeriefriDays(5));
    }

    [Fact]
    public void Redneck_Days_KeepTheNumberAndTheWordDay()
    {
        Loc.SetLanguage(AppLanguage.Redneck);
        Assert.Contains("1", Loc.Days(1));
        Assert.Contains("day", Loc.Days(1), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("days", Loc.Days(1), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5 ", Loc.Days(5));
        Assert.Contains("0.5", Loc.Days(0.5m));
        Assert.Contains("days", Loc.Days(2), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Numbers_FollowTheWindowsRegionalFormat_NotTheLanguage()
    {
        // 0,5 with a Danish regional format, whatever language the text is in.
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("da-DK");
        Loc.SetLanguage(AppLanguage.English);
        Assert.Equal("0,5 days", Loc.Days(0.5m));
        Loc.SetLanguage(AppLanguage.Romanian);
        Assert.Equal("2,5 zile", Loc.Days(2.5m));
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        Loc.SetLanguage(AppLanguage.Danish);
        Assert.Equal("2.5 dage", Loc.Days(2.5m));
    }

    [Fact]
    public void ConflictNote_Plural_Romanian()
    {
        Loc.SetLanguage(AppLanguage.Romanian);
        Assert.StartsWith("1 zi programată", Loc.Plural("Conflict.Note", 1, "x"));
        Assert.StartsWith("3 zile programate", Loc.Plural("Conflict.Note", 3, "x"));
        Assert.StartsWith("20 de zile programate", Loc.Plural("Conflict.Note", 20, "x"));
    }

    // ---- Dates ----

    // Thu 2026-12-24 (2026-01-01 is a Thursday; 2026-12-24 is day 358: 357 days after, 357 = 51 weeks exactly -> Thursday).
    // Mon 2026-12-21. Tue 2028-02-29 (2028-01-01 is a Saturday; + 59 days = 8 weeks + 3 -> Tuesday). Sun 2027-01-03.
    [Theory]
    [InlineData(AppLanguage.English, 2026, 12, 31, "31 December 2026", "Thursday 31 December 2026")]
    [InlineData(AppLanguage.Danish, 2026, 12, 31, "31. december 2026", "torsdag den 31. december 2026")]
    [InlineData(AppLanguage.Romanian, 2026, 12, 31, "31 decembrie 2026", "joi, 31 decembrie 2026")]
    [InlineData(AppLanguage.Redneck, 2026, 12, 31, "31 December 2026", "Thursday 31 December 2026")]
    [InlineData(AppLanguage.English, 2026, 12, 24, "24 December 2026", "Thursday 24 December 2026")]
    [InlineData(AppLanguage.Danish, 2026, 12, 24, "24. december 2026", "torsdag den 24. december 2026")]
    [InlineData(AppLanguage.Romanian, 2026, 12, 24, "24 decembrie 2026", "joi, 24 decembrie 2026")]
    [InlineData(AppLanguage.English, 2026, 12, 21, "21 December 2026", "Monday 21 December 2026")]
    [InlineData(AppLanguage.Danish, 2026, 12, 21, "21. december 2026", "mandag den 21. december 2026")]
    [InlineData(AppLanguage.Romanian, 2026, 12, 21, "21 decembrie 2026", "luni, 21 decembrie 2026")]
    [InlineData(AppLanguage.English, 2028, 2, 29, "29 February 2028", "Tuesday 29 February 2028")]
    [InlineData(AppLanguage.Danish, 2028, 2, 29, "29. februar 2028", "tirsdag den 29. februar 2028")]
    [InlineData(AppLanguage.Romanian, 2028, 2, 29, "29 februarie 2028", "marți, 29 februarie 2028")]
    [InlineData(AppLanguage.Redneck, 2028, 2, 29, "29 February 2028", "Tuesday 29 February 2028")]
    [InlineData(AppLanguage.Danish, 2027, 1, 3, "3. januar 2027", "søndag den 3. januar 2027")]
    [InlineData(AppLanguage.Romanian, 2027, 1, 3, "3 ianuarie 2027", "duminică, 3 ianuarie 2027")]
    [InlineData(AppLanguage.English, 2027, 1, 3, "3 January 2027", "Sunday 3 January 2027")]
    public void DateText_LongAndWithWeekday(AppLanguage language, int y, int m, int d, string longText, string withWeekday)
    {
        Loc.SetLanguage(language);
        var date = new DateOnly(y, m, d);
        Assert.Equal(longText, DateText.Long(date));
        Assert.Equal(withWeekday, DateText.WithWeekday(date));
        Assert.Equal(longText, DateText.Long(date.ToDateTime(TimeOnly.MinValue)));
    }

    [Theory]
    [InlineData(AppLanguage.English)]
    [InlineData(AppLanguage.Danish)]
    [InlineData(AppLanguage.Romanian)]
    [InlineData(AppLanguage.Redneck)]
    public void DateText_IsIndependentOfTheWindowsRegionalFormat(AppLanguage language)
    {
        Loc.SetLanguage(language);
        var date = new DateOnly(2026, 12, 24);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");
        var a = DateText.WithWeekday(date);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var b = DateText.WithWeekday(date);
        Assert.Equal(a, b);
    }

    [Fact]
    public void DateText_FirstYears_And9999_ArePaddedAndDoNotThrow()
    {
        Loc.SetLanguage(AppLanguage.Danish);
        Assert.Equal("1. januar 0001", DateText.Long(DateOnly.MinValue)); // 0001-01-01 is a Monday
        Assert.Equal("fredag den 31. december 9999", DateText.WithWeekday(DateOnly.MaxValue));
        Loc.SetLanguage(AppLanguage.Romanian);
        Assert.Equal("vineri, 31 decembrie 9999", DateText.WithWeekday(DateOnly.MaxValue));
        Assert.Equal("luni, 1 ianuarie 0001", DateText.WithWeekday(DateOnly.MinValue));
    }

    [Fact]
    public void DateTextConverter_UsesTheCurrentLanguage()
    {
        var converter = new DateTextConverter();
        Loc.SetLanguage(AppLanguage.Danish);
        Assert.Equal("24. december 2026", converter.Convert(new DateOnly(2026, 12, 24), typeof(string), null!, CultureInfo.InvariantCulture));
        Assert.Equal("torsdag den 24. december 2026", converter.Convert(new DateOnly(2026, 12, 24), typeof(string), "Weekday", CultureInfo.InvariantCulture));
        Loc.SetLanguage(AppLanguage.Romanian);
        Assert.Equal("24 decembrie 2026", converter.Convert(new DateTime(2026, 12, 24), typeof(string), null!, CultureInfo.InvariantCulture));
        Assert.Equal("", converter.Convert(null, typeof(string), null!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void MonthNames_AllTwelve_PerLanguage()
    {
        string[] english = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
        string[] danish = ["januar", "februar", "marts", "april", "maj", "juni", "juli", "august", "september", "oktober", "november", "december"];
        string[] romanian = ["ianuarie", "februarie", "martie", "aprilie", "mai", "iunie", "iulie", "august", "septembrie", "octombrie", "noiembrie", "decembrie"];
        foreach (var (language, names) in new[] { (AppLanguage.English, english), (AppLanguage.Redneck, english), (AppLanguage.Danish, danish), (AppLanguage.Romanian, romanian) })
        {
            Loc.SetLanguage(language);
            Assert.Equal(names, Enumerable.Range(1, 12).Select(Loc.MonthName));
        }
    }

    [Fact]
    public void MonthTitle_StartsWithACapital()
    {
        Loc.SetLanguage(AppLanguage.Danish);
        Assert.Equal("Februar", Loc.MonthTitle(2));
        Loc.SetLanguage(AppLanguage.Romanian);
        Assert.Equal("Ianuarie", Loc.MonthTitle(1));
        Loc.SetLanguage(AppLanguage.English);
        Assert.Equal("May", Loc.MonthTitle(5));
    }

    [Fact]
    public void WeekdayNames_FullAndShort_PerLanguage()
    {
        Loc.SetLanguage(AppLanguage.Danish);
        Assert.Equal(["søndag", "mandag", "tirsdag", "onsdag", "torsdag", "fredag", "lørdag"], Enum.GetValues<DayOfWeek>().Select(Loc.WeekdayName));
        Assert.Equal("MTOTFLS", string.Concat(Enumerable.Range(1, 7).Select(i => Loc.Get("WeekdayShort." + i))));

        Loc.SetLanguage(AppLanguage.Romanian);
        Assert.Equal(["duminică", "luni", "marți", "miercuri", "joi", "vineri", "sâmbătă"], Enum.GetValues<DayOfWeek>().Select(Loc.WeekdayName));
        Assert.Equal("LMMJVSD", string.Concat(Enumerable.Range(1, 7).Select(i => Loc.Get("WeekdayShort." + i))));

        Loc.SetLanguage(AppLanguage.English);
        Assert.Equal(["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"], Enum.GetValues<DayOfWeek>().Select(Loc.WeekdayName));
        Assert.Equal("MTWTFSS", string.Concat(Enumerable.Range(1, 7).Select(i => Loc.Get("WeekdayShort." + i))));

        Loc.SetLanguage(AppLanguage.Redneck);
        Assert.Equal("MTWTFSS", string.Concat(Enumerable.Range(1, 7).Select(i => Loc.Get("WeekdayShort." + i))));
    }

    [Fact]
    public void HolidayNames_Danish_AreTheRealNames_Romanian_AreNotDanish()
    {
        Loc.SetLanguage(AppLanguage.Danish);
        Assert.Equal("Skærtorsdag", Loc.HolidayName(DanishHolidayKind.MaundyThursday));
        Assert.Equal("2. juledag", Loc.HolidayName(DanishHolidayKind.SecondChristmasDay));
        Loc.SetLanguage(AppLanguage.Romanian);
        Assert.Equal("Joia Mare", Loc.HolidayName(DanishHolidayKind.MaundyThursday));
        Assert.Equal("Crăciunul", Loc.HolidayName(DanishHolidayKind.ChristmasDay));
        Loc.SetLanguage(AppLanguage.English);
        Assert.Equal("Whit Monday", Loc.HolidayName(DanishHolidayKind.WhitMonday));
    }

    // ---- Fallbacks ----

    [Fact]
    public void MissingKey_FallsBackToTheKeyInBrackets()
    {
        foreach (var language in Enum.GetValues<AppLanguage>())
            Assert.Equal("[No.Such.Key]", Loc.Get(language, "No.Such.Key"));
    }

    [Fact]
    public void LocCurrent_IsEnglishByDefault_OnAFreshThread()
    {
        AppLanguage? seen = null;
        var thread = new Thread(() => seen = Loc.Current);
        Loc.SetLanguage(AppLanguage.Romanian); // another thread must not see this
        thread.Start();
        thread.Join();
        Assert.Equal(AppLanguage.English, seen);
    }
}
