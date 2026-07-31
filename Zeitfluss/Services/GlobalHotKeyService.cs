using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Zeitfluss.Models;

namespace Zeitfluss.Services;

public sealed class GlobalHotKeyService : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const int StartId = 0x5A10;
    private const int PauseResumeId = 0x5A11;
    private const int StopId = 0x5A12;
    private readonly Window _owner;
    private readonly Dispatcher _dispatcher;
    private readonly HwndSource _source;
    private GlobalHotKeyBindings _bindings;
    private bool _disposed;

    public GlobalHotKeyService(Window owner, GlobalHotKeyBindings? bindings = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        owner.Dispatcher.VerifyAccess();
        _owner = owner;
        _dispatcher = owner.Dispatcher;
        _bindings = bindings ?? GlobalHotKeyBindings.Default;
        Validate(_bindings);

        var handle = new WindowInteropHelper(owner).EnsureHandle();
        _source = HwndSource.FromHwnd(handle)
            ?? throw new InvalidOperationException("Das Fensterhandle für globale Tastenkürzel ist nicht verfügbar.");
        _source.AddHook(WindowMessageHook);
        _owner.Closed += OwnerOnClosed;
    }

    public event EventHandler? StartRequested;
    public event EventHandler? PauseResumeRequested;
    public event EventHandler? StopRequested;

    public bool IsRegistered { get; private set; }
    public GlobalHotKeyBindings Bindings => _bindings;

    public void Register()
    {
        ThrowIfDisposed();
        _dispatcher.VerifyAccess();
        if (IsRegistered) return;

        var registrations = new[]
        {
            (StartId, _bindings.Start, "Start"),
            (PauseResumeId, _bindings.PauseResume, "Pause/Fortsetzen"),
            (StopId, _bindings.Stop, "Feierabend")
        };
        var registeredIds = new List<int>();
        foreach (var (id, gesture, name) in registrations)
        {
            if (RegisterHotKey(_source.Handle, id, (uint)gesture.Modifiers, gesture.VirtualKey))
            {
                registeredIds.Add(id);
                continue;
            }

            foreach (var registeredId in registeredIds) UnregisterHotKey(_source.Handle, registeredId);
            throw new HotKeyRegistrationException(name, gesture, new Win32Exception(Marshal.GetLastWin32Error()));
        }

        IsRegistered = true;
    }

    public void UpdateBindings(GlobalHotKeyBindings bindings)
    {
        ThrowIfDisposed();
        _dispatcher.VerifyAccess();
        ArgumentNullException.ThrowIfNull(bindings);
        Validate(bindings);
        if (bindings == _bindings) return;

        var previous = _bindings;
        var wasRegistered = IsRegistered;
        if (wasRegistered) UnregisterCore();
        _bindings = bindings;
        try
        {
            if (wasRegistered) Register();
        }
        catch
        {
            _bindings = previous;
            if (wasRegistered)
            {
                try { Register(); }
                catch { /* Preserve the original registration error. */ }
            }
            throw;
        }
    }

    public void Unregister()
    {
        ThrowIfDisposed();
        _dispatcher.VerifyAccess();
        UnregisterCore();
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(Dispose);
            return;
        }

        UnregisterCore();
        _source.RemoveHook(WindowMessageHook);
        _owner.Closed -= OwnerOnClosed;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmHotKey) return IntPtr.Zero;
        handled = true;
        switch (wParam.ToInt32())
        {
            case StartId:
                StartRequested?.Invoke(this, EventArgs.Empty);
                break;
            case PauseResumeId:
                PauseResumeRequested?.Invoke(this, EventArgs.Empty);
                break;
            case StopId:
                StopRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
        return IntPtr.Zero;
    }

    private void UnregisterCore()
    {
        if (!IsRegistered) return;
        UnregisterHotKey(_source.Handle, StartId);
        UnregisterHotKey(_source.Handle, PauseResumeId);
        UnregisterHotKey(_source.Handle, StopId);
        IsRegistered = false;
    }

    private void OwnerOnClosed(object? sender, EventArgs e) => Dispose();

    private static void Validate(GlobalHotKeyBindings bindings)
    {
        var gestures = new[] { bindings.Start, bindings.PauseResume, bindings.Stop };
        if (gestures.Any(gesture => gesture.IsEmpty))
            throw new ArgumentException("Globale Tastenkürzel benötigen eine Taste.", nameof(bindings));
        if (gestures.Distinct().Count() != gestures.Length)
            throw new ArgumentException("Jedes globale Tastenkürzel muss eindeutig sein.", nameof(bindings));
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
}

public sealed class HotKeyRegistrationException(
    string action,
    GlobalHotKeyGesture gesture,
    Exception innerException)
    : InvalidOperationException($"Das globale Tastenkürzel für ‚{action}‘ konnte nicht registriert werden.", innerException)
{
    public string Action { get; } = action;
    public GlobalHotKeyGesture Gesture { get; } = gesture;
}
