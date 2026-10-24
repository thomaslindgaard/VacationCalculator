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

    protected HolidayYearVmTestBase() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _oldCulture;
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
        decimal days, bool transferAllowed = false, decimal transferred = 0, DateOnly? employedSince = null)
    {
        var data = new VacationData { EmployedSince = employedSince, FifthWeekTransferAllowed = transferAllowed };
        var d = new DateOnly(2026, 1, 5);
        for (var left = days; left > 0; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            var amount = Math.Min(left, 1m);
            data.Days.Add(new BookedDay { Date = d, Amount = amount });
            left -= amount;
        }
        if (transferred != 0)
            data.HolidayYears.Add(new HolidayYearSettings { StartYear = 2025, TransferredToNext = transferred });
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
        // Selected 2027 (holiday year 2026/27, nothing booked): 25 unbooked. 2027 starts on a Friday and has
        // 365 days = 52 weeks + 1 Friday -> 261 weekdays.
        var vm = NewVm(Dec18);
        vm.SelectedYear = 2027;

        Assert.Equal(BalanceStatus.DaysToPlan, vm.Status.Status);
        Assert.Equal(
            "You have 25 days not yet booked. They must be used by 31 December 2027 (261 working days are still free).",
            vm.StatusMessage);
    }

    // ---- DaysWillBeLost ----

    [Fact]
    public void DaysWillBeLost_CurrentYear_SplitsLostAndPaidOut()
    {
        // Fri 2026-12-18, nothing booked: 25 unbooked, 10 free working days -> 15 can't fit.
        // Booking all 10 uses 10 of the first 20, so the 15 left are 5 (5th week, paid out) + 10 lost.
        var vm = NewVm(Dec18);

        Assert.Equal(BalanceStatus.DaysWillBeLost, vm.Status.Status);
        Assert.Equal(
            "You have 25 days not yet booked, but only 10 working days are left from today through 31 December 2026.",
            vm.StatusMessage);
        var first = Lines(vm.StatusDetail)[0];
        Assert.StartsWith("15 days can't fit", first);
        Assert.Contains("10 days will be lost", first);
        Assert.Contains("5 days will be paid out", first);
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
            "You have 25 days not yet booked, but only 10 working days are left in 2027 up to and including 31 December 2027.",
            vm.StatusMessage);
    }

    // ---- AllPlanned ----

    [Fact]
    public void AllPlanned_AllDaysBooked()
    {
        var vm = NewVm(Dec18, Seed(25));

        Assert.Equal(BalanceStatus.AllPlanned, vm.Status.Status);
        Assert.Contains("Nothing will expire", vm.StatusMessage);
        Assert.False(vm.HasStatusDetail);
        Assert.Equal(0, vm.UnbookedSign);
    }

    [Fact]
    public void AllPlanned_WhenUnbookedDaysAreAllTransferred()
    {
        // 20 used + 5 transferred = nothing left to plan.
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true, transferred: 5));

        Assert.Equal(BalanceStatus.AllPlanned, vm.Status.Status);
        Assert.Contains("transferred", vm.StatusMessage);
        Assert.False(vm.HasStatusDetail);
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
        Assert.False(vm.HasStatusDetail); // no 5th-week sentence: nothing is unused
    }

    // ---- Expired ----

    [Fact]
    public void Expired_PaidOutAndLost()
    {
        // Mon 2027-01-04, selected 2026: holiday year 2025/26 earned 25, 15 used -> 10 unused.
        // The unused days are 5th-week first: 5 paid out, 5 lost (the example from the requirement).
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
        // 24 used -> 1 unused 5th-week day.
        var vm = NewVm(new DateOnly(2027, 1, 4), Seed(24));
        vm.SelectedYear = 2026; // initial year is 2027 (today is in January 2027)

        Assert.Equal("Holiday year 2025/26 expired on 31 December 2026: 1 day was paid out.", vm.StatusMessage);
    }

    [Fact]
    public void Expired_HalfDay_UsesPlural()
    {
        var vm = NewVm(new DateOnly(2027, 1, 4), Seed(24.5m));
        vm.SelectedYear = 2026; // initial year is 2027 (today is in January 2027)

        Assert.Equal("Holiday year 2025/26 expired on 31 December 2026: 0.5 days were paid out.", vm.StatusMessage);
    }

    [Fact]
    public void Expired_OnlyLost_WhenAtMost20Earned()
    {
        // Employed since 2026-03-01: 12.5 earned, none used -> all 12.5 lost, nothing paid out.
        var vm = NewVm(new DateOnly(2027, 1, 4), Seed(0, employedSince: new DateOnly(2026, 3, 1)));
        vm.SelectedYear = 2026; // initial year is 2027 (today is in January 2027)

        Assert.Equal("Holiday year 2025/26 expired on 31 December 2026: 12.5 days were lost.", vm.StatusMessage);
    }

    [Fact]
    public void Expired_OneLostDay_Singular()
    {
        // 19 used of 25 -> 6 unused: 5 paid out, 1 lost.
        var vm = NewVm(new DateOnly(2027, 1, 4), Seed(19));
        vm.SelectedYear = 2026; // initial year is 2027 (today is in January 2027)

        Assert.Equal(
            "Holiday year 2025/26 expired on 31 December 2026: 5 days were paid out and 1 day was lost.",
            vm.StatusMessage);
    }

    [Fact]
    public void NotExpired_OnTheDeadlineItself()
    {
        // Thu 2026-12-31 is still within the holiday-taking period, so it is not Expired.
        var vm = NewVm(Dec31, Seed(15));

        Assert.NotEqual(BalanceStatus.Expired, vm.Status.Status);
    }

    // ---- 5th-week sentences ----

    [Fact]
    public void FifthWeek_SettingOff_UnusedDays_SaysPaidOut()
    {
        var vm = NewVm(Dec18, Seed(20));

        var lines = Lines(vm.StatusDetail);
        Assert.Contains("If they are not booked, 5 days will be paid out.", lines);
        Assert.DoesNotContain("5th-week", vm.StatusDetail);
        Assert.DoesNotContain("transferred", vm.StatusDetail);
    }

    [Fact]
    public void FifthWeek_SettingOff_NoUnusedFifthWeek_NoSentence()
    {
        // Earned 12.5 (employed since 2026-03-01): there is no 5th week at all, so no 5th-week sentence.
        var vm = NewVm(Dec18, Seed(5, employedSince: new DateOnly(2026, 3, 1)));

        Assert.DoesNotContain("5th-week", vm.StatusDetail);
    }

    [Fact]
    public void FifthWeek_SettingOn_NothingTransferred_SaysCanBeTransferred()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));

        Assert.Contains(
            "The unused 5th-week days (5 days) can be transferred by agreement before 31 December, otherwise they are paid out.",
            Lines(vm.StatusDetail));
    }

    [Fact]
    public void FifthWeek_SettingOn_PartlyTransferred_NoTransferSentence_AndPaidOutIsReduced()
    {
        // 20 used, 3 of the 5 unused 5th-week days transferred -> 2 unbooked; only 2 are paid out.
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true, transferred: 3));

        Assert.Equal(BalanceStatus.DaysToPlan, vm.Status.Status);
        Assert.Contains("You have 2 days not yet booked", vm.StatusMessage);
        Assert.Contains("2 days will be paid out", vm.StatusDetail);
        Assert.DoesNotContain("5th-week", vm.StatusDetail);
    }

    [Fact]
    public void FifthWeek_SettingOn_AfterDeadline_NoTransferSentence()
    {
        var vm = NewVm(new DateOnly(2027, 1, 4), Seed(20, transferAllowed: true));
        vm.SelectedYear = 2026; // initial year is 2027 (today is in January 2027)

        Assert.False(vm.HasStatusDetail);
    }

    // ---- Wording problem reported to the owner ----

    [Fact]
    public void FifthWeek_SettingOff_DoesNotRepeatPaidOutTwice()
    {
        // The requirement: the banner "says they will be paid out". Saying it twice in a row, i.e.
        // "If they are not booked, 5 days will be paid out." followed by
        // "The unused 5th-week days (5 days) will be paid out.", is redundant.
        var vm = NewVm(Dec18, Seed(20));

        var paidOutMentions = Lines(vm.StatusDetail).Count(l => l.Contains("paid out"));
        Assert.True(paidOutMentions <= 1, "Detail repeats 'paid out':" + Environment.NewLine + vm.StatusDetail);
    }
}
