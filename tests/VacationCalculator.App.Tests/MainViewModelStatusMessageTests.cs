using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>Status text for the "days will be lost" state, with an injected clock.</summary>
public class MainViewModelStatusMessageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vc-vm-" + Guid.NewGuid().ToString("N"));

    // The app formats decimals in the user's culture (comma on a Danish machine); pin invariant so expected text is stable.
    private readonly System.Globalization.CultureInfo _oldCulture = System.Globalization.CultureInfo.CurrentCulture;

    public MainViewModelStatusMessageTests() =>
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

    public void Dispose()
    {
        System.Globalization.CultureInfo.CurrentCulture = _oldCulture;
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private MainViewModel NewVm(DateOnly today) =>
        new(new DataStore(Path.Combine(_dir, "data.json")), () => today);

    [Fact]
    public void CurrentYear_SaysFromTodayThrough31December_WithSpaceAfterSentence()
    {
        // Fri 2026-12-18, allowance 25, nothing booked. Free: 18,21,22,23,24,25,28,29,30,31 = 10; 15 at risk.
        var vm = NewVm(new(2026, 12, 18));

        Assert.Equal(BalanceStatus.DaysWillBeLost, vm.Balance.Status);
        Assert.Equal(
            "You have 25 days not yet booked, but only 10 working days are left from today through 31 December 2026. " +
            "15 days will be lost even if you book every remaining day.",
            vm.StatusMessage);
    }

    [Fact]
    public void CurrentYear_UsesInjectedToday_NotTheRealClock()
    {
        // Wed 2030-12-18 (2030-07-10 is Wed, +161 days = 23 weeks). Same 10 free days as above.
        var vm = NewVm(new(2030, 12, 18));

        Assert.Contains("from today through 31 December 2030. 15 days will be lost", vm.StatusMessage);
    }

    [Fact]
    public void SingleWorkingDayLeft_UsesSingularWording()
    {
        // Thu 2026-12-31 is the only free working day; allowance 25 -> 24 at risk.
        var vm = NewVm(new(2026, 12, 31));

        Assert.Equal(1, vm.Balance.WorkingDaysLeft);
        Assert.Contains("only 1 working day is left from today", vm.StatusMessage);
        Assert.DoesNotContain("1 working days", vm.StatusMessage);
    }

    [Fact]
    public void DaysToPlan_SingleWorkingDayFree_UsesSingularWording()
    {
        // Thu 2026-12-31: allowance 1, one free working day -> DaysToPlan.
        var vm = NewVm(new(2026, 12, 31));
        vm.AllowanceDays = 1;

        Assert.Equal(BalanceStatus.DaysToPlan, vm.Balance.Status);
        Assert.Equal(
            "You have 1 day not yet booked. It must be used by 31 December 2026 (1 working day is still free).",
            vm.StatusMessage);
    }

    private static readonly DateOnly Dec31 = new(2026, 12, 31); // Thu

    private static void Book(MainViewModel vm, DateOnly start, DateOnly end, bool half = false)
    {
        vm.NewStart = start.ToDateTime(TimeOnly.MinValue);
        vm.NewEnd = end.ToDateTime(TimeOnly.MinValue);
        vm.IsHalfDay = half;
        vm.AddEntryCommand.Execute(null);
        Assert.Equal("", vm.ErrorMessage);
    }

    [Fact]
    public void DaysToPlan_HalfDay_UsesPluralWithDecimal()
    {
        // Allowance 0.5, today Thu 2026-12-31 with 1 free working day.
        var vm = NewVm(Dec31);
        vm.AllowanceDays = 0.5m;

        Assert.Equal(BalanceStatus.DaysToPlan, vm.Balance.Status);
        Assert.Equal(
            "You have 0.5 days not yet booked. They must be used by 31 December 2026 (1 working day is still free).",
            vm.StatusMessage);
    }

    [Fact]
    public void DaysToPlan_SeveralDays()
    {
        // Fri 2026-12-18: free 18,21,22,23,24,25,28,29,30,31 = 10 (as in test above); allowance 5 <= 10.
        var vm = NewVm(new(2026, 12, 18));
        vm.AllowanceDays = 5;

        Assert.Equal(BalanceStatus.DaysToPlan, vm.Balance.Status);
        Assert.Equal(
            "You have 5 days not yet booked. They must be used by 31 December 2026 (10 working days are still free).",
            vm.StatusMessage);
    }

    [Fact]
    public void DaysToPlan_FutureYear_SeveralDays()
    {
        // 2027 has 261 weekdays.
        var vm = NewVm(new(2026, 12, 18));
        vm.SelectedYear = 2027;
        vm.AllowanceDays = 20;

        Assert.Equal(
            "You have 20 days not yet booked. They must be used by 31 December 2027 (261 working days are still free).",
            vm.StatusMessage);
    }

    [Fact]
    public void DaysToPlan_FutureYear_SingleRemainingDay()
    {
        var vm = NewVm(new(2026, 12, 18));
        vm.SelectedYear = 2027;
        vm.AllowanceDays = 1;

        Assert.Equal(
            "You have 1 day not yet booked. It must be used by 31 December 2027 (261 working days are still free).",
            vm.StatusMessage);
    }

    [Fact]
    public void AllPlanned_ExactlyOneDay_UsesSingular()
    {
        // Allowance 1, Thu 2026-12-31 booked (today counts as used).
        var vm = NewVm(Dec31);
        vm.AllowanceDays = 1;
        Book(vm, Dec31, Dec31);

        Assert.Equal(BalanceStatus.AllPlanned, vm.Balance.Status);
        Assert.Equal("The 1 day is used or booked. Nothing will expire.", vm.StatusMessage);
    }

    [Fact]
    public void AllPlanned_SeveralDays_UsesAll()
    {
        // Allowance 2: Wed 30 and Thu 31 December booked.
        var vm = NewVm(Dec31);
        vm.AllowanceDays = 2;
        Book(vm, new(2026, 12, 30), Dec31);

        Assert.Equal("All 2 days are used or booked. Nothing will expire.", vm.StatusMessage);
    }

    [Fact]
    public void AllPlanned_HalfDay_UsesAllWithDecimal()
    {
        // Allowance 0.5, half day booked on 31 December.
        var vm = NewVm(Dec31);
        vm.AllowanceDays = 0.5m;
        Book(vm, Dec31, Dec31, half: true);

        Assert.Equal(BalanceStatus.AllPlanned, vm.Balance.Status);
        Assert.Equal("All 0.5 days are used or booked. Nothing will expire.", vm.StatusMessage);
    }

    [Fact]
    public void DaysWillBeLost_ExactlyOneDayLost_UsesSingular()
    {
        // Allowance 2, one free working day (31 Dec) -> 1 day at risk.
        var vm = NewVm(Dec31);
        vm.AllowanceDays = 2;

        Assert.Equal(
            "You have 2 days not yet booked, but only 1 working day is left from today through 31 December 2026. " +
            "1 day will be lost even if you book every remaining day.",
            vm.StatusMessage);
    }

    [Fact]
    public void DaysWillBeLost_HalfDayLost_UsesPlural()
    {
        // Allowance 1.5, one free working day -> 0.5 at risk.
        var vm = NewVm(Dec31);
        vm.AllowanceDays = 1.5m;

        Assert.Equal(
            "You have 1.5 days not yet booked, but only 1 working day is left from today through 31 December 2026. " +
            "0.5 days will be lost even if you book every remaining day.",
            vm.StatusMessage);
    }

    [Fact]
    public void DaysWillBeLost_HalfWorkingDayLeft()
    {
        // Allowance 2, half of 31 Dec booked -> remaining 1.5, free 0.5, at risk 1.
        var vm = NewVm(Dec31);
        vm.AllowanceDays = 2;
        Book(vm, Dec31, Dec31, half: true);

        Assert.Equal(
            "You have 1.5 days not yet booked, but only 0.5 working days are left from today through 31 December 2026. " +
            "1 day will be lost even if you book every remaining day.",
            vm.StatusMessage);
    }

    [Fact]
    public void OverBooked_OneDay_HalfDay_SeveralDays()
    {
        // Wed 30 and Thu 31 December = 2 booked days.
        var vm = NewVm(Dec31);
        vm.AllowanceDays = 1;
        Book(vm, new(2026, 12, 30), Dec31);
        Assert.Equal("You have booked 1 day more than your allowance for 2026.", vm.StatusMessage);

        vm.AllowanceDays = 1.5m;
        Assert.Equal("You have booked 0.5 days more than your allowance for 2026.", vm.StatusMessage);

        vm.AllowanceDays = 0;
        Assert.Equal("You have booked 2 days more than your allowance for 2026.", vm.StatusMessage);
    }

    [Fact]
    public void Expired_ExactlyOneDay_UsesSingular()
    {
        // Today Mon 2027-01-04; 2026 is over, allowance 1 unused.
        var vm = NewVm(new(2027, 1, 4));
        vm.SelectedYear = 2026;
        vm.AllowanceDays = 1;

        Assert.Equal(BalanceStatus.Expired, vm.Balance.Status);
        Assert.Equal("1 day was not used in 2026 and has expired.", vm.StatusMessage);
    }

    [Fact]
    public void Expired_HalfDay_And_SeveralDays_UsePlural()
    {
        var vm = NewVm(new(2027, 1, 4));
        vm.SelectedYear = 2026;
        vm.AllowanceDays = 0.5m;
        Assert.Equal("0.5 days were not used in 2026 and have expired.", vm.StatusMessage);

        vm.AllowanceDays = 25;
        Assert.Equal("25 days were not used in 2026 and have expired.", vm.StatusMessage);
    }

    [Fact]
    public void FutureYear_SaysWholeYear()
    {
        // 2027 has 261 weekdays (starts on Friday, 365 days). Allowance 300 -> 39 at risk.
        var vm = NewVm(new(2026, 12, 18));
        vm.SelectedYear = 2027;
        vm.AllowanceDays = 300;

        Assert.Equal(261, vm.Balance.WorkingDaysLeft);
        Assert.Equal(
            "You have 300 days not yet booked, but only 261 working days are left in 2027 up to and including 31 December 2027. " +
            "39 days will be lost even if you book every remaining day.",
            vm.StatusMessage);
    }
}
