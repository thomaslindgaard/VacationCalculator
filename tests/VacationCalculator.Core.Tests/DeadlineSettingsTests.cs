using VacationCalculator.Core;
using static VacationCalculator.Core.Tests.TestData;

namespace VacationCalculator.Core.Tests;

/// <summary>Per-holiday-year transfer limit and payout setting, with inheritance from earlier years.</summary>
public class DeadlineSettingsTests
{
    private static readonly HolidayYear Hy2024 = new(2024);
    private static readonly HolidayYear Hy2025 = new(2025);
    private static readonly HolidayYear Hy2026 = new(2026);
    private static readonly HolidayYear Hy2027 = new(2027);

    [Fact]
    public void NoValuesAtAll_GivesTheDefaults_AndReadingAddsNothing()
    {
        var data = new VacationData();

        var s = data.GetDeadlineSettings(Hy2025);

        Assert.Equal(5m, s.TransferLimitDays);
        Assert.Null(s.TransferLimitFrom);
        Assert.False(s.TransferLimitIsOwn);
        Assert.Equal(PayoutMode.None, s.PayoutMode);
        Assert.Equal(0m, s.PayoutDays);
        Assert.Null(s.PayoutFrom);
        Assert.False(s.PayoutIsOwn);
        Assert.Empty(data.HolidayYears);
    }

    [Fact]
    public void NullHolidayYears_GivesTheDefaults()
    {
        var data = new VacationData { HolidayYears = null! };

        Assert.Equal(5m, data.GetDeadlineSettings(Hy2025).TransferLimitDays);
    }

    [Fact]
    public void OwnValues_AreReportedAsOwn()
    {
        var data = new VacationData();
        data.SetTransferLimit(Hy2025, 8m);
        data.SetPayout(Hy2025, PayoutMode.Some, 3m);

        var s = data.GetDeadlineSettings(Hy2025);

        Assert.Equal(8m, s.TransferLimitDays);
        Assert.True(s.TransferLimitIsOwn);
        Assert.Equal(PayoutMode.Some, s.PayoutMode);
        Assert.Equal(3m, s.PayoutDays);
        Assert.True(s.PayoutIsOwn);
        Assert.Single(data.HolidayYears);
    }

    [Fact]
    public void YearWithoutValues_InheritsFromTheNearestEarlierYear()
    {
        var data = new VacationData();
        data.SetTransferLimit(Hy2024, 8m);
        data.SetTransferLimit(Hy2025, 10m);

        var s = data.GetDeadlineSettings(Hy2027);

        Assert.Equal(10m, s.TransferLimitDays);
        Assert.Equal(Hy2025, s.TransferLimitFrom);
        Assert.False(s.TransferLimitIsOwn);
        Assert.Equal(2, data.HolidayYears.Count); // reading 2027 added nothing
    }

    [Fact]
    public void LimitAndPayout_AreInheritedSeparately()
    {
        var data = new VacationData();
        data.SetTransferLimit(Hy2024, 8m);
        data.SetPayout(Hy2025, PayoutMode.All);

        var s = data.GetDeadlineSettings(Hy2026);

        Assert.Equal(8m, s.TransferLimitDays);
        Assert.Equal(Hy2024, s.TransferLimitFrom);
        Assert.Equal(PayoutMode.All, s.PayoutMode);
        Assert.Equal(Hy2025, s.PayoutFrom);
    }

    [Fact]
    public void LaterYearsValues_DoNotAffectEarlierYears()
    {
        var data = new VacationData();
        data.SetTransferLimit(Hy2025, 8m);
        data.SetPayout(Hy2025, PayoutMode.Some, 2m);
        data.SetTransferLimit(Hy2026, 2m);
        data.SetPayout(Hy2026, PayoutMode.All);

        Assert.Equal(5m, data.GetDeadlineSettings(Hy2024).TransferLimitDays); // before any value: default
        Assert.Equal(PayoutMode.None, data.GetDeadlineSettings(Hy2024).PayoutMode);
        Assert.Equal(8m, data.GetDeadlineSettings(Hy2025).TransferLimitDays);
        Assert.Equal(2m, data.GetDeadlineSettings(Hy2025).PayoutDays);
        Assert.Equal(2m, data.GetDeadlineSettings(Hy2026).TransferLimitDays);
        Assert.Equal(2m, data.GetDeadlineSettings(Hy2027).TransferLimitDays);  // inherits the latest
        Assert.Equal(PayoutMode.All, data.GetDeadlineSettings(Hy2027).PayoutMode);
    }

    [Fact]
    public void Clear_MakesTheYearInheritAgain()
    {
        var data = new VacationData();
        data.SetTransferLimit(Hy2024, 8m);
        data.SetTransferLimit(Hy2025, 0m);
        data.SetPayout(Hy2025, PayoutMode.All);

        data.ClearTransferLimit(Hy2025);
        data.ClearPayout(Hy2025);
        data.ClearPayout(Hy2027); // nothing stored: no-op, adds nothing

        var s = data.GetDeadlineSettings(Hy2025);
        Assert.Equal(8m, s.TransferLimitDays);
        Assert.Equal(Hy2024, s.TransferLimitFrom);
        Assert.Equal(PayoutMode.None, s.PayoutMode);
        Assert.Null(s.PayoutFrom);
        Assert.Equal(2, data.HolidayYears.Count);
    }

    [Fact]
    public void LimitAbove5AndZero_AreAllowed_NegativeIsStoredAsZero()
    {
        var data = new VacationData();
        data.SetTransferLimit(Hy2024, 12.5m);
        data.SetTransferLimit(Hy2025, 0m);
        data.SetTransferLimit(Hy2026, -3m);

        Assert.Equal(12.5m, data.GetDeadlineSettings(Hy2024).TransferLimitDays);
        Assert.Equal(0m, data.GetDeadlineSettings(Hy2025).TransferLimitDays);
        Assert.True(data.GetDeadlineSettings(Hy2025).TransferLimitIsOwn); // 0 is a value, not "inherit"
        Assert.Equal(0m, data.FindHolidayYear(2026)!.TransferLimitDays);
    }

    [Fact]
    public void SetPayout_KeepsDaysOnlyForSome()
    {
        var data = new VacationData();
        data.SetPayout(Hy2025, PayoutMode.Some, -2m);
        Assert.Equal(0m, data.FindHolidayYear(2025)!.PayoutDays);

        data.SetPayout(Hy2025, PayoutMode.All, 7m);
        Assert.Null(data.FindHolidayYear(2025)!.PayoutDays);
        Assert.Equal(0m, data.GetDeadlineSettings(Hy2025).PayoutDays);
    }

    [Fact]
    public void HandEditedNegativeValues_ReadAsZero()
    {
        var data = new VacationData
        {
            HolidayYears = [new() { StartYear = 2025, TransferLimitDays = -1m, PayoutMode = PayoutMode.Some, PayoutDays = -4m }],
        };

        var s = data.GetDeadlineSettings(Hy2025);

        Assert.Equal(0m, s.TransferLimitDays);
        Assert.Equal(0m, s.PayoutDays);
    }

    [Fact]
    public void DuplicateEntriesForAYear_FirstWins()
    {
        var data = new VacationData
        {
            HolidayYears =
            [
                new() { StartYear = 2025, TransferLimitDays = 7m },
                new() { StartYear = 2025, TransferLimitDays = 9m },
            ],
        };

        Assert.Equal(7m, data.GetDeadlineSettings(Hy2025).TransferLimitDays);
    }

    [Fact]
    public void ChangingAYearsValue_ChangesItsOwnCalculationButNotEarlierYears()
    {
        // 15 used in 2025, transfer on. Setting 2026's limit to 0 leaves 2025's transfer at 5.
        var data = Data(Fill(15), transferAllowed: true, firstTracked: 2025);
        var before = BalanceCalculator.CalculateHolidayYear(data, Hy2025, D("2026-10-24"));

        data.SetTransferLimit(Hy2026, 0m);

        var after2025 = BalanceCalculator.CalculateHolidayYear(data, Hy2025, D("2026-10-24"));
        var after2026 = BalanceCalculator.CalculateHolidayYear(data, Hy2026, D("2026-10-24"));
        Assert.Equal(before, after2025);
        Assert.Equal(5m, after2025.TransferredToNext);
        Assert.Equal(0m, after2026.TransferredToNext);
        Assert.Equal(5m, after2026.PaidOutDays);  // own 5th week
        Assert.Equal(25m, after2026.LostDays);    // 5 transferred in + 20 own
    }

    // ---- Split ----

    [Theory]
    // in, own, booked | allowed, limit, mode, N -> transferred, paid 5th week, paid other, lost in, lost other
    [InlineData("0", "25", "15", false, "5", PayoutMode.None, "0", "0", "5", "0", "0", "5")]
    [InlineData("0", "25", "15", true, "5", PayoutMode.None, "0", "5", "0", "0", "0", "5")]
    [InlineData("0", "25", "15", true, "3", PayoutMode.None, "0", "3", "2", "0", "0", "5")]  // 5th week partly transferred, rest paid
    [InlineData("0", "25", "15", true, "8", PayoutMode.Some, "1", "8", "0", "1", "0", "1")]  // 3 first-20 days transferred
    [InlineData("0", "25", "10", true, "5", PayoutMode.Some, "4", "5", "0", "4", "0", "6")]
    [InlineData("0", "25", "10", true, "5", PayoutMode.Some, "20", "5", "0", "10", "0", "0")] // N larger than the rest
    [InlineData("0", "25", "10", false, "5", PayoutMode.All, "0", "0", "5", "10", "0", "0")]
    [InlineData("5", "25", "0", true, "5", PayoutMode.All, "0", "5", "0", "20", "5", "0")]   // transferred-in lost even with All
    [InlineData("5", "25", "3", true, "5", PayoutMode.None, "0", "5", "0", "0", "2", "20")]  // bookings use transferred-in first
    [InlineData("5", "25", "8", false, "5", PayoutMode.None, "0", "0", "5", "0", "0", "17")] // 5 in + 3 own used
    [InlineData("0", "16.67", "0", true, "5", PayoutMode.None, "0", "0", "0", "0", "0", "16.67")] // no 5th week: the default limit moves nothing
    [InlineData("0", "16.67", "0", true, "8", PayoutMode.None, "0", "3", "0", "0", "0", "13.67")] // only the 3 above 5 reach the first 20
    [InlineData("0", "25", "0", true, "8", PayoutMode.None, "0", "8", "0", "0", "0", "17")]       // 5th week 5 + 3 first-20
    [InlineData("0", "25", "0", true, "3", PayoutMode.None, "0", "3", "2", "0", "0", "20")]       // 3 5th-week, nothing else
    [InlineData("0", "22", "0", true, "10", PayoutMode.None, "0", "7", "0", "0", "0", "15")]      // only 2 5th-week: 2 + 5 first-20
    [InlineData("0", "25", "0", true, "4.5", PayoutMode.None, "0", "4.5", "0.5", "0", "0", "20")] // limit below 5: 5th week only
    [InlineData("0", "25", "0", true, "5.5", PayoutMode.All, "0", "5.5", "0", "19.5", "0", "0")]  // half a first-20 day moves
    [InlineData("0", "25", "25", true, "5", PayoutMode.All, "0", "0", "0", "0", "0", "0")]
    [InlineData("5", "25", "31", true, "5", PayoutMode.All, "0", "0", "0", "0", "0", "0")]   // over-booked
    [InlineData("0", "25", "19.5", true, "5", PayoutMode.None, "0", "5", "0", "0", "0", "0.5")]
    public void Split_LostInThenTransferThenFifthWeekPaidThenPayoutSetting(
        string transferredIn, string own, string booked, bool allowed, string limit, PayoutMode mode, string n,
        string transferred, string paidFifth, string paidOther, string lostIn, string lostOther)
    {
        var settings = new DeadlineSettings(Hy2025, M(limit), null, mode, M(n), null);

        var outcome = settings.Split(M(transferredIn), M(own), M(booked), allowed);

        Assert.Equal(new DeadlineOutcome(M(transferred), M(paidFifth), M(paidOther), M(lostIn), M(lostOther)), outcome);
        Assert.Equal(Math.Max(0, M(transferredIn) + M(own) - M(booked)), outcome.UnusedDays);
    }
}
