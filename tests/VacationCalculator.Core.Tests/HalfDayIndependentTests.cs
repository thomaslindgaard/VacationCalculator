using VacationCalculator.Core;

namespace VacationCalculator.Core.Tests;

/// <summary>Independent tests for feature 1 (half days / per-day bookings). Calendar facts are worked out by hand.</summary>
public class HalfDayIndependentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vc-halfday-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static readonly HashSet<DateOnly> None = [];
    private static DateOnly D(string s) => DateOnly.Parse(s);
    private static HashSet<DateOnly> H(params string[] s) => s.Select(DateOnly.Parse).ToHashSet();

    private static BookedDay B(string date, decimal amount = 1, DayType type = DayType.Vacation, string desc = "") =>
        new() { Date = D(date), Amount = amount, Type = type, Description = desc };

    private static YearSettings Year(int y, decimal allowance = 25, params string[] holidays) =>
        new() { Year = y, AllowanceDays = allowance, PublicHolidays = holidays.Select(DateOnly.Parse).ToList() };

    // ---- Balance ----

    [Fact]
    public void HalfDayOnToday_IsUsed()
    {
        // Wed 2026-07-08 is today
        var b = BalanceCalculator.Calculate(Year(2026), [B("2026-07-08", 0.5m)], D("2026-07-08"));
        Assert.Equal(0.5m, b.UsedDays);
        Assert.Equal(0m, b.PlannedDays);
        Assert.Equal(24.5m, b.RemainingDays);
    }

    [Fact]
    public void HalfDayTomorrow_IsPlanned()
    {
        var b = BalanceCalculator.Calculate(Year(2026), [B("2026-07-09", 0.5m)], D("2026-07-08"));
        Assert.Equal(0m, b.UsedDays);
        Assert.Equal(0.5m, b.PlannedDays);
    }

    [Fact]
    public void HalfDayOn31December_IsUsedWhenTodayIs31December()
    {
        // Thu 2026-12-31
        var b = BalanceCalculator.Calculate(Year(2026), [B("2026-12-31", 0.5m)], D("2026-12-31"));
        Assert.Equal(0.5m, b.UsedDays);
        Assert.Equal(0m, b.WorkingDaysLeft);
    }

    [Fact]
    public void HalfBookedDay_ReducesWorkingDaysLeftByHalf_AndFlagsLoss()
    {
        // today Mon 2026-12-28; left: Tue 29, Wed 30 (half booked), Thu 31 = 2.5
        var b = BalanceCalculator.Calculate(Year(2026), [B("2026-12-30", 0.5m)], D("2026-12-28"));
        Assert.Equal(2.5m, b.WorkingDaysLeft);
        Assert.Equal(24.5m, b.RemainingDays);
        Assert.Equal(22m, b.DaysAtRisk);
        Assert.Equal(BalanceStatus.DaysWillBeLost, b.Status);
    }

    [Fact]
    public void RemainingEqualToFreeHalfDays_IsNotLost()
    {
        // allowance 3, half on Tue 29 -> remaining 2.5; free left = 1 + 0.5 + 1 = 2.5 (Tue half, Wed, Thu)
        var b = BalanceCalculator.Calculate(Year(2026, 3), [B("2026-12-29", 0.5m)], D("2026-12-28"));
        Assert.Equal(2.5m, b.WorkingDaysLeft);
        Assert.Equal(0m, b.DaysAtRisk);
        Assert.Equal(BalanceStatus.DaysToPlan, b.Status);
    }

    [Fact]
    public void FeriefriHalfDay_DoesNotCountButTakesHalfAFreeDay()
    {
        // today Mon 2026-12-28; Wed 30 Feriefri half
        var b = BalanceCalculator.Calculate(Year(2026), [B("2026-12-30", 0.5m, DayType.Feriefri)], D("2026-12-28"));
        Assert.Equal(0m, b.UsedDays + b.PlannedDays);
        Assert.Equal(25m, b.RemainingDays);
        Assert.Equal(2.5m, b.WorkingDaysLeft);
    }

    [Fact]
    public void ZeroAllowanceWithHalfDay_IsOverBookedByHalf()
    {
        var b = BalanceCalculator.Calculate(Year(2026, 0), [B("2026-07-09", 0.5m)], D("2026-07-08"));
        Assert.Equal(-0.5m, b.RemainingDays);
        Assert.Equal(BalanceStatus.OverBooked, b.Status);
        Assert.Equal(0m, b.DaysAtRisk);
    }

    [Fact]
    public void DecimalAllowanceWithHalfDays_Adds()
    {
        // 12.5 + 2.5 carry = 15; 3 half days -> 1.5 booked
        var s = Year(2026, 12.5m);
        s.CarryOverDays = 2.5m;
        var days = new[] { B("2026-07-06", 0.5m), B("2026-07-07", 0.5m), B("2026-07-08", 0.5m) };
        var b = BalanceCalculator.Calculate(s, days, D("2026-07-07"));
        Assert.Equal(1m, b.UsedDays);
        Assert.Equal(0.5m, b.PlannedDays);
        Assert.Equal(13.5m, b.RemainingDays);
    }

    [Fact]
    public void EmptyBookings_AreAllRemaining()
    {
        var b = BalanceCalculator.Calculate(Year(2026), [], D("2026-07-08"));
        Assert.Equal(25m, b.RemainingDays);
    }

    [Fact]
    public void RangeAcrossNewYear_CountsEachYearsOwnDays()
    {
        var data = new VacationData
        {
            Years = [Year(2026, 25, "2026-12-31"), Year(2027, 25, "2027-01-01")],
        };
        // Mon 2026-12-28 .. Fri 2027-01-08; working: 28,29,30 | Jan 4-8
        data.Days = Bookings.BookRange(data.Days, D("2026-12-28"), D("2027-01-08"), DayType.Vacation, "", data.AllPublicHolidays());
        var today = D("2026-12-29");

        var b26 = BalanceCalculator.Calculate(data.Years[0], data.Days, today);
        var b27 = BalanceCalculator.Calculate(data.Years[1], data.Days, today);

        Assert.Equal(2m, b26.UsedDays);   // 28, 29
        Assert.Equal(1m, b26.PlannedDays); // 30
        Assert.Equal(0m, b26.WorkingDaysLeft); // 30 booked, 31 holiday
        Assert.Equal(0m, b27.UsedDays);
        Assert.Equal(5m, b27.PlannedDays);
    }

    // ---- Bookings ----

    [Fact]
    public void BookRange_IncludesFeb29InLeapYear()
    {
        // Mon 2028-02-28 .. Wed 2028-03-01 (Feb 29 is a Tuesday)
        var days = Bookings.BookRange([], D("2028-02-28"), D("2028-03-01"), DayType.Vacation, "", None);
        Assert.Equal([D("2028-02-28"), D("2028-02-29"), D("2028-03-01")], days.Select(d => d.Date));
    }

    [Fact]
    public void BookHalfDay_TwiceOnSameDay_StaysHalf()
    {
        var once = Bookings.BookHalfDay([], D("2026-07-06"), DayType.Vacation, "", None);
        var twice = Bookings.BookHalfDay(once, D("2026-07-06"), DayType.Vacation, "", None);
        Assert.Single(twice);
        Assert.Equal(0.5m, twice[0].Amount);
    }

    [Fact]
    public void BookRange_OverHalfDay_MakesItFull()
    {
        var days = Bookings.BookRange([B("2026-07-07", 0.5m)], D("2026-07-06"), D("2026-07-08"), DayType.Vacation, "", None);
        Assert.Equal(3m, days.Sum(d => d.Amount));
    }

    [Fact]
    public void BookHalfDay_OnHolidayInOtherYearsSettingIsStillBookable()
    {
        // AllPublicHolidays takes a holiday only from its own year's settings, so a stray date is ignored
        var data = new VacationData { Years = [Year(2026, 25, "2027-07-06")] };
        var days = Bookings.BookHalfDay([], D("2027-07-06"), DayType.Vacation, "", data.AllPublicHolidays()); // Tue
        Assert.Single(days);
    }

    [Fact]
    public void RemoveDays_OfWholePeriod_LeavesOthers()
    {
        var days = Bookings.BookRange([], D("2026-07-06"), D("2026-07-08"), DayType.Vacation, "A", None)
            .Append(B("2026-07-10", 0.5m, desc: "B")).ToList();
        var periods = Bookings.GroupPeriods(days, 2026, None);
        Assert.Equal(2, periods.Count);

        var left = Bookings.RemoveDays(days, periods[0].Dates);

        Assert.Equal([B("2026-07-10", 0.5m, desc: "B")], left);
    }

    // ---- Grouping ----

    [Fact]
    public void Group_HalfDayInMiddle_StaysOnePeriodWithSum()
    {
        // Mon full, Tue half, Wed full = 2.5
        var days = new[] { B("2026-07-06"), B("2026-07-07", 0.5m), B("2026-07-08") };
        var p = Assert.Single(Bookings.GroupPeriods(days, 2026, None));
        Assert.Equal(2.5m, p.Amount);
    }

    [Fact]
    public void Group_FridayAndMonday_JoinAcrossWeekend_ButTwoFreeDaysSplit()
    {
        var joined = Bookings.GroupPeriods([B("2026-07-10"), B("2026-07-13")], 2026, None); // Fri, Mon
        Assert.Single(joined);

        var split = Bookings.GroupPeriods([B("2026-12-21"), B("2026-12-28")], 2026, None); // Mon, Mon; 22-25 free
        Assert.Equal(2, split.Count);
    }

    [Fact]
    public void Group_FeriefriNextToVacation_StaysSeparate()
    {
        var days = new[] { B("2026-07-10"), B("2026-07-13", type: DayType.Feriefri) };
        var periods = Bookings.GroupPeriods(days, 2026, None);
        Assert.Equal([DayType.Vacation, DayType.Feriefri], periods.Select(p => p.Type));
    }

    [Fact]
    public void Group_UnsortedAndDuplicateInput_IsHandled()
    {
        var days = new[] { B("2026-07-08"), B("2026-07-06"), B("2026-07-07"), B("2026-07-07", 0.5m) };
        var p = Assert.Single(Bookings.GroupPeriods(days, 2026, None));
        Assert.Equal(3m, p.Amount);
        Assert.Equal(D("2026-07-06"), p.Start);
    }

    [Fact]
    public void Group_PeriodWithOnlyWeekendRecords_AmountZeroButListed()
    {
        // A stray record on Sat 2026-07-11
        var p = Assert.Single(Bookings.GroupPeriods([B("2026-07-11")], 2026, None));
        Assert.Equal(0m, p.Amount);
    }

    // ---- DataStore ----

    [Fact]
    public void DataStore_RoundTripsHalfDayFeriefriLeapDayAndUnicode()
    {
        var store = new DataStore(Path.Combine(_dir, "sub", "data.json"));
        var data = new VacationData
        {
            Days = [B("2028-02-29", 0.5m, DayType.Feriefri, "Skovtur æøå – \"quoted\""), B("2026-12-31")],
            Years = [Year(2028, 12.5m)],
        };
        store.Save(data);

        var text = File.ReadAllText(store.Path);
        Assert.Contains("\"Feriefri\"", text);
        Assert.Contains("0.5", text);
        Assert.False(File.Exists(store.Path + ".tmp"));

        var loaded = store.Load();
        Assert.Equal(data.Days, loaded.Days);
        Assert.Equal(12.5m, loaded.Years[0].AllowanceDays);
    }

    [Fact]
    public void DataStore_SaveOverExistingFile_ReplacesIt()
    {
        var store = new DataStore(Path.Combine(_dir, "data.json"));
        store.Save(new VacationData { Days = [B("2026-07-06")] });
        store.Save(new VacationData { Days = [B("2026-07-07", 0.5m)] });
        Assert.Equal([B("2026-07-07", 0.5m)], store.Load().Days);
    }
}
