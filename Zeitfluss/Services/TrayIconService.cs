using System.Drawing;
using System.IO;
using System.Windows.Threading;
using Zeitfluss.Models;
using WinForms = System.Windows.Forms;

namespace Zeitfluss.Services;

public sealed class TrayIconService : IDisposable
{
    private const int MaximumTooltipLength = 127;
    private readonly Dispatcher _dispatcher;
    private readonly Icon _icon;
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ContextMenuStrip _menu;
    private readonly WinForms.ToolStripMenuItem _statusItem;
    private readonly WinForms.ToolStripMenuItem _startItem;
    private readonly WinForms.ToolStripMenuItem _pauseResumeItem;
    private readonly WinForms.ToolStripMenuItem _stopItem;
    private bool _disposed;

    public TrayIconService(string? iconPath = null, string applicationName = "Zeitfluss")
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        ApplicationName = string.IsNullOrWhiteSpace(applicationName) ? "Zeitfluss" : applicationName.Trim();
        _icon = LoadIcon(iconPath);

        _statusItem = new WinForms.ToolStripMenuItem("Bereit") { Enabled = false };
        _startItem = new WinForms.ToolStripMenuItem("Arbeit beginnen");
        _pauseResumeItem = new WinForms.ToolStripMenuItem("Pause") { Enabled = false };
        _stopItem = new WinForms.ToolStripMenuItem("Feierabend") { Enabled = false };
        var openItem = new WinForms.ToolStripMenuItem("Zeitfluss öffnen");
        var exitItem = new WinForms.ToolStripMenuItem("Zeitfluss beenden");

        openItem.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        _startItem.Click += (_, _) => StartRequested?.Invoke(this, EventArgs.Empty);
        _pauseResumeItem.Click += (_, _) => PauseResumeRequested?.Invoke(this, EventArgs.Empty);
        _stopItem.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _menu = new WinForms.ContextMenuStrip();
        _menu.Items.AddRange([
            _statusItem,
            new WinForms.ToolStripSeparator(),
            openItem,
            new WinForms.ToolStripSeparator(),
            _startItem,
            _pauseResumeItem,
            _stopItem,
            new WinForms.ToolStripSeparator(),
            exitItem
        ]);

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _icon,
            Text = ApplicationName,
            ContextMenuStrip = _menu,
            Visible = false
        };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? OpenRequested;
    public event EventHandler? StartRequested;
    public event EventHandler? PauseResumeRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? ExitRequested;

    public string ApplicationName { get; }
    public bool IsVisible => !_disposed && _notifyIcon.Visible;

    public void Show()
    {
        VerifyAccessAndNotDisposed();
        _notifyIcon.Visible = true;
    }

    public void Hide()
    {
        VerifyAccessAndNotDisposed();
        _notifyIcon.Visible = false;
    }

    public void Update(TrayIconState state)
    {
        VerifyAccessAndNotDisposed();
        ArgumentNullException.ThrowIfNull(state);

        var duration = TimeCalculator.FormatDuration(state.TodayDuration);
        _statusItem.Text = $"Heute {duration} · {state.StatusText}";
        _startItem.Enabled = !state.IsRunning;
        _startItem.Text = state.IsPaused ? "Arbeit fortsetzen" : "Arbeit beginnen";
        _pauseResumeItem.Enabled = state.IsRunning || state.IsPaused;
        _pauseResumeItem.Text = state.IsPaused ? "Fortsetzen" : "Pause";
        _stopItem.Enabled = state.CanStop;
        _notifyIcon.Text = Truncate($"{ApplicationName} · {duration} · {state.StatusText}", MaximumTooltipLength);
    }

    public void ShowNotification(string title, string message, int durationMilliseconds = 4000)
    {
        VerifyAccessAndNotDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _notifyIcon.ShowBalloonTip(
            Math.Clamp(durationMilliseconds, 1000, 30000),
            title,
            message,
            WinForms.ToolTipIcon.None);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(Dispose);
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void VerifyAccessAndNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _dispatcher.VerifyAccess();
    }

    private static Icon LoadIcon(string? iconPath)
    {
        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            if (!File.Exists(iconPath)) throw new FileNotFoundException("Das Tray-Icon wurde nicht gefunden.", iconPath);
            return new Icon(iconPath);
        }

        var executablePath = Environment.ProcessPath;
        using var associated = executablePath is null ? null : Icon.ExtractAssociatedIcon(executablePath);
        return associated is null ? (Icon)SystemIcons.Application.Clone() : (Icon)associated.Clone();
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..(maximumLength - 1)] + "…";
}
