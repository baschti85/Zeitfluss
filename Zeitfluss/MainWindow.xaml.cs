using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Zeitfluss.Models;
using Zeitfluss.Services;

namespace Zeitfluss;

public partial class MainWindow : Window
{
    private const double FullWidth = 342;
    private const double FullHeight = 408;
    private const double CompactWidth = 146;
    private const double CompactHeight = 48;
    private readonly DataStore _store;
    private readonly bool _persistChanges;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private AppData _data;
    private bool _isCompact;
    private bool _compactWasDragged;
    private Point _compactPointerStart;
    private Point _compactWindowStart;
    private double _normalLeft;
    private double _normalTop;

    public MainWindow() : this(null, true) { }

    public MainWindow(AppData? initialData, bool persistChanges = true)
    {
        InitializeComponent();
        _store = new DataStore();
        _persistChanges = persistChanges;
        if (initialData is not null) _data = initialData;
        else
        {
            try { _data = _store.Load(); }
            catch (InvalidDataException ex) { MessageBox.Show(ex.Message, "Zeitfluss", MessageBoxButton.OK, MessageBoxImage.Warning); _data = new AppData(); }
        }
        Loaded += OnLoaded;
        Closing += (_, _) => SaveWindowAndData();
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Topmost = _data.Settings.AlwaysOnTop;
        PinButton.Opacity = Topmost ? 1 : 0.45;
        if (_data.Settings.WindowLeft is double left && _data.Settings.WindowTop is double top && IsPositionVisible(left, top)) { Left = left; Top = top; }
        else { var area = SystemParameters.WorkArea; Left = area.Right - ActualWidth - 18; Top = area.Top + 18; }
        _normalLeft = Left;
        _normalTop = Top;
        Refresh();
    }

    private WorkInterval? ActiveInterval => _data.Intervals.FirstOrDefault(x => x.EndedAt is null);

    private void Refresh()
    {
        var now = DateTime.Now; var today = DateOnly.FromDateTime(now); var active = ActiveInterval;
        var actual = TimeCalculator.ActualForDay(_data, today, now); var target = TimeCalculator.TargetForDay(_data, today);
        var balance = TimeCalculator.Daily(_data, today, now).LastOrDefault()?.Cumulative ?? TimeSpan.Zero;
        DateText.Text = now.ToString("dddd, d. MMMM", CultureInfo.GetCultureInfo("de-DE"));
        var current = active is null ? actual : TimeCalculator.RecordedDuration(active, now);
        ElapsedText.Text = active is null ? TimeCalculator.FormatDuration(actual) : $"{(int)current.TotalHours:00}:{current.Minutes:00}:{current.Seconds:00}";
        TodayActualText.Text = TimeCalculator.FormatDuration(actual); TodayTargetText.Text = TimeCalculator.FormatDuration(target);
        CompactTimeText.Text = TimeCalculator.FormatDuration(actual);
        CompactStatusDot.Fill = (Brush)FindResource(active is not null ? "Positive" : "MutedInk");
        BalanceText.Text = balance == TimeSpan.Zero ? "±00:00" : TimeCalculator.FormatDuration(balance, true);
        BalanceText.Foreground = balance < TimeSpan.Zero ? (Brush)FindResource("Negative") : balance > TimeSpan.Zero ? (Brush)FindResource("Positive") : (Brush)FindResource("Ink");
        var hasToday = _data.Intervals.Any(x => DateOnly.FromDateTime(x.StartedAt) == today); var finished = _data.FinishedDays.Contains(today);
        StatusText.Text = active is not null ? $"Läuft seit {active.StartedAt:HH:mm} Uhr{(active.UsesFiveMinuteRounding ? " · 5-Min.-Rhythmus" : string.Empty)}" : finished ? "Feierabend" : hasToday ? "Pausiert" : "Bereit für den Tag";
        StartButton.Content = active is not null ? "Arbeitszeit läuft" : finished ? "Nochmals beginnen" : hasToday ? "Arbeit fortsetzen" : "Arbeit beginnen";
        StartButton.IsEnabled = active is null; PauseButton.IsEnabled = active is not null; EndButton.IsEnabled = active is not null || hasToday && !finished;
        var week = TimeCalculator.Periods(_data, today, now, PeriodKind.Week).FirstOrDefault();
        WeekText.Text = week is null ? $"Woche · 00:00 / {_data.Settings.WeeklyHours:0.##} h" : $"Woche · {TimeCalculator.FormatDuration(week.Actual)} / {_data.Settings.WeeklyHours:0.##} h";
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveInterval is not null) return;
        var now = DateTime.Now;
        var rounded = _data.Settings.UseFiveMinuteRounding;
        _data.FinishedDays.Remove(DateOnly.FromDateTime(now));
        _data.Intervals.Add(new WorkInterval { StartedAt = now, UsesFiveMinuteRounding = rounded, RoundedStartedAt = rounded ? TimeCalculator.RoundUpToFiveMinutes(now) : null });
        SaveAndRefresh();
    }
    private void PauseButton_Click(object sender, RoutedEventArgs e) { CloseActiveInterval(); SaveAndRefresh(); }
    private void EndButton_Click(object sender, RoutedEventArgs e) { CloseActiveInterval(); _data.FinishedDays.Add(DateOnly.FromDateTime(DateTime.Now)); SaveAndRefresh(); }
    private void CloseActiveInterval()
    {
        if (ActiveInterval is not { } active) return;
        var now = DateTime.Now;
        active.EndedAt = now;
        active.RoundedEndedAt = active.UsesFiveMinuteRounding ? TimeCalculator.RoundDownToFiveMinutes(now) : null;
    }
    private void SaveAndRefresh() { if (_persistChanges) _store.Save(_data); Refresh(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    private void PinButton_Click(object sender, RoutedEventArgs e) { Topmost = !Topmost; _data.Settings.AlwaysOnTop = Topmost; PinButton.Opacity = Topmost ? 1 : 0.45; SaveAndRefresh(); }
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => EnterCompactMode();
    private void CompactShell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _compactWasDragged = false;
        _compactPointerStart = GetPointerScreenPosition(e);
        _compactWindowStart = new Point(Left, Top);
        CompactShell.CaptureMouse();
        e.Handled = true;
    }

    private void CompactShell_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var current = GetPointerScreenPosition(e);
        if (!_compactWasDragged)
        {
            if (Math.Abs(current.X - _compactPointerStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - _compactPointerStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _compactWasDragged = true;
        }

        Left = _compactWindowStart.X + current.X - _compactPointerStart.X;
        Top = _compactWindowStart.Y + current.Y - _compactPointerStart.Y;
    }

    private void CompactShell_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        CompactShell.ReleaseMouseCapture();
        if (_compactWasDragged)
        {
            ClampToVirtualScreen();
            _data.Settings.CompactWindowLeft = Left;
            _data.Settings.CompactWindowTop = Top;
            if (_persistChanges) _store.Save(_data);
        }
        else RestoreFullMode();
        e.Handled = true;
    }
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void SettingsButton_Click(object sender, RoutedEventArgs e) { var window = new SettingsWindow(_data) { Owner = this }; if (window.ShowDialog() == true) SaveAndRefresh(); }
    private void StatisticsButton_Click(object sender, RoutedEventArgs e) => new StatisticsWindow(_data) { Owner = this }.ShowDialog();
    private void EnterCompactMode()
    {
        if (_isCompact) return;
        _normalLeft = Left;
        _normalTop = Top;
        _data.Settings.WindowLeft = Left;
        _data.Settings.WindowTop = Top;
        _isCompact = true;
        FullShell.Visibility = Visibility.Collapsed;
        CompactShell.Visibility = Visibility.Visible;
        Width = CompactWidth;
        Height = CompactHeight;
        var area = SystemParameters.WorkArea;
        if (_data.Settings.CompactWindowLeft is double compactLeft && _data.Settings.CompactWindowTop is double compactTop && IsPositionVisible(compactLeft, compactTop, CompactWidth, CompactHeight))
        {
            Left = compactLeft;
            Top = compactTop;
        }
        else
        {
            Left = area.Right - CompactWidth - 12;
            Top = area.Top + 12;
        }
        ShowInTaskbar = false;
        Topmost = true;
        if (_persistChanges) _store.Save(_data);
        Refresh();
    }

    private void RestoreFullMode()
    {
        if (!_isCompact) return;
        _isCompact = false;
        Width = FullWidth;
        Height = FullHeight;
        Left = IsPositionVisible(_normalLeft, _normalTop, FullWidth, FullHeight) ? _normalLeft : SystemParameters.WorkArea.Right - FullWidth - 18;
        Top = IsPositionVisible(_normalLeft, _normalTop, FullWidth, FullHeight) ? _normalTop : SystemParameters.WorkArea.Top + 18;
        CompactShell.Visibility = Visibility.Collapsed;
        FullShell.Visibility = Visibility.Visible;
        ShowInTaskbar = true;
        Topmost = _data.Settings.AlwaysOnTop;
        Activate();
        Refresh();
    }

    private void SaveWindowAndData()
    {
        if (!_persistChanges) return;
        if (!_isCompact && WindowState == WindowState.Normal) { _data.Settings.WindowLeft = Left; _data.Settings.WindowTop = Top; }
        _store.Save(_data);
    }
    private void ClampToVirtualScreen()
    {
        Left = Math.Clamp(Left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - ActualWidth);
        Top = Math.Clamp(Top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - ActualHeight);
    }

    private Point GetPointerScreenPosition(MouseEventArgs e)
    {
        var devicePoint = PointToScreen(e.GetPosition(this));
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformFromDevice.Transform(devicePoint) ?? devicePoint;
    }

    private static bool IsPositionVisible(double left, double top, double width = 20, double height = 20)
    {
        var virtualScreen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        return virtualScreen.Contains(new Point(left + width / 2, top + height / 2));
    }
}
