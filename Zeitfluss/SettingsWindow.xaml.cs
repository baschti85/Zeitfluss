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
    private readonly double _originalOpacityPercent;
    private bool _committed;
    private bool _isInitializing = true;

    public SettingsWindow(AppData data)
    {
        InitializeComponent();
        _data = data;
        _originalOpacityPercent = WindowAppearance.NormalizePercent(data.Settings.WindowOpacityPercent);
        Closing += (_, _) => RestoreOwnerPreview();
        _fields = new()
        {
            [DayOfWeek.Monday] = MondayText, [DayOfWeek.Tuesday] = TuesdayText, [DayOfWeek.Wednesday] = WednesdayText,
            [DayOfWeek.Thursday] = ThursdayText, [DayOfWeek.Friday] = FridayText, [DayOfWeek.Saturday] = SaturdayText, [DayOfWeek.Sunday] = SundayText
        };

        WeeklyText.Text = data.Settings.WeeklyHours.ToString("0.##", _culture);
        foreach (var (day, field) in _fields) field.Text = data.Settings.DailyHours.GetValueOrDefault(day).ToString("0.##", _culture);
        FiveMinuteRoundingCheckBox.IsChecked = data.Settings.UseFiveMinuteRounding;
        ForgottenTimerCheckBox.IsChecked = data.Settings.EnableForgottenTimerAssistant;
        GlobalHotKeysCheckBox.IsChecked = data.Settings.EnableGlobalHotKeys;
        EndReminderCheckBox.IsChecked = data.Settings.EnableEndOfDayReminder;
        AlwaysOnTopCheckBox.IsChecked = data.Settings.AlwaysOnTop;
        SelectComboByTag(IdleThresholdComboBox, data.Settings.IdleThresholdMinutes.ToString(CultureInfo.InvariantCulture));
        SelectComboByTag(HotKeyPresetComboBox, data.Settings.HotKeyPreset.ToString());
        SelectComboByTag(ReminderLeadComboBox, data.Settings.ReminderLeadMinutes.ToString(CultureInfo.InvariantCulture));
        OpacitySlider.Value = WindowAppearance.NormalizePercent(data.Settings.WindowOpacityPercent);
        UpdateOpacityPreview(OpacitySlider.Value);
        _isInitializing = false;
        SetDirty(false);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;
        if (!TryRead(WeeklyText, out var weekly) || weekly is < 0 or > 168) { ShowWorkTimeError("Bitte gib gültige Wochenstunden zwischen 0 und 168 ein."); return; }
        var values = new Dictionary<DayOfWeek, double>();
        foreach (var (day, field) in _fields)
        {
            if (!TryRead(field, out var value) || value is < 0 or > 24) { ShowWorkTimeError("Bitte gib für jeden Tag einen Wert zwischen 0 und 24 Stunden ein."); return; }
            values[day] = value;
        }
        var total = values.Values.Sum();
        if (Math.Abs(total - weekly) > 0.005) { ShowWorkTimeError($"Die Tagessollzeiten ergeben {total:0.##} Stunden. Sie müssen den {weekly:0.##} Wochenstunden entsprechen."); return; }

        _data.Settings.WeeklyHours = weekly;
        _data.Settings.DailyHours = values;
        _data.Settings.UseFiveMinuteRounding = FiveMinuteRoundingCheckBox.IsChecked == true;
        _data.Settings.WindowOpacityPercent = WindowAppearance.NormalizePercent(OpacitySlider.Value);
        _data.Settings.AlwaysOnTop = AlwaysOnTopCheckBox.IsChecked == true;
        _data.Settings.EnableForgottenTimerAssistant = ForgottenTimerCheckBox.IsChecked == true;
        _data.Settings.IdleThresholdMinutes = ReadComboInt(IdleThresholdComboBox, 10);
        _data.Settings.EnableGlobalHotKeys = GlobalHotKeysCheckBox.IsChecked == true;
        _data.Settings.HotKeyPreset = ReadHotKeyPreset();
        _data.Settings.EnableEndOfDayReminder = EndReminderCheckBox.IsChecked == true;
        _data.Settings.ReminderLeadMinutes = ReadComboInt(ReminderLeadComboBox, 5);
        _committed = true;
        DialogResult = true;
    }

    private void SettingChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitializing) SetDirty(true);
    }

    private void SettingChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitializing) SetDirty(true);
    }

    private void SettingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitializing) SetDirty(true);
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateOpacityPreview(e.NewValue);
        if (!_isInitializing) SetDirty(true);
    }

    private void UpdateOpacityPreview(double percent)
    {
        var normalized = WindowAppearance.NormalizePercent(percent);
        OpacityValueText.Text = $"{normalized:0} % Deckkraft";
        Opacity = WindowAppearance.ToOpacity(normalized);
        if (Owner is not null) Owner.Opacity = WindowAppearance.ToOpacity(normalized);
    }

    private void SetDirty(bool dirty)
    {
        SaveButton.IsEnabled = dirty;
        DirtyStatusText.Text = dirty ? "Nicht gespeicherte Änderungen" : "Alle Änderungen gespeichert";
        DirtyStatusText.Foreground = (System.Windows.Media.Brush)FindResource(dirty ? "Accent" : "MutedInk");
    }

    private void RestoreOwnerPreview()
    {
        if (!_committed && Owner is not null) Owner.Opacity = WindowAppearance.ToOpacity(_originalOpacityPercent);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { RestoreOwnerPreview(); DialogResult = false; }

    private void WorkTimeNavigation_Click(object sender, RoutedEventArgs e) => ShowSection(SettingsScroll, WorkTimeNavigation);
    private void CaptureNavigation_Click(object sender, RoutedEventArgs e) => ShowSection(CaptureScroll, CaptureNavigation);
    private void AppearanceNavigation_Click(object sender, RoutedEventArgs e) => ShowSection(AppearanceScroll, AppearanceNavigation);
    private void DataNavigation_Click(object sender, RoutedEventArgs e) => ShowSection(DataScroll, DataNavigation);

    private void ShowSection(ScrollViewer selected, RadioButton? navigation = null)
    {
        if (navigation is not null) navigation.IsChecked = true;
        foreach (var section in new[] { SettingsScroll, CaptureScroll, AppearanceScroll, DataScroll })
            section.Visibility = ReferenceEquals(section, selected) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Preset40_Click(object sender, RoutedEventArgs e) => ApplyWeekdayPreset(40);
    private void Preset35_Click(object sender, RoutedEventArgs e) => ApplyWeekdayPreset(35);
    private void Preset30_Click(object sender, RoutedEventArgs e) => ApplyWeekdayPreset(30);

    private void ApplyWeekdayPreset(double weekly)
    {
        WeeklyText.Text = weekly.ToString("0.##", _culture);
        var perDay = (weekly / 5).ToString("0.##", _culture);
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }) _fields[day].Text = perDay;
        _fields[DayOfWeek.Saturday].Text = "0";
        _fields[DayOfWeek.Sunday].Text = "0";
        SetDirty(true);
    }

    private void CopyMonday_Click(object sender, RoutedEventArgs e)
    {
        foreach (var day in new[] { DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }) _fields[day].Text = MondayText.Text;
        var parsedValues = new List<double>();
        foreach (var field in _fields.Values)
        {
            if (!TryRead(field, out var value))
            {
                SetDirty(true);
                return;
            }
            parsedValues.Add(value);
        }
        WeeklyText.Text = parsedValues.Sum().ToString("0.##", _culture);
        SetDirty(true);
    }

    private void ShowWorkTimeError(string message)
    {
        WorkTimeNavigation.IsChecked = true;
        ShowSection(SettingsScroll);
        ValidationText.Text = message;
    }

    private bool TryRead(TextBox field, out double value) =>
        double.TryParse(field.Text, NumberStyles.Number, _culture, out value) ||
        double.TryParse(field.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    private static void SelectComboByTag(ComboBox comboBox, string tag)
    {
        comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag?.ToString(), tag)) ?? comboBox.Items[0];
    }

    private static int ReadComboInt(ComboBox comboBox, int fallback) =>
        comboBox.SelectedItem is ComboBoxItem { Tag: not null } item && int.TryParse(item.Tag.ToString(), out var value) ? value : fallback;

    private HotKeyPreset ReadHotKeyPreset() =>
        HotKeyPresetComboBox.SelectedItem is ComboBoxItem { Tag: not null } item && Enum.TryParse<HotKeyPreset>(item.Tag.ToString(), out var preset) ? preset : HotKeyPreset.ControlAlt;

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

    private void About_Click(object sender, RoutedEventArgs e) =>
        new AboutWindow(_data.Settings) { Owner = this }.ShowDialog();

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
            _committed = true;
            if (Owner is not null) WindowAppearance.Apply(Owner, _data.Settings);
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
