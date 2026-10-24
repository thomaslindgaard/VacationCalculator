using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>
/// Banner matrix after feature 2b (calculated transfer and payout; the registered-transfer input and its clamp are gone)
/// and the "no vacation days" text.
/// </summary>
public class MainViewModelFixRoundTests : HolidayYearVmTestBase
{
    // Today Fri 2026-12-18: 10 free working days (Dec 18, 21-25, 28-31). Holiday year 2025/26 earns 25.

    [Fact]
    public void DaysWillBeLost_SettingOff_ListsLostAndPaidOut()
    {
        // 5 used -> 20 unbooked, 10 free -> 10 cannot fit. Booking 10 more leaves 10 unused: 5 first-20 days (lost)
        // and the 5 5th-week days (always paid out).
        var vm = NewVm(Dec18, Seed(5));

        Assert.Equal(BalanceStatus.DaysWillBeLost, vm.Status.Status);
        var d = vm.StatusDetail;
        Assert.Contains("10 days can't fit even if you book every free working day", d);
        Assert.Contains("5 days will be lost", d);
        Assert.Contains("5 days will be paid out", d);
        Assert.DoesNotContain("5th-week", d);
        Assert.DoesNotContain("transferred", d);
    }

    [Fact]
    public void Expired_SettingOn_NothingBooked_ThenAllFiveTransferred()
    {
        // 2b: 20 used, transfer on, 5 unused: all 5 transferred (nothing is registered any more).
        var vm = NewVm(new DateOnly(2027, 1, 4), Seed(20, transferAllowed: true));
        vm.SelectedYear = 2026;

        Assert.Equal(BalanceStatus.AllPlanned, vm.Status.Status);
        Assert.Equal("Nothing was paid out or lost.", vm.StatusMessage);
    }

    [Fact]
    public void Expired_SettingOn_StatesTransferredAndLost()
    {
        // 5 used, 20 unused: 5 (5th week) transferred, 15 lost (payout none).
        var vm = NewVm(new DateOnly(2027, 1, 4), Seed(5, transferAllowed: true));
        vm.SelectedYear = 2026;

        Assert.Equal(BalanceStatus.Expired, vm.Status.Status);
        Assert.Equal(
            "Holiday year 2025/26 expired on 31 December 2026: 5 days were transferred to 2026/27 and 15 days were lost.",
            vm.StatusMessage);
        Assert.False(vm.HasStatusDetail);
    }

    // ---- "No vacation days" ----

    [Fact]
    public void NoVacationDays_EmployedSinceOctober_ViewingSameYear()
    {
        // Employed 2026-10-01: holiday year 2025/26 (ends 31 Aug 2026) earns 0 and nothing is transferred in.
        var vm = NewVm(new DateOnly(2026, 10, 24), Seed(0, employedSince: new DateOnly(2026, 10, 1)));

        Assert.Equal(2026, vm.SelectedYear);
        Assert.Equal(BalanceStatus.AllPlanned, vm.Status.Status);
        Assert.Equal("No vacation days to use in holiday year 2025/26.", vm.StatusMessage);
    }

    [Fact]
    public void NormalAllPlanned_SaysNothingWillBePaidOutOrLost()
    {
        var vm = NewVm(Dec18, Seed(25));

        Assert.Equal(BalanceStatus.AllPlanned, vm.Status.Status);
        Assert.Equal("Nothing will be paid out or lost.", vm.StatusMessage);
    }
}
