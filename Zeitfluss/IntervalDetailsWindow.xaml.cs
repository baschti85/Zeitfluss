using System.Globalization;
using System.Windows;
using Zeitfluss.Models;
using Zeitfluss.Services;

namespace Zeitfluss;

public partial class IntervalDetailsWindow : Window
{
    public IntervalDetailsWindow(AppData data, PeriodSummary period)
    {
        InitializeComponent();
        var now = DateTime.Now;
        var details = TimeCalculator.DetailsForPeriod(data, period.Start, period.End, now);
        var culture = CultureInfo.GetCultureInfo("de-DE");
        TitleText.Text = $"Details · {period.Label}";
        SubtitleText.Text = $"{period.Start:dd.MM.yyyy} bis {period.End:dd.MM.yyyy} · {details.Count} Erfassung{(details.Count == 1 ? string.Empty : "en")}";
        ActualText.Text = TimeCalculator.FormatDuration(TimeSpan.FromTicks(details.Sum(x => x.Duration.Ticks)));
        DetailsList.ItemsSource = details.Select(detail => new DetailRow(
            detail.Date.ToDateTime(TimeOnly.MinValue).ToString("ddd, dd.MM.yyyy", culture),
            detail.Start.ToString("HH:mm"),
            detail.End is null ? "offen" : detail.End.Value.ToString("HH:mm"),
            TimeCalculator.FormatDuration(detail.Duration),
            detail.RoundingHint)).ToList();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private sealed record DetailRow(string Date, string Start, string End, string Duration, string Hint);
}
