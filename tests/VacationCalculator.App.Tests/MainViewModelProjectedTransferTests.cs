using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>Projected transfer in the holiday-year card, the deadline heading and clamped first-tracked values.</summary>
public class MainViewModelProjectedTransferTests : HolidayYearVmTestBase
{
    // Seed(20, transfer on): 2025/26 earns 25, 20 used in Jan 2026 -> 5 left, transfer limit 5 -> 5 transferred to 2026/27.
    // 2026/27 earns 25/12 per completed month from September: Sep-Nov = 3 months = 6.25.

    [Fact]
    public void Dec18_SumOfRemaining_IsTheDaysThatReallyExist()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));

        // Really existing: 25 - 20 = 5 in 2025/26, plus 6.25 earned in 2026/27 = 11.25 (the 5 are the same 5).
        Assert.Equal(5m, vm.HolidayYearRows[0].Remaining);
        Assert.Equal(6.25m, vm.HolidayYearRows[1].Remaining);
        Assert.Equal(11.25m, vm.HolidayYearRows.Sum(r => r.Remaining));
    }

    [Fact]
    public void Dec18_ProjectedRow_ShowsProjectedLabelAndTooltip()
    {
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));
        var row = vm.HolidayYearRows[1];

        Assert.True(row.TransferredInIsProjected);
        Assert.True(row.ShowProjected);
        Assert.False(vm.HolidayYearRows[0].ShowProjected);
        Assert.Contains("Projected transfer from 2025/26", row.ProjectedTooltip);
        Assert.Contains("31 December 2026", row.ProjectedTooltip);
    }

    [Fact]
    public void Dec31_StillProjected_NothingCountedTwice()
    {
        // 2026-12-31 is the deadline day itself: the days still count in 2025/26.
        var vm = NewVm(Dec31, Seed(20, transferAllowed: true));

        Assert.True(vm.HolidayYearRows[1].TransferredInIsProjected);
        Assert.Equal(5m, vm.HolidayYearRows[0].Remaining);
        // 2026/27 own days only (6.25 or 8.33 depending on whether December counts as completed): never 5 more.
        Assert.InRange(vm.HolidayYearRows[1].Remaining, 6.25m, 8.34m);
    }

    [Fact]
    public void Jan1_AfterDeadline_TransferIsActual_AndCountedOnce()
    {
        // Fri 2027-01-01: 2025/26 expired with 5 transferred. 2026/27 earned Sep-Dec = 4 * 25/12 = 8.33, + 5 = 13.33.
        var vm = NewVm(new DateOnly(2027, 1, 1), Seed(20, transferAllowed: true));
        var row = vm.HolidayYearRows.Single(r => r.Name == "2026/27");

        Assert.False(row.TransferredInIsProjected);
        Assert.False(row.ShowProjected);
        Assert.Equal(5m, row.TransferredIn);
        Assert.Equal(13.33m, row.Remaining);
    }

    [Fact]
    public void TransferOff_NeverProjected()
    {
        var vm = NewVm(Dec18, Seed(20));

        Assert.All(vm.HolidayYearRows, r => Assert.False(r.TransferredInIsProjected));
        Assert.All(vm.HolidayYearRows, r => Assert.False(r.ShowProjected));
    }

    [Fact]
    public void FirstTrackedYear_OpeningBalance_IsNotProjected()
    {
        var data = Seed(0);
        data.OpeningBalanceDays = 3;
        var vm = NewVm(Dec18, data);

        Assert.Equal(3m, vm.HolidayYearRows[0].TransferredIn);
        Assert.False(vm.HolidayYearRows[0].TransferredInIsProjected);
        Assert.False(vm.HolidayYearRows[0].ShowProjected);
        // Opening balance is actual: 25 + 3 - 0 = 28 remaining.
        Assert.Equal(28m, vm.HolidayYearRows[0].Remaining);
    }

    [Fact]
    public void ProjectedZeroTransfer_IsNotShownAsProjected()
    {
        // Transfer on but all 25 used: nothing to transfer, so no "(projected)" label.
        var vm = NewVm(Dec18, Seed(25, transferAllowed: true));

        Assert.Equal(0m, vm.HolidayYearRows[1].TransferredIn);
        Assert.False(vm.HolidayYearRows[1].ShowProjected);
    }

    [Fact]
    public void AdvanceHoliday_IsVisibleInTheNewerYear()
    {
        // 25 used in 2025/26 (Jan 5..Feb 6). Booking Mon 12-21 .. Wed 12-30 (8 weekdays: 21-25, 28-30) goes to 2026/27,
        // which has earned 6.25 so far: 6.25 - 8 = -1.75, advance holiday 1.75.
        var data = Seed(25);
        for (var d = new DateOnly(2026, 12, 21); d <= new DateOnly(2026, 12, 30); d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                data.Days.Add(new BookedDay { Date = d });
        var vm = NewVm(Dec18, data);
        var row = vm.HolidayYearRows[1];

        Assert.Equal(8m, row.BookedAhead);
        Assert.Equal(-1.75m, row.Remaining);
        Assert.Equal(-1, row.RemainingSign);
        Assert.Contains("advance holiday", row.Note);
        Assert.Contains("1.75", row.Note);
    }

    [Fact]
    public void Heading_NamesTheHolidayYearAndItsDeadline()
    {
        var vm = NewVm(Dec18, Seed(0));

        Assert.StartsWith("When 2025/26 expires (", vm.DeadlineSettingsTitle);
        Assert.Contains("31 December 2026", vm.DeadlineSettingsTitle);
        Assert.EndsWith(")", vm.DeadlineSettingsTitle);
    }

    [Fact]
    public void DeadlineSplit_Unchanged_ByTheProjectionFlag()
    {
        // 20 used, transfer on, limit 5, 5th week 5: unused 5 -> transferred 5, nothing paid out or lost.
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));

        Assert.Equal(5m, vm.HolidayYearRows[0].TransferredOut);
        Assert.Equal(5m, vm.Status.HolidayYear.TransferredToNext);
        Assert.Equal(0m, vm.Status.HolidayYear.PaidOutDays);
        Assert.Equal(0m, vm.Status.HolidayYear.LostDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-2026)]
    [InlineData(int.MinValue)]
    [InlineData(10000)]
    [InlineData(int.MaxValue)]
    public void ClampedFirstTracked_DoesNotCrash_AndDropdownShowsOneExtraItem(int stored)
    {
        // Normal choices at Dec18 2026: 2015/16 .. 2026/27 = 12 items. An out-of-range stored value adds exactly one.
        var vm = NewVm(Dec18, Seed(0, firstTracked: stored));

        var choices = vm.FirstTrackedChoices;
        Assert.Equal(13, choices.Count);
        Assert.Contains(vm.FirstTrackedHolidayYear, choices);
        Assert.NotEmpty(vm.HolidayYearRows);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
        Assert.True(vm.FirstTrackedHolidayYear.StartYear is >= 1 and <= 9998);
    }

    [Fact]
    public void NormalFirstTracked_DropdownHasNoExtraItem()
    {
        var vm = NewVm(Dec18, Seed(0));

        Assert.Equal(12, vm.FirstTrackedChoices.Count);
    }

    [Fact]
    public void ClampedFirstTracked_FileIsNotRewrittenWithClampedValue_OnLoad()
    {
        var vm = NewVm(Dec18, Seed(0, firstTracked: 10000));

        Assert.Equal(10000, new DataStore(DataFile).Load().FirstTrackedHolidayYear);
        _ = vm;
    }
}
