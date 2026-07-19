using System.Globalization;
using System.IO;
using System.Text;
using Zeitfluss.Models;

namespace Zeitfluss.Services;

public static class CsvExporter
{
    public static void Export(string path, AppData data, DateOnly through, DateTime now)
    {
        var culture = CultureInfo.GetCultureInfo("de-DE");
        var lines = new List<string> { "Datum;Wochentag;Soll;Ist;Tagessaldo;Gesamtsaldo;Intervalle" };
        lines.AddRange(TimeCalculator.Daily(data, through, now).Select(day => string.Join(';',
            Escape(day.Date.ToString("dd.MM.yyyy", culture)),
            Escape(day.Date.ToDateTime(TimeOnly.MinValue).ToString("dddd", culture)),
            Escape(TimeCalculator.FormatDuration(day.Target)),
            Escape(TimeCalculator.FormatDuration(day.Actual)),
            Escape(TimeCalculator.FormatDuration(day.Balance, true)),
            Escape(TimeCalculator.FormatDuration(day.Cumulative, true)),
            Escape(day.Intervals))));
        File.WriteAllLines(path, lines, new UTF8Encoding(true));
    }

    private static string Escape(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
