using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>
/// View-model tests for half days. The clock is injected: "today" is fixed at Wed 2030-07-10.
/// Calendar check: 2030-01-01 is a Tuesday; 2030-07-08 is day 189 of the year, 188 days later = 6 mod 7,
/// so Monday. Hence Sat 07-06, Mon 07-08, Wed 07-10, Thu 07-11.
/// </summary>
public class MainViewModelHalfDayTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vc-vm-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static readonly DateOnly FixedToday = new(2030, 7, 10);

    private MainViewModel NewVm(DateOnly? today = null)
    {
        var date = today ?? FixedToday;
        return new MainViewModel(new DataStore(Path.Combine(_dir, "data.json")), () => date);
    }

    private static readonly DateTime Sat = new(2030, 7, 6);
    private static readonly DateTime Mon = new(2030, 7, 8);
    private static readonly DateTime Wed = new(2030, 7, 10);
    private static readonly DateTime Thu = new(2030, 7, 11);

    [Fact]
    public void HalfDayCheckbox_IsDisabledUntilFromEqualsTo()
    {
        var vm = NewVm();
        Assert.False(vm.CanBookHalfDay);

        vm.NewStart = Mon; // end defaults to start
        Assert.True(vm.CanBookHalfDay);

        vm.NewEnd = Mon.AddDays(1);
        Assert.False(vm.CanBookHalfDay);

        vm.NewEnd = Mon;
        Assert.True(vm.CanBookHalfDay);
    }

    [Fact]
    public void WidenRange_ResetsHalfDayCheckbox()
    {
        var vm = NewVm();
        vm.NewStart = Mon;
        vm.IsHalfDay = true;

        vm.NewEnd = Mon.AddDays(2);

        Assert.False(vm.IsHalfDay);
    }

    [Fact]
    public void AddHalfDay_AfterToday_IsPlanned()
    {
        var vm = NewVm();
        vm.NewStart = Thu; // after today (Wed)
        vm.IsHalfDay = true;

        vm.AddEntryCommand.Execute(null);

        var row = Assert.Single(vm.Entries);
        Assert.Equal(0.5m, row.Period.Amount);
        Assert.False(row.IsPast);
        Assert.Equal(0.5m, vm.Balance.PlannedDays);
        Assert.Equal(0m, vm.Balance.UsedDays);
        Assert.Equal(24.5m, vm.Balance.RemainingDays);
        Assert.False(vm.IsHalfDay);
        Assert.Null(vm.NewStart);
    }

    [Fact]
    public void AddHalfDay_BeforeToday_IsUsedAndPast()
    {
        var vm = NewVm();
        vm.NewStart = Mon;
        vm.IsHalfDay = true;

        vm.AddEntryCommand.Execute(null);

        var row = Assert.Single(vm.Entries);
        Assert.True(row.IsPast);
        Assert.Equal(0.5m, vm.Balance.UsedDays);
        Assert.Equal(0m, vm.Balance.PlannedDays);
    }

    [Fact]
    public void AddHalfDay_OnToday_CountsAsUsed()
    {
        var vm = NewVm();
        vm.NewStart = Wed;
        vm.IsHalfDay = true;

        vm.AddEntryCommand.Execute(null);

        var row = Assert.Single(vm.Entries);
        Assert.Equal(new DateOnly(2030, 7, 10), row.Period.End);
        Assert.True(row.IsPast);
        Assert.Equal(0.5m, vm.Balance.UsedDays);
        Assert.Equal(0m, vm.Balance.PlannedDays);
    }

    [Fact]
    public void Period_SpanningToday_IsNotPast_AndSplitsUsedAndPlanned()
    {
        var vm = NewVm();
        vm.NewStart = Mon;  // Mon 07-08 .. Fri 07-12: Mon, Tue, Wed used (3), Thu, Fri planned (2)
        vm.NewEnd = Mon.AddDays(4);

        vm.AddEntryCommand.Execute(null);

        var row = Assert.Single(vm.Entries);
        Assert.False(row.IsPast); // ends after today
        Assert.Equal(3m, vm.Balance.UsedDays);
        Assert.Equal(2m, vm.Balance.PlannedDays);
    }

    // IsPast cases. Today = Wed 2030-07-10; yesterday Tue 07-09; tomorrow Thu 07-11.
    [Fact]
    public void IsPast_PeriodEndingYesterday_IsTrue()
    {
        var vm = NewVm();
        vm.NewStart = Mon;
        vm.NewEnd = Mon.AddDays(1); // Mon..Tue = 2 days, all before today
        vm.AddEntryCommand.Execute(null);

        Assert.True(Assert.Single(vm.Entries).IsPast);
        Assert.Equal(2m, vm.Balance.UsedDays);
    }

    [Fact]
    public void IsPast_FullDayBookedToday_IsTrue_AndCountedUsed()
    {
        var vm = NewVm();
        vm.NewStart = Wed;
        vm.AddEntryCommand.Execute(null);

        Assert.True(Assert.Single(vm.Entries).IsPast);
        Assert.Equal(1m, vm.Balance.UsedDays);
        Assert.Equal(0m, vm.Balance.PlannedDays);
    }

    [Fact]
    public void IsPast_PeriodEndingToday_StartedEarlier_IsTrue()
    {
        var vm = NewVm();
        vm.NewStart = Mon;
        vm.NewEnd = Wed; // Mon, Tue, Wed = 3 days, all used
        vm.AddEntryCommand.Execute(null);

        Assert.True(Assert.Single(vm.Entries).IsPast);
        Assert.Equal(3m, vm.Balance.UsedDays);
        Assert.Equal(0m, vm.Balance.PlannedDays);
    }

    [Fact]
    public void IsPast_PeriodEndingTomorrow_StartedBeforeToday_IsFalse()
    {
        var vm = NewVm();
        vm.NewStart = Mon;
        vm.NewEnd = Thu; // Mon..Thu: 3 used, 1 planned
        vm.AddEntryCommand.Execute(null);

        Assert.False(Assert.Single(vm.Entries).IsPast);
        Assert.Equal(3m, vm.Balance.UsedDays);
        Assert.Equal(1m, vm.Balance.PlannedDays);
    }

    [Fact]
    public void IsPast_FuturePeriod_IsFalse()
    {
        var vm = NewVm();
        vm.NewStart = Thu;
        vm.NewEnd = Thu.AddDays(1); // Thu, Fri
        vm.AddEntryCommand.Execute(null);

        Assert.False(Assert.Single(vm.Entries).IsPast);
        Assert.Equal(0m, vm.Balance.UsedDays);
        Assert.Equal(2m, vm.Balance.PlannedDays);
    }

    [Fact]
    public void AddRange_WithHalfDayUnchecked_BooksFullWorkingDays()
    {
        var vm = NewVm();
        vm.NewStart = Thu;            // Thu 2030-07-11
        vm.NewEnd = Thu.AddDays(6);   // Wed 07-17 -> Thu, Fri, Mon, Tue, Wed = 5 working days
        vm.AddEntryCommand.Execute(null);

        Assert.Equal(5m, Assert.Single(vm.Entries).Period.Amount);
        Assert.Equal(5m, vm.Balance.PlannedDays);
    }

    [Fact]
    public void HalfDayOnWeekend_ShowsErrorAndBooksNothing()
    {
        var vm = NewVm();
        vm.NewStart = Sat;
        vm.IsHalfDay = true;

        vm.AddEntryCommand.Execute(null);

        Assert.Empty(vm.Entries);
        Assert.NotEqual("", vm.ErrorMessage);
        Assert.False(File.Exists(Path.Combine(_dir, "data.json")));
    }

    [Fact]
    public void Delete_RemovesWholePeriod()
    {
        var vm = NewVm();
        vm.NewStart = Thu;
        vm.NewEnd = Thu.AddDays(4); // Thu..Mon: 07-11 Thu, 07-12 Fri, (weekend), 07-15 Mon -> 3 days
        vm.AddEntryCommand.Execute(null);
        var row = Assert.Single(vm.Entries);

        vm.DeleteEntryCommand.Execute(row);

        Assert.Empty(vm.Entries);
        Assert.Equal(0m, vm.Balance.PlannedDays);
    }

    [Fact]
    public void HalfDayPersistsAcrossReload()
    {
        var vm = NewVm();
        vm.NewStart = Thu;
        vm.IsHalfDay = true;
        vm.AddEntryCommand.Execute(null);

        var reloaded = NewVm();

        Assert.Equal(0.5m, Assert.Single(reloaded.Entries).Period.Amount);
    }

    [Fact]
    public void InitialSelectedYear_FollowsInjectedToday()
    {
        Assert.Equal(2030, NewVm().SelectedYear);
        Assert.Equal(2031, NewVm(new DateOnly(2031, 3, 5)).SelectedYear);
    }

    [Fact]
    public void CurrentYearCommand_GoesToInjectedTodaysYear()
    {
        var vm = NewVm(new DateOnly(2031, 12, 31));
        vm.SelectedYear = 2025;

        vm.CurrentYearCommand.Execute(null);

        Assert.Equal(2031, vm.SelectedYear);
    }

    [Fact]
    public void Preview_HalfDayOnWorkingDay_ShowsHalf()
    {
        var vm = NewVm();
        vm.NewStart = Mon;
        vm.IsHalfDay = true;

        Assert.Equal("0.5 working day(s)", vm.NewEntryPreview);
    }

    [Fact]
    public void Preview_HalfDayOnSaturday_ShowsZero()
    {
        var vm = NewVm();
        vm.NewStart = Sat;
        vm.IsHalfDay = true;

        Assert.Equal("0 working day(s)", vm.NewEntryPreview);
    }

    [Fact]
    public void Preview_HalfDayOnPublicHoliday_ShowsZero()
    {
        var vm = NewVm();
        vm.NewHoliday = Thu;
        vm.AddHolidayCommand.Execute(null);
        vm.NewStart = Thu;
        vm.IsHalfDay = true;

        Assert.Equal("0 working day(s)", vm.NewEntryPreview);
    }

    [Fact]
    public void Preview_FullDayRange_CountsWorkingDays()
    {
        var vm = NewVm();
        vm.NewStart = Mon;
        vm.NewEnd = Mon.AddDays(6); // Mon..Sun

        Assert.Equal("5 working day(s)", vm.NewEntryPreview);
    }

    [Fact]
    public void Preview_TogglingHalfDayOffOnWorkingDay_ShowsOne()
    {
        var vm = NewVm();
        vm.NewStart = Mon;
        vm.IsHalfDay = true;
        vm.IsHalfDay = false;

        Assert.Equal("1 working day(s)", vm.NewEntryPreview);
    }
}
