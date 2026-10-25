using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>
/// Feature 2b: the per-holiday-year transfer limit and payout settings in the sidebar. Fixed clock Fri 2026-12-18, so the
/// selected calendar year 2026 shows holiday year 2025/26 (StartYear 2025), 2027 shows 2026/27 (StartYear 2026).
/// </summary>
public class MainViewModelDeadlineSettingsTests : HolidayYearVmTestBase
{
    // Tests that need 10 unbooked <= free days start on Fri 2026-12-11 (12 free working days; from Fri 12-18 only 7 are left
    // because 24, 25 and 31 December are public holidays).
    private VacationData Saved() => new DataStore(DataFile).Load();

    // ---- Defaults ----

    [Fact]
    public void Defaults_Limit5_PayoutNone_SourceDefault()
    {
        var vm = NewVm(Dec18);

        Assert.Equal(5m, vm.TransferLimitDays);
        Assert.False(vm.TransferLimitIsOwn);
        Assert.Contains("Default", vm.TransferLimitSource, StringComparison.OrdinalIgnoreCase);
        Assert.True(vm.PayoutNone);
        Assert.False(vm.PayoutSome);
        Assert.False(vm.PayoutAll);
        Assert.Equal(0m, vm.PayoutDays);
        Assert.False(vm.PayoutIsOwn);
        Assert.Contains("Default", vm.PayoutSource, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Title_NamesTheHolidayYearTheSettingsApplyTo_AndFollowsTheSelectedYear()
    {
        var vm = NewVm(Dec18);
        Assert.Contains("2025/26", vm.DeadlineSettingsTitle);

        vm.SelectedYear = 2027;
        Assert.Contains("2026/27", vm.DeadlineSettingsTitle);
        Assert.DoesNotContain("2025/26", vm.DeadlineSettingsTitle);
    }

    [Fact]
    public void ReadingSettings_AndBrowsingYears_NeverAddsHolidayYearEntries()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));
        foreach (var year in new[] { 2026, 2027, 2025, 2030, 2026 })
        {
            vm.SelectedYear = year;
            _ = (vm.TransferLimitDays, vm.TransferLimitSource, vm.TransferLimitIsOwn, vm.PayoutNone, vm.PayoutSome,
                vm.PayoutAll, vm.PayoutDays, vm.PayoutSource, vm.PayoutIsOwn, vm.DeadlineSettingsTitle);
        }

        // Force a save through an unrelated edit and look at the file.
        vm.ResetTransferLimitCommand.Execute(null); // saves without adding an entry
        Assert.Empty(Saved().HolidayYears);
    }

    // ---- Transfer limit ----

    [Fact]
    public void TransferLimit_Set_IsStoredOnTheHolidayYearOnly_AndMarkedOwn()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));

        vm.TransferLimitDays = 8;

        Assert.Equal(8m, vm.TransferLimitDays);
        Assert.True(vm.TransferLimitIsOwn);
        Assert.Contains("this year", vm.TransferLimitSource, StringComparison.OrdinalIgnoreCase);
        var entry = Assert.Single(Saved().HolidayYears);
        Assert.Equal(2025, entry.StartYear);
        Assert.Equal(8m, entry.TransferLimitDays);
        Assert.Null(entry.PayoutMode);
    }

    [Fact]
    public void TransferLimit_RaisesNotifications_AndRefreshesTheBanner()
    {
        // 20 used, transfer on, default limit 5: AllPlanned. Limit 2 -> 2 transferred, 3 paid out -> DaysToPlan.
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));
        Assert.Equal(BalanceStatus.AllPlanned, vm.Status.Status);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.TransferLimitDays = 2;

        Assert.Contains("TransferLimitDays", raised);
        Assert.Contains("TransferLimitSource", raised);
        Assert.Contains("TransferLimitIsOwn", raised);
        Assert.Contains("StatusMessage", raised);
        Assert.Equal(BalanceStatus.DaysToPlan, vm.Status.Status);
        Assert.Equal(
            "If they are not booked, 2 days will be transferred to 2026/27 and 3 days will be paid out.",
            vm.StatusDetail);
        Assert.Equal(2m, vm.SummaryRows[1].TransferredIn);
    }

    [Fact]
    public void TransferLimit_Zero_TransfersNothing_AndFifthWeekIsPaidOut()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));

        vm.TransferLimitDays = 0;

        Assert.Equal(0m, vm.TransferLimitDays);
        Assert.True(vm.TransferLimitIsOwn);
        Assert.Equal(0m, vm.SummaryRows[1].TransferredIn);
        Assert.Equal("If they are not booked, 5 days will be paid out.", vm.StatusDetail);
    }

    [Fact]
    public void TransferLimit_Negative_IsStoredAsZero()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));

        vm.TransferLimitDays = -3;

        Assert.Equal(0m, vm.TransferLimitDays);
        Assert.Equal(0m, Assert.Single(Saved().HolidayYears).TransferLimitDays);
    }

    [Fact]
    public void TransferLimit_Decimal_IsKept()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));

        vm.TransferLimitDays = 2.5m;

        Assert.Equal(2.5m, vm.TransferLimitDays);
        Assert.Equal(2.5m, NewVm(Dec18).TransferLimitDays);
        Assert.Equal(2.5m, vm.SummaryRows[1].TransferredIn);
    }

    [Fact]
    public void TransferLimit_Above5_TransfersOtherDaysToo()
    {
        // 15 used: 5 first-20 + 5 5th-week unused; limit 8 -> 8 transferred (5th week first), 2 lost.
        var vm = NewVm(Dec11, Seed(15, transferAllowed: true));

        vm.TransferLimitDays = 8;

        Assert.Equal(8m, vm.SummaryRows[1].TransferredIn);
        Assert.Equal(8m, vm.SummaryRows[1].TransferredIn);
        Assert.Equal("If they are not booked, 8 days will be transferred to 2026/27 and 2 days will be lost.", vm.StatusDetail);
    }

    [Fact]
    public void TransferLimit_FollowsTheSelectedYear_Inherits_AndCanBeOverridden()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));
        vm.TransferLimitDays = 8; // holiday year 2025/26

        vm.SelectedYear = 2027; // holiday year 2026/27: nothing of its own -> inherits 8
        Assert.Equal(8m, vm.TransferLimitDays);
        Assert.False(vm.TransferLimitIsOwn);
        Assert.Contains("2025/26", vm.TransferLimitSource);

        vm.TransferLimitDays = 3; // its own
        Assert.Equal(3m, vm.TransferLimitDays);
        Assert.True(vm.TransferLimitIsOwn);

        vm.SelectedYear = 2026; // unchanged
        Assert.Equal(8m, vm.TransferLimitDays);
        vm.SelectedYear = 2028; // 2027/28 inherits from 2026/27 (nearest earlier)
        Assert.Equal(3m, vm.TransferLimitDays);
        Assert.Contains("2026/27", vm.TransferLimitSource);
        vm.SelectedYear = 2025; // 2024/25 is earlier than any set value: default
        Assert.Equal(5m, vm.TransferLimitDays);
        Assert.Contains("Default", vm.TransferLimitSource, StringComparison.OrdinalIgnoreCase);

        var saved = Saved();
        Assert.Equal(2, saved.HolidayYears.Count);
        Assert.Equal(8m, saved.FindHolidayYear(2025)!.TransferLimitDays);
        Assert.Equal(3m, saved.FindHolidayYear(2026)!.TransferLimitDays);
    }

    [Fact]
    public void ResetTransferLimit_GoesBackToInheritedValue_AndKeepsThePayoutSetting()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));
        vm.TransferLimitDays = 8;
        vm.SelectedYear = 2027;
        vm.TransferLimitDays = 3;
        vm.PayoutAll = true;

        vm.ResetTransferLimitCommand.Execute(null);

        Assert.Equal(8m, vm.TransferLimitDays);
        Assert.False(vm.TransferLimitIsOwn);
        Assert.True(vm.PayoutAll);
        Assert.True(vm.PayoutIsOwn);
        Assert.Null(Saved().FindHolidayYear(2026)!.TransferLimitDays);
        Assert.Equal(8m, Saved().FindHolidayYear(2025)!.TransferLimitDays);
    }

    [Fact]
    public void ResetTransferLimit_WithNoOwnValue_ChangesNothing_AndAddsNoEntry()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));

        vm.ResetTransferLimitCommand.Execute(null);

        Assert.Equal(5m, vm.TransferLimitDays);
        Assert.Empty(Saved().HolidayYears);
        Assert.Single(Saved().Days.Take(1));
    }

    [Fact]
    public void TransferLimit_SurvivesReload()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));
        vm.TransferLimitDays = 4;

        var reloaded = NewVm(Dec18);

        Assert.Equal(4m, reloaded.TransferLimitDays);
        Assert.True(reloaded.TransferLimitIsOwn);
        Assert.Equal(4m, reloaded.SummaryRows[1].TransferredIn);
    }

    [Fact]
    public void LimitZero_ThenBackToNonZero_TransfersAgain()
    {
        // 2c: "transfer off" is just limit 0 on the year. 20 used -> 5 unused 5th-week days.
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));
        vm.TransferLimitDays = 0;
        Assert.Equal(0m, vm.SummaryRows[1].TransferredIn);
        Assert.Equal("If they are not booked, 5 days will be paid out.", vm.StatusDetail);

        vm.TransferLimitDays = 4;
        Assert.Equal(4m, vm.SummaryRows[1].TransferredIn);
    }

    // ---- Payout ----

    [Fact]
    public void Payout_SomeWithNumber_IsStored_AndShownInTheBanner()
    {
        // Transfer off, 15 used: 5 5th-week (always paid) + 5 other unused. Some 3 -> 8 paid out, 2 lost.
        var vm = NewVm(Dec11, Seed(15));

        vm.PayoutSome = true;
        vm.PayoutDays = 3;

        Assert.True(vm.PayoutSome);
        Assert.False(vm.PayoutNone);
        Assert.False(vm.PayoutAll);
        Assert.Equal(3m, vm.PayoutDays);
        Assert.True(vm.PayoutIsOwn);
        var entry = Assert.Single(Saved().HolidayYears);
        Assert.Equal(PayoutMode.Some, entry.PayoutMode);
        Assert.Equal(3m, entry.PayoutDays);
        Assert.Equal(0m, entry.TransferLimitDays); // the seeded "transfer off" (limit 0), untouched by the payout edit
        Assert.Equal("If they are not booked, 8 days will be paid out and 2 days will be lost.", vm.StatusDetail);
    }

    [Fact]
    public void Payout_SomeLargerThanTheRest_PaysOnlyWhatIsThere()
    {
        // Same data, Some 100: all 5 other days paid out too -> 10 paid out, none lost.
        var vm = NewVm(Dec11, Seed(15));
        vm.PayoutSome = true;
        vm.PayoutDays = 100;

        Assert.Equal("If they are not booked, 10 days will be paid out.", vm.StatusDetail);
    }

    [Fact]
    public void Payout_All_PaysEverythingBeyondTheFifthWeek()
    {
        var vm = NewVm(Dec11, Seed(15));

        vm.PayoutAll = true;

        Assert.True(vm.PayoutAll);
        Assert.False(vm.PayoutSome);
        Assert.False(vm.PayoutNone);
        Assert.Equal("If they are not booked, 10 days will be paid out.", vm.StatusDetail);
        Assert.Equal(PayoutMode.All, Assert.Single(Saved().HolidayYears).PayoutMode);
    }

    [Fact]
    public void Payout_BackToNone_LosesTheOtherDaysAgain()
    {
        var vm = NewVm(Dec11, Seed(15));
        vm.PayoutAll = true;

        vm.PayoutNone = true;

        Assert.True(vm.PayoutNone);
        Assert.False(vm.PayoutAll);
        Assert.True(vm.PayoutIsOwn); // an explicit "None" for this year
        Assert.Contains("5 days will be lost", vm.StatusDetail);
        Assert.Contains("5 days will be paid out", vm.StatusDetail);
    }

    [Fact]
    public void Payout_FalseWrites_DoNotChangeAnything()
    {
        // Radio buttons write false to the option being left; that must not change the mode.
        var vm = NewVm(Dec11, Seed(15));
        vm.PayoutAll = true;

        vm.PayoutNone = false;
        vm.PayoutSome = false;
        vm.PayoutAll = false;

        Assert.True(vm.PayoutAll);
    }

    [Fact]
    public void PayoutDays_IgnoredUnlessModeIsSome()
    {
        var vm = NewVm(Dec11, Seed(15));

        vm.PayoutDays = 3;

        Assert.Equal(0m, vm.PayoutDays);
        Assert.All(Saved().HolidayYears, e => Assert.Null(e.PayoutMode)); // only the seeded limit 0 entry, no payout setting
    }

    [Fact]
    public void PayoutDays_Negative_IsStoredAsZero()
    {
        var vm = NewVm(Dec11, Seed(15));
        vm.PayoutSome = true;

        vm.PayoutDays = -2;

        Assert.Equal(0m, vm.PayoutDays);
        Assert.Contains("5 days will be lost", vm.StatusDetail);
    }

    [Fact]
    public void Payout_FollowsTheSelectedYear_Inherits_AndResets()
    {
        var vm = NewVm(Dec11, Seed(15));
        vm.PayoutAll = true; // 2025/26

        vm.SelectedYear = 2027;
        Assert.True(vm.PayoutAll);
        Assert.False(vm.PayoutIsOwn);
        Assert.Contains("2025/26", vm.PayoutSource);

        vm.PayoutNone = true; // 2026/27 overrides
        Assert.True(vm.PayoutNone);
        Assert.True(vm.PayoutIsOwn);
        vm.SelectedYear = 2026;
        Assert.True(vm.PayoutAll);

        vm.SelectedYear = 2027;
        vm.ResetPayoutCommand.Execute(null);
        Assert.True(vm.PayoutAll);
        Assert.False(vm.PayoutIsOwn);
        Assert.Null(Saved().FindHolidayYear(2026)!.PayoutMode);
        Assert.Equal(PayoutMode.All, Saved().FindHolidayYear(2025)!.PayoutMode);
    }

    [Fact]
    public void Payout_SurvivesReload()
    {
        var vm = NewVm(Dec11, Seed(15));
        vm.PayoutSome = true;
        vm.PayoutDays = 2.5m;

        var reloaded = NewVm(Dec18);

        Assert.True(reloaded.PayoutSome);
        Assert.Equal(2.5m, reloaded.PayoutDays);
    }

    [Fact]
    public void Payout_DoesNotAffectTransferredInDays_AlwaysLost()
    {
        // Payout All for 2026/27: the 5 transferred-in days, unused, are still lost (not paid out); own 5th week is
        // transferred on (default limit 5); own other 20 are paid out. Expired view of 2026/27 on 2028-01-03.
        var data = Seed(20, transferAllowed: true);
        data.SetPayout(new HolidayYear(2026), PayoutMode.All);
        var vm = NewVm(new DateOnly(2028, 1, 3), data);
        vm.SelectedYear = 2027;

        Assert.Equal(
            "Holiday year 2026/27 expired on 31 December 2027: 5 days were transferred to 2027/28, 20 days were paid out and 5 days were lost.",
            vm.StatusMessage);
    }

    // ---- Previous version's file ----

    [Fact]
    public void OldFile_FifthWeekTransferAllowed_AndStoredTransfer_Loads_DefaultLimitApplies_IgnoringTheOldTransfer()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(DataFile, """
            {
              "Days": [ { "Date": "2026-01-05", "Type": "Vacation", "Amount": 1, "Description": "x" } ],
              "FifthWeekTransferAllowed": true,
              "HolidayYears": [ { "StartYear": 2025, "TransferredToNext": 3 } ]
            }
            """);

        var vm = NewVm(Dec18);

        Assert.Equal(5m, vm.TransferLimitDays);
        Assert.False(vm.TransferLimitIsOwn);
        // 1 used, 24 unused: default limit 5 -> 5 transferred, not the old 3.
        Assert.Equal(5m, vm.SummaryRows[1].TransferredIn);

        // An edit saves and keeps the booking and the transfer setting.
        vm.TransferLimitDays = 6;
        var saved = Saved();
        Assert.Single(saved.Days);
        Assert.DoesNotContain("TransferAllowed", File.ReadAllText(DataFile)); // the old switch is dropped on save
    }
}
