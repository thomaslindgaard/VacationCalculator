using System.Text.Json;
using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>
/// Backlog 2c: the "unused days can be transferred" switch is gone. Transfers depend only on each holiday year's
/// effective limit (default 5, inherited from the nearest earlier year that sets one, 0 = none).
/// Fixed clock Fri 2026-12-18: selected calendar year 2026 shows holiday year 2025/26, 2027 shows 2026/27.
/// </summary>
public class MainViewModelTransferSwitchRemovedTests : HolidayYearVmTestBase
{
    private VacationData Saved() => new DataStore(DataFile).Load();

    /// <summary>Plain data with no transfer setting at all: 20 weekdays booked Mon 2026-01-05 .. Fri 2026-01-30.</summary>
    private static VacationData Plain(int days = 20)
    {
        var data = new VacationData { FirstTrackedHolidayYear = 2025 };
        var d = new DateOnly(2026, 1, 5);
        for (var n = 0; n < days; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            data.Days.Add(new BookedDay { Date = d });
            n++;
        }
        return data;
    }

    // ---- The switch no longer exists ----

    [Fact]
    public void ViewModelAndData_HaveNoTransferSwitch()
    {
        Assert.Null(typeof(MainViewModel).GetProperty("TransferAllowed"));
        Assert.Null(typeof(VacationData).GetProperty("TransferAllowed"));
        Assert.Null(typeof(VacationData).GetProperty("FifthWeekTransferAllowed"));
        Assert.Null(typeof(VacationData).GetProperty("LegacyFifthWeekTransferAllowed"));
    }

    [Fact]
    public void Sidebar_HasNoSwitchCheckbox_AndTheLimitBoxIsNotHiddenByAnyTrigger()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VacationCalculator.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var xaml = File.ReadAllLines(Path.Combine(dir!.FullName, "src", "VacationCalculator.App", "MainWindow.xaml"));

        Assert.DoesNotContain(xaml, l => l.Contains("TransferAllowed"));
        Assert.DoesNotContain(xaml, l => l.Contains("can be transferred to the next holiday year"));
        var label = Array.FindIndex(xaml, l => l.Contains("Days that can be transferred"));
        Assert.True(label >= 0);
        Assert.Contains("TransferLimitDays", xaml[label + 1]);
        // Nothing in the few lines around the label collapses or triggers on visibility.
        var around = string.Join("\n", xaml[Math.Max(0, label - 4)..(label + 2)]);
        Assert.DoesNotContain("Collapsed", around);
        Assert.DoesNotContain("DataTrigger", around);
        Assert.Contains("0 = no transfer", string.Join("\n", xaml));
    }

    [Fact]
    public void LimitBox_IsAlwaysUsable_EvenAtZero_AndOnAFreshFile()
    {
        var vm = NewVm(Dec18); // no file at all

        Assert.Equal(5m, vm.TransferLimitDays);
        vm.TransferLimitDays = 0;
        Assert.Equal(0m, vm.TransferLimitDays);
        vm.TransferLimitDays = 7;
        Assert.Equal(7m, vm.TransferLimitDays);
    }

    // ---- Defaults ----

    [Fact]
    public void DefaultData_TransfersTheUnusedFifthWeek_NothingPaidOut()
    {
        // 20 of 25 used in Jan 2026 (Mon 01-05 .. Fri 01-30 = 4 full weeks); 5 unused 5th-week days, default limit 5.
        var vm = NewVm(Dec18, Plain());

        Assert.Equal(5m, vm.HolidayYearRows[0].TransferredOut);
        Assert.Equal(5m, vm.HolidayYearRows[1].TransferredIn);
        Assert.Equal(BalanceStatus.AllPlanned, vm.Status.Status);
        Assert.Equal("5 days will be transferred to 2026/27.", vm.StatusDetail);
        Assert.DoesNotContain("paid out", vm.StatusDetail);
    }

    [Fact]
    public void DefaultData_NextYearGetsThe5ExtraDays()
    {
        // 2026/27: 25 earned + 5 transferred in, nothing booked.
        var vm = NewVm(Dec18, Plain());
        vm.SelectedYear = 2027;

        Assert.Equal(30m, vm.Status.UnbookedDays);
    }

    // ---- Limit 0 means no transfer, and is inherited ----

    [Fact]
    public void LimitZero_PaysOutTheFifthWeek_AndIsInheritedByLaterYearsUntilOneSetsItsOwn()
    {
        var vm = NewVm(Dec18, Plain());

        vm.TransferLimitDays = 0; // 2025/26
        Assert.Equal(0m, vm.HolidayYearRows[0].TransferredOut);
        Assert.Equal(0m, vm.HolidayYearRows[1].TransferredIn);
        Assert.Equal("If they are not booked, 5 days will be paid out.", vm.StatusDetail);

        vm.SelectedYear = 2027; // 2026/27 inherits 0
        Assert.Equal(0m, vm.TransferLimitDays);
        Assert.False(vm.TransferLimitIsOwn);
        Assert.Equal(25m, vm.Status.UnbookedDays); // nothing transferred in
        Assert.Equal(0m, vm.HolidayYearRows[0].TransferredOut); // rows at 2027 = 2026/27, 2027/28; inherited 0: its own unused 5th week is paid out too

        vm.SelectedYear = 2028; // 2027/28 inherits 0 as well
        Assert.Equal(0m, vm.TransferLimitDays);

        vm.SelectedYear = 2027;
        vm.TransferLimitDays = 5; // 2026/27 sets its own
        Assert.True(vm.TransferLimitIsOwn);
        Assert.Equal(5m, vm.HolidayYearRows[0].TransferredOut); // nothing booked in 2026/27: 5th week moves on
        Assert.Equal(0m, vm.HolidayYearRows[0].TransferredIn); // 2025/26 still transfers nothing

        vm.SelectedYear = 2028; // now inherits 5 from 2026/27
        Assert.Equal(5m, vm.TransferLimitDays);
        Assert.Contains("2026/27", vm.TransferLimitSource);
        Assert.Equal(5m, vm.HolidayYearRows[0].TransferredIn); // row 0 is 2027/28; receives 2026/27's 5

        vm.SelectedYear = 2026; // earlier year unaffected: still its own 0
        Assert.Equal(0m, vm.TransferLimitDays);
        Assert.True(vm.TransferLimitIsOwn);
    }

    [Fact]
    public void LimitZero_ResetGoesBackToTheDefault5_AndTransfersAgain()
    {
        var vm = NewVm(Dec18, Plain());
        vm.TransferLimitDays = 0;

        vm.ResetTransferLimitCommand.Execute(null);

        Assert.Equal(5m, vm.TransferLimitDays);
        Assert.False(vm.TransferLimitIsOwn);
        Assert.Equal(5m, vm.HolidayYearRows[0].TransferredOut);
        Assert.Null(Saved().FindHolidayYear(2025)!.TransferLimitDays);
    }

    [Fact]
    public void LimitZero_OpeningBalanceStillApplies_ItIsAFactAboutThePast()
    {
        var data = Plain(0);
        data.OpeningBalanceDays = 4;
        var vm = NewVm(Dec18, data);

        vm.TransferLimitDays = 0;

        Assert.Equal(4m, vm.HolidayYearRows[0].TransferredIn);
    }

    // ---- Old files ----

    [Fact]
    public void OldFile_TransferAllowedFalse_Loads_AndTheDefaultLimitNowApplies()
    {
        // Behaviour change (documented in the report): the old switch value is ignored. A user who had it off now gets
        // the default limit 5, i.e. unused 5th-week days are transferred instead of paid out, until they set 0.
        Directory.CreateDirectory(Dir);
        File.WriteAllText(DataFile, """
            {
              "Days": [ { "Date": "2026-01-05", "Type": "Vacation", "Amount": 1, "Description": "keep me" } ],
              "TransferAllowed": false,
              "FirstTrackedHolidayYear": 2025
            }
            """);

        var vm = NewVm(Dec18);

        Assert.Equal(5m, vm.TransferLimitDays);
        Assert.False(vm.TransferLimitIsOwn);
        Assert.Equal(5m, vm.HolidayYearRows[0].TransferredOut); // 24 unused: only 5th week (5) moves on
        Assert.Contains("transferred to 2026/27", vm.StatusDetail);

        // The booking survives an edit and the dropped switch is not written back.
        vm.OpeningBalanceDays = 1;
        var text = File.ReadAllText(DataFile);
        Assert.DoesNotContain("TransferAllowed", text);
        Assert.Equal("keep me", Assert.Single(Saved().Days).Description);
    }

    [Fact]
    public void OldFile_TransferAllowedTrue_AndFifthWeekSpelling_BothLoadWithTheSameResult()
    {
        foreach (var name in new[] { "TransferAllowed", "FifthWeekTransferAllowed" })
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(DataFile, $$"""
                { "Days": [], "{{name}}": true, "FirstTrackedHolidayYear": 2025 }
                """);

            var vm = NewVm(Dec18);

            Assert.Equal(5m, vm.TransferLimitDays);
            Assert.Equal(5m, vm.HolidayYearRows[0].TransferredOut); // nothing booked: 25 unused, 5th week moves on
        }
    }

    [Fact]
    public void OldFile_SwitchOff_WithAnExplicitStoredLimit_KeepsTheStoredLimit()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(DataFile, """
            {
              "TransferAllowed": false,
              "FirstTrackedHolidayYear": 2025,
              "HolidayYears": [ { "StartYear": 2025, "TransferLimitDays": 2 } ]
            }
            """);

        var vm = NewVm(Dec18);

        Assert.Equal(2m, vm.TransferLimitDays);
        Assert.True(vm.TransferLimitIsOwn);
        Assert.Equal(2m, vm.HolidayYearRows[0].TransferredOut);
    }

    [Fact]
    public void SavedFile_NeverContainsEitherSwitchProperty()
    {
        var vm = NewVm(Dec18, Plain());
        vm.TransferLimitDays = 0;

        using var doc = JsonDocument.Parse(File.ReadAllText(DataFile));
        Assert.False(doc.RootElement.TryGetProperty("TransferAllowed", out _));
        Assert.False(doc.RootElement.TryGetProperty("FifthWeekTransferAllowed", out _));
        Assert.Equal(0m, Saved().FindHolidayYear(2025)!.TransferLimitDays);
    }
}
