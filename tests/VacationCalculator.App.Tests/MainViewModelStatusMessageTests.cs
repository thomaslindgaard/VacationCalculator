using System.Globalization;
using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>
/// Shared setup for view-model tests of the holiday-year rules: a temp data file, a fixed clock, invariant culture.
/// Calendar facts used throughout: Mon 2026-01-05, Fri 2026-12-18, Thu 2026-12-31, Mon 2027-01-04, Fri 2027-01-01.
/// Bookings made by <see cref="Fill"/> are in Jan/Feb 2026, i.e. in holiday year 2025/26 (deadline 31 Dec 2026).
/// </summary>
public abstract class HolidayYearVmTestBase : IDisposable
{
    protected readonly string Dir = Path.Combine(Path.GetTempPath(), "vc-vm-" + Guid.NewGuid().ToString("N"));
    protected string DataFile => Path.Combine(Dir, "data.json");

    // The app formats decimals in the user's culture (comma on a Danish machine); pin invariant so expected text is stable.
    private readonly CultureInfo _oldCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _oldUICulture = CultureInfo.CurrentUICulture;

    protected HolidayYearVmTestBase() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _oldCulture;
        CultureInfo.CurrentUICulture = _oldUICulture;
        if (Directory.Exists(Dir))
            Directory.Delete(Dir, recursive: true);
    }

    protected static readonly DateOnly Dec18 = new(2026, 12, 18);
    protected static readonly DateOnly Dec31 = new(2026, 12, 31);

    protected MainViewModel NewVm(DateOnly today, VacationData? data = null)
    {
        var store = new DataStore(DataFile);
        if (data is not null)
            store.Save(data);
        return new MainViewModel(store, () => today);
    }

    /// <summary>
    /// <paramref name="days"/> vacation days on consecutive weekdays from Mon 2026-01-05 (a .5 becomes a final
    /// half day). 20 = ..Fri 01-30, 24 = ..Thu 02-05, 25 = ..Fri 02-06, 26 = ..Mon 02-09.
    /// </summary>
    protected static VacationData Seed(
        decimal days, bool transferAllowed = false, DateOnly? employedSince = null, int? firstTracked = 2025)
    {
        // firstTracked 2025 = what the app stores on the first save in 2026 (oldest holiday year open then, 2025/26).
        // Pass firstTracked: null for a file from before the setting existed.
        var data = new VacationData
        {
            EmployedSince = employedSince, FirstTrackedHolidayYear = firstTracked,
        };
        // 2c: the transfer switch is gone. "Transfer off" (the old default of these tests) is now a limit of 0 from the
        // first holiday year on (inherited by later years); "transfer on" is the plain default (limit 5).
        if (!transferAllowed)
            data.SetTransferLimit(new HolidayYear(firstTracked ?? 2025), 0);
        var d = new DateOnly(2026, 1, 5);
        for (var left = days; left > 0; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            var amount = Math.Min(left, 1m);
            data.Days.Add(new BookedDay { Date = d, Amount = amount });
            left -= amount;
        }
        return data;
    }

    protected static string[] Lines(string text) => text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Banner texts (StatusMessage / StatusDetail) for every state, with an injected clock.</summary>
public class MainViewModelStatusMessageTests : HolidayYearVmTestBase
{
    // ---- DaysToPlan ----

    [Fact]
    public void DaysToPlan_PaidOutOnly_SettingOff()
    {
        // Fri 2026-12-18; 20 of the 25 days used in January. 5 unbooked <= 10 free working days
        // (Dec 18, 21-25, 28-31 = 10). The 5 unused are 5th-week days -> paid out, none lost.
        var vm = NewVm(Dec18, Seed(20));

        Assert.Equal(BalanceStatus.DaysToPlan, vm.Status.Status);
        Assert.Equal(
            "You have 5 days not yet booked. They must be used by 31 December 2026 (10 working days are still free).",
            vm.StatusMessage);
        Assert.Contains("5 days will be paid out", vm.StatusDetail);
        Assert.DoesNotContain("lost", vm.StatusDetail);
    }

    [Fact]
    public void DaysToPlan_PaidOutAndLost_BothAreStated()
    {
        // 15 used -> 10 unbooked: 5 of them 5th-week (paid out), 5 within the first 20 (lost).
        var vm = NewVm(Dec18, Seed(15));

        Assert.Equal(BalanceStatus.DaysToPlan, vm.Status.Status);
        Assert.Contains("You have 10 days not yet booked", vm.StatusMessage);
        var detail = Lines(vm.StatusDetail)[0];
        Assert.Contains("5 days will be paid out", detail);
        Assert.Contains("5 days will be lost", detail);
    }

    [Fact]
    public void DaysToPlan_OnlyLost_WhenEarnedIsAtMost20()
    {
        // Employed since 2026-03-01: Mar..Aug = 6 months = 12.5 earned, so no 5th week. 5 booked -> 7.5 unbooked,
        // and 7.5 <= 10 free days -> DaysToPlan; all 7.5 would be lost.
        var vm = NewVm(Dec18, Seed(5, employedSince: new DateOnly(2026, 3, 1)));

        Assert.Equal(7.5m, vm.Status.UnbookedDays);
        Assert.Equal(BalanceStatus.DaysToPlan, vm.Status.Status);
        var detail = vm.StatusDetail;
        Assert.Contains("7.5 days will be lost", detail);
        Assert.DoesNotContain("paid out", detail);
        Assert.DoesNotContain("5th-week", detail);
    }

    [Fact]
    public void DaysToPlan_OneUnbookedDay_UsesSingular()
    {
        // 24 used -> 1 unbooked; 10 free.
        var vm = NewVm(Dec18, Seed(24));

        Assert.Equal(
            "You have 1 day not yet booked. It must be used by 31 December 2026 (10 working days are still free).",
            vm.StatusMessage);
        Assert.Contains("1 day will be paid out", vm.StatusDetail);
    }

    [Fact]
    public void DaysToPlan_OneFreeWorkingDay_UsesSingular()
    {
        // Thu 2026-12-31 is the only free working day; 24 used -> 1 unbooked.
        var vm = NewVm(Dec31, Seed(24));

        Assert.Equal(
            "You have 1 day not yet booked. It must be used by 31 December 2026 (1 working day is still free).",
            vm.StatusMessage);
    }

    [Fact]
    public void DaysToPlan_HalfDay_UsesPluralWithDecimal()
    {
        // 24.5 used -> 0.5 unbooked.
        var vm = NewVm(Dec31, Seed(24.5m));

        Assert.Equal(
            "You have 0.5 days not yet booked. They must be used by 31 December 2026 (1 working day is still free).",
            vm.StatusMessage);
    }

    [Fact]
    public void DaysToPlan_FutureYear_CountsWholeYear()
    {
        // Selected 2027 (holiday year 2026/27, nothing booked): 30 unbooked (25 own + 5 transferred in from the unused 5th week of 2025/26, default limit 5). 2027 starts on a Friday and has
        // 365 days = 52 weeks + 1 Friday -> 261 weekdays.
        var vm = NewVm(Dec18);
        vm.SelectedYear = 2027;

        Assert.Equal(BalanceStatus.DaysToPlan, vm.Status.Status);
        Assert.Equal(
            "You have 30 days not yet booked. They must be used by 31 December 2027 (261 working days are still free).",
            vm.StatusMessage);
    }

    // ---- DaysWillBeLost ----

    [Fact]
    public void DaysWillBeLost_CurrentYear_SplitsLostAndPaidOut()
    {
        // Fri 2026-12-18, nothing booked: 25 unbooked, 10 free working days -> 15 can't fit.
        // Booking all 10 uses 10 of the first 20, so the 15 left are 5 (5th week, transferred by default) + 10 lost.
        var vm = NewVm(Dec18);

        Assert.Equal(BalanceStatus.DaysWillBeLost, vm.Status.Status);
        Assert.Equal(
            "You have 25 days not yet booked, but only 10 working days are left from today through 31 December 2026.",
            vm.StatusMessage);
        var first = Lines(vm.StatusDetail)[0];
        Assert.StartsWith("15 days can't fit", first);
        Assert.Contains("10 days will be lost", first);
        Assert.Contains("5 days will be transferred to 2026/27", first);
        Assert.DoesNotContain("paid out", first);
    }

    [Fact]
    public void DaysWillBeLost_UsesInjectedToday_NotTheRealClock()
    {
        // Fri 2027-12-17 .. Fri 12-31 : free 17, 20-24, 27-31 = 11. The holiday year with deadline 2027 is 2026/27.
        var vm = NewVm(new DateOnly(2027, 12, 17));

        Assert.Contains("from today through 31 December 2027", vm.StatusMessage);
        Assert.Contains("only 11 working days are left", vm.StatusMessage);
    }

    [Fact]
    public void DaysWillBeLost_OneWorkingDayLeft_UsesSingular()
    {
        // Thu 2026-12-31: 1 free day, 25 unbooked, 24 can't fit.
        var vm = NewVm(Dec31);

        Assert.Equal(1m, vm.Status.WorkingDaysLeft);
        Assert.Contains("only 1 working day is left from today", vm.StatusMessage);
        Assert.DoesNotContain("1 working days", vm.StatusMessage);
        Assert.StartsWith("24 days can't fit", Lines(vm.StatusDetail)[0]);
    }

    [Fact]
    public void DaysWillBeLost_OneDayCantFit_UsesSingular()
    {
        // 23 used on Dec 31 -> 2 unbooked, 1 free -> 1 can't fit; it is a 5th-week day (unused 2 <= 5).
        var vm = NewVm(Dec31, Seed(23));

        Assert.Equal(BalanceStatus.DaysWillBeLost, vm.Status.Status);
        var first = Lines(vm.StatusDetail)[0];
        Assert.StartsWith("1 day can't fit", first);
        Assert.Contains("1 day will be paid out", first);
        Assert.DoesNotContain("lost", first);
    }

    [Fact]
    public void DaysWillBeLost_HalfWorkingDayLeft()
    {
        // Half of Thu 12-31 booked as well: 23 + 0.5 booked via data. Unbooked 1.5, free 0.5 -> 1 can't fit.
        var data = Seed(23);
        data.Days.Add(new BookedDay { Date = Dec31, Amount = 0.5m });
        var vm = NewVm(Dec31, data);

        Assert.Equal(
            "You have 1.5 days not yet booked, but only 0.5 working days are left from today through 31 December 2026.",
            vm.StatusMessage);
        Assert.StartsWith("1 day can't fit", Lines(vm.StatusDetail)[0]);
    }

    [Fact]
    public void DaysWillBeLost_FutureYear_SaysWholeYear()
    {
        // Selected 2027 with every weekday up to Fri 2027-12-17 a public holiday: free Dec 20-24, 27-31 = 10.
        // Holiday year 2026/27 has 25 unbooked -> 15 can't fit.
        var data = new VacationData();
        var year = new YearSettings { Year = 2027 };
        for (var d = new DateOnly(2027, 1, 1); d <= new DateOnly(2027, 12, 17); d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                year.PublicHolidays.Add(d);
        data.Years.Add(year);
        var vm = NewVm(Dec18, data);
        vm.SelectedYear = 2027;

        Assert.Equal(10m, vm.Status.WorkingDaysLeft);
        Assert.Equal(
            "You have 30 days not yet booked, but only 10 working days are left in 2027 up to and including 31 December 2027.",
            vm.StatusMessage);
    }

    // ---- AllPlanned ----

    [Fact]
    public void AllPlanned_AllDaysBooked()
    {
        var vm = NewVm(Dec18, Seed(25));

        Assert.Equal(BalanceStatus.AllPlanned, vm.Status.Status);
        Assert.Equal("Nothing will be paid out or lost.", vm.StatusMessage);
        Assert.False(vm.HasStatusDetail);
        Assert.Equal(0, vm.UnbookedSign);
    }

    [Fact]
    public void AllPlanned_WhenUnbookedDaysAreAllTransferred()
    {
        // 2b: transfer on, nothing registered. 20 used, 5 unbooked 5th-week days, default limit 5 -> all 5 transferred
        // to 2026/27, nothing paid out or lost.
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));

        Assert.Equal(BalanceStatus.AllPlanned, vm.Status.Status);
        Assert.Equal("Nothing will be paid out or lost.", vm.StatusMessage);
        Assert.Equal("5 days will be transferred to 2026/27.", vm.StatusDetail);
    }

    [Fact]
    public void AllPlanned_OneTransferredDay_Singular()
    {
        // 24 used, 1 unused 5th-week day, transferred.
        var vm = NewVm(Dec18, Seed(24, transferAllowed: true));

        Assert.Equal(BalanceStatus.AllPlanned, vm.Status.Status);
        Assert.Equal("1 day will be transferred to 2026/27.", vm.StatusDetail);
    }

    [Fact]
    public void AllPlanned_AfterTheDeadline_UsesPastTense()
    {
        // Mon 2027-01-04, viewing 2026: 20 used, the 5 unused were transferred.
        var vm = NewVm(new DateOnly(2027, 1, 4), Seed(20, transferAllowed: true));
        vm.SelectedYear = 2026;

        Assert.Equal(BalanceStatus.AllPlanned, vm.Status.Status);
        Assert.Equal("Nothing was paid out or lost.", vm.StatusMessage);
        Assert.Equal("5 days were transferred to 2026/27.", vm.StatusDetail);
    }

    [Fact]
    public void AllPlanned_AfterTheDeadline_OneTransferredDay_SaysWas()
    {
        var vm = NewVm(new DateOnly(2027, 1, 4), Seed(24, transferAllowed: true));
        vm.SelectedYear = 2026;

        Assert.Equal("1 day was transferred to 2026/27.", vm.StatusDetail);
    }

    [Fact]
    public void AllPlanned_AfterTheDeadline_NothingUnused_NoDetail()
    {
        var vm = NewVm(new DateOnly(2027, 1, 4), Seed(25, transferAllowed: true));
        vm.SelectedYear = 2026;

        Assert.Equal("Nothing was paid out or lost.", vm.StatusMessage);
        Assert.False(vm.HasStatusDetail);
    }

    [Fact]
    public void AllPlanned_NotAllPlanned_WhenTheUnbookedRestIsPaidOutNotTransferred()
    {
        // Transfer off, 20 used: the 5 unused are paid out, so the banner must not claim "nothing paid out".
        var vm = NewVm(Dec18, Seed(20));

        Assert.NotEqual(BalanceStatus.AllPlanned, vm.Status.Status);
        Assert.DoesNotContain("Nothing will be paid out", vm.StatusMessage);
    }

    // ---- OverBooked ----

    [Fact]
    public void OverBooked_OneDay_HalfDay_SeveralDays()
    {
        // 26 booked of 25 earnable -> 1 more; 25.5 -> 0.5 more; 30 -> 5 more.
        Assert.Equal("You have booked 1 day more than you can earn by 31 August.",
            NewVm(Dec18, Seed(26)).StatusMessage);
        Assert.Equal("You have booked 0.5 days more than you can earn by 31 August.",
            NewVm(Dec18, Seed(25.5m)).StatusMessage);
        Assert.Equal("You have booked 5 days more than you can earn by 31 August.",
            NewVm(Dec18, Seed(30)).StatusMessage);
    }

    [Fact]
    public void OverBooked_State_AndSign()
    {
        var vm = NewVm(Dec18, Seed(26));

        Assert.Equal(BalanceStatus.OverBooked, vm.Status.Status);
        Assert.Equal(-1, vm.UnbookedSign);
        Assert.False(vm.HasStatusDetail); // nothing is unused
    }

    [Fact]
    public void OverBooked_WithTransferOn_NothingIsTransferred()
    {
        // 26 booked of 25: over-booked, so nothing can be transferred to 2026/27.
        var vm = NewVm(Dec18, Seed(26, transferAllowed: true));

        Assert.Equal(BalanceStatus.OverBooked, vm.Status.Status);
        Assert.False(vm.HasStatusDetail);
        Assert.Equal(0m, vm.HolidayYearRows[0].TransferredOut);
        Assert.Equal(0m, vm.HolidayYearRows[1].TransferredIn);
    }

    // ---- Expired ----

    [Fact]
    public void Expired_PaidOutAndLost()
    {
        // Mon 2027-01-04, selected 2026: holiday year 2025/26 earned 25, 15 used -> 10 unused.
        // Transfer off: 5th-week 5 always paid out, the other 5 lost (payout default None).
        var vm = NewVm(new DateOnly(2027, 1, 4), Seed(15));
        vm.SelectedYear = 2026; // initial year is 2027 (today is in January 2027)

        Assert.Equal(BalanceStatus.Expired, vm.Status.Status);
        Assert.Equal(
            "Holiday year 2025/26 expired on 31 December 2026: 5 days were paid out and 5 days were lost.",
            vm.StatusMessage);
        Assert.False(vm.HasStatusDetail);
    }

    [Fact]
    public void Expired_OnlyPaidOut_Singular()
    {
        var vm = NewVm(new DateOnly(2027, 1, 4), Seed(24));
        vm.SelectedYear = 2026;

        Assert.Equal("Holiday year 2025/26 expired on 31 December 2026: 1 day was paid out.", vm.StatusMessage);
    }

    [Fact]
    public void Expired_HalfDay_UsesPlural()
    {
        var vm = NewVm(new DateOnly(2027, 1, 4), Seed(24.5m));
        vm.SelectedYear = 2026;

        Assert.Equal("Holiday year 2025/26 expired on 31 December 2026: 0.5 days were paid out.", vm.StatusMessage);
    }

    [Fact]
    public void Expired_OnlyLost_WhenAtMost20Earned()
    {
        // Employed since 2026-03-01: 12.5 earned, none used -> all 12.5 lost, nothing paid out.
        var vm = NewVm(new DateOnly(2027, 1, 4), Seed(0, employedSince: new DateOnly(2026, 3, 1)));
        vm.SelectedYear = 2026;

        Assert.Equal("Holiday year 2025/26 expired on 31 December 2026: 12.5 days were lost.", vm.StatusMessage);
    }

    [Fact]
    public void Expired_OneLostDay_Singular()
    {
        // 19 used of 25 -> 6 unused: 5 paid out, 1 lost.
        var vm = NewVm(new DateOnly(2027, 1, 4), Seed(19));
        vm.SelectedYear = 2026;

        Assert.Equal(
            "Holiday year 2025/26 expired on 31 December 2026: 5 days were paid out and 1 day was lost.",
            vm.StatusMessage);
    }

    [Fact]
    public void Expired_TransferOn_StatesTransferredPaidOutAndLost_InThatOrder()
    {
        // 5 used, 20 unused: 5th-week 5 transferred (limit 5), payout Some 4 -> 4 of the 15 other days paid out, 11 lost.
        var data = Seed(5, transferAllowed: true);
        data.SetPayout(new HolidayYear(2025), PayoutMode.Some, 4);
        var vm = NewVm(new DateOnly(2027, 1, 4), data);
        vm.SelectedYear = 2026;

        Assert.Equal(BalanceStatus.Expired, vm.Status.Status);
        Assert.Equal(
            "Holiday year 2025/26 expired on 31 December 2026: 5 days were transferred to 2026/27, 4 days were paid out and 11 days were lost.",
            vm.StatusMessage);
    }

    [Fact]
    public void Expired_OneTransferredDay_SaysWas()
    {
        // Limit 1: of 5 unused 5th-week days 1 is transferred, 4 paid out.
        var data = Seed(20, transferAllowed: true);
        data.SetTransferLimit(new HolidayYear(2025), 1);
        var vm = NewVm(new DateOnly(2027, 1, 4), data);
        vm.SelectedYear = 2026;

        Assert.Equal(
            "Holiday year 2025/26 expired on 31 December 2026: 1 day was transferred to 2026/27 and 4 days were paid out.",
            vm.StatusMessage);
    }

    [Fact]
    public void Expired_TransferredInDaysUnused_AreStatedAsLost()
    {
        // Holiday year 2026/27 (deadline 2027-12-31), viewed 2028-01-03. 20 used in 2025/26 -> 5 transferred in.
        // Nothing booked in 2026/27: 5 transferred-in lost; own 25: 5 transferred on, 20 lost -> 25 lost.
        var vm = NewVm(new DateOnly(2028, 1, 3), Seed(20, transferAllowed: true));
        vm.SelectedYear = 2027;

        Assert.Equal(BalanceStatus.Expired, vm.Status.Status);
        Assert.Equal(
            "Holiday year 2026/27 expired on 31 December 2027: 5 days were transferred to 2027/28 and 25 days were lost.",
            vm.StatusMessage);
    }

    [Fact]
    public void NotExpired_OnTheDeadlineItself()
    {
        var vm = NewVm(Dec31, Seed(15));

        Assert.NotEqual(BalanceStatus.Expired, vm.Status.Status);
    }

    // ---- Projected split with transfer / payout ----

    [Fact]
    public void FifthWeek_SettingOff_UnusedDays_SaysPaidOut()
    {
        var vm = NewVm(Dec18, Seed(20));

        Assert.Equal(new[] { "If they are not booked, 5 days will be paid out." }, Lines(vm.StatusDetail));
    }

    [Fact]
    public void FifthWeek_SettingOff_NoUnusedFifthWeek_NoPaidOutSentence()
    {
        var vm = NewVm(Dec18, Seed(5, employedSince: new DateOnly(2026, 3, 1)));

        Assert.DoesNotContain("paid out", vm.StatusDetail);
    }

    [Fact]
    public void DaysToPlan_TransferOn_TransferredAndLost()
    {
        // 15 used -> 10 unbooked == 10 free: DaysToPlan. Unused: 5 first-20 + 5 5th week. Transfer 5 (5th week), lose 5.
        var vm = NewVm(Dec18, Seed(15, transferAllowed: true));

        Assert.Equal(BalanceStatus.DaysToPlan, vm.Status.Status);
        Assert.Equal(
            new[] { "If they are not booked, 5 days will be transferred to 2026/27 and 5 days will be lost." },
            Lines(vm.StatusDetail));
    }

    [Fact]
    public void DaysToPlan_TransferOn_AllThreeParts()
    {
        // 15 used, limit 7, payout Some 1: unused 10 = 5 first-20 + 5 5th week. Transfer 7 (5 + 2 others),
        // remaining 3 other: 1 paid out, 2 lost. Fifth week is all transferred.
        var data = Seed(15, transferAllowed: true);
        data.SetTransferLimit(new HolidayYear(2025), 7);
        data.SetPayout(new HolidayYear(2025), PayoutMode.Some, 1);
        var vm = NewVm(Dec18, data);

        Assert.Equal(BalanceStatus.DaysToPlan, vm.Status.Status);
        Assert.Equal(
            "If they are not booked, 7 days will be transferred to 2026/27, 1 day will be paid out and 2 days will be lost.",
            Assert.Single(Lines(vm.StatusDetail)));
    }

    [Fact]
    public void DaysToPlan_TransferOn_LimitSmallerThan5_RestOfFifthWeekIsPaidOut()
    {
        // 20 used, limit 2: 2 transferred, 3 5th-week days paid out (always), nothing lost.
        var data = Seed(20, transferAllowed: true);
        data.SetTransferLimit(new HolidayYear(2025), 2);
        var vm = NewVm(Dec18, data);

        Assert.Equal(BalanceStatus.DaysToPlan, vm.Status.Status);
        Assert.Equal(
            "If they are not booked, 2 days will be transferred to 2026/27 and 3 days will be paid out.",
            Assert.Single(Lines(vm.StatusDetail)));
    }

    [Fact]
    public void DaysToPlan_PayoutAll_NothingLost()
    {
        // Transfer off, 15 used: 5 5th-week + 5 other unused. Payout All -> 10 paid out, 0 lost.
        var data = Seed(15);
        data.SetPayout(new HolidayYear(2025), PayoutMode.All);
        var vm = NewVm(Dec18, data);

        Assert.Equal(
            "If they are not booked, 10 days will be paid out.",
            Assert.Single(Lines(vm.StatusDetail)));
    }

    [Fact]
    public void DaysToPlan_UnbookedRestLargerThanFreeDays_ButTransferable_IsNotDaysWillBeLost()
    {
        // 10 used, transfer on: 15 unbooked, 10 free, 5 can't fit - but they are the 5 transferable days,
        // so nothing is paid out or lost even if every free day is booked: DaysToPlan, not DaysWillBeLost.
        var vm = NewVm(Dec18, Seed(10, transferAllowed: true));

        Assert.Equal(BalanceStatus.DaysToPlan, vm.Status.Status);
        Assert.Equal(5m, vm.Status.DaysAtRisk);
        Assert.Equal(
            "You have 15 days not yet booked. They must be used by 31 December 2026 (10 working days are still free).",
            vm.StatusMessage);
        // Unused if nothing more is booked: 5 transferred, 10 lost.
        Assert.Equal(
            "If they are not booked, 5 days will be transferred to 2026/27 and 10 days will be lost.",
            Assert.Single(Lines(vm.StatusDetail)));
    }

    [Fact]
    public void DaysWillBeLost_TransferOn_SplitIncludesTransferPart()
    {
        // 5 used, transfer on: 20 unbooked, 10 free, 10 can't fit. Booking 10 -> 15 booked, 10 unused
        // (5 first-20 + 5 5th week): 5 transferred, 5 lost.
        var vm = NewVm(Dec18, Seed(5, transferAllowed: true));

        Assert.Equal(BalanceStatus.DaysWillBeLost, vm.Status.Status);
        Assert.Equal(
            "10 days can't fit even if you book every free working day: 5 days will be transferred to 2026/27 and 5 days will be lost.",
            Assert.Single(Lines(vm.StatusDetail)));
    }

    [Fact]
    public void DaysWillBeLost_TransferOn_Singular()
    {
        // Thu 2026-12-31, 23 used + 1 free day: unbooked 2, 1 can't fit. Booking it -> 24 booked, 1 unused 5th-week
        // day -> transferred. Not lost, so status is DaysToPlan here; use a limit of 0 to force the paid-out case.
        var data = Seed(23, transferAllowed: true);
        data.SetTransferLimit(new HolidayYear(2025), 0);
        var vm = NewVm(Dec31, data);

        Assert.Equal(BalanceStatus.DaysWillBeLost, vm.Status.Status);
        Assert.Equal(
            "1 day can't fit even if you book every free working day: 1 day will be paid out.",
            Assert.Single(Lines(vm.StatusDetail)));
    }

    // ---- Transferred-in line ----

    [Fact]
    public void TransferredIn_LineWarnsThatDaysMustBeUsedInThisHolidayYear()
    {
        // 20 used in 2025/26 -> 5 transferred into 2026/27. Viewing 2027 on Fri 2026-12-18: 30 unbooked (261 free).
        // Unused if nothing is booked: 5 transferred-in lost + own 25 (5 transferred on, 20 lost) = 25 lost.
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));
        vm.SelectedYear = 2027;

        Assert.Equal(BalanceStatus.DaysToPlan, vm.Status.Status);
        var lines = Lines(vm.StatusDetail);
        Assert.Equal(2, lines.Length);
        Assert.Equal("If they are not booked, 5 days will be transferred to 2027/28 and 25 days will be lost.", lines[0]);
        Assert.Contains("5 days transferred from 2025/26", lines[1]);
        Assert.Contains("must be used", lines[1]);
        Assert.Contains("lost", lines[1]);
    }

    [Fact]
    public void TransferredIn_LineIsSingularForOneDay()
    {
        var data = Seed(24, transferAllowed: true); // 1 unused -> 1 transferred in to 2026/27
        var vm = NewVm(Dec18, data);
        vm.SelectedYear = 2027;

        var lines = Lines(vm.StatusDetail);
        Assert.Contains("1 day transferred from 2025/26", lines[^1]);
        Assert.Contains("it is lost", lines[^1]);
    }

    [Fact]
    public void TransferredIn_NoLine_WhenNothingWasTransferredIn()
    {
        var vm = NewVm(Dec18, Seed(25, transferAllowed: true));
        vm.SelectedYear = 2027;

        Assert.DoesNotContain("transferred from", vm.StatusDetail);
    }

    [Fact]
    public void TransferredIn_NoLine_WhenAllTransferredInDaysAreBooked()
    {
        // 20 used in 2025/26 -> 5 transferred into 2026/27. Book 5 days in 2027 (Mon 2027-01-04..Fri 01-08):
        // those use the transferred-in days first, none left unused.
        var data = Seed(20, transferAllowed: true);
        for (var d = new DateOnly(2027, 1, 4); d <= new DateOnly(2027, 1, 8); d = d.AddDays(1))
            data.Days.Add(new BookedDay { Date = d });
        var vm = NewVm(Dec18, data);
        vm.SelectedYear = 2027;

        Assert.DoesNotContain("transferred from", vm.StatusDetail);
    }
}
