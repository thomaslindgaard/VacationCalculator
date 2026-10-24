using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>
/// Extreme-date regression: From = To = 31-12-9999 (a Friday, DateOnly.MaxValue) must not crash the app.
/// Clock fixed at 2030-07-10.
/// </summary>
public class MainViewModelMaxDateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vc-vm-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private MainViewModel NewVm() =>
        new(new DataStore(Path.Combine(_dir, "data.json")), () => new DateOnly(2030, 7, 10));

    private VacationData Saved() => new DataStore(Path.Combine(_dir, "data.json")).Load();

    [Fact]
    public void AddingMaxDate_DoesNotThrow_AndBooksTheDay()
    {
        var vm = NewVm();
        vm.NewStart = new DateTime(9999, 12, 31);
        vm.NewEnd = new DateTime(9999, 12, 31);

        vm.AddEntryCommand.Execute(null);

        Assert.Equal("", vm.ErrorMessage);
        // Fri 9999-12-31 is a working day, so it is booked; it belongs to year 9999, not 2030.
        Assert.Empty(vm.Entries);
        var day = Assert.Single(Saved().Days);
        Assert.Equal(DateOnly.MaxValue, day.Date);
        Assert.Equal(1m, day.Amount);
        // Form is cleared after a successful add.
        Assert.Null(vm.NewStart);
    }

    [Fact]
    public void AddingMaxDateAsHalfDay_DoesNotThrow_AndBooksHalf()
    {
        var vm = NewVm();
        vm.NewStart = new DateTime(9999, 12, 31);
        vm.NewEnd = new DateTime(9999, 12, 31);
        vm.IsHalfDay = true;

        vm.AddEntryCommand.Execute(null);
        Assert.Equal(0.5m, Assert.Single(Saved().Days).Amount);
    }

    [Fact]
    public void AddingMaxDate_IsPersisted()
    {
        var vm = NewVm();
        vm.NewStart = new DateTime(9999, 12, 31);
        vm.NewEnd = new DateTime(9999, 12, 31);
        vm.AddEntryCommand.Execute(null);

        Assert.Equal(DateOnly.MaxValue, Assert.Single(Saved().Days).Date);
        // A fresh view model on the saved file starts without throwing.
        var again = NewVm();
        Assert.Empty(again.Entries);
    }
}

