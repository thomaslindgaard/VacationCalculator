using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>
/// Feature 6 re-test: days already as booked are unchanged (no save, no message). Today is Fri 2026-12-18.
/// 2026 calendar: Mon 2 Nov, Tue 3, Wed 4, Thu 5, Fri 6, Mon 9, Tue 10; Sat 7 Nov.
/// </summary>
public class MainViewModelCalendarUnchangedIndependentTests : HolidayYearVmTestBase, IDisposable
{
    private static readonly DateOnly Mon2 = new(2026, 11, 2);
    private static readonly DateOnly Tue3 = new(2026, 11, 3);
    private static readonly DateOnly Wed4 = new(2026, 11, 4);
    private static readonly DateOnly Thu5 = new(2026, 11, 5);
    private static readonly DateOnly Fri6 = new(2026, 11, 6);
    private static readonly DateOnly Mon9 = new(2026, 11, 9);

    private static CalendarGesture Drag(DateOnly a, DateOnly b, DayType t = DayType.Vacation) => new(a, b, t, false);

    private static DayCell Cell(MainViewModel vm, DateOnly d) =>
        vm.CalendarMonths[d.Month - 1].Cells.OfType<DayCell>().Single(c => !c.IsBlank && c.Date == d);

    private List<BookedDay> Saved() => new DataStore(DataFile).Load().Days;

    private static BookedDay Day(DateOnly d, DayType t = DayType.Vacation, decimal amount = 1m, string desc = "") =>
        new() { Date = d, Type = t, Amount = amount, Description = desc };

    private static VacationData WithDays(decimal allowance, params BookedDay[] days)
    {
        var data = Seed(0);
        data.Days.AddRange(days);
        data.SetFeriefriAllowance(new HolidayYear(2025), 0m);
        data.SetFeriefriAllowance(new HolidayYear(2026), allowance);
        return data;
    }

    public new void Dispose()
    {
        if (File.Exists(DataFile))
            File.SetAttributes(DataFile, File.GetAttributes(DataFile) & ~FileAttributes.ReadOnly);
        base.Dispose();
    }

    [Fact]
    public void VacationDrag_OverAlreadyFullVacation_NoSaveNoMessage()
    {
        var vm = NewVm(Dec18, WithDays(5m, Day(Mon2, desc: "a"), Day(Tue3, desc: "b"), Day(Wed4)));
        var before = File.ReadAllBytes(DataFile);
        File.SetAttributes(DataFile, File.GetAttributes(DataFile) | FileAttributes.ReadOnly); // a save would error
        vm.CalendarGestureCommand.Execute(Drag(Mon2, Wed4));
        File.SetAttributes(DataFile, File.GetAttributes(DataFile) & ~FileAttributes.ReadOnly);
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal("", vm.InfoMessage);
        Assert.Equal(before, File.ReadAllBytes(DataFile));
    }

    [Fact]
    public void FeriefriDrag_OverAlreadyFullFeriefri_NoSaveNoMessage()
    {
        var vm = NewVm(Dec18, WithDays(5m, Day(Mon2, DayType.Feriefri), Day(Tue3, DayType.Feriefri)));
        var before = File.ReadAllBytes(DataFile);
        File.SetAttributes(DataFile, File.GetAttributes(DataFile) | FileAttributes.ReadOnly);
        vm.CalendarGestureCommand.Execute(Drag(Mon2, Tue3, DayType.Feriefri));
        File.SetAttributes(DataFile, File.GetAttributes(DataFile) & ~FileAttributes.ReadOnly);
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal("", vm.InfoMessage);
        Assert.Equal(before, File.ReadAllBytes(DataFile));
    }

    [Fact]
    public void VacationDrag_OverFullVacation_KeepsDescription()
    {
        var vm = NewVm(Dec18, WithDays(5m, Day(Mon2, desc: "ski trip")));
        vm.CalendarGestureCommand.Execute(Drag(Mon2, Tue3));
        var saved = Saved();
        Assert.Equal([Mon2, Tue3], saved.Select(d => d.Date));
        Assert.Equal("ski trip", saved[0].Description);
        Assert.Equal("", saved[1].Description);
    }

    [Fact]
    public void FeriefriDrag_MixUnchangedAndNew_CountsOnlyNew_NoNoteForUnchanged()
    {
        // Allowance 3: Mon 2 already full Feriefri (1 used). Drag Mon 2 .. Fri 6 (5 working days):
        // Mon unchanged; Tue, Wed fit (total 3); Thu, Fri left out = 2.
        var vm = NewVm(Dec18, WithDays(3m, Day(Mon2, DayType.Feriefri, 1m, "keep")));
        vm.CalendarGestureCommand.Execute(Drag(Mon2, Fri6, DayType.Feriefri));
        Assert.Equal([Mon2, Tue3, Wed4], Saved().Select(d => d.Date));
        Assert.Equal("keep", Saved()[0].Description);
        Assert.Contains("2 Feriefri days booked; 2 days left out", vm.InfoMessage);
        Assert.DoesNotContain("half", vm.InfoMessage);
        Assert.DoesNotContain("Monday 2 November", vm.InfoMessage);
    }

    [Fact]
    public void FeriefriDrag_MixUnchangedAndNew_AllFit_NoMessage()
    {
        var vm = NewVm(Dec18, WithDays(5m, Day(Mon2, DayType.Feriefri)));
        vm.CalendarGestureCommand.Execute(Drag(Mon2, Wed4, DayType.Feriefri));
        Assert.Equal(3, Saved().Count);
        Assert.Equal("", vm.InfoMessage);
        Assert.Equal("", vm.ErrorMessage);
    }

    [Fact]
    public void ReviewerScenario_RightDragUpgradesHalfToFull_NoHalfNote()
    {
        // Allowance 2: Tue 3 half (0.5) + Wed 4 full (1) = 1.5 used; upgrading Tue to full makes exactly 2.
        var vm = NewVm(Dec18, WithDays(2m, Day(Tue3, DayType.Feriefri, 0.5m), Day(Wed4, DayType.Feriefri)));
        vm.CalendarGestureCommand.Execute(Drag(Tue3, Wed4, DayType.Feriefri));
        Assert.All(Saved(), d => Assert.Equal(1m, d.Amount));
        Assert.Equal(2, Saved().Count);
        Assert.False(Cell(vm, Tue3).IsHalf);
        Assert.Equal("", vm.InfoMessage);
        Assert.Equal("", vm.ErrorMessage);
    }

    [Fact]
    public void ReviewerVariant_NothingMoreFits_IsNoOp()
    {
        // Allowance 1.5: Tue 3 half + Wed 4 full = 1.5 used; nothing more fits, Tue stays half.
        var vm = NewVm(Dec18, WithDays(1.5m, Day(Tue3, DayType.Feriefri, 0.5m), Day(Wed4, DayType.Feriefri)));
        var before = File.ReadAllBytes(DataFile);
        File.SetAttributes(DataFile, File.GetAttributes(DataFile) | FileAttributes.ReadOnly);
        vm.CalendarGestureCommand.Execute(Drag(Tue3, Wed4, DayType.Feriefri));
        File.SetAttributes(DataFile, File.GetAttributes(DataFile) & ~FileAttributes.ReadOnly);
        Assert.Equal(before, File.ReadAllBytes(DataFile));
        Assert.Equal("", vm.InfoMessage);
        Assert.Equal("", vm.ErrorMessage);
        Assert.True(Cell(vm, Tue3).IsHalf);
    }

    // ---- List tab form ----

    private static void Fill(MainViewModel vm, DateOnly a, DateOnly b, string desc = "", bool half = false)
    {
        vm.BookFeriefri = true;
        vm.NewStart = a.ToDateTime(TimeOnly.MinValue);
        vm.NewEnd = b.ToDateTime(TimeOnly.MinValue);
        vm.NewDescription = desc;
        vm.IsHalfDay = half;
    }

    [Fact]
    public void Form_IdenticalFeriefri_InfoText_ClearedForm_NoSave()
    {
        var vm = NewVm(Dec18, WithDays(5m, Day(Mon2, DayType.Feriefri, 1m, "x"), Day(Tue3, DayType.Feriefri, 1m, "x")));
        var before = File.ReadAllBytes(DataFile);
        File.SetAttributes(DataFile, File.GetAttributes(DataFile) | FileAttributes.ReadOnly);
        Fill(vm, Mon2, Tue3, "x");
        vm.AddEntryCommand.Execute(null);
        File.SetAttributes(DataFile, File.GetAttributes(DataFile) & ~FileAttributes.ReadOnly);
        Assert.Equal("Those days were already booked as Feriefri.", vm.InfoMessage);
        Assert.Equal("", vm.ErrorMessage);
        Assert.Null(vm.NewStart);
        Assert.Null(vm.NewEnd);
        Assert.Equal("", vm.NewDescription);
        Assert.Equal(before, File.ReadAllBytes(DataFile));
    }

    [Fact]
    public void Form_SameDaysDifferentDescription_Rewrites()
    {
        var vm = NewVm(Dec18, WithDays(5m, Day(Mon2, DayType.Feriefri, 1m, "old")));
        Fill(vm, Mon2, Mon2, "new");
        vm.AddEntryCommand.Execute(null);
        Assert.Equal("new", Assert.Single(Saved()).Description);
        Assert.NotEqual("Those days were already booked as Feriefri.", vm.InfoMessage);
    }

    [Fact]
    public void Form_PartlyUnchanged_StillGivesPartialMessage()
    {
        // Allowance 3: Mon 2 same as requested (unchanged); Tue 3, Wed 4 fit; Thu 5, Fri 6 left out.
        var vm = NewVm(Dec18, WithDays(3m, Day(Mon2, DayType.Feriefri, 1m, "x")));
        Fill(vm, Mon2, Fri6, "x");
        vm.AddEntryCommand.Execute(null);
        Assert.Contains("left out", vm.InfoMessage);
        Assert.Equal(3, Saved().Count);
    }

    // ---- year change ----

    [Fact]
    public void YearChange_ClearsAllDragMarks()
    {
        var vm = NewVm(Dec18, Seed(0));
        vm.CalendarPreviewCommand.Execute(Drag(Mon2, Mon9));
        Assert.Contains(vm.CalendarMonths.SelectMany(m => m.Cells).OfType<DayCell>(), c => c.Drag != DragKind.None);
        vm.SelectedYear = 2027;
        vm.SelectedYear = 2026;
        Assert.DoesNotContain(vm.CalendarMonths.SelectMany(m => m.Cells).OfType<DayCell>(), c => c.Drag != DragKind.None);
        vm.CalendarPreviewCommand.Execute(Drag(Mon2, Mon9));
        vm.SelectedYear = 2027;
        Assert.DoesNotContain(vm.CalendarMonths.SelectMany(m => m.Cells).OfType<DayCell>(), c => c.Drag != DragKind.None);
    }
}
