using VacationCalculator.Core;
using static VacationCalculator.Core.Tests.TestData;

namespace VacationCalculator.Core.Tests;

public class HolidayYearTests
{
    [Fact]
    public void Bounds_OfHolidayYear2025()
    {
        var year = new HolidayYear(2025);

        Assert.Equal(D("2025-09-01"), year.Start);    // Mon
        Assert.Equal(D("2026-08-31"), year.End);      // Mon
        Assert.Equal(D("2026-12-31"), year.Deadline); // Thu
        Assert.Equal("2025/26", year.ToString());
    }

    [Theory]
    [InlineData("2026-08-31", 2025)] // Mon, last day of holiday year 2025
    [InlineData("2026-09-01", 2026)] // Tue, first day of holiday year 2026
    [InlineData("2026-01-01", 2025)] // Thu
    [InlineData("2026-12-31", 2026)] // Thu
    [InlineData("2028-02-29", 2027)] // Tue, leap day
    public void Containing_SplitsAt1September(string date, int startYear)
    {
        Assert.Equal(new HolidayYear(startYear), HolidayYear.Containing(D(date)));
    }

    [Theory]
    [InlineData("2025-08-31", false)] // Sun, day before start
    [InlineData("2025-09-01", true)]  // Mon, start
    [InlineData("2026-08-31", true)]  // Mon, end of earning
    [InlineData("2026-12-31", true)]  // Thu, deadline
    [InlineData("2027-01-01", false)] // Fri, day after deadline
    public void IsInTakingPeriod_RunsFrom1SeptemberTo31DecemberNextYear(string date, bool expected)
    {
        Assert.Equal(expected, new HolidayYear(2025).IsInTakingPeriod(D(date)));
    }

    [Fact]
    public void Contains_IsTheEarningPeriodOnly()
    {
        var year = new HolidayYear(2025);
        Assert.True(year.Contains(D("2026-08-31")));
        Assert.False(year.Contains(D("2026-09-01")));
    }

    [Fact]
    public void WithDeadlineIn_IsTheYearStartingTheSeptemberBefore()
    {
        Assert.Equal(new HolidayYear(2025), HolidayYear.WithDeadlineIn(2026));
    }

    [Theory]
    [InlineData("2026-08-31", new[] { 2025 })]       // Mon
    [InlineData("2026-09-01", new[] { 2025, 2026 })] // Tue
    [InlineData("2026-12-31", new[] { 2025, 2026 })] // Thu
    [InlineData("2027-01-01", new[] { 2026 })]       // Fri
    public void OpenOn_HasTwoYearsFromSeptemberToDecember(string date, int[] startYears)
    {
        Assert.Equal(startYears.Select(y => new HolidayYear(y)), HolidayYear.OpenOn(D(date)));
    }

    [Fact]
    public void InCalendarYear_IsTheExpiringAndTheStartingYear()
    {
        Assert.Equal([new HolidayYear(2025), new HolidayYear(2026)], HolidayYear.InCalendarYear(2026));
    }
}

public class AccrualTests
{
    private static readonly HolidayYear Year2025 = new(2025);

    [Theory]
    [InlineData("2025-08-31", "0")]     // Sun, before the year
    [InlineData("2025-09-01", "0")]     // Mon, first day: September not yet complete
    [InlineData("2025-09-30", "0")]     // Tue, September still running
    [InlineData("2025-10-01", "2.08")]  // Wed, September usable: 25/12 = 2.0833
    [InlineData("2025-10-31", "2.08")]  // Fri
    [InlineData("2025-11-01", "4.17")]  // Sat, 2 months = 4.1667
    [InlineData("2025-12-01", "6.25")]  // Mon, 3 months
    [InlineData("2026-01-01", "8.33")]  // Thu, 4 months = 8.3333
    [InlineData("2026-02-01", "10.42")] // Sun, 5 months = 10.4167
    [InlineData("2026-03-01", "12.5")]  // Sun, 6 months
    [InlineData("2026-08-01", "22.92")] // Sat, 11 months = 22.9167
    [InlineData("2026-08-31", "22.92")] // Mon, August still running
    [InlineData("2026-09-01", "25")]    // Tue, all 12 months
    [InlineData("2027-06-01", "25")]    // Tue, long after
    public void EarnedAsOf_CountsCompletedMonths(string date, string expected)
    {
        Assert.Equal(M(expected), Accrual.EarnedAsOf(Year2025, null, D(date)));
    }

    [Fact]
    public void FullYear_IsExactly25()
    {
        Assert.Equal(25m, Accrual.EarnableDays(Year2025, null));
        Assert.Equal(25m, Accrual.EarnableDays(Year2025, D("2025-09-01"))); // employed from the first day
        Assert.Equal(25m, Accrual.EarnableDays(Year2025, D("2019-03-17"))); // long before
    }

    [Fact]
    public void EmployedMidMonth_PartialMonthIsProportional()
    {
        // Sun 2025-11-16: 15 of November's 30 days employed = half a month
        var since = D("2025-11-16");

        Assert.Equal(0m, Accrual.EarnedAsOf(Year2025, since, D("2025-11-30")));
        Assert.Equal(M("1.04"), Accrual.EarnedAsOf(Year2025, since, D("2025-12-01")));  // 25 * 0.5 / 12 = 1.0417
        Assert.Equal(M("3.13"), Accrual.EarnedAsOf(Year2025, since, D("2026-01-01")));  // 25 * 1.5 / 12 = 3.125
        Assert.Equal(M("19.79"), Accrual.EarnableDays(Year2025, since));                // 25 * 9.5 / 12 = 19.7917
    }

    [Fact]
    public void EmployedOnFirstOfMonthMidYear_EarnsWholeMonthsFromThere()
    {
        var since = D("2026-03-01"); // Sun

        Assert.Equal(0m, Accrual.EarnedAsOf(Year2025, since, D("2026-03-01")));
        Assert.Equal(M("2.08"), Accrual.EarnedAsOf(Year2025, since, D("2026-04-01")));
        Assert.Equal(M("12.5"), Accrual.EarnableDays(Year2025, since)); // Mar .. Aug = 6 months
    }

    [Fact]
    public void EmployedOnLastDayOfMonth_EarnsOneDaysShare()
    {
        // Sat 2026-01-31: 1/31 of January, then Feb .. Aug = 7 months. 25 * (7 + 1/31) / 12 = 14.6505
        Assert.Equal(M("14.65"), Accrual.EarnableDays(Year2025, D("2026-01-31")));
    }

    [Fact]
    public void EmployedAfterTheYear_EarnsNothing()
    {
        Assert.Equal(0m, Accrual.EarnableDays(Year2025, D("2026-09-01")));
        Assert.Equal(0m, Accrual.EarnedAsOf(Year2025, D("2026-09-01"), D("2027-01-01")));
    }

    [Fact]
    public void EmployedInLeapFebruary_UsesItsTwentyNineDays()
    {
        // Tue 2028-02-15: 15 of 29 days (15 .. 29), then Mar .. Aug 2028 = 6 months.
        // 25 * (6 * 29 + 15) / (12 * 29) = 4725 / 348 = 13.5776; February alone = 375 / 348 = 1.0776
        var year = new HolidayYear(2027);
        var since = D("2028-02-15");

        Assert.Equal(M("1.08"), Accrual.EarnedAsOf(year, since, D("2028-03-01")));
        Assert.Equal(M("13.58"), Accrual.EarnableDays(year, since));
    }
}
