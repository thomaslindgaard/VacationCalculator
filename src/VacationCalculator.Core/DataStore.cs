using System.Text.Json;

namespace VacationCalculator.Core;

/// <summary>Loads and saves <see cref="VacationData"/> as a JSON file.</summary>
public sealed class DataStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Path { get; } = path;

    public static string DefaultPath =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VacationCalculator",
            "vacation-data.json");

    public VacationData Load()
    {
        if (!File.Exists(Path))
            return new VacationData();

        using var stream = File.OpenRead(Path);
        return JsonSerializer.Deserialize<VacationData>(stream, Options) ?? new VacationData();
    }

    public void Save(VacationData data)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);

        // Write to a temp file first so a crash mid-write can't corrupt existing data.
        var tempPath = Path + ".tmp";
        using (var stream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(stream, data, Options);
        }
        File.Move(tempPath, Path, overwrite: true);
    }
}
