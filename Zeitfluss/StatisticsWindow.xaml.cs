using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Zeitfluss.Models;
using Zeitfluss.Services;

namespace Zeitfluss;

public partial class StatisticsWindow : Window
{
    private readonly AppData _data;
    private PeriodKind _kind = PeriodKind.Week;

    public StatisticsWindow(AppData data)
    {
        InitializeComponent();
        _data = data;
        Refresh();
    }

    private void Refresh()
    {
        var today = DateOnly.FromDateTime(DateTime.Now); var now = DateTime.Now;
        var days = TimeCalculator.Daily(_data, today, now); var last = days.LastOrDefault();
        TotalActualText.Text = TimeCalculator.FormatDuration(TimeSpan.FromTicks(days.Sum(x => x.Actual.Ticks)));
        TotalTargetText.Text = TimeCalculator.FormatDuration(TimeSpan.FromTicks(days.Sum(x => x.Target.Ticks)));
        TotalBalanceText.Text = last is null || last.Cumulative == TimeSpan.Zero ? "±00:00" : TimeCalculator.FormatDuration(last.Cumulative, true);
        TotalBalanceText.Foreground = last?.Cumulative < TimeSpan.Zero ? (Brush)FindResource("Negative") : last?.Cumulative > TimeSpan.Zero ? (Brush)FindResource("Positive") : (Brush)FindResource("Ink");
        PeriodList.ItemsSource = TimeCalculator.Periods(_data, today, now, _kind).Select(x => new PeriodRow(x, x.Label, TimeCalculator.FormatDuration(x.Target), TimeCalculator.FormatDuration(x.Actual), TimeCalculator.FormatDuration(x.Balance, true), TimeCalculator.FormatDuration(x.Cumulative, true))).ToList();
        ApplyTabStyle(DayButton, _kind == PeriodKind.Day); ApplyTabStyle(WeekButton, _kind == PeriodKind.Week); ApplyTabStyle(MonthButton, _kind == PeriodKind.Month); ApplyTabStyle(YearButton, _kind == PeriodKind.Year);
    }

    private void ApplyTabStyle(Button button, bool active) => button.Style = (Style)FindResource(active ? "SecondaryButton" : "GhostButton");
    private void Day_Click(object sender, RoutedEventArgs e) { _kind = PeriodKind.Day; Refresh(); }
    private void Week_Click(object sender, RoutedEventArgs e) { _kind = PeriodKind.Week; Refresh(); }
    private void Month_Click(object sender, RoutedEventArgs e) { _kind = PeriodKind.Month; Refresh(); }
    private void Year_Click(object sender, RoutedEventArgs e) { _kind = PeriodKind.Year; Refresh(); }
    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not PeriodRow row) return;
        new IntervalDetailsWindow(_data, row.Summary) { Owner = this }.ShowDialog();
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Arbeitszeiten exportieren", Filter = "CSV-Datei (*.csv)|*.csv", FileName = $"Arbeitszeiten-{DateTime.Now:yyyy-MM-dd}.csv", DefaultExt = ".csv" };
        if (dialog.ShowDialog(this) != true) return;
        try { CsvExporter.Export(dialog.FileName, _data, DateOnly.FromDateTime(DateTime.Now), DateTime.Now); ExportStatusText.Text = $"Export gespeichert: {dialog.FileName}"; }
        catch (Exception ex) { MessageBox.Show($"Der Export konnte nicht gespeichert werden.\n\n{ex.Message}", "Zeitfluss", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private sealed record PeriodRow(PeriodSummary Summary, string Label, string Target, string Actual, string Balance, string Cumulative);
}
