using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>Employed since and the holiday-year card (settings for transfer and payout: MainViewModelDeadlineSettingsTests).</summary>
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
        Assert.Equal(2, vm.SummaryRows.Count);
        var old = vm.SummaryRows[0];
        Assert.Equal("2025/26", old.Name);
        Assert.Equal("25 of 25", old.EarnedTooltip);
        Assert.Equal(20m, old.Used);
        Assert.Equal(0m, old.BookedAhead);
        Assert.Equal(5m, old.Remaining);
        Assert.Equal(1, old.RemainingSign);
        var next = vm.SummaryRows[1];
        Assert.Equal("2026/27", next.Name);
        Assert.Equal("6.25 of 25", next.EarnedTooltip);
        Assert.Equal(6.25m, next.Remaining);
    }

    [Fact]
    public void Title_AndRows_FollowSelectedYear()
    {
        var vm = NewVm(Dec18);
        vm.SelectedYear = 2027;

        Assert.Equal("Holiday year 2026/27 · use by 31 December 2027", vm.HolidayYearTitle);
        Assert.Equal(["2026/27", "2027/28"], vm.SummaryRows.Select(r => r.Name));
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

        var next = vm.SummaryRows[1];
        Assert.Equal(3m, next.BookedAhead);
        Assert.Equal(3.25m, next.Remaining);
        Assert.Null(next.RemainingTooltip);
        Assert.Equal(25m, vm.SummaryRows[0].Used);
    }

    [Fact]
    public void Rows_AdvanceHoliday_IsFlaggedAndNegative()
    {
        // Mon-Wed 12-21..23 and Mon-Wed 12-28..30 (6 days; 24, 25 and 31 Dec are public holidays and 26-27 a weekend)
        // plus Mon 2027-01-04 = 7 working days, all in 2026/27: 6.25 earned so far - 7 = -0.75.
        var data = Seed(25);
        foreach (var day in new[] { 21, 22, 23, 28, 29, 30 })
            data.Days.Add(new BookedDay { Date = new DateOnly(2026, 12, day) });
        data.Days.Add(new BookedDay { Date = new DateOnly(2027, 1, 4) });
        var vm = NewVm(Dec18, data);

        var next = vm.SummaryRows[1];
        Assert.Equal(-0.75m, next.Remaining);
        Assert.Equal(-1, next.RemainingSign);
        Assert.Equal("advance holiday: 0.75 not earned yet", next.RemainingTooltip);
    }

    [Fact]
    public void Rows_BookedBeyondWhatCanBeEarned_IsFlaggedAsError()
    {
        // 26 booked days in 2025/26 (25 earnable) -> 1 more than can be earned by 31 August.
        var vm = NewVm(Dec18, Seed(26));

        var row = vm.SummaryRows[0];
        Assert.Equal("1 more than can be earned by 31 August", row.RemainingTooltip);
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

    // ---- Card: transfer columns ----

    [Fact]
    public void Rows_TransferOn_ShowTransferredIn()
    {
        // 20 used, transfer on: 5 unused 5th-week days are transferred from 2025/26 to 2026/27.
        // 2026/27: 6.25 earned so far (Sep-Nov). The 5 transferred days are only projected on Dec 18 (2025/26 is
        // still open and still counts them as its own 5 remaining), so they are not in 2026/27's Remaining yet:
        // 6.25 + 5 (in 2025/26) = 11.25 days that really exist, not 16.25.
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));

        Assert.Equal(0m, vm.SummaryRows[0].TransferredIn);
        Assert.Equal(5m, vm.SummaryRows[1].TransferredIn);
        Assert.True(vm.SummaryRows[1].TransferredInIsProjected);
        Assert.Equal(6.25m, vm.SummaryRows[1].Remaining);
        Assert.Equal(11.25m, vm.SummaryRows[0].Remaining + vm.SummaryRows[1].Remaining);
        // The projected transfer does not reduce what is still bookable in 2025/26.
        Assert.Equal(5m, vm.SummaryRows[0].Remaining);
    }

    [Fact]
    public void Rows_TransferOff_ShowNoTransfers()
    {
        var vm = NewVm(Dec18, Seed(20));

        Assert.All(vm.SummaryRows, r => Assert.Equal(0m, r.TransferredIn));
    }

    [Fact]
    public void Rows_TransferredIn_FollowsThePerYearLimit()
    {
        // Limit 3 for 2025/26: 3 of the 5 unused days are transferred.
        var data = Seed(20, transferAllowed: true);
        data.SetTransferLimit(new HolidayYear(2025), 3);
        var vm = NewVm(Dec18, data);

        Assert.Equal(3m, vm.SummaryRows[1].TransferredIn);
    }

    [Fact]
    public void Rows_SeptemberToDecember_BookingsUseTheOlderYearFirst_ReducingTheTransfer()
    {
        // 20 used in Jan; booking Mon 12-21 and Tue 12-22 (after today 12-18) use 2026's older year first:
        // 2025/26 has 5 left -> 2 more booked -> 3 unused -> only 3 transferred.
        var data = Seed(20, transferAllowed: true);
        foreach (var day in new[] { 21, 22 })
            data.Days.Add(new BookedDay { Date = new DateOnly(2026, 12, day) });
        var vm = NewVm(Dec18, data);

        Assert.Equal(2m, vm.SummaryRows[0].BookedAhead);
        Assert.Equal(3m, vm.SummaryRows[1].TransferredIn);
        Assert.Equal(0m, vm.SummaryRows[1].BookedAhead);
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
        Assert.Equal(5m, vm.TransferLimitDays); // default

        // An edit saves, and the bookings and holidays are still there afterwards.
        vm.TransferLimitDays = 3;
        var saved = Saved();
        Assert.Single(saved.Days);
        Assert.Contains(new DateOnly(2026, 12, 24), saved.Years.Single(y => y.Year == 2026).PublicHolidays);
    }
}
