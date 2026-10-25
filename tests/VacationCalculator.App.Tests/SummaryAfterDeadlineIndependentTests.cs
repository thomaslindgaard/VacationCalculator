using System.Globalization;
using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>
/// Summary table after a deadline (a leftover must not be shown as remaining in the old year AND as transferred in to
/// the next), plus decimal-comma tooltips. Counted by hand.
/// </summary>
public class SummaryAfterDeadlineIndependentTests : HolidayYearVmTestBase
{
    // Seed(22, transfer limit 5): 22 working days Mon 2026-01-05 .. Tue 02-03 (4 weeks = 20 to Fri 01-30, plus Mon 02-02, Tue 02-03)
    // in 2025/26, which earned 25 (20 own + 5th week). 3 unused 5th-week days, limit 5 -> 3 transferred into 2026/27.
    private MainViewModel Vm(DateOnly today, VacationData data)
    {
        var vm = NewVm(today, data);
        vm.SelectedYear = 2026; // rows 2025/26 and 2026/27
        return vm;
    }

    [Fact]
    public void AfterDeadline_LeftoverIsRemainingInNeitherYearTwice()
    {
        var vm = Vm(new DateOnly(2027, 1, 5), Seed(22, transferAllowed: true));
        Assert.Equal(["2025/26", "2026/27"], vm.SummaryRows.Select(r => r.Name));
        var a = vm.SummaryRows[0];
        var b = vm.SummaryRows[1];

        Assert.Equal((25m, 22m, 0m, 0), (a.EarnedSoFar, a.Used, a.Remaining, a.RemainingSign));
        // 2026/27: Sep, Oct, Nov, Dec = 4 months * 25/12 = 8.33 earned, 3 transferred in (real, deadline passed).
        Assert.Equal(3m, b.TransferredIn);
        Assert.False(b.TransferredInIsProjected);
        Assert.Equal(8.33m, b.EarnedSoFar);
        Assert.Equal(11.33m, b.Remaining);
        Assert.Equal(1, b.RemainingSign);
        // Each day counted once: the 3 days are only in the new year.
        Assert.Equal(b.EarnedSoFar + b.TransferredIn, b.Remaining);
        Assert.Equal(a.Remaining + b.Remaining, b.Remaining);
        // Total = vacation + Feriefri of the same row.
        Assert.Equal(a.Remaining + a.FeriefriRemaining, a.TotalRemaining);
        Assert.Equal(b.Remaining + b.FeriefriRemaining, b.TotalRemaining);
    }

    [Fact]
    public void DeadlineDay_31Dec_StillOpen_1Jan_Closed()
    {
        var onDay = Vm(new DateOnly(2026, 12, 31), Seed(22, transferAllowed: true));
        Assert.Equal((3m, 1), (onDay.SummaryRows[0].Remaining, onDay.SummaryRows[0].RemainingSign)); // 25 - 22, still usable
        Assert.Equal(3m, onDay.SummaryRows[1].TransferredIn);
        Assert.True(onDay.SummaryRows[1].TransferredInIsProjected);

        var next = Vm(new DateOnly(2027, 1, 1), Seed(22, transferAllowed: true));
        Assert.Equal((0m, 0), (next.SummaryRows[0].Remaining, next.SummaryRows[0].RemainingSign));
        Assert.Equal(3m, next.SummaryRows[1].TransferredIn);
        Assert.False(next.SummaryRows[1].TransferredInIsProjected);
        Assert.Equal(next.SummaryRows[1].EarnedSoFar + 3m, next.SummaryRows[1].Remaining);
    }

    [Fact]
    public void LostLeftover_AfterDeadline_IsZeroToo_NotTransferred()
    {
        // Limit 0: 3 unused days are lost at 31 Dec 2026 (paid out nothing, payout None). Nothing transferred, remaining 0.
        var vm = Vm(new DateOnly(2027, 1, 5), Seed(22));
        Assert.Equal((0m, 0), (vm.SummaryRows[0].Remaining, vm.SummaryRows[0].RemainingSign));
        Assert.Equal(0m, vm.SummaryRows[1].TransferredIn);
        Assert.Equal(8.33m, vm.SummaryRows[1].Remaining);
    }

    [Fact]
    public void OverBookedPastYear_StaysNegativeAfterDeadline()
    {
        // 27 working days Mon 2026-01-05 .. Tue 02-10 in 2025/26, which earned 25 (limit 0): 2 over-booked, -2 both on
        // the deadline day and after it; nothing is "unused" so nothing is added back.
        foreach (var today in new[] { new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 5) })
        {
            var vm = Vm(today, Seed(27));
            Assert.Equal((-2m, -1), (vm.SummaryRows[0].Remaining, vm.SummaryRows[0].RemainingSign));
        }
    }

    // ---------- comma culture ----------

    [Fact]
    public void Tooltips_UseTheDecimalComma_InDaDk()
    {
        CultureInfo.CurrentCulture = new CultureInfo("da-DK");
        CultureInfo.CurrentUICulture = new CultureInfo("da-DK");

        // Seed(25) fills 2025/26. In 2026/27 (earned at Dec 18 = 3 * 25/12 = 6.25) book 7 days: Mon 12-21, Tue 12-22,
        // Wed 12-23, Mon 12-28, Tue 12-29, Wed 12-30, Mon 2027-01-04 -> 6.25 - 7 = -0.75 -> advance 0.75.
        var data = Seed(25);
        foreach (var (y, m, d) in new[] { (2026, 12, 21), (2026, 12, 22), (2026, 12, 23), (2026, 12, 28), (2026, 12, 29), (2026, 12, 30), (2027, 1, 4) })
            data.Days.Add(new BookedDay { Date = new DateOnly(y, m, d), Amount = 1 });
        // Feriefri: 3.5 booked of 5 -> 1.5 left, lost on 31 Dec (2026/27 allowance 5, open 6.5 > 5: 1.5 above 5 is paid out
        // from the previous leftover) - so use allowance 1 for 2026/27: open 1.5 + 1 = 2.5, not above 5: 1.5 lost.
        data.Days.Add(new BookedDay { Date = new DateOnly(2026, 3, 2), Type = DayType.Feriefri });
        data.Days.Add(new BookedDay { Date = new DateOnly(2026, 3, 3), Type = DayType.Feriefri });
        data.Days.Add(new BookedDay { Date = new DateOnly(2026, 3, 4), Type = DayType.Feriefri });
        data.Days.Add(new BookedDay { Date = new DateOnly(2026, 3, 5), Type = DayType.Feriefri, Amount = 0.5m });
        data.SetFeriefriAllowance(new HolidayYear(2026), 1m);

        var vm = NewVm(Dec18, data);
        var a = vm.SummaryRows[0];
        var b = vm.SummaryRows[1];

        Assert.Equal("advance holiday: 0,75 not earned yet", b.RemainingTooltip);
        Assert.Equal("6,25 of 25", b.EarnedTooltip);
        Assert.Equal("1,5 will be lost", a.FeriefriRemainingTooltip);
    }

    [Fact]
    public void TransferredInTooltip_UsesTheDecimalComma_InDaDk()
    {
        CultureInfo.CurrentCulture = new CultureInfo("da-DK");
        // 21.5 days: 20 + 1.5 of the 5th week; 3.5 unused, limit 5 -> 3.5 projected into 2026/27 on 18 Dec 2026.
        var vm = NewVm(Dec18, Seed(21.5m, transferAllowed: true));
        Assert.StartsWith("3,5 (projected). Projected transfer from 2025/26 on 31 December 2026.", vm.SummaryRows[1].TransferredInTooltip);
    }
}
