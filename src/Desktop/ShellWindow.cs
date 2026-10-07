using System;
using System.Windows.Interop;
using System.Windows.Threading;
using TvDesk.Core;

namespace TvDesk.Desktop;

/// <summary>
/// Hidden top-level window that receives broadcast messages: Explorer restarts (TaskbarCreated),
/// display changes, and global hotkeys. (Message-only windows do not receive broadcasts.)
/// </summary>
public sealed class ShellWindow : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int WM_DISPLAYCHANGE = 0x007E;
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int WM_DPICHANGED = 0x02E0;
    public const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_NOREPEAT = 0x4000;

    private readonly HwndSource _source;
    private readonly uint _taskbarCreated;
    private readonly System.Collections.Generic.List<int> _hotkeys = new();

    public event Action? ExplorerRestarted;
    public event Action? DisplayChanged;
    public event Action<int>? Hotkey;

    public ShellWindow()
    {
        var p = new HwndSourceParameters("TvDeskShellWindow")
        {
            Width = 0,
            Height = 0,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, not visible
            ExtendedWindowStyle = 0x00000080,          // WS_EX_TOOLWINDOW
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
        _taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
    }

    public bool RegisterHotkey(int id, uint modifiers, uint vk)
    {
        bool ok = Native.RegisterHotKey(_source.Handle, id, modifiers | MOD_NOREPEAT, vk);
        if (ok) _hotkeys.Add(id);
        else Logger.Log($"Hotkey {id} could not be registered (already used by another app?)");
        return ok;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_taskbarCreated != 0 && msg == (int)_taskbarCreated)
        {
            Logger.Log("TaskbarCreated received (Explorer restarted)");
            ExplorerRestarted?.Invoke();
        }
        else if (msg == WM_DISPLAYCHANGE || msg == WM_DPICHANGED)
        {
            DisplayChanged?.Invoke();
        }
        else if (msg == WM_SETTINGCHANGE && wParam.ToInt64() == 0x002F /* SPI_SETWORKAREA */)
        {
            DisplayChanged?.Invoke();
        }
        else if (msg == WM_HOTKEY)
        {
            Hotkey?.Invoke(wParam.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (int id in _hotkeys) Native.UnregisterHotKey(_source.Handle, id);
        _hotkeys.Clear();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}

/// <summary>
/// Decides whether the wallpaper should keep playing, mute or pause:
/// another app focused / a maximized window / a fullscreen app or game / on battery / PC locked.
/// </summary>
public sealed class ActivityWatcher : IDisposable
{
    private static readonly string[] ShellClasses =
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow",
        "ForegroundStaging", "MultitaskingViewFrame", "TaskListThumbnailWnd", "#32768",
    };

    private readonly DispatcherTimer _timer;
    private readonly Func<AppSettings> _settings;
    private readonly Func<System.Drawing.Rectangle?> _wallpaperRect;
    private readonly uint _pid = (uint)Environment.ProcessId;
    private AutoAction _applied = AutoAction.None;
    private AutoAction _pending = AutoAction.None;
    private int _stableTicks;
    private bool _locked;

    public event Action<AutoAction>? ActionChanged;

    public AutoAction Current => _applied;

    public ActivityWatcher(Func<AppSettings> settings, Func<System.Drawing.Rectangle?> wallpaperRect)
    {
        _settings = settings;
        _wallpaperRect = wallpaperRect;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(1000) };
        _timer.Tick += (_, _) => Tick();
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public void Start() => _timer.Start();

    /// <summary>Re-evaluate right away (e.g. after settings changed).</summary>
    public void Poke()
    {
        _stableTicks = 99;
        Tick();
    }

    private void OnSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock || e.Reason == Microsoft.Win32.SessionSwitchReason.ConsoleDisconnect)
            _locked = true;
        else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock || e.Reason == Microsoft.Win32.SessionSwitchReason.ConsoleConnect)
            _locked = false;
    }

    private void Tick()
    {
        AutoAction desired;
        try { desired = Evaluate(_settings()); }
        catch (Exception ex) { Logger.Log("ActivityWatcher", ex); return; }

        if (desired == _pending) _stableTicks++;
        else { _pending = desired; _stableTicks = 0; }

        // Require the state to hold for ~2s before acting, so alt-tab flicker doesn't toggle playback.
        if (_pending != _applied && _stableTicks >= 1)
        {
            _applied = _pending;
            ActionChanged?.Invoke(_applied);
        }
    }

    private AutoAction Evaluate(AppSettings s)
    {
        var action = AutoAction.None;
        if (_locked) action = Max(action, s.OnLocked);

        if (s.OnBattery != AutoAction.None && Native.GetSystemPowerStatus(out var power) && power.ACLineStatus == 0)
            action = Max(action, s.OnBattery);

        IntPtr fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || IsShellOrOwn(fg) || Native.IsIconic(fg) || !Native.IsWindowVisible(fg) || Native.IsCloaked(fg))
            return action;

        action = Max(action, s.OnAppFocused);
        if (!OnWallpaperMonitor(fg)) return action;

        if (s.OnMaximized != AutoAction.None && Native.IsZoomed(fg)) action = Max(action, s.OnMaximized);
        if (s.OnFullscreen != AutoAction.None && IsFullscreen(fg)) action = Max(action, s.OnFullscreen);
        return action;
    }

    private bool IsShellOrOwn(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == _pid) return true;
        string cls = Native.ClassOf(hwnd);
        return Array.IndexOf(ShellClasses, cls) >= 0;
    }

    private static bool IsFullscreen(IntPtr hwnd)
    {
        if (!Native.GetWindowRect(hwnd, out var r)) return false;
        IntPtr mon = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
        var mi = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(mon, ref mi)) return false;
        var m = mi.rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    private bool OnWallpaperMonitor(IntPtr hwnd)
    {
        var wr = _wallpaperRect();
        if (wr == null) return true;
        if (!Native.GetWindowRect(hwnd, out var r)) return true;
        var win = System.Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        var inter = System.Drawing.Rectangle.Intersect(win, wr.Value);
        // Count it when at least a third of the window is on the wallpaper area.
        long area = (long)Math.Max(1, win.Width) * Math.Max(1, win.Height);
        return (long)inter.Width * inter.Height * 3 >= area;
    }

    private static AutoAction Max(AutoAction a, AutoAction b) => (AutoAction)Math.Max((int)a, (int)b);

    public void Dispose()
    {
        _timer.Stop();
        Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
    }
}
