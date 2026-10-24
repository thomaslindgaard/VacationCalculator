using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>
/// A failed save (read-only data file) rolls the change back so the app shows what is saved, and says so.
/// Today is Fri 2026-12-18; the selected year is 2026, whose deadline year is holiday year 2025/26.
/// </summary>
public class MainViewModelFailedSaveTests : HolidayYearVmTestBase, IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Everything is set to a non-default value so any change and any rollback is visible.</summary>
    private static VacationData Rich()
    {
        // Past booking Mon 2026-01-05 .. Fri 01-09 (5 days) plus a future one Mon 12-21 .. Tue 12-22.
        var data = Seed(5, transferAllowed: true, employedSince: new DateOnly(2025, 1, 1));
        data.Days.Add(new BookedDay { Date = new DateOnly(2026, 12, 21), Description = "xmas" });
        data.Days.Add(new BookedDay { Date = new DateOnly(2026, 12, 22), Description = "xmas" });
        data.OpeningBalanceDays = 2;
        data.Years.Add(new YearSettings { Year = 2026, PublicHolidays = [new(2026, 12, 24), new(2026, 12, 25)] });
        data.HolidayYears.Add(new HolidayYearSettings
        {
            StartYear = 2025, TransferLimitDays = 8, PayoutMode = PayoutMode.Some, PayoutDays = 3,
        });
        return data;
    }

    private static string InMemory(MainViewModel vm)
    {
        var field = typeof(MainViewModel).GetField("_data", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return JsonSerializer.Serialize(field.GetValue(vm), Json);
    }

    /// <summary>Every bound value the requirement lists, as text.</summary>
    private static string State(MainViewModel vm) => string.Join("\n",
        "entries: " + string.Join(" | ", vm.Entries.Select(e =>
            $"{e.Period.Start}-{e.Period.End} {e.Period.Amount} {e.Period.Description} past={e.IsPast}")),
        "holidays: " + string.Join(",", vm.Holidays),
        $"employed={vm.EmployedSince:yyyy-MM-dd} first={vm.FirstTrackedHolidayYear.StartYear} opening={vm.OpeningBalanceDays}",
        "choices: " + string.Join(",", vm.FirstTrackedChoices.Select(c => c.StartYear)),
        $"limit={vm.TransferLimitDays} src={vm.TransferLimitSource} own={vm.TransferLimitIsOwn}",
        $"payout none={vm.PayoutNone} some={vm.PayoutSome} all={vm.PayoutAll} days={vm.PayoutDays} src={vm.PayoutSource} own={vm.PayoutIsOwn}",
        "rows: " + string.Join(" | ", vm.HolidayYearRows),
        "status: " + vm.Status,
        "msg: " + vm.StatusMessage + " / " + vm.StatusDetail,
        "earnable: " + vm.EarnableNote,
        "inmemory: " + InMemory(vm));

    private static string Form(MainViewModel vm) =>
        $"{vm.NewStart:yyyy-MM-dd}|{vm.NewEnd:yyyy-MM-dd}|{vm.NewDescription}|{vm.IsHalfDay}|{vm.NewHoliday:yyyy-MM-dd}";

    private void ReadOnly(bool on)
    {
        var attrs = File.GetAttributes(DataFile);
        File.SetAttributes(DataFile, on ? attrs | FileAttributes.ReadOnly : attrs & ~FileAttributes.ReadOnly);
    }

    private MainViewModel NewVmFresh() => new(new DataStore(Path.Combine(Dir, "fresh.json")), () => Dec18);

    public new void Dispose()
    {
        if (File.Exists(DataFile))
            ReadOnly(false); // else the base cleanup can't delete the temp folder
        base.Dispose();
    }

    public sealed record Case(string Name, Action<MainViewModel> Run, string? Property, bool KeepsForm = false)
    {
        public override string ToString() => Name;
    }

    public static IEnumerable<object[]> Cases() => new Case[]
    {
        // Wed 2026-12-30 is a working day, Mon 12-28 too.
        new("add vacation range", vm =>
        {
            vm.NewStart = new DateTime(2026, 12, 28); vm.NewEnd = new DateTime(2026, 12, 30);
            vm.NewDescription = "  trip "; vm.AddEntryCommand.Execute(null);
        }, null, KeepsForm: true),
        new("add half day", vm =>
        {
            vm.NewStart = new DateTime(2026, 11, 2); vm.NewEnd = new DateTime(2026, 11, 2);
            vm.IsHalfDay = true; vm.AddEntryCommand.Execute(null);
        }, null, KeepsForm: true),
        new("delete vacation", vm => vm.DeleteEntryCommand.Execute(vm.Entries[0]), null),
        new("add holiday", vm => { vm.NewHoliday = new DateTime(2026, 5, 1); vm.AddHolidayCommand.Execute(null); }, null, KeepsForm: true),
        new("remove holiday", vm => vm.RemoveHolidayCommand.Execute(new DateOnly(2026, 12, 25)), null),
        new("employed since", vm => vm.EmployedSince = new DateTime(2026, 6, 16), nameof(MainViewModel.EmployedSince)),
        new("first tracked year", vm => vm.FirstTrackedHolidayYear = new HolidayYear(2024), nameof(MainViewModel.FirstTrackedHolidayYear)),
        new("opening balance", vm => vm.OpeningBalanceDays = 4, nameof(MainViewModel.OpeningBalanceDays)),
        new("transfer limit 0 (formerly the switch)", vm => vm.TransferLimitDays = 0, nameof(MainViewModel.TransferLimitDays)),
        new("transfer limit", vm => vm.TransferLimitDays = 12, nameof(MainViewModel.TransferLimitDays)),
        new("reset transfer limit", vm => vm.ResetTransferLimitCommand.Execute(null), null),
        new("payout all", vm => vm.PayoutAll = true, nameof(MainViewModel.PayoutAll)),
        new("payout none", vm => vm.PayoutNone = true, nameof(MainViewModel.PayoutNone)),
        new("payout days", vm => vm.PayoutDays = 6, nameof(MainViewModel.PayoutDays)),
        new("reset payout", vm => vm.ResetPayoutCommand.Execute(null), null),
    }.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(Cases))]
    public void FailedSave_RollsEverythingBack_ThenSucceedsOnceWritable(Case c)
    {
        var vm = NewVm(Dec18, Rich());
        var before = State(vm);
        var bytesBefore = File.ReadAllBytes(DataFile);
        ReadOnly(true);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        c.Run(vm);

        Assert.Equal(before, State(vm)); // identical bound state and in-memory data
        Assert.StartsWith("Could not save: ", vm.ErrorMessage);
        Assert.EndsWith(". Your change was not saved.", vm.ErrorMessage);
        Assert.DoesNotContain("..", vm.ErrorMessage);
        Assert.Contains("denied", vm.ErrorMessage);
        Assert.Equal(bytesBefore, File.ReadAllBytes(DataFile));
        Assert.False(File.Exists(DataFile + ".tmp"));
        Assert.Equal([DataFile], Directory.GetFiles(Dir)); // no .tmp or other leftovers
        if (c.Property is not null)
            Assert.Contains(c.Property, raised); // bound control snaps back
        if (c.KeepsForm)
            Assert.NotEqual(Form(NewVmFresh()), Form(vm));

        // Same action once the file is writable again: it works and the error goes away.
        ReadOnly(false);
        c.Run(vm);

        Assert.Equal("", vm.ErrorMessage);
        Assert.NotEqual(before, State(vm));
        Assert.NotEqual(bytesBefore, File.ReadAllBytes(DataFile));
        Assert.False(File.Exists(DataFile + ".tmp"));
        Assert.Equal(InMemory(vm), JsonSerializer.Serialize(new DataStore(DataFile).Load(), Json)); // screen == disk
    }

    [Fact]
    public void FailedAdd_KeepsFormInputs_AndSuccessClearsThem()
    {
        var vm = NewVm(Dec18, Rich());
        ReadOnly(true);
        vm.NewStart = new DateTime(2026, 12, 28);
        vm.NewEnd = new DateTime(2026, 12, 30);
        vm.NewDescription = "trip";
        vm.AddEntryCommand.Execute(null);

        Assert.Equal(new DateTime(2026, 12, 28), vm.NewStart);
        Assert.Equal(new DateTime(2026, 12, 30), vm.NewEnd);
        Assert.Equal("trip", vm.NewDescription);

        ReadOnly(false);
        vm.AddEntryCommand.Execute(null);

        Assert.Null(vm.NewStart);
        Assert.Null(vm.NewEnd);
        Assert.Equal("", vm.NewDescription);
        Assert.Equal("", vm.ErrorMessage);
    }

    [Fact]
    public void FailedHalfDayAdd_KeepsHalfDayFlag()
    {
        var vm = NewVm(Dec18, Rich());
        ReadOnly(true);
        vm.NewStart = new DateTime(2026, 11, 2);
        vm.NewEnd = new DateTime(2026, 11, 2);
        vm.IsHalfDay = true;
        vm.AddEntryCommand.Execute(null);

        Assert.True(vm.IsHalfDay);
        Assert.Equal(new DateTime(2026, 11, 2), vm.NewStart);
    }

    [Fact]
    public void FailedHolidayAdd_KeepsDate_AndSuccessClearsIt()
    {
        var vm = NewVm(Dec18, Rich());
        ReadOnly(true);
        vm.NewHoliday = new DateTime(2026, 5, 1);
        vm.AddHolidayCommand.Execute(null);
        Assert.Equal(new DateTime(2026, 5, 1), vm.NewHoliday);
        Assert.DoesNotContain(new DateOnly(2026, 5, 1), vm.Holidays);

        ReadOnly(false);
        vm.AddHolidayCommand.Execute(null);
        Assert.Null(vm.NewHoliday);
        Assert.Contains(new DateOnly(2026, 5, 1), vm.Holidays);
    }

    [Fact]
    public void FailedHolidayAdd_InOtherYear_IsRolledBack_AndDoesNotSayAdded()
    {
        var vm = NewVm(Dec18, Rich());
        var before = State(vm);
        ReadOnly(true);
        vm.NewHoliday = new DateTime(2027, 1, 1);
        vm.AddHolidayCommand.Execute(null);

        Assert.Equal(before, State(vm));
        Assert.StartsWith("Could not save: ", vm.ErrorMessage);
        Assert.DoesNotContain("Added to", vm.ErrorMessage);
    }

    [Fact]
    public void SecondFailure_ReplacesTheMessage_AndSuccessAfterErrorClearsIt()
    {
        var vm = NewVm(Dec18, Rich());
        ReadOnly(true);
        vm.OpeningBalanceDays = 4;
        var first = vm.ErrorMessage;
        vm.OpeningBalanceDays = 5;
        Assert.Equal(first, vm.ErrorMessage); // same reason, same text, not appended
        Assert.Equal(2m, vm.OpeningBalanceDays);

        ReadOnly(false);
        vm.TransferLimitDays = 0; // a different action succeeds
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal(0m, new DataStore(DataFile).Load().FindHolidayYear(2025)!.TransferLimitDays);
        Assert.Equal(2m, new DataStore(DataFile).Load().OpeningBalanceDays); // the failed changes never came back
    }

    [Fact]
    public void ValidationError_IsClearedByNextSuccessfulChange()
    {
        var vm = NewVm(Dec18, Rich());
        vm.AddEntryCommand.Execute(null); // no dates
        Assert.Equal("Pick a start and end date.", vm.ErrorMessage);

        vm.OpeningBalanceDays = 3;
        Assert.Equal("", vm.ErrorMessage);
    }

    [Fact]
    public void FailedChangeAfterRollback_ViewModelStillWorks_ForOtherYears()
    {
        // The rollback replaces the data object; navigating and editing afterwards must use the restored one.
        var vm = NewVm(Dec18, Rich());
        ReadOnly(true);
        vm.NewHoliday = new DateTime(2026, 5, 1);
        vm.AddHolidayCommand.Execute(null);
        ReadOnly(false);

        vm.SelectedYear = 2027;
        vm.NewHoliday = new DateTime(2027, 1, 1);
        vm.AddHolidayCommand.Execute(null);

        Assert.Equal("", vm.ErrorMessage);
        Assert.Contains(new DateOnly(2027, 1, 1), vm.Holidays);
        var saved = new DataStore(DataFile).Load();
        Assert.Equal([new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 25)], saved.Years.Single(y => y.Year == 2026).PublicHolidays);
    }

    [Fact]
    public void FailedHolidayRemove_InAnotherSelectedYear_RestoresThatYear()
    {
        var vm = NewVm(Dec18, Rich());
        vm.SelectedYear = 2026;
        ReadOnly(true);
        vm.RemoveHolidayCommand.Execute(new DateOnly(2026, 12, 24));
        Assert.Equal([new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 25)], vm.Holidays);
        ReadOnly(false);
        vm.RemoveHolidayCommand.Execute(new DateOnly(2026, 12, 24));
        Assert.Equal([new DateOnly(2026, 12, 25)], vm.Holidays);
    }

    // ---- very first save: no file yet, folder cannot be created ----

    private string BlockedPath()
    {
        // A file where the data folder should be: CreateDirectory fails with an IOException.
        Directory.CreateDirectory(Dir);
        var blocker = Path.Combine(Dir, "blocker");
        File.WriteAllText(blocker, "x");
        return Path.Combine(blocker, "data.json");
    }

    private static string? StoredFirstTracked(MainViewModel vm)
    {
        var field = typeof(MainViewModel).GetField("_data", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return ((VacationData)field.GetValue(vm)!).FirstTrackedHolidayYear?.ToString();
    }

    [Fact]
    public void FirstEverChange_FailedSave_DoesNotStoreFirstTrackedYear_AndRetrySucceeds()
    {
        var path = BlockedPath();
        var vm = new MainViewModel(new DataStore(path), () => Dec18);
        var before = State(vm);
        Assert.Null(StoredFirstTracked(vm));

        vm.NewStart = new DateTime(2026, 12, 28);
        vm.NewEnd = new DateTime(2026, 12, 30);
        vm.AddEntryCommand.Execute(null);

        Assert.StartsWith("Could not save: ", vm.ErrorMessage);
        Assert.EndsWith(". Your change was not saved.", vm.ErrorMessage);
        Assert.Null(StoredFirstTracked(vm));
        Assert.Empty(vm.Entries);
        Assert.Equal(new DateTime(2026, 12, 28), vm.NewStart);
        Assert.Equal(before, State(vm));
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));

        File.Delete(Path.Combine(Dir, "blocker")); // the folder can be created now
        vm.AddEntryCommand.Execute(null);

        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal("2025", StoredFirstTracked(vm)); // oldest holiday year open on 2026-12-18 is 2025/26
        Assert.Single(vm.Entries);
        Assert.Equal(2025, new DataStore(path).Load().FirstTrackedHolidayYear);
    }

    [Fact]
    public void FirstEverSettingChange_FailedSave_DoesNotStoreFirstTrackedYear()
    {
        var path = BlockedPath();
        var vm = new MainViewModel(new DataStore(path), () => Dec18);

        vm.TransferLimitDays = 3;

        Assert.Equal(5m, vm.TransferLimitDays); // rolled back to the default
        Assert.False(vm.TransferLimitIsOwn);
        Assert.Null(StoredFirstTracked(vm));
        Assert.StartsWith("Could not save: ", vm.ErrorMessage);
    }
}

