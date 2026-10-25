using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>Year navigation clamp: selectable calendar years are 2..9998 (holiday years Y-1 and Y must be 1..9998).</summary>
public class MainViewModelYearRangeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vc-vm-" + Guid.NewGuid().ToString("N"));
    private string DataFile => Path.Combine(_dir, "data.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private MainViewModel NewVm(DateOnly? today = null) =>
        new(new DataStore(DataFile), () => today ?? new DateOnly(2030, 7, 10));

    private static void AssertComputed(MainViewModel vm)
    {
        Assert.NotNull(vm.Status);
        Assert.Equal(2, vm.SummaryRows.Count);
        Assert.False(string.IsNullOrEmpty(vm.HolidayYearTitle));
        Assert.False(string.IsNullOrEmpty(vm.DeadlineSettingsTitle));
        _ = vm.StatusMessage;
        _ = vm.StatusDetail;
        _ = vm.FirstTrackedChoices;
        _ = vm.TransferLimitSource;
        _ = vm.PayoutSource;
    }

    [Theory]
    [InlineData(9999, 9998)]
    [InlineData(int.MaxValue, 9998)]
    [InlineData(10000, 9998)]
    [InlineData(1, 2)]
    [InlineData(0, 2)]
    [InlineData(-1, 2)]
    [InlineData(int.MinValue, 2)]
    public void OutOfRangeSelectedYear_IsClamped(int requested, int expected)
    {
        var vm = NewVm();
        vm.SelectedYear = requested;
        Assert.Equal(expected, vm.SelectedYear);
        AssertComputed(vm);
        Assert.Equal(expected, vm.Status.Year);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(9998)]
    [InlineData(3)]
    [InlineData(9997)]
    public void EdgeYears_WorkEndToEnd_WithAndWithoutTransfer(int year)
    {
        foreach (var limit in new[] { 0m, 5m }) // 0 = no transfer (formerly the switch off)
        {
            var vm = NewVm();
            vm.SelectedYear = year;
            vm.TransferLimitDays = limit;
            Assert.Equal(year, vm.SelectedYear);
            AssertComputed(vm);
            // Rows are holiday years Y-1 and Y.
            Assert.Equal(new HolidayYear(year - 1).ToString(), vm.SummaryRows[0].Name);
            Assert.Equal(new HolidayYear(year).ToString(), vm.SummaryRows[1].Name);
            Assert.Equal(new HolidayYear(year - 1), vm.Status.HolidayYear.HolidayYear);
            // "yyyy" zero-pads the absurd edge years: 31 December 0002.
            Assert.Contains($"31 December {year:D4}", vm.HolidayYearTitle);
            // Deadline settings can be changed in these years too.
            vm.TransferLimitDays = 3m;
            vm.PayoutAll = true;
            AssertComputed(vm);
            Assert.True(vm.PayoutAll);
            vm.PayoutSome = true;
            vm.PayoutDays = 1m;
            vm.ResetTransferLimitCommand.Execute(null);
            vm.ResetPayoutCommand.Execute(null);
            AssertComputed(vm);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(9998)]
    public void EdgeYears_WithBookingsInThem(int year)
    {
        var vm = NewVm();
        vm.SelectedYear = year;
        // Book Mon..Fri in that year's first week of March (some such weekday range exists every year).
        var monday = new DateTime(year, 3, 1);
        while (monday.DayOfWeek != DayOfWeek.Monday) monday = monday.AddDays(1);
        vm.NewStart = monday;
        vm.NewEnd = monday.AddDays(4);
        vm.AddEntryCommand.Execute(null);
        Assert.Equal("", vm.ErrorMessage);
        Assert.Single(vm.Entries);
        Assert.Equal(5m, vm.Entries[0].Period.Amount);
        AssertComputed(vm);

        // Also a booking in Dec of the previous calendar year (holiday year Y-1) and holiday add/remove.
        vm.NewHoliday = new DateTime(year, 5, 1);
        vm.AddHolidayCommand.Execute(null);
        Assert.Single(vm.Holidays);
        vm.RemoveHolidayCommand.Execute(vm.Holidays[0]);
        Assert.Empty(vm.Holidays);

        vm.SelectedYear = year; // no-op
        var reloaded = NewVm();
        reloaded.SelectedYear = year;
        Assert.Single(reloaded.Entries);
        AssertComputed(reloaded);
    }

    [Fact]
    public void BookingInPreviousYearOfLowestYear_IsCountedWithoutCrash()
    {
        var vm = NewVm();
        vm.SelectedYear = 2;
        vm.NewStart = new DateTime(1, 12, 3);
        vm.NewEnd = new DateTime(1, 12, 3);
        vm.AddEntryCommand.Execute(null);
        AssertComputed(vm);
        vm.NewStart = new DateTime(1, 1, 3);
        vm.NewEnd = new DateTime(1, 1, 3);
        vm.AddEntryCommand.Execute(null); // year 1 -> holiday year 0 (unsupported); must not crash
        AssertComputed(vm);
        AssertComputed(NewVm());
    }

    [Fact]
    public void NextAtMax_AndPreviousAtMin_CannotExecute()
    {
        var vm = NewVm();
        vm.SelectedYear = 9998;
        Assert.False(vm.NextYearCommand.CanExecute(null));
        Assert.True(vm.PreviousYearCommand.CanExecute(null));
        vm.NextYearCommand.Execute(null); // forced execute must still clamp
        Assert.Equal(9998, vm.SelectedYear);

        vm.SelectedYear = 2;
        Assert.False(vm.PreviousYearCommand.CanExecute(null));
        Assert.True(vm.NextYearCommand.CanExecute(null));
        vm.PreviousYearCommand.Execute(null);
        Assert.Equal(2, vm.SelectedYear);

        vm.SelectedYear = 3;
        Assert.True(vm.PreviousYearCommand.CanExecute(null));
        Assert.True(vm.NextYearCommand.CanExecute(null));
    }

    [Fact]
    public void CanExecuteChanged_FiresWhenMovingIntoAndOutOfEnds()
    {
        var vm = NewVm();
        vm.SelectedYear = 9997;
        int next = 0, prev = 0;
        vm.NextYearCommand.CanExecuteChanged += (_, _) => next++;
        vm.PreviousYearCommand.CanExecuteChanged += (_, _) => prev++;

        vm.NextYearCommand.Execute(null); // 9998: now disabled
        Assert.Equal(9998, vm.SelectedYear);
        Assert.True(next >= 1);
        Assert.False(vm.NextYearCommand.CanExecute(null));
        var nextBefore = next;
        vm.PreviousYearCommand.Execute(null); // 9997: re-enabled
        Assert.True(next > nextBefore);
        Assert.True(vm.NextYearCommand.CanExecute(null));

        vm.SelectedYear = 3;
        int prevBefore = prev;
        vm.PreviousYearCommand.Execute(null); // 2: disabled
        Assert.True(prev > prevBefore);
        Assert.False(vm.PreviousYearCommand.CanExecute(null));
        prevBefore = prev;
        vm.NextYearCommand.Execute(null);
        Assert.True(prev > prevBefore);
        Assert.True(vm.PreviousYearCommand.CanExecute(null));
    }

    [Fact]
    public void ClampedAssignment_AtEnd_RaisesCanExecuteChanged()
    {
        var vm = NewVm();
        int next = 0;
        vm.NextYearCommand.CanExecuteChanged += (_, _) => next++;
        vm.SelectedYear = int.MaxValue;
        Assert.True(next >= 1);
        Assert.False(vm.NextYearCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(9999, 6, 1)]
    [InlineData(9999, 10, 1)]
    [InlineData(9999, 12, 31)]
    [InlineData(9998, 12, 31)]
    [InlineData(1, 3, 1)]
    [InlineData(1, 9, 1)]
    [InlineData(1, 1, 1)]
    [InlineData(2, 3, 1)]
    public void ExtremeClock_DoesNotCrashConstructor_OrFirstTrackedDropdown(int y, int m, int d)
    {
        var vm = NewVm(new DateOnly(y, m, d));
        Assert.InRange(vm.SelectedYear, MainViewModel.MinYear, MainViewModel.MaxYear);
        AssertComputed(vm);
        var choices = vm.FirstTrackedChoices;
        Assert.NotEmpty(choices);
        Assert.Contains(vm.FirstTrackedHolidayYear, choices);
        // Interacting with the setting at this clock must also be safe.
        vm.TransferLimitDays = 3;
        AssertComputed(vm);
        vm.CurrentYearCommand.Execute(null);
        Assert.InRange(vm.SelectedYear, MainViewModel.MinYear, MainViewModel.MaxYear);
        AssertComputed(vm);
        vm.PreviousYearCommand.Execute(null);
        vm.NextYearCommand.Execute(null);
        AssertComputed(vm);
    }

    [Fact]
    public void Constructor_ClampsInitialYear_ToRange()
    {
        Assert.Equal(9998, NewVm(new DateOnly(9999, 6, 1)).SelectedYear);
        Assert.Equal(2, NewVm(new DateOnly(1, 3, 1)).SelectedYear);
    }

    [Fact]
    public void Core_RangeIsExactlyTwoToNinetyNineNinetyEight()
    {
        var data = new VacationData();
        foreach (var year in new[] { 2, 9998 })
        {
            var s = BalanceCalculator.CalculateCalendarYear(data, year, new DateOnly(2030, 7, 10));
            Assert.Equal(year, s.Year);
            BalanceCalculator.CalculateHolidayYears(data, HolidayYear.InCalendarYear(year), new DateOnly(2030, 7, 10));
        }
        // Calendar year 1 needs holiday year 0 (Start = 0000-09-01) and 9999 needs holiday year 9999 (Deadline 10000-12-31).
        Assert.ThrowsAny<Exception>(() => new HolidayYear(0).Start);
        Assert.ThrowsAny<Exception>(() => new HolidayYear(9999).Deadline);
        Assert.ThrowsAny<Exception>(() =>
        {
            var s = BalanceCalculator.CalculateCalendarYear(data, 1, new DateOnly(2030, 7, 10));
            BalanceCalculator.CalculateHolidayYears(data, HolidayYear.InCalendarYear(1), new DateOnly(2030, 7, 10));
        });
        Assert.ThrowsAny<Exception>(() =>
        {
            BalanceCalculator.CalculateCalendarYear(data, 9999, new DateOnly(2030, 7, 10));
            BalanceCalculator.CalculateHolidayYears(data, HolidayYear.InCalendarYear(9999), new DateOnly(2030, 7, 10));
        });
    }
}
