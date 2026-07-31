using Microsoft.Win32;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Zeitfluss.Models;
using Zeitfluss.Services;

namespace Zeitfluss;

public partial class StatisticsWindow : Window
{
    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");

    private readonly AppData _data;
    private readonly Action? _onDataChanged;
    private readonly Dictionary<PeriodKind, double> _overviewOffsets = [];
    private PeriodKind _kind = PeriodKind.Week;
    private IReadOnlyList<DailySummary> _dailyRows = [];
    private IReadOnlyList<BalanceChartPoint> _chartRows = [];
    private bool _restoringOverviewScroll;

    public StatisticsWindow(AppData data, Action? onDataChanged = null)
    {
        InitializeComponent();
        _data = data;
        WindowAppearance.Apply(this, _data.Settings);
        _onDataChanged = onDataChanged;
        Refresh();
    }

    private void Refresh()
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        _dailyRows = TimeCalculator.Daily(_data, today, now);
        var last = _dailyRows.LastOrDefault();

        TotalActualText.Text = TimeCalculator.FormatDuration(TimeSpan.FromTicks(_dailyRows.Sum(x => x.Actual.Ticks)));
        TotalTargetText.Text = TimeCalculator.FormatDuration(TimeSpan.FromTicks(_dailyRows.Sum(x => x.Target.Ticks)));
        TotalBalanceText.Text = last is null || last.Cumulative == TimeSpan.Zero
            ? "±00:00"
            : TimeCalculator.FormatDuration(last.Cumulative, true);
        TotalBalanceText.Foreground = BalanceBrush(last?.Cumulative ?? TimeSpan.Zero);

        var periods = TimeCalculator.PeriodsFromDays(_dailyRows, _kind);
        PeriodList.ItemsSource = periods
            .Select(x => new PeriodRow(
                x,
                x.Label,
                TimeCalculator.FormatDuration(x.Target),
                TimeCalculator.FormatDuration(x.Actual),
                TimeCalculator.FormatDuration(x.Balance, true),
                TimeCalculator.FormatDuration(x.Cumulative, true)))
            .ToList();

        ApplyTabStyle(DayButton, _kind == PeriodKind.Day);
        ApplyTabStyle(WeekButton, _kind == PeriodKind.Week);
        ApplyTabStyle(MonthButton, _kind == PeriodKind.Month);
        ApplyTabStyle(YearButton, _kind == PeriodKind.Year);

        BuildPeriodOverview(periods);
        _chartRows = periods
            .OrderBy(period => period.Start)
            .Select(period => new BalanceChartPoint(period.Start, period.End, period.Cumulative, ChartAxisLabel(period)))
            .ToList();
        UpdateChartHeader();
        RenderBalanceChart();
    }

    private void BuildPeriodOverview(IReadOnlyList<PeriodSummary> periods)
    {
        PeriodOverviewHost.Children.Clear();
        var ordered = periods.OrderBy(period => period.Start).ToList();
        var (title, subtitle, unit) = OverviewCopy(_kind);
        OverviewTitleText.Text = title;
        OverviewSubtitleText.Text = subtitle;
        OverviewRangeText.Text = OverviewRangeLabel(ordered, unit);
        BalanceChartTitleText.Text = _kind switch
        {
            PeriodKind.Day => "Saldo nach Tagen",
            PeriodKind.Week => "Saldo nach Wochen",
            PeriodKind.Month => "Saldo nach Monaten",
            _ => "Saldo nach Jahren"
        };

        if (ordered.Count == 0)
        {
            PeriodOverviewHost.Children.Add(new TextBlock
            {
                Text = "Noch keine Zeitdaten vorhanden.",
                Foreground = (Brush)FindResource("MutedInk"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 55, 0, 0)
            });
            LatestOverviewButton.IsEnabled = false;
            return;
        }

        var content = new StackPanel();
        if (_kind == PeriodKind.Day)
        {
            var weekdayHeader = new UniformGrid { Columns = 7, Margin = new Thickness(3, 0, 3, 3) };
            foreach (var label in new[] { "MO", "DI", "MI", "DO", "FR", "SA", "SO" })
            {
                weekdayHeader.Children.Add(new TextBlock
                {
                    Text = label,
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("MutedInk"),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }
            content.Children.Add(weekdayHeader);
        }

        var columns = _kind switch
        {
            PeriodKind.Day => 7,
            _ => 4
        };
        var periodGrid = new UniformGrid { Columns = columns };
        if (_kind == PeriodKind.Day)
        {
            var leadingDays = ((int)ordered[0].Start.DayOfWeek + 6) % 7;
            for (var index = 0; index < leadingDays; index++)
                periodGrid.Children.Add(CreateOverviewPlaceholder());
        }

        foreach (var period in ordered)
            periodGrid.Children.Add(CreateOverviewButton(period));

        while (_kind == PeriodKind.Day && periodGrid.Children.Count % 7 != 0)
            periodGrid.Children.Add(CreateOverviewPlaceholder());

        content.Children.Add(periodGrid);
        PeriodOverviewHost.Children.Add(content);
        AutomationProperties.SetName(content, $"{title}, {ordered.Count} {unit}");
        _restoringOverviewScroll = true;
        var hasSavedOffset = _overviewOffsets.TryGetValue(_kind, out var savedOffset);
        Dispatcher.BeginInvoke(() =>
        {
            if (hasSavedOffset) PeriodOverviewScroll.ScrollToVerticalOffset(savedOffset);
            else PeriodOverviewScroll.ScrollToEnd();
            _restoringOverviewScroll = false;
            UpdateLatestOverviewState();
        }, DispatcherPriority.Loaded);
    }

    private Button CreateOverviewButton(PeriodSummary period)
    {
        var title = OverviewCellLabel(period);
        var balance = period.Balance == TimeSpan.Zero ? "±00:00" : TimeCalculator.FormatDuration(period.Balance, true);
        var description = $"{period.Label}, Ist {TimeCalculator.FormatDuration(period.Actual)}, " +
                          $"Soll {TimeCalculator.FormatDuration(period.Target)}, Differenz {balance}";
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = _kind == PeriodKind.Day ? 10 : 11,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        stack.Children.Add(new TextBlock
        {
            Text = balance,
            FontSize = 9,
            Opacity = 0.78,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        });

        var button = new Button
        {
            Style = (Style)FindResource("PeriodOverviewButton"),
            Height = _kind == PeriodKind.Year ? 55 : 49,
            Content = stack,
            Tag = period,
            ToolTip = description,
            Background = PeriodHeatBrush(period)
        };
        if (HasDarkPeriodFill(period)) button.Foreground = Brushes.White;
        AutomationProperties.SetName(button, description);
        AutomationProperties.SetHelpText(button, "Öffnet die einzelnen Zeiterfassungen dieses Zeitraums.");
        button.Click += OverviewPeriod_Click;
        return button;
    }

    private static Border CreateOverviewPlaceholder() => new()
    {
        Height = 49,
        Margin = new Thickness(3),
        Background = Brushes.Transparent,
        IsHitTestVisible = false
    };

    private static (string Title, string Subtitle, string Unit) OverviewCopy(PeriodKind kind) => kind switch
    {
        PeriodKind.Day => ("Tagesverlauf", "Gesamte Tageshistorie · scrollbar", "Tage"),
        PeriodKind.Week => ("Wochenverlauf", "Gesamte Wochenhistorie · scrollbar", "Wochen"),
        PeriodKind.Month => ("Monatsverlauf", "Gesamte Monatshistorie · scrollbar", "Monate"),
        _ => ("Jahresverlauf", "Alle erfassten Jahre", "Jahre")
    };

    private string OverviewRangeLabel(IReadOnlyList<PeriodSummary> ordered, string unit)
    {
        if (ordered.Count == 0) return $"0 {unit}";
        return _kind switch
        {
            PeriodKind.Day => $"{ordered[0].Start:dd.MM.yy}–{ordered[^1].End:dd.MM.yy} · {ordered.Count}",
            PeriodKind.Week => $"KW {ISOWeek.GetWeekOfYear(ordered[0].Start.ToDateTime(TimeOnly.MinValue)):00}–" +
                               $"{ISOWeek.GetWeekOfYear(ordered[^1].Start.ToDateTime(TimeOnly.MinValue)):00} · {ordered.Count}",
            PeriodKind.Month => $"{ordered[0].Start:MM.yy}–{ordered[^1].End:MM.yy} · {ordered.Count}",
            _ => ordered.Count == 1 ? ordered[0].Start.Year.ToString(GermanCulture) : $"{ordered[0].Start.Year}–{ordered[^1].End.Year}"
        };
    }

    private string OverviewCellLabel(PeriodSummary period) => _kind switch
    {
        PeriodKind.Day => period.Start.ToDateTime(TimeOnly.MinValue).ToString("ddd · dd.MM.", GermanCulture),
        PeriodKind.Week => period.Label.Replace(" · ", " "),
        PeriodKind.Month => period.Start.ToDateTime(TimeOnly.MinValue).ToString("MMM yyyy", GermanCulture),
        _ => period.Label
    };

    private string ChartAxisLabel(PeriodSummary period) => _kind switch
    {
        PeriodKind.Day => period.Start.ToString("dd.MM."),
        PeriodKind.Week => $"KW {ISOWeek.GetWeekOfYear(period.Start.ToDateTime(TimeOnly.MinValue)):00}",
        PeriodKind.Month => period.Start.ToDateTime(TimeOnly.MinValue).ToString("MMM yy", GermanCulture),
        _ => period.Start.Year.ToString(GermanCulture)
    };

    private static Brush PeriodHeatBrush(PeriodSummary period)
    {
        if (period.Target == TimeSpan.Zero && period.Actual == TimeSpan.Zero)
            return BrushFromHex("#E6E8ED");
        if (Math.Abs(period.Balance.TotalMinutes) < 5)
            return BrushFromHex("#D9E8E1");
        if (period.Balance > TimeSpan.Zero)
        {
            var scaleHours = Math.Max(3d, period.Target.TotalHours * 0.18);
            return InterpolateBrush("#CBE8DC", "#32896B", Math.Clamp(period.Balance.TotalHours / scaleHours, 0d, 1d));
        }

        var targetHours = Math.Max(1d, period.Target.TotalHours);
        return InterpolateBrush("#F5DFE0", "#D16870", Math.Clamp(-period.Balance.TotalHours / targetHours, 0d, 1d));
    }

    private static bool HasDarkPeriodFill(PeriodSummary period)
    {
        if (period.Balance > TimeSpan.Zero)
            return period.Balance.TotalHours / Math.Max(3d, period.Target.TotalHours * 0.18) >= 0.72;
        return period.Balance < TimeSpan.Zero && period.Target > TimeSpan.Zero &&
               -period.Balance.TotalHours / period.Target.TotalHours >= 0.72;
    }

    private void UpdateChartHeader()
    {
        if (_chartRows.Count == 0)
        {
            BalanceChartRangeText.Text = "Noch keine Zeitdaten";
            BalanceChartCurrentText.Text = "±00:00";
            BalanceChartCurrentText.Foreground = (Brush)FindResource("Ink");
            return;
        }

        BalanceChartRangeText.Text = $"{_chartRows[0].Start:dd. MMM yyyy} – {_chartRows[^1].End:dd. MMM yyyy}";
        var current = _chartRows[^1].Cumulative;
        BalanceChartCurrentText.Text = current == TimeSpan.Zero
            ? "±00:00"
            : TimeCalculator.FormatDuration(current, true);
        BalanceChartCurrentText.Foreground = BalanceBrush(current);
        AutomationProperties.SetName(
            BalanceChartCanvas,
            $"{BalanceChartTitleText.Text}, {BalanceChartRangeText.Text}, aktueller Saldo {BalanceChartCurrentText.Text}");
    }

    private void BalanceChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderBalanceChart();

    private void RenderBalanceChart()
    {
        BalanceChartCanvas.Children.Clear();
        if (_chartRows.Count == 0 || BalanceChartCanvas.ActualWidth < 100 || BalanceChartCanvas.ActualHeight < 70)
            return;

        var width = BalanceChartCanvas.ActualWidth;
        var height = BalanceChartCanvas.ActualHeight;
        const double left = 8;
        const double right = 9;
        const double top = 10;
        const double bottom = 22;
        var chartWidth = width - left - right;
        var chartHeight = height - top - bottom;

        var values = _chartRows.Select(x => x.Cumulative.TotalHours).ToArray();
        var minimum = Math.Min(0, values.Min());
        var maximum = Math.Max(0, values.Max());
        if (Math.Abs(maximum - minimum) < 0.5)
        {
            maximum += 0.25;
            minimum -= 0.25;
        }
        else
        {
            var padding = (maximum - minimum) * 0.12;
            maximum += padding;
            minimum -= padding;
        }

        double X(int index) => left + (values.Length == 1 ? chartWidth / 2 : chartWidth * index / (values.Length - 1));
        double Y(double value) => top + (maximum - value) / (maximum - minimum) * chartHeight;

        var points = new PointCollection(values.Select((value, index) => new Point(X(index), Y(value))));
        var zeroY = Y(0);
        BalanceChartCanvas.Children.Add(new Line
        {
            X1 = left,
            X2 = width - right,
            Y1 = zeroY,
            Y2 = zeroY,
            Stroke = BrushFromHex("#D7DAE2"),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 4 }
        });

        var current = _chartRows[^1].Cumulative;
        var lineBrush = current < TimeSpan.Zero
            ? BrushFromHex("#C85D67")
            : current > TimeSpan.Zero
                ? BrushFromHex("#238064")
                : BrushFromHex("#667080");

        BalanceChartCanvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = CreateAreaGeometry(points, zeroY),
            Fill = new LinearGradientBrush(
                Color.FromArgb(68, ((SolidColorBrush)lineBrush).Color.R, ((SolidColorBrush)lineBrush).Color.G, ((SolidColorBrush)lineBrush).Color.B),
                Color.FromArgb(0, ((SolidColorBrush)lineBrush).Color.R, ((SolidColorBrush)lineBrush).Color.G, ((SolidColorBrush)lineBrush).Color.B),
                new Point(0.5, 0),
                new Point(0.5, 1))
        });
        BalanceChartCanvas.Children.Add(new Polyline
        {
            Points = points,
            Stroke = lineBrush,
            StrokeThickness = 2.25,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });

        var lastPoint = points[^1];
        var marker = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = Brushes.White,
            Stroke = lineBrush,
            StrokeThickness = 2,
            ToolTip = $"Aktueller Saldo {BalanceChartCurrentText.Text}"
        };
        AutomationProperties.SetName(marker, $"Aktueller Saldo {BalanceChartCurrentText.Text}");
        Canvas.SetLeft(marker, lastPoint.X - 4);
        Canvas.SetTop(marker, lastPoint.Y - 4);
        BalanceChartCanvas.Children.Add(marker);

        AddChartLabel("0", left, Math.Clamp(zeroY - 15, 0, height - 18));
        AddChartLabel(_chartRows[0].AxisLabel, left, height - 16);
        var endLabel = AddChartLabel(_chartRows[^1].AxisLabel, 0, height - 16);
        endLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(endLabel, Math.Max(left, width - right - endLabel.DesiredSize.Width));
    }

    private TextBlock AddChartLabel(string text, double x, double y)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = 9,
            Foreground = (Brush)FindResource("MutedInk"),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        BalanceChartCanvas.Children.Add(label);
        return label;
    }

    private static Geometry CreateAreaGeometry(PointCollection points, double baseline)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(new Point(points[0].X, baseline), true, true);
        context.LineTo(points[0], true, false);
        for (var index = 1; index < points.Count; index++)
            context.LineTo(points[index], true, false);
        context.LineTo(new Point(points[^1].X, baseline), true, false);
        geometry.Freeze();
        return geometry;
    }

    private Brush BalanceBrush(TimeSpan balance) => balance < TimeSpan.Zero
        ? (Brush)FindResource("Negative")
        : balance > TimeSpan.Zero
            ? (Brush)FindResource("Positive")
            : (Brush)FindResource("Ink");

    private static SolidColorBrush BrushFromHex(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush InterpolateBrush(string fromHex, string toHex, double amount)
    {
        var from = (Color)ColorConverter.ConvertFromString(fromHex);
        var to = (Color)ColorConverter.ConvertFromString(toHex);
        byte Mix(byte start, byte end) => (byte)Math.Round(start + (end - start) * amount);
        var brush = new SolidColorBrush(Color.FromRgb(Mix(from.R, to.R), Mix(from.G, to.G), Mix(from.B, to.B)));
        brush.Freeze();
        return brush;
    }

    private void ApplyTabStyle(Button button, bool active)
    {
        button.Style = (Style)FindResource(active ? "SecondaryButton" : "GhostButton");
        AutomationProperties.SetHelpText(button, active ? "Ausgewählt" : "Nicht ausgewählt");
    }

    private void SelectKind(PeriodKind kind)
    {
        if (_kind == kind) return;
        if (!_restoringOverviewScroll) _overviewOffsets[_kind] = PeriodOverviewScroll.VerticalOffset;
        _kind = kind;
        Refresh();
    }

    private void Day_Click(object sender, RoutedEventArgs e) => SelectKind(PeriodKind.Day);
    private void Week_Click(object sender, RoutedEventArgs e) => SelectKind(PeriodKind.Week);
    private void Month_Click(object sender, RoutedEventArgs e) => SelectKind(PeriodKind.Month);
    private void Year_Click(object sender, RoutedEventArgs e) => SelectKind(PeriodKind.Year);

    private void OverviewPeriod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PeriodSummary period }) ShowDetails(period);
    }

    private void LatestOverview_Click(object sender, RoutedEventArgs e) => PeriodOverviewScroll.ScrollToEnd();

    private void PeriodOverviewScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!_restoringOverviewScroll) _overviewOffsets[_kind] = PeriodOverviewScroll.VerticalOffset;
        UpdateLatestOverviewState();
    }

    private void UpdateLatestOverviewState() =>
        LatestOverviewButton.IsEnabled = PeriodOverviewScroll.ScrollableHeight > 1 &&
                                         PeriodOverviewScroll.VerticalOffset < PeriodOverviewScroll.ScrollableHeight - 1;

    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not PeriodRow row) return;
        ShowDetails(row.Summary);
    }

    private void ShowDetails(PeriodSummary summary)
    {
        new IntervalDetailsWindow(_data, summary, _onDataChanged) { Owner = this }.ShowDialog();
        Refresh();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Arbeitszeiten exportieren",
            Filter = "CSV-Datei (*.csv)|*.csv",
            FileName = $"Arbeitszeiten-{DateTime.Now:yyyy-MM-dd}.csv",
            DefaultExt = ".csv"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            CsvExporter.Export(dialog.FileName, _data, DateOnly.FromDateTime(DateTime.Now), DateTime.Now);
            ExportStatusText.Text = $"Export gespeichert: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Der Export konnte nicht gespeichert werden.\n\n{ex.Message}",
                "Zeitfluss",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private sealed record PeriodRow(
        PeriodSummary Summary,
        string Label,
        string Target,
        string Actual,
        string Balance,
        string Cumulative);

    private sealed record BalanceChartPoint(
        DateOnly Start,
        DateOnly End,
        TimeSpan Cumulative,
        string AxisLabel);
}
