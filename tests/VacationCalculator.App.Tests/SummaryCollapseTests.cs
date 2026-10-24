using System.Text.Json;
using VacationCalculator.App.Services;
using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>Feature 5, collapsible summary: view-model state and its persistence in the preferences file.</summary>
public class SummaryCollapseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vc-summary-" + Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Today = new(2026, 12, 18);

    public SummaryCollapseTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private string Prefs => Path.Combine(_dir, "vacation-calculator-preferences.json");
    private string Data => Path.Combine(_dir, "vacation-data.json");

    private MainViewModel Vm(ThemeService? theme) => new(new DataStore(Data), () => Today, theme);

    [Fact]
    public void DefaultsToExpanded_WithoutTheme()
    {
        var vm = Vm(null);
        Assert.True(vm.IsSummaryExpanded);
        Assert.Equal("Hide summary", vm.SummaryToggleText);
    }

    [Fact]
    public void Toggle_FlipsStateTextAndRaisesBothNotifications_InMemory()
    {
        var vm = Vm(null);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ToggleSummaryCommand.Execute(null);
        Assert.False(vm.IsSummaryExpanded);
        Assert.Equal("Show summary", vm.SummaryToggleText);
        Assert.Contains(nameof(MainViewModel.IsSummaryExpanded), raised);
        Assert.Contains(nameof(MainViewModel.SummaryToggleText), raised);

        vm.ToggleSummaryCommand.Execute(null);
        Assert.True(vm.IsSummaryExpanded);
        Assert.Equal("Hide summary", vm.SummaryToggleText);
    }

    [Fact]
    public void SettingSameValue_RaisesNothing()
    {
        var vm = Vm(null);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.IsSummaryExpanded = true;
        Assert.Empty(raised);
    }

    [Fact]
    public void Collapsed_IsRemembered_WithTheTheme_AcrossRestart()
    {
        using (var theme = new ThemeService(Prefs))
        {
            var vm = Vm(theme);
            theme.Mode = ThemeMode.Dark;
            vm.ToggleSummaryCommand.Execute(null);
            Assert.False(vm.IsSummaryExpanded);
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(Prefs));
        Assert.Equal("Dark", doc.RootElement.GetProperty("theme").GetString());
        Assert.False(doc.RootElement.GetProperty("summaryExpanded").GetBoolean());

        using var theme2 = new ThemeService(Prefs);
        var vm2 = Vm(theme2);
        Assert.False(vm2.IsSummaryExpanded);
        Assert.Equal("Show summary", vm2.SummaryToggleText);
        Assert.Equal(ThemeMode.Dark, theme2.Mode);

        vm2.ToggleSummaryCommand.Execute(null);
        using var theme3 = new ThemeService(Prefs);
        Assert.True(theme3.SummaryExpanded);
        Assert.Equal(ThemeMode.Dark, theme3.Mode);
    }

    [Fact]
    public void ChangingTheme_KeepsCollapsedState()
    {
        using var theme = new ThemeService(Prefs);
        var vm = Vm(theme);
        vm.IsSummaryExpanded = false;
        theme.Mode = ThemeMode.Light;
        using var again = new ThemeService(Prefs);
        Assert.False(again.SummaryExpanded);
        Assert.Equal(ThemeMode.Light, again.Mode);
    }

    [Fact]
    public void OldPreferencesFile_WithoutField_IsExpanded_AndKeepsTheme()
    {
        File.WriteAllText(Prefs, "{ \"theme\": \"Light\" }");
        using var theme = new ThemeService(Prefs);
        Assert.True(theme.SummaryExpanded);
        Assert.Equal(ThemeMode.Light, theme.Mode);
        Assert.True(Vm(theme).IsSummaryExpanded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"summaryExpanded\":\"false\"}")]
    [InlineData("{\"summaryExpanded\":null}")]
    [InlineData("{\"summaryExpanded\":0}")]
    public void CorruptOrOddField_IsExpanded(string content)
    {
        File.WriteAllText(Prefs, content);
        using var theme = new ThemeService(Prefs);
        Assert.True(theme.SummaryExpanded);
    }

    [Fact]
    public void FalseField_IsRespected_EvenWithoutTheme()
    {
        File.WriteAllText(Prefs, "{\"summaryExpanded\":false}");
        using var theme = new ThemeService(Prefs);
        Assert.False(theme.SummaryExpanded);
        Assert.Equal(ThemeMode.System, theme.Mode);
    }

    [Fact]
    public void MissingFile_IsExpanded_AndNoFileWrittenByReadingOnly()
    {
        using var theme = new ThemeService(Prefs);
        Assert.True(theme.SummaryExpanded);
        Assert.False(File.Exists(Prefs));
    }

    [Fact]
    public void PreferencesPathThatIsADirectory_DoesNotThrow_AndStateStillChanges()
    {
        Directory.CreateDirectory(Prefs);
        using var theme = new ThemeService(Prefs);
        var vm = Vm(theme);
        vm.ToggleSummaryCommand.Execute(null);
        Assert.False(vm.IsSummaryExpanded);
    }

    [Fact]
    public void Toggling_NeverTouchesTheVacationData()
    {
        var store = new DataStore(Data);
        var data = new VacationData();
        data.Days.Add(new BookedDay { Date = new DateOnly(2026, 12, 21), Description = "keep" });
        store.Save(data);
        var before = File.ReadAllBytes(Data);
        var stamp = File.GetLastWriteTimeUtc(Data);

        using var theme = new ThemeService(Prefs);
        var vm = new MainViewModel(store, () => Today, theme);
        var afterLoad = File.ReadAllBytes(Data);
        vm.ToggleSummaryCommand.Execute(null);
        vm.ToggleSummaryCommand.Execute(null);
        vm.ToggleSummaryCommand.Execute(null);

        Assert.Equal(afterLoad, File.ReadAllBytes(Data));
        Assert.Single(store.Load().Days);
        Assert.Equal("keep", store.Load().Days[0].Description);
        _ = before; _ = stamp;
    }

    [Fact]
    public void PreferencesPathFor_DataFileInSameFolder_GivesSeparateFile()
    {
        var p = ThemeService.PreferencesPathFor(Data);
        Assert.Equal(Prefs, p);
    }
}
