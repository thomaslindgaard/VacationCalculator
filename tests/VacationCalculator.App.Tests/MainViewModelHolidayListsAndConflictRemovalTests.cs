using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>
/// Feature 4 fix round: the two sidebar lists, the not-counted tag on 0-day rows, and "Remove these days".
/// Clock Fri 2026-12-18. Calendar: Wed 2026-12-23, Thu 12-24 (Juleaftensdag), Fri 12-25 (Juledag), Sat 12-26,
/// Mon 12-28, Fri 2027-01-01 (Nytaarsdag), Fri 2026-06-05 (not a Danish holiday), Fri 2027-06-04 (not a holiday).
/// </summary>
public class MainViewModelHolidayListsAndConflictRemovalTests : HolidayYearVmTestBase, IDisposable
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);
    private VacationData Saved() => new DataStore(DataFile).Load();

    private static BookedDay Day(DateOnly date, string desc = "", decimal amount = 1, DayType type = DayType.Vacation) =>
        new() { Date = date, Description = desc, Amount = amount, Type = type };

    private static VacationData With(params BookedDay[] days)
    {
        var data = Seed(0);
        data.Days.AddRange(days);
        return data;
    }

    private void ReadOnly(bool on)
    {
        var attrs = File.GetAttributes(DataFile);
        File.SetAttributes(DataFile, on ? attrs | FileAttributes.ReadOnly : attrs & ~FileAttributes.ReadOnly);
    }

    public new void Dispose()
    {
        if (File.Exists(DataFile))
            ReadOnly(false);
        base.Dispose();
    }

    // ---- The two lists ----

    [Fact]
    public void FreshStart_NoManual_TwelveAutomatic_HasNoManualTrue()
    {
        var vm = NewVm(Dec18);

        Assert.Empty(vm.ManualHolidayRows);
        Assert.True(vm.HasNoManualHolidays);
        Assert.Equal(12, vm.AutomaticHolidayRows.Count);
        Assert.All(vm.AutomaticHolidayRows, r => { Assert.True(r.IsAutomatic); Assert.True(r.HasName); });
        Assert.Equal(vm.AutomaticHolidayRows.Select(r => r.Date).Order(), vm.AutomaticHolidayRows.Select(r => r.Date));
    }

    [Fact]
    public void ManualOnly_AppearsOnlyInManual_HasNoManualFalse_AndAutomaticUnchanged()
    {
        var vm = NewVm(Dec18);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.NewHoliday = new DateTime(2026, 6, 5);
        vm.AddHolidayCommand.Execute(null);

        var row = Assert.Single(vm.ManualHolidayRows);
        Assert.Equal(D(2026, 6, 5), row.Date);
        Assert.True(row.CanRemove);
        Assert.False(row.HasName);
        Assert.False(vm.HasNoManualHolidays);
        Assert.Equal(12, vm.AutomaticHolidayRows.Count);
        Assert.DoesNotContain(vm.AutomaticHolidayRows, r => r.Date == D(2026, 6, 5));
        Assert.Contains(nameof(MainViewModel.HasNoManualHolidays), raised);
    }

    [Fact]
    public void ManualRows_AreInDateOrder_AndBothDatesAreInBothLists_WithName()
    {
        // Manual: Fri 2026-12-24 (also Danish, Juleaftensdag), Fri 2026-06-05, Thu 2026-12-31 (also Danish).
        var data = Seed(0).WithManualHoliday(D(2026, 12, 31)).WithManualHoliday(D(2026, 6, 5)).WithManualHoliday(D(2026, 12, 24));
        var vm = NewVm(Dec18, data);

        Assert.Equal([D(2026, 6, 5), D(2026, 12, 24), D(2026, 12, 31)], vm.ManualHolidayRows.Select(r => r.Date));
        Assert.Equal("", vm.ManualHolidayRows[0].Name);
        Assert.Equal("Juleaftensdag", vm.ManualHolidayRows[1].Name);
        Assert.Equal("Nytårsaftensdag", vm.ManualHolidayRows[2].Name);
        Assert.All(vm.ManualHolidayRows, r => Assert.True(r.CanRemove));
        // Both-dates also stay in the automatic list (removable only through the manual list)
        Assert.Equal(12, vm.AutomaticHolidayRows.Count);
        Assert.Contains(vm.AutomaticHolidayRows, r => r.Date == D(2026, 12, 24));
        Assert.Contains(vm.AutomaticHolidayRows, r => r.Date == D(2026, 12, 31));
        Assert.DoesNotContain(vm.AutomaticHolidayRows, r => r.Date == D(2026, 6, 5));
        Assert.All(vm.AutomaticHolidayRows.Where(r => r.Date.Month == 12 && r.Date.Day >= 24), r => Assert.True(r.IsAutomatic));
    }

    [Fact]
    public void RemovingABothDate_FromManualList_KeepsItAutomatic_AndEmptiesManual()
    {
        var vm = NewVm(Dec18, Seed(0).WithManualHoliday(D(2026, 12, 24)));
        Assert.Single(vm.ManualHolidayRows);
        Assert.False(vm.HasNoManualHolidays);

        vm.RemoveHolidayCommand.Execute(vm.ManualHolidayRows[0].Date);

        Assert.Empty(vm.ManualHolidayRows);
        Assert.True(vm.HasNoManualHolidays);
        Assert.Equal(12, vm.AutomaticHolidayRows.Count);
        var auto = vm.AutomaticHolidayRows.Single(r => r.Date == D(2026, 12, 24));
        Assert.False(auto.CanRemove);
        Assert.Empty(Saved().Years.Single(y => y.Year == 2026).PublicHolidays);
    }

    [Fact]
    public void ManualRemoval_OfOnlyOneOfTwoManualDates_LeavesTheOther()
    {
        var vm = NewVm(Dec18, Seed(0).WithManualHoliday(D(2026, 6, 5)).WithManualHoliday(D(2026, 12, 24)));

        vm.RemoveHolidayCommand.Execute(D(2026, 6, 5));

        Assert.Equal([D(2026, 12, 24)], vm.ManualHolidayRows.Select(r => r.Date));
        Assert.False(vm.HasNoManualHolidays);
    }

    [Fact]
    public void Lists_FollowTheSelectedYear()
    {
        var data = Seed(0).WithManualHoliday(D(2026, 6, 5)).WithManualHoliday(D(2027, 6, 4));
        var vm = NewVm(Dec18, data);
        Assert.Equal([D(2026, 6, 5)], vm.ManualHolidayRows.Select(r => r.Date));

        vm.SelectedYear = 2027;

        Assert.Equal([D(2027, 6, 4)], vm.ManualHolidayRows.Select(r => r.Date));
        Assert.All(vm.AutomaticHolidayRows, r => Assert.Equal(2027, r.Date.Year));
        Assert.Equal(12, vm.AutomaticHolidayRows.Count);

        vm.SelectedYear = 2028; // no manual entries there
        Assert.Empty(vm.ManualHolidayRows);
        Assert.True(vm.HasNoManualHolidays);
    }

    // ---- 0-day rows ----

    [Fact]
    public void HolidayOnlyPeriod_IsNotCounted_WithTagAfterDescription()
    {
        var vm = NewVm(Dec18, With(Day(D(2026, 12, 24), "Christmas")));

        var row = Assert.Single(vm.Entries);
        Assert.Equal(0m, row.Period.Amount);
        Assert.True(row.IsNotCounted);
        Assert.Equal("  (public holiday – not counted)", row.NotCountedSuffix);
    }

    [Fact]
    public void HolidayOnlyPeriod_WithoutDescription_TagHasNoParentheses()
    {
        var vm = NewVm(Dec18, With(Day(D(2026, 12, 24))));

        var row = Assert.Single(vm.Entries);
        Assert.True(row.IsNotCounted);
        Assert.Equal("public holiday – not counted", row.NotCountedSuffix);
    }

    [Fact]
    public void NormalPeriod_IsCounted_NoTag()
    {
        var vm = NewVm(Dec18, With(Day(D(2026, 12, 23), "x")));

        var row = Assert.Single(vm.Entries);
        Assert.False(row.IsNotCounted);
        Assert.Equal("", row.NotCountedSuffix);
    }

    [Fact]
    public void PeriodWithOneHolidayInside_StillCountsItsWorkingDays_NoTag()
    {
        // Wed 23 + Thu 24 (holiday) + Mon 28, same description: one period, 2 counted days.
        var vm = NewVm(Dec18, With(Day(D(2026, 12, 23), "x"), Day(D(2026, 12, 24), "x"), Day(D(2026, 12, 28), "x")));

        var row = Assert.Single(vm.Entries);
        Assert.Equal(2m, row.Period.Amount);
        Assert.False(row.IsNotCounted);
    }

    [Fact]
    public void HalfDayOnHoliday_IsNotCounted()
    {
        var vm = NewVm(Dec18, With(Day(D(2026, 12, 25), "", 0.5m)));

        Assert.True(Assert.Single(vm.Entries).IsNotCounted);
    }

    // ---- RemoveConflictDaysCommand ----

    [Fact]
    public void RemoveConflict_RemovesExactlyTheHolidayRecords_KeepsTheRest_BalancesUnchanged_PeriodsRegroup()
    {
        // Wed 23 "x", Thu 24 "y" (holiday), Mon 28 "x": three periods; after removal Wed 23 and Mon 28 merge.
        var vm = NewVm(Dec18, With(Day(D(2026, 12, 23), "x"), Day(D(2026, 12, 24), "y"), Day(D(2026, 12, 28), "x")));
        Assert.Equal(3, vm.Entries.Count);
        var statusBefore = vm.Status;
        Assert.True(vm.HasConflictNote);

        vm.RemoveConflictDaysCommand.Execute(null);

        Assert.Equal("Removed 1 booked day on a public holiday.", vm.InfoMessage);
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal([D(2026, 12, 23), D(2026, 12, 28)], Saved().Days.Select(d => d.Date));
        Assert.Equal(statusBefore, vm.Status);
        var row = Assert.Single(vm.Entries);
        Assert.Equal(D(2026, 12, 23), row.Period.Start);
        Assert.Equal(D(2026, 12, 28), row.Period.End);
        Assert.Equal(2m, row.Period.Amount);
        Assert.Equal("", vm.ConflictNote);
        Assert.False(vm.HasConflictNote);
    }

    [Fact]
    public void RemoveConflict_Plural_IncludesHalfDaysAndFeriefri_AndKeepsOtherTypes()
    {
        var vm = NewVm(Dec18, With(
            Day(D(2026, 12, 23)),
            Day(D(2026, 12, 24)),
            Day(D(2026, 12, 25), "", 0.5m),
            Day(D(2026, 12, 31), "", 1, DayType.Feriefri),
            Day(D(2026, 12, 29), "", 1, DayType.Feriefri)));
        var statusBefore = vm.Status;

        vm.RemoveConflictDaysCommand.Execute(null);

        Assert.Equal("Removed 3 booked days on public holidays.", vm.InfoMessage);
        Assert.Equal(
            [(D(2026, 12, 23), DayType.Vacation), (D(2026, 12, 29), DayType.Feriefri)],
            Saved().Days.Select(d => (d.Date, d.Type)));
        Assert.Equal(statusBefore, vm.Status);
        Assert.Equal("", vm.ConflictNote);
    }

    [Fact]
    public void RemoveConflict_FollowsTheSelectedYear_OtherYearsConflictsStay()
    {
        // 2026: Thu 12-24 and Fri 12-25. 2027: Fri 01-01.
        var vm = NewVm(Dec18, With(Day(D(2026, 12, 24)), Day(D(2026, 12, 25)), Day(D(2027, 1, 1))));
        vm.SelectedYear = 2027;
        Assert.True(vm.HasConflictNote);

        vm.RemoveConflictDaysCommand.Execute(null);

        Assert.Equal("Removed 1 booked day on a public holiday.", vm.InfoMessage);
        Assert.Equal([D(2026, 12, 24), D(2026, 12, 25)], Saved().Days.Select(d => d.Date));
        Assert.Equal("", vm.ConflictNote);

        vm.SelectedYear = 2026;
        Assert.True(vm.HasConflictNote);
        vm.RemoveConflictDaysCommand.Execute(null);
        Assert.Equal("Removed 2 booked days on public holidays.", vm.InfoMessage);
        Assert.Empty(Saved().Days);
    }

    [Fact]
    public void RemoveConflict_WithNothingToRemove_ChangesNothing_AndWritesNoFile()
    {
        var vm = NewVm(Dec18);

        vm.RemoveConflictDaysCommand.Execute(null);

        Assert.Equal("", vm.InfoMessage);
        Assert.Equal("", vm.ErrorMessage);
        Assert.False(File.Exists(DataFile));
    }

    [Fact]
    public void RemoveConflict_AlsoRemovesRecordsOnAManualHoliday()
    {
        var vm = NewVm(Dec18, With(Day(D(2026, 6, 4)), Day(D(2026, 6, 5))).WithManualHoliday(D(2026, 6, 5)));
        Assert.True(vm.HasConflictNote);

        vm.RemoveConflictDaysCommand.Execute(null);

        Assert.Equal([D(2026, 6, 4)], Saved().Days.Select(d => d.Date));
        Assert.Equal([D(2026, 6, 5)], Saved().Years.Single().PublicHolidays); // the holiday itself stays
    }

    [Fact]
    public void RemoveConflict_FailedSave_RollsBack_ReportsError_ThenSucceedsOnceWritable()
    {
        var vm = NewVm(Dec18, With(Day(D(2026, 12, 23)), Day(D(2026, 12, 24)), Day(D(2026, 12, 25))));
        var noteBefore = vm.ConflictNote;
        var entriesBefore = vm.Entries.Select(e => (e.Period.Start, e.Period.End, e.Period.Amount)).ToList();
        var fileBefore = File.ReadAllBytes(DataFile);
        ReadOnly(true);

        vm.RemoveConflictDaysCommand.Execute(null);

        Assert.NotEqual("", vm.ErrorMessage);
        Assert.DoesNotContain("Removed", vm.InfoMessage);
        Assert.Equal(noteBefore, vm.ConflictNote);
        Assert.True(vm.HasConflictNote);
        Assert.Equal(entriesBefore, vm.Entries.Select(e => (e.Period.Start, e.Period.End, e.Period.Amount)).ToList());
        Assert.Equal(fileBefore, File.ReadAllBytes(DataFile));

        ReadOnly(false);
        vm.RemoveConflictDaysCommand.Execute(null);

        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal("Removed 2 booked days on public holidays.", vm.InfoMessage);
        Assert.Equal([D(2026, 12, 23)], Saved().Days.Select(d => d.Date));
    }
}
