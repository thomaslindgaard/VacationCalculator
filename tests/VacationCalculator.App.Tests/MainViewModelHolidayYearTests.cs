using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>Settings (Employed since, 5th-week transfer), transfer registration and the holiday-year card.</summary>
public class MainViewModelHolidayYearTests : HolidayYearVmTestBase
{
    private VacationData Saved() => new DataStore(DataFile).Load();

    // ---- Employed since ----

    [Fact]
    public void EmployedSince_DefaultsToNull_AndMeansFullYear()
    {
        var vm = NewVm(Dec18);

        Assert.Null(vm.EmployedSince);
        Assert.Equal(25m, vm.Status.HolidayYear.EarnableDays);
        Assert.Equal("of 25 earnable", vm.EarnableNote);
    }

    [Fact]
    public void EmployedSince_Set_ChangesEarnable_IsSaved_AndSurvivesReload()
    {
        // Employed since Tue 2026-06-16 in holiday year 2025/26: June 16..30 = 15 of 30 days = half a month,
        // plus July and August = 2.5 months. 25 * 2.5 / 12 = 5.2083 -> 5.21.
        var vm = NewVm(Dec18);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.EmployedSince = new DateTime(2026, 6, 16);

        Assert.Equal(5.21m, vm.Status.HolidayYear.EarnableDays);
        Assert.Equal("of 5.21 earnable", vm.EarnableNote);
        Assert.Contains("EmployedSince", raised);
        Assert.Contains("StatusMessage", raised);
        Assert.Equal(new DateOnly(2026, 6, 16), Saved().EmployedSince);

        var reloaded = NewVm(Dec18);
        Assert.Equal(new DateTime(2026, 6, 16), reloaded.EmployedSince);
        Assert.Equal(5.21m, reloaded.Status.HolidayYear.EarnableDays);
    }

    [Fact]
    public void EmployedSince_ClearedAgain_GoesBackToFullYear()
    {
        var vm = NewVm(Dec18, Seed(0, employedSince: new DateOnly(2026, 6, 16)));
        Assert.Equal(5.21m, vm.Status.HolidayYear.EarnableDays);

        vm.EmployedSince = null;

        Assert.Equal(25m, vm.Status.HolidayYear.EarnableDays);
        Assert.Null(Saved().EmployedSince);
    }

    [Fact]
    public void EmployedSince_AfterTheHolidayYear_EarnsNothing_AndNothingToPlan()
    {
        // Employed from 2026-09-01: holiday year 2025/26 earns 0, so there is nothing unbooked.
        var vm = NewVm(Dec18);
        vm.EmployedSince = new DateTime(2026, 9, 1);

        Assert.Equal(0m, vm.Status.HolidayYear.EarnableDays);
        Assert.Equal(BalanceStatus.AllPlanned, vm.Status.Status);
    }

    // ---- Holiday-year card, title ----

    [Fact]
    public void Title_AndRows_ForCurrentYear()
    {
        // Fri 2026-12-18, 20 days used: holiday year 2025/26 (earned 25 of 25, used 20, remaining 5) and
        // 2026/27 (Sep, Oct, Nov = 3 months = 6.25 earned so far of 25, nothing booked).
        var vm = NewVm(Dec18, Seed(20));

        Assert.Equal("Holiday year 2025/26 · use by 31 December 2026", vm.HolidayYearTitle);
        Assert.Equal(2, vm.HolidayYearRows.Count);
        var old = vm.HolidayYearRows[0];
        Assert.Equal("2025/26", old.Name);
        Assert.Equal("25 of 25", old.Earned);
        Assert.Equal(20m, old.Used);
        Assert.Equal(0m, old.BookedAhead);
        Assert.Equal(5m, old.Remaining);
        Assert.Equal(1, old.RemainingSign);
        Assert.Equal(new DateOnly(2026, 12, 31), old.Deadline);
        var next = vm.HolidayYearRows[1];
        Assert.Equal("2026/27", next.Name);
        Assert.Equal("6.25 of 25", next.Earned);
        Assert.Equal(6.25m, next.Remaining);
        Assert.Equal(new DateOnly(2027, 12, 31), next.Deadline);
    }

    [Fact]
    public void Title_AndRows_FollowSelectedYear()
    {
        var vm = NewVm(Dec18);
        vm.SelectedYear = 2027;

        Assert.Equal("Holiday year 2026/27 · use by 31 December 2027", vm.HolidayYearTitle);
        Assert.Equal(["2026/27", "2027/28"], vm.HolidayYearRows.Select(r => r.Name));
    }

    [Fact]
    public void Rows_OldestYearTakesBookingsFirst_InSeptemberToDecember()
    {
        // 25 used in Jan/Feb 2026 -> 2025/26 is full. Mon 12-21, Tue 12-22, Wed 12-23 are after today (12-18):
        // they go to 2026/27 as booked ahead. 6.25 earned so far -> remaining 3.25.
        var data = Seed(25);
        foreach (var day in new[] { 21, 22, 23 })
            data.Days.Add(new BookedDay { Date = new DateOnly(2026, 12, day) });
        var vm = NewVm(Dec18, data);

        var next = vm.HolidayYearRows[1];
        Assert.Equal(3m, next.BookedAhead);
        Assert.Equal(3.25m, next.Remaining);
        Assert.Equal("", next.Note);
        Assert.Equal(25m, vm.HolidayYearRows[0].Used);
    }

    [Fact]
    public void Rows_AdvanceHoliday_IsFlaggedAndNegative()
    {
        // 7 December days (21-25, 28, 29) all land in 2026/27: 6.25 earned so far - 7 = -0.75.
        var data = Seed(25);
        foreach (var day in new[] { 21, 22, 23, 24, 25, 28, 29 })
            data.Days.Add(new BookedDay { Date = new DateOnly(2026, 12, day) });
        var vm = NewVm(Dec18, data);

        var next = vm.HolidayYearRows[1];
        Assert.Equal(-0.75m, next.Remaining);
        Assert.Equal(-1, next.RemainingSign);
        Assert.Equal("advance holiday: 0.75 not earned yet", next.Note);
        Assert.False(next.NoteIsError);
    }

    [Fact]
    public void Rows_BookedBeyondWhatCanBeEarned_IsFlaggedAsError()
    {
        // 26 booked days in 2025/26 (25 earnable) -> 1 more than can be earned by 31 August.
        var vm = NewVm(Dec18, Seed(26));

        var row = vm.HolidayYearRows[0];
        Assert.Equal("1 more than can be earned by 31 August", row.Note);
        Assert.True(row.NoteIsError);
        Assert.Equal(-1, row.RemainingSign);
    }

    [Fact]
    public void BookingBeyondEarnedDays_IsNeverBlocked()
    {
        // Employed since 2026-09-01: 2025/26 earns 0 and today is Dec 18 -> still a booking on Mon 12-21 is accepted.
        var vm = NewVm(Dec18, Seed(0, employedSince: new DateOnly(2026, 9, 1)));
        vm.NewStart = new DateTime(2026, 12, 21);
        vm.AddEntryCommand.Execute(null);

        Assert.Equal("", vm.ErrorMessage);
        Assert.Single(vm.Entries);
        Assert.Single(Saved().Days);
    }

    // ---- Transfer setting ----

    [Fact]
    public void TransferSetting_DefaultsToOff_Toggles_AndIsSaved()
    {
        var vm = NewVm(Dec18, Seed(20));
        Assert.False(vm.FifthWeekTransferAllowed);
        Assert.Equal("max 0", vm.MaxTransferText);
        Assert.False(vm.Status.TransferOptionApplies);

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.FifthWeekTransferAllowed = true;

        Assert.Contains("FifthWeekTransferAllowed", raised);
        Assert.Contains("StatusDetail", raised);
        Assert.Equal("max 5", vm.MaxTransferText);
        Assert.True(vm.Status.TransferOptionApplies);
        Assert.True(Saved().FifthWeekTransferAllowed);
        Assert.True(NewVm(Dec18).FifthWeekTransferAllowed);

        vm.FifthWeekTransferAllowed = false;
        Assert.Equal("max 0", vm.MaxTransferText);
        Assert.False(Saved().FifthWeekTransferAllowed);
    }

    [Fact]
    public void TransferredToNext_IsCappedAtUnusedFifthWeekDays()
    {
        // 20 used -> 5 unused 5th-week days. Typing 10 shows (applies) 5.
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));

        vm.TransferredToNext = 10;

        Assert.Equal(5m, vm.TransferredToNext);
        Assert.Equal(BalanceStatus.AllPlanned, vm.Status.Status);
    }

    [Fact]
    public void TransferredToNext_TypingTheAppliedValueAfterAnOverCap_StoresWhatWasLastTyped()
    {
        // 5 unused 5th-week days. The user types 10 (shown as 5), then types 5. The registered transfer must now be
        // 5; if the over-cap 10 stays in the file, deleting a booking later would silently raise the transfer to 6+.
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));
        vm.TransferredToNext = 10;
        Assert.Equal(5m, vm.TransferredToNext);

        vm.TransferredToNext = 5;

        Assert.True(Saved().HolidayYears.Single().TransferredToNext <= 5m,
            "stored value is " + Saved().HolidayYears.Single().TransferredToNext);
    }

    [Fact]
    public void TransferredToNext_IsSavedToTheHolidayYearExpiringInTheSelectedYear_AndReloaded()
    {
        // Selected 2026 -> holiday year 2025/26, whose StartYear is 2025.
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));

        vm.TransferredToNext = 3;

        Assert.Equal(3m, vm.TransferredToNext);
        var stored = Assert.Single(Saved().HolidayYears);
        Assert.Equal(2025, stored.StartYear);
        Assert.Equal(3m, stored.TransferredToNext);
        Assert.Equal(3m, NewVm(Dec18).TransferredToNext);
    }

    [Fact]
    public void TransferredToNext_AddsDaysToTheNextHolidayYear()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));

        vm.TransferredToNext = 3;

        Assert.Equal(3m, vm.HolidayYearRows[1].TransferredIn);
        Assert.Equal(9.25m, vm.HolidayYearRows[1].Remaining); // 6.25 earned so far + 3 transferred in, nothing used
    }

    [Fact]
    public void TransferredToNext_ForAnotherSelectedYear_WritesThatYearsPrecedingHolidayYear()
    {
        // Selected 2027 -> holiday year 2026/27 (StartYear 2026), nothing booked: 25 unused, 5 of them 5th week.
        var vm = NewVm(Dec18, Seed(0, transferAllowed: true));
        vm.SelectedYear = 2027;
        Assert.Equal("max 5", vm.MaxTransferText);

        vm.TransferredToNext = 2;

        var stored = Assert.Single(Saved().HolidayYears);
        Assert.Equal(2026, stored.StartYear);
        Assert.Equal(2m, stored.TransferredToNext);
    }

    [Fact]
    public void TransferredToNext_Negative_BecomesZero()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true, transferred: 3));

        vm.TransferredToNext = -4;

        Assert.Equal(0m, vm.TransferredToNext);
        Assert.Equal(0m, Saved().HolidayYears.Single().TransferredToNext);
    }

    [Fact]
    public void TransferredToNext_Decimal_IsAccepted()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));

        vm.TransferredToNext = 2.5m;

        Assert.Equal(2.5m, vm.TransferredToNext);
    }

    [Fact]
    public void TransferredToNext_SettingOff_ShowsZero_AndStoredValueIsKeptWhenSwitchedOff()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true, transferred: 3));
        Assert.Equal(3m, vm.TransferredToNext);

        vm.FifthWeekTransferAllowed = false;

        Assert.Equal(0m, vm.TransferredToNext);
        // Not destroyed: switching the setting back on restores the registered transfer.
        Assert.Equal(3m, Saved().HolidayYears.Single().TransferredToNext);
        vm.FifthWeekTransferAllowed = true;
        Assert.Equal(3m, vm.TransferredToNext);
    }

    [Fact]
    public void TransferredToNext_Change_RaisesStatusProperties()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.TransferredToNext = 5;

        Assert.Contains("TransferredToNext", raised);
        Assert.Contains("StatusMessage", raised);
        Assert.Contains("UnbookedSign", raised);
    }

    // ---- Data written by the previous version ----

    [Fact]
    public void OldDataFile_WithAllowanceAndCarryOver_LoadsWithoutLosingBookingsOrHolidays()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(DataFile, """
            {
              "Days": [ { "Date": "2026-12-21", "Type": "Vacation", "Amount": 1, "Description": "x" } ],
              "Years": [ { "Year": 2026, "AllowanceDays": 20, "CarryOverDays": 3, "PublicHolidays": [ "2026-12-24" ] } ]
            }
            """);

        var vm = NewVm(Dec18);

        Assert.Single(vm.Entries);
        Assert.Equal(new DateOnly(2026, 12, 24), Assert.Single(vm.Holidays));
        Assert.Null(vm.EmployedSince);
        Assert.False(vm.FifthWeekTransferAllowed);

        // An edit saves, and the bookings and holidays are still there afterwards.
        vm.FifthWeekTransferAllowed = true;
        var saved = Saved();
        Assert.Single(saved.Days);
        Assert.Contains(new DateOnly(2026, 12, 24), saved.Years.Single(y => y.Year == 2026).PublicHolidays);
    }
}
