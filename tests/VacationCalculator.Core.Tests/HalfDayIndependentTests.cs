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

    private static YearSettings Year(int y, params string[] holidays) =>
        new() { Year = y, PublicHolidays = holidays.Select(DateOnly.Parse).ToList() };

    /// <summary>Calendar-year status (holiday year with its deadline on 31 Dec <paramref name="year"/>).</summary>
    private static CalendarYearStatus Status(IEnumerable<BookedDay> days, int year, string today, DateOnly? employedSince = null) =>
        BalanceCalculator.CalculateCalendarYear(new VacationData { Days = days.ToList(), EmployedSince = employedSince }, year, D(today));

    /// <summary>n full vacation days on weekdays from Mon 2026-01-05 (all in the past for the tests below).</summary>
    private static IEnumerable<BookedDay> Past(int n) =>
        Bookings.BookRange([], D("2026-01-05"), D("2026-03-31"), DayType.Vacation, "", None).Take(n);

    // ---- Balance ----

    [Fact]
    public void HalfDayOnToday_IsUsed()
    {
        // Wed 2026-07-08 is today
        var b = Status([B("2026-07-08", 0.5m)], 2026, "2026-07-08").HolidayYear;
        Assert.Equal(0.5m, b.UsedDays);
        Assert.Equal(0m, b.BookedAheadDays);
        Assert.Equal(24.5m, b.UnbookedDays);
    }

    [Fact]
    public void HalfDayTomorrow_IsPlanned()
    {
        var b = Status([B("2026-07-09", 0.5m)], 2026, "2026-07-08").HolidayYear;
        Assert.Equal(0m, b.UsedDays);
        Assert.Equal(0.5m, b.BookedAheadDays);
    }

    [Fact]
    public void HalfDayOn31December_IsUsedWhenTodayIs31December()
    {
        // Thu 2026-12-31
        var s = Status([B("2026-12-31", 0.5m)], 2026, "2026-12-31");
        Assert.Equal(0.5m, s.HolidayYear.UsedDays);
        Assert.Equal(0.5m, s.WorkingDaysLeft); // today counts; its other half is still free
    }

    [Fact]
    public void HalfBookedDay_ReducesWorkingDaysLeftByHalf_AndFlagsLoss()
    {
        // today Mon 2026-12-28 (included); left: Mon 28, Tue 29, Wed 30 (half booked), Thu 31 = 3.5
        var s = Status([B("2026-12-30", 0.5m)], 2026, "2026-12-28");
        Assert.Equal(3.5m, s.WorkingDaysLeft);
        Assert.Equal(24.5m, s.UnbookedDays);
        Assert.Equal(21m, s.DaysAtRisk);
        Assert.Equal(BalanceStatus.DaysWillBeLost, s.Status);
    }

    [Fact]
    public void RemainingEqualToFreeHalfDays_IsNotLost()
    {
        // 21 days used in Jan/Feb leave 4; half on Tue 29 -> 3.5 unbooked; free left = 1 + 0.5 + 1 + 1 = 3.5 (Mon today, Tue half, Wed, Thu)
        // 2c: limit 0 on 2025, else the 3.5 (all 5th week) are transferred and the status is AllPlanned
        var data = new VacationData { Days = Past(21).Append(B("2026-12-29", 0.5m)).ToList() };
        data.SetTransferLimit(new HolidayYear(2025), 0);
        var s = BalanceCalculator.CalculateCalendarYear(data, 2026, D("2026-12-28"));
        Assert.Equal(3.5m, s.UnbookedDays);
        Assert.Equal(3.5m, s.WorkingDaysLeft);
        Assert.Equal(0m, s.DaysAtRisk);
        Assert.Equal(BalanceStatus.DaysToPlan, s.Status);
    }

    [Fact]
    public void FeriefriHalfDay_DoesNotCountButTakesHalfAFreeDay()
    {
        // today Mon 2026-12-28 (included); Wed 30 Feriefri half -> 1 + 1 + 0.5 + 1
        var s = Status([B("2026-12-30", 0.5m, DayType.Feriefri)], 2026, "2026-12-28");
        Assert.Equal(0m, s.HolidayYear.BookedDays);
        Assert.Equal(25m, s.UnbookedDays);
        Assert.Equal(3.5m, s.WorkingDaysLeft);
    }

    [Fact]
    public void NoDaysLeftWithHalfDay_IsOverBookedByHalf()
    {
        // 25 days used in Jan/Feb, then half on Thu 2026-07-09
        var s = Status(Past(25).Append(B("2026-07-09", 0.5m)), 2026, "2026-07-08");
        Assert.Equal(-0.5m, s.UnbookedDays);
        Assert.Equal(BalanceStatus.OverBooked, s.Status);
        Assert.Equal(0m, s.DaysAtRisk);
    }

    [Fact]
    public void DecimalAllowanceWithHalfDays_Adds()
    {
        // Employed from Sun 2026-03-01: Mar .. Aug = 12.5 days; 3 half days -> 1.5 booked
        var days = new[] { B("2026-07-06", 0.5m), B("2026-07-07", 0.5m), B("2026-07-08", 0.5m) };
        var b = Status(days, 2026, "2026-07-07", employedSince: D("2026-03-01")).HolidayYear;
        Assert.Equal(12.5m, b.EarnableDays);
        Assert.Equal(1m, b.UsedDays);
        Assert.Equal(0.5m, b.BookedAheadDays);
        Assert.Equal(11m, b.UnbookedDays);
    }

    [Fact]
    public void EmptyBookings_AreAllRemaining()
    {
        Assert.Equal(25m, Status([], 2026, "2026-07-08").UnbookedDays);
    }

    [Fact]
    public void RangeAcrossNewYear_CountsEachYearsOwnDays()
    {
        var data = new VacationData
        {
            Years = [Year(2026, "2026-12-31"), Year(2027, "2027-01-01")],
        };
        // Mon 2026-12-28 .. Fri 2027-01-08; working: 28,29,30 | Jan 4-8
        data.Days = Bookings.BookRange(data.Days, D("2026-12-28"), D("2027-01-08"), DayType.Vacation, "", data.AllPublicHolidays());
        var today = D("2026-12-29");

        var s26 = BalanceCalculator.CalculateCalendarYear(data, 2026, today);
        var s27 = BalanceCalculator.CalculateCalendarYear(data, 2027, today);

        Assert.Equal(2m, s26.HolidayYear.UsedDays);        // 28, 29
        Assert.Equal(1m, s26.HolidayYear.BookedAheadDays); // 30
        Assert.Equal(0m, s26.WorkingDaysLeft);             // 29 (today) and 30 booked, 31 holiday
        Assert.Equal(0m, s27.HolidayYear.UsedDays);
        Assert.Equal(5m, s27.HolidayYear.BookedAheadDays);
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
        var data = new VacationData { Years = [Year(2026, "2027-07-06")] };
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
            Years = [Year(2028, "2028-02-28")],
        };
        store.Save(data);

        var text = File.ReadAllText(store.Path);
        Assert.Contains("\"Feriefri\"", text);
        Assert.Contains("0.5", text);
        Assert.False(File.Exists(store.Path + ".tmp"));

        var loaded = store.Load();
        Assert.Equal(data.Days, loaded.Days);
        Assert.Equal([D("2028-02-28")], loaded.Years[0].PublicHolidays);
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
