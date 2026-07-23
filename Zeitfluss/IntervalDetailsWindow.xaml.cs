using System.Globalization;
using System.Windows;
using Zeitfluss.Models;
using Zeitfluss.Services;

namespace Zeitfluss;

public partial class IntervalDetailsWindow : Window
{
    private readonly AppData _data;
    private readonly PeriodSummary _period;
    private readonly Action? _onDataChanged;

    public IntervalDetailsWindow(AppData data, PeriodSummary period, Action? onDataChanged = null)
    {
        InitializeComponent();
        _data = data;
        _period = period;
        _onDataChanged = onDataChanged;
        Refresh();
    }

    private void Refresh()
    {
        var now = DateTime.Now;
        var details = TimeCalculator.DetailsForPeriod(_data, _period.Start, _period.End, now);
        var culture = CultureInfo.GetCultureInfo("de-DE");
        TitleText.Text = $"Details · {_period.Label}";
        SubtitleText.Text = $"{_period.Start:dd.MM.yyyy} bis {_period.End:dd.MM.yyyy} · {details.Count} Erfassung{(details.Count == 1 ? string.Empty : "en")}";
        ActualText.Text = TimeCalculator.FormatDuration(TimeSpan.FromTicks(details.Sum(x => x.Duration.Ticks)));
        DetailsList.ItemsSource = details.Select(detail => new DetailRow(
            detail.IntervalId,
            detail.Date.ToDateTime(TimeOnly.MinValue).ToString("ddd, dd.MM.yyyy", culture),
            detail.Start.ToString("HH:mm"),
            detail.End is null ? "offen" : detail.End.Value.ToString("HH:mm"),
            TimeCalculator.FormatDuration(detail.Duration),
            detail.RoundingHint)).ToList();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DetailRow row) return;
        var interval = _data.Intervals.FirstOrDefault(item => item.Id == row.IntervalId);
        if (interval is null) { Refresh(); return; }
        var previousDate = DateOnly.FromDateTime(interval.StartedAt);
        if (new EditIntervalWindow(_data, interval) { Owner = this }.ShowDialog() != true) return;
        RemoveOrphanedFinishedDay(previousDate);
        if (interval.EndedAt is null) _data.FinishedDays.Remove(DateOnly.FromDateTime(interval.StartedAt));
        _onDataChanged?.Invoke();
        Refresh();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DetailRow row) return;
        var interval = _data.Intervals.FirstOrDefault(item => item.Id == row.IntervalId);
        if (interval is null) { Refresh(); return; }
        var question = interval.EndedAt is null
            ? "Diese Erfassung läuft noch. Möchtest du sie wirklich vollständig löschen?"
            : $"Möchtest du die Erfassung vom {interval.StartedAt:dd.MM.yyyy} wirklich löschen?";
        if (MessageBox.Show(this, question, "Erfassung löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var previousDate = DateOnly.FromDateTime(interval.StartedAt);
        _data.Intervals.Remove(interval);
        RemoveOrphanedFinishedDay(previousDate);
        _onDataChanged?.Invoke();
        Refresh();
    }

    private void RemoveOrphanedFinishedDay(DateOnly date)
    {
        if (!_data.Intervals.Any(interval => DateOnly.FromDateTime(interval.StartedAt) == date))
            _data.FinishedDays.Remove(date);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private sealed record DetailRow(Guid IntervalId, string Date, string Start, string End, string Duration, string Hint);
}
