using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>
/// Feature 2b opening balance in the view model: the first tracked holiday year is stored only on the first save, the
/// selector range, the opening balance, and pinning a typed value equal to the inherited one. Clock Fri 2026-12-18
/// unless stated: selected calendar year 2026 = holiday year 2025/26 (StartYear 2025), open years 2025/26 and 2026/27.
/// </summary>
public class MainViewModelOpeningBalanceTests : HolidayYearVmTestBase
{
    private VacationData Saved() => new DataStore(DataFile).Load();

    // ---- First tracked year is stored on the first save only ----

    [Fact]
    public void Load_WithoutChanges_WritesNothing_AndDoesNotCreateAFile()
    {
        var vm = NewVm(Dec18);
        _ = (vm.FirstTrackedHolidayYear, vm.FirstTrackedChoices, vm.OpeningBalanceDays, vm.StatusMessage);
        vm.SelectedYear = 2027;
        vm.SelectedYear = 2024;

        Assert.False(File.Exists(DataFile));
    }

    [Fact]
    public void Load_OfAFileWithoutFirstTrackedYear_LeavesTheFileUntouched()
    {
        NewVm(Dec18, Seed(10, firstTracked: null)); // writes the seed
        var before = File.ReadAllBytes(DataFile);
        var stamp = File.GetLastWriteTimeUtc(DataFile);

        var vm = NewVm(Dec18);
        vm.SelectedYear = 2027;
        vm.SelectedYear = 2026;

        Assert.Equal(before, File.ReadAllBytes(DataFile));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(DataFile));
        Assert.Null(Saved().FirstTrackedHolidayYear);
        Assert.Equal(new HolidayYear(2025), vm.FirstTrackedHolidayYear); // the default shows meanwhile
    }

    [Theory]
    // today, expected stored start year (oldest holiday year open that day)
    [InlineData("2026-12-18", 2025)]
    [InlineData("2026-08-31", 2025)]
    [InlineData("2026-09-01", 2025)]
    [InlineData("2026-12-31", 2025)]
    [InlineData("2027-01-01", 2026)]
    [InlineData("2027-01-04", 2026)]
    public void FirstSave_StoresTheDefaultFirstTrackedYear(string today, int expected)
    {
        var vm = NewVm(DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture), Seed(0, firstTracked: null));
        Assert.Null(Saved().FirstTrackedHolidayYear);

        vm.OpeningBalanceDays = 1; // any edit saves

        Assert.Equal(expected, Saved().FirstTrackedHolidayYear);
        Assert.Equal(new HolidayYear(expected), vm.FirstTrackedHolidayYear);
    }

    [Fact]
    public void FirstSave_ThroughABooking_AlsoStoresIt()
    {
        var vm = NewVm(Dec18, Seed(0, firstTracked: null));

        vm.NewStart = new DateTime(2026, 12, 21);
        vm.AddEntryCommand.Execute(null);

        Assert.Equal(2025, Saved().FirstTrackedHolidayYear);
    }

    [Fact]
    public void ALaterSave_DoesNotMoveTheStoredYear()
    {
        var first = NewVm(Dec18, Seed(0, firstTracked: null));
        first.OpeningBalanceDays = 1; // stores 2025

        var later = NewVm(new DateOnly(2028, 3, 1));
        later.OpeningBalanceDays = 2; // saves again, two years later

        var saved = Saved();
        Assert.Equal(2025, saved.FirstTrackedHolidayYear);
        Assert.Equal(2m, saved.OpeningBalanceDays);
        Assert.Equal(new HolidayYear(2025), later.FirstTrackedHolidayYear);
    }

    [Fact]
    public void StoredValue_IsNeverOverwrittenBySaves()
    {
        var vm = NewVm(Dec18, Seed(0, firstTracked: 2019));

        vm.OpeningBalanceDays = 1;
        vm.EmployedSince = new DateTime(2018, 5, 2);

        Assert.Equal(2019, Saved().FirstTrackedHolidayYear);
    }

    // ---- Selector range ----

    [Fact]
    public void Choices_FromTheEarliestSupportedYear_ToTheNewestOpenYear()
    {
        var vm = NewVm(Dec18);

        // Open on 2026-12-18: 2025/26 and 2026/27 -> newest 2026. Earliest 2015.
        Assert.Equal(Enumerable.Range(2015, 12).Select(y => new HolidayYear(y)), vm.FirstTrackedChoices);
        Assert.Contains(vm.FirstTrackedHolidayYear, vm.FirstTrackedChoices);
    }

    [Fact]
    public void Choices_NewestIsTheNewestOpenYear_OnDifferentDays()
    {
        // 2026-08-31: only 2025/26 is open. 2026-09-01: 2026/27 opens. 2027-01-01: only 2026/27.
        Assert.Equal(2025, NewVm(new DateOnly(2026, 8, 31)).FirstTrackedChoices[^1].StartYear);
        Assert.Equal(2026, NewVm(new DateOnly(2026, 9, 1)).FirstTrackedChoices[^1].StartYear);
        Assert.Equal(2026, NewVm(new DateOnly(2027, 1, 1)).FirstTrackedChoices[^1].StartYear);
    }

    [Fact]
    public void Choices_StartAtTheEmployedSinceYear_WhenLaterThan2015()
    {
        // Employed 2020-03-10 = holiday year 2019/20 (Mar < Sep). Range 2019..2026.
        var vm = NewVm(Dec18, Seed(0, employedSince: new DateOnly(2020, 3, 10), firstTracked: 2025));

        Assert.Equal(new HolidayYear(2019), vm.FirstTrackedChoices[0]);
        Assert.Equal(new HolidayYear(2026), vm.FirstTrackedChoices[^1]);
        Assert.Equal(8, vm.FirstTrackedChoices.Count);
    }

    [Fact]
    public void Choices_EmployedSinceInSeptember_StartsThatSeptembersYear()
    {
        // 2019-09-01 is in holiday year 2019/20; 2019-08-31 in 2018/19.
        Assert.Equal(2019, NewVm(Dec18, Seed(0, employedSince: new DateOnly(2019, 9, 1), firstTracked: 2025)).FirstTrackedChoices[0].StartYear);
    }

    [Fact]
    public void Choices_EmployedSinceBefore2015_StartAt2015()
    {
        var vm = NewVm(Dec18, Seed(0, employedSince: new DateOnly(2009, 1, 1), firstTracked: 2025));

        Assert.Equal(2015, vm.FirstTrackedChoices[0].StartYear);
    }

    [Fact]
    public void Choices_StoredValueOutsideTheRange_IsIncluded()
    {
        var older = NewVm(Dec18, Seed(0, firstTracked: 2012));
        Assert.Equal(2012, older.FirstTrackedChoices[0].StartYear);
        Assert.Contains(new HolidayYear(2012), older.FirstTrackedChoices);
        Assert.Equal(2026, older.FirstTrackedChoices[^1].StartYear);
        Assert.Equal(2012, older.FirstTrackedHolidayYear.StartYear);

        var newer = NewVm(Dec18, Seed(0, firstTracked: 2029));
        Assert.Equal(2029, newer.FirstTrackedChoices[^1].StartYear);
        Assert.Equal(newer.FirstTrackedChoices.Count, newer.FirstTrackedChoices.Distinct().Count());
    }

    [Fact]
    public void Choices_EmployedSinceAfterTheStoredYear_StillContainsTheStoredYear()
    {
        // Employed 2026-10-01 (holiday year 2026) but first tracked 2025 stored: 2025 stays selectable.
        var vm = NewVm(Dec18, Seed(0, employedSince: new DateOnly(2026, 10, 1), firstTracked: 2025));

        Assert.Equal([new HolidayYear(2025), new HolidayYear(2026)], vm.FirstTrackedChoices);
    }

    [Fact]
    public void Choices_UnstoredDefaultOutsideTheEmployedRange_IsStillIncluded()
    {
        // Nothing stored, employed since 2026-10-01 (2026/27); default first year 2025 is widened in.
        var vm = NewVm(Dec18, Seed(0, employedSince: new DateOnly(2026, 10, 1), firstTracked: null));

        Assert.Equal(new HolidayYear(2025), vm.FirstTrackedHolidayYear);
        Assert.Contains(new HolidayYear(2025), vm.FirstTrackedChoices);
    }

    // ---- Changing the first tracked year ----

    [Fact]
    public void ChoosingAFirstTrackedYear_IsSaved_AndMovesTheOpeningBalance()
    {
        var vm = NewVm(Dec18, Seed(0, transferAllowed: true));
        vm.OpeningBalanceDays = 3;
        Assert.Equal(3m, vm.HolidayYearRows[0].TransferredIn); // 2025/26 row

        vm.FirstTrackedHolidayYear = new HolidayYear(2026);

        Assert.Equal(2026, Saved().FirstTrackedHolidayYear);
        Assert.Equal(0m, vm.HolidayYearRows[0].TransferredIn);
        Assert.Equal(3m, vm.HolidayYearRows[1].TransferredIn); // 2026/27 row
        Assert.Equal(0m, vm.HolidayYearRows[0].TransferredOut); // 2025/26 is before the first tracked year
    }

    [Fact]
    public void ChoosingAFirstTrackedYear_RaisesNotifications()
    {
        var vm = NewVm(Dec18, Seed(0));
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.FirstTrackedHolidayYear = new HolidayYear(2024);

        Assert.Contains("FirstTrackedHolidayYear", raised);
        Assert.Contains("HolidayYearRows", raised);
        Assert.Equal(new HolidayYear(2024), vm.FirstTrackedHolidayYear);
    }

    [Fact]
    public void ChoosingNothing_FromTheComboBox_IsIgnored()
    {
        var vm = NewVm(Dec18, Seed(0, firstTracked: 2025));

        vm.FirstTrackedHolidayYear = default; // StartYear 0 = no selection

        Assert.Equal(2025, vm.FirstTrackedHolidayYear.StartYear);
        Assert.Equal(2025, Saved().FirstTrackedHolidayYear);
    }

    [Fact]
    public void FirstTrackedYear_SurvivesReload()
    {
        var vm = NewVm(Dec18, Seed(0));
        vm.FirstTrackedHolidayYear = new HolidayYear(2022);

        Assert.Equal(new HolidayYear(2022), NewVm(Dec18).FirstTrackedHolidayYear);
    }

    // ---- Opening balance ----

    [Fact]
    public void OpeningBalance_DefaultsToZero()
    {
        Assert.Equal(0m, NewVm(Dec18).OpeningBalanceDays);
    }

    [Fact]
    public void OpeningBalance_Set_IsSaved_RaisesNotifications_AndShowsInTheFirstTrackedRow()
    {
        var vm = NewVm(Dec18, Seed(0));
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.OpeningBalanceDays = 3;

        Assert.Equal(3m, Saved().OpeningBalanceDays);
        Assert.Contains("OpeningBalanceDays", raised);
        Assert.Contains("StatusMessage", raised);
        Assert.Equal(3m, vm.HolidayYearRows[0].TransferredIn); // first tracked 2025 = the 2025/26 row
        Assert.Equal(0m, vm.HolidayYearRows[1].TransferredIn); // transfer off: nothing flows on
        Assert.Equal(28m, vm.Status.HolidayYear.TotalDays);
    }

    [Fact]
    public void OpeningBalance_AppliesWithTransferLimitZero()
    {
        var vm = NewVm(Dec18, Seed(10));
        vm.OpeningBalanceDays = 4;

        Assert.Equal(0m, vm.TransferLimitDays); // the test data has transfer off = limit 0
        Assert.Equal(4m, vm.HolidayYearRows[0].TransferredIn);
        // 29 total, 10 booked.
        Assert.Equal(19m, vm.Status.UnbookedDays);
    }

    [Fact]
    public void OpeningBalance_Decimal_IsKept_AndSurvivesReload()
    {
        var vm = NewVm(Dec18, Seed(0));
        vm.OpeningBalanceDays = 2.5m;

        var reloaded = NewVm(Dec18);

        Assert.Equal(2.5m, reloaded.OpeningBalanceDays);
        Assert.Equal(2.5m, reloaded.HolidayYearRows[0].TransferredIn);
    }

    [Fact]
    public void OpeningBalance_Negative_ReadsAndStoresAsZero()
    {
        var vm = NewVm(Dec18, Seed(0));
        vm.OpeningBalanceDays = 3;

        vm.OpeningBalanceDays = -2;

        Assert.Equal(0m, vm.OpeningBalanceDays);
        Assert.Equal(0m, Saved().OpeningBalanceDays);
        Assert.Equal(0m, vm.HolidayYearRows[0].TransferredIn);
    }

    [Fact]
    public void OpeningBalance_SameValue_DoesNotSave()
    {
        var vm = NewVm(Dec18, Seed(0));
        vm.OpeningBalanceDays = 3;
        File.Delete(DataFile);

        vm.OpeningBalanceDays = 3;

        Assert.False(File.Exists(DataFile));
    }

    [Fact]
    public void OpeningBalance_UnusedDays_AreStatedInTheBanner_AsTransferredInAndLost()
    {
        // 1 booked in Jan 2026 uses 1 of the 3 opening days; 2 are left and must be used in 2025/26, or are lost.
        var vm = NewVm(Dec18, Seed(1));
        vm.OpeningBalanceDays = 3;

        Assert.Equal(2m, vm.Status.HolidayYear.UnusedTransferredInDays);
        Assert.Contains("2 days of opening balance must be used in this holiday year, or they are lost.", vm.StatusDetail);
        Assert.DoesNotContain("transferred from", vm.StatusDetail);
    }

    [Fact]
    public void OpeningBalance_OneUnusedDay_BannerUsesSingular()
    {
        // 2 booked in Jan 2026 (Mon 5th, Tue 6th) use 2 of the 3 opening days; 1 is left.
        var data = Seed(0);
        data.Days.Add(new BookedDay { Date = new DateOnly(2026, 1, 5) });
        data.Days.Add(new BookedDay { Date = new DateOnly(2026, 1, 6) });
        var vm = NewVm(Dec18, data);
        vm.OpeningBalanceDays = 3;

        Assert.Equal(1m, vm.Status.HolidayYear.UnusedTransferredInDays);
        Assert.Contains("1 day of opening balance must be used in this holiday year, or it is lost.", vm.StatusDetail);
    }

    [Fact]
    public void OpeningBalance_UsedUp_NoTransferredInLineInTheBanner()
    {
        // 20 booked: 3 opening + 17 own. Unused: own 8 (5th week 5 paid out, 3 lost); no transferred-in left.
        // Fri 2026-12-11 has 12 free working days (11, 14-18, 21-23, 28-30), so 8 unbooked still fit (from 12-18 only 7 do).
        var vm = NewVm(Dec11, Seed(20));
        vm.OpeningBalanceDays = 3;

        Assert.Equal(0m, vm.Status.HolidayYear.UnusedTransferredInDays);
        Assert.DoesNotContain("transferred from", vm.StatusDetail);
        Assert.Equal("If they are not booked, 5 days will be paid out and 3 days will be lost.", vm.StatusDetail);
    }

    [Fact]
    public void OpeningBalance_Expired_UnusedOpeningDaysCountAsLost()
    {
        // Today Mon 2027-01-04, viewing 2026: first tracked 2025, opening 3, nothing booked, transfer off.
        // Own 25: 5 paid out (5th week), 20 lost; opening 3 lost -> 23 lost.
        var data = Seed(0, firstTracked: 2025);
        data.OpeningBalanceDays = 3;
        var vm = NewVm(new DateOnly(2027, 1, 4), data);
        vm.SelectedYear = 2026;

        Assert.Equal(
            "Holiday year 2025/26 expired on 31 December 2026: 5 days were paid out and 23 days were lost.",
            vm.StatusMessage);
    }

    // ---- Pinning a typed value equal to the inherited one ----

    [Fact]
    public void TransferLimit_TypingTheInheritedDefault_PinsItForThisYear()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));
        Assert.False(vm.TransferLimitIsOwn);

        vm.TransferLimitDays = 5; // equals the default

        Assert.True(vm.TransferLimitIsOwn);
        Assert.Contains("this year", vm.TransferLimitSource, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5m, Assert.Single(Saved().HolidayYears).TransferLimitDays);
    }

    [Fact]
    public void TransferLimit_TypingTheInheritedValueFromAnEarlierYear_PinsItForThisYear()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));
        vm.TransferLimitDays = 8; // 2025/26
        vm.SelectedYear = 2027;
        Assert.False(vm.TransferLimitIsOwn);

        vm.TransferLimitDays = 8;

        Assert.True(vm.TransferLimitIsOwn);
        Assert.Equal(8m, Saved().FindHolidayYear(2026)!.TransferLimitDays);

        // Pinned: changing the earlier year no longer changes this one.
        vm.SelectedYear = 2026;
        vm.TransferLimitDays = 6;
        vm.SelectedYear = 2027;
        Assert.Equal(8m, vm.TransferLimitDays);
    }

    [Fact]
    public void TransferLimit_TypingTheOwnValueAgain_DoesNotSave()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));
        vm.TransferLimitDays = 8;
        File.Delete(DataFile);

        vm.TransferLimitDays = 8;

        Assert.False(File.Exists(DataFile));
        Assert.True(vm.TransferLimitIsOwn);
    }

    [Fact]
    public void TransferLimit_ResetThenTypeTheSame_PinsAgain()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));
        vm.TransferLimitDays = 5;
        vm.ResetTransferLimitCommand.Execute(null);
        Assert.False(vm.TransferLimitIsOwn);

        vm.TransferLimitDays = 5;

        Assert.True(vm.TransferLimitIsOwn);
    }

    [Fact]
    public void PayoutDays_TypingTheInheritedNumber_PinsPayoutForThisYear()
    {
        var vm = NewVm(Dec18, Seed(15));
        vm.PayoutSome = true;
        vm.PayoutDays = 3; // 2025/26
        vm.SelectedYear = 2027;
        Assert.True(vm.PayoutSome);
        Assert.False(vm.PayoutIsOwn);

        vm.PayoutDays = 3;

        Assert.True(vm.PayoutIsOwn);
        var entry = Saved().FindHolidayYear(2026)!;
        Assert.Equal(PayoutMode.Some, entry.PayoutMode);
        Assert.Equal(3m, entry.PayoutDays);
    }

    [Fact]
    public void PayoutDays_TypingTheOwnNumberAgain_DoesNotSave()
    {
        var vm = NewVm(Dec18, Seed(15));
        vm.PayoutSome = true;
        vm.PayoutDays = 3;
        File.Delete(DataFile);

        vm.PayoutDays = 3;

        Assert.False(File.Exists(DataFile));
    }

    [Fact]
    public void PayoutDays_WhenModeIsNotSome_StillIgnored_EvenIfEqual()
    {
        var vm = NewVm(Dec18, Seed(15));
        vm.PayoutAll = true;
        vm.SelectedYear = 2027; // inherits All

        vm.PayoutDays = 0;

        Assert.False(vm.PayoutIsOwn);
        Assert.Null(Saved().FindHolidayYear(2026));
    }

    // ---- Old files ----

    [Fact]
    public void OldFile_WithoutFirstTrackedYearOrOpeningBalance_LoadsWithDefaults_AndKeepsEverythingOnTheFirstSave()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(DataFile, """
            {
              "Days": [ { "Date": "2026-01-05", "Type": "Vacation", "Amount": 1, "Description": "keep me" } ],
              "TransferAllowed": true
            }
            """);

        var vm = NewVm(Dec18);

        Assert.Equal(new HolidayYear(2025), vm.FirstTrackedHolidayYear);
        Assert.Equal(0m, vm.OpeningBalanceDays);
        Assert.Null(Saved().FirstTrackedHolidayYear);

        vm.OpeningBalanceDays = 1;
        var saved = Saved();
        Assert.Equal("keep me", Assert.Single(saved.Days).Description);
        Assert.DoesNotContain("TransferAllowed", File.ReadAllText(DataFile)); // the old switch is dropped on save
        Assert.Equal(2025, saved.FirstTrackedHolidayYear);
    }

    [Fact]
    public void FileWithNegativeOpeningBalance_ReadsAsZero()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(DataFile, """{ "FirstTrackedHolidayYear": 2025, "OpeningBalanceDays": -7 }""");

        Assert.Equal(0m, NewVm(Dec18).OpeningBalanceDays);
    }
}
