using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

        Render(new MainWindow(activeData, false), Path.Combine(outputDirectory, "main-active.png"));
        Render(new MainWindow(pausedData, false), Path.Combine(outputDirectory, "main-paused.png"));
        Render(new MainWindow(finishedData, false), Path.Combine(outputDirectory, "main-finished.png"));
        Render(new MainWindow(activeData, false), Path.Combine(outputDirectory, "main-compact.png"), window =>
        {
            var button = (Button)window.FindName("CompactModeButton");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        VerifyCompactRoundTrip(activeData);
        VerifyCompactActions(today);

        Render(new SettingsWindow(activeData) { ShowInTaskbar = false }, Path.Combine(outputDirectory, "settings-window.png"));
        Render(new SettingsWindow(roundedData) { ShowInTaskbar = false }, Path.Combine(outputDirectory, "settings-rounding.png"), window =>
        {
            ((ScrollViewer)window.FindName("SettingsScroll")).ScrollToVerticalOffset(250);
        });
        Render(new SettingsWindow(pausedData) { ShowInTaskbar = false }, Path.Combine(outputDirectory, "settings-backup.png"), window =>
        {
            var scroll = (ScrollViewer)window.FindName("SettingsScroll");
            scroll.ScrollToEnd();
            window.UpdateLayout();
            scroll.ScrollToVerticalOffset(Math.Max(0, scroll.VerticalOffset - 22));
        });
        Render(new StatisticsWindow(finishedData) { ShowInTaskbar = false }, Path.Combine(outputDirectory, "statistics-days.png"), window =>
        {
            var button = (Button)window.FindName("DayButton");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        var roundedPeriod = TimeCalculator.Periods(roundedData, today, now, PeriodKind.Day).First(period => period.Start == today);
        Render(new IntervalDetailsWindow(roundedData, roundedPeriod) { ShowInTaskbar = false }, Path.Combine(outputDirectory, "interval-details.png"));
        Render(new EditIntervalWindow(roundedData, roundedData.Intervals[0]) { ShowInTaskbar = false }, Path.Combine(outputDirectory, "edit-interval.png"));
        app.Shutdown();
        Console.WriteLine(Path.GetFullPath(outputDirectory));
    }

    private static AppData TestData(DateOnly today)
    {
        var data = new AppData { TrackingStartedOn = today.AddDays(-2) };
        foreach (var day in Enum.GetValues<DayOfWeek>()) data.Settings.DailyHours[day] = 0;
        data.Settings.DailyHours[today.DayOfWeek] = 8;
        data.Settings.WeeklyHours = 8;
        return data;
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
        if (Math.Abs(window.ActualWidth - 238) > 0.5 || Math.Abs(window.ActualHeight - 54) > 0.5 || window.ShowInTaskbar)
            throw new InvalidOperationException("Der Kompaktmodus hat nicht die erwartete Geometrie.");
        var dragSurface = (Grid)window.FindName("CompactDragSurface");
        dragSurface.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
        window.UpdateLayout();
        if (Math.Abs(window.ActualWidth - 342) > 0.5 || Math.Abs(window.ActualHeight - 408) > 0.5 || !window.ShowInTaskbar)
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
        ((Button)pauseWindow.FindName("CompactPauseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (pauseData.Intervals[0].EndedAt is null) throw new InvalidOperationException("Die Pause-Schaltfläche der Bubble hat die laufende Erfassung nicht beendet.");
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
}
