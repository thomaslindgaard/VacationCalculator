using VacationCalculator.App.Services;
using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

public class ThemeServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vc-theme-" + Guid.NewGuid().ToString("N"));

    public ThemeServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private string Prefs => Path.Combine(_dir, "vacation-calculator-preferences.json");
    private string Data => Path.Combine(_dir, "vacation-data.json");

    [Fact]
    public void NoFile_DefaultsToSystem()
    {
        using var t = new ThemeService(Prefs);
        Assert.Equal(ThemeMode.System, t.Mode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"theme\":\"Purple\"}")]
    [InlineData("{\"theme\":42}")]
    [InlineData("{\"theme\":null}")]
    [InlineData("{\"theme\":\"7\"}")]
    [InlineData("{\"theme\":\"-3\"}")]
    public void CorruptOrUnknownFile_GivesSystem(string content)
    {
        File.WriteAllText(Prefs, content);
        using var t = new ThemeService(Prefs);
        Assert.Equal(ThemeMode.System, t.Mode);
    }

    [Fact]
    public void PathThatIsDirectory_GivesSystemAndDoesNotThrow()
    {
        Directory.CreateDirectory(Prefs);
        using var t = new ThemeService(Prefs);
        Assert.Equal(ThemeMode.System, t.Mode);
        t.Mode = ThemeMode.Dark; // write fails; must not throw
        Assert.Equal(ThemeMode.Dark, t.Mode);
    }

    [Theory]
    [InlineData("Light", ThemeMode.Light)]
    [InlineData("Dark", ThemeMode.Dark)]
    [InlineData("System", ThemeMode.System)]
    [InlineData("dark", ThemeMode.Dark)]
    public void ReadsStoredValue(string value, ThemeMode expected)
    {
        File.WriteAllText(Prefs, "{\"theme\":\"" + value + "\"}");
        using var t = new ThemeService(Prefs);
        Assert.Equal(expected, t.Mode);
    }

    [Theory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void ChoosingMode_PersistsAndReloads(ThemeMode mode)
    {
        using (var t = new ThemeService(Prefs))
            t.Mode = mode;
        Assert.True(File.Exists(Prefs));
        using var again = new ThemeService(Prefs);
        Assert.Equal(mode, again.Mode);
    }

    [Fact]
    public void ChoosingSystemAfterDark_PersistsSystem()
    {
        File.WriteAllText(Prefs, "{\"theme\":\"Dark\"}");
        using (var t = new ThemeService(Prefs))
            t.Mode = ThemeMode.System;
        using var again = new ThemeService(Prefs);
        Assert.Equal(ThemeMode.System, again.Mode);
    }

    [Fact]
    public void LightAndDark_IsDarkMatchesWithoutRegistry()
    {
        using var t = new ThemeService(Prefs);
        t.Mode = ThemeMode.Dark;
        Assert.True(t.IsDark);
        t.Mode = ThemeMode.Light;
        Assert.False(t.IsDark);
    }

    [Fact]
    public void PreferencesPathFor_IsNextToDataFile()
    {
        Assert.Equal(Prefs, ThemeService.PreferencesPathFor(Data));
        var rel = ThemeService.PreferencesPathFor(Path.Combine("some dir", "d.json"));
        Assert.Equal(Path.Combine(Path.GetFullPath("some dir"), "vacation-calculator-preferences.json"), rel);
    }

    [Fact]
    public void ThemeChanges_NeverCreateOrModifyDataFile()
    {
        // No data file: must not be created.
        var vm = new MainViewModel(new DataStore(Data), () => new DateOnly(2030, 7, 10), new ThemeService(Prefs));
        var existedBefore = File.Exists(Data);
        vm.IsDarkTheme = true;
        vm.IsLightTheme = true;
        vm.IsSystemTheme = true;
        Assert.Equal(existedBefore, File.Exists(Data));

        // Existing data file: bytes unchanged.
        const string json = "{\"Years\":{}}";
        File.WriteAllText(Data, json);
        var stamp = File.GetLastWriteTimeUtc(Data);
        vm.IsDarkTheme = true;
        Assert.Equal(json, File.ReadAllText(Data));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(Data));
    }

    [Fact]
    public void ViewModel_RadioPropertiesFollowMode_AndRaiseNotifications()
    {
        using var t = new ThemeService(Prefs);
        var vm = new MainViewModel(new DataStore(Data), () => new DateOnly(2030, 7, 10), t);
        Assert.True(vm.IsSystemTheme);
        Assert.False(vm.IsLightTheme);
        Assert.False(vm.IsDarkTheme);

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.IsDarkTheme = true;
        Assert.True(vm.IsDarkTheme);
        Assert.False(vm.IsSystemTheme);
        Assert.Contains("IsDarkTheme", raised);
        Assert.Contains("IsSystemTheme", raised);

        vm.IsLightTheme = false; // unchecking must not change the mode
        Assert.True(vm.IsDarkTheme);

        vm.IsLightTheme = true;
        Assert.Equal(ThemeMode.Light, t.Mode);
        Assert.Contains("\"Light\"", File.ReadAllText(Prefs));
    }

    [Fact]
    public void ViewModelWithoutTheme_StillConstructs()
    {
        var vm = new MainViewModel(new DataStore(Data), () => new DateOnly(2030, 7, 10));
        Assert.True(vm.IsSystemTheme);
        vm.IsDarkTheme = true; // no-op
        Assert.False(vm.IsDarkTheme);
    }

    [Theory]
    [InlineData("vacation-calculator-preferences.json")]
    [InlineData("VACATION-Calculator-Preferences.JSON")]
    public void PreferencesPathFor_IsNullWhenDataFileIsThePreferencesName(string name)
    {
        Assert.Null(ThemeService.PreferencesPathFor(Path.Combine(_dir, name)));
        Assert.Null(ThemeService.PreferencesPathFor(Path.Combine(_dir, "sub", "..", name)));
    }

    [Fact]
    public void NullPath_NeverCreatesOrChangesAnyFile()
    {
        using var t = new ThemeService(null, () => true);
        Assert.Equal(ThemeMode.System, t.Mode);
        t.Mode = ThemeMode.Dark;
        t.Mode = ThemeMode.Light;
        Assert.Equal(ThemeMode.Light, t.Mode);
        Assert.Empty(Directory.GetFileSystemEntries(_dir));
    }

    [Fact]
    public void DataFileNamedLikePreferences_IsNeverReadOrOverwritten_ThroughViewModel()
    {
        var data = Path.Combine(_dir, "VACATION-calculator-preferences.json");
        const string json = "{\"theme\":\"Dark\",\"Years\":[]}"; // would parse as Dark if wrongly read as preferences
        File.WriteAllText(data, json);
        var stamp = File.GetLastWriteTimeUtc(data);
        using var t = new ThemeService(ThemeService.PreferencesPathFor(data));
        Assert.Equal(ThemeMode.System, t.Mode);
        var vm = new MainViewModel(new DataStore(data), () => new DateOnly(2030, 7, 10), t);
        vm.IsDarkTheme = true;
        vm.IsLightTheme = true;
        vm.IsSystemTheme = true;
        Assert.Equal(json, File.ReadAllText(data));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(data));
        Assert.Single(Directory.GetFileSystemEntries(_dir));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SystemMode_IsDarkFollowsFunction_LightDarkIgnoreIt(bool systemDark)
    {
        using var t = new ThemeService(Prefs, () => systemDark);
        Assert.Equal(systemDark, t.IsDark);
        t.Mode = ThemeMode.Light;
        Assert.False(t.IsDark);
        t.Mode = ThemeMode.Dark;
        Assert.True(t.IsDark);
        t.Mode = ThemeMode.System;
        Assert.Equal(systemDark, t.IsDark);
    }

    [Fact]
    public void SystemMode_IsDarkReevaluatesFunctionEachTime()
    {
        var dark = false;
        using var t = new ThemeService(Prefs, () => dark);
        Assert.False(t.IsDark);
        dark = true;
        Assert.True(t.IsDark);
    }

    // Today = Wed 2030-07-10, selected year 2030 -> holiday year 2029 (1 Sep 2029 .. 31 Aug 2030, deadline 31 Dec 2030).
    // EmployedSince 2030-09-01 is after the holiday year, so it earns 0; 2030-03-01 gives Mar..Aug = 6 months = 12.5;
    // no date means a full year = 25.
    private MainViewModel Vm(decimal earnable)
    {
        var vm = new MainViewModel(new DataStore(Data), () => new DateOnly(2030, 7, 10));
        SetEarnable(vm, earnable);
        return vm;
    }

    private static void SetEarnable(MainViewModel vm, decimal earnable) =>
        vm.EmployedSince = earnable switch
        {
            0 => new DateTime(2030, 9, 1),
            12.5m => new DateTime(2030, 3, 1),
            25 => null,
            _ => throw new ArgumentOutOfRangeException(nameof(earnable)),
        };

    [Theory]
    [InlineData(25, 1)]
    [InlineData(12.5, 1)]
    [InlineData(0, 0)]
    public void UnbookedSign_NoEntries(decimal earnable, int sign)
    {
        Assert.Equal(sign, Vm(earnable).UnbookedSign);
    }

    [Fact]
    public void UnbookedSign_Negative_WhenOverbooked_AndRaisesPropertyChanged()
    {
        // Earnable 0; Mon 2030-07-08 is a working day (see half-day tests), so 1 day booked -> unbooked -1.
        var vm = Vm(0);
        Assert.Equal(0, vm.UnbookedSign);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.NewStart = new DateTime(2030, 7, 8);
        vm.NewEnd = new DateTime(2030, 7, 8);
        vm.AddEntryCommand.Execute(null);
        Assert.Equal(-1, vm.UnbookedSign);
        Assert.Contains("UnbookedSign", raised);
    }

    [Fact]
    public void UnbookedSign_HalfDayOverbooked_IsNegative_AndFractionalPositive()
    {
        var vm = Vm(0);
        vm.NewStart = new DateTime(2030, 7, 8);
        vm.NewEnd = new DateTime(2030, 7, 8);
        vm.IsHalfDay = true;
        vm.AddEntryCommand.Execute(null);
        Assert.Equal(-0.5m, vm.Status.UnbookedDays);
        Assert.Equal(-1, vm.UnbookedSign);

        SetEarnable(vm, 12.5m); // 12.5 - 0.5 = 12
        Assert.Equal(12m, vm.Status.UnbookedDays);
        Assert.Equal(1, vm.UnbookedSign);

        // 12 more working days Mon 07-15 .. Tue 07-30 (5 + 5 + 2) use the rest: 12.5 - 12.5 = 0.
        vm.NewStart = new DateTime(2030, 7, 15);
        vm.NewEnd = new DateTime(2030, 7, 30);
        vm.AddEntryCommand.Execute(null);
        Assert.Equal(0m, vm.Status.UnbookedDays);
        Assert.Equal(0, vm.UnbookedSign);
    }

    [Fact]
    public void UnbookedSign_RaisedWhenEmployedSinceChanges()
    {
        var vm = Vm(12.5m);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        SetEarnable(vm, 0);
        Assert.Contains("UnbookedSign", raised);
        Assert.Equal(0, vm.UnbookedSign);
    }

    [Fact]
    public void DataPathWithInvalidCharacters_EditingDoesNotCrash()
    {
        // Path.GetFullPath accepts '|' and '?' on .NET Core, so --data with such a path passes startup
        // validation; the first edit then must not throw an unhandled exception.
        var bad = Path.Combine(_dir, "a|b?.json");
        var vm = new MainViewModel(new DataStore(bad), () => new DateOnly(2030, 7, 10));
        var ex = Record.Exception(() => vm.TransferAllowed = true);
        Assert.Null(ex);
        Assert.Empty(Directory.GetFiles(_dir));
    }
}
