using VacationCalculator.Core;
using static VacationCalculator.Core.Tests.TestData;

namespace VacationCalculator.Core.Tests;

/// <summary>
/// Feature 2b, independent of the implementation: expectations are counted by hand from section 2b of docs/features.md.
/// Full holiday year = 25 own days, the first 20 then the 5th week (5). Fill(n) books n weekdays from Mon 2026-01-05
/// (holiday year 2025); Fill(n, "2027-01-04") books in holiday year 2026; Fill(n, "2028-01-03") in 2027.
/// "Late" is a date after every deadline involved, so the balances report what actually happened.
/// </summary>
public class DeadlineTransferIndependentTests : IDisposable
{
    private const string Late = "2029-01-02";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "VacationCalculatorTests", Guid.NewGuid().ToString());

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static HolidayYearBalance Bal(VacationData data, int startYear, string today = Late) =>
        BalanceCalculator.CalculateHolidayYear(data, new HolidayYear(startYear), D(today));

    private static CalendarYearStatus Cal(VacationData data, int year, string today) =>
        BalanceCalculator.CalculateCalendarYear(data, year, D(today));

    // ------------------------------------------------------------ deadline order, one holiday year

    [Fact]
    public void Order_TransferFifthWeekFirst_ThenPayoutSome_ThenLost()
    {
        // 10 used -> 15 unused = 10 of the first 20 + the 5 5th-week days. Limit 5: the 5th week moves (5).
        // The 10 other days: payout Some 4 -> 4 paid out, 6 lost.
        var b = Bal(Data(Fill(10), firstTracked: 2025).WithPayout(2025, PayoutMode.Some, 4), 2025);

        Assert.Equal(5m, b.AtDeadline.TransferredDays);
        Assert.Equal(0m, b.AtDeadline.PaidOutFifthWeekDays);
        Assert.Equal(4m, b.AtDeadline.PaidOutOtherDays);
        Assert.Equal(6m, b.AtDeadline.LostOtherDays);
        Assert.Equal(15m, b.UnbookedDays);
    }

    [Fact]
    public void Order_LimitAbove5_MovesOtherDaysAfterTheFifthWeek()
    {
        // Same 15 unused, limit 12: 5 (5th week) + 7 other = 12 transferred; 3 other days left, payout All -> 3 paid.
        var b = Bal(Data(Fill(10), firstTracked: 2025).WithLimit(2025, 12).WithPayout(2025, PayoutMode.All), 2025);

        Assert.Equal(12m, b.TransferredToNext);
        Assert.Equal(3m, b.PaidOutDays);
        Assert.Equal(0m, b.LostDays);
        Assert.Equal(12m, Bal(Data(Fill(10), firstTracked: 2025).WithLimit(2025, 12), 2026).TransferredIn);
    }

    [Theory]
    // used, limit, payout mode, payout N, expected transferred, expected paid out, expected lost  (own 25, transfer on, nothing transferred in)
    [InlineData("1", "5", PayoutMode.None, "0", "5", "0", "19")]     // unused 24 = 5th week 5 + 19 other
    [InlineData("1", "5", PayoutMode.All, "0", "5", "19", "0")]
    [InlineData("1", "24", PayoutMode.None, "0", "24", "0", "0")]
    [InlineData("1", "30", PayoutMode.None, "0", "24", "0", "0")]    // capped at what is unused
    [InlineData("1", "10", PayoutMode.Some, "3", "10", "3", "11")]   // 5 + 5 other moved; 14 other left: 3 paid, 11 lost
    [InlineData("1", "0", PayoutMode.Some, "3", "0", "8", "16")]     // 5th week 5 paid + 3 of the 19 other
    [InlineData("10", "5", PayoutMode.Some, "3", "5", "3", "7")]     // unused 15: 5 moved, 10 other: 3 paid, 7 lost
    [InlineData("20", "0", PayoutMode.All, "0", "0", "5", "0")]      // only the 5th week left: paid
    [InlineData("22.5", "1", PayoutMode.None, "0", "1", "1.5", "0")] // 2.5 unused, all 5th week: 1 moved, 1.5 paid
    [InlineData("25", "5", PayoutMode.All, "0", "0", "0", "0")]
    [InlineData("4", "2", PayoutMode.Some, "100", "2", "19", "0")]   // unused 21: 2 moved, 3 5th-week paid, 16 other paid (N larger than the rest)
    public void Outcome_Grid(string used, string limit, PayoutMode mode, string payoutDays, string transferred, string paid, string lost)
    {
        var data = Data(Fill(M(used)), firstTracked: 2025).WithLimit(2025, M(limit)).WithPayout(2025, mode, M(payoutDays));

        var b = Bal(data, 2025);

        Assert.Equal(M(transferred), b.TransferredToNext);
        Assert.Equal(M(paid), b.PaidOutDays);
        Assert.Equal(M(lost), b.LostDays);
        Assert.Equal(25m - M(used), b.TransferredToNext + b.PaidOutDays + b.LostDays); // everything unused is accounted for once
    }

    [Fact]
    public void LimitZero_FifthWeekPaid_OthersLost()
    {
        // 2c: was "switch off, limit 12 ignored"; now limit 0 is how transfer is off (2025 tracked)
        var b = Bal(Data(Fill(10), firstTracked: 2025).WithLimit(2025, 0), 2025);

        Assert.Equal(0m, b.TransferredToNext);
        Assert.Equal(5m, b.PaidOutDays);
        Assert.Equal(10m, b.LostDays);
    }

    [Fact]
    public void OverBooked_NothingIsTransferredPaidOrLost()
    {
        var b = Bal(Data(Fill(26), firstTracked: 2025), 2025);

        Assert.Equal(0m, b.TransferredToNext);
        Assert.Equal(0m, b.PaidOutDays);
        Assert.Equal(0m, b.LostDays);
        Assert.Equal(-1m, b.UnbookedDays);
        Assert.Equal(0m, Bal(Data(Fill(26), firstTracked: 2025), 2026).TransferredIn);
    }

    [Fact]
    public void HalfDay_Remainder_IsTransferred()
    {
        // 24.5 used -> 0.5 unused 5th-week day, transferred.
        var data = Data(Fill(24.5m), firstTracked: 2025);

        Assert.Equal(0.5m, Bal(data, 2025).TransferredToNext);
        Assert.Equal(0.5m, Bal(data, 2026).TransferredIn);
    }

    [Fact]
    public void PartialYear_NoFifthWeek_DefaultLimitMovesNothing()
    {
        // Employed since 2026-03-01: Mar..Aug = 6 months = 12.5 days, so no 5th week at all (FifthWeekDays 0).
        // Section 2b step 2: days from the first 20 only move through the part of the limit above 5, so the
        // default limit 5 moves nothing (Holiday Act: only days beyond 4 weeks move). Limit 8 would move 3.
        var data = Data([Day("2026-03-02")], D("2026-03-01"), firstTracked: 2025); // 1 used

        var b = Bal(data, 2025, "2026-12-18");

        Assert.Equal(12.5m, b.EarnableDays);
        Assert.Equal(0m, b.FifthWeekDays);
        Assert.Equal(0m, b.TransferredToNext);
        Assert.Equal(11.5m, b.LostDays); // all 11.5 unused
        Assert.Equal(0m, b.PaidOutDays);
        Assert.Equal(3m, Bal(data.WithLimit(2025, 8), 2025, "2026-12-18").TransferredToNext);
    }

    // ------------------------------------------------------------ transferred-in days

    [Fact]
    public void TransferredIn_AreUsedBeforeOwnDays()
    {
        // 2025: 20 used -> 5 transferred into 2026 (30 available). 3 booked in 2026: the 5 transferred-in days go
        // first, so 2 of them are left and lost; own 25 are all unused: 5 moved on, 20 lost. Lost = 2 + 20 = 22.
        var data = Data(Fill(20).Concat(Fill(3, "2027-01-04")), firstTracked: 2025);

        var b = Bal(data, 2026);

        Assert.Equal(5m, b.TransferredIn);
        Assert.Equal(3m, b.UsedDays);
        Assert.Equal(2m, b.UnusedTransferredInDays);
        Assert.Equal(5m, b.TransferredToNext);
        Assert.Equal(0m, b.PaidOutDays);
        Assert.Equal(22m, b.LostDays);
        Assert.Equal(27m, b.UnbookedDays); // 30 - 3
    }

    [Fact]
    public void TransferredIn_FullyUsed_NothingLostFromThem_OwnFifthWeekMovesOn()
    {
        // 7 booked in 2026: 5 transferred-in + 2 own. Own unused 23 = 18 + the 5th week 5: 5 moved, 18 lost.
        var data = Data(Fill(20).Concat(Fill(7, "2027-01-04")), firstTracked: 2025);

        var b = Bal(data, 2026);

        Assert.Equal(0m, b.UnusedTransferredInDays);
        Assert.Equal(5m, b.TransferredToNext);
        Assert.Equal(18m, b.LostDays);
    }

    [Fact]
    public void TransferredIn_NotPaidOut_EvenWhenPayoutIsAll()
    {
        // 2026 nothing booked, payout All: transferred-in 5 lost (never paid out); own: 5th week moved, 20 other paid.
        var data = Data(Fill(20), firstTracked: 2025).WithPayout(2026, PayoutMode.All);

        var b = Bal(data, 2026);

        Assert.Equal(5m, b.UnusedTransferredInDays);
        Assert.Equal(5m, b.LostDays);
        Assert.Equal(20m, b.PaidOutDays);
        Assert.Equal(5m, b.TransferredToNext);
    }

    [Fact]
    public void TransferredIn_NeverTransferredAgain_2025DaysNeverReach2027()
    {
        // 2025 -> 5 into 2026. 2026 has nothing booked and a huge limit: it can move only its own 25 days, never the
        // 5 from 2025, so 2027 gets 25 (not 30) and the 5 are lost in 2026.
        var data = Data(Fill(20), firstTracked: 2025).WithLimit(2026, 100);

        var y2026 = Bal(data, 2026);
        var y2027 = Bal(data, 2027);

        Assert.Equal(25m, y2026.TransferredToNext);
        Assert.Equal(5m, y2026.UnusedTransferredInDays);
        Assert.Equal(5m, y2026.LostDays);
        Assert.Equal(25m, y2027.TransferredIn);
        Assert.Equal(50m, y2027.TotalDays); // 25 own + 25 in; guards against 55
    }

    [Fact]
    public void TransferredIn_AreNotFifthWeekDays()
    {
        // 2026 has 5 transferred in; FifthWeekDays stays its own 5 and the 30 days are not "35".
        var b = Bal(Data(Fill(20), firstTracked: 2025), 2026);

        Assert.Equal(5m, b.FifthWeekDays);
        Assert.Equal(30m, b.TotalDays);
    }

    [Fact]
    public void Chain_ThreeYears()
    {
        // 2025: 20 used -> 5 moved. 2026: 20 booked = 5 transferred-in + 15 own; own unused 10 = 5 first-20 + 5th week:
        // 5 moved, 5 lost. 2027: 5 in; 25 booked = 5 in + 20 own; own unused 5 (5th week) -> 5 moved to 2028.
        var data = Data(
            Fill(20).Concat(Fill(20, "2027-01-04")).Concat(Fill(25, "2028-01-03")), firstTracked: 2025);

        Assert.Equal(5m, Bal(data, 2025).TransferredToNext);
        var y2026 = Bal(data, 2026);
        Assert.Equal(5m, y2026.TransferredIn);
        Assert.Equal(5m, y2026.TransferredToNext);
        Assert.Equal(5m, y2026.LostDays);
        var y2027 = Bal(data, 2027);
        Assert.Equal(5m, y2027.TransferredIn);
        Assert.Equal(5m, y2027.TransferredToNext);
        Assert.Equal(0m, y2027.LostDays);
        Assert.Equal(5m, Bal(data, 2028).TransferredIn);
    }

    // ------------------------------------------------------------ per-year settings

    [Fact]
    public void PerYear_LimitAndPayout_DifferBetweenYears_AndInherit()
    {
        // 2025: limit 2 -> of 5 unused, 2 move, 3 paid out. 2026 inherits limit 2 (nothing booked: 25 own unused, 2 move).
        // 2026 has its own payout All: other 18 + ... own unused 25: 2 moved (5th week), 3 5th-week paid, 20 other paid; in 2 lost.
        var data = Data(Fill(20), firstTracked: 2025).WithLimit(2025, 2).WithPayout(2026, PayoutMode.All);

        var y2025 = Bal(data, 2025);
        Assert.Equal(2m, y2025.TransferredToNext);
        Assert.Equal(3m, y2025.PaidOutDays);
        Assert.Equal(0m, y2025.LostDays);

        var y2026 = Bal(data, 2026);
        Assert.Equal(2m, y2026.TransferredIn);
        Assert.Equal(2m, y2026.TransferredToNext);        // inherited limit 2
        Assert.Equal(2m, y2026.LostDays);                // the 2 transferred-in days, unused
        Assert.Equal(23m, y2026.PaidOutDays);            // 3 of the 5th week + all 20 other
        Assert.Equal(25m + 2m, y2026.TransferredToNext + y2026.PaidOutDays + y2026.LostDays);
    }

    [Fact]
    public void PerYear_ChangingALaterYearsLimit_DoesNotChangeEarlierOnes()
    {
        var data = Data(Fill(20), firstTracked: 2025).WithLimit(2026, 0);

        Assert.Equal(5m, Bal(data, 2025).TransferredToNext);
        Assert.Equal(0m, Bal(data, 2026).TransferredToNext);
        Assert.Equal(0m, Bal(data, 2027).TransferredIn);
    }

    // ------------------------------------------------------------ September to December

    [Fact]
    public void SeptToDec_BookingsUseTheOlderYearFirst_ReducingTheTransfer()
    {
        // 2025: 20 used in January -> 5 left. Mon 2026-10-05 and Tue 10-06 go to 2025 (older, oldest-first):
        // 3 unused -> 3 transferred, none to 2026's used count.
        var data = Data(Fill(20).Concat([Day("2026-10-05"), Day("2026-10-06")]), firstTracked: 2025);

        var old = Bal(data, 2025, "2026-12-18");
        var next = Bal(data, 2026, "2026-12-18");

        Assert.Equal(22m, old.UsedDays);
        Assert.Equal(3m, old.TransferredToNext);
        Assert.Equal(0m, next.UsedDays);
        Assert.Equal(3m, next.TransferredIn);
    }

    [Fact]
    public void SeptToDec_OnceTheOlderYearIsFull_NextDayGoesToTheNewYear_AndNothingIsTransferred()
    {
        // Oct 5-9 (5 days) fill 2025 to 25; Mon Oct 12 goes to 2026.
        var data = Data(
            Fill(20).Concat(Range("2026-10-05", "2026-10-09")).Concat([Day("2026-10-12")]), firstTracked: 2025);

        var old = Bal(data, 2025, "2026-12-18");
        var next = Bal(data, 2026, "2026-12-18");

        Assert.Equal(25m, old.UsedDays);
        Assert.Equal(0m, old.TransferredToNext);
        Assert.Equal(1m, next.UsedDays);
        Assert.Equal(0m, next.TransferredIn);
    }

    [Fact]
    public void SeptToDec_OlderYearWithTransferredIn_HasMoreRoom()
    {
        // 2026 has 30 available (5 transferred in). 5 days booked from Mon 2027-10-04 are all older-year (2026) days.
        var data = Data(Fill(20).Concat(Range("2027-10-04", "2027-10-08")), firstTracked: 2025);

        var y2026 = Bal(data, 2026, "2028-01-03");

        Assert.Equal(5m, y2026.UsedDays);
        Assert.Equal(0m, y2026.UnusedTransferredInDays); // the transferred-in days were used first
        Assert.Equal(0m, Bal(data, 2027, "2028-01-03").UsedDays);
    }

    // ------------------------------------------------------------ status

    [Fact]
    public void Status_AllPlanned_WhenTheUnbookedRestIsAllTransferred_NotWhenPaidOrLost()
    {
        // 20 used, 5 unbooked 5th-week days at Fri 2026-12-18.
        Assert.Equal(BalanceStatus.AllPlanned, Cal(Data(Fill(20), firstTracked: 2025), 2026, "2026-12-18").Status);
        Assert.Equal(BalanceStatus.DaysToPlan, Cal(Data(Fill(20), firstTracked: 2025).WithLimit(2025, 3), 2026, "2026-12-18").Status); // 2 paid out
        Assert.Equal(BalanceStatus.DaysToPlan, Cal(Data(Fill(20), firstTracked: 2025).WithLimit(2025, 0), 2026, "2026-12-18").Status);
        Assert.Equal(BalanceStatus.AllPlanned, Cal(Data(Fill(20)), 2026, "2026-12-18").Status); // 2c: default limit 5 (was: switch off, paid out)
    }

    [Fact]
    public void Status_AfterTheDeadline_AllTransferred_IsAllPlanned_PaidOrLost_IsExpired()
    {
        Assert.Equal(BalanceStatus.AllPlanned, Cal(Data(Fill(20), firstTracked: 2025), 2026, "2027-01-04").Status);
        Assert.Equal(BalanceStatus.Expired, Cal(Data(Fill(20), firstTracked: 2025).WithLimit(2025, 3), 2026, "2027-01-04").Status);
        Assert.Equal(BalanceStatus.Expired, Cal(Data(Fill(15), firstTracked: 2025), 2026, "2027-01-04").Status); // 5 first-20 lost
    }

    [Fact]
    public void Status_TransferredInDaysUnused_AfterTheDeadline_AreExpired()
    {
        // 2026/27 (deadline 2027-12-31): 5 transferred in and unused -> lost -> Expired even though all own days move on.
        var s = Cal(Data(Fill(20).Concat(Fill(20, "2027-01-04")), firstTracked: 2025), 2027, "2028-01-03");

        // 20 booked in 2026: 5 in + 15 own -> in used; own unused 10: 5 moved, 5 lost -> Expired.
        Assert.Equal(BalanceStatus.Expired, s.Status);
        Assert.Equal(5m, s.TransferredToNext);
        Assert.Equal(5m, s.LostDays);
        Assert.Equal(0m, s.HolidayYear.UnusedTransferredInDays);
    }

    [Fact]
    public void AtRisk_Split_RestThatCannotFitButCanBeTransferred_IsNotLost()
    {
        // Fri 2026-12-18, 10 used: 15 unbooked, 10 free working days (18, 21-25, 28-31) -> 5 at risk.
        // Booking all 10 free days leaves 5 unused (the 5th week) -> transferred: nothing paid out or lost.
        var s = Cal(Data(Fill(10), firstTracked: 2025), 2026, "2026-12-18");

        Assert.Equal(10m, s.WorkingDaysLeft);
        Assert.Equal(5m, s.DaysAtRisk);
        Assert.Equal(5m, s.AtRiskTransferredDays);
        Assert.Equal(0m, s.AtRiskPaidOutDays);
        Assert.Equal(0m, s.AtRiskLostDays);
        Assert.Equal(BalanceStatus.DaysToPlan, s.Status); // not DaysWillBeLost: nothing would be lost even if all free days are booked
        // Projection if nothing more is booked: 15 unused = 5 moved + 10 lost.
        Assert.Equal(5m, s.TransferredToNext);
        Assert.Equal(10m, s.LostDays);
    }

    [Fact]
    public void AtRisk_Split_WithLimit3_TwoOfTheFifthWeekArePaidOut()
    {
        var s = Cal(Data(Fill(10), firstTracked: 2025).WithLimit(2025, 3), 2026, "2026-12-18");

        Assert.Equal(3m, s.AtRiskTransferredDays);
        Assert.Equal(2m, s.AtRiskPaidOutDays);
        Assert.Equal(0m, s.AtRiskLostDays);
        Assert.Equal(BalanceStatus.DaysWillBeLost, s.Status);
    }

    [Fact]
    public void AtRisk_Split_NothingBookedBefore_TransferredAndLost()
    {
        // 5 used: 20 unbooked, 10 free -> book 10 -> 15 booked, 10 unused (5 first-20 + 5th week): 5 moved, 5 lost.
        var s = Cal(Data(Fill(5), firstTracked: 2025), 2026, "2026-12-18");

        Assert.Equal(10m, s.DaysAtRisk);
        Assert.Equal(5m, s.AtRiskTransferredDays);
        Assert.Equal(0m, s.AtRiskPaidOutDays);
        Assert.Equal(5m, s.AtRiskLostDays);
        Assert.Equal(BalanceStatus.DaysWillBeLost, s.Status);
    }

    [Fact]
    public void BeforeAndAfterTheDeadline_ShowTheSameNumbers_OnlyStatusDiffers()
    {
        var data = Data(Fill(15), firstTracked: 2025);

        var before = Cal(data, 2026, "2026-12-31");
        var after = Cal(data, 2026, "2027-01-04");

        Assert.Equal(before.TransferredToNext, after.TransferredToNext);
        Assert.Equal(before.PaidOutDays, after.PaidOutDays);
        Assert.Equal(before.LostDays, after.LostDays);
        Assert.NotEqual(BalanceStatus.Expired, before.Status);
        Assert.Equal(BalanceStatus.Expired, after.Status);
    }

    // ------------------------------------------------------------ persistence

    [Fact]
    public void DataStore_OldFile_WithFifthWeekFlagAndStoredTransfer_LoadsOn_IgnoresTheStoredTransfer_AndRewritesCleanly()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "data.json");
        File.WriteAllText(path, """
            {
              "Days": [ { "Date": "2026-01-05", "Type": "Vacation", "Amount": 1, "Description": "keep me" } ],
              "Years": [ { "Year": 2026, "PublicHolidays": [ "2026-12-24" ] } ],
              "EmployedSince": "2025-03-01",
              "FifthWeekTransferAllowed": true,
              "HolidayYears": [ { "StartYear": 2025, "TransferredToNext": 3 } ]
            }
            """);
        var store = new DataStore(path);

        var loaded = store.Load();
        Assert.Equal(5m, loaded.GetDeadlineSettings(new HolidayYear(2025)).TransferLimitDays);
        store.Save(loaded);

        var json = File.ReadAllText(path);
        Assert.DoesNotContain("TransferAllowed", json); // 2c: the switch is gone, in either spelling
        Assert.DoesNotContain("TransferredToNext", json);
        var again = store.Load();
        Assert.Equal("keep me", Assert.Single(again.Days).Description);
        Assert.Equal(D("2025-03-01"), again.EmployedSince);
        Assert.Equal(D("2026-12-24"), Assert.Single(Assert.Single(again.Years).PublicHolidays));
    }

    [Fact]
    public void DataStore_NewSettings_RoundTrip()
    {
        var path = Path.Combine(_dir, "data.json");
        var store = new DataStore(path);
        var data = Data(Fill(3), firstTracked: 2025).WithLimit(2025, 7.5m).WithPayout(2025, PayoutMode.Some, 2.5m).WithPayout(2026, PayoutMode.All);
        data.SetTransferLimit(new HolidayYear(2027), 0);

        store.Save(data);
        var loaded = store.Load();

        var s2025 = loaded.GetDeadlineSettings(new HolidayYear(2025));
        Assert.Equal(7.5m, s2025.TransferLimitDays);
        Assert.Equal(PayoutMode.Some, s2025.PayoutMode);
        Assert.Equal(2.5m, s2025.PayoutDays);
        var s2026 = loaded.GetDeadlineSettings(new HolidayYear(2026));
        Assert.Equal(PayoutMode.All, s2026.PayoutMode);
        Assert.Equal(7.5m, s2026.TransferLimitDays);          // inherited
        Assert.Equal(0m, loaded.GetDeadlineSettings(new HolidayYear(2027)).TransferLimitDays);
        Assert.Equal(PayoutMode.All, loaded.GetDeadlineSettings(new HolidayYear(2027)).PayoutMode); // inherited from 2026
    }

    [Fact]
    public void DataStore_FileWithoutAnyNewSettings_UsesDefaults()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "data.json");
        File.WriteAllText(path, """{ "Days": [], "Years": [] }""");

        var loaded = new DataStore(path).Load();
        var s = loaded.GetDeadlineSettings(new HolidayYear(2025));

        Assert.Equal(5m, s.TransferLimitDays);
        Assert.Equal(PayoutMode.None, s.PayoutMode);
        Assert.Empty(loaded.HolidayYears);
    }
}
