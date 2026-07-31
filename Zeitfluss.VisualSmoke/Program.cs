using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Zeitfluss;
using Zeitfluss.Models;
using Zeitfluss.Services;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var outputDirectory = args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "renders");
        var app = new App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var activeData = TestData(today);
        activeData.Intervals.Add(new WorkInterval { StartedAt = now.AddHours(-3), EndedAt = now.AddHours(-2) });
        activeData.Intervals.Add(new WorkInterval { StartedAt = now.AddMinutes(-37) });

        var pausedData = TestData(today);
        pausedData.Intervals.Add(new WorkInterval { StartedAt = now.AddHours(-4), EndedAt = now.AddHours(-1) });

        var finishedData = TestData(today);
        finishedData.Intervals.Add(new WorkInterval { StartedAt = now.AddHours(-8), EndedAt = now.AddMinutes(-20) });
        finishedData.FinishedDays.Add(today);

        var roundedData = TestData(today);
        roundedData.Settings.UseFiveMinuteRounding = true;
        var roundedStart = today.ToDateTime(new TimeOnly(12, 26));
        var roundedEnd = today.ToDateTime(new TimeOnly(13, 2));
        roundedData.Intervals.Add(new WorkInterval { StartedAt = roundedStart, EndedAt = roundedEnd, UsesFiveMinuteRounding = true, RoundedStartedAt = TimeCalculator.RoundUpToFiveMinutes(roundedStart), RoundedEndedAt = TimeCalculator.RoundDownToFiveMinutes(roundedEnd) });

        var historyData = HistoryData(today);

        Render(new MainWindow(activeData, false), Path.Combine(outputDirectory, "main-active.png"));
        Render(new MainWindow(pausedData, false), Path.Combine(outputDirectory, "main-paused.png"));
        Render(new MainWindow(finishedData, false), Path.Combine(outputDirectory, "main-finished.png"));
        Render(new MainWindow(activeData, false), Path.Combine(outputDirectory, "main-compact.png"), window =>
        {
            var button = (Button)window.FindName("CompactModeButton");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Render(new MainWindow(pausedData, false), Path.Combine(outputDirectory, "main-compact-paused.png"), window =>
        {
            var button = (Button)window.FindName("CompactModeButton");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        VerifyCompactRoundTrip(activeData);
        VerifyCompactActions(today);
        VerifyStateAwareHero(activeData, pausedData);
        VerifyMainUndo(today);

        Render(new SettingsWindow(activeData) { ShowInTaskbar = false }, Path.Combine(outputDirectory, "settings-window.png"));
        Render(new SettingsWindow(roundedData) { ShowInTaskbar = false }, Path.Combine(outputDirectory, "settings-rounding.png"), window =>
        {
            ((RadioButton)window.FindName("CaptureNavigation")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Render(new SettingsWindow(activeData) { ShowInTaskbar = false }, Path.Combine(outputDirectory, "settings-appearance.png"), window =>
        {
            ((RadioButton)window.FindName("AppearanceNavigation")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((Slider)window.FindName("OpacitySlider")).Value = 86;
        });
        Render(new SettingsWindow(pausedData) { ShowInTaskbar = false }, Path.Combine(outputDirectory, "settings-backup.png"), window =>
        {
            ((RadioButton)window.FindName("DataNavigation")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Render(new AboutWindow(activeData.Settings) { ShowInTaskbar = false }, Path.Combine(outputDirectory, "about-window.png"));
        VerifySettingsNavigation(activeData);
        VerifyAboutWindow(activeData.Settings);
        Render(new StatisticsWindow(historyData) { ShowInTaskbar = false }, Path.Combine(outputDirectory, "statistics-days.png"), window =>
        {
            ((Button)window.FindName("DayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((ScrollViewer)window.FindName("PeriodOverviewScroll")).ScrollToEnd();
        });
        Render(new StatisticsWindow(historyData) { ShowInTaskbar = false }, Path.Combine(outputDirectory, "statistics-weeks.png"), window =>
        {
            ((Button)window.FindName("WeekButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((ScrollViewer)window.FindName("PeriodOverviewScroll")).ScrollToEnd();
        });
        Render(new StatisticsWindow(historyData) { ShowInTaskbar = false }, Path.Combine(outputDirectory, "statistics-months.png"), window =>
        {
            ((Button)window.FindName("MonthButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((ScrollViewer)window.FindName("PeriodOverviewScroll")).ScrollToEnd();
        });
        Render(new StatisticsWindow(historyData) { ShowInTaskbar = false }, Path.Combine(outputDirectory, "statistics-years.png"), window =>
        {
            ((Button)window.FindName("YearButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((ScrollViewer)window.FindName("PeriodOverviewScroll")).ScrollToEnd();
        });
        Render(new StatisticsWindow(historyData) { ShowInTaskbar = false, Width = 900, Height = 760 }, Path.Combine(outputDirectory, "statistics-minimum.png"), window =>
        {
            ((Button)window.FindName("WeekButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((ScrollViewer)window.FindName("PeriodOverviewScroll")).ScrollToEnd();
        });
        VerifyStatisticsDynamicOverview(historyData);
        VerifyStatisticsMinimumSize(historyData);
        var roundedPeriod = TimeCalculator.Periods(roundedData, today, now, PeriodKind.Day).First(period => period.Start == today);
        Render(new IntervalDetailsWindow(roundedData, roundedPeriod) { ShowInTaskbar = false }, Path.Combine(outputDirectory, "interval-details.png"));
        Render(new EditIntervalWindow(roundedData, roundedData.Intervals[0]) { ShowInTaskbar = false }, Path.Combine(outputDirectory, "edit-interval.png"));
        var recoveryData = TestData(today);
        var recoveryStart = today.AddDays(-1).ToDateTime(new TimeOnly(8, 0));
        recoveryData.Intervals.Add(new WorkInterval { StartedAt = recoveryStart });
        var recoveryAssessment = new TimeRecoveryAdvisor().Assess(recoveryData, now, options: new RecoveryAdvisorOptions { LongRunningThreshold = TimeSpan.FromHours(12), IdleThreshold = TimeSpan.FromMinutes(10) });
        Render(new TimeRecoveryWindow(recoveryData, recoveryAssessment) { ShowInTaskbar = false }, Path.Combine(outputDirectory, "time-recovery.png"));
        VerifyOpacityIsApplied(today);
        app.Shutdown();
        Console.WriteLine(Path.GetFullPath(outputDirectory));
    }

    private static AppData TestData(DateOnly today)
    {
        var data = new AppData { TrackingStartedOn = today.AddDays(-2) };
        foreach (var day in Enum.GetValues<DayOfWeek>()) data.Settings.DailyHours[day] = 0;
        data.Settings.DailyHours[today.DayOfWeek] = 8;
        data.Settings.WeeklyHours = 8;
        data.Settings.WindowOpacityPercent = 88;
        return data;
    }

    private static AppData HistoryData(DateOnly today)
    {
        var currentMonday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var data = new AppData { TrackingStartedOn = currentMonday.AddDays(-20 * 7) };
        foreach (var day in Enum.GetValues<DayOfWeek>())
            data.Settings.DailyHours[day] = day is >= DayOfWeek.Monday and <= DayOfWeek.Friday ? 8 : 0;
        data.Settings.WeeklyHours = 40;
        data.Settings.WindowOpacityPercent = 88;

        for (var week = 0; week < 20; week++)
        {
            var monday = currentMonday.AddDays(-week * 7);
            AddHistoryInterval(data, monday, today, 8);
            AddHistoryInterval(data, monday.AddDays(1), today, 7);
            AddHistoryInterval(data, monday.AddDays(2), today, 9);
        }
        return data;
    }

    private static void AddHistoryInterval(AppData data, DateOnly date, DateOnly today, int hours)
    {
        if (date > today) return;
        var startedAt = date.ToDateTime(new TimeOnly(8, 0));
        data.Intervals.Add(new WorkInterval { StartedAt = startedAt, EndedAt = startedAt.AddHours(hours) });
    }

    private static void Render(Window window, string output, Action<Window>? beforeCapture = null)
    {
        window.Show();
        window.UpdateLayout();
        beforeCapture?.Invoke(window);
        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        using (var stream = File.Create(output)) encoder.Save(stream);
        window.Close();
    }

    private static void VerifyCompactRoundTrip(AppData data)
    {
        var window = new MainWindow(data, false);
        window.Show();
        window.UpdateLayout();
        ((Button)window.FindName("CompactModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        if (Math.Abs(window.ActualWidth - 276) > 0.5 || Math.Abs(window.ActualHeight - 58) > 0.5 || window.ShowInTaskbar)
            throw new InvalidOperationException("Der Kompaktmodus hat nicht die erwartete Geometrie.");
        var dragSurface = (Grid)window.FindName("CompactDragSurface");
        dragSurface.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
        window.UpdateLayout();
        if (Math.Abs(window.ActualWidth - 380) > 0.5 || Math.Abs(window.ActualHeight - 520) > 0.5 || !window.ShowInTaskbar)
            throw new InvalidOperationException("Das Hauptfenster wurde nicht korrekt wiederhergestellt.");
        window.Close();
    }

    private static void VerifyCompactActions(DateOnly today)
    {
        var pauseData = TestData(today);
        pauseData.Intervals.Add(new WorkInterval { StartedAt = DateTime.Now.AddMinutes(-10) });
        var pauseWindow = new MainWindow(pauseData, false);
        pauseWindow.Show();
        pauseWindow.UpdateLayout();
        ((Button)pauseWindow.FindName("CompactModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var pauseButton = (Button)pauseWindow.FindName("CompactPauseButton");
        pauseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (pauseData.Intervals[0].EndedAt is null) throw new InvalidOperationException("Die Pause-Schaltfläche der Bubble hat die laufende Erfassung nicht beendet.");
        if (!pauseButton.IsEnabled || !Equals(pauseButton.ToolTip, "Fortsetzen")) throw new InvalidOperationException("Die Bubble bietet nach dem Pausieren kein Fortsetzen an.");
        pauseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (pauseData.Intervals.Count != 2 || pauseData.Intervals[^1].EndedAt is not null) throw new InvalidOperationException("Die Play-Schaltfläche der Bubble hat die Erfassung nicht fortgesetzt.");
        pauseWindow.Close();

        var stopData = TestData(today);
        stopData.Intervals.Add(new WorkInterval { StartedAt = DateTime.Now.AddMinutes(-10) });
        var stopWindow = new MainWindow(stopData, false);
        stopWindow.Show();
        stopWindow.UpdateLayout();
        ((Button)stopWindow.FindName("CompactModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((Button)stopWindow.FindName("CompactEndButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (stopData.Intervals[0].EndedAt is null || !stopData.FinishedDays.Contains(today)) throw new InvalidOperationException("Die Stop-Schaltfläche der Bubble hat den Arbeitstag nicht beendet.");
        stopWindow.Close();
    }

    private static void VerifyStateAwareHero(AppData activeData, AppData pausedData)
    {
        var active = new MainWindow(activeData, false);
        active.Show();
        active.UpdateLayout();
        if (((TextBlock)active.FindName("HeroCaptionText")).Text != "AKTUELLE SITZUNG" || !((TextBlock)active.FindName("HeroMetaText")).Text.Contains("Noch"))
            throw new InvalidOperationException("Die laufende Fokuskarte zeigt keine Sitzung oder Restzeit.");
        active.Close();

        var paused = new MainWindow(pausedData, false);
        paused.Show();
        paused.UpdateLayout();
        if (((TextBlock)paused.FindName("HeroCaptionText")).Text != "HEUTE ERFASST" || !((TextBlock)paused.FindName("StatusText")).Text.StartsWith("Pausiert"))
            throw new InvalidOperationException("Die pausierte Fokuskarte zeigt ihren Zustand nicht eindeutig.");
        paused.Close();
    }

    private static void VerifyMainUndo(DateOnly today)
    {
        var data = TestData(today);
        data.Intervals.Add(new WorkInterval { StartedAt = DateTime.Now.AddMinutes(-10) });
        var window = new MainWindow(data, false);
        window.Show();
        window.UpdateLayout();
        ((Button)window.FindName("PauseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (data.Intervals[0].EndedAt is null || ((Border)window.FindName("ActionToast")).Visibility != Visibility.Visible)
            throw new InvalidOperationException("Pause oder Rückgängig-Hinweis wurde nicht ausgelöst.");
        ((Button)window.FindName("UndoActionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (data.Intervals[0].EndedAt is not null) throw new InvalidOperationException("Die pausierte Erfassung wurde nicht wiederhergestellt.");
        window.Close();
    }

    private static void VerifySettingsNavigation(AppData data)
    {
        var window = new SettingsWindow(data);
        window.Show();
        window.UpdateLayout();
        ((RadioButton)window.FindName("CaptureNavigation")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (((ScrollViewer)window.FindName("CaptureScroll")).Visibility != Visibility.Visible) throw new InvalidOperationException("Der Erfassungsbereich ist nicht erreichbar.");
        ((RadioButton)window.FindName("AppearanceNavigation")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (((ScrollViewer)window.FindName("AppearanceScroll")).Visibility != Visibility.Visible) throw new InvalidOperationException("Der Darstellungsbereich ist nicht erreichbar.");
        ((RadioButton)window.FindName("DataNavigation")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (((ScrollViewer)window.FindName("DataScroll")).Visibility != Visibility.Visible) throw new InvalidOperationException("Der Datenbereich ist nicht erreichbar.");
        window.Close();
    }

    private static void VerifyAboutWindow(AppSettings settings)
    {
        var window = new AboutWindow(settings);
        window.Show();
        window.UpdateLayout();
        if (((TextBlock)window.FindName("ImprintNameText")).Text != "Bastian Werner" ||
            ((TextBlock)window.FindName("ImprintOrganizationText")).Text != "BAIUDBw TM 1" ||
            AboutWindow.ContactEmail != "bastianwerner@bundeswehr.org" ||
            AboutWindow.GitHubUrl != "https://github.com/baschti85/Zeitfluss")
            throw new InvalidOperationException("Das Impressum enthält nicht die freigegebenen Kontaktdaten.");
        if (!((TextBlock)window.FindName("VersionText")).Text.StartsWith("Version 1.1.0", StringComparison.Ordinal))
            throw new InvalidOperationException("Das Impressum zeigt nicht die erwartete Version 1.1.0.");
        window.Close();
    }

    private static void VerifyStatisticsDynamicOverview(AppData data)
    {
        var window = new StatisticsWindow(data);
        window.Show();
        window.UpdateLayout();
        VerifyStatisticsMode(window, data, PeriodKind.Week, "Wochenverlauf", "Saldo nach Wochen");

        foreach (var (buttonName, kind, title, chartTitle) in new[]
        {
            ("DayButton", PeriodKind.Day, "Tagesverlauf", "Saldo nach Tagen"),
            ("MonthButton", PeriodKind.Month, "Monatsverlauf", "Saldo nach Monaten"),
            ("YearButton", PeriodKind.Year, "Jahresverlauf", "Saldo nach Jahren")
        })
        {
            ((Button)window.FindName(buttonName)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            VerifyStatisticsMode(window, data, kind, title, chartTitle);
        }

        ((Button)window.FindName("DayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var overviewScroll = (ScrollViewer)window.FindName("PeriodOverviewScroll");
        overviewScroll.ScrollToVerticalOffset(overviewScroll.ScrollableHeight / 2);
        var savedDayOffset = overviewScroll.VerticalOffset;
        ((Button)window.FindName("MonthButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((Button)window.FindName("DayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        if (Math.Abs(overviewScroll.VerticalOffset - savedDayOffset) > 1)
            throw new InvalidOperationException("Die Tagesansicht verliert ihre historische Scrollposition beim Moduswechsel.");
        window.Close();
    }

    private static void VerifyStatisticsMinimumSize(AppData data)
    {
        var window = new StatisticsWindow(data) { Width = 900, Height = 760 };
        window.Show();
        window.UpdateLayout();
        VerifyStatisticsMode(window, data, PeriodKind.Week, "Wochenverlauf", "Saldo nach Wochen");
        window.Close();
    }

    private static void VerifyStatisticsMode(StatisticsWindow window, AppData data, PeriodKind kind, string expectedTitle, string expectedChartTitle)
    {
        var overviewTitle = (TextBlock)window.FindName("OverviewTitleText");
        var chartTitle = (TextBlock)window.FindName("BalanceChartTitleText");
        var host = (Grid)window.FindName("PeriodOverviewHost");
        var scroll = (ScrollViewer)window.FindName("PeriodOverviewScroll");
        var latest = (Button)window.FindName("LatestOverviewButton");
        var expectedCount = TimeCalculator.Periods(data, DateOnly.FromDateTime(DateTime.Now), DateTime.Now, kind).Count;
        var periodButtons = VisualDescendants<Button>(host).Where(button => button.Tag is PeriodSummary).ToList();

        if (overviewTitle.Text != expectedTitle || chartTitle.Text != expectedChartTitle)
            throw new InvalidOperationException($"Die Visualisierung folgt der Auswahl {kind} nicht.");
        if (periodButtons.Count != expectedCount)
            throw new InvalidOperationException($"{kind}: {periodButtons.Count} sichtbare Perioden statt {expectedCount}.");

        scroll.ScrollToEnd();
        window.UpdateLayout();
        if (periodButtons.Count > 0)
        {
            var last = periodButtons[^1];
            var point = last.TransformToAncestor(scroll).Transform(new Point(0, 0));
            if (point.Y < -1 || point.Y + last.ActualHeight > scroll.ActualHeight + 1)
                throw new InvalidOperationException($"{kind}: Die neueste Periodenzeile ist am unteren Rand abgeschnitten.");

            var fullyVisible = periodButtons.Count(button =>
            {
                var cell = button.TransformToAncestor(scroll).Transform(new Point(0, 0));
                return cell.Y >= -1 && cell.Y + button.ActualHeight <= scroll.ActualHeight + 1;
            });
            if (kind == PeriodKind.Week && fullyVisible <= 6)
                throw new InvalidOperationException($"Die Wochenansicht zeigt nur {fullyVisible} vollständige Wochen gleichzeitig.");
        }
        if (scroll.ScrollableHeight > 1)
        {
            scroll.ScrollToVerticalOffset(scroll.ScrollableHeight / 2);
            window.UpdateLayout();
            if (!latest.IsEnabled) throw new InvalidOperationException($"{kind}: Der Rücksprung zum aktuellen Zeitraum wird beim Scrollen nicht angeboten.");
            latest.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            if (scroll.VerticalOffset < scroll.ScrollableHeight - 1)
                throw new InvalidOperationException($"{kind}: 'Aktuell' springt nicht zum neuesten Zeitraum.");
        }
    }

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in VisualDescendants<T>(child)) yield return descendant;
        }
    }

    private static void VerifyOpacityIsApplied(DateOnly today)
    {
        var data = TestData(today);
        var start = today.ToDateTime(new TimeOnly(8, 0));
        data.Intervals.Add(new WorkInterval { StartedAt = start, EndedAt = start.AddHours(1) });

        var owner = new MainWindow(data, false);
        owner.Show();
        owner.UpdateLayout();
        AssertOpacity(owner, 0.88, "Hauptfenster");
        ((Button)owner.FindName("CompactModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        AssertOpacity(owner, 0.88, "Bubble");
        ((Grid)owner.FindName("CompactDragSurface")).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });

        var settings = new SettingsWindow(data) { Owner = owner, ShowInTaskbar = false };
        settings.Show();
        ((Slider)settings.FindName("OpacitySlider")).Value = 84;
        AssertOpacity(settings, 0.84, "Einstellungen");
        AssertOpacity(owner, 0.84, "Live-Vorschau");
        settings.Close();
        AssertOpacity(owner, 0.88, "abgebrochene Live-Vorschau");

        var period = TimeCalculator.Periods(data, today, DateTime.Now, PeriodKind.Day).First(x => x.Start == today);
        var statistics = new StatisticsWindow(data);
        var details = new IntervalDetailsWindow(data, period);
        var editor = new EditIntervalWindow(data, data.Intervals[0]);
        var assessment = new TimeRecoveryAdvisor().Assess(data, today.AddDays(1).ToDateTime(new TimeOnly(8, 0)), options: new RecoveryAdvisorOptions { LongRunningThreshold = TimeSpan.FromMinutes(30), IdleThreshold = TimeSpan.FromMinutes(10) });
        var recovery = new TimeRecoveryWindow(data, assessment);
        var about = new AboutWindow(data.Settings);
        AssertOpacity(statistics, 0.88, "Statistik");
        AssertOpacity(details, 0.88, "Details");
        AssertOpacity(editor, 0.88, "Zeitkorrektur");
        AssertOpacity(recovery, 0.88, "Korrekturassistent");
        AssertOpacity(about, 0.88, "Impressum");
        statistics.Close();
        details.Close();
        editor.Close();
        recovery.Close();
        about.Close();
        owner.Close();
    }

    private static void AssertOpacity(Window window, double expected, string scope)
    {
        if (Math.Abs(window.Opacity - expected) > 0.0001)
            throw new InvalidOperationException($"Die Transparenz für {scope} ist {window.Opacity:0.##} statt {expected:0.##}.");
    }
}
