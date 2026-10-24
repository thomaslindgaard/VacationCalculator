using VacationCalculator.Core;
using static VacationCalculator.Core.Tests.TestData;

namespace VacationCalculator.Core.Tests;

/// <summary>
/// Feature 4: Danish public holidays are calculated for every year and always apply, on top of the manual ones.
/// Easter dates are checked against the published table of Western (Gregorian) Easter Sundays.
/// 2026: Easter Sun 04-05, so Thu 04-02, Fri 04-03, Mon 04-06, Thu 05-14 (Ascension), Sun 05-24, Mon 05-25;
/// Thu 01-01, Thu 12-24, Fri 12-25, Sat 12-26, Thu 12-31.
/// </summary>
public class DanishHolidaysTests : IDisposable
{
    private static readonly HashSet<DateOnly> NoHolidays = [];
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vc-danish-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    // ---- Easter ----

    [Theory]
    [InlineData(1818, "1818-03-22")] // earliest possible
    [InlineData(1943, "1943-04-25")] // latest possible
    [InlineData(1961, "1961-04-02")]
    [InlineData(2000, "2000-04-23")]
    [InlineData(2008, "2008-03-23")]
    [InlineData(2011, "2011-04-24")]
    [InlineData(2019, "2019-04-21")]
    [InlineData(2024, "2024-03-31")]
    [InlineData(2025, "2025-04-20")]
    [InlineData(2026, "2026-04-05")]
    [InlineData(2027, "2027-03-28")]
    [InlineData(2028, "2028-04-16")]
    [InlineData(2029, "2029-04-01")]
    [InlineData(2030, "2030-04-21")]
    [InlineData(2038, "2038-04-25")] // latest possible
    [InlineData(2285, "2285-03-22")] // earliest possible
    public void EasterSunday_MatchesTheTable(int year, string easter)
    {
        Assert.Equal(D(easter), DanishHolidays.EasterSunday(year));
    }

    [Fact]
    public void EveryYear_HasTwelveDistinctHolidaysInOrder_AndEasterIsASundayBetween22MarchAnd25April()
    {
        for (var year = 1; year <= 9999; year++)
        {
            var easter = DanishHolidays.EasterSunday(year);
            Assert.Equal(DayOfWeek.Sunday, easter.DayOfWeek);
            Assert.InRange(easter, new DateOnly(year, 3, 22), new DateOnly(year, 4, 25));

            var list = DanishHolidays.For(year);
            Assert.Equal(12, list.Count);
            Assert.Equal(12, list.Select(h => h.Date).Distinct().Count());
            Assert.All(list, h => Assert.Equal(year, h.Date.Year));
            Assert.Equal(list.OrderBy(h => h.Date).Select(h => h.Date), list.Select(h => h.Date));
            Assert.All(list, h => Assert.Equal(h.Name, DanishHolidays.NameOf(h.Date)));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10000)]
    [InlineData(-1)]
    public void UnrepresentableYears_Throw(int year)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DanishHolidays.EasterSunday(year));
        Assert.Throws<ArgumentOutOfRangeException>(() => DanishHolidays.For(year));
    }

    [Fact]
    public void DateOnlyBounds_AreHolidays_AndNeverThrow()
    {
        Assert.Equal("Nytårsdag", DanishHolidays.NameOf(DateOnly.MinValue));       // Mon 0001-01-01
        Assert.Equal("Nytårsaftensdag", DanishHolidays.NameOf(DateOnly.MaxValue)); // Fri 9999-12-31
        Assert.True(DanishHolidays.IsHoliday(D("9999-03-29"))); // Easter 9999 is Sun 03-28 -> 2. påskedag
        Assert.True(DanishHolidays.IsHoliday(D("0001-04-02"))); // Easter 0001 is Sun 04-01 -> 2. påskedag
    }

    // ---- The list for 2026 ----

    [Fact]
    public void For2026_IsTheFullListWithDanishNames()
    {
        Assert.Equal(
            [
                new DanishHoliday(D("2026-01-01"), "Nytårsdag"),             // Thu
                new DanishHoliday(D("2026-04-02"), "Skærtorsdag"),           // Thu
                new DanishHoliday(D("2026-04-03"), "Langfredag"),            // Fri
                new DanishHoliday(D("2026-04-05"), "Påskedag"),              // Sun
                new DanishHoliday(D("2026-04-06"), "2. påskedag"),           // Mon
                new DanishHoliday(D("2026-05-14"), "Kristi himmelfartsdag"), // Thu
                new DanishHoliday(D("2026-05-24"), "Pinsedag"),              // Sun
                new DanishHoliday(D("2026-05-25"), "2. pinsedag"),           // Mon
                new DanishHoliday(D("2026-12-24"), "Juleaftensdag"),         // Thu
                new DanishHoliday(D("2026-12-25"), "Juledag"),               // Fri
                new DanishHoliday(D("2026-12-26"), "2. juledag"),            // Sat
                new DanishHoliday(D("2026-12-31"), "Nytårsaftensdag"),       // Thu
            ],
            DanishHolidays.For(2026));
    }

    [Fact]
    public void For2025_EasterBasedDates()
    {
        // Easter Sun 2025-04-20
        var byName = DanishHolidays.For(2025).ToDictionary(h => h.Name, h => h.Date);
        Assert.Equal(D("2025-04-17"), byName["Skærtorsdag"]);
        Assert.Equal(D("2025-04-18"), byName["Langfredag"]);
        Assert.Equal(D("2025-04-21"), byName["2. påskedag"]);
        Assert.Equal(D("2025-05-29"), byName["Kristi himmelfartsdag"]);
        Assert.Equal(D("2025-06-08"), byName["Pinsedag"]);
        Assert.Equal(D("2025-06-09"), byName["2. pinsedag"]);
    }

    [Fact]
    public void For2038_LatestEaster_WhitMondayInJune()
    {
        // Easter Sun 2038-04-25: Ascension Thu 06-03, Whit Monday 06-14 (the latest possible)
        var byName = DanishHolidays.For(2038).ToDictionary(h => h.Name, h => h.Date);
        Assert.Equal(D("2038-06-03"), byName["Kristi himmelfartsdag"]);
        Assert.Equal(D("2038-06-14"), byName["2. pinsedag"]);
    }

    [Fact]
    public void For2285_EarliestEaster_SkaertorsdagOn19March()
    {
        var byName = DanishHolidays.For(2285).ToDictionary(h => h.Name, h => h.Date);
        Assert.Equal(D("2285-03-19"), byName["Skærtorsdag"]);
        Assert.Equal(D("2285-04-30"), byName["Kristi himmelfartsdag"]);
    }

    [Theory]
    [InlineData("2026-06-05")] // Fri, Grundlovsdag: not automatic
    [InlineData("2026-05-01")] // Fri, Store bededag no longer exists (and 1 May isn't one)
    [InlineData("2026-04-04")] // Sat between Langfredag and Påskedag
    [InlineData("2028-02-29")] // Tue, leap day
    [InlineData("2026-12-27")] // Sun
    [InlineData("2026-12-30")] // Wed
    public void OtherDays_AreNotAutomaticHolidays(string date)
    {
        Assert.False(DanishHolidays.IsHoliday(D(date)));
        Assert.Null(DanishHolidays.NameOf(D(date)));
    }

    [Fact]
    public void StoreBededag2026_IsNotAHoliday()
    {
        // Store bededag would be Easter + 26 = Fri 2026-05-01; abolished from 2024 and not part of the list
        Assert.DoesNotContain(DanishHolidays.For(2026), h => h.Date == D("2026-05-01"));
        Assert.DoesNotContain(DanishHolidays.For(2023), h => h.Date == D("2023-05-05"));
    }

    // ---- Working days ----

    [Fact]
    public void IsWorkingDay_ExcludesAutomaticHolidaysWhateverSetIsGiven()
    {
        Assert.False(BalanceCalculator.IsWorkingDay(D("2026-04-06"), NoHolidays)); // Mon, 2. påskedag
        Assert.False(BalanceCalculator.IsWorkingDay(D("2026-12-24"), NoHolidays)); // Thu
        Assert.True(BalanceCalculator.IsWorkingDay(D("2026-04-07"), NoHolidays));  // Tue
        Assert.False(BalanceCalculator.IsWorkingDay(D("2026-04-07"), new HashSet<DateOnly> { D("2026-04-07") }));
    }

    [Fact]
    public void ManualHolidayOnAnAutomaticOne_IsNotCountedTwice()
    {
        // Mon 2026-12-21 .. Thu 2026-12-31: 9 weekdays less Thu 24, Fri 25, Thu 31 = 6, with or without manual 24/25
        Assert.Equal(6, BalanceCalculator.CountWorkingDays(D("2026-12-21"), D("2026-12-31"), NoHolidays));
        Assert.Equal(6, BalanceCalculator.CountWorkingDays(
            D("2026-12-21"), D("2026-12-31"), new HashSet<DateOnly> { D("2026-12-24"), D("2026-12-25") }));
        // Plus a manual Mon 28: 5
        Assert.Equal(5, BalanceCalculator.CountWorkingDays(
            D("2026-12-21"), D("2026-12-31"), new HashSet<DateOnly> { D("2026-12-24"), D("2026-12-28") }));
    }

    [Fact]
    public void CountWorkingDays_WholeYearsAreFast()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var total = 0;
        for (var y = 2000; y < 2100; y++)
            total += BalanceCalculator.CountWorkingDays(new DateOnly(y, 1, 1), new DateOnly(y, 12, 31), NoHolidays);
        Assert.InRange(total, 100 * 250, 100 * 255);
        Assert.True(watch.ElapsedMilliseconds < 2000, $"Took {watch.ElapsedMilliseconds} ms");
    }

    // ---- Balances ----

    [Fact]
    public void RangeOverEaster2026_BooksSevenDays()
    {
        // Mon 2026-03-30 .. Fri 2026-04-10: 10 weekdays less Thu 04-02, Fri 04-03, Mon 04-06 = 7
        var data = Data(Range("2026-03-30", "2026-04-10"));

        Assert.Equal(7, data.Days.Count);
        Assert.DoesNotContain(data.Days, d => d.Date == D("2026-04-02") || d.Date == D("2026-04-03") || d.Date == D("2026-04-06"));
        var b = BalanceCalculator.CalculateHolidayYear(data, new HolidayYear(2025), D("2026-03-01"));
        Assert.Equal(7m, b.BookedAheadDays);
        Assert.Equal(18m, b.UnbookedDays);
    }

    [Fact]
    public void ChristmasWeek2026_BooksSixDays()
    {
        // Mon 2026-12-21 .. Thu 2026-12-31: 21, 22, 23, 28, 29, 30
        var days = Bookings.BookRange([], D("2026-12-21"), D("2026-12-31"), DayType.Vacation, "Jul", NoHolidays);

        Assert.Equal(
            [D("2026-12-21"), D("2026-12-22"), D("2026-12-23"), D("2026-12-28"), D("2026-12-29"), D("2026-12-30")],
            days.Select(d => d.Date));
        var b = BalanceCalculator.CalculateHolidayYear(Data(days), new HolidayYear(2025), D("2026-10-24"));
        Assert.Equal(6m, b.BookedAheadDays);
    }

    [Fact]
    public void StoredBookingsOnAutomaticHolidays_AreNotCounted()
    {
        // Records on Mon 2026-04-06 (2. påskedag) and Thu 2026-05-14 (Kristi himmelfartsdag), e.g. from an old file,
        // plus Tue 2026-04-07
        var data = Data([Day("2026-04-06"), Day("2026-05-14", 0.5m), Day("2026-04-07"), Day("2026-04-03", type: DayType.Feriefri)]);

        var b = BalanceCalculator.CalculateHolidayYear(data, new HolidayYear(2025), D("2026-10-24"));
        Assert.Equal(1m, b.UsedDays);
        Assert.Equal([D("2026-04-07")], BalanceCalculator.Allocate(data, D("2026-10-24")).Select(a => a.Date));
        Assert.Equal(0m, FeriefriCalculator.CalculateHolidayYear(data, new HolidayYear(2025), D("2026-10-24")).UsedDays);
    }

    [Fact]
    public void FreeWorkingDaysLeft_ExcludeAutomaticHolidays()
    {
        // Today Mon 2026-12-21: 21, 22, 23, 28, 29, 30 = 6
        Assert.Equal(6m, BalanceCalculator.CalculateCalendarYear(Data(), 2026, D("2026-12-21")).WorkingDaysLeft);
        // Today Mon 2026-03-30 .. Fri 04-10 booked: those 7 aren't free either; the rest of 2026 from 03-30:
        // 2026 has 252 working days; Jan 21 (22 less 01-01), Feb 20, Mar 1..27 = 20 -> 252 - 61 = 191, less the 7 booked = 184
        var data = Data(Range("2026-03-30", "2026-04-10"));
        Assert.Equal(184m, BalanceCalculator.CalculateCalendarYear(data, 2026, D("2026-03-30")).WorkingDaysLeft);
    }

    // ---- Booking ----

    [Fact]
    public void BookDay_OnAnAutomaticHoliday_DoesNothing_AndKeepsExistingRecords()
    {
        var existing = new List<BookedDay> { Day("2026-04-03", 0.5m) }; // Fri, Langfredag, stored earlier

        Assert.Equal(existing, Bookings.BookDay(existing, D("2026-04-03"), DayType.Vacation, 1, "", NoHolidays));
        Assert.Empty(Bookings.BookHalfDay([], D("2026-12-25"), DayType.Vacation, "", NoHolidays)); // Fri, Juledag
        Assert.Empty(Bookings.BookDay([], D("2026-05-14"), DayType.Feriefri, 1, "", NoHolidays));  // Thu, Kristi himmelfartsdag
    }

    [Fact]
    public void BookRange_OnlyHolidays_BooksNothing()
    {
        // Thu 2026-04-02 .. Mon 2026-04-06: Skærtorsdag, Langfredag, Sat, Påskedag, 2. påskedag
        Assert.Empty(Bookings.BookRange([], D("2026-04-02"), D("2026-04-06"), DayType.Vacation, "", NoHolidays));
    }

    [Fact]
    public void BookFeriefri_SkipsAutomaticHolidays()
    {
        // Mon 2026-03-30 .. Fri 2026-04-03: Mon, Tue, Wed are working days; Thu, Fri are holidays
        var r = Bookings.BookFeriefri(Data(), D("2026-03-30"), D("2026-04-03"), 1, "Påske");

        Assert.Equal(3m, r.RequestedDays);
        Assert.Equal(3m, r.BookedDays);
        Assert.True(r.IsComplete);
        Assert.Equal([D("2026-03-30"), D("2026-03-31"), D("2026-04-01")], r.Days.Select(d => d.Date));
    }

    [Fact]
    public void BookFeriefri_SingleHoliday_RequestsNothing_IsNotRefused()
    {
        // Thu 2026-12-24
        var data = Data([Day("2026-12-23")]);
        var r = Bookings.BookFeriefri(data, D("2026-12-24"), D("2026-12-24"), 0.5m, "");

        Assert.Equal(0m, r.RequestedDays);
        Assert.False(r.IsRefused);
        Assert.Equal(data.Days, r.Days);
    }

    [Fact]
    public void BookFeriefri_AllowanceCountsOnlyWorkingDays()
    {
        // Default allowance 5 per holiday year. Mon 2026-12-21 .. Fri 2027-01-08 has 3 + 3 + 5 = 11 working days
        // (24, 25, 31 Dec and 1 Jan are holidays). 2025/26's 5 take 21, 22, 23, 28, 29 (older year first in December);
        // 2026/27's 5 take Wed 30 and Mon 4 .. Thu 7 Jan; Fri 8 Jan is left out.
        var r = Bookings.BookFeriefri(Data(), D("2026-12-21"), D("2027-01-08"), 1, "");

        Assert.Equal(11m, r.RequestedDays);
        Assert.Equal(10m, r.BookedDays);
        Assert.Equal([D("2027-01-08")], r.LeftOutDates);
    }

    [Fact]
    public void GroupPeriods_HolidaysInsideAPeriodDoNotSplitIt()
    {
        // Mon 2026-03-30 .. Fri 2026-04-10 booked: Easter (Thu 2 .. Mon 6) lies inside, one period of 7 days
        var days = Bookings.BookRange([], D("2026-03-30"), D("2026-04-10"), DayType.Vacation, "Påske", NoHolidays);

        var period = Assert.Single(Bookings.GroupPeriods(days, 2026, NoHolidays));
        Assert.Equal(D("2026-03-30"), period.Start);
        Assert.Equal(D("2026-04-10"), period.End);
        Assert.Equal(7m, period.Amount);
    }

    [Fact]
    public void GroupPeriods_AcrossNewYear_HolidaysDoNotSplit()
    {
        // Wed 2026-12-30 and Mon 2027-01-04: Thu 31, Fri 1 Jan and the weekend lie between
        var days = new[] { Day("2026-12-30"), Day("2027-01-04") };

        var period = Assert.Single(Bookings.GroupPeriods(days, 2027, NoHolidays));
        Assert.Equal(D("2026-12-30"), period.Start);
        Assert.Equal(1m, period.Amount); // only 2027's working days
    }

    // ---- Lookup for the UI ----

    [Fact]
    public void PublicHolidaysIn_CombinesAutomaticAndManual()
    {
        // Manual: Thu 2026-12-24 (also automatic) and Fri 2026-06-05 (Grundlovsdag); a stray 2027 date under 2026 is ignored
        var data = Data().WithHolidays("2026-12-24", "2026-06-05");
        data.Years.Single(y => y.Year == 2026).PublicHolidays.Add(D("2027-06-04"));

        var list = data.PublicHolidaysIn(2026);

        Assert.Equal(13, list.Count);
        Assert.Equal(list.OrderBy(h => h.Date), list);
        Assert.Equal(new PublicHoliday(D("2026-06-05"), null, IsAutomatic: false, IsManual: true), list.Single(h => h.Date == D("2026-06-05")));
        Assert.Equal(new PublicHoliday(D("2026-12-24"), "Juleaftensdag", IsAutomatic: true, IsManual: true), list.Single(h => h.Date == D("2026-12-24")));
        Assert.Equal(new PublicHoliday(D("2026-04-05"), "Påskedag", IsAutomatic: true, IsManual: false), list.Single(h => h.Date == D("2026-04-05")));
        Assert.DoesNotContain(list, h => h.Date.Year != 2026);
    }

    [Fact]
    public void FindPublicHoliday_GivesNameAndKind()
    {
        var data = Data().WithHolidays("2026-06-05", "2026-12-25");

        Assert.Equal(new PublicHoliday(D("2026-04-06"), "2. påskedag", true, false), data.FindPublicHoliday(D("2026-04-06")));
        Assert.Equal(new PublicHoliday(D("2026-06-05"), null, false, true), data.FindPublicHoliday(D("2026-06-05")));
        Assert.Equal(new PublicHoliday(D("2026-12-25"), "Juledag", true, true), data.FindPublicHoliday(D("2026-12-25")));
        Assert.Null(data.FindPublicHoliday(D("2026-06-04")));
        Assert.True(data.IsPublicHoliday(D("2026-06-05")));
        Assert.True(data.IsPublicHoliday(D("2026-01-01")));
        Assert.False(data.IsPublicHoliday(D("2026-01-02")));
        Assert.True(data.IsManualPublicHoliday(D("2026-06-05")));
        Assert.False(data.IsManualPublicHoliday(D("2026-01-01")));
    }

    [Fact]
    public void FindPublicHoliday_IgnoresManualDatesStoredUnderAnotherYear()
    {
        var data = Data();
        data.Years.Add(new YearSettings { Year = 2027, PublicHolidays = [D("2026-06-05")] });

        Assert.Null(data.FindPublicHoliday(D("2026-06-05")));
        Assert.Equal(12, data.PublicHolidaysIn(2026).Count);
    }

    [Fact]
    public void Lookups_HandleNullListsFromHandEditedFiles()
    {
        var data = new VacationData { Years = null!, Days = null! };

        Assert.Equal(12, data.PublicHolidaysIn(2026).Count);
        Assert.False(data.IsManualPublicHoliday(D("2026-06-05")));
        Assert.Empty(data.BookedDaysOnHolidays());
    }

    [Fact]
    public void AllPublicHolidays_StillHoldsOnlyTheManualOnes()
    {
        // Nothing automatic is added to the stored data or to the manual set
        var data = Data().WithHolidays("2026-06-05");

        Assert.Equal([D("2026-06-05")], data.AllPublicHolidays());
        Assert.Equal([D("2026-06-05")], data.Years.Single().PublicHolidays);
    }

    // ---- Conflicts ----

    [Fact]
    public void BookedDaysOnHolidays_FindsRecordsOnAutomaticAndManualHolidays()
    {
        var data = Data(
        [
            Day("2026-12-24"),                         // Thu, Juleaftensdag
            Day("2026-06-05", 0.5m, DayType.Feriefri), // Fri, manual
            Day("2026-12-23"),                         // Wed, fine
            Day("2027-01-01"),                         // Fri, Nytårsdag
            Day("2026-12-24", 0.5m),                   // duplicate record: the first wins
            Day("2026-04-04"),                         // Sat: not a holiday (weekends aren't conflicts here)
        ]).WithHolidays("2026-06-05");

        Assert.Equal(
            [D("2026-06-05"), D("2026-12-24"), D("2027-01-01")],
            data.BookedDaysOnHolidays().Select(d => d.Date));
        Assert.Equal(1m, data.BookedDaysOnHolidays().Single(d => d.Date == D("2026-12-24")).Amount);
        Assert.Equal([D("2027-01-01")], data.BookedDaysOnHolidays(2027).Select(d => d.Date));
    }

    // ---- Persistence ----

    [Fact]
    public void OldFile_LoadsUnchanged_AndSavingAddsNoAutomaticHolidays()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "vacation-data.json");
        File.WriteAllText(path, """
            { "Days": [ { "Date": "2026-12-24" }, { "Date": "2026-12-23" } ],
              "Years": [ { "Year": 2026, "PublicHolidays": [ "2026-06-05" ] } ] }
            """);
        var store = new DataStore(path);

        var data = store.Load();
        Assert.Equal(2, data.Days.Count); // the record on Juleaftensdag is kept, just not counted
        Assert.Equal([D("2026-06-05")], data.Years.Single().PublicHolidays);

        store.Save(data);
        var again = store.Load();
        Assert.Equal(2, again.Days.Count);
        Assert.Equal([D("2026-06-05")], again.Years.Single().PublicHolidays);
        Assert.Equal(1m, BalanceCalculator.CalculateHolidayYear(again, new HolidayYear(2025), D("2026-10-24")).BookedAheadDays);
    }
}
