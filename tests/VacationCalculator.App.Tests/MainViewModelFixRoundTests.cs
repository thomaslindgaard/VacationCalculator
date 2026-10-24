using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>Re-test after the wpf-ui fix round: banner wording matrix, transfer clamp, "no vacation days" text.</summary>
public class MainViewModelFixRoundTests : HolidayYearVmTestBase
{
    private const string TransferSentence =
        "The unused 5th-week days (5 days) can be transferred by agreement before 31 December, otherwise they are paid out.";

    private VacationData Saved() => new DataStore(DataFile).Load();

    // Today Fri 2026-12-18: 10 free working days (Dec 18, 21-25, 28-31). Holiday year 2025/26 earns 25.

    [Fact]
    public void DaysToPlan_SettingOn_NothingTransferred_OnlyTransferSentence()
    {
        // 20 used -> 5 unbooked <= 10 free. All 5 are 5th-week days; nothing lost. Paid-out fact is in the sentence only.
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));

        Assert.Equal(BalanceStatus.DaysToPlan, vm.Status.Status);
        Assert.Equal(new[] { TransferSentence }, Lines(vm.StatusDetail));
    }

    [Fact]
    public void DaysToPlan_SettingOn_NothingTransferred_WithLostDays_LostLineThenTransferSentence()
    {
        // 15 used -> 10 unbooked == 10 free: DaysToPlan. 5 are 5th-week days (paid-out fact is in the transfer
        // sentence), the other 5 would be lost.
        var vm = NewVm(Dec18, Seed(15, transferAllowed: true));

        Assert.Equal(BalanceStatus.DaysToPlan, vm.Status.Status);
        Assert.Equal(new[] { "If they are not booked, 5 days will be lost.", TransferSentence }, Lines(vm.StatusDetail));
    }

    [Fact]
    public void DaysToPlan_SettingOn_PartialTransfer_PaidOutReduced_NoFifthWeekSentence()
    {
        // 20 used, 3 transferred -> 2 unbooked, 2 fifth-week left -> paid out 2.
        var vm = NewVm(Dec18, Seed(20, true, 3));

        Assert.Equal(new[] { "If they are not booked, 2 days will be paid out." }, Lines(vm.StatusDetail));
    }

    [Fact]
    public void DaysToPlan_SettingOn_FullTransfer_BecomesAllPlanned_NoDetail()
    {
        var vm = NewVm(Dec18, Seed(20, true, 5));

        Assert.Equal(BalanceStatus.AllPlanned, vm.Status.Status);
        Assert.Equal("Everything is used, booked or transferred. Nothing will expire.", vm.StatusMessage);
        Assert.False(vm.HasStatusDetail);
    }

    [Fact]
    public void DaysWillBeLost_SettingOff_ListsLostAndPaidOut()
    {
        // 5 used -> 20 unbooked, 10 free -> 10 cannot fit; 5th-week first: 5 paid out, 5 lost.
        var vm = NewVm(Dec18, Seed(5));

        Assert.Equal(BalanceStatus.DaysWillBeLost, vm.Status.Status);
        var d = vm.StatusDetail;
        Assert.Contains("10 days can't fit even if you book every free working day", d);
        Assert.Contains("5 days will be lost", d);
        Assert.Contains("5 days will be paid out", d);
        Assert.DoesNotContain("5th-week", d);
    }

    [Fact]
    public void DaysWillBeLost_SettingOn_NothingTransferred_ShortLostLine_ThenTransferSentence()
    {
        var vm = NewVm(Dec18, Seed(5, transferAllowed: true));

        Assert.Equal(BalanceStatus.DaysWillBeLost, vm.Status.Status);
        Assert.Equal(
            new[] { "5 days will be lost even if you book every free working day.", TransferSentence },
            Lines(vm.StatusDetail));
    }

    [Fact]
    public void DaysWillBeLost_SettingOn_NothingLost_OnlyTransferSentence()
    {
        // 10 used -> 15 unbooked, 10 free -> 5 cannot fit; they are the 5 fifth-week days -> paid out, 0 lost.
        var vm = NewVm(Dec18, Seed(10, transferAllowed: true));

        Assert.Equal(BalanceStatus.DaysWillBeLost, vm.Status.Status);
        Assert.Equal(new[] { TransferSentence }, Lines(vm.StatusDetail));
    }

    [Fact]
    public void DaysWillBeLost_SettingOn_PartialTransfer_NoTransferSentence_PaidOutReduced()
    {
        // 5 used, 3 transferred -> 17 unbooked, 10 free, 7 cannot fit. Fifth-week at risk = min(7+3, 5) = 5, of which
        // 3 are transferred -> 2 paid out; lost = 7 - 2 = 5.
        var vm = NewVm(Dec18, Seed(5, true, 3));

        Assert.Equal(BalanceStatus.DaysWillBeLost, vm.Status.Status);
        Assert.DoesNotContain("5th-week", vm.StatusDetail);
        Assert.Contains("5 days will be lost", vm.StatusDetail);
        Assert.Contains("2 days will be paid out", vm.StatusDetail);
    }

    [Fact]
    public void Expired_SettingOn_PartialTransfer_StatesOutcome_NoDetail()
    {
        // Mon 2027-01-04 viewing 2026: 20 used, 3 transferred -> 2 unused: paid out.
        var vm = NewVm(new DateOnly(2027, 1, 4), Seed(20, true, 3));
        vm.SelectedYear = 2026;

        Assert.Equal(BalanceStatus.Expired, vm.Status.Status);
        Assert.Equal("Holiday year 2025/26 expired on 31 December 2026: 2 days were paid out.", vm.StatusMessage);
        Assert.False(vm.HasStatusDetail);
    }

    [Fact]
    public void Expired_SettingOn_NothingTransferred_NoTransferSentence()
    {
        var vm = NewVm(new DateOnly(2027, 1, 4), Seed(5, transferAllowed: true));
        vm.SelectedYear = 2026;

        Assert.Equal(BalanceStatus.Expired, vm.Status.Status);
        Assert.Equal(
            "Holiday year 2025/26 expired on 31 December 2026: 5 days were paid out and 15 days were lost.",
            vm.StatusMessage);
        Assert.False(vm.HasStatusDetail);
    }

    // ---- Transfer clamp ----

    [Fact]
    public void Transfer_OverCap_StoresCap_AndSnapsBack()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.TransferredToNext = 10;

        Assert.Equal(5m, vm.TransferredToNext);
        Assert.Equal(5m, Saved().FindHolidayYear(2025)!.TransferredToNext);
        Assert.Contains("TransferredToNext", raised);
    }

    [Fact]
    public void Transfer_OverCap_WhenAlreadyAtCap_StaysAndNotifiesSoBoxSnapsBack()
    {
        var vm = NewVm(Dec18, Seed(20, true, 5));
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.TransferredToNext = 10;

        Assert.Equal(5m, vm.TransferredToNext);
        Assert.Contains("TransferredToNext", raised);
        Assert.Equal(5m, Saved().FindHolidayYear(2025)!.TransferredToNext);
    }

    [Fact]
    public void Transfer_Negative_StoresZero()
    {
        var vm = NewVm(Dec18, Seed(20, true, 3));

        vm.TransferredToNext = -2;

        Assert.Equal(0m, vm.TransferredToNext);
        Assert.Equal(0m, Saved().FindHolidayYear(2025)?.TransferredToNext ?? 0m);
    }

    [Fact]
    public void Transfer_Decimal_IsKept_AndPersistsAfterReload()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));

        vm.TransferredToNext = 2.5m;

        Assert.Equal(2.5m, vm.TransferredToNext);
        var reloaded = NewVm(Dec18);
        Assert.Equal(2.5m, reloaded.TransferredToNext);
        // 20 used + 2.5 transferred -> 2.5 unbooked.
        Assert.Contains("You have 2.5 days not yet booked", reloaded.StatusMessage);
    }

    [Fact]
    public void Transfer_ExactCap_IsAccepted()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));

        vm.TransferredToNext = 5;

        Assert.Equal(5m, vm.TransferredToNext);
        Assert.Equal(BalanceStatus.AllPlanned, vm.Status.Status);
    }

    [Fact]
    public void Transfer_SameValueAgain_ChangesNothing()
    {
        var vm = NewVm(Dec18, Seed(20, true, 3));

        vm.TransferredToNext = 3;

        Assert.Equal(3m, vm.TransferredToNext);
        Assert.Equal(3m, Saved().FindHolidayYear(2025)!.TransferredToNext);
        Assert.Single(Saved().HolidayYears);
    }

    [Fact]
    public void Transfer_SettingOff_CapIsZero_StoresNothing()
    {
        var vm = NewVm(Dec18, Seed(20));

        vm.TransferredToNext = 3;

        Assert.Equal(0m, vm.TransferredToNext);
        Assert.Equal(0m, Saved().FindHolidayYear(2025)?.TransferredToNext ?? 0m);
    }

    [Fact]
    public void Transfer_WhenViewing2027_UsesHolidayYear2026()
    {
        // Today 2027-03-01 viewing 2027: holiday year 2026/27. Seed gives bookings in Jan/Feb 2026 only (holiday year 2025/26),
        // so 2026/27 has 25 unused; cap 5.
        var vm = NewVm(new DateOnly(2027, 3, 1), Seed(0, transferAllowed: true));
        Assert.Equal(2027, vm.SelectedYear);

        vm.TransferredToNext = 4;

        Assert.Equal(4m, vm.TransferredToNext);
        Assert.Equal(4m, Saved().FindHolidayYear(2026)!.TransferredToNext);
        Assert.Null(Saved().FindHolidayYear(2025));
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
    public void NormalAllPlanned_StillSaysEverythingIsUsed()
    {
        var vm = NewVm(Dec18, Seed(25));

        Assert.Equal(BalanceStatus.AllPlanned, vm.Status.Status);
        Assert.Equal("Everything is used, booked or transferred. Nothing will expire.", vm.StatusMessage);
    }
}

