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
            Years = [new() { Year = 2026, AllowanceDays = 25, CarryOverDays = 2.5m, PublicHolidays = [new(2026, 12, 25)] }],
        };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(original.Days, loaded.Days);
        var year = Assert.Single(loaded.Years);
        Assert.Equal(25m, year.AllowanceDays);
        Assert.Equal(2.5m, year.CarryOverDays);
        Assert.Equal([new DateOnly(2026, 12, 25)], year.PublicHolidays);
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
    }
}
