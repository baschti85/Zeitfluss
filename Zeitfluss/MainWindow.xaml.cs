using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Zeitfluss.Models;
using Zeitfluss.Services;

namespace Zeitfluss;

public partial class MainWindow : Window
{
    private const double FullWidth = 380;
    private const double FullHeight = 520;
    private const double CompactWidth = 276;
    private const double CompactHeight = 58;
    private readonly DataStore _store;
    private readonly bool _persistChanges;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly WorkdayInsightService _insightService = new();
    private readonly TimeRecoveryAdvisor _recoveryAdvisor = new();
    private readonly IIdleTimeProvider _idleTimeProvider = new WindowsIdleTimeProvider();
    private AppData _data;
    private TrayIconService? _trayIcon;
    private GlobalHotKeyService? _hotKeyService;
    private bool _isCompact;
    private bool _compactWasDragged;
    private bool _recoveryDialogOpen;
    private Point _compactPointerStart;
    private Point _compactWindowStart;
    private double _normalLeft;
    private double _normalTop;
    private DateTime? _idleEpisodeStartedAt;
    private UndoSnapshot? _undoSnapshot;

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
        Closed += (_, _) => DisposeDesktopExperience();
        _timer.Tick += (_, _) => Refresh();
        _toastTimer.Tick += (_, _) => HideActionToast();
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
        if (_persistChanges) InitializeDesktopExperience();
        Refresh();
        if (_persistChanges && _data.Settings.EnableForgottenTimerAssistant)
            Dispatcher.BeginInvoke(() => ReviewOpenInterval(null), DispatcherPriority.ContextIdle);
    }

    private WorkInterval? ActiveInterval => _data.Intervals.FirstOrDefault(x => x.EndedAt is null);

    private void Refresh()
    {
        WindowAppearance.Apply(this, _data.Settings);
        var now = DateTime.Now;
        var insight = _insightService.Create(_data, now);
        var active = ActiveInterval;
        DateText.Text = now.ToString("dddd, d. MMMM", CultureInfo.GetCultureInfo("de-DE"));
        TodayActualText.Text = TimeCalculator.FormatDuration(insight.Actual);
        TodayTargetText.Text = TimeCalculator.FormatDuration(insight.Target);
        CompactTimeText.Text = TimeCalculator.FormatDuration(insight.Actual);
        BalanceText.Text = insight.CumulativeBalance == TimeSpan.Zero ? "±00:00" : TimeCalculator.FormatDuration(insight.CumulativeBalance, true);
        BalanceText.Foreground = insight.CumulativeBalance < TimeSpan.Zero ? (Brush)FindResource("Negative") : insight.CumulativeBalance > TimeSpan.Zero ? (Brush)FindResource("Positive") : (Brush)FindResource("Ink");

        ApplyHeroState(insight, now);
        StartButtonText.Text = insight.State switch
        {
            WorkdayState.Running => "Arbeitszeit läuft",
            WorkdayState.Finished => "Nochmals beginnen",
            WorkdayState.Paused => "Arbeit fortsetzen",
            _ => "Arbeit beginnen"
        };
        StartButton.IsEnabled = active is null;
        PauseButton.IsEnabled = active is not null;
        EndButton.IsEnabled = active is not null || insight.State == WorkdayState.Paused;
        var stateBrushKey = insight.State switch { WorkdayState.Running => "Positive", WorkdayState.Paused => "Warning", WorkdayState.Finished => "MutedInk", _ => "Accent" };
        var stateSurfaceKey = insight.State switch { WorkdayState.Running => "PositiveSoft", WorkdayState.Paused => "WarningSoft", _ => "AccentSoft" };
        StatusDot.Fill = (Brush)FindResource(stateBrushKey);
        CompactStatusDot.Fill = (Brush)FindResource(stateBrushKey);
        StatePill.Background = (Brush)FindResource(stateSurfaceKey);
        StatusText.Foreground = (Brush)FindResource(stateBrushKey);
        CompactPauseButton.IsEnabled = active is not null || insight.State == WorkdayState.Paused;
        CompactPauseIcon.Visibility = active is not null ? Visibility.Visible : Visibility.Collapsed;
        CompactResumeIcon.Visibility = insight.State == WorkdayState.Paused ? Visibility.Visible : Visibility.Collapsed;
        var compactPauseLabel = insight.State == WorkdayState.Paused ? "Arbeitszeit fortsetzen" : "Arbeitszeit pausieren";
        CompactPauseButton.ToolTip = insight.State == WorkdayState.Paused ? "Fortsetzen" : "Pause";
        AutomationProperties.SetName(CompactPauseButton, compactPauseLabel);
        CompactEndButton.IsEnabled = active is not null || insight.State == WorkdayState.Paused;

        var week = TimeCalculator.Periods(_data, DateOnly.FromDateTime(now), now, PeriodKind.Week).FirstOrDefault();
        WeekText.Text = week is null ? $"Woche · 00:00 / {_data.Settings.WeeklyHours:0.##} h" : $"Woche · {TimeCalculator.FormatDuration(week.Actual)} / {_data.Settings.WeeklyHours:0.##} h";
        WeekProgress.Maximum = Math.Max(1, _data.Settings.WeeklyHours * 60);
        WeekProgress.Value = Math.Clamp(week?.Actual.TotalMinutes ?? 0, 0, WeekProgress.Maximum);

        _trayIcon?.Update(new TrayIconState(StatusText.Text, insight.Actual, insight.State == WorkdayState.Running, insight.State == WorkdayState.Paused, EndButton.IsEnabled));
        CheckEndOfDayReminder(insight);
        MonitorIdleReturn(now);
    }

    private void ApplyHeroState(WorkdayInsight insight, DateTime now)
    {
        switch (insight.State)
        {
            case WorkdayState.Running:
                ElapsedText.Text = FormatDurationWithSeconds(insight.CurrentSessionElapsed);
                HeroCaptionText.Text = "AKTUELLE SITZUNG";
                StatusText.Text = $"Läuft seit {insight.ActiveSince:HH:mm} Uhr{(ActiveInterval?.UsesFiveMinuteRounding == true ? " · 5-Min.-Rhythmus" : string.Empty)}";
                HeroMetaText.Text = insight.Remaining <= TimeSpan.Zero
                    ? "Tagessoll erreicht"
                    : $"Noch {TimeCalculator.FormatDuration(insight.Remaining)} · bis {insight.ProjectedFinishAt:HH:mm}";
                break;
            case WorkdayState.Paused:
                ElapsedText.Text = TimeCalculator.FormatDuration(insight.Actual);
                HeroCaptionText.Text = "HEUTE ERFASST";
                var lastEnd = _data.Intervals.Where(interval => interval.EndedAt is not null && DateOnly.FromDateTime(interval.EndedAt.Value) == insight.Date).MaxBy(interval => interval.EndedAt)?.EndedAt;
                StatusText.Text = lastEnd is null ? "Pausiert" : $"Pausiert seit {lastEnd:HH:mm} Uhr";
                HeroMetaText.Text = insight.Remaining <= TimeSpan.Zero
                    ? "Tagessoll erreicht"
                    : $"Noch {TimeCalculator.FormatDuration(insight.Remaining)} · bis {insight.FinishIfResumedNow:HH:mm}";
                break;
            case WorkdayState.Finished:
                ElapsedText.Text = TimeCalculator.FormatDuration(insight.Actual);
                HeroCaptionText.Text = "ARBEITSTAG ABGESCHLOSSEN";
                StatusText.Text = "Feierabend";
                HeroMetaText.Text = insight.DayBalance == TimeSpan.Zero ? "Tagessoll genau erreicht" : $"Tagessaldo {TimeCalculator.FormatDuration(insight.DayBalance, true)}";
                break;
            default:
                ElapsedText.Text = "00:00";
                HeroCaptionText.Text = "HEUTE ERFASST";
                StatusText.Text = insight.Target <= TimeSpan.Zero ? "Heute ist kein Soll hinterlegt" : "Bereit für den Tag";
                HeroMetaText.Text = $"Soll {TimeCalculator.FormatDuration(insight.Target)}";
                break;
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e) => RequestStart();
    private void PauseButton_Click(object sender, RoutedEventArgs e) => RequestPauseResume();
    private void EndButton_Click(object sender, RoutedEventArgs e) => RequestStop();
    private void CompactPauseButton_Click(object sender, RoutedEventArgs e) { RequestPauseResume(); e.Handled = true; }
    private void CompactEndButton_Click(object sender, RoutedEventArgs e) { RequestStop(); e.Handled = true; }

    private void RequestStart()
    {
        if (ActiveInterval is not null) return;
        StartNewInterval();
        SaveAndRefresh();
    }

    private void RequestPauseResume()
    {
        if (ActiveInterval is { } active)
        {
            CaptureUndo(active);
            CloseActiveInterval();
            SaveAndRefresh();
            ShowActionToast("Arbeitszeit pausiert", true);
        }
        else
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (_data.Intervals.Any(interval => DateOnly.FromDateTime(interval.StartedAt) == today) && !_data.FinishedDays.Contains(today))
            {
                StartNewInterval();
                SaveAndRefresh();
            }
        }
    }

    private void RequestStop()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var active = ActiveInterval;
        var hasToday = _data.Intervals.Any(interval => DateOnly.FromDateTime(interval.StartedAt) == today);
        if (active is null && !hasToday) return;
        CaptureUndo(active);
        CloseActiveInterval();
        _data.FinishedDays.Add(today);
        SaveAndRefresh();
        ShowActionToast("Arbeitstag beendet", true);
    }

    private void StartNewInterval()
    {
        if (ActiveInterval is not null) return;
        var now = DateTime.Now;
        var rounded = _data.Settings.UseFiveMinuteRounding;
        _data.FinishedDays.Remove(DateOnly.FromDateTime(now));
        _data.Intervals.Add(new WorkInterval { StartedAt = now, UsesFiveMinuteRounding = rounded, RoundedStartedAt = rounded ? TimeCalculator.RoundUpToFiveMinutes(now) : null });
        _idleEpisodeStartedAt = null;
    }

    private void CloseActiveInterval()
    {
        if (ActiveInterval is not { } active) return;
        var now = DateTime.Now;
        active.EndedAt = now;
        active.RoundedEndedAt = active.UsesFiveMinuteRounding ? TimeCalculator.RoundDownToFiveMinutes(now) : null;
        _idleEpisodeStartedAt = null;
    }

    private void ReviewOpenInterval(DateTime? lastUserInputAt)
    {
        if (!_persistChanges || !_data.Settings.EnableForgottenTimerAssistant || _recoveryDialogOpen || ActiveInterval is null || !IsEnabled) return;
        var assessment = _recoveryAdvisor.Assess(_data, DateTime.Now, lastUserInputAt, new RecoveryAdvisorOptions
        {
            LongRunningThreshold = TimeSpan.FromHours(12),
            IdleThreshold = TimeSpan.FromMinutes(_data.Settings.IdleThresholdMinutes)
        });
        if (!assessment.RequiresReview || assessment.Suggestions.Count == 0) return;

        _recoveryDialogOpen = true;
        try
        {
            var window = new TimeRecoveryWindow(_data, assessment) { Owner = this };
            if (window.ShowDialog() != true || window.SelectedSuggestion is not { } suggestion) return;
            var interval = _data.Intervals.FirstOrDefault(item => item.Id == assessment.IntervalId);
            if (interval is null || interval.EndedAt is not null) return;
            CaptureUndo(interval);
            interval.EndedAt = suggestion.EndAt;
            interval.RoundedEndedAt = interval.UsesFiveMinuteRounding ? suggestion.CreditedEndAt : null;
            if (assessment.Signals.HasFlag(RecoverySignalKind.CrossedDayBoundary) || assessment.Signals.HasFlag(RecoverySignalKind.ExcessiveDuration))
                _data.FinishedDays.Add(DateOnly.FromDateTime(interval.StartedAt));
            SaveAndRefresh();
            ShowActionToast($"Erfassung bis {suggestion.EndAt:HH:mm} korrigiert", true);
        }
        finally { _recoveryDialogOpen = false; }
    }

    private void MonitorIdleReturn(DateTime now)
    {
        if (!_persistChanges || !_data.Settings.EnableForgottenTimerAssistant || ActiveInterval is null || _recoveryDialogOpen || !IsEnabled) { _idleEpisodeStartedAt = null; return; }
        try
        {
            var lastInputAt = _idleTimeProvider.GetLastInputAt(now);
            var threshold = TimeSpan.FromMinutes(_data.Settings.IdleThresholdMinutes);
            if (now - lastInputAt >= threshold)
            {
                _idleEpisodeStartedAt ??= lastInputAt;
                return;
            }
            if (_idleEpisodeStartedAt is not { } idleStartedAt) return;
            _idleEpisodeStartedAt = null;
            Dispatcher.BeginInvoke(() => ReviewOpenInterval(idleStartedAt), DispatcherPriority.Background);
        }
        catch (Exception) when (!System.Diagnostics.Debugger.IsAttached)
        {
            _idleEpisodeStartedAt = null;
        }
    }

    private void CheckEndOfDayReminder(WorkdayInsight insight)
    {
        if (!_persistChanges || _trayIcon is null || !_data.Settings.EnableEndOfDayReminder || insight.State != WorkdayState.Running || insight.Target <= TimeSpan.Zero || _data.LastEndReminderOn == insight.Date) return;
        var lead = TimeSpan.FromMinutes(_data.Settings.ReminderLeadMinutes);
        if (insight.Remaining > lead) return;
        var message = insight.Remaining <= TimeSpan.Zero
            ? $"Dein Tagessoll von {TimeCalculator.FormatDuration(insight.Target)} ist erreicht."
            : $"Noch {TimeCalculator.FormatDuration(insight.Remaining)} bis zu deinem Tagessoll.";
        _data.LastEndReminderOn = insight.Date;
        if (_persistChanges) _store.Save(_data);
        _trayIcon.ShowNotification("Zeit für einen guten Abschluss", message);
    }

    private void InitializeDesktopExperience()
    {
        _trayIcon = new TrayIconService();
        _trayIcon.OpenRequested += (_, _) => RunOnUi(OpenFromTray);
        _trayIcon.StartRequested += (_, _) => RunOnUi(RequestStart);
        _trayIcon.PauseResumeRequested += (_, _) => RunOnUi(RequestPauseResume);
        _trayIcon.StopRequested += (_, _) => RunOnUi(RequestStop);
        _trayIcon.ExitRequested += (_, _) => RunOnUi(Close);
        _trayIcon.Show();
        ApplyDesktopPreferences();
    }

    private void ApplyDesktopPreferences()
    {
        if (!_isCompact)
        {
            Topmost = _data.Settings.AlwaysOnTop;
            PinButton.Opacity = Topmost ? 1 : 0.45;
        }
        if (!_persistChanges) return;
        if (!_data.Settings.EnableGlobalHotKeys)
        {
            if (_hotKeyService?.IsRegistered == true) _hotKeyService.Unregister();
            return;
        }

        try
        {
            var bindings = CreateHotKeyBindings(_data.Settings.HotKeyPreset);
            if (_hotKeyService is null)
            {
                _hotKeyService = new GlobalHotKeyService(this, bindings);
                _hotKeyService.StartRequested += (_, _) => RequestStart();
                _hotKeyService.PauseResumeRequested += (_, _) => RequestPauseResume();
                _hotKeyService.StopRequested += (_, _) => RequestStop();
            }
            else _hotKeyService.UpdateBindings(bindings);
            _hotKeyService.Register();
        }
        catch (HotKeyRegistrationException)
        {
            ShowActionToast("Tastenkürzel bereits anderweitig belegt", false);
        }
    }

    private static GlobalHotKeyBindings CreateHotKeyBindings(HotKeyPreset preset)
    {
        var modifiers = preset switch
        {
            HotKeyPreset.ControlShift => GlobalHotKeyModifiers.Control | GlobalHotKeyModifiers.Shift,
            HotKeyPreset.AltShift => GlobalHotKeyModifiers.Alt | GlobalHotKeyModifiers.Shift,
            _ => GlobalHotKeyModifiers.Control | GlobalHotKeyModifiers.Alt
        } | GlobalHotKeyModifiers.NoRepeat;
        return new GlobalHotKeyBindings(new(0x77, modifiers), new(0x78, modifiers), new(0x79, modifiers));
    }

    private void OpenFromTray()
    {
        if (_isCompact) RestoreFullMode();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    private void RunOnUi(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.BeginInvoke(action);
    }

    private void DisposeDesktopExperience()
    {
        _hotKeyService?.Dispose();
        _trayIcon?.Dispose();
    }

    private void CaptureUndo(WorkInterval? interval)
    {
        var day = DateOnly.FromDateTime(interval?.StartedAt ?? DateTime.Now);
        _undoSnapshot = new UndoSnapshot(interval?.Id, interval?.EndedAt, interval?.RoundedEndedAt, day, _data.FinishedDays.Contains(day));
    }

    private void UndoActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_undoSnapshot is not { } snapshot) return;
        var interval = snapshot.IntervalId is Guid id ? _data.Intervals.FirstOrDefault(item => item.Id == id) : null;
        if (snapshot.IntervalId is not null && interval is null) { HideActionToast(); return; }
        if (snapshot.EndedAt is null && ActiveInterval is { } active && active.Id != snapshot.IntervalId)
        {
            ShowActionToast("Rückgängig nicht möglich: Eine neue Erfassung läuft", false);
            return;
        }
        if (interval is not null)
        {
            interval.EndedAt = snapshot.EndedAt;
            interval.RoundedEndedAt = snapshot.RoundedEndedAt;
        }
        if (snapshot.FinishedWasPresent) _data.FinishedDays.Add(snapshot.Day); else _data.FinishedDays.Remove(snapshot.Day);
        HideActionToast();
        SaveAndRefresh();
    }

    private void ShowActionToast(string message, bool canUndo)
    {
        ActionToastText.Text = message;
        UndoActionButton.Visibility = canUndo ? Visibility.Visible : Visibility.Collapsed;
        if (!_isCompact) ActionToast.Visibility = Visibility.Visible;
        else _trayIcon?.ShowNotification("Zeitfluss", message);
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideActionToast()
    {
        _toastTimer.Stop();
        ActionToast.Visibility = Visibility.Collapsed;
        _undoSnapshot = null;
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
        CompactDragSurface.CaptureMouse();
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
        CompactDragSurface.ReleaseMouseCapture();
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
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_data) { Owner = this };
        if (window.ShowDialog() != true) return;
        ApplyDesktopPreferences();
        SaveAndRefresh();
    }
    private void StatisticsButton_Click(object sender, RoutedEventArgs e) => new StatisticsWindow(_data, SaveAndRefresh) { Owner = this }.ShowDialog();

    private void EnterCompactMode()
    {
        if (_isCompact) return;
        HideActionToast();
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
        else { Left = area.Right - CompactWidth - 12; Top = area.Top + 12; }
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

    private static string FormatDurationWithSeconds(TimeSpan value) => $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";

    private sealed record UndoSnapshot(Guid? IntervalId, DateTime? EndedAt, DateTime? RoundedEndedAt, DateOnly Day, bool FinishedWasPresent);
}
