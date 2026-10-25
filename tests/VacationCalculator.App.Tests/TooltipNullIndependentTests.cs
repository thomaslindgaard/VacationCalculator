using VacationCalculator.App.Services;
using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>
/// WPF shows no tooltip only for a null ToolTip, so an empty description / empty Feriefri note must give null, not "".
/// Clock Fri 2026-12-18. Thu 2026-12-24 is Christmas Eve (public holiday), Mon 2026-12-21 is an ordinary working day.
/// </summary>
public class TooltipNullIndependentTests : HolidayYearVmTestBase, IDisposable
{
    private readonly List<ThemeService> _themes = [];

    public new void Dispose()
    {
        foreach (var t in _themes)
            t.Dispose();
        Loc.SetLanguage(AppLanguage.English);
        base.Dispose();
    }

    private static DateOnly Date(int y, int m, int d) => new(y, m, d);

    private MainViewModel Build(params BookedDay[] days) => Build(Dec18, days);

    private MainViewModel Build(DateOnly today, params BookedDay[] days)
    {
        Directory.CreateDirectory(Dir);
        var data = Seed(0);
        foreach (var d in days)
            data.Days.Add(d);
        var store = new DataStore(DataFile);
        store.Save(data);
        var theme = new ThemeService(Path.Combine(Dir, "vacation-calculator-preferences.json"), () => false, () => System.Globalization.CultureInfo.InvariantCulture);
        _themes.Add(theme);
        return new MainViewModel(store, () => today, theme);
    }

    private static void Pick(MainViewModel vm, LanguageMode mode) =>
        vm.SelectedLanguage = vm.LanguageOptions.Single(o => o.Mode == mode);

    private static BookedDay Day(int month, int day, string description) =>
        new() { Date = new DateOnly(2026, month, day), Type = DayType.Vacation, Amount = 1, Description = description };

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   \t ")]
    public void CountedBookingWithNoOrBlankDescription_HasNullFullDescription(string description)
    {
        var vm = Build(Day(12, 21, description));
        var row = vm.Entries.Single();
        Assert.False(row.IsNotCounted);
        Assert.Null(row.FullDescription);
    }

    [Fact]
    public void CountedBookingWithText_FullDescriptionIsTheText()
    {
        var vm = Build(Day(12, 21, "Skiing trip"));
        Assert.Equal("Skiing trip", vm.Entries.Single().FullDescription);
    }

    [Fact]
    public void NotCountedBookingWithoutDescription_FullDescriptionIsTheNotCountedText_InSeveralLanguages()
    {
        var vm = Build(Day(12, 24, ""));
        var row = vm.Entries.Single();
        Assert.True(row.IsNotCounted);
        Assert.Equal("public holiday – not counted", row.FullDescription);

        Pick(vm, LanguageMode.Danish);
        Assert.Equal("helligdag – tæller ikke", vm.Entries.Single().FullDescription);

        Pick(vm, LanguageMode.Romanian);
        Assert.Equal("sărbătoare – nu se numără", vm.Entries.Single().FullDescription);
    }

    [Fact]
    public void NotCountedBookingWithDescription_FullDescriptionHasTextAndSuffix()
    {
        var vm = Build(Day(12, 24, "oops"));
        Assert.Equal("oops  (public holiday – not counted)", vm.Entries.Single().FullDescription);
        Pick(vm, LanguageMode.Danish);
        Assert.Equal("oops  (helligdag – tæller ikke)", vm.Entries.Single().FullDescription);
    }

    [Fact]
    public void FullDescription_FollowsALiveLanguageSwitch_BackAndForth()
    {
        var vm = Build(Day(12, 24, ""));
        Pick(vm, LanguageMode.Danish);
        var da = vm.Entries.Single().FullDescription;
        Pick(vm, LanguageMode.Romanian);
        var ro = vm.Entries.Single().FullDescription;
        Pick(vm, LanguageMode.English);
        var en = vm.Entries.Single().FullDescription;
        Assert.Equal("helligdag – tæller ikke", da);
        Assert.Equal("sărbătoare – nu se numără", ro);
        Assert.Equal("public holiday – not counted", en);
    }

    [Fact]
    public void FeriefriNoteTooltip_IsNullWithoutNote_AndEqualsTheNoteWithOne()
    {
        // On Mon 2026-03-02 no holiday year with Feriefri has started its payout notes (notes only appear once a year has begun
        // and Feriefri is over the allowance), and none is booked, so the rows have no note.
        var vm = Build(Date(2026, 3, 2));
        Assert.NotEmpty(vm.FeriefriRows);
        Assert.Contains(vm.FeriefriRows, r => r.Note.Length == 0);
        foreach (var r in vm.FeriefriRows.Where(r => r.Note.Length == 0))
            Assert.Null(r.NoteTooltip);
    }

    [Fact]
    public void FeriefriNoteTooltip_EqualsTheYearEndPayoutNote()
    {
        // One Feriefri day booked ahead (Mon 2026-12-21) in 2025/26: 4 of 5 remain, and 4 are paid out in January 2027.
        var vm = Build(new BookedDay { Date = new DateOnly(2026, 12, 21), Type = DayType.Feriefri, Amount = 1 });
        var row = vm.FeriefriRows.Single(r => r.Name == "2025/26");
        Assert.NotEqual("", row.Note);
        Assert.Equal(row.Note, row.NoteTooltip);
        Assert.Contains("4", row.Note);

        Pick(vm, LanguageMode.Danish);
        var da = vm.FeriefriRows.Single(r => r.Name == "2025/26");
        Assert.Equal("4 udbetales i januar 2027", da.Note);
        Assert.Equal(da.Note, da.NoteTooltip);
    }
}
