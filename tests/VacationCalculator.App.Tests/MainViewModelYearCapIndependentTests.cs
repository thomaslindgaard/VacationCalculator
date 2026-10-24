using System.Diagnostics;
using System.Globalization;
using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>One-year booking cap through the view model. Clock fixed at 2026-10-24.</summary>
public class MainViewModelYearCapIndependentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vc-cap-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "data.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private MainViewModel NewVm() => new(new DataStore(FilePath), () => new DateOnly(2026, 10, 24));

    private static void Inv(Action a)
    {
        var old = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try { a(); } finally { CultureInfo.CurrentCulture = old; }
    }

    private static void Set(MainViewModel vm, DateTime s, DateTime e)
    {
        vm.NewStart = s; // setting the start may move the end, so set the end afterwards
        vm.NewEnd = e;
    }

    [Fact]
    public void TooLong_ShowsMessage_AndSavesNothing() => Inv(() =>
    {
        var vm = NewVm();
        // Start 2026-03-15: latest end is 2027-03-14; 2027-03-15 is one day too far.
        Set(vm, new DateTime(2026, 3, 15), new DateTime(2027, 3, 15));
        var before = File.Exists(FilePath) ? File.ReadAllText(FilePath) : null;

        vm.AddEntryCommand.Execute(null);

        Assert.Equal("A booking can cover at most one year. The latest end date for a booking starting 15 March 2026 is 14 March 2027.",
            vm.ErrorMessage);
        Assert.Equal(before, File.Exists(FilePath) ? File.ReadAllText(FilePath) : null);
        Assert.Empty(vm.Entries);
        Assert.NotNull(vm.NewStart); // form kept so the user can fix the end date
    });

    [Fact]
    public void LeapStart_ErrorNamesFeb28() => Inv(() =>
    {
        var vm = NewVm();
        Set(vm, new DateTime(2028, 2, 29), new DateTime(2029, 3, 1));
        vm.AddEntryCommand.Execute(null);
        Assert.Contains("starting 29 February 2028 is 28 February 2029.", vm.ErrorMessage);
    });

    [Fact]
    public void ExactlyOneYear_IsAccepted_AndSaved()
    {
        var vm = NewVm();
        Set(vm, new DateTime(2026, 1, 31), new DateTime(2027, 1, 30));
        vm.AddEntryCommand.Execute(null);
        Assert.Equal("", vm.ErrorMessage);
        Assert.NotEmpty(new DataStore(FilePath).Load().Days);
    }

    [Fact]
    public void Year9999_FullYear_Accepted()
    {
        var vm = NewVm();
        Set(vm, new DateTime(9999, 1, 1), new DateTime(9999, 12, 31));
        vm.AddEntryCommand.Execute(null);
        Assert.Equal("", vm.ErrorMessage);
        // 9999 starts and ends on a Friday: 261 weekdays. Minus 8 weekday holidays: Fri 1 Jan, Fri 24 Dec, Fri 31 Dec
        // (25/26 Dec are Sat/Sun) and the 5 weekday Easter holidays (Thu, Fri, Mon, Thu, Mon) = 253.
        Assert.Equal(253, new DataStore(FilePath).Load().Days.Count);
    }

    [Fact]
    public void HalfDay_SingleDay_Unaffected()
    {
        var vm = NewVm();
        Set(vm, new DateTime(2026, 11, 2), new DateTime(2026, 11, 2)); // Monday
        vm.IsHalfDay = true;
        vm.AddEntryCommand.Execute(null);
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal(0.5m, Assert.Single(new DataStore(FilePath).Load().Days).Amount);
    }

    [Fact]
    public void HalfDayTicked_ButRangeTooLong_StillRefused() => Inv(() =>
    {
        var vm = NewVm();
        Set(vm, new DateTime(2026, 1, 1), new DateTime(2027, 1, 1));
        vm.IsHalfDay = true; // may be ignored/unavailable for ranges; either way nothing is booked
        vm.AddEntryCommand.Execute(null);
        Assert.Contains("at most one year", vm.ErrorMessage);
        Assert.False(File.Exists(FilePath));
    });

    [Fact]
    public void Order_MissingDates_First()
    {
        var vm = NewVm();
        vm.AddEntryCommand.Execute(null);
        Assert.Equal("Pick a start and end date.", vm.ErrorMessage);
    }

    [Fact]
    public void Order_EndBeforeStart_BeatsLimit()
    {
        var vm = NewVm();
        vm.NewStart = new DateTime(2030, 1, 1);
        vm.NewEnd = new DateTime(2020, 1, 1); // 10 years apart, but reversed
        vm.AddEntryCommand.Execute(null);
        Assert.Equal("The end date must be on or after the start date.", vm.ErrorMessage);
    }

    [Fact]
    public void Order_LimitBeatsNoWorkingDays_ForShortWeekendStillNoWorkingDays()
    {
        var vm = NewVm();
        Set(vm, new DateTime(2026, 11, 7), new DateTime(2026, 11, 8)); // Sat-Sun, within limit
        vm.AddEntryCommand.Execute(null);
        Assert.Equal("That period has no working days in it.", vm.ErrorMessage);
    }

    [Fact]
    public void Preview_TooLong_And_Valid()
    {
        var vm = NewVm();
        Set(vm, new DateTime(2026, 3, 15), new DateTime(2027, 3, 15));
        Assert.Equal("More than one year", vm.NewEntryPreview);
        Set(vm, new DateTime(2026, 3, 15), new DateTime(2027, 3, 14));
        Assert.EndsWith("working day(s)", vm.NewEntryPreview);
        // Mon 2026-11-02 .. Fri 2026-11-06 = 5 days (no holidays entered)
        Set(vm, new DateTime(2026, 11, 2), new DateTime(2026, 11, 6));
        Assert.Equal("5 working day(s)", vm.NewEntryPreview);
    }

    [Fact]
    public void Preview_FullDateRange_IsFast()
    {
        var vm = NewVm();
        var sw = Stopwatch.StartNew();
        Set(vm, DateTime.MinValue.Date, DateTime.MaxValue.Date);
        var text = vm.NewEntryPreview;
        sw.Stop();
        Assert.Equal("More than one year", text);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"took {sw.ElapsedMilliseconds} ms");
    }
}
