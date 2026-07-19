using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zeitfluss.Models;

namespace Zeitfluss.Services;

public static class BackupService
{
    public const string FileExtension = ".zeitfluss";
    private const string FormatName = "ZeitflussBackup";
    private const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };

    public static void Export(string path, AppData data)
    {
        Validate(data);
        var envelope = new BackupEnvelope
        {
            Format = FormatName,
            Version = CurrentVersion,
            CreatedAtUtc = DateTime.UtcNow,
            Data = data
        };
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidDataException("Der Sicherungspfad ist ungültig.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(envelope, JsonOptions));
            File.Move(temporaryPath, fullPath, true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    public static AppData Import(string path)
    {
        if (new FileInfo(path).Length > 50 * 1024 * 1024)
            throw new InvalidDataException("Die ausgewählte Sicherung ist ungewöhnlich groß und wird aus Sicherheitsgründen nicht importiert.");
        BackupEnvelope? envelope;
        try { envelope = JsonSerializer.Deserialize<BackupEnvelope>(File.ReadAllText(path), JsonOptions); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException("Die ausgewählte Datei ist keine lesbare Zeitfluss-Sicherung.", ex);
        }

        if (envelope is null || envelope.Format != FormatName)
            throw new InvalidDataException("Die ausgewählte Datei ist keine Zeitfluss-Sicherung.");
        if (envelope.Version != CurrentVersion)
            throw new InvalidDataException($"Die Sicherungsversion {envelope.Version} wird von dieser Zeitfluss-Version nicht unterstützt.");
        if (envelope.Data is null)
            throw new InvalidDataException("Die Sicherung enthält keine Arbeitszeitdaten.");

        Validate(envelope.Data);
        return envelope.Data;
    }

    public static void Apply(AppData target, AppData source)
    {
        target.TrackingStartedOn = source.TrackingStartedOn;
        target.Settings = source.Settings;
        target.Intervals = source.Intervals;
        target.FinishedDays = source.FinishedDays;
    }

    public static void PreserveLocalWindowPlacement(AppData local, AppData imported)
    {
        imported.Settings.WindowLeft = local.Settings.WindowLeft;
        imported.Settings.WindowTop = local.Settings.WindowTop;
        imported.Settings.CompactWindowLeft = local.Settings.CompactWindowLeft;
        imported.Settings.CompactWindowTop = local.Settings.CompactWindowTop;
    }

    public static void Validate(AppData data)
    {
        if (data.Settings is null || data.Intervals is null || data.FinishedDays is null)
            throw new InvalidDataException("Die Sicherung ist unvollständig.");
        if (data.Settings.WeeklyHours is < 0 or > 168 || !double.IsFinite(data.Settings.WeeklyHours))
            throw new InvalidDataException("Die Sicherung enthält ungültige Wochenstunden.");
        if (data.Settings.DailyHours is null)
            throw new InvalidDataException("Die Sicherung enthält keine Tagessollzeiten.");
        if (data.TrackingStartedOn == default)
            throw new InvalidDataException("Die Sicherung enthält kein gültiges Startdatum.");
        if (data.Settings.DailyHours.Count != Enum.GetValues<DayOfWeek>().Length || data.Settings.DailyHours.Keys.Any(day => !Enum.IsDefined(day)))
            throw new InvalidDataException("Die Sicherung enthält unbekannte oder doppelte Wochentage.");

        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            if (!data.Settings.DailyHours.TryGetValue(day, out var value) || value is < 0 or > 24 || !double.IsFinite(value))
                throw new InvalidDataException($"Die Sicherung enthält eine ungültige Sollzeit für {day}.");
        }
        if (Math.Abs(data.Settings.DailyHours.Values.Sum() - data.Settings.WeeklyHours) > 0.01)
            throw new InvalidDataException("Tagessollzeiten und Wochenstunden der Sicherung stimmen nicht überein.");
        if (data.Intervals.Any(x => x is null))
            throw new InvalidDataException("Die Sicherung enthält ein leeres Arbeitsintervall.");
        if (data.Intervals.Count(x => x.EndedAt is null) > 1)
            throw new InvalidDataException("Die Sicherung enthält mehrere gleichzeitig offene Arbeitsintervalle.");
        if (data.Intervals.Any(x => x.Id == Guid.Empty || x.StartedAt == default || x.EndedAt < x.StartedAt))
            throw new InvalidDataException("Die Sicherung enthält ein ungültiges Arbeitsintervall.");
        if (data.Intervals.Select(x => x.Id).Distinct().Count() != data.Intervals.Count)
            throw new InvalidDataException("Die Sicherung enthält doppelte Arbeitsintervalle.");
        if (data.Intervals.Any(x => DateOnly.FromDateTime(x.StartedAt) < data.TrackingStartedOn) || data.FinishedDays.Any(x => x < data.TrackingStartedOn))
            throw new InvalidDataException("Die Sicherung enthält Daten vor dem Beginn der Zeiterfassung.");
        var ordered = data.Intervals.OrderBy(x => x.StartedAt).ToList();
        for (var index = 1; index < ordered.Count; index++)
        {
            if ((ordered[index - 1].EndedAt ?? DateTime.MaxValue) > ordered[index].StartedAt)
                throw new InvalidDataException("Die Sicherung enthält überlappende Arbeitsintervalle.");
        }
        if (data.Intervals.FirstOrDefault(x => x.EndedAt is null) is { } openInterval && data.FinishedDays.Contains(DateOnly.FromDateTime(openInterval.StartedAt)))
            throw new InvalidDataException("Ein als beendet markierter Tag enthält ein offenes Arbeitsintervall.");
        ValidatePosition(data.Settings.WindowLeft, "Hauptfenster");
        ValidatePosition(data.Settings.WindowTop, "Hauptfenster");
        ValidatePosition(data.Settings.CompactWindowLeft, "Kompaktfenster");
        ValidatePosition(data.Settings.CompactWindowTop, "Kompaktfenster");
    }

    private static void ValidatePosition(double? value, string label)
    {
        if (value is double position && !double.IsFinite(position))
            throw new InvalidDataException($"Die Sicherung enthält eine ungültige Position für das {label}.");
    }

    private sealed class BackupEnvelope
    {
        public string Format { get; set; } = string.Empty;
        public int Version { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public AppData? Data { get; set; }
    }
}
