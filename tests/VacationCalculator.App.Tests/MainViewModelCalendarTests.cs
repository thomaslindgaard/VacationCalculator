using System.Globalization;
using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>
/// Feature 5: the calendar tab's view model. Today is Fri 2026-12-18. Tooltips are English (DateText) whatever the machine culture.
/// </summary>
public class MainViewModelCalendarTests : HolidayYearVmTestBase, IDisposable
{
    private static readonly string NL = Environment.NewLine;

    private static DayCell Cell(MainViewModel vm, int month, int day) =>
        vm.CalendarMonths[month - 1].Cells.OfType<DayCell>().Single(c => !c.IsBlank && c.Date.Month == month && c.Date.Day == day);

    private static VacationData WithBookings(params BookedDay[] days)
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

    [Fact]
    public void CalendarTabIsTheDefault_AndSwitches()
    {
        var vm = NewVm(Dec18, Seed(0));
        Assert.True(vm.IsCalendarTab);
        Assert.False(vm.IsListTab);

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.IsListTab = true;
        Assert.False(vm.IsCalendarTab);
        Assert.Contains(nameof(MainViewModel.IsCalendarTab), raised);

        vm.IsCalendarTab = true;
        Assert.False(vm.IsListTab);
        vm.IsCalendarTab = false; // unchecking a radio button changes nothing by itself
        Assert.True(vm.IsCalendarTab);
    }

    [Fact]
    public void TwelveMonths_FirstWeekdayAndWeekNumbers()
    {
        var vm = NewVm(Dec18, Seed(0));
        Assert.Equal(12, vm.CalendarMonths.Count);
        Assert.Equal("January", vm.CalendarMonths[0].Name);
        Assert.Equal("December", vm.CalendarMonths[11].Name);

        // January 2026: 5 rows of 8 cells. Row 1 = week number 1, then Mon 12-29, Tue 12-30, Wed 12-31 blank, Thu 01-01.
        var jan = vm.CalendarMonths[0].Cells;
        Assert.Equal(40, jan.Count);
        Assert.Equal("1", ((WeekNumberCell)jan[0]).Text);
        Assert.True(((DayCell)jan[1]).IsBlank && ((DayCell)jan[2]).IsBlank && ((DayCell)jan[3]).IsBlank);
        Assert.False(((DayCell)jan[4]).IsBlank);
        Assert.Equal(new DateOnly(2026, 1, 1), ((DayCell)jan[4]).Date);
        Assert.Equal(["1", "2", "3", "4", "5"], jan.OfType<WeekNumberCell>().Select(w => w.Text));

        // December 2026: weeks 49..53.
        Assert.Equal(["49", "50", "51", "52", "53"], vm.CalendarMonths[11].Cells.OfType<WeekNumberCell>().Select(w => w.Text));

        // February 2026 starts Sunday: row 1 has six blanks, then Sun 1.
        var feb = vm.CalendarMonths[1].Cells.OfType<DayCell>().Take(7).ToList();
        Assert.All(feb.Take(6), c => Assert.True(c.IsBlank));
        Assert.Equal(new DateOnly(2026, 2, 1), feb[6].Date);
        Assert.Equal("1", feb[6].Text);
    }

    [Fact]
    public void FillsAndFlags()
    {
        var data = WithBookings(
            new BookedDay { Date = new(2026, 12, 21), Description = "xmas" },                            // Mon vacation full, future
            new BookedDay { Date = new(2026, 11, 2), Amount = 0.5m },                                     // Mon vacation half, past
            new BookedDay { Date = new(2026, 11, 3), Type = DayType.Feriefri },                           // Tue Feriefri full
            new BookedDay { Date = new(2026, 11, 4), Amount = 0.5m, Type = DayType.Feriefri },            // Wed Feriefri half
            new BookedDay { Date = new(2026, 12, 24), Description = "oops" });                           // Thu Danish holiday: conflict
        var vm = NewVm(Dec18, data);

        var xmas = Cell(vm, 12, 21);
        Assert.Equal(DayFill.Vacation, xmas.Fill);
        Assert.False(xmas.IsHalf);
        Assert.False(xmas.IsPast);

        var half = Cell(vm, 11, 2);
        Assert.Equal(DayFill.Vacation, half.Fill);
        Assert.True(half.IsHalf);
        Assert.True(half.IsPast);

        Assert.Equal(DayFill.Feriefri, Cell(vm, 11, 3).Fill);
        Assert.False(Cell(vm, 11, 3).IsHalf);
        Assert.Equal(DayFill.Feriefri, Cell(vm, 11, 4).Fill);
        Assert.True(Cell(vm, 11, 4).IsHalf);

        var conflict = Cell(vm, 12, 24);
        Assert.True(conflict.IsConflict);
        Assert.Equal(DayFill.Holiday, conflict.Fill);
        Assert.False(conflict.IsHalf);

        Assert.Equal(DayFill.Plain, Cell(vm, 12, 17).Fill);  // Thu
        Assert.Equal(DayFill.Weekend, Cell(vm, 12, 19).Fill); // Sat
        Assert.Equal(DayFill.Holiday, Cell(vm, 1, 1).Fill);

        var today = Cell(vm, 12, 18);
        Assert.True(today.IsToday);
        Assert.Single(vm.CalendarMonths.SelectMany(m => m.Cells).OfType<DayCell>(), c => c.IsToday);
        Assert.Single(vm.CalendarMonths.SelectMany(m => m.Cells).OfType<DayCell>(), c => c.IsConflict);
    }

    [Fact]
    public void Tooltips_English()
    {
        var data = WithBookings(
            new BookedDay { Date = new(2026, 12, 21), Description = "xmas" },
            new BookedDay { Date = new(2026, 11, 4), Amount = 0.5m, Type = DayType.Feriefri },
            new BookedDay { Date = new(2026, 12, 24), Description = "oops" });
        data.WithManualHoliday(new DateOnly(2026, 6, 5));
        var vm = NewVm(Dec18, data);

        Assert.Equal("Friday 18 December 2026", Cell(vm, 12, 18).Tooltip);
        Assert.Equal("Saturday 19 December 2026" + NL + "Weekend", Cell(vm, 12, 19).Tooltip);
        Assert.Equal("Friday 5 June 2026" + NL + "Public holiday (added by you)", Cell(vm, 6, 5).Tooltip);
        Assert.Equal("Thursday 1 January 2026" + NL + "New Year's Day", Cell(vm, 1, 1).Tooltip);
        Assert.Equal(
            "Monday 21 December 2026" + NL + "Vacation, full day" + NL + "Note: xmas" + NL + "Holiday year 2025/26",
            Cell(vm, 12, 21).Tooltip);
        Assert.Equal(
            "Wednesday 4 November 2026" + NL + "Feriefri, half day" + NL + "Holiday year 2025/26",
            Cell(vm, 11, 4).Tooltip);
        Assert.Equal(
            "Thursday 24 December 2026" + NL + "Christmas Eve" + NL + "Vacation, full day" + NL + "Note: oops" + NL
            + "Booked on a public holiday or weekend; not counted",
            Cell(vm, 12, 24).Tooltip);
    }

    [Fact]
    public void Tooltip_IsEnglishUnderDanishCulture()
    {
        var vm = NewVm(Dec18, Seed(0));
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("da-DK");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("da-DK");
        var fresh = new MainViewModel(new DataStore(DataFile), () => Dec18);
        Assert.Equal("Friday 18 December 2026", Cell(fresh, 12, 18).Tooltip);
        Assert.NotNull(vm);
    }

    [Fact]
    public void SplitDay_TooltipListsBothHolidayYears()
    {
        // 24.5 days from Mon 2026-01-05 leave holiday year 2025 half a day; Mon 2026-12-21 is split 0.5 / 0.5.
        var data = Seed(24.5m, transferAllowed: false);
        data.Days.Add(new BookedDay { Date = new(2026, 12, 21) });
        var vm = NewVm(Dec18, data);
        Assert.EndsWith("0.5 from 2025/26, 0.5 from 2026/27", Cell(vm, 12, 21).Tooltip);
        Assert.Equal(
            "Monday 21 December 2026" + NL + "Vacation, full day" + NL + "0.5 from 2025/26, 0.5 from 2026/27",
            Cell(vm, 12, 21).Tooltip);
    }

    [Fact]
    public void CalendarRebuilds_AfterBooking_YearChange_AndHolidayAddRemove()
    {
        var vm = NewVm(Dec18, Seed(0));
        var months = vm.CalendarMonths;
        Assert.Equal(DayFill.Plain, Cell(vm, 12, 22).Fill);

        vm.NewStart = new DateTime(2026, 12, 22);
        vm.NewEnd = new DateTime(2026, 12, 23);
        vm.AddEntryCommand.Execute(null);
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal(12, vm.CalendarMonths.Count);
        for (var i = 0; i < 12; i++)
            Assert.Same(months[i], vm.CalendarMonths[i]); // updated in place
        Assert.Equal(DayFill.Vacation, Cell(vm, 12, 22).Fill);
        Assert.Equal(DayFill.Vacation, Cell(vm, 12, 23).Fill);

        vm.NewHoliday = new DateTime(2026, 6, 5);
        vm.AddHolidayCommand.Execute(null);
        Assert.Equal(DayFill.Holiday, Cell(vm, 6, 5).Fill);
        vm.RemoveHolidayCommand.Execute(new DateOnly(2026, 6, 5));
        Assert.Equal(DayFill.Plain, Cell(vm, 6, 5).Fill);

        vm.DeleteEntryCommand.Execute(vm.Entries[0]);
        Assert.Equal(DayFill.Plain, Cell(vm, 12, 22).Fill);

        vm.SelectedYear = 2027;
        Assert.Equal(2027, vm.Calendar.Year);
        // Fri 2027-01-01: week 53 row, four blanks (Mon 2026-12-28 .. Thu 12-31), then Fri 01-01.
        var jan = vm.CalendarMonths[0].Cells;
        Assert.Equal("53", ((WeekNumberCell)jan[0]).Text);
        Assert.All(jan.Skip(1).Take(4), c => Assert.True(((DayCell)c).IsBlank));
        Assert.Equal(new DateOnly(2027, 1, 1), ((DayCell)jan[5]).Date);
        Assert.False(Cell(vm, 12, 22).IsPast); // the 2027 calendar of 2027-12-22: after today
        Assert.Equal(0, vm.CalendarMonths.SelectMany(m => m.Cells).OfType<DayCell>().Count(c => c.IsToday));
    }

    private static List<DayCell> AllDays(MainViewModel vm) =>
        vm.CalendarMonths.SelectMany(m => m.Cells).OfType<DayCell>().Where(c => !c.IsBlank).ToList();

    [Fact]
    public void YearChange_UpdatesEveryCell_NoStaleDates_AndFeb2027ShrinksToFourWeeks()
    {
        // 2026: Feb starts Sunday 1 -> 5 week rows (1, 2-8, 9-15, 16-22, 23-28). 2027: Feb starts Monday 1, 28 days -> 4 rows.
        var vm = NewVm(Dec18, WithBookings(
            new BookedDay { Date = new(2026, 12, 21) },
            new BookedDay { Date = new(2027, 3, 1), Description = "march" }));
        var months = vm.CalendarMonths.ToList();
        var feb = months[1];
        Assert.Equal(5, feb.Cells.OfType<WeekNumberCell>().Count());
        var janCells = months[0].Cells;
        var janWeek1 = (WeekNumberCell)janCells[0];
        var jan1Cell = (DayCell)janCells[4];
        var changes = new List<string?>();
        jan1Cell.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.SelectedYear = 2027;

        for (var i = 0; i < 12; i++)
            Assert.Same(months[i], vm.CalendarMonths[i]);
        Assert.Equal(4, feb.Cells.OfType<WeekNumberCell>().Count());
        Assert.Equal(32, feb.Cells.Count);
        // Fri 2027-01-01 is week 53 (of 2026); January 2027 has 5 rows, so cells were reused; Jan 1 cell is now column 5.
        Assert.Same(janCells, months[0].Cells);
        Assert.Equal("53", janWeek1.Text);
        Assert.True(jan1Cell.IsBlank); // index 4 is now Mon 2026-12-28 +3 = Thu: blank
        Assert.Contains(nameof(DayCell.IsBlank), changes);
        Assert.Contains(nameof(DayCell.Date), changes);

        var days = AllDays(vm);
        Assert.Equal(365, days.Count); // 2027 is not a leap year
        Assert.All(days, d => Assert.Equal(2027, d.Date.Year));
        Assert.Equal(365, days.Select(d => d.Date).Distinct().Count());
        // every month shows only its own dates
        for (var m = 1; m <= 12; m++)
            Assert.All(vm.CalendarMonths[m - 1].Cells.OfType<DayCell>().Where(c => !c.IsBlank), c => Assert.Equal(m, c.Date.Month));
        // Blank cells carry no flags
        Assert.All(vm.CalendarMonths.SelectMany(x => x.Cells).OfType<DayCell>().Where(c => c.IsBlank),
            c => { Assert.Equal(DayFill.Plain, c.Fill); Assert.Equal("", c.Tooltip); Assert.False(c.IsToday); });
        // the 2026 booking is gone, the 2027 one is there; nothing is "today"
        Assert.Equal(DayFill.Vacation, Cell(vm, 3, 1).Fill); // Mon 2027-03-01
        Assert.Equal(DayFill.Plain, Cell(vm, 12, 21).Fill);  // Tue 2027-12-21
        Assert.DoesNotContain(days, d => d.IsToday);
        Assert.DoesNotContain(days, d => d.IsPast);
        Assert.Equal(DayFill.Holiday, Cell(vm, 12, 25).Fill); // Saturday 2027-12-25 is still a public holiday (Christmas Day)

        // and back: Feb grows to 5 rows again, 365 days, today is flagged
        vm.SelectedYear = 2026;
        Assert.Equal(5, feb.Cells.OfType<WeekNumberCell>().Count());
        Assert.Equal(365, AllDays(vm).Count);
        Assert.Single(AllDays(vm), d => d.IsToday);
        Assert.Equal(new DateOnly(2026, 12, 18), AllDays(vm).Single(d => d.IsToday).Date);
        Assert.Equal(DayFill.Vacation, Cell(vm, 12, 21).Fill);
    }

    [Fact]
    public void LeapYear_Has366Days_AndFeb29()
    {
        var vm = NewVm(Dec18, Seed(0));
        vm.SelectedYear = 2028;
        Assert.Equal(366, AllDays(vm).Count);
        Assert.Equal(29, vm.CalendarMonths[1].Cells.OfType<DayCell>().Count(c => !c.IsBlank));
        vm.SelectedYear = 2027;
        Assert.Equal(28, vm.CalendarMonths[1].Cells.OfType<DayCell>().Count(c => !c.IsBlank));
    }

    [Fact]
    public void Booking_UpdatesOnlyAffectedCells_AndRaisesNotifications()
    {
        var vm = NewVm(Dec18, Seed(0));
        var before = AllDays(vm).ToDictionary(c => c.Date, c => (c.Fill, c.IsHalf, c.Tooltip));
        var raised = new Dictionary<DateOnly, List<string?>>();
        foreach (var c in AllDays(vm))
        {
            var list = raised[c.Date] = [];
            c.PropertyChanged += (_, e) => list.Add(e.PropertyName);
        }

        vm.NewStart = new DateTime(2026, 12, 22);
        vm.NewEnd = new DateTime(2026, 12, 22);
        vm.NewDescription = "trip";
        vm.IsHalfDay = true;
        vm.AddEntryCommand.Execute(null);
        Assert.Equal("", vm.ErrorMessage);

        foreach (var c in AllDays(vm))
        {
            if (c.Date == new DateOnly(2026, 12, 22))
            {
                Assert.Equal(DayFill.Vacation, c.Fill);
                Assert.True(c.IsHalf);
                Assert.Contains("Note: trip", c.Tooltip);
                Assert.Contains(nameof(DayCell.Fill), raised[c.Date]);
                Assert.Contains(nameof(DayCell.Tooltip), raised[c.Date]);
            }
            else
            {
                Assert.Equal(before[c.Date], (c.Fill, c.IsHalf, c.Tooltip));
                Assert.DoesNotContain(nameof(DayCell.Fill), raised[c.Date]);
            }
        }
    }

    [Fact]
    public void HolidayAddRemove_UpdatesThatCellOnly()
    {
        var vm = NewVm(Dec18, Seed(0));
        var cell = Cell(vm, 6, 5);
        Assert.Equal(DayFill.Plain, cell.Fill);
        var raised = new List<string?>();
        cell.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.NewHoliday = new DateTime(2026, 6, 5);
        vm.AddHolidayCommand.Execute(null);
        Assert.Same(cell, Cell(vm, 6, 5)); // same instance, updated in place
        Assert.Equal(DayFill.Holiday, cell.Fill);
        Assert.Equal("Friday 5 June 2026" + NL + "Public holiday (added by you)", cell.Tooltip);
        Assert.Contains(nameof(DayCell.Fill), raised);

        raised.Clear();
        vm.RemoveHolidayCommand.Execute(new DateOnly(2026, 6, 5));
        Assert.Equal(DayFill.Plain, cell.Fill);
        Assert.Equal("Friday 5 June 2026", cell.Tooltip);
        Assert.Contains(nameof(DayCell.Fill), raised);
    }

    [Fact]
    public void ConflictAndToday_ShowTogether()
    {
        // Fri 2026-12-18 is today; booking it is allowed (weekday) but a weekend/holiday booking is a conflict.
        // Today on a holiday: use a manual holiday on today and book it.
        var data = WithBookings(new BookedDay { Date = Dec18 });
        data.WithManualHoliday(Dec18);
        var vm = NewVm(Dec18, data);
        var c = Cell(vm, 12, 18);
        Assert.True(c.IsToday);
        Assert.True(c.IsConflict);
    }

    [Fact]
    public void MonthName_AndWeekNumbers_UpdateInPlaceOnYearChange()
    {
        var vm = NewVm(Dec18, Seed(0));
        var dec = vm.CalendarMonths[11];
        var weeks = dec.Cells.OfType<WeekNumberCell>().ToList();
        Assert.Equal(["49", "50", "51", "52", "53"], weeks.Select(w => w.Text));
        // December 2027: Wed 1 Dec -> weeks 48 (Dec 1-5), 49, 50, 51, 52 (Dec 27-31).
        vm.SelectedYear = 2027;
        Assert.Equal(["48", "49", "50", "51", "52"], dec.Cells.OfType<WeekNumberCell>().Select(w => w.Text));
        Assert.Same(weeks[0], dec.Cells.OfType<WeekNumberCell>().First());
        Assert.Equal("December", dec.Name);
    }

    [Fact]
    public void YearNavigationExtremes_BuildCalendars()
    {
        var vm = NewVm(Dec18, Seed(0));
        vm.SelectedYear = 9998;
        Assert.Equal(12, vm.CalendarMonths.Count);
        vm.SelectedYear = 2;
        Assert.Equal(12, vm.CalendarMonths.Count);
        Assert.Equal(2, vm.Calendar.Year);
    }

    [Fact]
    public void FailedSave_LeavesCalendarShowingSavedData()
    {
        var vm = NewVm(Dec18, Seed(0));
        ReadOnly(true);

        vm.NewStart = new DateTime(2026, 12, 22);
        vm.NewEnd = new DateTime(2026, 12, 22);
        vm.AddEntryCommand.Execute(null);
        Assert.StartsWith("Could not save", vm.ErrorMessage);
        Assert.Equal(DayFill.Plain, Cell(vm, 12, 22).Fill);

        vm.NewHoliday = new DateTime(2026, 6, 5);
        vm.AddHolidayCommand.Execute(null);
        Assert.Equal(DayFill.Plain, Cell(vm, 6, 5).Fill);

        ReadOnly(false);
        vm.AddEntryCommand.Execute(null);
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal(DayFill.Vacation, Cell(vm, 12, 22).Fill);
    }

    [Fact]
    public void FailedDelete_KeepsBookingInCalendar()
    {
        var vm = NewVm(Dec18, WithBookings(new BookedDay { Date = new(2026, 12, 22) }));
        ReadOnly(true);
        vm.DeleteEntryCommand.Execute(vm.Entries[0]);
        Assert.StartsWith("Could not save", vm.ErrorMessage);
        Assert.Equal(DayFill.Vacation, Cell(vm, 12, 22).Fill);
    }

    [Fact]
    public void Messages_SurviveTabSwitching()
    {
        var vm = NewVm(Dec18, Seed(0));
        vm.AddEntryCommand.Execute(null); // no dates
        Assert.Equal("Pick a start and end date.", vm.ErrorMessage);
        vm.IsListTab = true;
        Assert.Equal("Pick a start and end date.", vm.ErrorMessage);
        vm.IsCalendarTab = true;
        Assert.Equal("Pick a start and end date.", vm.ErrorMessage);
    }
}

