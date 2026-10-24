using VacationCalculator.Core;
using static VacationCalculator.Core.Tests.TestData;

namespace VacationCalculator.Core.Tests;

/// <summary>
/// Clocks and first tracked years at the edges of <see cref="HolidayYear.MinStartYear"/>..<see cref="HolidayYear.MaxStartYear"/>.
/// Now that transfer is always on (default limit 5), the transfer chain runs from the first tracked year, so it must
/// never build a holiday year whose dates aren't representable (e.g. holiday year 0, Start = 0000-09-01).
/// </summary>
public class ExtremeYearTransferTests
{
    public static TheoryData<string> Year1Clocks => new()
    {
        "0001-01-01", // Mon, January: only holiday year 0 is open
        "0001-03-01", // Thu, March
        "0001-09-01", // Sat, September: holiday years 0 and 1 are open
        "0001-12-31", // Mon, December
    };

    [Theory]
    [MemberData(nameof(Year1Clocks))]
    public void DefaultFirstTracked_InYear1_IsClampedToHolidayYear1(string today)
    {
        Assert.Equal(new HolidayYear(1), VacationData.DefaultFirstTrackedHolidayYear(D(today)));
        Assert.Equal(new HolidayYear(1), Data().EffectiveFirstTrackedHolidayYear(D(today)));

        var data = Data();
        Assert.True(data.EnsureFirstTrackedHolidayYear(D(today)));
        Assert.Equal(1, data.FirstTrackedHolidayYear);
    }

    [Theory]
    [MemberData(nameof(Year1Clocks))]
    public void Year1Clock_CalendarYear2_TransfersFromHolidayYear1(string today)
    {
        var data = Data(openingBalance: 3);

        // Calendar year 2 = holiday year 1 (deadline 0002-12-31), the first one with representable dates.
        var status = BalanceCalculator.CalculateCalendarYear(data, 2, D(today));
        Assert.Equal(new HolidayYear(1), status.HolidayYear.HolidayYear);
        Assert.Equal(3, status.HolidayYear.TransferredIn);
        Assert.Equal(25, status.HolidayYear.EarnableDays);
        // Nothing booked: the 3 opening days are lost, the 5th week is transferred, the first 20 are lost (default payout).
        Assert.Equal(5, status.TransferredToNext);

        var rows = BalanceCalculator.CalculateHolidayYears(data, HolidayYear.InCalendarYear(2), D(today));
        Assert.Equal([new HolidayYear(1), new HolidayYear(2)], rows.Select(r => r.HolidayYear));
        Assert.Equal(3, rows[0].TransferredIn);
        Assert.Equal(5, rows[1].TransferredIn);
        Assert.True(rows[1].TransferredInIsProjected); // today is before 0002-12-31
    }

    [Theory]
    [MemberData(nameof(Year1Clocks))]
    public void Year1Clock_AllocatesBookingsInBothOpenYears(string today)
    {
        // Mon 0002-09-02 .. Fri 0002-09-06: holiday years 1 and 2 are open, the older one (1) takes them.
        var data = Data(Range("0002-09-02", "0002-09-06"), openingBalance: 2);

        var allocated = BalanceCalculator.Allocate(data, D(today));

        Assert.Equal(5, allocated.Sum(a => a.Amount));
        Assert.All(allocated, a => Assert.Equal(new HolidayYear(1), a.HolidayYear));
        var y1 = BalanceCalculator.CalculateHolidayYear(data, new HolidayYear(1), D(today));
        Assert.Equal(5, y1.BookedAheadDays);
        Assert.Equal(0, y1.UnusedTransferredInDays); // the 2 opening days are used first
    }

    [Theory]
    [InlineData("9999-01-01")] // Fri
    [InlineData("9999-06-01")] // Tue
    [InlineData("9999-12-31")] // Fri, DateOnly.MaxValue
    public void Year9999Clock_DoesNotThrow_AndTracksFrom9998(string today)
    {
        Assert.Equal(new HolidayYear(9998), VacationData.DefaultFirstTrackedHolidayYear(D(today)));

        var data = Data(openingBalance: 4);
        foreach (var year in new[] { 9998, 9999 })
        {
            var status = BalanceCalculator.CalculateCalendarYear(data, year, D(today));
            Assert.Equal(year, status.Year);
        }

        // Calendar year 9998 = holiday years 9997 (untracked) and 9998 (first tracked, gets the opening balance).
        var rows = BalanceCalculator.CalculateHolidayYears(data, HolidayYear.InCalendarYear(9998), D(today));
        Assert.Equal(0, rows[0].TransferredIn);
        Assert.Equal(0, rows[0].TransferredToNext);
        Assert.Equal(4, rows[1].TransferredIn);
        Assert.False(rows[1].TransferredInIsProjected);

        // Holiday year 9998 still transfers its 5th week (to 9999, which can't be represented but is never built).
        var last = BalanceCalculator.CalculateHolidayYear(data, new HolidayYear(9998), D(today));
        Assert.Equal(5, last.TransferredToNext);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void StoredFirstTrackedBelowRange_DefaultLimits_ChainStartsAtHolidayYear1(int stored)
    {
        // Sat 2026-10-24
        var today = D("2026-10-24");
        var data = Data(firstTracked: stored, openingBalance: 2);

        Assert.Equal(new HolidayYear(1), data.EffectiveFirstTrackedHolidayYear(today));
        Assert.Equal(stored, data.FirstTrackedHolidayYear); // the stored value itself is left alone

        var status = BalanceCalculator.CalculateCalendarYear(data, 2026, today);
        // Every year since 1 transfers its unused 5th week (5) to the next, so 2025/26 gets 5.
        Assert.Equal(5, status.HolidayYear.TransferredIn);
        Assert.Equal(5, status.TransferredToNext);

        var first = BalanceCalculator.CalculateHolidayYear(data, new HolidayYear(1), today);
        Assert.Equal(2, first.TransferredIn);
    }

    [Theory]
    [MemberData(nameof(Year1Clocks))]
    public void StoredFirstTrackedZero_Year1Clock_DoesNotThrow(string today)
    {
        var data = Data(firstTracked: 0);

        var status = BalanceCalculator.CalculateCalendarYear(data, 2, D(today));
        var rows = BalanceCalculator.CalculateHolidayYears(data, HolidayYear.InCalendarYear(2), D(today));

        Assert.Equal(0, status.HolidayYear.TransferredIn);
        Assert.Equal(5, rows[1].TransferredIn);
    }

    [Fact]
    public void StoredFirstTrackedAboveRange_IsClampedTo9998()
    {
        // Sat 2026-10-24
        var data = Data(firstTracked: 10000, openingBalance: 1);

        Assert.Equal(new HolidayYear(9998), data.EffectiveFirstTrackedHolidayYear(D("2026-10-24")));
        var status = BalanceCalculator.CalculateCalendarYear(data, 2026, D("2026-10-24"));
        Assert.Equal(0, status.HolidayYear.TransferredIn); // 2025 is before the first tracked year
        Assert.Equal(0, status.TransferredToNext);
    }
}
