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
    public void FeriefriRemainingTooltip_IsNullWithoutNote_NeverEmpty()
    {
        // On Mon 2026-03-02 holiday year 2026/27 has not started, nothing is booked and no allowance is exceeded,
        // so its Feriefri cell has no note: the tooltip must be null (not "").
        var vm = Build(Date(2026, 3, 2));
        Assert.NotEmpty(vm.SummaryRows);
        Assert.Null(vm.SummaryRows.Single(r => r.Name == "2026/27").FeriefriRemainingTooltip);
        Assert.All(vm.SummaryRows, r => Assert.NotEqual("", r.FeriefriRemainingTooltip));
    }

    [Fact]
    public void FeriefriRemainingTooltip_HoldsTheYearEndPayoutNote()
    {
        // One Feriefri day booked ahead (Mon 2026-12-21) in 2025/26: 4 of 5 remain, and 4 are paid out in January 2027.
        var vm = Build(new BookedDay { Date = new DateOnly(2026, 12, 21), Type = DayType.Feriefri, Amount = 1 });
        var row = vm.SummaryRows.Single(r => r.Name == "2025/26");
        Assert.Equal("4 will be paid out in January 2027", row.FeriefriRemainingTooltip);

        Pick(vm, LanguageMode.Danish);
        var da = vm.SummaryRows.Single(r => r.Name == "2025/26");
        Assert.Equal("4 udbetales i januar 2027", da.FeriefriRemainingTooltip);
    }

    [Fact]
    public void RemainingTooltip_IsNullWhenNothingToSay_AndHasTheNoteOtherwise()
    {
        // Nothing booked on Fri 2026-12-18: no advance holiday, so no note anywhere.
        var vm = Build();
        Assert.All(vm.SummaryRows, r => Assert.Null(r.RemainingTooltip));

        // 25 days Mon 2026-01-05 .. Fri 2026-02-06 fill 2025/26. Then Mon-Wed 12-21..23 and 12-28..30 (24-26 and 31 Dec are
        // holidays) plus Mon 2027-01-04 = 7 working days go to 2026/27, which has earned 3 months * 25/12 = 6.25 so far: -0.75.
        var days = new List<BookedDay>();
        for (var d = new DateOnly(2026, 1, 5); d <= new DateOnly(2026, 2, 6); d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                days.Add(new BookedDay { Date = d });
        foreach (var day in new[] { 21, 22, 23, 28, 29, 30 })
            days.Add(new BookedDay { Date = new DateOnly(2026, 12, day) });
        days.Add(new BookedDay { Date = new DateOnly(2027, 1, 4) });
        var over = Build(days.ToArray());

        Assert.Null(over.SummaryRows[0].RemainingTooltip);
        Assert.Equal(-0.75m, over.SummaryRows[1].Remaining);
        Assert.Equal("advance holiday: 0.75 not earned yet", over.SummaryRows[1].RemainingTooltip);
    }
    [Fact]
    public void TransferredInTooltip_IsNullWithoutProjection()
    {
        // Seed(0) has nothing booked and no opening balance: no transfer, so no projected-transfer tooltip anywhere.
        var vm = Build();
        Assert.All(vm.SummaryRows, r => Assert.Null(r.TransferredInTooltip));
    }
}