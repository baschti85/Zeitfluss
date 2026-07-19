using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zeitfluss.Models;

namespace Zeitfluss.Services;

public sealed class DataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zeitfluss");

    public string DataFile => Path.Combine(DataDirectory, "arbeitszeiten.json");

    public AppData Load()
    {
        try
        {
            if (!File.Exists(DataFile)) return new AppData();
            var data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(DataFile), JsonOptions) ?? new AppData();
            EnsureSchedule(data.Settings);
            return data;
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory(DataDirectory);
            if (File.Exists(DataFile))
                File.Copy(DataFile, Path.Combine(DataDirectory, $"arbeitszeiten.defekt-{DateTime.Now:yyyyMMdd-HHmmss}.json"), true);
            throw new InvalidDataException("Die lokalen Arbeitszeitdaten konnten nicht gelesen werden. Eine Sicherung wurde angelegt.", ex);
        }
    }

    public void Save(AppData data)
    {
        Directory.CreateDirectory(DataDirectory);
        var temporaryFile = Path.Combine(DataDirectory, $".arbeitszeiten.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryFile, JsonSerializer.Serialize(data, JsonOptions));
            File.Move(temporaryFile, DataFile, true);
        }
        finally { if (File.Exists(temporaryFile)) File.Delete(temporaryFile); }
    }

    private static void EnsureSchedule(AppSettings settings)
    {
        foreach (var day in Enum.GetValues<DayOfWeek>())
            settings.DailyHours.TryAdd(day, 0);
    }
}
