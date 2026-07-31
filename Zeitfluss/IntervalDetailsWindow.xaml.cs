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
    private DeletedIntervalState? _pendingUndo;

    public IntervalDetailsWindow(AppData data, PeriodSummary period, Action? onDataChanged = null)
    {
        InitializeComponent();
        _data = data;
        WindowAppearance.Apply(this, _data.Settings);
        _period = period;
        _onDataChanged = onDataChanged;
        Refresh();
    }

    private void Refresh()
    {
        var now = DateTime.Now;
        var details = TimeCalculator.DetailsForPeriod(_data, _period.Start, _period.End, now);
        var rows = details.Select(detail => CreateDetailRow(detail, now)).ToList();
        var culture = CultureInfo.GetCultureInfo("de-DE");

        TitleText.Text = $"Details · {_period.Label}";
        SubtitleText.Text = $"{_period.Start:dd.MM.yyyy} bis {_period.End:dd.MM.yyyy} · {details.Count} Erfassung{(details.Count == 1 ? string.Empty : "en")}";
        RawActualText.Text = TimeCalculator.FormatDuration(TimeSpan.FromTicks(rows.Sum(row => row.RawDuration.Ticks)));
        ActualText.Text = TimeCalculator.FormatDuration(TimeSpan.FromTicks(rows.Sum(row => row.CreditedDuration.Ticks)));

        var dayRows = rows
            .GroupBy(row => row.Date)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var intervals = group.OrderBy(row => row.RawStartMinute).ToList();
                var rawTotal = TimeSpan.FromTicks(intervals.Sum(row => row.RawDuration.Ticks));
                var creditedTotal = TimeSpan.FromTicks(intervals.Sum(row => row.CreditedDuration.Ticks));
                return new DayTimelineRow(
                    group.Key.ToDateTime(TimeOnly.MinValue).ToString("dddd, dd. MMMM yyyy", culture),
                    intervals.Count == 1 ? "1 ERFASSUNG" : $"{intervals.Count} ERFASSUNGEN",
                    $"Roh {TimeCalculator.FormatDuration(rawTotal)}",
                    $"Angerechnet {TimeCalculator.FormatDuration(creditedTotal)}",
                    intervals);
            })
            .ToList();
        DetailsList.ItemsSource = dayRows;
        EmptyState.Visibility = dayRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private DetailRow CreateDetailRow(IntervalDetail detail, DateTime now)
    {
        var interval = _data.Intervals.First(item => item.Id == detail.IntervalId);
        var dayStart = detail.Date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        var rawStart = interval.StartedAt > dayStart ? interval.StartedAt : dayStart;
        var intervalRawEnd = interval.EndedAt ?? now;
        var rawEnd = intervalRawEnd < dayEnd ? intervalRawEnd : dayEnd;
        if (rawEnd < rawStart) rawEnd = rawStart;

        var creditedStart = detail.Start;
        var creditedEnd = creditedStart + detail.Duration;
        if (creditedEnd > dayEnd) creditedEnd = dayEnd;

        var rawDuration = rawEnd - rawStart;
        var isOpenToday = interval.EndedAt is null && detail.Date == DateOnly.FromDateTime(now);
        var rawEndLabel = ClockLabel(rawEnd, dayEnd, isOpenToday);
        var creditedEndLabel = detail.End is null ? "offen" : ClockLabel(creditedEnd, dayEnd, false);
        var creditedRange = $"{creditedStart:HH:mm}–{creditedEndLabel}";
        var rawRange = $"{rawStart:HH:mm}–{rawEndLabel}";
        var roundingLabel = interval.UsesFiveMinuteRounding ? "5-Min.-Rundung" : "ohne Rundung";

        return new DetailRow(
            detail.IntervalId,
            detail.Date,
            MinutesSinceMidnight(rawStart, dayStart),
            MinutesSinceMidnight(rawEnd, dayStart),
            MinutesSinceMidnight(creditedStart, dayStart),
            MinutesSinceMidnight(creditedEnd, dayStart),
            rawDuration,
            detail.Duration,
            $"Angerechnet {creditedRange}",
            $"Roh {rawRange} · {TimeCalculator.FormatDuration(rawDuration)}",
            TimeCalculator.FormatDuration(detail.Duration),
            roundingLabel,
            $"Roh {rawRange} ({TimeCalculator.FormatDuration(rawDuration)})\nAngerechnet {creditedRange} ({TimeCalculator.FormatDuration(detail.Duration)})");
    }

    private static string ClockLabel(DateTime value, DateTime dayEnd, bool isOpen) =>
        isOpen ? "offen" : value == dayEnd ? "24:00" : value.ToString("HH:mm");

    private static double MinutesSinceMidnight(DateTime value, DateTime dayStart) =>
        Math.Clamp((value - dayStart).TotalMinutes, 0d, 1440d);

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

        var spansMultipleDays = interval.EndedAt is not null && DateOnly.FromDateTime(interval.StartedAt) != DateOnly.FromDateTime(interval.EndedAt.Value);
        var question = interval.EndedAt is null
            ? "Diese Erfassung läuft noch. Möchtest du sie wirklich vollständig löschen?"
            : spansMultipleDays
                ? $"Diese Erfassung vom {interval.StartedAt:dd.MM.yyyy} umfasst mehrere Tage. Möchtest du sie mit allen Tagesabschnitten löschen?"
                : $"Möchtest du die Erfassung vom {interval.StartedAt:dd.MM.yyyy} wirklich löschen?";
        if (MessageBox.Show(this, question, "Erfassung löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var previousDate = DateOnly.FromDateTime(interval.StartedAt);
        _pendingUndo = new DeletedIntervalState(
            interval,
            _data.Intervals.IndexOf(interval),
            previousDate,
            _data.FinishedDays.Contains(previousDate));
        _data.Intervals.Remove(interval);
        RemoveOrphanedFinishedDay(previousDate);
        _onDataChanged?.Invoke();
        Refresh();
        UndoMessageText.Text = $"Erfassung vom {interval.StartedAt:dd.MM.yyyy}, {interval.StartedAt:HH:mm} Uhr gelöscht";
        UndoPanel.Visibility = Visibility.Visible;
    }

    private void UndoDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUndo is not { } state)
        {
            UndoPanel.Visibility = Visibility.Collapsed;
            return;
        }

        if (_data.Intervals.All(interval => interval.Id != state.Interval.Id))
        {
            _data.Intervals.Insert(Math.Clamp(state.OriginalIndex, 0, _data.Intervals.Count), state.Interval);
            if (state.WasFinishedDay) _data.FinishedDays.Add(state.StartDate);
            _onDataChanged?.Invoke();
        }

        _pendingUndo = null;
        UndoPanel.Visibility = Visibility.Collapsed;
        Refresh();
    }

    private void RemoveOrphanedFinishedDay(DateOnly date)
    {
        if (!_data.Intervals.Any(interval => DateOnly.FromDateTime(interval.StartedAt) == date))
            _data.FinishedDays.Remove(date);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record DayTimelineRow(
        string Date,
        string CountLabel,
        string RawTotalLabel,
        string CreditedTotalLabel,
        IReadOnlyList<DetailRow> Intervals);

    private sealed record DetailRow(
        Guid IntervalId,
        DateOnly Date,
        double RawStartMinute,
        double RawEndMinute,
        double CreditedStartMinute,
        double CreditedEndMinute,
        TimeSpan RawDuration,
        TimeSpan CreditedDuration,
        string CreditedRangeLabel,
        string RawRangeLabel,
        string CreditedDurationLabel,
        string RoundingLabel,
        string TimelineToolTip);

    private sealed record DeletedIntervalState(
        WorkInterval Interval,
        int OriginalIndex,
        DateOnly StartDate,
        bool WasFinishedDay);
}
