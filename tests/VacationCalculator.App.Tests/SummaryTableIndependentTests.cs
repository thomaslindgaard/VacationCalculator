using VacationCalculator.App.Services;
using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>
/// Feature 8, the simplified summary table (one <see cref="SummaryRow"/> per holiday year). Values are counted by hand.
///
/// Scenario (selected year 2026, so rows 2025/26 and 2026/27; first tracked holiday year 2025; transfer limit 0):
///  - opening balance 2 days -> 2025/26 transferred in = 2.
///  - Vacation Mon 2026-01-05 .. Tue 2026-02-10 = 27 working days (5 weeks = 25 to Fri 02-06, plus Mon 02-09 and Tue 02-10).
///    2025/26 has 25 own + 2 transferred in = 27, so it is exactly full: remaining 0.
///  - Vacation Wed 2026-12-16 and Fri 2026-12-18 (today when today = 12-18): used, in 2026/27 (2025/26 is full).
///  - Vacation Tue 12-22, Wed 12-23, Mon 12-28, Tue 12-29, Wed 12-30 and Mon 2027-01-04, Tue 2027-01-05 (24-26 and 31 Dec
///    are public holidays) = 7 days booked ahead, also in 2026/27.
///    2026/27: earned so far at Dec 18 = Sep, Oct, Nov = 3 * 25/12 = 6.25; used 2 + booked 7 -> 6.25 - 9 = -2.75.
///  - Feriefri: allowance 5 (own) for 2025/26, 2 (own) for 2026/27. Used in 2025/26: Mon 2026-03-02 (1), Tue 03-03 (half, 0.5),
///    Thu 12-17 (1) = 2.5; Mon 12-21 (after today) = 1 booked; oldest first so all of it is 2025/26.
///    Feriefri remaining: 2025/26 = 5 - 2.5 - 1 = 1.5; 2026/27 = 2.
///  - Total remaining: 2025/26 = 0 + 1.5 = 1.5; 2026/27 = -2.75 + 2 = -0.75.
///  - Year end 2026: open = 1.5 (leftover) + 2 = 3.5, not above 5 -> nothing paid out, the 1.5 leftover is lost.
///    Year end 2027: 2 leftover + 2 (2027/28 inherits 2) = 4 -> nothing paid out, the 2 leftover is lost.
/// </summary>
public class SummaryTableIndependentTests : HolidayYearVmTestBase, IDisposable
{
    private readonly List<ThemeService> _themes = [];

    public new void Dispose()
    {
        foreach (var t in _themes)
            t.Dispose();
        Loc.SetLanguage(AppLanguage.English);
        if (File.Exists(DataFile))
            File.SetAttributes(DataFile, FileAttributes.Normal);
        base.Dispose();
    }

    private static BookedDay V(int y, int m, int d, decimal amount = 1) => new() { Date = new DateOnly(y, m, d), Amount = amount };

    private static BookedDay F(int y, int m, int d, decimal amount = 1) =>
        new() { Date = new DateOnly(y, m, d), Type = DayType.Feriefri, Amount = amount };

    private static VacationData Scenario()
    {
        var data = Seed(27);
        data.OpeningBalanceDays = 2;
        foreach (var d in new[] { 16, 18, 22, 23, 28, 29, 30 })
            data.Days.Add(V(2026, 12, d));
        data.Days.Add(V(2027, 1, 4));
        data.Days.Add(V(2027, 1, 5));
        data.Days.Add(F(2026, 3, 2));
        data.Days.Add(F(2026, 3, 3, 0.5m));
        data.Days.Add(F(2026, 12, 17));
        data.Days.Add(F(2026, 12, 21));
        data.SetFeriefriAllowance(new HolidayYear(2025), 5m);
        data.SetFeriefriAllowance(new HolidayYear(2026), 2m);
        return data;
    }

    private MainViewModel BuildWithTheme(DateOnly today, VacationData data)
    {
        var store = new DataStore(DataFile);
        store.Save(data);
        var theme = new ThemeService(Path.Combine(Dir, "vacation-calculator-preferences.json"), () => false,
            () => System.Globalization.CultureInfo.InvariantCulture);
        _themes.Add(theme);
        return new MainViewModel(store, () => today, theme);
    }

    private VacationData Saved() => new DataStore(DataFile).Load();

    private static void Pick(MainViewModel vm, LanguageMode mode) =>
        vm.SelectedLanguage = vm.LanguageOptions.Single(o => o.Mode == mode);

    // ---------- every column, per row ----------

    [Fact]
    public void Dec18_EveryColumnOfBothRows()
    {
        var vm = NewVm(Dec18, Scenario());

        Assert.Equal(2, vm.SummaryRows.Count);
        var a = vm.SummaryRows[0];
        var b = vm.SummaryRows[1];

        Assert.Equal("2025/26", a.Name);
        Assert.Equal((2m, 25m, 27m, 0m, 0m, 0), (a.TransferredIn, a.EarnedSoFar, a.Used, a.BookedAhead, a.Remaining, a.RemainingSign));
        Assert.Equal((5m, true, 2.5m, 1m, 1.5m, 1), (a.AllowanceDays, a.AllowanceIsOwn, a.FeriefriUsed, a.FeriefriBookedAhead, a.FeriefriRemaining, a.FeriefriRemainingSign));
        Assert.Equal((1.5m, 1), (a.TotalRemaining, a.TotalRemainingSign));

        Assert.Equal("2026/27", b.Name);
        Assert.Equal((0m, 6.25m, 2m, 7m, -2.75m, -1), (b.TransferredIn, b.EarnedSoFar, b.Used, b.BookedAhead, b.Remaining, b.RemainingSign));
        Assert.Equal((2m, true, 0m, 0m, 2m, 1), (b.AllowanceDays, b.AllowanceIsOwn, b.FeriefriUsed, b.FeriefriBookedAhead, b.FeriefriRemaining, b.FeriefriRemainingSign));
        Assert.Equal((-0.75m, -1), (b.TotalRemaining, b.TotalRemainingSign));
    }

    [Fact]
    public void TodayCountsAsUsed_TheDayAfterIsBooked()
    {
        // Today Wed 2026-12-16: vacation 12-16 is used (today counts), 12-18 is now booked ahead: used 1, booked 6 + 2 = 8.
        // Feriefri: 12-17 and 12-21 are both after today: used 1.5 (Mar 2 and half of Mar 3), booked 2; remaining still 1.5.
        var vm = NewVm(new DateOnly(2026, 12, 16), Scenario());
        var b = vm.SummaryRows[1];
        Assert.Equal((1m, 8m), (b.Used, b.BookedAhead));
        Assert.Equal(-2.75m, b.Remaining); // 6.25 - 9, unchanged by the split
        var a = vm.SummaryRows[0];
        Assert.Equal((1.5m, 2m, 1.5m), (a.FeriefriUsed, a.FeriefriBookedAhead, a.FeriefriRemaining));

        // Today Thu 12-17: Feriefri 12-17 is used (today), 12-21 booked ahead.
        var vm2 = NewVm(new DateOnly(2026, 12, 17), Scenario());
        Assert.Equal((2.5m, 1m), (vm2.SummaryRows[0].FeriefriUsed, vm2.SummaryRows[0].FeriefriBookedAhead));
        Assert.Equal((1m, 8m), (vm2.SummaryRows[1].Used, vm2.SummaryRows[1].BookedAhead)); // 12-16 used; 12-18 now ahead
    }

    [Fact]
    public void TotalRemaining_IsVacationPlusFeriefri_AlsoWhenBothAreNegative()
    {
        // Allowance 0 for 2025/26. Oldest-year-first: the two March days (1.5) are before September and belong to 2025/26
        // -> remaining -1.5. Thu 12-17 (used) and Mon 12-21 (ahead) find 2025/26 without days and go to 2026/27 (allowance 2):
        // used 1, booked 1, remaining 0.
        var data = Scenario();
        data.SetFeriefriAllowance(new HolidayYear(2025), 0m);
        var vm = NewVm(Dec18, data);
        var a = vm.SummaryRows[0];
        var b = vm.SummaryRows[1];

        Assert.Equal((1.5m, 0m, -1.5m), (a.FeriefriUsed, a.FeriefriBookedAhead, a.FeriefriRemaining));
        Assert.Equal((0m, -1.5m, -1), (a.Remaining, a.TotalRemaining, a.TotalRemainingSign)); // vacation 0 + Feriefri -1.5
        Assert.Equal((1m, 1m, 0m), (b.FeriefriUsed, b.FeriefriBookedAhead, b.FeriefriRemaining));
        Assert.Equal((-2.75m, -2.75m, -1), (b.Remaining, b.TotalRemaining, b.TotalRemainingSign)); // -2.75 + 0
    }

    [Fact]
    public void ZeroTotal_HasSignZero()
    {
        // Nothing booked in the 2025/26 holiday year other than a full year of Feriefri: allowance 0 and no days -> 0.
        var data = Seed(25);
        data.SetFeriefriAllowance(new HolidayYear(2025), 0m);
        var vm = NewVm(Dec18, data);
        var a = vm.SummaryRows[0];
        Assert.Equal((0m, 0m, 0m, 0), (a.Remaining, a.FeriefriRemaining, a.TotalRemaining, a.TotalRemainingSign));
    }

    [Fact]
    public void OneColumnPerFigure_SelectedYearMovesTheRows()
    {
        var vm = NewVm(Dec18, Scenario());
        vm.SelectedYear = 2027;
        Assert.Equal(["2026/27", "2027/28"], vm.SummaryRows.Select(r => r.Name));
        // 2026/27 is now the expiring year: same figures as before, still joined to its own Feriefri row.
        Assert.Equal((-2.75m, 2m, -0.75m), (vm.SummaryRows[0].Remaining, vm.SummaryRows[0].FeriefriRemaining, vm.SummaryRows[0].TotalRemaining));
        // 2027/28 inherits the allowance 2 from 2026/27 and has nothing booked.
        Assert.Equal((2m, false, 2m), (vm.SummaryRows[1].AllowanceDays, vm.SummaryRows[1].AllowanceIsOwn, vm.SummaryRows[1].FeriefriRemaining));
    }

    // ---------- tooltips ----------

    [Fact]
    public void Tooltips_EveryStateInEnglish()
    {
        var vm = NewVm(Dec18, Scenario());
        var a = vm.SummaryRows[0];
        var b = vm.SummaryRows[1];

        Assert.Equal("25 of 25", a.EarnedTooltip);
        Assert.Equal("6.25 of 25", b.EarnedTooltip);
        Assert.Null(a.TransferredInTooltip);
        Assert.Null(b.TransferredInTooltip);
        Assert.Null(a.RemainingTooltip); // exactly 0, nothing to say
        Assert.Equal("advance holiday: 2.75 not earned yet", b.RemainingTooltip);
        Assert.Equal("1.5 will be lost", a.FeriefriRemainingTooltip);
        Assert.Equal("2 will be lost", b.FeriefriRemainingTooltip);
        Assert.StartsWith("Set for this holiday year\n", a.AllowanceTooltip);
        Assert.StartsWith("Set for this holiday year\n", b.AllowanceTooltip);
        Assert.Contains("Enter", a.AllowanceTooltip);
    }

    [Fact]
    public void Tooltip_ExceedsEarnable_WhenMoreIsBookedThanCanEverBeEarned()
    {
        // 26 days in 2025/26 without opening balance: 1 more than the 25 earnable.
        var vm = NewVm(Dec18, Seed(26));
        Assert.Equal("1 more than can be earned by 31 August", vm.SummaryRows[0].RemainingTooltip);
    }

    [Fact]
    public void Tooltip_ProjectedTransfer_OnlyWhileThePreviousYearIsOpen()
    {
        // Seed(20) with transfer on: 5 days unused in 2025/26 -> transfer limit 5 -> 5 projected into 2026/27 at Dec 18.
        var vm = NewVm(Dec18, Seed(20, transferAllowed: true));
        var b = vm.SummaryRows[1];
        Assert.Equal(5m, b.TransferredIn);
        Assert.Equal(
            "5 (projected). Projected transfer from 2025/26 on 31 December 2026. Until then these days still count as remaining in 2025/26.",
            b.TransferredInTooltip);
        Assert.Null(vm.SummaryRows[0].TransferredInTooltip);

        // Fri 2027-01-01: the deadline has passed, the transfer is real: no projection tooltip.
        var after = NewVm(new DateOnly(2027, 1, 1), Seed(20, transferAllowed: true));
        var row = after.SummaryRows.Single(r => r.Name == "2026/27");
        Assert.Equal(5m, row.TransferredIn);
        Assert.Null(row.TransferredInTooltip);
    }

    [Fact]
    public void Tooltip_ExceededFeriefriAllowance_IsInTheFeriefriRemainingTooltip()
    {
        // Allowance 2 for 2025/26 and 0 for 2026/27 with 3.5 Feriefri days booked: 2025/26 takes 2 (Mar 2, Mar 3 half = 1.5,
        // then half of Dec 17), the remaining 1.5 goes to the newest open year 2026/27, which provides 0: 1.5 more than the allowance.
        var data = Scenario();
        data.SetFeriefriAllowance(new HolidayYear(2025), 2m);
        data.SetFeriefriAllowance(new HolidayYear(2026), 0m);
        var vm = NewVm(Dec18, data);
        var b = vm.SummaryRows[1];
        Assert.Equal(-1.5m, b.FeriefriRemaining);
        Assert.Equal(-4.25m, b.TotalRemaining); // -2.75 + -1.5
        Assert.Contains("1.5 more than the allowance", b.FeriefriRemainingTooltip);
    }

    [Fact]
    public void Tooltip_FeriefriPayout_WhenAboveFiveAtYearEnd_AndLostRestJoinedBySemicolon()
    {
        // 2025/26 leftover 1.5 (allowance 5 - 3.5 booked) + 2026/27 allowance 6 = 7.5 open on 31 Dec 2026; above 5 by 2.5.
        // The previous leftover is paid first: all 1.5 of it, then 1 from 2026/27's own. Nothing of 2025/26 is lost.
        var data = Scenario();
        data.SetFeriefriAllowance(new HolidayYear(2026), 6m);
        var vm = NewVm(Dec18, data);
        Assert.Equal("1.5 will be paid out in January 2027", vm.SummaryRows[0].FeriefriRemainingTooltip);

        // Allowance 3 for 2026/27 instead: open 1.5 + 3 = 4.5 <= 5, nothing paid, 1.5 lost.
        data.SetFeriefriAllowance(new HolidayYear(2026), 3m);
        var vm2 = NewVm(Dec18, data);
        Assert.Equal("1.5 will be lost", vm2.SummaryRows[0].FeriefriRemainingTooltip);

        // Allowance 5 for 2026/27 (open 6.5, excess 1.5 paid from the previous leftover): "paid out" only.
        data.SetFeriefriAllowance(new HolidayYear(2026), 5m);
        var vm3 = NewVm(Dec18, data);
        Assert.Equal("1.5 will be paid out in January 2027", vm3.SummaryRows[0].FeriefriRemainingTooltip);
    }

    [Fact]
    public void Tooltips_AfterTheYearEnd_UsePastTense()
    {
        // Mon 2027-01-04: 2025/26's Feriefri leftover was lost on 31 Dec 2026 (1.5 + 2 = 3.5 open, not above 5).
        var vm = NewVm(new DateOnly(2027, 1, 4), Scenario());
        vm.SelectedYear = 2026;
        Assert.Equal("1.5 were lost", vm.SummaryRows[0].FeriefriRemainingTooltip);
    }

    [Fact]
    public void Tooltips_AreNullNeverEmpty_WhenThereIsNothingToSay()
    {
        // Mon 2026-03-02, nothing booked: no advance holiday, no transfer, nothing exceeded.
        var vm = NewVm(new DateOnly(2026, 3, 2), Seed(0));
        foreach (var r in vm.SummaryRows)
        {
            Assert.Null(r.RemainingTooltip);
            Assert.Null(r.TransferredInTooltip);
            Assert.NotEqual("", r.EarnedTooltip);
            Assert.NotEqual("", r.AllowanceTooltip);
            if (r.FeriefriRemainingTooltip is { } t)
                Assert.NotEqual("", t);
        }
        // 2026/27 has not started on 2 March 2026: no payout or lost note there.
        Assert.Null(vm.SummaryRows[1].FeriefriRemainingTooltip);
    }

    // ---------- Allotted editing ----------

    [Fact]
    public void Allotted_Edit_ChangesOnlyThatHolidayYear_AndPersists()
    {
        var vm = NewVm(Dec18, Scenario());

        vm.SummaryRows[1].AllowanceDays = 9m; // 2026/27: own 2 -> own 9, nothing booked there

        var again = NewVm(Dec18);
        Assert.Equal((5m, true, 1.5m), (again.SummaryRows[0].AllowanceDays, again.SummaryRows[0].AllowanceIsOwn, again.SummaryRows[0].FeriefriRemaining));
        Assert.Equal((9m, true, 9m), (again.SummaryRows[1].AllowanceDays, again.SummaryRows[1].AllowanceIsOwn, again.SummaryRows[1].FeriefriRemaining));
        Assert.Equal(6.25m, again.SummaryRows[1].TotalRemaining); // 9 - 2.75
        // The vacation side of the file is untouched.
        Assert.Equal(27 + 9, Saved().Days.Count(d => d.Type == DayType.Vacation)); // 27 in Jan/Feb + 2 used + 7 ahead
        Assert.Equal(4, Saved().Days.Count(d => d.Type == DayType.Feriefri));
    }

    [Theory]
    [InlineData(25, 20)]
    [InlineData(21, 20)]
    [InlineData(-1, 0)]
    [InlineData(-100, 0)]
    public void Allotted_OutOfRange_IsNeverStoredOutsideZeroToTwenty(int typed, int expected)
    {
        var vm = NewVm(Dec18, Scenario());

        vm.SummaryRows[1].AllowanceDays = typed;

        Assert.Equal(expected, vm.SummaryRows[1].AllowanceDays);
        Assert.InRange(NewVm(Dec18).SummaryRows[1].AllowanceDays, 0m, 20m);
        Assert.Equal(expected, NewVm(Dec18).SummaryRows[1].AllowanceDays);
    }

    [Fact]
    public void Allotted_Boundaries_ZeroAndTwenty_AreAccepted()
    {
        var vm = NewVm(Dec18, Scenario());
        vm.SummaryRows[1].AllowanceDays = 20m;
        Assert.Equal(20m, vm.SummaryRows[1].AllowanceDays);
        vm.SummaryRows[1].AllowanceDays = 0m;
        Assert.Equal(0m, vm.SummaryRows[1].AllowanceDays);
        Assert.True(vm.SummaryRows[1].AllowanceIsOwn);
        Assert.Equal(0m, vm.SummaryRows[1].FeriefriRemaining);
    }

    [Fact]
    public void Allotted_Reset_RemovesOwnValue_AndInheritsAgain()
    {
        var vm = NewVm(Dec18, Scenario());

        // Reset 2026/27 (own 2): it inherits 5 from 2025/26 (own 5).
        vm.SummaryRows[1].ResetAllowanceCommand.Execute(null);
        var b = vm.SummaryRows[1];
        Assert.Equal((5m, false), (b.AllowanceDays, b.AllowanceIsOwn));
        Assert.StartsWith("Inherited from 2025/26\n", b.AllowanceTooltip);
        Assert.Equal(5m, b.FeriefriRemaining);
        Assert.Equal(2.25m, b.TotalRemaining); // -2.75 + 5
        Assert.True(vm.SummaryRows[0].AllowanceIsOwn);

        // Reset 2025/26: no earlier year, so the default 5 (not own).
        vm.SummaryRows[0].ResetAllowanceCommand.Execute(null);
        var a = vm.SummaryRows[0];
        Assert.Equal((5m, false), (a.AllowanceDays, a.AllowanceIsOwn));
        Assert.StartsWith("Default allowance\n", a.AllowanceTooltip);

        Assert.Null(Saved().FindHolidayYear(2025)?.FeriefriAllowanceDays);
        Assert.Null(Saved().FindHolidayYear(2026)?.FeriefriAllowanceDays);
    }

    [Fact]
    public void Allotted_InheritanceFollowsAnEarlierYearEdit()
    {
        var vm = NewVm(Dec18, Scenario());
        vm.SummaryRows[1].ResetAllowanceCommand.Execute(null); // 2026/27 inherits

        vm.SummaryRows[0].AllowanceDays = 8m;

        Assert.Equal((8m, false), (vm.SummaryRows[1].AllowanceDays, vm.SummaryRows[1].AllowanceIsOwn));
        Assert.Equal(8m, vm.SummaryRows[1].FeriefriRemaining);
    }

    [Theory]
    [InlineData("set")]
    [InlineData("reset")]
    public void Allotted_FailedSave_RollsBack_AndReports(string what)
    {
        var vm = NewVm(Dec18, Scenario());
        var before = string.Join("|", vm.SummaryRows.Select(r => $"{r.Name} {r.AllowanceDays} {r.AllowanceIsOwn} {r.FeriefriRemaining} {r.TotalRemaining} {r.FeriefriRemainingTooltip}"));
        var bytes = File.ReadAllBytes(DataFile);
        File.SetAttributes(DataFile, FileAttributes.ReadOnly);

        if (what == "set")
            vm.SummaryRows[1].AllowanceDays = 12m;
        else
            vm.SummaryRows[1].ResetAllowanceCommand.Execute(null);

        var after = string.Join("|", vm.SummaryRows.Select(r => $"{r.Name} {r.AllowanceDays} {r.AllowanceIsOwn} {r.FeriefriRemaining} {r.TotalRemaining} {r.FeriefriRemainingTooltip}"));
        Assert.Equal(before, after);
        Assert.StartsWith("Could not save: ", vm.ErrorMessage);
        File.SetAttributes(DataFile, FileAttributes.Normal);
        Assert.Equal(bytes, File.ReadAllBytes(DataFile));
        Assert.Equal(2m, vm.SummaryRows[1].AllowanceDays);
    }

    [Fact]
    public void Allotted_SuccessfulEdit_ClearsAnEarlierError()
    {
        var vm = NewVm(Dec18, Scenario());
        vm.NewStart = null;
        vm.AddEntryCommand.Execute(null);
        Assert.NotEqual("", vm.ErrorMessage);

        vm.SummaryRows[1].AllowanceDays = 4m;

        Assert.Equal("", vm.ErrorMessage);
    }

    // ---------- live language switch ----------

    [Fact]
    public void LanguageSwitch_RebuildsRowsAndTooltips()
    {
        var vm = BuildWithTheme(Dec18, Scenario());
        var englishRows = vm.SummaryRows;
        Assert.Equal("advance holiday: 2.75 not earned yet", vm.SummaryRows[1].RemainingTooltip);

        Pick(vm, LanguageMode.Danish);

        Assert.NotSame(englishRows, vm.SummaryRows);
        Assert.Equal("25 af 25", vm.SummaryRows[0].EarnedTooltip);
        Assert.Equal("6.25 af 25", vm.SummaryRows[1].EarnedTooltip);
        Assert.Equal("ferie på forskud: 2.75 endnu ikke optjent", vm.SummaryRows[1].RemainingTooltip);
        Assert.Equal("1.5 bortfalder", vm.SummaryRows[0].FeriefriRemainingTooltip);
        Assert.StartsWith("Valgt for dette ferieår\n", vm.SummaryRows[0].AllowanceTooltip);

        Pick(vm, LanguageMode.Romanian);
        Assert.Equal("25 din 25", vm.SummaryRows[0].EarnedTooltip);
        Assert.Equal("concediu în avans: 2.75 încă neacumulate", vm.SummaryRows[1].RemainingTooltip);
        Assert.StartsWith("Setat pentru acest an de concediu\n", vm.SummaryRows[0].AllowanceTooltip);

        Pick(vm, LanguageMode.English);
        Assert.Equal("advance holiday: 2.75 not earned yet", vm.SummaryRows[1].RemainingTooltip);
        // The figures do not depend on the language.
        Assert.Equal((-0.75m, 1.5m), (vm.SummaryRows[1].TotalRemaining, vm.SummaryRows[0].TotalRemaining));
    }

    [Fact]
    public void LanguageSwitch_KeepsAnEditedAllowance()
    {
        var vm = BuildWithTheme(Dec18, Scenario());
        vm.SummaryRows[1].AllowanceDays = 7m;

        Pick(vm, LanguageMode.Danish);

        Assert.Equal((7m, true), (vm.SummaryRows[1].AllowanceDays, vm.SummaryRows[1].AllowanceIsOwn));
    }

    // ---------- old data ----------

    [Fact]
    public void EmptyData_BuildsTwoRowsWithAllZeroFigures()
    {
        var vm = NewVm(Dec18);
        Assert.Equal(2, vm.SummaryRows.Count);
        Assert.All(vm.SummaryRows, r => Assert.Equal(0m, r.Used + r.BookedAhead + r.FeriefriUsed + r.FeriefriBookedAhead));
        Assert.Equal(0m, vm.SummaryRows[0].TransferredIn);
    }
}
