using VacationCalculator.Core;
using static VacationCalculator.Core.Tests.TestData;

namespace VacationCalculator.Core.Tests;

/// <summary>
/// Status of a calendar year: the holiday year with its deadline on 31 December of that year, against the
/// free working days left in it. Bookings via <see cref="TestData.Fill"/> are in Jan/Feb 2026 (holiday year 2025).
/// </summary>
public class CalendarYearStatusTests
{
    private static CalendarYearStatus Status(VacationData data, int year, string today) =>
        BalanceCalculator.CalculateCalendarYear(data, year, D(today));

    // ---- Which holiday year ----

    [Fact]
    public void JanuaryToAugust_ConcernsTheYearStartedLastSeptember()
    {
        // Today Mon 2026-03-02. 2026 has 261 weekdays; Jan (22) + Feb (20) are gone -> 219 left, less the 8 Danish
        // holidays on weekdays after it (Thu 04-02, Fri 04-03, Mon 04-06, Thu 05-14, Mon 05-25, Thu 12-24, Fri 12-25,
        // Thu 12-31) -> 211.
        var s = Status(Data(), 2026, "2026-03-02");

        Assert.Equal(new HolidayYear(2025), s.HolidayYear.HolidayYear);
        Assert.Equal(D("2026-12-31"), s.Deadline);
        Assert.Equal(M("12.5"), s.HolidayYear.EarnedSoFar); // Sep .. Feb
        Assert.Equal(25m, s.UnbookedDays);
        Assert.Equal(211m, s.WorkingDaysLeft);
        Assert.Equal(BalanceStatus.DaysToPlan, s.Status);
    }

    [Fact]
    public void SeptemberToDecember_CurrentYearIsTheExpiringOne_NextYearTheNewOne()
    {
        // Today Sat 2026-10-24
        var current = Status(Data(), 2026, "2026-10-24");
        var next = Status(Data(), 2027, "2026-10-24");

        Assert.Equal(new HolidayYear(2025), current.HolidayYear.HolidayYear);
        Assert.Equal(new HolidayYear(2026), next.HolidayYear.HolidayYear);
        Assert.Equal(D("2027-12-31"), next.Deadline);
        Assert.Equal(M("2.08"), next.HolidayYear.EarnedSoFar);
        // Whole of 2027: 261 weekdays less 8 Danish holidays on weekdays (Fri 01-01, Thu 03-25, Fri 03-26, Mon 03-29,
        // Thu 05-06, Mon 05-17, Fri 12-24, Fri 12-31)
        Assert.Equal(253m, next.WorkingDaysLeft);
        Assert.Equal(BalanceStatus.DaysToPlan, next.Status);
    }

    [Fact]
    public void PastYear_IsExpiredAndSplitsPaidOutAndLost()
    {
        // Calendar year 2025 = holiday year 2024 (deadline Wed 2025-12-31); nothing booked
        var s = Status(Data(), 2025, "2026-10-24");

        Assert.Equal(new HolidayYear(2024), s.HolidayYear.HolidayYear);
        Assert.Equal(BalanceStatus.Expired, s.Status);
        Assert.Equal(0m, s.WorkingDaysLeft);
        Assert.Equal(0m, s.TransferredToNext);
        Assert.Equal(5m, s.PaidOutDays); // the 5th week
        Assert.Equal(20m, s.LostDays);
    }

    [Fact]
    public void PastYear_WithPayoutSome_PaysThatManyOfTheOtherDaysToo()
    {
        var s = Status(Data().WithPayout(2024, PayoutMode.Some, 5m), 2025, "2026-10-24");

        Assert.Equal(BalanceStatus.Expired, s.Status);
        Assert.Equal(10m, s.PaidOutDays);
        Assert.Equal(15m, s.LostDays);
    }

    [Fact]
    public void FutureYear_CountsTheWholeCalendarYear()
    {
        // 2028 is a leap year with 260 weekdays (Sat 2028-01-01 .. Sun 2028-12-31), less 7 Danish holidays on weekdays
        // (Thu 04-13, Fri 04-14, Mon 04-17, Thu 05-25, Mon 06-05, Mon 12-25, Tue 12-26) -> 253
        var s = Status(Data(), 2028, "2026-10-24");

        Assert.Equal(new HolidayYear(2027), s.HolidayYear.HolidayYear);
        Assert.Equal(0m, s.HolidayYear.EarnedSoFar);
        Assert.Equal(253m, s.WorkingDaysLeft);
        Assert.Equal(BalanceStatus.DaysToPlan, s.Status);
    }

    // ---- Statuses ----

    [Fact]
    public void FlagsDaysThatCannotFitBeforeTheDeadline_AndSplitsThem()
    {
        // From Fri 2026-12-18 (today, included): 18,21,22,23,28,29,30 = 7 (Thu 24, Fri 25, Thu 31 are Danish holidays).
        // 25 unbooked.
        var s = Status(Data().NoTransferFrom(2025), 2026, "2026-12-18"); // 2c: limit 0 (was the default "off")

        Assert.Equal(7m, s.WorkingDaysLeft);
        Assert.Equal(BalanceStatus.DaysWillBeLost, s.Status);
        Assert.Equal(18m, s.DaysAtRisk);
        Assert.Equal(0m, s.AtRiskTransferredDays);
        Assert.Equal(5m, s.AtRiskPaidOutDays);  // booking all 7 uses the first 20, the 5th week stays
        Assert.Equal(13m, s.AtRiskLostDays);
        Assert.Equal(5m, s.PaidOutDays);        // if nothing more is booked
        Assert.Equal(20m, s.LostDays);
    }

    [Fact]
    public void DefaultData_AtRiskFifthWeekIsTransferred_NotPaidOut()
    {
        // 2c: no limit stored -> default 5. From Fri 2026-12-18 (today): 7 free (24, 25, 31 are holidays),
        // 25 unbooked, 18 at risk.
        var s = Status(Data(), 2026, "2026-12-18");

        Assert.Equal(BalanceStatus.DaysWillBeLost, s.Status);
        Assert.Equal(18m, s.DaysAtRisk);
        Assert.Equal(5m, s.AtRiskTransferredDays);
        Assert.Equal(0m, s.AtRiskPaidOutDays);
        Assert.Equal(13m, s.AtRiskLostDays);
        Assert.Equal(5m, s.TransferredToNext);  // if nothing more is booked
        Assert.Equal(0m, s.PaidOutDays);
        Assert.Equal(20m, s.LostDays);
    }

    [Fact]
    public void DaysAtRiskWithinTheFifthWeek_AreAllPaidOut()
    {
        // 15 used -> 10 unbooked; 7 free from Fri 2026-12-18 (24, 25, 31 are holidays) -> 3 at risk, all 5th week
        var s = Status(Data(Fill(15)).NoTransferFrom(2025), 2026, "2026-12-18"); // 2c: limit 0

        Assert.Equal(3m, s.DaysAtRisk);
        Assert.Equal(3m, s.AtRiskPaidOutDays);
        Assert.Equal(0m, s.AtRiskLostDays);
        Assert.Equal(BalanceStatus.DaysWillBeLost, s.Status);
    }

    [Fact]
    public void DaysAtRiskCanBeHalf()
    {
        // 17.5 used -> 7.5 unbooked; 7 free from Fri 2026-12-18 (24, 25, 31 are holidays)
        var s = Status(Data(Fill(17.5m)).NoTransferFrom(2025), 2026, "2026-12-18"); // 2c: limit 0

        Assert.Equal(BalanceStatus.DaysWillBeLost, s.Status);
        Assert.Equal(0.5m, s.DaysAtRisk);
        Assert.Equal(0.5m, s.AtRiskPaidOutDays);
    }

    [Fact]
    public void AtRisk_TransferredInDaysAreUsedFirst_TheRestIsLost()
    {
        // 2025 -> 2026: 5 (Fill(20), transfer on). Calendar 2027, today Fri 2027-12-17: 2026 has 30, nothing booked.
        // Free: Fri 17, Mon 20 .. Thu 23, Mon 27 .. Thu 30 = 9 (Fri 24 and Fri 31 are holidays). Booking them uses the
        // 5 transferred in + 4 own, leaving 21 own: 5 transferred, 16 lost.
        var s = Status(Data(Fill(20), firstTracked: 2025), 2027, "2027-12-17");

        Assert.Equal(9m, s.WorkingDaysLeft);
        Assert.Equal(21m, s.DaysAtRisk);
        Assert.Equal(5m, s.AtRiskTransferredDays);
        Assert.Equal(0m, s.AtRiskPaidOutDays);
        Assert.Equal(16m, s.AtRiskLostDays);
        Assert.Equal(25m, s.LostDays); // if nothing more is booked: 5 transferred in + 20 own
    }

    [Fact]
    public void AtRiskSplit_TransferThenPayoutThenLost()
    {
        // Transfer on, payout some 4, nothing booked. 7 free from Fri 2026-12-18 (24, 25, 31 are holidays) -> 18 at risk:
        // 5 transferred, 4 paid out, 9 lost. If nothing is booked: 5 transferred, 4 paid out, 16 lost.
        var s = Status(Data(firstTracked: 2025).WithPayout(2025, PayoutMode.Some, 4m), 2026, "2026-12-18");

        Assert.Equal(25m, s.UnbookedDays);
        Assert.Equal(18m, s.DaysAtRisk);
        Assert.Equal(5m, s.AtRiskTransferredDays);
        Assert.Equal(4m, s.AtRiskPaidOutDays);
        Assert.Equal(9m, s.AtRiskLostDays);
        Assert.Equal(5m, s.TransferredToNext);
        Assert.Equal(4m, s.PaidOutDays);
        Assert.Equal(16m, s.LostDays);
        Assert.Equal(BalanceStatus.DaysWillBeLost, s.Status);
    }

    [Fact]
    public void DaysAtRiskThatAreAllTransferred_AreNotWillBeLost()
    {
        // 15 used -> 10 unbooked; 7 free from Fri 2026-12-18 (24, 25, 31 are holidays) -> 3 at risk, all within the
        // transfer limit of 5. Booking 5 more is enough, so there is still time: DaysToPlan.
        var s = Status(Data(Fill(15), firstTracked: 2025), 2026, "2026-12-18");

        Assert.Equal(3m, s.DaysAtRisk);
        Assert.Equal(3m, s.AtRiskTransferredDays);
        Assert.Equal(0m, s.AtRiskPaidOutDays);
        Assert.Equal(0m, s.AtRiskLostDays);
        Assert.Equal(BalanceStatus.DaysToPlan, s.Status);
    }

    [Fact]
    public void BannerSplit_SomeTransferredSomeLost()
    {
        // 17 used -> 8 unbooked: "if they are not booked, 5 days will be transferred to 2026/27 and 3 will be lost"
        var s = Status(Data(Fill(17), firstTracked: 2025), 2026, "2026-10-24");

        Assert.Equal(BalanceStatus.DaysToPlan, s.Status);
        Assert.Equal(8m, s.UnbookedDays);
        Assert.Equal(5m, s.TransferredToNext);
        Assert.Equal(0m, s.PaidOutDays);
        Assert.Equal(3m, s.LostDays);
        Assert.Equal(3m, s.HolidayYear.ExpiringDays);
        Assert.Equal("2026/27", s.NextHolidayYear.ToString());
    }

    [Fact]
    public void AfterTheDeadline_TransferredAndLost_IsExpired()
    {
        // Fri 2027-01-01: 15 used -> 5 were transferred, 5 lost
        var s = Status(Data(Fill(15), firstTracked: 2025), 2026, "2027-01-01");

        Assert.Equal(BalanceStatus.Expired, s.Status);
        Assert.Equal(5m, s.TransferredToNext);
        Assert.Equal(5m, s.LostDays);
    }

    [Fact]
    public void AllPlanned_WhenEverythingIsUsedOrBooked()
    {
        var s = Status(Data(Fill(25)), 2026, "2026-10-24");

        Assert.Equal(BalanceStatus.AllPlanned, s.Status);
    }

    [Fact]
    public void AllPlanned_WithHalfDays()
    {
        var s = Status(Data(Fill(24.5m).Append(Day("2026-12-21", 0.5m))), 2026, "2026-10-24"); // Mon

        Assert.Equal(0m, s.UnbookedDays);
        Assert.Equal(BalanceStatus.AllPlanned, s.Status);
    }

    [Fact]
    public void AllPlanned_WhenTheRestIsTransferred()
    {
        // 20 used -> 5 unbooked, all within the transfer limit: nothing will be paid out or lost
        var data = Data(Fill(20), firstTracked: 2025);
        var s = Status(data, 2026, "2026-10-24");

        Assert.Equal(5m, s.UnbookedDays);
        Assert.Equal(5m, s.TransferredToNext);
        Assert.Equal(0m, s.HolidayYear.ExpiringDays);
        Assert.Equal(BalanceStatus.AllPlanned, s.Status);
        // After the deadline too: nothing expired (Fri 2027-01-01)
        Assert.Equal(BalanceStatus.AllPlanned, Status(data, 2026, "2027-01-01").Status);
    }

    [Fact]
    public void AllPlanned_NotWhenTheRestIsPaidOut()
    {
        // Limit 0 (2c): the unbooked 5th week is paid out rather than taken, so there is something to plan
        var s = Status(Data(Fill(20)).NoTransferFrom(2025), 2026, "2026-10-24");

        Assert.Equal(5m, s.PaidOutDays);
        Assert.Equal(BalanceStatus.DaysToPlan, s.Status);
    }

    [Fact]
    public void AllPlanned_WhenNothingCanBeEarned()
    {
        // Employed from Tue 2026-09-01: holiday year 2025 earns nothing
        var s = Status(Data(employedSince: D("2026-09-01")), 2026, "2026-10-24");

        Assert.Equal(0m, s.HolidayYear.TotalDays);
        Assert.Equal(BalanceStatus.AllPlanned, s.Status);
    }

    [Fact]
    public void OverBooked_WhenMoreIsBookedThanCanBeEarned()
    {
        // 25 used plus Mon 2026-08-03
        var s = Status(Data(Fill(25).Append(Day("2026-08-03"))), 2026, "2026-10-24");

        Assert.Equal(BalanceStatus.OverBooked, s.Status);
        Assert.Equal(1m, s.HolidayYear.ExceedsEarnableBy);
        Assert.Equal(0m, s.DaysAtRisk);
    }

    [Fact]
    public void OverBookedByHalfADay()
    {
        var s = Status(Data(Fill(25).Append(Day("2026-07-13", 0.5m))), 2026, "2026-10-24"); // Mon

        Assert.Equal(-0.5m, s.UnbookedDays);
        Assert.Equal(BalanceStatus.OverBooked, s.Status);
    }

    [Fact]
    public void AutumnBookingsBeyondTheOldYear_GoToTheNewYear_NotOverBooked()
    {
        // 25 used, then Mon 2026-10-19: spills to holiday year 2026, so 2026's status is all planned
        var data = Data(Fill(25).Append(Day("2026-10-19")));

        Assert.Equal(BalanceStatus.AllPlanned, Status(data, 2026, "2026-10-24").Status);
        Assert.Equal(1m, Status(data, 2027, "2026-10-24").HolidayYear.UsedDays);
    }

    [Fact]
    public void AdvanceHolidayInTheNewYear_IsNotOverBooked()
    {
        // Employed from Tue 2026-09-01; Mon 2026-10-19 .. Fri 2026-10-23 = 5 taken with 2.08 earned
        var s = Status(Data(Range("2026-10-19", "2026-10-23"), employedSince: D("2026-09-01")), 2027, "2026-10-24");

        Assert.Equal(M("-2.92"), s.HolidayYear.RemainingDays);
        Assert.Equal(20m, s.UnbookedDays);
        Assert.Equal(BalanceStatus.DaysToPlan, s.Status);
    }

    // ---- Free working days left ----

    [Fact]
    public void WorkingDaysLeftExcludesHolidaysAndBookedDays()
    {
        // Mon 2026-12-21 .. Wed 2026-12-23 booked; 24, 25, 31 holidays. Today Fri 2026-12-18.
        var data = Data(Range("2026-12-21", "2026-12-23")).WithHolidays("2026-12-24", "2026-12-25", "2026-12-31");

        // Left: Fri 18 (today), Mon 28, Tue 29, Wed 30
        Assert.Equal(4m, Status(data, 2026, "2026-12-18").WorkingDaysLeft);
    }

    [Fact]
    public void HalfBookedDayLeavesHalfAFreeWorkingDay()
    {
        var data = Data([Day("2026-12-21", 0.5m), Day("2026-12-22")]) // Mon half, Tue full
            .WithHolidays("2026-12-24", "2026-12-25", "2026-12-31");

        // 18 (today) = 1, 21 = 0.5, 22 = 0, 23 = 1, 28/29/30 = 3
        Assert.Equal(5.5m, Status(data, 2026, "2026-12-18").WorkingDaysLeft);
    }

    [Fact]
    public void FeriefriDayIsNotAFreeWorkingDay()
    {
        var data = Data([Day("2026-12-28", type: DayType.Feriefri), Day("2026-12-29", 0.5m, DayType.Feriefri)]); // Mon, Tue

        var s = Status(data, 2026, "2026-12-25"); // Fri

        // Fri 25 (today, Juledag) = 0, 28 = 0, 29 = 0.5, Wed 30 = 1, Thu 31 (Nytårsaftensdag) = 0
        Assert.Equal(1.5m, s.WorkingDaysLeft);
        Assert.Equal(0m, s.HolidayYear.BookedDays);
    }

    [Fact]
    public void OnLastWorkingDayOfYear_TodayIsStillFree()
    {
        // Wed 2026-12-30 is today and unbooked (Thu 31 is Nytårsaftensdag): one free working day, so one unbooked day
        // still fits
        var s = Status(Data(Fill(24)).NoTransferFrom(2025), 2026, "2026-12-30"); // 2c: limit 0, else the 1 day is transferred

        Assert.Equal(1m, s.WorkingDaysLeft);
        Assert.Equal(0m, s.DaysAtRisk);
        Assert.Equal(BalanceStatus.DaysToPlan, s.Status);
    }

    [Fact]
    public void OnLastDayOfYear_NytaarsaftensdagIsNotFree()
    {
        // Thu 2026-12-31 is today but an automatic holiday: nothing is free, the one unbooked day can't be taken
        var s = Status(Data(Fill(24)).NoTransferFrom(2025), 2026, "2026-12-31"); // 2c: limit 0

        Assert.Equal(0m, s.WorkingDaysLeft);
        Assert.Equal(1m, s.DaysAtRisk);
        Assert.Equal(BalanceStatus.DaysWillBeLost, s.Status);
    }

    [Fact]
    public void OnLastWorkingDayOfYear_MoreThanOneUnbookedWillBeLost()
    {
        var s = Status(Data(Fill(23.5m)).NoTransferFrom(2025), 2026, "2026-12-30"); // Wed; 1.5 unbooked; 2c: limit 0

        Assert.Equal(1m, s.WorkingDaysLeft);
        Assert.Equal(0.5m, s.DaysAtRisk);
        Assert.Equal(BalanceStatus.DaysWillBeLost, s.Status);
    }

    [Fact]
    public void OnLastDayOfYear_AsHoliday_NothingIsFree()
    {
        var s = Status(Data(Fill(24)).NoTransferFrom(2025).WithHolidays("2026-12-31"), 2026, "2026-12-31"); // 2c: limit 0

        Assert.Equal(0m, s.WorkingDaysLeft);
        Assert.Equal(BalanceStatus.DaysWillBeLost, s.Status);
    }

    [Fact]
    public void TodayHalfBookedCountsHalf()
    {
        // Today Tue 2026-12-29 half booked (used): 0.5 + Wed 30 = 1.5 (Thu 31 is a holiday)
        var s = Status(Data([Day("2026-12-29", 0.5m)]), 2026, "2026-12-29");

        Assert.Equal(1.5m, s.WorkingDaysLeft);
        Assert.Equal(0.5m, s.HolidayYear.UsedDays);
        Assert.Equal(0m, s.HolidayYear.BookedAheadDays);
    }

    [Theory]
    [InlineData(DayType.Vacation)]
    [InlineData(DayType.Feriefri)]
    public void TodayFullyBookedIsNotFree(DayType type)
    {
        // Today Tue 2026-12-29 fully booked: only Wed 30 = 1 (Thu 31 is a holiday)
        Assert.Equal(1m, Status(Data([Day("2026-12-29", type: type)]), 2026, "2026-12-29").WorkingDaysLeft);
    }

    [Fact]
    public void TodayOnWeekendOrHolidayIsNotCounted()
    {
        // Sat 2026-12-26: Mon 28 .. Wed 30 = 3 (Thu 31 is a holiday). Thu 2026-12-24 (automatic, also entered by hand):
        // also 28 .. 30 = 3. Mon 2026-12-28 as a manual holiday: Tue 29, Wed 30 = 2.
        Assert.Equal(3m, Status(Data(), 2026, "2026-12-26").WorkingDaysLeft);
        Assert.Equal(3m, Status(Data(), 2026, "2026-12-24").WorkingDaysLeft);
        Assert.Equal(3m, Status(Data().WithHolidays("2026-12-24", "2026-12-25"), 2026, "2026-12-24").WorkingDaysLeft);
        Assert.Equal(2m, Status(Data().WithHolidays("2026-12-28"), 2026, "2026-12-28").WorkingDaysLeft);
    }

    [Fact]
    public void TodayOnFirstDayOfFollowingYear_IsExpired()
    {
        // Fri 2027-01-01 is the day after holiday year 2025's deadline
        var s = Status(Data(), 2026, "2027-01-01");

        Assert.Equal(0m, s.WorkingDaysLeft);
        Assert.Equal(BalanceStatus.Expired, s.Status);
    }

    [Fact]
    public void TodayOnLastDayOfPreviousYear_CountsTheWholeYear()
    {
        // Wed 2025-12-31; 2026 has 261 weekdays less 9 Danish holidays on weekdays = 252
        Assert.Equal(252m, Status(Data(), 2026, "2025-12-31").WorkingDaysLeft);
    }

    [Fact]
    public void TodayOnFirstWorkingDayOfYear_IncludesIt()
    {
        // Mon 2027-01-04 (Fri 01-01 is Nytårsdag): all 253 working days of 2027 are left, today included
        Assert.Equal(253m, Status(Data(), 2027, "2027-01-04").WorkingDaysLeft);
        Assert.Equal(252m, Status(Data().WithHolidays("2027-01-04"), 2027, "2027-01-04").WorkingDaysLeft);
        // Fri 2027-01-01 itself is not a working day
        Assert.Equal(253m, Status(Data(), 2027, "2027-01-01").WorkingDaysLeft);
    }

    [Fact]
    public void LeapYear_CountsFebruary29()
    {
        // 2028: 253 working days (see FutureYear_CountsTheWholeCalendarYear); Tue 2028-02-29 half booked, for holiday year 2027
        var s = Status(Data([Day("2028-02-29", 0.5m)]), 2028, "2027-12-31");

        Assert.Equal(0.5m, s.HolidayYear.BookedAheadDays);
        Assert.Equal(252.5m, s.WorkingDaysLeft);
    }

    [Fact]
    public void HolidaysOfOtherYearsDoNotLeakIn()
    {
        // A stray 2026 date stored under 2027's settings is ignored, so Mon 2026-12-28 stays a working day
        var data = Data();
        data.Years.Add(new YearSettings { Year = 2027, PublicHolidays = [D("2026-12-28")] });

        Assert.Equal(3m, Status(data, 2026, "2026-12-28").WorkingDaysLeft); // 28, 29, 30
    }
}
