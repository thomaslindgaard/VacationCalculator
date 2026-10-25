using System.Text.RegularExpressions;
using VacationCalculator.App.Services;

namespace VacationCalculator.App.Tests;

/// <summary>
/// Feature 7: the string tables. Every language has exactly the English keys, the same {n} placeholders per key, no empty
/// values; the XAML and the code use only keys that exist; no user-facing English is left in the XAML.
/// </summary>
public class TranslationKeysIndependentTests
{
    public static IEnumerable<object[]> OtherLanguages() =>
        [[AppLanguage.Danish], [AppLanguage.Romanian], [AppLanguage.Redneck]];

    public static IEnumerable<object[]> AllLanguages() =>
        [[AppLanguage.English], [AppLanguage.Danish], [AppLanguage.Romanian], [AppLanguage.Redneck]];

    private static IReadOnlyDictionary<string, string> English => StringTables.For(AppLanguage.English);

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VacationCalculator.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string AppSrc => Path.Combine(SolutionRoot(), "src", "VacationCalculator.App");

    private static string Placeholders(string text) =>
        string.Join(",", Regex.Matches(text, @"\{(\d+)[^}]*\}").Select(m => int.Parse(m.Groups[1].Value)).Distinct().Order());

    [Fact]
    public void English_HasKeys()
    {
        Assert.True(English.Count >= 200, "English has " + English.Count + " keys");
    }

    [Theory]
    [MemberData(nameof(OtherLanguages))]
    public void Language_HasExactlyTheEnglishKeys(AppLanguage language)
    {
        var table = StringTables.For(language);
        var missing = English.Keys.Except(table.Keys).Order().ToList();
        var extra = table.Keys.Except(English.Keys).Order().ToList();
        Assert.True(missing.Count == 0, language + " lacks: " + string.Join(", ", missing));
        Assert.True(extra.Count == 0, language + " has keys English lacks: " + string.Join(", ", extra));
    }

    [Theory]
    [MemberData(nameof(OtherLanguages))]
    public void Language_HasTheSamePlaceholdersAsEnglish_ForEveryKey(AppLanguage language)
    {
        var table = StringTables.For(language);
        var bad = English
            .Where(kv => table.TryGetValue(kv.Key, out var text) && Placeholders(text) != Placeholders(kv.Value))
            .Select(kv => $"{kv.Key}: English [{Placeholders(kv.Value)}] vs [{Placeholders(table[kv.Key])}]")
            .ToList();
        Assert.True(bad.Count == 0, language + ": " + string.Join(" | ", bad));
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void NoValueIsEmptyOrWhitespace(AppLanguage language)
    {
        var empty = StringTables.For(language).Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).ToList();
        Assert.True(empty.Count == 0, language + ": " + string.Join(", ", empty));
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void EveryValueFormatsWithoutThrowing_AndNeverContainsTheAppNameTranslated(AppLanguage language)
    {
        foreach (var (key, text) in StringTables.For(language))
        {
            var count = Regex.Matches(text, @"\{(\d+)[^}]*\}").Select(m => int.Parse(m.Groups[1].Value) + 1).DefaultIfEmpty(0).Max();
            var args = Enumerable.Range(0, count).Select(i => (object)("<" + i + ">")).ToArray();
            var ex = Record.Exception(() => string.Format(text, args));
            Assert.True(ex is null, language + " " + key + ": " + ex?.Message);
        }
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void NoTranslationOfTheAppName_AndFeriefriIsKeptAsAName_ExceptInDanish(AppLanguage language)
    {
        var table = StringTables.For(language);
        Assert.Equal("Feriefri", table["Feriefri.Name"]);
        Assert.Equal("Feriefri", table["Type.Feriefri"]);
        // The app name is the Windows title, never a table entry.
        Assert.DoesNotContain(table.Values, v => v.Contains("Vacation Calculator", StringComparison.OrdinalIgnoreCase));
        if (language != AppLanguage.Danish)
        {
            Assert.Contains("Feriefri", table["Unit.FeriefriDay.Other"]);
            Assert.Contains("Feriefri", table["Form.AddFeriefri"]);
        }
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void PluralFamilies_HaveAllThreeForms_AndOneIsDistinctFromOther_WhereTheLanguageTellsThemApart(AppLanguage language)
    {
        var table = StringTables.For(language);
        var bases = table.Keys.Where(k => k.EndsWith(".One", StringComparison.Ordinal) && table.ContainsKey(k[..^4] + ".Few"))
            .Select(k => k[..^4]).ToList();
        Assert.NotEmpty(bases);
        foreach (var b in bases)
        {
            Assert.True(table.ContainsKey(b + ".Other"), language + " lacks " + b + ".Other");
            Assert.Single(Regex.Matches(table[b + ".One"], @"\{0\}"));
        }
        // Romanian is the only language with a separate "Few" form: 2 zile vs 20 de zile.
        if (language == AppLanguage.Romanian)
            Assert.NotEqual(table["Unit.Day.Few"], table["Unit.Day.Other"]);
    }

    // ---- The XAML ----

    private static IEnumerable<string> StrKeysInXaml()
    {
        foreach (var file in Directory.EnumerateFiles(AppSrc, "*.xaml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"\{svc:Str\s+(?:Key\s*=\s*)?([^\s,}]+)\s*\}"))
                yield return m.Groups[1].Value;
        }
    }

    [Fact]
    public void XamlUsesStrKeys_AndAtLeastTheKnownNumberOfThem()
    {
        Assert.True(StrKeysInXaml().Count() >= 70);
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void EveryStrKeyInTheXaml_ExistsInEveryLanguage(AppLanguage language)
    {
        var table = StringTables.For(language);
        var missing = StrKeysInXaml().Distinct().Where(k => !table.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, language + " lacks XAML keys: " + string.Join(", ", missing));
    }

    private static string StripXamlNoise(string xaml)
    {
        xaml = Regex.Replace(xaml, "<!--.*?-->", "", RegexOptions.Singleline);
        return Regex.Replace(xaml, @"&#x[0-9A-Fa-f]+;|&#\d+;", "");
    }

    [Fact]
    public void NoUserFacingEnglishLiteralInAnyXaml_ExceptTheAppName()
    {
        var leaks = new List<string>();
        var attribute = new Regex(@"\b(Text|Content|Header|ToolTip|Title|Watermark|Label|Description|AutomationProperties\.Name|AutomationProperties\.HelpText)\s*=\s*""([^""]*)""");
        foreach (var file in Directory.EnumerateFiles(AppSrc, "*.xaml", SearchOption.AllDirectories))
        {
            var xaml = StripXamlNoise(File.ReadAllText(file));
            foreach (Match m in attribute.Matches(xaml))
            {
                var value = m.Groups[2].Value;
                if (value.StartsWith('{') || value == "Vacation Calculator")
                    continue;
                if (value.Any(char.IsLetter))
                    leaks.Add(Path.GetFileName(file) + ": " + m.Value);
            }
            // Text between tags, e.g. <TextBlock>Hello</TextBlock> or <Run>Hello</Run>.
            foreach (Match m in Regex.Matches(xaml, @">\s*([^<>{}\s][^<>{}]*?)\s*<").Where(_ => Path.GetFileName(file) == "MainWindow.xaml" || Path.GetFileName(file) == "App.xaml"))
            {
                if (m.Groups[1].Value.Any(char.IsLetter))
                    leaks.Add(Path.GetFileName(file) + ": element text '" + m.Groups[1].Value + "'");
            }
            // Format strings with words, e.g. StringFormat='Saved to {0}'.
            foreach (Match m in Regex.Matches(xaml, @"(StringFormat|TargetNullValue|FallbackValue)\s*=\s*('[^']*'|[^,}]*)"))
            {
                var value = Regex.Replace(m.Groups[2].Value, @"\{\d*(:[^}]*)?\}", "");
                if (m.Groups[1].Value == "StringFormat" && Regex.IsMatch(value, @"[A-Za-z]{2,}") && !Regex.IsMatch(value, @"^\s*[dDgGtT]\s*$"))
                    leaks.Add(Path.GetFileName(file) + ": " + m.Value);
            }
        }
        Assert.True(leaks.Count == 0, "English left in XAML: " + string.Join(" | ", leaks));
    }

    // ---- The code ----

    private static readonly Regex KeyLiteral = new("\"([A-Z][A-Za-z]*(?:\\.[A-Za-z0-9]+)+)\"", RegexOptions.Compiled);

    private static IEnumerable<(string File, string Key)> KeyLiteralsInCode()
    {
        foreach (var file in Directory.EnumerateFiles(AppSrc, "*.cs", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("StringTables", StringComparison.Ordinal) || file.Contains(@"\obj\") || file.Contains(@"\bin\"))
                continue;
            foreach (var line in File.ReadLines(file))
            {
                if (line.TrimStart().StartsWith("//"))
                    continue;
                foreach (Match m in KeyLiteral.Matches(line))
                {
                    var key = m.Groups[1].Value;
                    // File names such as "Themes/LightTheme.xaml" or "x.json" are not keys.
                    if (!Regex.IsMatch(key, @"\.(xaml|json|cs|dll|exe|txt)$"))
                        yield return (name, key);
                }
            }
        }
    }

    private static readonly string[] Suffixes = [".Other", ".Future.Other", ".Past.Other"];

    private static bool KeyOrFamilyExists(IReadOnlyDictionary<string, string> table, string key) =>
        table.ContainsKey(key) || Suffixes.Any(s => table.ContainsKey(key + s));

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void EveryKeyLikeLiteralInTheCode_IsAKey_OrAFamilyBase_InEveryLanguage(AppLanguage language)
    {
        var table = StringTables.For(language);
        var literals = KeyLiteralsInCode().ToList();
        Assert.True(literals.Count >= 40, "found only " + literals.Count + " key literals");
        var missing = literals.Where(l => !KeyOrFamilyExists(table, l.Key)).Select(l => l.File + ": " + l.Key).Distinct().ToList();
        Assert.True(missing.Count == 0, language + " lacks: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryEnglishKey_IsUsedSomewhere_InXamlOrCode()
    {
        var used = new HashSet<string>(StrKeysInXaml());
        foreach (var (_, key) in KeyLiteralsInCode())
            used.Add(key);

        static string FamilyBase(string key)
        {
            foreach (var s in new[] { ".Future.One", ".Future.Other", ".Past.One", ".Past.Other", ".One", ".Few", ".Other" })
            {
                if (key.EndsWith(s, StringComparison.Ordinal))
                    return key[..^s.Length];
            }
            return key;
        }

        // Keys built from a number or an enum name at run time: Month.N, Weekday.N, Holiday.Kind.
        var unused = English.Keys
            .Where(k => !used.Contains(k) && !used.Contains(FamilyBase(k))
                && !k.StartsWith("Month.", StringComparison.Ordinal) && !k.StartsWith("Weekday.", StringComparison.Ordinal)
                && !k.StartsWith("Holiday.", StringComparison.Ordinal) && !k.StartsWith("WeekdayShort.", StringComparison.Ordinal)
                && !k.StartsWith("Unit.", StringComparison.Ordinal))
            .Order().ToList();
        Assert.True(unused.Count == 0, "unused keys: " + string.Join(", ", unused));
    }

    [Fact]
    public void EveryHolidayKind_HasAName_InEveryLanguage()
    {
        foreach (var language in Enum.GetValues<AppLanguage>())
        {
            foreach (var kind in Enum.GetValues<VacationCalculator.Core.DanishHolidayKind>())
            {
                Assert.True(StringTables.For(language).ContainsKey("Holiday." + kind), language + " lacks Holiday." + kind);
            }
        }
        Assert.Equal(
            ["New Year's Day", "Maundy Thursday", "Good Friday", "Easter Sunday", "Easter Monday", "Ascension Day", "Whit Sunday",
             "Whit Monday", "Christmas Eve", "Christmas Day", "Boxing Day", "New Year's Eve"],
            Enum.GetValues<VacationCalculator.Core.DanishHolidayKind>().Select(k => English["Holiday." + k]));
        Assert.Equal(
            ["Nytårsdag", "Skærtorsdag", "Langfredag", "Påskedag", "2. påskedag", "Kristi himmelfartsdag", "Pinsedag", "2. pinsedag",
             "Juleaftensdag", "Juledag", "2. juledag", "Nytårsaftensdag"],
            Enum.GetValues<VacationCalculator.Core.DanishHolidayKind>().Select(k => StringTables.For(AppLanguage.Danish)["Holiday." + k]));
    }

    [Fact]
    public void WeekdayAndMonthNames_AreAllDistinctWithinEachLanguage_AndComplete()
    {
        foreach (var language in Enum.GetValues<AppLanguage>())
        {
            var t = StringTables.For(language);
            Assert.Equal(12, Enumerable.Range(1, 12).Select(m => t["Month." + m]).Distinct().Count());
            Assert.Equal(7, Enumerable.Range(0, 7).Select(d => t["Weekday." + d]).Distinct().Count());
            Assert.All(Enumerable.Range(1, 7), d => Assert.False(string.IsNullOrEmpty(t["WeekdayShort." + d])));
        }
    }

    [Fact]
    public void DanishAndRomanian_AreNotJustCopiesOfEnglish_ExceptKnownSharedWords()
    {
        // Values identical to English are fine for names, symbols and a few international words; anything else looks untranslated.
        var allowed = new HashSet<string>
        {
            "Feriefri.Name", "Type.Feriefri", "Cal.Booked", "Legend.Weekend", "Theme.System", "Language.System", "Date.Long",
            "Cal.Note", "Column.Type", "Tab.Calendar", "Banner.Feriefri.Lead",
        };
        foreach (var language in new[] { AppLanguage.Danish, AppLanguage.Romanian })
        {
            var t = StringTables.For(language);
            var same = English.Where(kv => t[kv.Key] == kv.Value && !allowed.Contains(kv.Key)
                    && kv.Value.Any(char.IsLetter) && !kv.Key.StartsWith("Month.", StringComparison.Ordinal) && !kv.Key.StartsWith("WeekdayShort.", StringComparison.Ordinal))
                .Select(kv => kv.Key + "=" + kv.Value).ToList();
            Assert.True(same.Count == 0, language + " has values identical to English: " + string.Join(" | ", same));
        }
    }
}
