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

    [Fact]
    public void Load_ReturnsEmptyDataWhenFileIsMissing()
    {
        var data = new DataStore(Path.Combine(_dir, "missing.json")).Load();
        Assert.Empty(data.Entries);
        Assert.Empty(data.Years);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new DataStore(Path.Combine(_dir, "data.json"));
        var original = new VacationData
        {
            Entries = [new() { Start = new(2026, 7, 6), End = new(2026, 7, 17), Description = "Summer" }],
            Years = [new() { Year = 2026, AllowanceDays = 25, CarryOverDays = 2.5m, PublicHolidays = [new(2026, 12, 25)] }],
        };

        store.Save(original);
        var loaded = store.Load();

        var entry = Assert.Single(loaded.Entries);
        Assert.Equal(original.Entries[0].Id, entry.Id);
        Assert.Equal(new DateOnly(2026, 7, 17), entry.End);
        Assert.Equal("Summer", entry.Description);

        var year = Assert.Single(loaded.Years);
        Assert.Equal(2.5m, year.CarryOverDays);
        Assert.Equal([new DateOnly(2026, 12, 25)], year.PublicHolidays);
    }
}
