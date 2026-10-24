using VacationCalculator.Core;

namespace VacationCalculator.Core.Tests;

public class DataStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "VacationCalculatorTests", Guid.NewGuid().ToString());

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static DateOnly D(string date) => DateOnly.Parse(date);

    private static BookedDay Day(string date, string description = "", decimal amount = 1, DayType type = DayType.Vacation) =>
        new() { Date = D(date), Description = description, Amount = amount, Type = type };

    private DataStore StoreWith(string json)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "data.json");
        File.WriteAllText(path, json);
        return new DataStore(path);
    }

    [Fact]
    public void Load_ReturnsEmptyDataWhenFileIsMissing()
    {
        var data = new DataStore(Path.Combine(_dir, "missing.json")).Load();
        Assert.Empty(data.Days);
        Assert.Empty(data.Years);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new DataStore(Path.Combine(_dir, "data.json"));
        var original = new VacationData
        {
            Days =
            [
                Day("2026-07-06", "Summer"),                              // Mon
                Day("2026-07-07", "Dentist", 0.5m, DayType.Feriefri),     // Tue
            ],
            Years = [new() { Year = 2026, PublicHolidays = [new(2026, 12, 25)] }],
            EmployedSince = new(2025, 11, 16),
            FifthWeekTransferAllowed = true,
            HolidayYears = [new() { StartYear = 2025, TransferredToNext = 2.5m }],
        };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(original.Days, loaded.Days);
        var year = Assert.Single(loaded.Years);
        Assert.Equal(2026, year.Year);
        Assert.Equal([new DateOnly(2026, 12, 25)], year.PublicHolidays);
        Assert.Equal(new DateOnly(2025, 11, 16), loaded.EmployedSince);
        Assert.True(loaded.FifthWeekTransferAllowed);
        var holidayYear = Assert.Single(loaded.HolidayYears);
        Assert.Equal(2025, holidayYear.StartYear);
        Assert.Equal(2.5m, holidayYear.TransferredToNext);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEmptyEmployedSince()
    {
        var store = new DataStore(Path.Combine(_dir, "data.json"));
        store.Save(new VacationData());

        var loaded = store.Load();

        Assert.Null(loaded.EmployedSince);
        Assert.False(loaded.FifthWeekTransferAllowed);
        Assert.Empty(loaded.HolidayYears);
    }

    [Fact]
    public void Load_FileFromBeforeTheHolidayAct_KeepsDaysAndHolidaysAndIgnoresOldAllowance()
    {
        // Format before feature 2: AllowanceDays/CarryOverDays per year, no holiday-year settings
        var store = StoreWith("""
            {
              "Days": [ { "Date": "2026-07-06", "Type": "Vacation", "Amount": 1, "Description": "Summer" } ],
              "Years": [ { "Year": 2026, "AllowanceDays": 25, "CarryOverDays": 2.5, "PublicHolidays": [ "2026-12-25" ] } ]
            }
            """);

        var data = store.Load();

        Assert.Equal([Day("2026-07-06", "Summer")], data.Days);
        var year = Assert.Single(data.Years);
        Assert.Equal([new DateOnly(2026, 12, 25)], year.PublicHolidays);
        Assert.Null(data.EmployedSince);
        Assert.False(data.FifthWeekTransferAllowed);
        Assert.Empty(data.HolidayYears);
    }

    [Fact]
    public void Load_NullHolidayYearsIsTolerated()
    {
        var data = StoreWith("""{ "HolidayYears": null, "FifthWeekTransferAllowed": true }""").Load();

        Assert.Null(data.FindHolidayYear(2025));
        Assert.Equal(25m, BalanceCalculator.CalculateHolidayYear(data, new HolidayYear(2025), new(2026, 10, 24)).UnbookedDays);
        data.GetOrAddHolidayYear(2025).TransferredToNext = 1;
        Assert.Equal(1m, data.FindHolidayYear(2025)!.TransferredToNext);
    }

    [Fact]
    public void Save_WritesDayTypeAsText()
    {
        var path = Path.Combine(_dir, "data.json");
        var store = new DataStore(path);

        store.Save(new VacationData { Days = [Day("2026-07-07", amount: 0.5m, type: DayType.Feriefri)] });
        var json = File.ReadAllText(path);

        Assert.Contains("\"Feriefri\"", json);
        Assert.Contains("0.5", json);
    }

    [Fact]
    public void Load_AcceptsDayTypeAsNumber()
    {
        var store = StoreWith("""
            {
              "Days": [ { "Date": "2026-07-07", "Type": 1, "Amount": 0.5, "Description": "" } ],
              "Years": []
            }
            """);

        Assert.Equal([Day("2026-07-07", amount: 0.5m, type: DayType.Feriefri)], store.Load().Days);
    }

    [Fact]
    public void Load_DayWithMissingFieldsGetsDefaults()
    {
        var store = StoreWith("""{ "Days": [ { "Date": "2026-07-07" } ] }""");

        Assert.Equal([Day("2026-07-07")], store.Load().Days);
    }

    [Fact]
    public void Load_EmptyObjectGivesEmptyData()
    {
        var data = StoreWith("{}").Load();

        Assert.Empty(data.Days);
        Assert.Empty(data.Years);
        Assert.Empty(data.HolidayYears);
        Assert.Null(data.EmployedSince);
    }
}
