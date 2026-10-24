using VacationCalculator.Core;

namespace VacationCalculator.Core.Tests;

public class DataStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "VacationCalculatorTests", Guid.NewGuid().ToString());

    public void Dispose()
    {
        if (!Directory.Exists(_dir))
            return;
        foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Save_ReadOnlyTarget_ThrowsOriginalException_RemovesTempFile_KeepsTargetContent()
    {
        var store = StoreWith("""{ "Days": [ { "Date": "2026-01-05", "Description": "keep" } ] }""");
        var before = File.ReadAllText(store.Path);
        File.SetAttributes(store.Path, FileAttributes.ReadOnly);

        var ex = Record.Exception(() => store.Save(new VacationData()));

        Assert.IsType<UnauthorizedAccessException>(ex);
        Assert.False(File.Exists(store.Path + ".tmp"));
        Assert.Equal(before, File.ReadAllText(store.Path));
        Assert.Equal("keep", Assert.Single(store.Load().Days).Description);
    }

    [Fact]
    public void Save_LockedTarget_ThrowsIOOrAccessException_RemovesTempFile_KeepsTargetContent()
    {
        var store = StoreWith("""{ "Days": [ { "Date": "2026-01-05", "Description": "keep" } ] }""");
        var before = File.ReadAllText(store.Path);

        Exception? ex;
        using (new FileStream(store.Path, FileMode.Open, FileAccess.Read, FileShare.None))
            ex = Record.Exception(() => store.Save(new VacationData()));

        // Windows reports replacing a file held open without sharing as access denied;
        // either way it is one of the two types the App handles.
        Assert.True(ex is IOException or UnauthorizedAccessException, $"Unexpected: {ex}");
        Assert.False(File.Exists(store.Path + ".tmp"));
        Assert.Equal(before, File.ReadAllText(store.Path));
    }

    [Fact]
    public void Save_StaleTempFileFromEarlierCrash_IsOverwrittenAndRemoved()
    {
        var store = StoreWith("""{ "Days": [] }""");
        // A longer, half-written temp file: truncation must not leave its tail behind
        File.WriteAllText(store.Path + ".tmp", "{ \"Days\": [ garbage " + new string('x', 10_000));

        store.Save(new VacationData { Days = [Day("2026-07-06", "Summer")] }); // Mon

        Assert.False(File.Exists(store.Path + ".tmp"));
        Assert.Equal([Day("2026-07-06", "Summer")], store.Load().Days);
    }

    [Fact]
    public void Save_Normal_LeavesNoTempFile()
    {
        var store = new DataStore(Path.Combine(_dir, "data.json"));

        store.Save(new VacationData { Days = [Day("2026-07-07", "Dentist")] }); // Tue

        Assert.False(File.Exists(store.Path + ".tmp"));
        Assert.Equal([Day("2026-07-07", "Dentist")], store.Load().Days);
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
            TransferAllowed = true,
            HolidayYears =
            [
                new() { StartYear = 2025, TransferLimitDays = 7.5m, PayoutMode = PayoutMode.Some, PayoutDays = 2.5m },
                new() { StartYear = 2026, PayoutMode = PayoutMode.All },
            ],
        };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(original.Days, loaded.Days);
        var year = Assert.Single(loaded.Years);
        Assert.Equal(2026, year.Year);
        Assert.Equal([new DateOnly(2026, 12, 25)], year.PublicHolidays);
        Assert.Equal(new DateOnly(2025, 11, 16), loaded.EmployedSince);
        Assert.True(loaded.TransferAllowed);
        Assert.Equal(2, loaded.HolidayYears.Count);
        var s2025 = loaded.GetDeadlineSettings(new HolidayYear(2025));
        Assert.Equal(7.5m, s2025.TransferLimitDays);
        Assert.Equal(PayoutMode.Some, s2025.PayoutMode);
        Assert.Equal(2.5m, s2025.PayoutDays);
        Assert.True(s2025.PayoutIsOwn);
        var s2026 = loaded.GetDeadlineSettings(new HolidayYear(2026));
        Assert.Equal(7.5m, s2026.TransferLimitDays); // inherited, not stored on 2026
        Assert.Null(loaded.FindHolidayYear(2026)!.TransferLimitDays);
        Assert.Equal(PayoutMode.All, s2026.PayoutMode);
    }

    [Fact]
    public void Save_WritesTheNewNamesOnly_PayoutModeAsText()
    {
        var path = Path.Combine(_dir, "data.json");
        var data = new VacationData { TransferAllowed = true };
        data.SetPayout(new HolidayYear(2025), PayoutMode.Some, 3m);

        new DataStore(path).Save(data);
        var json = File.ReadAllText(path);

        Assert.Contains("\"TransferAllowed\": true", json);
        Assert.Contains("\"Some\"", json);
        Assert.DoesNotContain("FifthWeekTransferAllowed", json);
        Assert.DoesNotContain("TransferredToNext", json);
    }

    [Fact]
    public void Load_FileFromFeature2_KeepsTransferAllowedAndIgnoresTransferredToNext()
    {
        // Format of feature 2: FifthWeekTransferAllowed and a registered TransferredToNext per holiday year
        var store = StoreWith("""
            {
              "Days": [ { "Date": "2026-01-05", "Type": "Vacation", "Amount": 1, "Description": "" } ],
              "Years": [],
              "EmployedSince": null,
              "FifthWeekTransferAllowed": true,
              "HolidayYears": [ { "StartYear": 2025, "TransferredToNext": 2.5 } ]
            }
            """);

        var data = store.Load();

        Assert.True(data.TransferAllowed);
        var entry = Assert.Single(data.HolidayYears);
        Assert.Equal(2025, entry.StartYear);
        Assert.Null(entry.TransferLimitDays);
        Assert.Null(entry.PayoutMode);
        var settings = data.GetDeadlineSettings(new HolidayYear(2025));
        Assert.Equal(5m, settings.TransferLimitDays);
        Assert.Equal(PayoutMode.None, settings.PayoutMode);
        // 1 used Mon 2026-01-05 -> 24 unused: the calculated 5 are transferred, not the stored 2.5
        Assert.Equal(5m, BalanceCalculator.CalculateHolidayYear(data, new HolidayYear(2025), new(2026, 10, 24)).TransferredToNext);
    }

    [Fact]
    public void Load_LegacyTransferOff_StaysOff()
    {
        Assert.False(StoreWith("""{ "FifthWeekTransferAllowed": false }""").Load().TransferAllowed);
    }

    [Fact]
    public void Load_AcceptsPayoutModeAsNumber()
    {
        var data = StoreWith("""{ "HolidayYears": [ { "StartYear": 2025, "PayoutMode": 2 } ] }""").Load();

        Assert.Equal(PayoutMode.All, data.GetDeadlineSettings(new HolidayYear(2025)).PayoutMode);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEmptyEmployedSince()
    {
        var store = new DataStore(Path.Combine(_dir, "data.json"));
        store.Save(new VacationData());

        var loaded = store.Load();

        Assert.Null(loaded.EmployedSince);
        Assert.False(loaded.TransferAllowed);
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
        Assert.False(data.TransferAllowed);
        Assert.Empty(data.HolidayYears);
    }

    [Fact]
    public void Load_NullHolidayYearsIsTolerated()
    {
        var data = StoreWith("""{ "HolidayYears": null, "FifthWeekTransferAllowed": true }""").Load();

        Assert.Null(data.FindHolidayYear(2025));
        Assert.True(data.TransferAllowed);
        Assert.Equal(25m, BalanceCalculator.CalculateHolidayYear(data, new HolidayYear(2025), new(2026, 10, 24)).UnbookedDays);
        Assert.Equal(5m, data.GetDeadlineSettings(new HolidayYear(2025)).TransferLimitDays);
        data.SetTransferLimit(new HolidayYear(2025), 1);
        Assert.Equal(1m, data.FindHolidayYear(2025)!.TransferLimitDays);
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
        Assert.Null(data.FirstTrackedHolidayYear);
        Assert.Equal(0m, data.OpeningBalanceDays);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsFirstTrackedYearAndOpeningBalance()
    {
        var path = Path.Combine(_dir, "data.json");
        var store = new DataStore(path);

        store.Save(new VacationData { FirstTrackedHolidayYear = 2025, OpeningBalanceDays = 3.5m });
        var json = File.ReadAllText(path);
        var loaded = store.Load();

        Assert.Contains("\"FirstTrackedHolidayYear\": 2025", json);
        Assert.Contains("\"OpeningBalanceDays\": 3.5", json);
        Assert.Equal(2025, loaded.FirstTrackedHolidayYear);
        Assert.Equal(3.5m, loaded.OpeningBalanceDays);
    }

    [Fact]
    public void SaveThenLoad_FirstTrackedYearNotSet_StaysNull()
    {
        var store = new DataStore(Path.Combine(_dir, "data.json"));

        store.Save(new VacationData());
        var loaded = store.Load();

        Assert.Null(loaded.FirstTrackedHolidayYear);
        Assert.Equal(0m, loaded.OpeningBalanceDays);
    }

    [Fact]
    public void Load_FileWithoutOpeningBalance_KeepsEverythingAndDefaults()
    {
        // Format of feature 2b before the opening balance
        var data = StoreWith("""
            {
              "Days": [ { "Date": "2026-01-05", "Type": "Vacation", "Amount": 1, "Description": "keep" } ],
              "Years": [],
              "EmployedSince": null,
              "TransferAllowed": true,
              "HolidayYears": [ { "StartYear": 2025, "TransferLimitDays": 8, "PayoutMode": "Some", "PayoutDays": 2 } ]
            }
            """).Load();

        Assert.Null(data.FirstTrackedHolidayYear);
        Assert.Equal(0m, data.OpeningBalanceDays);
        Assert.True(data.TransferAllowed);
        Assert.Equal("keep", Assert.Single(data.Days).Description);
        Assert.Equal(8m, data.GetDeadlineSettings(new HolidayYear(2025)).TransferLimitDays);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2147483648")]
    [InlineData("10000")]
    public void Load_HandEditedFirstTrackedYear_CalculatesWithoutThrowing_AndSavesTheValueUnchanged(string stored)
    {
        var store = StoreWith($$"""
            {
              "Days": [ { "Date": "2026-01-05", "Type": "Vacation", "Amount": 1, "Description": "keep" } ],
              "TransferAllowed": true,
              "FirstTrackedHolidayYear": {{stored}}
            }
            """);
        var data = store.Load();
        var today = new DateOnly(2026, 10, 24); // Sat

        var status = BalanceCalculator.CalculateCalendarYear(data, 2026, today);
        BalanceCalculator.CalculateHolidayYears(data, HolidayYear.InCalendarYear(2026), today);
        store.Save(data);

        Assert.Equal(1m, status.HolidayYear.UsedDays);
        Assert.Equal(int.Parse(stored), store.Load().FirstTrackedHolidayYear);
        Assert.Equal("keep", Assert.Single(store.Load().Days).Description);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsFeriefriAllowance()
    {
        var path = Path.Combine(_dir, "data.json");
        var store = new DataStore(path);
        var data = new VacationData();
        data.SetFeriefriAllowance(new HolidayYear(2025), 7.5m);
        data.SetTransferLimit(new HolidayYear(2026), 8m);

        store.Save(data);
        var json = File.ReadAllText(path);
        var loaded = store.Load();

        Assert.Contains("\"FeriefriAllowanceDays\": 7.5", json);
        Assert.Equal(7.5m, loaded.FindHolidayYear(2025)!.FeriefriAllowanceDays);
        Assert.Null(loaded.FindHolidayYear(2026)!.FeriefriAllowanceDays);
        var inherited = loaded.GetFeriefriAllowance(new HolidayYear(2026));
        Assert.Equal(7.5m, inherited.Days);
        Assert.True(inherited.IsInherited);
    }

    [Fact]
    public void Load_FileWithoutFeriefriAllowance_UsesTheDefault5_AndKeepsFeriefriDays()
    {
        // Format before feature 3: Feriefri days stored, no allowance per holiday year
        var data = StoreWith("""
            {
              "Days": [ { "Date": "2026-01-05", "Type": "Feriefri", "Amount": 1, "Description": "keep" } ],
              "TransferAllowed": true,
              "HolidayYears": [ { "StartYear": 2025, "TransferLimitDays": 8, "PayoutMode": "None", "PayoutDays": null } ],
              "FirstTrackedHolidayYear": 2025,
              "OpeningBalanceDays": 0
            }
            """).Load();

        Assert.Null(data.FindHolidayYear(2025)!.FeriefriAllowanceDays);
        Assert.True(data.GetFeriefriAllowance(new HolidayYear(2025)).IsDefault);
        // Mon 2026-01-05 used: 4 of 5 left
        Assert.Equal(4m, FeriefriCalculator.CalculateHolidayYear(data, new HolidayYear(2025), new(2026, 3, 2)).RemainingDays);
        Assert.Equal(8m, data.GetDeadlineSettings(new HolidayYear(2025)).TransferLimitDays);
    }

    [Fact]
    public void Load_NegativeOpeningBalance_ReadsAs0()
    {
        var data = StoreWith("""{ "FirstTrackedHolidayYear": 2025, "OpeningBalanceDays": -2 }""").Load();

        Assert.Equal(2025, data.FirstTrackedHolidayYear);
        Assert.Equal(0m, data.OpeningBalanceDays);
    }
}
