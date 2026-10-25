using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>
/// View-model tests for Feriefri (feature 3). Today is Fri 2026-12-18 unless stated; the selected year is 2026, whose
/// deadline year is holiday year 2025/26 (1 Sep 2025 .. 31 Aug 2026, deadline 31 Dec 2026). Calendar: Mon 2026-01-05,
/// Mon 2026-01-12, Mon 2026-12-28, Thu 2026-12-31, Mon 2027-01-04, Mon 2027-01-11.
/// </summary>
public class MainViewModelFeriefriTests : HolidayYearVmTestBase, IDisposable
{
    private static BookedDay FF(int y, int m, int d, decimal amount = 1) =>
        new() { Date = new DateOnly(y, m, d), Type = DayType.Feriefri, Amount = amount };

    /// <summary>Feriefri on the first <paramref name="n"/> weekdays from Mon 2026-01-05 (5 = Jan 5..9, 4 = Jan 5..8).</summary>
    private static List<BookedDay> JanFF(int n) =>
        Enumerable.Range(5, 5).Take(n).Select(d => FF(2026, 1, d)).ToList();

    private static VacationData WithDays(IEnumerable<BookedDay> days)
    {
        var data = new VacationData { Days = days.ToList() };
        // firstTracked fixed so that the store doesn't rewrite settings unpredictably.
        data.FirstTrackedHolidayYear = 2025;
        return data;
    }

    private static void Enter(MainViewModel vm, DateTime start, DateTime end, bool feriefri = true, bool half = false)
    {
        vm.BookFeriefri = feriefri;
        vm.NewStart = start;
        vm.NewEnd = end;
        if (half)
            vm.IsHalfDay = true;
        vm.NewDescription = " trip ";
        vm.AddEntryCommand.Execute(null);
    }

    private static readonly DateTime Jan12 = new(2026, 1, 12);
    private static readonly DateTime Jan16 = new(2026, 1, 16);

    private MainViewModel Reload(DateOnly today) => new(new DataStore(DataFile), () => today);

    // ---------- form ----------

    [Fact]
    public void Form_DefaultsToVacation_AndTogglesButtonText()
    {
        var vm = NewVm(Dec18);
        Assert.False(vm.BookFeriefri);
        Assert.True(vm.BookVacation);
        Assert.Equal("Add vacation", vm.AddButtonText);

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.BookFeriefri = true;
        Assert.False(vm.BookVacation);
        Assert.Equal("Add Feriefri", vm.AddButtonText);
        Assert.Contains(nameof(MainViewModel.BookVacation), raised);
        Assert.Contains(nameof(MainViewModel.AddButtonText), raised);

        vm.BookVacation = true;
        Assert.False(vm.BookFeriefri);
        Assert.Equal("Add vacation", vm.AddButtonText);
    }

    [Fact]
    public void Book_FullRange_Fits_SavesClearsFormAndCountsOnlyFeriefri()
    {
        var vm = NewVm(Dec18, WithDays([]));

        Enter(vm, Jan12, Jan16); // Mon..Fri = 5 days, allowance 5

        Assert.Equal("", vm.ErrorMessage);
        Assert.Null(vm.NewStart);
        Assert.Null(vm.NewEnd);
        Assert.Equal("", vm.NewDescription);
        var row = Assert.Single(vm.Entries);
        Assert.True(row.IsFeriefri);
        Assert.Equal(5m, row.Period.Amount);
        Assert.Equal("trip", row.Period.Description);
        Assert.Equal(0m, vm.Status.HolidayYear.UsedDays); // vacation figures unaffected
        Assert.Equal(5m, vm.SummaryRows[0].FeriefriUsed);
        Assert.Equal(0m, vm.SummaryRows[0].FeriefriRemaining);
        Assert.Equal(5, Reload(Dec18).Entries.Single().Period.Dates.Count); // persisted
    }

    [Fact]
    public void Book_Partial_SavesWhatFitted_ClearsForm_PluralMessage()
    {
        var vm = NewVm(Dec18, WithDays(JanFF(3))); // 2 left

        Enter(vm, Jan12, Jan16); // 5 requested, 2 fit

        Assert.Equal("2 Feriefri days booked; 3 days left out because the Feriefri days for 2025/26 are used up.", vm.InfoMessage);
        Assert.Null(vm.NewStart);
        Assert.Equal("", vm.NewDescription);
        Assert.Equal(5m, vm.SummaryRows[0].FeriefriUsed);
        Assert.Equal(5m, Reload(Dec18).SummaryRows[0].FeriefriUsed);
    }

    [Fact]
    public void Book_Partial_SingularMessage()
    {
        var vm = NewVm(Dec18, WithDays(JanFF(4))); // 1 left

        Enter(vm, Jan12, new DateTime(2026, 1, 13)); // 2 requested, 1 fits

        Assert.Equal("1 Feriefri day booked; 1 day left out because the Feriefri days for 2025/26 are used up.", vm.InfoMessage);
    }

    [Fact]
    public void Book_Refused_SavesNothing_KeepsForm_AndSaysSo()
    {
        var vm = NewVm(Dec18, WithDays(JanFF(5)));
        var before = File.ReadAllBytes(DataFile);

        Enter(vm, Jan12, Jan12);

        Assert.Equal("All 5 Feriefri days for 2025/26 are used.", vm.ErrorMessage);
        Assert.Equal(Jan12, vm.NewStart);
        Assert.Equal(Jan12, vm.NewEnd);
        Assert.Equal(" trip ", vm.NewDescription);
        Assert.True(vm.BookFeriefri);
        Assert.Equal(before, File.ReadAllBytes(DataFile));
        Assert.Single(vm.Entries);
    }

    [Fact]
    public void Book_HalfDayRequestedWhenNothingFits_Refused()
    {
        var vm = NewVm(Dec18, WithDays(JanFF(5)));
        vm.BookFeriefri = true;
        vm.NewStart = Jan12;
        vm.IsHalfDay = true;
        vm.AddEntryCommand.Execute(null);
        Assert.Equal("All 5 Feriefri days for 2025/26 are used.", vm.ErrorMessage);
        Assert.True(vm.IsHalfDay); // form kept
    }

    [Fact]
    public void Book_FullDayBecomesHalf_MessageHasEnglishDate()
    {
        var days = JanFF(4);
        days.Add(FF(2026, 1, 9, 0.5m)); // placeholder replaced below: 4 full + 1 half on Fri 01-09 = 4.5
        days.RemoveAll(d => d.Date == new DateOnly(2026, 1, 9) && d.Amount == 1);
        var vm = NewVm(Dec18, WithDays(days));

        Enter(vm, Jan12, Jan12); // full day requested, only 0.5 left

        Assert.Equal(
            "0.5 Feriefri days booked; 0.5 days left out because the Feriefri days for 2025/26 are used up. 12 January 2026 booked as a half day.",
            vm.InfoMessage);
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal(5m, vm.SummaryRows[0].FeriefriUsed);
    }

    [Fact]
    public void Book_HalfDay_Fits_NoMessage()
    {
        var vm = NewVm(Dec18, WithDays([]));
        vm.BookFeriefri = true;
        vm.NewStart = Jan12;
        vm.IsHalfDay = true;
        vm.AddEntryCommand.Execute(null);
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal(0.5m, vm.SummaryRows[0].FeriefriUsed);
        Assert.False(vm.IsHalfDay);
    }

    [Fact]
    public void Book_AutumnPlural_RefusalMentionsPayoutInParentheses()
    {
        // HY2025 used up in Jan; HY2026 allowance 8 with 5 booked Jan 2027 (Mon 01-04..Fri 01-08); at 31 Dec 2026 open is 8, 3 paid out.
        // Selected year 2027 (deadline year 2026/27); Mon 2027-01-11 does not fit.
        var data = WithDays(JanFF(5).Concat(Enumerable.Range(4, 5).Select(d => FF(2027, 1, d))));
        data.SetFeriefriAllowance(new HolidayYear(2026), 8m);
        var vm = NewVm(Dec18, data);
        vm.SelectedYear = 2027;

        Enter(vm, new DateTime(2027, 1, 11), new DateTime(2027, 1, 11));

        Assert.Equal("All 8 Feriefri days for 2026/27 are used (3 are paid out in January 2027).", vm.ErrorMessage);
    }

    [Fact]
    public void Book_VacationStillWorks_AndFeriefriDoesNotCountAsVacation()
    {
        var vm = NewVm(Dec18, WithDays(JanFF(2)));
        Enter(vm, new DateTime(2026, 2, 2), new DateTime(2026, 2, 3), feriefri: false); // vacation Mon+Tue
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal(2m, vm.Status.HolidayYear.UsedDays);
        Assert.Equal(2m, vm.SummaryRows[0].FeriefriUsed);
        Assert.Equal(2, vm.Entries.Count);
        Assert.Contains(vm.Entries, e => e.IsFeriefri);
        Assert.Contains(vm.Entries, e => !e.IsFeriefri);
    }

    [Fact]
    public void Book_AdjacentVacationAndFeriefri_AreSeparateRows()
    {
        var vm = NewVm(Dec18, WithDays([]));
        Enter(vm, Jan12, new DateTime(2026, 1, 13), feriefri: false);
        Enter(vm, new DateTime(2026, 1, 14), new DateTime(2026, 1, 15), feriefri: true);
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void Book_FeriefriOverVacation_ReplacesIt()
    {
        var vm = NewVm(Dec18, WithDays([new BookedDay { Date = new DateOnly(2026, 1, 12), Amount = 1 }]));
        Assert.Equal(1m, vm.Status.HolidayYear.UsedDays);
        Enter(vm, Jan12, Jan12);
        Assert.Equal(0m, vm.Status.HolidayYear.UsedDays);
        Assert.Equal(1m, vm.SummaryRows[0].FeriefriUsed);
    }

    [Fact]
    public void Delete_FeriefriEntry_FreesTheDays()
    {
        var vm = NewVm(Dec18, WithDays(JanFF(5)));
        vm.DeleteEntryCommand.Execute(vm.Entries[0]);
        Assert.Empty(vm.Entries);
        Assert.Equal(0m, vm.SummaryRows[0].FeriefriUsed);
        Assert.Equal(5m, vm.SummaryRows[0].FeriefriRemaining);
    }

    // ---------- per-row allowance ----------
    /// <summary>Where the allowance comes from, read off the first line of its tooltip (the old "Source" column text).</summary>
    private static string Src(SummaryRow r)
    {
        var first = r.AllowanceTooltip.Split('\n')[0];
        return first switch
        {
            "Default allowance" => "default",
            "Set for this holiday year" => "own",
            _ => first.Replace("Inherited from ", "from "),
        };
    }


    private static (decimal Days, string Source, bool Own) A(SummaryRow r) => (r.AllowanceDays, Src(r), r.AllowanceIsOwn);

    [Fact]
    public void RowAllowance_Default_SetOwn_Persist_Clamp_Reset()
    {
        var vm = NewVm(Dec18, WithDays([]));
        Assert.Equal((5m, "default", false), A(vm.SummaryRows[0]));
        Assert.StartsWith("Default allowance\n", vm.SummaryRows[0].AllowanceTooltip);

        vm.SummaryRows[0].AllowanceDays = 8m;
        Assert.Equal((8m, "own", true), A(vm.SummaryRows[0]));
        Assert.StartsWith("Set for this holiday year\n", vm.SummaryRows[0].AllowanceTooltip);
        Assert.Equal((8m, "own", true), A(Reload(Dec18).SummaryRows[0])); // persisted

        vm.SummaryRows[0].AllowanceDays = 25m; // clamped to 20
        Assert.Equal(20m, vm.SummaryRows[0].AllowanceDays);
        vm.SummaryRows[0].AllowanceDays = -1m; // clamped to 0
        Assert.Equal(0m, vm.SummaryRows[0].AllowanceDays);

        vm.SummaryRows[0].ResetAllowanceCommand.Execute(null);
        Assert.Equal((5m, "default", false), A(vm.SummaryRows[0]));
        Assert.Equal((5m, "default", false), A(Reload(Dec18).SummaryRows[0]));
        Assert.Equal("", vm.ErrorMessage);
    }

    [Fact]
    public void RowAllowance_SettingTheDefaultValue_MakesItOwn()
    {
        var vm = NewVm(Dec18, WithDays([]));
        vm.SummaryRows[0].AllowanceDays = 5m;
        Assert.True(vm.SummaryRows[0].AllowanceIsOwn);
        Assert.Equal("own", Src(vm.SummaryRows[0]));
    }

    [Fact]
    public void RowAllowance_EditingLaterYear_DoesNotTouchEarlierYear()
    {
        var vm = NewVm(Dec18, WithDays([]));
        Assert.Equal("2025/26", vm.SummaryRows[0].Name);
        Assert.Equal("2026/27", vm.SummaryRows[1].Name);

        vm.SummaryRows[1].AllowanceDays = 8m;

        Assert.Equal((5m, "default", false), A(vm.SummaryRows[0]));
        Assert.Equal(5m, vm.SummaryRows[0].FeriefriRemaining);
        Assert.Equal((8m, "own", true), A(vm.SummaryRows[1]));
        Assert.Equal(8m, vm.SummaryRows[1].FeriefriRemaining);
        var again = Reload(Dec18);
        Assert.Equal((5m, "default", false), A(again.SummaryRows[0]));
        Assert.Equal((8m, "own", true), A(again.SummaryRows[1]));
    }

    [Fact]
    public void RowAllowance_EditingEarlierYear_IsInheritedByLaterYear_AndResetFallsBackToInherited()
    {
        var vm = NewVm(Dec18, WithDays([]));
        vm.SummaryRows[0].AllowanceDays = 8m;
        Assert.Equal((8m, "from 2025/26", false), A(vm.SummaryRows[1]));
        Assert.StartsWith("Inherited from 2025/26\n", vm.SummaryRows[1].AllowanceTooltip);

        vm.SummaryRows[1].AllowanceDays = 3m;
        Assert.Equal((3m, "own", true), A(vm.SummaryRows[1]));
        Assert.Equal((8m, "own", true), A(vm.SummaryRows[0]));

        vm.SummaryRows[1].ResetAllowanceCommand.Execute(null);
        Assert.Equal((8m, "from 2025/26", false), A(vm.SummaryRows[1]));

        vm.SelectedYear = 2028; // rows 2027/28 and 2028/29 inherit from 2025/26
        Assert.Equal((8m, "from 2025/26", false), A(vm.SummaryRows[0]));
    }

    [Fact]
    public void RowAllowance_Reset_WhenNothingOwn_IsHarmless()
    {
        var vm = NewVm(Dec18, WithDays([]));
        vm.SummaryRows[1].ResetAllowanceCommand.Execute(null);
        Assert.Equal((5m, "default", false), A(vm.SummaryRows[1]));
        Assert.Equal("", vm.ErrorMessage);
    }

    [Fact]
    public void RowAllowance_SettingSameOwnValueAgain_ChangesNothing()
    {
        var vm = NewVm(Dec18, WithDays([]));
        vm.SummaryRows[0].AllowanceDays = 8m;
        vm.SummaryRows[0].AllowanceDays = 8m;
        Assert.Equal((8m, "own", true), A(vm.SummaryRows[0]));
        Assert.Equal("", vm.ErrorMessage);
    }

    [Fact]
    public void RowAllowance_Change_RefreshesRowsAndBanner()
    {
        var vm = NewVm(Dec18, WithDays([]));
        Assert.True(vm.HasFeriefriBanner);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.SummaryRows[0].AllowanceDays = 0m; // HY2025 none: nothing remains to pay out or lose from it
        Assert.Equal(0m, vm.SummaryRows[0].FeriefriRemaining);
        Assert.Contains(nameof(MainViewModel.SummaryRows), raised);
        Assert.Contains(nameof(MainViewModel.FeriefriBanner), raised);
    }

    [Theory]
    [InlineData(0, "set")]
    [InlineData(1, "set")]
    [InlineData(1, "reset")]
    public void RowAllowance_FailedSave_RollsBack_AndReportsError(int row, string what)
    {
        var data = WithDays([]);
        data.SetFeriefriAllowance(new HolidayYear(2026), 7m);
        var vm = NewVm(Dec18, data);
        var before = State(vm);
        var bytes = File.ReadAllBytes(DataFile);
        ReadOnly(true);

        if (what == "set")
            vm.SummaryRows[row].AllowanceDays = 12m;
        else
            vm.SummaryRows[row].ResetAllowanceCommand.Execute(null);

        Assert.Equal(before, State(vm));
        Assert.Equal(7m, vm.SummaryRows[1].AllowanceDays);
        Assert.StartsWith("Could not save: ", vm.ErrorMessage);
        Assert.Equal("", vm.InfoMessage);
        ReadOnly(false);
        Assert.Equal(bytes, File.ReadAllBytes(DataFile));
    }

    // ---------- partial text with a year-end payout, info vs error ----------

    [Fact]
    public void Book_Partial_WithPayout_Plural()
    {
        // HY2025 used up (Jan 5..9 2026); HY2026 allowance 12: at 31 Dec 2026 open 12, 12-5 = 7 paid out, 5 usable from January.
        // Request Mon 2027-01-04 .. Fri 2027-01-15 = 10 weekdays: 5 fit, 5 left out.
        var data = WithDays(JanFF(5));
        data.SetFeriefriAllowance(new HolidayYear(2026), 12m);
        var vm = NewVm(Dec18, data);
        vm.SelectedYear = 2027;

        Enter(vm, new DateTime(2027, 1, 4), new DateTime(2027, 1, 15));

        Assert.Equal(
            "5 Feriefri days booked; 5 days left out: from January only 5 of the 12 Feriefri days for 2026/27 can be used, " +
            "because 7 are paid out in January 2027 unless used by 31 December 2026.",
            vm.InfoMessage);
        Assert.Equal("", vm.ErrorMessage);
    }

    [Fact]
    public void Book_Partial_WithPayout_Singular()
    {
        // HY2026 allowance 6: 1 paid out at the year end, 5 usable. Request Mon 2027-01-04 .. Mon 01-11 = 6 weekdays: 5 fit, 1 left out.
        var data = WithDays(JanFF(5));
        data.SetFeriefriAllowance(new HolidayYear(2026), 6m);
        var vm = NewVm(Dec18, data);
        vm.SelectedYear = 2027;

        Enter(vm, new DateTime(2027, 1, 4), new DateTime(2027, 1, 11));

        Assert.Equal(
            "5 Feriefri days booked; 1 day left out: from January only 5 of the 6 Feriefri days for 2026/27 can be used, " +
            "because 1 is paid out in January 2027 unless used by 31 December 2026.",
            vm.InfoMessage);
        Assert.Equal("", vm.ErrorMessage);
    }

    [Fact]
    public void Messages_Refusal_IsErrorOnly_Partial_IsInfoOnly()
    {
        var vm = NewVm(Dec18, WithDays(JanFF(3))); // 2 left
        Enter(vm, Jan12, Jan16);
        Assert.NotEqual("", vm.InfoMessage);
        Assert.Equal("", vm.ErrorMessage);

        Enter(vm, new DateTime(2026, 1, 19), new DateTime(2026, 1, 19)); // now used up: refused
        Assert.Equal("", vm.InfoMessage); // cleared by the next attempt
        Assert.Equal("All 5 Feriefri days for 2025/26 are used.", vm.ErrorMessage);
    }

    [Fact]
    public void Messages_Validation_IsError_AndNextSuccessClearsBoth()
    {
        var vm = NewVm(Dec18, WithDays(JanFF(3)));
        Enter(vm, Jan12, Jan16); // info
        Assert.NotEqual("", vm.InfoMessage);

        vm.BookFeriefri = false;
        vm.NewStart = null;
        vm.NewEnd = null;
        vm.AddEntryCommand.Execute(null);
        Assert.Equal("Pick a start and end date.", vm.ErrorMessage);

        Enter(vm, new DateTime(2026, 2, 2), new DateTime(2026, 2, 2), feriefri: false); // successful vacation
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal("", vm.InfoMessage);
    }

    [Fact]
    public void Messages_YearNavigation_ClearsBoth()
    {
        var vm = NewVm(Dec18, WithDays(JanFF(3)));
        Enter(vm, Jan12, Jan16);
        Assert.NotEqual("", vm.InfoMessage);
        vm.SelectedYear = 2027;
        Assert.Equal("", vm.InfoMessage);
        Assert.Equal("", vm.ErrorMessage);

        vm.BookFeriefri = false;
        vm.NewStart = null;
        vm.AddEntryCommand.Execute(null);
        Assert.NotEqual("", vm.ErrorMessage);
        vm.SelectedYear = 2026;
        Assert.Equal("", vm.ErrorMessage);
    }

    [Fact]
    public void Messages_HolidayAddedToOtherYear_IsInfoNotError()
    {
        var vm = NewVm(Dec18, WithDays([]));
        vm.NewHoliday = new DateTime(2027, 1, 6);
        vm.AddHolidayCommand.Execute(null);
        Assert.Equal("Added to 2027's holidays.", vm.InfoMessage);
        Assert.Equal("", vm.ErrorMessage);
    }

    // ---------- notes for a holiday year that has not started ----------

    [Fact]
    public void Notes_NotYetStartedYear_HasNoPayoutOrLostNote_StartedYearStillHas()
    {
        var june = new DateOnly(2026, 6, 15); // 2026/27 starts 1 Sep 2026
        var vm = NewVm(june, WithDays([]));
        Assert.Equal("2025/26", vm.SummaryRows[0].Name);
        Assert.Equal("5 will be paid out in January 2027", vm.SummaryRows[0].FeriefriRemainingTooltip);
        Assert.Equal("2026/27", vm.SummaryRows[1].Name);
        Assert.Null(vm.SummaryRows[1].FeriefriRemainingTooltip);

        // Exactly on 1 Sep it has started.
        var sep1 = NewVmSecond(new DateOnly(2026, 9, 1), WithDays([]));
        Assert.Equal("5 will be paid out in January 2028", sep1.SummaryRows[1].FeriefriRemainingTooltip);
        // The day before it has not.
        var aug31 = NewVmSecond(new DateOnly(2026, 8, 31), WithDays([]));
        Assert.Null(aug31.SummaryRows[1].FeriefriRemainingTooltip);
    }

    [Fact]
    public void Notes_NotYetStartedYear_LostNoteHidden_ButMoreThanAllowanceAlwaysShown()
    {
        // Mon 2026-09-07..Fri 09-25 = 15 weekdays in 2026/27 (Sep days before 31 Dec use 2025/26's 5 first): 10 against allowance 5 = 5 more.
        var days = Enumerable.Range(7, 19).Select(d => FF(2026, 9, d))
            .Where(d => d.Date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList();
        var data = WithDays(days);
        data.SetFeriefriAllowance(new HolidayYear(2025), 5m);
        var vm = NewVm(new DateOnly(2026, 6, 15), data);
        var row = vm.SummaryRows[1];
        Assert.Equal("2026/27", row.Name);
        Assert.Equal("5 more than the allowance", row.FeriefriRemainingTooltip);
    }
    // ---------- rows ----------

    [Fact]
    public void Rows_Figures_And_PayoutNote_BeforeYearEnd()
    {
        // 3 used in Jan: HY2025 remaining 2; open 31 Dec = 2 + 5 = 7 -> 2 will be paid out in January 2027.
        var vm = NewVm(Dec18, WithDays(JanFF(3)));
        Assert.Equal(2, vm.SummaryRows.Count);
        var a = vm.SummaryRows[0];
        Assert.Equal("2025/26", a.Name);
        Assert.Equal((5m, "default", false), (a.AllowanceDays, Src(a), a.AllowanceIsOwn));
        Assert.Equal((3m, 0m, 2m, 1), (a.FeriefriUsed, a.FeriefriBookedAhead, a.FeriefriRemaining, a.FeriefriRemainingSign));
        Assert.Equal("2 will be paid out in January 2027", a.FeriefriRemainingTooltip);

        var b = vm.SummaryRows[1];
        Assert.Equal("2026/27", b.Name);
        Assert.Equal(5m, b.FeriefriRemaining);
        Assert.Equal("5 will be paid out in January 2028", b.FeriefriRemainingTooltip); // its own leftover at the 2027 year end (projected)
    }

    [Fact]
    public void Rows_BookedAheadAndSource()
    {
        // today Dec18: Mon 12-21 is booked ahead and goes to HY2025.
        var data = WithDays([FF(2026, 12, 21)]);
        data.SetFeriefriAllowance(new HolidayYear(2025), 8m);
        var vm = NewVm(Dec18, data);
        var a = vm.SummaryRows[0];
        // allowance 8: 3 were already paid out on 31 Dec 2025 (open 5 + 8 = 13, excess 8, 5 from the older year): 8 - 3 - 1 = 4.
        Assert.Equal((0m, 1m, 4m), (a.FeriefriUsed, a.FeriefriBookedAhead, a.FeriefriRemaining));
        Assert.Contains("3 were paid out in January 2026", a.FeriefriRemainingTooltip);
        Assert.Equal((8m, "own", true), (a.AllowanceDays, Src(a), a.AllowanceIsOwn));
        Assert.Equal((8m, "from 2025/26", false), (vm.SummaryRows[1].AllowanceDays, Src(vm.SummaryRows[1]), vm.SummaryRows[1].AllowanceIsOwn));
    }

    [Fact]
    public void Rows_LostNote_AndOverAllowanceNote()
    {
        // HY2026 allowance 0 -> HY2025's 5 leftover days are lost on 31 Dec 2026.
        var data = WithDays([]);
        data.SetFeriefriAllowance(new HolidayYear(2026), 0m);
        var vm = NewVm(Dec18, data);
        Assert.Equal("5 will be lost", vm.SummaryRows[0].FeriefriRemainingTooltip);

        // 7 stored in HY2025 (allowance 5)
        var over = NewVm(Dec18, WithDays(Enumerable.Range(5, 8).Select(d => FF(2026, 1, d)).Where(d => d.Date.DayOfWeek != DayOfWeek.Saturday && d.Date.DayOfWeek != DayOfWeek.Sunday)));
        var row = over.SummaryRows[0];
        Assert.Equal(-1m, row.FeriefriRemaining); // Jan 5..9 and Mon 12 = 6 weekdays (the 10th and 11th are a weekend)
        Assert.Equal(-1, row.FeriefriRemainingSign);
        Assert.Equal("1 more than the allowance", row.FeriefriRemainingTooltip);
    }

    [Fact]
    public void Rows_AfterTheYearEnd_UsesPastTense()
    {
        var today = new DateOnly(2027, 1, 4);
        // 5 leftover days of HY2025: "5 were paid out in January 2027".
        var vm = NewVm(today, WithDays([]));
        vm.SelectedYear = 2026;
        Assert.Equal("5 were paid out in January 2027", vm.SummaryRows[0].FeriefriRemainingTooltip);

    }

    [Fact]
    public void Rows_AfterTheYearEnd_Singular()
    {
        var today = new DateOnly(2027, 1, 4);
        var vm = NewVm(today, WithDays(JanFF(4)));
        vm.SelectedYear = 2026;
        Assert.Equal("1 was paid out in January 2027", vm.SummaryRows[0].FeriefriRemainingTooltip);
    }

    // ---------- banner ----------

    [Fact]
    public void Banner_BeforeYearEnd_Plural_AndOnThe31st()
    {
        var expected = "Feriefri: 5 days must be used by 31 December 2026, otherwise they are paid out in January.";
        var vm = NewVm(Dec18, WithDays([]));
        Assert.True(vm.HasFeriefriBanner);
        Assert.Equal(expected, vm.FeriefriBanner);

        Assert.Equal(expected, NewVm(Dec31, WithDays([])).FeriefriBanner); // the 31st itself still warns
    }

    [Fact]
    public void Banner_Singular_AndNoneWhenNothingToExpire_AndUpdatesAfterBooking()
    {
        var vm = NewVm(Dec18, WithDays(JanFF(4)));
        Assert.Equal("Feriefri: 1 day must be used by 31 December 2026, otherwise it is paid out in January.", vm.FeriefriBanner);

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Enter(vm, new DateTime(2026, 1, 9), new DateTime(2026, 1, 9)); // the fifth day

        Assert.Equal("", vm.FeriefriBanner);
        Assert.False(vm.HasFeriefriBanner);
        Assert.Contains(nameof(MainViewModel.FeriefriBanner), raised);
        Assert.Contains(nameof(MainViewModel.HasFeriefriBanner), raised);
    }

    [Fact]
    public void Banner_LostOnly_AndPaidAndLost()
    {
        var zero = WithDays([]);
        zero.SetFeriefriAllowance(new HolidayYear(2026), 0m);
        Assert.Equal("Feriefri: 5 days from 2025/26 will be lost if not used by 31 December 2026.", NewVm(Dec18, zero).FeriefriBanner);

        // HY2026 allowance 2: open 7, 2 paid out, 3 lost.
        var two = WithDays([]);
        two.SetFeriefriAllowance(new HolidayYear(2026), 2m);
        Assert.Equal(
            "Feriefri: 2 days must be used by 31 December 2026, otherwise they are paid out in January. " +
            "3 days from 2025/26 will be lost if not used by then.",
            NewVm(Dec18, two).FeriefriBanner);
    }

    [Fact]
    public void Banner_AfterYearEnd_PastTense()
    {
        var today = new DateOnly(2027, 1, 4);
        var vm = NewVm(today, WithDays([]));
        vm.SelectedYear = 2026;
        Assert.Equal("Feriefri: 5 days were paid out in January 2027.", vm.FeriefriBanner);

        var one = NewVmSecond(today, WithDays(JanFF(4)));
        one.SelectedYear = 2026;
        Assert.Equal("Feriefri: 1 day was paid out in January 2027.", one.FeriefriBanner);
    }

    private MainViewModel NewVmSecond(DateOnly today, VacationData data)
    {
        new DataStore(DataFile).Save(data);
        return Reload(today);
    }

    [Fact]
    public void Banner_FollowsSelectedYear()
    {
        // Selected 2027: 31 Dec 2027 is projected from today 2026-12-18. HY2026 leftover 5 + HY2027 5 -> 5 paid out.
        var vm = NewVm(Dec18, WithDays([]));
        vm.SelectedYear = 2027;
        Assert.Equal("Feriefri: 5 days must be used by 31 December 2027, otherwise they are paid out in January.", vm.FeriefriBanner);
    }

    // ---------- failed save ----------

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

    private static string InMemory(MainViewModel vm)
    {
        var field = typeof(MainViewModel).GetField("_data", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return JsonSerializer.Serialize(field.GetValue(vm), new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } });
    }

    private static string State(MainViewModel vm) => string.Join("\n",
        string.Join("|", vm.Entries.Select(e => $"{e.Period.Start}-{e.Period.End} {e.Period.Type} {e.Period.Amount}")),
        string.Join("|", vm.SummaryRows.Select(r => $"{r.Name} {r.AllowanceDays} {Src(r)} {r.FeriefriUsed} {r.FeriefriBookedAhead} {r.FeriefriRemaining} {r.FeriefriRemainingTooltip} {r.TotalRemaining}")),
        vm.FeriefriBanner,
        InMemory(vm));

    [Theory]
    [InlineData("partial booking")]
    [InlineData("full booking")]
    [InlineData("set allowance")]
    [InlineData("reset allowance")]
    public void FailedSave_RollsBack_ThenWorksWhenWritable(string what)
    {
        var data = WithDays(JanFF(3));
        data.SetFeriefriAllowance(new HolidayYear(2025), 6m);
        var vm = NewVm(Dec18, data);
        var before = State(vm);
        var bytes = File.ReadAllBytes(DataFile);
        ReadOnly(true);

        Action run = what switch
        {
            "partial booking" => () => Enter(vm, Jan12, Jan16), // 6-3 = 3 left, 5 requested
            "full booking" => () => Enter(vm, Jan12, Jan12),
            "set allowance" => () => vm.SummaryRows[0].AllowanceDays = 9m,
            _ => () => vm.SummaryRows[0].ResetAllowanceCommand.Execute(null),
        };
        run();

        Assert.Equal(before, State(vm));
        Assert.StartsWith("Could not save: ", vm.ErrorMessage);
        Assert.EndsWith(". Your change was not saved.", vm.ErrorMessage);
        if (what.EndsWith("booking"))
        {
            Assert.Equal(Jan12, vm.NewStart); // form kept for a retry
            Assert.Equal(" trip ", vm.NewDescription);
        }
        ReadOnly(false);
        Assert.Equal(bytes, File.ReadAllBytes(DataFile));

        run(); // now succeeds
        Assert.NotEqual(before, State(vm));
        Assert.DoesNotContain("Could not save", vm.ErrorMessage);
    }
}

