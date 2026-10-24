using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>
/// Feature 4 through the view model: the sidebar list of public holidays (automatic Danish ones and the user's own),
/// refusing an automatic date as a manual holiday, the message for booking on a holiday, and the conflict note.
/// Clock fixed at Fri 2026-12-18 (HolidayYearVmTestBase), so the selected year starts at 2026.
/// Calendar facts: Easter 2026 = Sun 04-05, Easter 2027 = Sun 03-28; Thu 2026-12-24, Fri 12-25, Sat 12-26, Thu 12-31.
/// </summary>
public class MainViewModelDanishHolidaysTests : HolidayYearVmTestBase, IDisposable
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private VacationData Saved() => new DataStore(DataFile).Load();

    private static VacationData WithDays(params (DateOnly Date, decimal Amount, DayType Type)[] days)
    {
        var data = Seed(0);
        foreach (var (date, amount, type) in days)
            data.Days.Add(new BookedDay { Date = date, Amount = amount, Type = type });
        return data;
    }

    // ---- The list ----

    [Fact]
    public void FreshStart_ListsTheTwelveAutomaticHolidays2026_InDateOrder_WithNames_NoneRemovable()
    {
        var vm = NewVm(Dec18);

        Assert.Equal(
            [
                (D(2026, 1, 1), "Nytårsdag"), (D(2026, 4, 2), "Skærtorsdag"), (D(2026, 4, 3), "Langfredag"),
                (D(2026, 4, 5), "Påskedag"), (D(2026, 4, 6), "2. påskedag"), (D(2026, 5, 14), "Kristi himmelfartsdag"),
                (D(2026, 5, 24), "Pinsedag"), (D(2026, 5, 25), "2. pinsedag"), (D(2026, 12, 24), "Juleaftensdag"),
                (D(2026, 12, 25), "Juledag"), (D(2026, 12, 26), "2. juledag"), (D(2026, 12, 31), "Nytårsaftensdag"),
            ],
            vm.PublicHolidays.Select(r => (r.Date, r.Name)));
        Assert.All(vm.PublicHolidays, r =>
        {
            Assert.True(r.IsAutomatic);
            Assert.False(r.CanRemove);
            Assert.True(r.HasName);
        });
        Assert.Empty(vm.Holidays); // Holidays = the user's own dates only
    }

    [Fact]
    public void FollowsTheSelectedYear()
    {
        var vm = NewVm(Dec18);

        vm.SelectedYear = 2027;

        Assert.Equal(12, vm.PublicHolidays.Count);
        Assert.All(vm.PublicHolidays, r => Assert.Equal(2027, r.Date.Year));
        Assert.Equal(D(2027, 3, 25), vm.PublicHolidays.Single(r => r.Name == "Skærtorsdag").Date);
        Assert.Equal(D(2027, 5, 6), vm.PublicHolidays.Single(r => r.Name == "Kristi himmelfartsdag").Date);
        Assert.Equal(D(2027, 5, 16), vm.PublicHolidays.Single(r => r.Name == "Pinsedag").Date);
    }

    [Fact]
    public void ManualOnlyHoliday_HasNoName_IsRemovable_AndSitsInDateOrder()
    {
        var vm = NewVm(Dec18);

        vm.NewHoliday = new DateTime(2026, 6, 5); // Fri, Grundlovsdag: not automatic
        vm.AddHolidayCommand.Execute(null);

        Assert.Equal(13, vm.PublicHolidays.Count);
        var row = vm.PublicHolidays.Single(r => r.Date == D(2026, 6, 5));
        Assert.False(row.IsAutomatic);
        Assert.True(row.CanRemove);
        Assert.False(row.HasName);
        Assert.Equal("", row.Name);
        Assert.Equal(vm.PublicHolidays.Select(r => r.Date).Order(), vm.PublicHolidays.Select(r => r.Date));
        Assert.Equal([D(2026, 6, 5)], vm.Holidays);
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal("", vm.InfoMessage);
        Assert.Null(vm.NewHoliday);
        Assert.Equal([D(2026, 6, 5)], Saved().Years.Single().PublicHolidays);
    }

    [Fact]
    public void ManualOnlyHoliday_CanBeRemoved_AndTheRowGoesAway()
    {
        var vm = NewVm(Dec18);
        vm.NewHoliday = new DateTime(2026, 6, 5);
        vm.AddHolidayCommand.Execute(null);

        vm.RemoveHolidayCommand.Execute(D(2026, 6, 5));

        Assert.Equal(12, vm.PublicHolidays.Count);
        Assert.DoesNotContain(vm.PublicHolidays, r => r.Date == D(2026, 6, 5));
        Assert.Empty(vm.Holidays);
        Assert.Empty(Saved().Years.Single().PublicHolidays);
    }

    [Fact]
    public void DateThatIsBoth_ShowsAsAutomaticWithName_KeepsARemoveButton_AndRemovesOnlyTheManualEntry()
    {
        // An older file with Thu 2026-12-24 entered by hand.
        var data = Seed(0).WithManualHoliday(D(2026, 12, 24));
        var vm = NewVm(Dec18, data);

        var row = vm.PublicHolidays.Single(r => r.Date == D(2026, 12, 24));
        Assert.True(row.IsAutomatic);
        Assert.True(row.CanRemove);
        Assert.Equal("Juleaftensdag", row.Name);
        Assert.Equal(12, vm.PublicHolidays.Count); // not listed twice

        vm.RemoveHolidayCommand.Execute(D(2026, 12, 24));

        var after = vm.PublicHolidays.Single(r => r.Date == D(2026, 12, 24));
        Assert.True(after.IsAutomatic);
        Assert.False(after.CanRemove);
        Assert.Equal(12, vm.PublicHolidays.Count);
        Assert.Empty(vm.Holidays);
        Assert.Empty(Saved().Years.Single(y => y.Year == 2026).PublicHolidays);
        // The day still isn't bookable
        vm.NewStart = new DateTime(2026, 12, 24);
        vm.AddEntryCommand.Execute(null);
        Assert.Equal("Thursday 24 December 2026 is a public holiday (Juleaftensdag).", vm.ErrorMessage);
    }

    [Fact]
    public void RemoveTooltip_DiffersForTheBothCase()
    {
        var vm = NewVm(Dec18, Seed(0).WithManualHoliday(D(2026, 12, 24)).WithManualHoliday(D(2026, 6, 5)));

        Assert.Equal("Remove", vm.PublicHolidays.Single(r => r.Date == D(2026, 6, 5)).RemoveTooltip);
        Assert.NotEqual("Remove", vm.PublicHolidays.Single(r => r.Date == D(2026, 12, 24)).RemoveTooltip);
    }

    [Fact]
    public void ManualHolidayStoredUnderTheWrongYear_IsNotListed()
    {
        var data = Seed(0);
        data.Years.Add(new YearSettings { Year = 2027, PublicHolidays = [D(2026, 6, 5)] });
        var vm = NewVm(Dec18, data);

        Assert.Equal(12, vm.PublicHolidays.Count);
        vm.SelectedYear = 2027;
        Assert.Equal(12, vm.PublicHolidays.Count);
    }

    // ---- Adding an automatic holiday as a manual one ----

    [Theory]
    [InlineData(2026, 4, 6, "6 April 2026 is already a public holiday (2. påskedag).")]
    [InlineData(2026, 5, 24, "24 May 2026 is already a public holiday (Pinsedag).")] // Sunday
    [InlineData(2026, 12, 24, "24 December 2026 is already a public holiday (Juleaftensdag).")]
    [InlineData(2026, 12, 31, "31 December 2026 is already a public holiday (Nytårsaftensdag).")]
    [InlineData(2027, 1, 1, "1 January 2027 is already a public holiday (Nytårsdag).")]  // other year than selected
    [InlineData(2028, 5, 25, "25 May 2028 is already a public holiday (Kristi himmelfartsdag).")]
    public void AutomaticDate_IsRefusedWithAnInfoMessage_AndNothingIsStored(int y, int m, int d, string message)
    {
        var vm = NewVm(Dec18);

        vm.NewHoliday = new DateTime(y, m, d);
        vm.AddHolidayCommand.Execute(null);

        Assert.Equal(message, vm.InfoMessage);
        Assert.Equal("", vm.ErrorMessage);
        Assert.Null(vm.NewHoliday);
        Assert.Empty(vm.Holidays);
        Assert.Equal(12, vm.PublicHolidays.Count);
        Assert.False(File.Exists(DataFile)); // nothing was saved, not even an empty file
    }

    [Fact]
    public void AutomaticDate_Refused_LeavesAnExistingFileByteForByteAlone()
    {
        var vm = NewVm(Dec18, Seed(5));
        vm.TransferLimitDays = 3; // a save, so the file exists
        var before = File.ReadAllBytes(DataFile);

        vm.NewHoliday = new DateTime(2026, 12, 25);
        vm.AddHolidayCommand.Execute(null);

        Assert.Equal(before, File.ReadAllBytes(DataFile));
    }

    [Fact]
    public void AutomaticDate_Refused_EvenWhenTheFileIsReadOnly_NoSaveIsAttempted_SoNoSaveError()
    {
        var vm = NewVm(Dec18, Seed(5));
        vm.TransferLimitDays = 3;
        File.SetAttributes(DataFile, File.GetAttributes(DataFile) | FileAttributes.ReadOnly);
        try
        {
            vm.NewHoliday = new DateTime(2026, 12, 25);
            vm.AddHolidayCommand.Execute(null);

            Assert.Equal("", vm.ErrorMessage);
            Assert.Contains("already a public holiday", vm.InfoMessage);
        }
        finally
        {
            File.SetAttributes(DataFile, File.GetAttributes(DataFile) & ~FileAttributes.ReadOnly);
        }
    }

    [Fact]
    public void ManualDateInAnotherYear_StillSaysAdded_AndDoesNotShowInThisYearsList()
    {
        var vm = NewVm(Dec18);

        vm.NewHoliday = new DateTime(2027, 6, 5); // Saturday, not automatic
        vm.AddHolidayCommand.Execute(null);

        Assert.Equal("Added to 2027's holidays.", vm.InfoMessage);
        Assert.Equal(12, vm.PublicHolidays.Count);
        vm.SelectedYear = 2027;
        Assert.Equal(13, vm.PublicHolidays.Count);
    }

    [Fact]
    public void AddingWithoutADate_ShowsTheOldError()
    {
        var vm = NewVm(Dec18);

        vm.AddHolidayCommand.Execute(null);

        Assert.Equal("Pick a date for the holiday.", vm.ErrorMessage);
    }

    // ---- Booking on a holiday ----

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void BookingASingleAutomaticHoliday_SaysSo_ForVacationAndFeriefri_FullAndHalfDay(bool feriefri, bool halfDay)
    {
        var vm = NewVm(Dec18, Seed(5));
        var before = File.Exists(DataFile) ? File.ReadAllBytes(DataFile) : null;
        vm.BookFeriefri = feriefri;
        vm.NewStart = new DateTime(2026, 12, 24); // Thu
        vm.IsHalfDay = halfDay;

        vm.AddEntryCommand.Execute(null);

        Assert.Equal("Thursday 24 December 2026 is a public holiday (Juleaftensdag).", vm.ErrorMessage);
        Assert.Equal(before, File.Exists(DataFile) ? File.ReadAllBytes(DataFile) : null); // nothing booked, nothing saved
        Assert.DoesNotContain(vm.Entries, e => e.Period.Start == D(2026, 12, 24));
    }

    [Fact]
    public void BookingASingleManualHoliday_SaysSo_WithoutAName()
    {
        var vm = NewVm(Dec18);
        vm.NewHoliday = new DateTime(2026, 6, 5);
        vm.AddHolidayCommand.Execute(null);

        vm.NewStart = new DateTime(2026, 6, 5);
        vm.AddEntryCommand.Execute(null);

        Assert.Equal("Friday 5 June 2026 is a public holiday.", vm.ErrorMessage);
    }

    [Fact]
    public void BookingASingleHolidayOnAWeekend_StillNamesTheHoliday()
    {
        var vm = NewVm(Dec18);
        vm.NewStart = new DateTime(2026, 12, 26); // Saturday and 2. juledag

        vm.AddEntryCommand.Execute(null);

        Assert.Equal("Saturday 26 December 2026 is a public holiday (2. juledag).", vm.ErrorMessage);
    }

    [Fact]
    public void BookingAPlainWeekend_KeepsTheOldMessage()
    {
        var vm = NewVm(Dec18);
        vm.NewStart = new DateTime(2026, 11, 7);
        vm.NewEnd = new DateTime(2026, 11, 8);

        vm.AddEntryCommand.Execute(null);

        Assert.Equal("That period has no working days in it.", vm.ErrorMessage);
    }

    [Fact]
    public void BookingARangeOfOnlyHolidays_UsesTheGenericMessage()
    {
        var vm = NewVm(Dec18);
        vm.NewStart = new DateTime(2026, 12, 24);
        vm.NewEnd = new DateTime(2026, 12, 26); // Thu hol, Fri hol, Sat hol

        vm.AddEntryCommand.Execute(null);

        Assert.Equal("That period has no working days in it.", vm.ErrorMessage);
    }

    [Fact]
    public void BookingARangeThroughChristmas_BooksOnlyTheWorkingDays_AndPreviewCountsThem()
    {
        var vm = NewVm(Dec18);
        vm.NewStart = new DateTime(2026, 12, 21);
        vm.NewEnd = new DateTime(2026, 12, 31);

        // Mon 21, 22, 23, 28, 29, 30 (24, 25, 31 are holidays)
        Assert.Equal("6 working day(s)", vm.NewEntryPreview);
        vm.AddEntryCommand.Execute(null);

        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal(
            [D(2026, 12, 21), D(2026, 12, 22), D(2026, 12, 23), D(2026, 12, 28), D(2026, 12, 29), D(2026, 12, 30)],
            Saved().Days.Select(d => d.Date));
        // One period (holidays and the weekend between don't split it), 6 days
        Assert.Equal(6m, Assert.Single(vm.Entries).Period.Amount);
        Assert.Equal("", vm.ConflictNote);
    }

    [Fact]
    public void Preview_OnASingleHoliday_IsZero()
    {
        var vm = NewVm(Dec18);
        vm.NewStart = new DateTime(2026, 12, 25);
        Assert.Equal("0 working day(s)", vm.NewEntryPreview);
    }

    // ---- ConflictNote ----

    [Fact]
    public void ConflictNote_EmptyWhenNothingBookedOnHolidays()
    {
        var vm = NewVm(Dec18, WithDays((D(2026, 12, 23), 1, DayType.Vacation)));

        Assert.Equal("", vm.ConflictNote);
        Assert.False(vm.HasConflictNote);
    }

    [Fact]
    public void ConflictNote_OneDay_Singular()
    {
        var vm = NewVm(Dec18, WithDays((D(2026, 12, 24), 1, DayType.Vacation), (D(2026, 12, 23), 1, DayType.Vacation)));

        Assert.True(vm.HasConflictNote);
        Assert.Equal("1 booked day falls on public holidays and is not counted: 24 December 2026.", vm.ConflictNote);
    }

    [Fact]
    public void ConflictNote_SeveralDays_Plural_InDateOrder_HalfDaysAndFeriefriIncluded()
    {
        var vm = NewVm(Dec18, WithDays(
            (D(2026, 12, 31), 1, DayType.Feriefri),
            (D(2026, 4, 6), 0.5m, DayType.Vacation),
            (D(2026, 12, 25), 1, DayType.Vacation)));

        Assert.Equal(
            "3 booked days fall on public holidays and are not counted: 6 April 2026, 25 December 2026, 31 December 2026.",
            vm.ConflictNote);
    }

    [Fact]
    public void ConflictNote_FollowsTheSelectedYear()
    {
        var vm = NewVm(Dec18, WithDays(
            (D(2026, 12, 24), 1, DayType.Vacation),
            (D(2027, 1, 1), 1, DayType.Vacation),
            (D(2027, 3, 26), 1, DayType.Vacation))); // Fri, Langfredag 2027
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.Equal("1 booked day falls on public holidays and is not counted: 24 December 2026.", vm.ConflictNote);

        vm.SelectedYear = 2027;
        Assert.Equal(
            "2 booked days fall on public holidays and are not counted: 1 January 2027, 26 March 2027.", vm.ConflictNote);
        Assert.Contains("ConflictNote", raised);
        Assert.Contains("HasConflictNote", raised);

        vm.SelectedYear = 2028;
        Assert.Equal("", vm.ConflictNote);
        Assert.False(vm.HasConflictNote);
    }

    [Fact]
    public void ConflictNote_AppearsWhenAManualHolidayIsAddedOnABookedDay_AndGoesWhenItIsRemoved()
    {
        var vm = NewVm(Dec18);
        vm.NewStart = new DateTime(2026, 6, 1); // Mon
        vm.NewEnd = new DateTime(2026, 6, 5);   // Fri
        vm.AddEntryCommand.Execute(null);
        Assert.Equal("", vm.ConflictNote);
        Assert.Equal(5m, Assert.Single(vm.Entries).Period.Amount);

        vm.NewHoliday = new DateTime(2026, 6, 5);
        vm.AddHolidayCommand.Execute(null);

        Assert.Equal("1 booked day falls on public holidays and is not counted: 5 June 2026.", vm.ConflictNote);
        Assert.Equal(4m, Assert.Single(vm.Entries).Period.Amount);

        vm.RemoveHolidayCommand.Execute(D(2026, 6, 5));

        Assert.Equal("", vm.ConflictNote);
        Assert.Equal(5m, Assert.Single(vm.Entries).Period.Amount);
    }

    [Fact]
    public void ConflictNote_DuplicateRecordOnTheSameDate_CountsOnce()
    {
        // Hand-edited file with two records on Thu 2026-12-24.
        var vm = NewVm(Dec18, WithDays((D(2026, 12, 24), 1, DayType.Vacation), (D(2026, 12, 24), 0.5m, DayType.Vacation)));

        Assert.StartsWith("1 booked day falls", vm.ConflictNote);
    }

    [Fact]
    public void RecordsOnHolidays_DoNotCountInTheBanner()
    {
        // 24 and 25 Dec booked in an old file: nothing is used or booked ahead.
        var vm = NewVm(Dec18, WithDays((D(2026, 12, 24), 1, DayType.Vacation), (D(2026, 12, 25), 1, DayType.Vacation)));

        Assert.Equal(0m, vm.Status.HolidayYear.BookedDays);
        Assert.Equal(25m, vm.Status.UnbookedDays);
    }

    // ---- Failed save ----

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

    [Fact]
    public void FailedAdd_OfAManualHoliday_RestoresTheList_AndKeepsTheDate()
    {
        var vm = NewVm(Dec18, Seed(5).WithManualHoliday(D(2026, 12, 24)));
        var rowsBefore = vm.PublicHolidays.ToList();
        ReadOnly(true);

        vm.NewHoliday = new DateTime(2026, 6, 5);
        vm.AddHolidayCommand.Execute(null);

        Assert.StartsWith("Could not save: ", vm.ErrorMessage);
        Assert.Equal(rowsBefore, vm.PublicHolidays.ToList());
        Assert.Equal([D(2026, 12, 24)], vm.Holidays);
        Assert.Equal(new DateTime(2026, 6, 5), vm.NewHoliday);

        ReadOnly(false);
        vm.AddHolidayCommand.Execute(null);
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal(13, vm.PublicHolidays.Count);
    }

    [Fact]
    public void FailedRemove_OfABothDate_KeepsItsRemoveButton_AndRetrySucceeds()
    {
        var vm = NewVm(Dec18, Seed(5).WithManualHoliday(D(2026, 12, 24)).WithManualHoliday(D(2026, 6, 5)));
        ReadOnly(true);

        vm.RemoveHolidayCommand.Execute(D(2026, 12, 24));

        Assert.StartsWith("Could not save: ", vm.ErrorMessage);
        Assert.True(vm.PublicHolidays.Single(r => r.Date == D(2026, 12, 24)).CanRemove);
        Assert.Equal([D(2026, 6, 5), D(2026, 12, 24)], vm.Holidays.Order());
        Assert.Equal(13, vm.PublicHolidays.Count);

        ReadOnly(false);
        vm.RemoveHolidayCommand.Execute(D(2026, 12, 24));
        Assert.Equal("", vm.ErrorMessage);
        Assert.False(vm.PublicHolidays.Single(r => r.Date == D(2026, 12, 24)).CanRemove);
        Assert.Equal([D(2026, 6, 5)], vm.Holidays);
    }

    [Fact]
    public void FailedAdd_OfAManualHolidayOnABookedDay_DoesNotShowAConflictNote()
    {
        var vm = NewVm(Dec18, WithDays((D(2026, 6, 5), 1, DayType.Vacation)));
        vm.TransferLimitDays = 4; // makes sure the file exists
        ReadOnly(true);

        vm.NewHoliday = new DateTime(2026, 6, 5);
        vm.AddHolidayCommand.Execute(null);

        Assert.Equal("", vm.ConflictNote);
        Assert.Equal(1m, Assert.Single(vm.Entries).Period.Amount);
    }

    // ---- Old file ----

    [Fact]
    public void OldFile_WithManualChristmasHolidays_LoadsAndShowsThemAsAutomaticAndRemovable()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(DataFile, """
            { "Days": [ { "Date": "2026-12-23", "Type": "Vacation", "Amount": 1, "Description": "x" } ],
              "Years": [ { "Year": 2026, "AllowanceDays": 25, "PublicHolidays": [ "2026-12-24", "2026-12-25", "2026-12-26" ] } ] }
            """);

        var vm = NewVm(Dec18);

        Assert.Equal(12, vm.PublicHolidays.Count);
        Assert.Equal(
            [D(2026, 12, 24), D(2026, 12, 25), D(2026, 12, 26)],
            vm.PublicHolidays.Where(r => r.CanRemove).Select(r => r.Date));
        Assert.Equal("", vm.ConflictNote);
        Assert.Single(vm.Entries);
    }
}

internal static class DanishHolidayVmTestExtensions
{
    public static VacationData WithManualHoliday(this VacationData data, DateOnly date)
    {
        var year = data.Years.Find(y => y.Year == date.Year);
        if (year is null)
            data.Years.Add(year = new YearSettings { Year = date.Year });
        year.PublicHolidays.Add(date);
        return data;
    }
}
