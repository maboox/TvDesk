using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace TvDesk.Behaviors;

/// <summary>رفتار هوشمند (مثل Wallpaper Engine): میوت هنگام فوکوس اپ دیگر، توقف هنگام فول‌اسکرین.</summary>
public sealed class FocusFullscreenWatcher : IDisposable
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    private readonly DispatcherTimer _timer;
    private bool _pausedForFullscreen;
    private bool _pausedForFocus;

    public FocusFullscreenWatcher()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Tick;
    }

    public void Start() => _timer.Start();

    private void Tick(object? sender, EventArgs e)
    {
        var ctrl = AppController.Instance;
        if (ctrl == null) return;

        IntPtr fg = GetForegroundWindow();
        uint myPid = (uint)Environment.ProcessId;
        GetWindowThreadProcessId(fg, out uint fgPid);
        bool ourApp = fgPid == myPid;
        bool isShell = fg == GetShellWindow() || fg == IntPtr.Zero;
        bool focusLost = !ourApp && !isShell;

        bool fullscreen = false;
        if (focusLost && GetWindowRect(fg, out RECT r))
        {
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            fullscreen = w >= (int)System.Windows.SystemParameters.PrimaryScreenWidth
                      && h >= (int)System.Windows.SystemParameters.PrimaryScreenHeight;
        }

        // تصویر: fullscreen اولویت دارد، بعد focus. هر کدام مستقل از صدا قابل تنظیم‌اند.
        if (ctrl.Settings.PauseVideoOnFullscreen && fullscreen)
        {
            if (!_pausedForFullscreen) { ctrl.PauseVideo("fullscreen"); _pausedForFullscreen = true; }
        }
        else if (_pausedForFullscreen && !fullscreen)
        {
            _pausedForFullscreen = false;
            ctrl.ResumeVideo("fullscreen ended");
        }

        if (!_pausedForFullscreen && ctrl.Settings.PauseVideoOnFocusLoss && focusLost)
        {
            if (!_pausedForFocus) { ctrl.PauseVideo("focus lost"); _pausedForFocus = true; }
        }
        else if (_pausedForFocus && (!focusLost || _pausedForFullscreen))
        {
            _pausedForFocus = false;
            if (!_pausedForFullscreen) ctrl.ResumeVideo("focus returned");
        }

        // صدا: اگر کاربر دستی mute کرده، آن را دست نمی‌زنیم.
        if (!ctrl.Settings.Muted)
        {
            bool shouldMute = (ctrl.Settings.MuteAudioOnFullscreen && fullscreen)
                           || (ctrl.Settings.MuteAudioOnFocusLoss && focusLost);
            ctrl.Playback.SetMuted(shouldMute);
        }
    }

    public void Dispose() => _timer.Stop();
}

/// <summary>هات‌کی‌های سراسری (ایده ۷): Ctrl+Alt+D آیکون‌ها، Ctrl+Alt+M میوت، Ctrl+Alt+T پنل.</summary>
public sealed class HotkeyManager : IDisposable
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002;
    const int WM_HOTKEY = 0x0312;

    private HwndSource? _source;
    private IntPtr _handle;

    public void Register()
    {
        var parameters = new HwndSourceParameters("TvDeskHotkeys")
        {
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(-3) // HWND_MESSAGE (message-only window)
        };
        _source = new HwndSource(parameters);
        _handle = _source.Handle;
        _source.AddHook(WndProc);

        RegisterHotKey(_handle, 1, MOD_CONTROL | MOD_ALT, 0x44); // D
        RegisterHotKey(_handle, 2, MOD_CONTROL | MOD_ALT, 0x4D); // M
        RegisterHotKey(_handle, 3, MOD_CONTROL | MOD_ALT, 0x54); // T
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var ctrl = AppController.Instance;
            switch (wParam.ToInt32())
            {
                case 1: ctrl.ToggleDesktopIcons(); handled = true; break;
                case 2: ctrl.ToggleMute(); handled = true; break;
                case 3: ctrl.ShowControl(); handled = true; break;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            UnregisterHotKey(_handle, 1);
            UnregisterHotKey(_handle, 2);
            UnregisterHotKey(_handle, 3);
        }
        _source?.Dispose();
    }
}
