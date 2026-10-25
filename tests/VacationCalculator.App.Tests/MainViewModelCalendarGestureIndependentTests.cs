using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>
/// Feature 6: mouse gestures on the calendar through the view model. Today is Fri 2026-12-18.
/// Calendar reference (2026): Mon 2 Nov; Mon 9 Nov; Mon 16 Nov; Fri 30 Oct; Sat 31 Oct; Sun 1 Nov.
/// Holiday year 2026/27 (1 Sep 2026 - 31 Aug 2027) has a default Feriefri allowance of 5.
/// </summary>
public class MainViewModelCalendarGestureIndependentTests : HolidayYearVmTestBase, IDisposable
{
    private static readonly DateOnly Mon2 = new(2026, 11, 2);
    private static readonly DateOnly Tue3 = new(2026, 11, 3);
    private static readonly DateOnly Wed4 = new(2026, 11, 4);
    private static readonly DateOnly Thu5 = new(2026, 11, 5);
    private static readonly DateOnly Fri6 = new(2026, 11, 6);
    private static readonly DateOnly Sat7 = new(2026, 11, 7);
    private static readonly DateOnly Mon9 = new(2026, 11, 9);
    private static readonly DateOnly Tue10 = new(2026, 11, 10);
    private static readonly DateOnly Wed11 = new(2026, 11, 11);
    private static readonly DateOnly Mon16 = new(2026, 11, 16);

    private static CalendarGesture Click(DateOnly d, DayType t = DayType.Vacation, bool shift = false) => new(d, d, t, shift);
    private static CalendarGesture Drag(DateOnly a, DateOnly b, DayType t = DayType.Vacation) => new(a, b, t, false);

    private static DayCell Cell(MainViewModel vm, DateOnly d) =>
        vm.CalendarMonths[d.Month - 1].Cells.OfType<DayCell>().Single(c => !c.IsBlank && c.Date == d);

    private List<BookedDay> Saved() => new DataStore(DataFile).Load().Days;

    private static BookedDay Day(DateOnly d, DayType t = DayType.Vacation, decimal amount = 1m, string desc = "") =>
        new() { Date = d, Type = t, Amount = amount, Description = desc };

    private static VacationData WithDays(params BookedDay[] days)
    {
        var data = Seed(0);
        data.Days.AddRange(days);
        return data;
    }

    // A previous holiday year's unused Feriefri can be used until 31 Dec, so Feriefri bookings in Nov 2026 first draw on 2025/26.
    // Tests about the allowance running out therefore zero 2025/26 and set 2026/27 explicitly (it would otherwise inherit 0).
    private static void Allow(VacationData data, decimal current)
    {
        data.SetFeriefriAllowance(new HolidayYear(2025), 0m);
        data.SetFeriefriAllowance(new HolidayYear(2026), current);
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

    // ---- click cycle ----

    [Fact]
    public void LeftClick_CyclesFreeFullHalfFree()
    {
        var vm = NewVm(Dec18, Seed(0));
        vm.CalendarGestureCommand.Execute(Click(Mon2));
        Assert.Equal([(Mon2, DayType.Vacation, 1m)], Saved().Select(d => (d.Date, d.Type, d.Amount)));
        Assert.Equal(DayFill.Vacation, Cell(vm, Mon2).Fill);
        Assert.False(Cell(vm, Mon2).IsHalf);

        vm.CalendarGestureCommand.Execute(Click(Mon2));
        Assert.Equal([(Mon2, DayType.Vacation, 0.5m)], Saved().Select(d => (d.Date, d.Type, d.Amount)));
        Assert.True(Cell(vm, Mon2).IsHalf);

        vm.CalendarGestureCommand.Execute(Click(Mon2));
        Assert.Empty(Saved());
        Assert.Equal(DayFill.Plain, Cell(vm, Mon2).Fill);

        vm.CalendarGestureCommand.Execute(Click(Mon2)); // and round again
        Assert.Equal(1m, Assert.Single(Saved()).Amount);
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal("", vm.InfoMessage);
    }

    [Fact]
    public void RightClick_CyclesFeriefri()
    {
        var vm = NewVm(Dec18, Seed(0));
        vm.CalendarGestureCommand.Execute(Click(Mon2, DayType.Feriefri));
        Assert.Equal([(Mon2, DayType.Feriefri, 1m)], Saved().Select(d => (d.Date, d.Type, d.Amount)));
        Assert.Equal(DayFill.Feriefri, Cell(vm, Mon2).Fill);

        vm.CalendarGestureCommand.Execute(Click(Mon2, DayType.Feriefri));
        Assert.Equal(0.5m, Assert.Single(Saved()).Amount);
        Assert.True(Cell(vm, Mon2).IsHalf);

        vm.CalendarGestureCommand.Execute(Click(Mon2, DayType.Feriefri));
        Assert.Empty(Saved());
    }

    [Fact]
    public void ClickOnOtherType_ReplacesAsFullDay()
    {
        var vm = NewVm(Dec18, WithDays(Day(Mon2, DayType.Vacation, 0.5m, "trip")));
        vm.CalendarGestureCommand.Execute(Click(Mon2, DayType.Feriefri));
        var d = Assert.Single(Saved());
        Assert.Equal((DayType.Feriefri, 1m), (d.Type, d.Amount));

        vm.CalendarGestureCommand.Execute(Click(Mon2, DayType.Vacation));
        d = Assert.Single(Saved());
        Assert.Equal((DayType.Vacation, 1m), (d.Type, d.Amount));
        Assert.Equal(DayFill.Vacation, Cell(vm, Mon2).Fill);
    }

    [Fact]
    public void ClickOnWeekendAndHoliday_DoesNothing()
    {
        var vm = NewVm(Dec18, Seed(0));
        vm.CalendarGestureCommand.Execute(Click(Sat7));
        vm.CalendarGestureCommand.Execute(Click(new DateOnly(2026, 12, 24), DayType.Feriefri)); // Thu, Danish holiday
        Assert.Empty(Saved());
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal("", vm.InfoMessage);
    }

    [Fact]
    public void FeriefriClick_HalfLeft_BooksHalfDay_WithInfoMessage()
    {
        // Allowance 4.5: four full days Mon 2 - Thu 5 Nov, then a click on Mon 16 Nov fits only half.
        var data = WithDays(Day(Mon2, DayType.Feriefri), Day(Tue3, DayType.Feriefri), Day(Wed4, DayType.Feriefri), Day(Thu5, DayType.Feriefri));
        Allow(data, 4.5m);
        var vm = NewVm(Dec18, data);

        vm.CalendarGestureCommand.Execute(Click(Mon16, DayType.Feriefri));

        var d = Saved().Single(x => x.Date == Mon16);
        Assert.Equal((DayType.Feriefri, 0.5m), (d.Type, d.Amount));
        Assert.Equal("Only half a Feriefri day was left for 2026/27, so Monday 16 November 2026 was booked as a half day.", vm.InfoMessage);
        Assert.Equal("", vm.ErrorMessage);
    }

    [Fact]
    public void FeriefriClick_ReplacingVacation_HalfLeft_BooksHalf()
    {
        var data = WithDays(Day(Mon2, DayType.Feriefri), Day(Tue3, DayType.Feriefri), Day(Wed4, DayType.Feriefri), Day(Thu5, DayType.Feriefri),
            Day(Mon16, DayType.Vacation));
        Allow(data, 4.5m);
        var vm = NewVm(Dec18, data);

        vm.CalendarGestureCommand.Execute(Click(Mon16, DayType.Feriefri));

        var d = Saved().Single(x => x.Date == Mon16);
        Assert.Equal((DayType.Feriefri, 0.5m), (d.Type, d.Amount));
        Assert.Contains("Only half a Feriefri day was left for 2026/27", vm.InfoMessage);
    }

    [Fact]
    public void FeriefriClick_AllowanceUsed_IsRefused_NothingChangesOrSaves()
    {
        var data = WithDays(Day(Mon2, DayType.Feriefri), Day(Tue3, DayType.Feriefri), Day(Wed4, DayType.Feriefri), Day(Thu5, DayType.Feriefri),
            Day(Fri6, DayType.Feriefri), Day(Mon16, DayType.Vacation, 1m, "keep"));
        Allow(data, 5m);
        var vm = NewVm(Dec18, data);
        var before = File.ReadAllBytes(DataFile);
        ReadOnly(true); // a save attempt would now fail with an error message of its own

        vm.CalendarGestureCommand.Execute(Click(Mon9, DayType.Feriefri));
        Assert.Equal("All 5 Feriefri days for 2026/27 are used.", vm.ErrorMessage);
        Assert.Equal("", vm.InfoMessage);

        vm.CalendarGestureCommand.Execute(Click(Mon16, DayType.Feriefri)); // existing vacation day stays
        Assert.StartsWith("All 5 Feriefri days for 2026/27 are used", vm.ErrorMessage);
        Assert.Equal(DayFill.Vacation, Cell(vm, Mon16).Fill);
        Assert.Equal(DayFill.Plain, Cell(vm, Mon9).Fill);
        ReadOnly(false);
        Assert.Equal(before, File.ReadAllBytes(DataFile));
    }

    [Fact]
    public void FeriefriClick_ZeroAllowance_IsRefused()
    {
        var data = Seed(0);
        Allow(data, 0m);
        var vm = NewVm(Dec18, data);
        vm.CalendarGestureCommand.Execute(Click(Mon2, DayType.Feriefri));
        Assert.Equal("All 0 Feriefri days for 2026/27 are used.", vm.ErrorMessage);
        Assert.Empty(Saved());
    }

    // ---- ranges ----

    [Fact]
    public void Drag_BooksWorkingDaysOnly_OverwritesExistingAsFull_AndWorksReversed()
    {
        // Fri 6 Nov .. Tue 10 Nov: Fri 6, (Sat 7, Sun 8 skipped), Mon 9, Tue 10 = 3 days.
        var data = WithDays(Day(Mon9, DayType.Feriefri, 0.5m, "old"), Day(Sat7, DayType.Vacation));
        var vm = NewVm(Dec18, data);
        vm.CalendarGestureCommand.Execute(Drag(Tue10, Fri6)); // dragged backwards

        var saved = Saved();
        Assert.Equal([Fri6, Sat7, Mon9, Tue10], saved.Select(d => d.Date)); // Sat 7 record is outside nothing: untouched (old stray record)
        Assert.All(saved.Where(d => d.Date != Sat7), d => Assert.Equal((DayType.Vacation, 1m), (d.Type, d.Amount)));
        Assert.Equal(DayFill.Vacation, Cell(vm, Mon9).Fill);
        Assert.False(Cell(vm, Mon9).IsHalf);
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal("", vm.InfoMessage);
    }

    [Fact]
    public void Drag_SkipsPublicHolidays()
    {
        // Tue 22 Dec .. Mon 28 Dec 2026: 22, 23 work; 24, 25 holidays; 26 Sat; 27 Sun; 28 work.
        var vm = NewVm(Dec18, Seed(0));
        vm.CalendarGestureCommand.Execute(Drag(new DateOnly(2026, 12, 22), new DateOnly(2026, 12, 28)));
        Assert.Equal([new DateOnly(2026, 12, 22), new DateOnly(2026, 12, 23), new DateOnly(2026, 12, 28)], Saved().Select(d => d.Date));
    }

    [Fact]
    public void Drag_OnlyWeekend_ChangesNothing_NoMessage()
    {
        var vm = NewVm(Dec18, Seed(0));
        ReadOnly(true);
        vm.CalendarGestureCommand.Execute(Drag(Sat7, new DateOnly(2026, 11, 8)));
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal("", vm.InfoMessage);
        ReadOnly(false);
        Assert.Empty(Saved());
    }

    [Fact]
    public void RightDrag_Feriefri_StopsAtAllowance_WithMessage()
    {
        // Mon 2 Nov .. Wed 11 Nov = 8 working days, allowance 5: Mon 2 - Fri 6 booked, 3 left out.
        var data = Seed(0);
        Allow(data, 5m);
        var vm = NewVm(Dec18, data);
        vm.CalendarGestureCommand.Execute(Drag(Mon2, Wed11, DayType.Feriefri));
        Assert.Equal([Mon2, Tue3, Wed4, Thu5, Fri6], Saved().Select(d => d.Date));
        Assert.All(Saved(), d => Assert.Equal(DayType.Feriefri, d.Type));
        Assert.Contains("5 Feriefri days booked; 3 days left out", vm.InfoMessage);
        Assert.Equal("", vm.ErrorMessage);
    }

    [Fact]
    public void RightDrag_Feriefri_NothingFits_ShowsRefusal()
    {
        var data = WithDays(Day(Mon2, DayType.Feriefri), Day(Tue3, DayType.Feriefri), Day(Wed4, DayType.Feriefri), Day(Thu5, DayType.Feriefri),
            Day(Fri6, DayType.Feriefri));
        Allow(data, 5m);
        var vm = NewVm(Dec18, data);
        vm.CalendarGestureCommand.Execute(Drag(Mon9, Wed11, DayType.Feriefri));
        Assert.Equal("All 5 Feriefri days for 2026/27 are used.", vm.ErrorMessage);
        Assert.Equal(5, Saved().Count);
    }

    [Fact]
    public void Drag_MoreThanOneYear_ShowsTooLongError_NothingBooked()
    {
        var vm = NewVm(Dec18, Seed(0));
        vm.CalendarGestureCommand.Execute(Drag(new DateOnly(2026, 1, 5), new DateOnly(2027, 12, 31)));
        Assert.StartsWith("A booking can cover at most one year.", vm.ErrorMessage);
        Assert.Empty(Saved());
    }

    // ---- shift ----

    [Fact]
    public void ShiftClick_BooksFromLastClickedDay_WithButtonType()
    {
        var vm = NewVm(Dec18, Seed(0));
        vm.CalendarGestureCommand.Execute(Click(Mon2));                       // Mon 2 full vacation
        vm.CalendarGestureCommand.Execute(Click(Wed4, DayType.Vacation, shift: true)); // Mon 2..Wed 4 vacation
        Assert.Equal([Mon2, Tue3, Wed4], Saved().Select(d => d.Date));
        Assert.All(Saved(), d => Assert.Equal((DayType.Vacation, 1m), (d.Type, d.Amount)));

        // The shift click is itself the new last clicked day; a right shift-click books Feriefri Wed 4 .. Fri 6.
        vm.CalendarGestureCommand.Execute(Click(Fri6, DayType.Feriefri, shift: true));
        Assert.Equal([Mon2, Tue3, Wed4, Thu5, Fri6], Saved().Select(d => d.Date));
        Assert.Equal([DayType.Vacation, DayType.Vacation, DayType.Feriefri, DayType.Feriefri, DayType.Feriefri], Saved().Select(d => d.Type));
    }

    [Fact]
    public void ShiftClick_BackwardsFromLastClicked()
    {
        var vm = NewVm(Dec18, Seed(0));
        vm.CalendarGestureCommand.Execute(Click(Wed4));
        vm.CalendarGestureCommand.Execute(Click(Mon2, DayType.Vacation, shift: true));
        Assert.Equal([Mon2, Tue3, Wed4], Saved().Select(d => d.Date));
    }

    [Fact]
    public void ShiftClick_WithoutLastClicked_ActsAsPlainClick()
    {
        var vm = NewVm(Dec18, Seed(0));
        vm.CalendarGestureCommand.Execute(Click(Wed4, DayType.Vacation, shift: true));
        Assert.Equal([Wed4], Saved().Select(d => d.Date));
        Assert.Equal(1m, Saved()[0].Amount);
    }

    [Fact]
    public void ShiftClick_AfterYearChange_ActsAsPlainClick()
    {
        var vm = NewVm(Dec18, Seed(0));
        vm.CalendarGestureCommand.Execute(Click(Mon2));
        vm.SelectedYear = 2027;
        vm.SelectedYear = 2026;
        vm.CalendarGestureCommand.Execute(Click(Wed4, DayType.Vacation, shift: true));
        Assert.Equal([Mon2, Wed4], Saved().Select(d => d.Date)); // not Tue 3: last clicked was forgotten
    }

    [Fact]
    public void Drag_SetsLastClicked_ToReleaseDate()
    {
        var vm = NewVm(Dec18, Seed(0));
        vm.CalendarGestureCommand.Execute(Drag(Mon2, Tue3));
        vm.CalendarGestureCommand.Execute(Click(Thu5, DayType.Vacation, shift: true)); // from Tue 3
        Assert.Equal([Mon2, Tue3, Wed4, Thu5], Saved().Select(d => d.Date));
    }

    // ---- preview ----

    [Fact]
    public void Preview_MarksOnlyBookableDaysBetweenAnchorAndPointer_AndClears()
    {
        var vm = NewVm(Dec18, Seed(0));
        // Fri 30 Oct .. Tue 3 Nov: Fri 30, Mon 2, Tue 3 are bookable; Sat 31 Oct and Sun 1 Nov are not.
        vm.CalendarPreviewCommand.Execute(Drag(new DateOnly(2026, 10, 30), Tue3));
        var marked = vm.CalendarMonths.SelectMany(m => m.Cells).OfType<DayCell>().Where(c => c.Drag != DragKind.None).Select(c => c.Date);
        Assert.Equal([new DateOnly(2026, 10, 30), Mon2, Tue3], marked.OrderBy(d => d));
        Assert.All(vm.CalendarMonths.SelectMany(m => m.Cells).OfType<DayCell>().Where(c => c.Drag != DragKind.None),
            c => Assert.Equal(DragKind.Vacation, c.Drag));
        Assert.Equal(DragKind.None, Cell(vm, new DateOnly(2026, 10, 31)).Drag);
        Assert.Equal(DragKind.None, Cell(vm, new DateOnly(2026, 11, 1)).Drag);
        Assert.Empty(Saved()); // a preview never books

        // Pointer moves backwards past the anchor, with the right button: Feriefri kind, shrinking the previous range.
        vm.CalendarPreviewCommand.Execute(Drag(Wed4, Tue3, DayType.Feriefri));
        var now = vm.CalendarMonths.SelectMany(m => m.Cells).OfType<DayCell>().Where(c => c.Drag != DragKind.None).ToList();
        Assert.Equal([Tue3, Wed4], now.Select(c => c.Date).OrderBy(d => d));
        Assert.All(now, c => Assert.Equal(DragKind.Feriefri, c.Drag));

        vm.CalendarPreviewCommand.Execute(null);
        Assert.DoesNotContain(vm.CalendarMonths.SelectMany(m => m.Cells).OfType<DayCell>(), c => c.Drag != DragKind.None);
    }

    [Fact]
    public void Preview_HolidayInsideRangeIsNotMarked()
    {
        var vm = NewVm(Dec18, Seed(0));
        vm.CalendarPreviewCommand.Execute(Drag(new DateOnly(2026, 12, 23), new DateOnly(2026, 12, 28)));
        var marked = vm.CalendarMonths.SelectMany(m => m.Cells).OfType<DayCell>().Where(c => c.Drag != DragKind.None).Select(c => c.Date);
        Assert.Equal([new DateOnly(2026, 12, 23), new DateOnly(2026, 12, 28)], marked.OrderBy(d => d)); // 24, 25 holidays
    }

    [Fact]
    public void FinishedGesture_ClearsPreview()
    {
        var vm = NewVm(Dec18, Seed(0));
        vm.CalendarPreviewCommand.Execute(Drag(Mon2, Wed4));
        vm.CalendarGestureCommand.Execute(Drag(Mon2, Wed4));
        Assert.DoesNotContain(vm.CalendarMonths.SelectMany(m => m.Cells).OfType<DayCell>(), c => c.Drag != DragKind.None);
    }

    [Fact]
    public void BookableFlag_FollowsWorkingDays()
    {
        var vm = NewVm(Dec18, Seed(0));
        Assert.True(Cell(vm, Mon2).IsBookable);
        Assert.False(Cell(vm, Sat7).IsBookable);
        Assert.False(Cell(vm, new DateOnly(2026, 12, 24)).IsBookable);
        Assert.False(Cell(vm, new DateOnly(2026, 1, 1)).IsBookable);
        Assert.True(vm.CalendarMonths[1].Cells.OfType<DayCell>().Where(c => c.IsBlank).All(c => !c.IsBookable));
    }

    // ---- saving, rollback, updating in place, messages ----

    [Fact]
    public void FailedSave_RollsBack_ShowsError_ThenSucceeds()
    {
        var vm = NewVm(Dec18, Seed(0));
        ReadOnly(true);
        vm.CalendarGestureCommand.Execute(Click(Mon2));
        Assert.NotEqual("", vm.ErrorMessage);
        Assert.Equal(DayFill.Plain, Cell(vm, Mon2).Fill);

        vm.CalendarGestureCommand.Execute(Drag(Mon2, Wed4));
        Assert.NotEqual("", vm.ErrorMessage);
        Assert.Equal(DayFill.Plain, Cell(vm, Tue3).Fill);
        ReadOnly(false);
        Assert.Empty(Saved());

        vm.CalendarGestureCommand.Execute(Drag(Mon2, Wed4));
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal(3, Saved().Count);
        Assert.Equal(DayFill.Vacation, Cell(vm, Tue3).Fill);
    }

    [Fact]
    public void FailedSave_OnCycleToHalf_KeepsFullDay()
    {
        var vm = NewVm(Dec18, WithDays(Day(Mon2)));
        ReadOnly(true);
        vm.CalendarGestureCommand.Execute(Click(Mon2));
        Assert.NotEqual("", vm.ErrorMessage);
        Assert.False(Cell(vm, Mon2).IsHalf);
        ReadOnly(false);
        Assert.Equal(1m, Assert.Single(Saved()).Amount);
    }

    [Fact]
    public void Gesture_UpdatesCalendarCellsInPlace()
    {
        var vm = NewVm(Dec18, Seed(0));
        var months = vm.CalendarMonths;
        var cell = Cell(vm, Mon2);
        var other = Cell(vm, Tue3);
        var changes = new List<string?>();
        cell.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.CalendarGestureCommand.Execute(Click(Mon2));

        Assert.Same(cell, Cell(vm, Mon2));
        Assert.Same(other, Cell(vm, Tue3));
        Assert.Same(months[10], vm.CalendarMonths[10]);
        Assert.Equal(DayFill.Vacation, cell.Fill);
        Assert.Contains(nameof(DayCell.Fill), changes);
    }

    [Fact]
    public void EachGesture_ClearsEarlierMessages()
    {
        var data = WithDays(Day(Mon2, DayType.Feriefri), Day(Tue3, DayType.Feriefri), Day(Wed4, DayType.Feriefri), Day(Thu5, DayType.Feriefri));
        Allow(data, 4.5m);
        var vm = NewVm(Dec18, data);

        vm.CalendarGestureCommand.Execute(Click(Mon16, DayType.Feriefri));   // info (half day)
        Assert.NotEqual("", vm.InfoMessage);
        vm.CalendarGestureCommand.Execute(Click(Sat7));                      // no-op click
        Assert.Equal("", vm.InfoMessage);
        Assert.Equal("", vm.ErrorMessage);

        vm.CalendarGestureCommand.Execute(Click(Mon9, DayType.Feriefri));    // refused: error
        Assert.NotEqual("", vm.ErrorMessage);
        vm.CalendarGestureCommand.Execute(Click(Mon9));                      // vacation click succeeds
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal("", vm.InfoMessage);
    }

    [Fact]
    public void Gesture_ClearsPreExistingFormMessages()
    {
        var vm = NewVm(Dec18, Seed(0));
        vm.NewStart = null;
        vm.NewEnd = null;
        vm.AddEntryCommand.Execute(null); // validation error from the form
        Assert.NotEqual("", vm.ErrorMessage);
        vm.CalendarGestureCommand.Execute(Click(Mon2));
        Assert.Equal("", vm.ErrorMessage);
    }

    [Fact]
    public void Gesture_BalancesAndListFollow()
    {
        var vm = NewVm(Dec18, Seed(0));
        vm.CalendarGestureCommand.Execute(Drag(Mon2, Fri6));
        Assert.Single(vm.Entries);
        Assert.Equal(new DateOnly(2026, 11, 2), vm.Entries[0].Period.Start);
        Assert.Equal(new DateOnly(2026, 11, 6), vm.Entries[0].Period.End);
    }
}


