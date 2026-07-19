using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Zeitfluss.Models;
using Zeitfluss.Services;

namespace Zeitfluss;

public partial class SettingsWindow : Window
{
    private readonly AppData _data;
    private readonly Dictionary<DayOfWeek, TextBox> _fields;
    private readonly CultureInfo _culture = CultureInfo.GetCultureInfo("de-DE");

    public SettingsWindow(AppData data)
    {
        InitializeComponent();
        _data = data;
        _fields = new()
        {
            [DayOfWeek.Monday] = MondayText, [DayOfWeek.Tuesday] = TuesdayText, [DayOfWeek.Wednesday] = WednesdayText,
            [DayOfWeek.Thursday] = ThursdayText, [DayOfWeek.Friday] = FridayText, [DayOfWeek.Saturday] = SaturdayText, [DayOfWeek.Sunday] = SundayText
        };
        WeeklyText.Text = data.Settings.WeeklyHours.ToString("0.##", _culture);
        foreach (var (day, field) in _fields) field.Text = data.Settings.DailyHours.GetValueOrDefault(day).ToString("0.##", _culture);
        FiveMinuteRoundingCheckBox.IsChecked = data.Settings.UseFiveMinuteRounding;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;
        if (!TryRead(WeeklyText, out var weekly) || weekly is < 0 or > 168) { ValidationText.Text = "Bitte gib gültige Wochenstunden zwischen 0 und 168 ein."; return; }
        var values = new Dictionary<DayOfWeek, double>();
        foreach (var (day, field) in _fields)
        {
            if (!TryRead(field, out var value) || value is < 0 or > 24) { ValidationText.Text = "Bitte gib für jeden Tag einen Wert zwischen 0 und 24 Stunden ein."; return; }
            values[day] = value;
        }
        var total = values.Values.Sum();
        if (Math.Abs(total - weekly) > 0.005) { ValidationText.Text = $"Die Tagessollzeiten ergeben {total:0.##} Stunden. Sie müssen den {weekly:0.##} Wochenstunden entsprechen."; return; }
        _data.Settings.WeeklyHours = weekly;
        _data.Settings.DailyHours = values;
        _data.Settings.UseFiveMinuteRounding = FiveMinuteRoundingCheckBox.IsChecked == true;
        DialogResult = true;
    }

    private bool TryRead(TextBox field, out double value) => double.TryParse(field.Text, NumberStyles.Number, _culture, out value) || double.TryParse(field.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        BackupStatusText.Text = string.Empty;
        if (_data.Intervals.Any(x => x.EndedAt is null))
        {
            MessageBox.Show("Bitte pausiere die laufende Arbeitszeit vor der Sicherung. Sonst würde die Transferzeit auf einem neuen PC als Arbeitszeit weiterlaufen.", "Arbeitszeit läuft", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = "Zeitfluss-Sicherung exportieren",
            Filter = "Zeitfluss-Sicherung (*.zeitfluss)|*.zeitfluss",
            FileName = $"Zeitfluss-Sicherung-{DateTime.Now:yyyy-MM-dd}.zeitfluss",
            DefaultExt = BackupService.FileExtension
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            BackupService.Export(dialog.FileName, _data);
            BackupStatusText.Text = $"Sicherung gespeichert: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show($"Die Sicherung konnte nicht erstellt werden.\n\n{ex.Message}", "Zeitfluss", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportBackup_Click(object sender, RoutedEventArgs e)
    {
        BackupStatusText.Text = string.Empty;
        var dialog = new OpenFileDialog
        {
            Title = "Zeitfluss-Sicherung importieren",
            Filter = "Zeitfluss-Sicherung (*.zeitfluss)|*.zeitfluss",
            DefaultExt = BackupService.FileExtension,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var imported = BackupService.Import(dialog.FileName);
            var openNotice = imported.Intervals.Any(x => x.EndedAt is null) ? "\n\nAchtung: Die Sicherung enthält ein offenes Arbeitsintervall, das nach dem Import weiterläuft." : string.Empty;
            var answer = MessageBox.Show(
                $"Die Sicherung enthält {imported.Intervals.Count} Arbeitsintervalle ab dem {imported.TrackingStartedOn:dd.MM.yyyy}.\n\nDer aktuelle Datenbestand wird vollständig ersetzt.{openNotice}\n\nFortfahren?",
                "Zeitfluss-Sicherung importieren", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
            var store = new DataStore();
            Directory.CreateDirectory(store.DataDirectory);
            var beforeImportPath = Path.Combine(store.DataDirectory, $"Zeitfluss-vor-Import-{DateTime.Now:yyyyMMdd-HHmmss}.zeitfluss");
            BackupService.Export(beforeImportPath, _data);
            BackupService.PreserveLocalWindowPlacement(_data, imported);
            store.Save(imported);
            BackupService.Apply(_data, imported);
            DialogResult = true;
        }
        catch (InvalidDataException ex)
        {
            MessageBox.Show(ex.Message, "Ungültige Zeitfluss-Sicherung", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Die Sicherung konnte nicht gelesen werden.\n\n{ex.Message}", "Zeitfluss", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
