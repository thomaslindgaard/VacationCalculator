using System.Globalization;
using System.Text;
using VacationCalculator.App.Services;
using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>
/// Feature 7 through the view model: switching language live, preferences, machine independence. Clock Fri 2026-12-18;
/// data: 5 vacation days (Mon 2026-01-05 .. Fri 01-09) of the 25 earned in 2025/26, so 20 are unbooked.
/// </summary>
public class TranslationLiveSwitchIndependentTests : HolidayYearVmTestBase, IDisposable
{
    private string Prefs => Path.Combine(Dir, "vacation-calculator-preferences.json");

    private readonly List<ThemeService> _themes = [];

    public new void Dispose()
    {
        foreach (var theme in _themes)
            theme.Dispose();
        Loc.SetLanguage(AppLanguage.English);
        base.Dispose();
    }

    private (MainViewModel Vm, ThemeService Theme) Build(CultureInfo? ui = null, VacationData? data = null)
    {
        Directory.CreateDirectory(Dir);
        var store = new DataStore(DataFile);
        store.Save(data ?? Seed(5));
        var theme = new ThemeService(Prefs, () => false, ui is null ? null : () => ui);
        _themes.Add(theme);
        return (new MainViewModel(store, () => Dec18, theme), theme);
    }

    private static void Pick(MainViewModel vm, LanguageMode mode) =>
        vm.SelectedLanguage = vm.LanguageOptions.Single(o => o.Mode == mode);

    /// <summary>Every user-visible computed text of the view model, by name.</summary>
    private static Dictionary<string, string> Snapshot(MainViewModel vm)
    {
        var s = new Dictionary<string, string>
        {
            ["SummaryToggleText"] = vm.SummaryToggleText,
            ["HolidayYearTitle"] = vm.HolidayYearTitle,
            ["EarnableNote"] = vm.EarnableNote,
            ["StatusMessage"] = vm.StatusMessage,
            ["StatusDetail"] = vm.StatusDetail,
            ["FeriefriBanner"] = vm.FeriefriBanner,
            ["DeadlineSettingsTitle"] = vm.DeadlineSettingsTitle,
            ["TransferLimitSource"] = vm.TransferLimitSource,
            ["PayoutSource"] = vm.PayoutSource,
            ["AddButtonText"] = vm.AddButtonText,
            ["NewEntryPreview"] = vm.NewEntryPreview,
            ["ConflictNote"] = vm.ConflictNote,
            ["DataPathText"] = vm.DataPathText,
            ["ErrorMessage"] = vm.ErrorMessage,
            ["InfoMessage"] = vm.InfoMessage,
        };
        foreach (var o in vm.LanguageOptions)
            s["Language:" + o.Mode] = o.DisplayName;
        foreach (var (e, i) in vm.Entries.Select((e, i) => (e, i)))
            s["Entry" + i] = e.NotCountedSuffix;
        foreach (var h in vm.PublicHolidays)
        {
            s["Holiday " + h.Date.ToString("yyyy-MM-dd")] = h.Name;
            s["HolidayTip " + h.Date.ToString("yyyy-MM-dd")] = h.RemoveTooltip;
        }
        foreach (var h in vm.ManualHolidayRows)
            s["Manual " + h.Date.ToString("yyyy-MM-dd")] = h.Name + "|" + h.RemoveTooltip;
        foreach (var h in vm.AutomaticHolidayRows)
            s["Auto " + h.Date.ToString("yyyy-MM-dd")] = h.Name + "|" + h.RemoveTooltip;
        foreach (var r in vm.FeriefriRows)
        {
            var k = "Feriefri " + r.Name;
            s[k + " source"] = r.AllowanceSource;
            s[k + " tooltip"] = r.AllowanceTooltip;
            s[k + " used"] = r.UsedText;
            s[k + " ahead"] = r.BookedAheadText;
            s[k + " remaining"] = r.RemainingText;
            s[k + " note"] = r.Note;
        }
        foreach (var r in vm.HolidayYearRows)
        {
            s["Year " + r.Name + " earned"] = r.Earned;
            s["Year " + r.Name + " note"] = r.Note;
            s["Year " + r.Name + " projected"] = r.ProjectedText;
            s["Year " + r.Name + " projectedTip"] = r.ProjectedTooltip;
        }
        foreach (var (m, mi) in vm.CalendarMonths.Select((m, i) => (m, i)))
        {
            s["Month " + (mi + 1)] = m.Name;
            foreach (var c in m.Cells.OfType<DayCell>().Where(c => !c.IsBlank))
                s["Cell " + c.Date.ToString("yyyy-MM-dd")] = c.Tooltip;
        }
        return s;
    }

    /// <summary>The scenario: 5 vacation days used, a Feriefri day booked ahead (Mon 2026-12-21), a manual holiday (Fri 2026-06-05).</summary>
    private (MainViewModel Vm, ThemeService Theme) Scenario(CultureInfo? ui = null)
    {
        var (vm, theme) = Build(ui);
        vm.BookFeriefri = true;
        vm.NewStart = new DateTime(2026, 12, 21);
        vm.AddEntryCommand.Execute(null);
        vm.NewHoliday = new DateTime(2026, 6, 5);
        vm.AddHolidayCommand.Execute(null);
        vm.BookFeriefri = false;
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal("", vm.InfoMessage);
        return (vm, theme);
    }

    // ---- Live switching ----

    [Fact]
    public void English_IsTheBaseline_ForTheScenario()
    {
        var (vm, _) = Scenario();
        // 20 of 25 days unbooked; free working days 18, 22, 23, 28, 29, 30 Dec = 6 (21 Dec is booked, 24/25/31 are holidays).
        Assert.Equal("You have 20 days not yet booked, but only 6 working days are left from today through 31 December 2026.", vm.StatusMessage);
        Assert.Equal("14 days can't fit even if you book every free working day: 5 days will be paid out and 9 days will be lost.", vm.StatusDetail);
        Assert.Equal("December", vm.CalendarMonths[11].Name);
    }

    [Fact]
    public void SwitchToDanish_RewritesTheSummaryBannerHolidaysCalendarAndFeriefri()
    {
        var (vm, theme) = Scenario();

        Pick(vm, LanguageMode.Danish);

        Assert.Equal(AppLanguage.Danish, theme.Language);
        Assert.Equal("Skjul oversigt", vm.SummaryToggleText);
        Assert.Equal("Ferieår 2025/26 · brug inden 31. december 2026", vm.HolidayYearTitle);
        Assert.Equal("af 25 mulige", vm.EarnableNote);
        Assert.Equal("Du mangler at booke 20 dage, men kun 6 arbejdsdage er tilbage fra i dag til og med 31. december 2026.", vm.StatusMessage);
        Assert.Equal("14 dage kan ikke nå at blive brugt, selv hvis du booker alle ledige arbejdsdage: 5 dage udbetales og 9 dage bortfalder.", vm.StatusDetail);
        Assert.Equal("Feriefridage: 4 dage skal bruges inden 31. december 2026, ellers udbetales de i januar.", vm.FeriefriBanner);
        Assert.Equal("Når ferieåret 2025/26 udløber (31. december 2026)", vm.DeadlineSettingsTitle);
        Assert.Equal("Tilføj ferie", vm.AddButtonText);
        Assert.StartsWith("Gemt i ", vm.DataPathText);
        Assert.Equal("Sprog", Loc.Get("Language.Title"));

        // Holiday rows: the real Danish names, in date order; the user's own row has no name.
        Assert.Equal(
            ["Nytårsdag", "Skærtorsdag", "Langfredag", "Påskedag", "2. påskedag", "Kristi himmelfartsdag", "Pinsedag", "2. pinsedag",
             "", "Juleaftensdag", "Juledag", "2. juledag", "Nytårsaftensdag"],
            vm.PublicHolidays.Select(r => r.Name));
        Assert.Equal("Fjern", vm.PublicHolidays.Single(r => r.Date == new DateOnly(2026, 6, 5)).RemoveTooltip);

        // Calendar: month cards and tooltips (Mon 2026-01-05, Thu 2026-12-24, Mon 2026-12-21 Feriefri, Fri 2026-06-05 manual holiday).
        Assert.Equal(
            ["Januar", "Februar", "Marts", "April", "Maj", "Juni", "Juli", "August", "September", "Oktober", "November", "December"],
            vm.CalendarMonths.Select(m => m.Name));
        Assert.Equal("torsdag den 24. december 2026" + NL + "Juleaftensdag", Tip(vm, 12, 24));
        Assert.Equal("mandag den 5. januar 2026" + NL + "Ferie, hel dag" + NL + "Ferieår 2025/26", Tip(vm, 1, 5));
        Assert.Equal("mandag den 21. december 2026" + NL + "Feriefri, hel dag" + NL + "Ferieår 2025/26", Tip(vm, 12, 21));
        Assert.Equal("fredag den 5. juni 2026" + NL + "Helligdag (tilføjet af dig)", Tip(vm, 6, 5));

        // Feriefri rows: 2025/26 has 1 booked ahead, 4 remaining; 4 paid out in January 2027.
        var ff = vm.FeriefriRows[0];
        Assert.Equal("2025/26", ff.Name);
        Assert.Equal("0 brugt", ff.UsedText);
        Assert.Equal("1 booket fremover", ff.BookedAheadText);
        Assert.Equal("4 tilbage", ff.RemainingText);
        Assert.Equal("4 udbetales i januar 2027", ff.Note);
        Assert.Equal("standard", ff.AllowanceSource);

        Assert.Equal(["System", "English", "Dansk", "Română", "Redneck"], vm.LanguageOptions.Select(o => o.DisplayName));
    }

    // ---- Preview plural ("N working day(s)") ----

    private static readonly DateTime Mon5Jan = new(2026, 1, 5);

    /// <summary>Preview text for N working days starting Mon 2026-01-05 (no holidays until February; weeks start 5, 12, 19, 26 Jan, 2 Feb).</summary>
    private static string PreviewFor(MainViewModel vm, decimal days)
    {
        vm.IsHalfDay = false;
        switch (days)
        {
            case 0: vm.NewStart = new DateTime(2026, 1, 3); vm.NewEnd = new DateTime(2026, 1, 4); break; // Sat-Sun
            case 0.5m: vm.NewStart = Mon5Jan; vm.NewEnd = Mon5Jan; vm.IsHalfDay = true; break;
            case 1: vm.NewStart = Mon5Jan; vm.NewEnd = Mon5Jan; break;
            case 2: vm.NewStart = Mon5Jan; vm.NewEnd = new DateTime(2026, 1, 6); break;
            case 19: vm.NewStart = Mon5Jan; vm.NewEnd = new DateTime(2026, 1, 29); break; // 5+5+5+4
            case 20: vm.NewStart = Mon5Jan; vm.NewEnd = new DateTime(2026, 1, 30); break; // 4 full weeks
            case 25: vm.NewStart = Mon5Jan; vm.NewEnd = new DateTime(2026, 2, 6); break; // 5 full weeks
            default: throw new ArgumentOutOfRangeException(nameof(days));
        }
        return vm.NewEntryPreview;
    }

    private static void WithCulture(string name, Action action)
    {
        var cur = CultureInfo.CurrentCulture;
        var ui = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            CultureInfo.CurrentUICulture = new CultureInfo(name);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = cur;
            CultureInfo.CurrentUICulture = ui;
        }
    }

    [Fact]
    public void PreviewPlural_English()
    {
        WithCulture("en-US", () =>
        {
            var (vm, _) = Build();
            Pick(vm, LanguageMode.English);
            Assert.Equal("0 working days", PreviewFor(vm, 0));
            Assert.Equal("0.5 working days", PreviewFor(vm, 0.5m));
            Assert.Equal("1 working day", PreviewFor(vm, 1));
            Assert.Equal("2 working days", PreviewFor(vm, 2));
            Assert.Equal("25 working days", PreviewFor(vm, 25));
        });
    }

    [Fact]
    public void PreviewPlural_Danish()
    {
        WithCulture("da-DK", () =>
        {
            var (vm, _) = Build();
            Pick(vm, LanguageMode.Danish);
            Assert.Equal("1 arbejdsdag", PreviewFor(vm, 1));
            Assert.Equal("2 arbejdsdage", PreviewFor(vm, 2));
            Assert.Equal("0 arbejdsdage", PreviewFor(vm, 0));
            Assert.Equal("0,5 arbejdsdage", PreviewFor(vm, 0.5m));
            Assert.Equal("25 arbejdsdage", PreviewFor(vm, 25));
        });
    }

    [Fact]
    public void PreviewPlural_Redneck()
    {
        WithCulture("en-US", () =>
        {
            var (vm, _) = Build();
            Pick(vm, LanguageMode.Redneck);
            Assert.Equal("1 workin' day", PreviewFor(vm, 1));
            Assert.Equal("2 workin' days", PreviewFor(vm, 2));
            Assert.Equal("0 workin' days", PreviewFor(vm, 0));
            Assert.Equal("0.5 workin' days", PreviewFor(vm, 0.5m));
        });
    }

    [Fact]
    public void PreviewPlural_Romanian_OneFewAndDe()
    {
        // Romanian: 1 zi; 0, fractions and 2..19 "zile"; 20 and up "de zile".
        WithCulture("ro-RO", () =>
        {
            var (vm, _) = Build();
            Pick(vm, LanguageMode.Romanian);
            Assert.Equal("0 zile lucrătoare", PreviewFor(vm, 0));
            Assert.Equal("0,5 zile lucrătoare", PreviewFor(vm, 0.5m));
            Assert.Equal("1 zi lucrătoare", PreviewFor(vm, 1));
            Assert.Equal("2 zile lucrătoare", PreviewFor(vm, 2));
            Assert.Equal("19 zile lucrătoare", PreviewFor(vm, 19));
            Assert.Equal("20 de zile lucrătoare", PreviewFor(vm, 20));
            Assert.Equal("25 de zile lucrătoare", PreviewFor(vm, 25));
        });
    }

    [Fact]
    public void PreviewDecimalSeparator_FollowsTheCulture_NotTheLanguage()
    {
        // English text on a comma-decimal machine, and Danish text on a point-decimal machine.
        WithCulture("da-DK", () =>
        {
            var (vm, _) = Build();
            Assert.Equal("0,5 working days", PreviewFor(vm, 0.5m));
        });
        WithCulture("en-US", () =>
        {
            var (vm, _) = Build();
            Pick(vm, LanguageMode.Danish);
            Assert.Equal("0.5 arbejdsdage", PreviewFor(vm, 0.5m));
        });
    }

    [Fact]
    public void PreviewPlural_FollowsALiveLanguageSwitch()
    {
        WithCulture("en-US", () =>
        {
            var (vm, _) = Build();
            vm.NewStart = Mon5Jan;
            vm.NewEnd = new DateTime(2026, 1, 30); // 20 working days
            Assert.Equal("20 working days", vm.NewEntryPreview);
            Pick(vm, LanguageMode.Romanian);
            Assert.Equal("20 de zile lucrătoare", vm.NewEntryPreview);
            Pick(vm, LanguageMode.Danish);
            Assert.Equal("20 arbejdsdage", vm.NewEntryPreview);
        });
    }

    [Fact]
    public void RomanianFeriefriRow_ColonLabelsFirst()
    {
        var (vm, _) = Scenario();
        Pick(vm, LanguageMode.Romanian);
        var ff = vm.FeriefriRows[0];
        Assert.Equal("folosite: 0", ff.UsedText);
        Assert.Equal("programate: 1", ff.BookedAheadText);
        Assert.Equal("rămase: 4", ff.RemainingText);
        Assert.Equal("Reportate" + "\n" + "în an", Loc.Get(AppLanguage.Romanian, "Table.TransferredIn"));
        Assert.Equal("În anul" + "\n" + "viitor", Loc.Get(AppLanguage.Romanian, "Table.ToNextYear"));
    }
    private static readonly string NL = Environment.NewLine;

    private static string Tip(MainViewModel vm, int month, int day) =>
        vm.CalendarMonths[month - 1].Cells.OfType<DayCell>().Single(c => !c.IsBlank && c.Date.Month == month && c.Date.Day == day).Tooltip;

    [Fact]
    public void SwitchToRomanian_RewritesTheSummaryBannerHolidaysCalendarAndFeriefri()
    {
        var (vm, theme) = Scenario();

        Pick(vm, LanguageMode.Romanian);

        Assert.Equal(AppLanguage.Romanian, theme.Language);
        Assert.Equal("Ascunde rezumatul", vm.SummaryToggleText);
        Assert.Equal("An de concediu 2025/26 · de folosit până la 31 decembrie 2026", vm.HolidayYearTitle);
        // 20 -> "20 de zile", 6 -> "6 zile".
        Assert.Equal("Mai ai de programat 20 de zile, dar de azi până la 31 decembrie 2026 inclusiv sunt disponibile doar 6 zile lucrătoare.", vm.StatusMessage);
        Assert.Equal("Nu se pot folosi 14 zile, chiar dacă programezi toate zilele lucrătoare libere: 5 zile vor fi plătite și 9 zile vor fi pierdute.", vm.StatusDetail);
        Assert.Equal("Feriefri: 4 zile trebuie folosite până la 31 decembrie 2026, altfel sunt plătite în ianuarie.", vm.FeriefriBanner);
        Assert.Equal("Când expiră anul de concediu 2025/26 (31 decembrie 2026)", vm.DeadlineSettingsTitle);
        Assert.Equal("Limbă", Loc.Get("Language.Title"));

        Assert.Equal(
            ["Anul Nou", "Joia Mare", "Vinerea Mare", "Duminica Paștelui", "Lunea Paștelui", "Înălțarea Domnului", "Duminica Rusaliilor",
             "Lunea Rusaliilor", "", "Ajunul Crăciunului", "Crăciunul", "A doua zi de Crăciun", "Ajunul Anului Nou"],
            vm.PublicHolidays.Select(r => r.Name));
        Assert.Equal(
            ["Ianuarie", "Februarie", "Martie", "Aprilie", "Mai", "Iunie", "Iulie", "August", "Septembrie", "Octombrie", "Noiembrie", "Decembrie"],
            vm.CalendarMonths.Select(m => m.Name));
        Assert.Equal("joi, 24 decembrie 2026" + NL + "Ajunul Crăciunului", Tip(vm, 12, 24));
        Assert.Equal("luni, 5 ianuarie 2026" + NL + "Concediu, zi întreagă" + NL + "An de concediu 2025/26", Tip(vm, 1, 5));
        Assert.Equal("luni, 21 decembrie 2026" + NL + "Feriefri, zi întreagă" + NL + "An de concediu 2025/26", Tip(vm, 12, 21));
        Assert.Equal("vineri, 5 iunie 2026" + NL + "Sărbătoare legală (adăugată de tine)", Tip(vm, 6, 5));

        var ff = vm.FeriefriRows[0];
        Assert.Equal("folosite: 0", ff.UsedText);
        Assert.Equal("programate: 1", ff.BookedAheadText);
        Assert.Equal("rămase: 4", ff.RemainingText);
        Assert.Equal("4 vor fi plătite în ianuarie 2027", ff.Note);
        Assert.Equal(["Sistem", "English", "Dansk", "Română", "Redneck"], vm.LanguageOptions.Select(o => o.DisplayName));
    }

    [Fact]
    public void SwitchToRedneck_KeepsEveryNumberDateAndHolidayDateExact()
    {
        var (vm, _) = Scenario();

        Pick(vm, LanguageMode.Redneck);

        // Wording is free; the facts are not.
        var status = vm.StatusMessage;
        Assert.Contains("20", status);
        Assert.Contains("6", status);
        Assert.Contains("31 December 2026", status);
        var detail = vm.StatusDetail;
        foreach (var n in new[] { "14", "5", "9" })
            Assert.Contains(n, detail);
        Assert.Contains("4", vm.FeriefriBanner);
        Assert.Contains("Feriefri", vm.FeriefriBanner);
        Assert.Contains("31 December 2026", vm.FeriefriBanner);
        Assert.NotEqual("You have 20 days not yet booked, but only 6 working days are left from today through 31 December 2026.", status);
        Assert.StartsWith("Thursday 24 December 2026", Tip(vm, 12, 24));
        Assert.StartsWith("Monday 5 January 2026", Tip(vm, 1, 5));
        Assert.Contains("Feriefri", Tip(vm, 12, 21));
        Assert.Equal(
            Enumerable.Range(1, 12).Select(m => new DateOnly(2026, m, 1).ToString("MMMM", CultureInfo.InvariantCulture)).ToList(),
            vm.CalendarMonths.Select(m => m.Name).ToList());
        Assert.Equal("Redneck", vm.SelectedLanguage.DisplayName);
        Assert.Equal(13, vm.PublicHolidays.Count);
        Assert.DoesNotContain(vm.PublicHolidays, r => r.Date == new DateOnly(2026, 6, 5) && r.HasName);
    }

    [Fact]
    public void SwitchingBackToEnglish_GivesExactlyTheOriginalStrings_FromEveryLanguage()
    {
        var (vm, _) = Scenario();
        var original = Snapshot(vm);
        Assert.True(original.Count > 400, "snapshot has " + original.Count + " texts");

        foreach (var mode in new[] { LanguageMode.Danish, LanguageMode.Romanian, LanguageMode.Redneck, LanguageMode.Danish, LanguageMode.Romanian })
        {
            Pick(vm, mode);
            Assert.NotEqual(original["StatusMessage"], Snapshot(vm)["StatusMessage"]);
            Pick(vm, LanguageMode.English);
            Assert.Equal(original, Snapshot(vm));
        }
    }

    [Fact]
    public void EveryTextChanges_ForDanishAndRomanian_AndNoEnglishIsLeft()
    {
        var (vm, _) = Scenario();
        var english = Snapshot(vm);
        string[] englishWords =
        [
            " days", " day ", "booked ", "holiday", "Holiday", "will be", "Christmas", "Remove", "Public", "Vacation", "remaining", "paid out", "lost",
            "Projected", "Default", "Saved to", "must be used", "Set for", "Inherited", "allowance", "Monday", "Thursday", "Friday", "January",
        ];
        // Unchanged texts allowed: language names, month cards that are the same word (August), empty texts, the path, "Feriefri".
        foreach (var mode in new[] { LanguageMode.Danish, LanguageMode.Romanian })
        {
            Pick(vm, mode);
            var now = Snapshot(vm);
            Assert.Equal(english.Keys.Count, now.Count);
            foreach (var (key, value) in now)
            {
                if (key == "DataPathText" || key.StartsWith("Language:") || value.Length == 0 || key.StartsWith("Month "))
                    continue;
                Assert.True(value != english[key], mode + " " + key + " is still '" + value + "'");
                foreach (var word in englishWords)
                    Assert.True(!value.Contains(word, StringComparison.Ordinal), mode + " " + key + " has English '" + word + "': " + value);
            }
        }
    }

    [Fact]
    public void Switching_ClearsAnErrorMessage_AndAnInfoMessage()
    {
        var (vm, _) = Scenario();
        vm.AddEntryCommand.Execute(null); // no dates
        Assert.Equal("Pick a start and end date.", vm.ErrorMessage);
        Pick(vm, LanguageMode.Danish);
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal("", vm.InfoMessage);

        vm.NewHoliday = new DateTime(2026, 12, 24);
        vm.AddHolidayCommand.Execute(null);
        Assert.Equal("24. december 2026 er allerede en helligdag (Juleaftensdag).", vm.InfoMessage);
        Pick(vm, LanguageMode.Romanian);
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal("", vm.InfoMessage);
    }

    [Fact]
    public void MessagesProducedAfterTheSwitch_AreInTheNewLanguage()
    {
        var (vm, _) = Scenario();

        Pick(vm, LanguageMode.Danish);
        vm.AddEntryCommand.Execute(null);
        Assert.Equal("Vælg en startdato og en slutdato.", vm.ErrorMessage);
        vm.NewStart = new DateTime(2026, 12, 24); // Thu, Christmas Eve
        vm.AddEntryCommand.Execute(null);
        // A sentence starting with a date starts with a capital.
        Assert.Equal("Torsdag den 24. december 2026 er en helligdag (Juleaftensdag).", vm.ErrorMessage);

        Pick(vm, LanguageMode.Romanian);
        vm.NewStart = new DateTime(2026, 12, 24);
        vm.AddEntryCommand.Execute(null);
        Assert.Equal("Joi, 24 decembrie 2026 este sărbătoare legală (Ajunul Crăciunului).", vm.ErrorMessage);
        vm.NewStart = new DateTime(2026, 12, 23);
        vm.NewEnd = new DateTime(2026, 12, 22);
        vm.AddEntryCommand.Execute(null);
        Assert.Equal("Data de sfârșit nu poate fi înaintea datei de început.", vm.ErrorMessage);
    }

    [Fact]
    public void FeriefriPartialBooking_PluralsAndTextFollowTheLanguage()
    {
        // 3 Feriefri days used in January 2026; 2 left in 2025/26; asking for Mon 12 .. Fri 16 Jan books 2 and leaves 3 out.
        var data = Seed(0);
        foreach (var d in new[] { 5, 6, 7 })
            data.Days.Add(new BookedDay { Date = new DateOnly(2026, 1, d), Type = DayType.Feriefri, Amount = 1 });
        var (vm, _) = Build(data: data);

        Pick(vm, LanguageMode.Danish);
        vm.BookFeriefri = true;
        vm.NewStart = new DateTime(2026, 1, 12);
        vm.NewEnd = new DateTime(2026, 1, 16);
        vm.AddEntryCommand.Execute(null);
        Assert.Equal("2 feriefridage booket; 3 dage udeladt, fordi feriefridagene for 2025/26 er brugt.", vm.InfoMessage);

        // 1 left (4 of 5 used): asking for Mon 12 and Tue 13 January books 1 and leaves 1 out.
        var data2 = Seed(0);
        foreach (var d in new[] { 5, 6, 7, 8 })
            data2.Days.Add(new BookedDay { Date = new DateOnly(2026, 1, d), Type = DayType.Feriefri, Amount = 1 });
        File.Delete(DataFile);
        var (vm2, _) = Build(data: data2);
        Pick(vm2, LanguageMode.Romanian);
        vm2.BookFeriefri = true;
        vm2.NewStart = new DateTime(2026, 1, 12);
        vm2.NewEnd = new DateTime(2026, 1, 13);
        vm2.AddEntryCommand.Execute(null);
        Assert.Equal("Programat: 1 zi Feriefri; lăsat deoparte: 1 zi, deoarece zilele Feriefri pentru 2025/26 sunt epuizate.", vm2.InfoMessage);
    }

    [Fact]
    public void ConflictNoteAndNotCountedRows_FollowTheLanguage()
    {
        // 24 and 25 Dec 2026 (Thu, Fri) are public holidays; vacation stored on them counts for nothing.
        var data = Seed(0);
        data.Days.Add(new BookedDay { Date = new DateOnly(2026, 12, 24), Type = DayType.Vacation, Amount = 1 });
        data.Days.Add(new BookedDay { Date = new DateOnly(2026, 12, 25), Type = DayType.Vacation, Amount = 1 });
        var (vm, _) = Build(data: data);
        Assert.Equal("2 booked days fall on public holidays and are not counted: 24 December 2026, 25 December 2026.", vm.ConflictNote);
        Assert.Equal("public holiday – not counted", vm.Entries.Single().NotCountedSuffix.Trim());

        Pick(vm, LanguageMode.Danish);
        Assert.Equal("2 bookede dage ligger på helligdage og tæller ikke: 24. december 2026, 25. december 2026.", vm.ConflictNote);
        Assert.Equal("helligdag – tæller ikke", vm.Entries.Single().NotCountedSuffix.Trim());

        Pick(vm, LanguageMode.Romanian);
        Assert.Equal("2 zile programate cad în sărbători legale și nu se numără: 24 decembrie 2026, 25 decembrie 2026.", vm.ConflictNote);
        Assert.Equal("sărbătoare – nu se numără", vm.Entries.Single().NotCountedSuffix.Trim());
    }

    [Fact]
    public void ConflictNote_OneDay_SingularFormsPerLanguage()
    {
        var data = Seed(0);
        data.Days.Add(new BookedDay { Date = new DateOnly(2026, 12, 24), Type = DayType.Vacation, Amount = 1 });
        var (vm, _) = Build(data: data);

        Pick(vm, LanguageMode.Danish);
        Assert.Equal("1 booket dag ligger på helligdage og tæller ikke: 24. december 2026.", vm.ConflictNote);
        Pick(vm, LanguageMode.Romanian);
        Assert.Equal("1 zi programată cade într-o sărbătoare legală și nu se numără: 24 decembrie 2026.", vm.ConflictNote);
    }

    [Fact]
    public void Switching_NeverTouchesTheVacationDataFile_OnlyThePreferences()
    {
        var (vm, theme) = Scenario();
        var bytes = File.ReadAllBytes(DataFile);
        var written = File.GetLastWriteTimeUtc(DataFile);
        var prefsBefore = File.Exists(Prefs) ? File.ReadAllText(Prefs) : "";
        // The data file is read-only, so any attempt to save would fail and show an error.
        File.SetAttributes(DataFile, File.GetAttributes(DataFile) | FileAttributes.ReadOnly);
        try
        {
            foreach (var mode in new[] { LanguageMode.Danish, LanguageMode.Romanian, LanguageMode.Redneck, LanguageMode.English, LanguageMode.System })
            {
                Pick(vm, mode);
                Assert.Equal("", vm.ErrorMessage);
                Assert.Equal(bytes, File.ReadAllBytes(DataFile));
                Assert.Equal(written, File.GetLastWriteTimeUtc(DataFile));
            }
            Pick(vm, LanguageMode.Danish);
        }
        finally
        {
            File.SetAttributes(DataFile, File.GetAttributes(DataFile) & ~FileAttributes.ReadOnly);
        }

        var prefsAfter = File.ReadAllText(Prefs);
        Assert.NotEqual(prefsBefore, prefsAfter);
        Assert.Contains("\"language\": \"Danish\"", prefsAfter);
        Assert.Equal(bytes, File.ReadAllBytes(DataFile));
        Assert.Equal(LanguageMode.Danish, theme.LanguageMode);
    }

    [Fact]
    public void Switching_RaisesAnEverythingChangedNotification_OnlyWhenTheChoiceChanges()
    {
        var (vm, _) = Scenario();
        var names = new List<string?>();
        vm.PropertyChanged += (_, e) => names.Add(e.PropertyName);

        Pick(vm, LanguageMode.English); // already System/English? System is the default, so this is a change
        Assert.Contains(string.Empty, names);

        names.Clear();
        Pick(vm, LanguageMode.English); // same again
        Assert.Empty(names);

        vm.SelectedLanguage = null!;
        Assert.Empty(names);
        Assert.Equal(LanguageMode.English, vm.SelectedLanguage.Mode);
    }

    [Fact]
    public void SelectedLanguage_Defaults_ToSystem_AndTheOptionsAreInPickerOrder()
    {
        var (vm, theme) = Build();
        Assert.Equal(LanguageMode.System, vm.SelectedLanguage.Mode);
        Assert.Equal(
            [LanguageMode.System, LanguageMode.English, LanguageMode.Danish, LanguageMode.Romanian, LanguageMode.Redneck],
            vm.LanguageOptions.Select(o => o.Mode));
        Assert.Equal(AppLanguage.English, theme.Language);
    }

    [Fact]
    public void WithoutAThemeService_SelectedLanguageIsSystem_AndWritingItChangesNothing()
    {
        var store = new DataStore(DataFile);
        var vm = new MainViewModel(store, () => Dec18);
        var before = vm.StatusMessage;
        vm.SelectedLanguage = vm.LanguageOptions.Single(o => o.Mode == LanguageMode.Danish);
        Assert.Equal(before, vm.StatusMessage);
        Assert.Equal(LanguageMode.System, vm.SelectedLanguage.Mode);
        Assert.Equal(AppLanguage.English, Loc.Current);
    }

    // ---- Preferences ----

    [Fact]
    public void Language_IsRemembered_AcrossRestart_AlongsideThemeAndSummary()
    {
        Directory.CreateDirectory(Dir);
        using (var t = new ThemeService(Prefs, () => false))
        {
            t.Mode = ThemeMode.Dark;
            t.SummaryExpanded = false;
            t.LanguageMode = LanguageMode.Romanian;
        }
        using var again = new ThemeService(Prefs, () => false);
        Assert.Equal(LanguageMode.Romanian, again.LanguageMode);
        Assert.Equal(AppLanguage.Romanian, again.Language);
        Assert.Equal(ThemeMode.Dark, again.Mode);
        Assert.False(again.SummaryExpanded);

        // Changing the theme afterwards keeps the language, and vice versa.
        again.Mode = ThemeMode.Light;
        using var third = new ThemeService(Prefs, () => false);
        Assert.Equal(LanguageMode.Romanian, third.LanguageMode);
        Assert.Equal(ThemeMode.Light, third.Mode);
        Assert.False(third.SummaryExpanded);
    }

    [Theory]
    [InlineData(LanguageMode.System)]
    [InlineData(LanguageMode.English)]
    [InlineData(LanguageMode.Danish)]
    [InlineData(LanguageMode.Romanian)]
    [InlineData(LanguageMode.Redneck)]
    public void EveryChoice_RoundTripsThroughTheFile(LanguageMode mode)
    {
        Directory.CreateDirectory(Dir);
        using (var t = new ThemeService(Prefs, () => false))
        {
            t.LanguageMode = LanguageMode.Danish;
            t.LanguageMode = mode;
        }
        using var again = new ThemeService(Prefs, () => false);
        Assert.Equal(mode, again.LanguageMode);
    }

    [Fact]
    public void OldFileWithoutTheField_GivesSystem_AndKeepsTheOtherSettings()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Prefs, "{\"theme\":\"Dark\",\"summaryExpanded\":false}");
        using var t = new ThemeService(Prefs, () => false);
        Assert.Equal(LanguageMode.System, t.LanguageMode);
        Assert.Equal(ThemeMode.Dark, t.Mode);
        Assert.False(t.SummaryExpanded);
        Assert.Equal(AppLanguage.English, t.Language);

        using var danishWindows = new ThemeService(Prefs, () => false, () => CultureInfo.GetCultureInfo("da-DK"));
        Assert.Equal(AppLanguage.Danish, danishWindows.Language);
        using var romanianWindows = new ThemeService(Prefs, () => false, () => CultureInfo.GetCultureInfo("ro-RO"));
        Assert.Equal(AppLanguage.Romanian, romanianWindows.Language);
    }

    [Theory]
    [InlineData("{\"language\":\"Klingon\"}")]
    [InlineData("{\"language\":\"\"}")]
    [InlineData("{\"language\":42}")]
    [InlineData("{\"language\":null}")]
    [InlineData("{\"language\":\"99\"}")]
    [InlineData("{\"language\":\"-1\"}")]
    [InlineData("{\"language\":[\"Danish\"]}")]
    [InlineData("{\"language\":{}}")]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"Danish\"")]
    public void UnknownOrCorruptValue_GivesSystem_AndDoesNotThrow(string content)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Prefs, content);
        using var t = new ThemeService(Prefs, () => false);
        Assert.Equal(LanguageMode.System, t.LanguageMode);
        Assert.Equal(AppLanguage.English, t.Language);
    }

    [Theory]
    [InlineData("danish", LanguageMode.Danish)]
    [InlineData("ROMANIAN", LanguageMode.Romanian)]
    [InlineData("Redneck", LanguageMode.Redneck)]
    [InlineData("English", LanguageMode.English)]
    public void StoredName_IsReadCaseInsensitively(string stored, LanguageMode expected)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Prefs, "{\"language\":\"" + stored + "\"}");
        using var t = new ThemeService(Prefs, () => false);
        Assert.Equal(expected, t.LanguageMode);
    }

    [Fact]
    public void CorruptFile_ThenPickingALanguage_OverwritesItWithValidJson_AndTheVmKeepsWorking()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Prefs, "{ corrupt");
        var (vm, _) = Build();
        Pick(vm, LanguageMode.Danish);
        Assert.Equal("Skjul oversigt", vm.SummaryToggleText);
        using var again = new ThemeService(Prefs, () => false);
        Assert.Equal(LanguageMode.Danish, again.LanguageMode);
    }

    [Fact]
    public void PreferencesThatCannotBeWritten_StillSwitchTheLanguageInMemory()
    {
        Directory.CreateDirectory(Dir);
        Directory.CreateDirectory(Prefs); // a directory where the file should be
        var (vm, theme) = Build();
        Pick(vm, LanguageMode.Romanian);
        Assert.Equal(AppLanguage.Romanian, theme.Language);
        Assert.Equal("Ascunde rezumatul", vm.SummaryToggleText);
        Assert.Equal("", vm.ErrorMessage);
    }

    [Fact]
    public void ThemeServiceWithoutAFile_IsSystem_AndNeverWrites()
    {
        using var t = new ThemeService(null, () => false);
        t.LanguageMode = LanguageMode.Danish;
        Assert.Equal(AppLanguage.Danish, t.Language);
        Assert.False(File.Exists(Prefs));
    }

    // ---- Machine independence and thread isolation ----

    [Fact]
    public void ViewModelWithoutATheme_IsEnglish_WhateverTheWindowsLanguageIs()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("da-DK");
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("da-DK");
        Loc.SetLanguage(AppLanguage.Danish); // left over from something else

        var vm = new MainViewModel(new DataStore(DataFile), () => Dec18);

        Assert.Equal(AppLanguage.English, Loc.Current);
        Assert.Equal("Hide summary", vm.SummaryToggleText);
        Assert.StartsWith("You have", vm.StatusMessage);
        Assert.Equal("December", vm.CalendarMonths[11].Name);
        Assert.Equal("Christmas Eve", vm.PublicHolidays.Single(r => r.Date == new DateOnly(2026, 12, 24)).Name);
    }

    [Fact]
    public void ThemeServiceWithoutACultureProvider_IgnoresTheThreadsUiCulture()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ro-RO");
        var (vm, theme) = Build();
        Assert.Equal(AppLanguage.English, theme.Language);
        Assert.Equal("Hide summary", vm.SummaryToggleText);
    }

    [Fact]
    public void SystemChoice_WithADanishWindows_StartsInDanish_AndExplicitEnglishOverridesIt()
    {
        var (vm, theme) = Build(CultureInfo.GetCultureInfo("da-DK"));
        Assert.Equal(AppLanguage.Danish, Loc.Current);
        Assert.Equal("Skjul oversigt", vm.SummaryToggleText);
        Assert.Equal("System", vm.SelectedLanguage.DisplayName);

        Pick(vm, LanguageMode.English);
        Assert.Equal("Hide summary", vm.SummaryToggleText);
        Assert.Equal(AppLanguage.English, theme.Language);

        Pick(vm, LanguageMode.System);
        Assert.Equal("Skjul oversigt", vm.SummaryToggleText);
        Assert.Equal(AppLanguage.Danish, Loc.Current);
    }

    [Fact]
    public void SystemChoice_WithARomanianWindows_StartsInRomanian_ButRedneckIsNeverChosenAutomatically()
    {
        var (vm, _) = Build(CultureInfo.GetCultureInfo("ro-RO"));
        Assert.Equal(AppLanguage.Romanian, Loc.Current);
        Assert.Equal("Sistem", vm.SelectedLanguage.DisplayName);
        Assert.Equal("Ascunde rezumatul", vm.SummaryToggleText);
    }

    [Fact]
    public async Task LanguagesOnParallelThreads_DoNotSeeEachOther()
    {
        var tasks = new[] { AppLanguage.Danish, AppLanguage.Romanian, AppLanguage.English, AppLanguage.Redneck }
            .Select(language => Task.Factory.StartNew(() =>
            {
                Loc.SetLanguage(language);
                for (var i = 0; i < 2000; i++)
                {
                    if (Loc.Current != language)
                        return language + " saw " + Loc.Current;
                    Thread.Yield();
                }
                return "";
            }, TaskCreationOptions.LongRunning));
        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.Equal("", r));
    }

    [Fact]
    public void TwoViewModels_OnTheSameThread_LastWins_ButAFreshOneWithoutThemeResetsToEnglish()
    {
        var (da, _) = Build(CultureInfo.GetCultureInfo("da-DK"));
        Assert.Equal(AppLanguage.Danish, Loc.Current);
        var plain = new MainViewModel(new DataStore(DataFile), () => Dec18);
        Assert.Equal(AppLanguage.English, Loc.Current);
        Assert.Equal("Hide summary", plain.SummaryToggleText);
        Assert.NotNull(da);
    }
}

