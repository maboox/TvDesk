using System;
using TvDesk.Core;

namespace TvDesk.Desktop;

public enum AttachMode
{
    None,
    /// <summary>Windows 10 / Windows 11 up to 23H2: a top-level WorkerW sits behind the icon layer.</summary>
    ClassicWorkerW,
    /// <summary>Windows 11 24H2+: SHELLDLL_DefView and WorkerW are children of Progman ("raised desktop").</summary>
    RaisedDesktop,
    /// <summary>Anything else: child of Progman, z-ordered below the icon layer.</summary>
    ProgmanFallback,
}

/// <summary>Puts a window between the desktop wallpaper and the desktop icons.</summary>
internal static class DesktopAttacher
{
    public static AttachMode Attach(IntPtr hwnd, out IntPtr parent)
    {
        parent = IntPtr.Zero;
        IntPtr progman = Native.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            Logger.Log("Attach: Progman not found (Explorer not running?)");
            return AttachMode.None;
        }

        // Ask Explorer to create the WorkerW layer behind the icons (undocumented but used by every live-wallpaper app).
        Native.SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1), Native.SMTO_NORMAL, 1000, out _);
        Native.SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, Native.SMTO_NORMAL, 1000, out _);

        MakeChildStyle(hwnd);

        IntPtr defViewInProgman = Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        IntPtr workerInProgman = Native.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);

        AttachMode mode;
        if (defViewInProgman != IntPtr.Zero && workerInProgman != IntPtr.Zero)
        {
            // Windows 11 24H2+: Progman > [SHELLDLL_DefView (icons), <us>, WorkerW (static wallpaper)]
            Native.SetParent(hwnd, progman);
            parent = progman;
            Native.SetWindowPos(hwnd, defViewInProgman, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            Native.SetWindowPos(workerInProgman, hwnd, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            mode = AttachMode.RaisedDesktop;
        }
        else
        {
            IntPtr worker = FindClassicWorkerW();
            if (worker != IntPtr.Zero)
            {
                Native.SetParent(hwnd, worker);
                parent = worker;
                Native.SetWindowPos(hwnd, Native.HWND_TOP, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                mode = AttachMode.ClassicWorkerW;
            }
            else
            {
                Native.SetParent(hwnd, progman);
                parent = progman;
                if (defViewInProgman != IntPtr.Zero)
                    Native.SetWindowPos(hwnd, defViewInProgman, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                mode = AttachMode.ProgmanFallback;
            }
        }

        Logger.Log($"Attach: mode={mode} hwnd={hwnd} parent={parent} progman={progman} defView={defViewInProgman} workerInProgman={workerInProgman} build={Environment.OSVersion.Version}");
        return mode;
    }

    /// <summary>The empty WorkerW that follows the window hosting SHELLDLL_DefView (Win10 / Win11 ≤ 23H2).</summary>
    private static IntPtr FindClassicWorkerW()
    {
        IntPtr result = IntPtr.Zero;
        Native.EnumWindows((top, _) =>
        {
            IntPtr dv = Native.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (dv == IntPtr.Zero) return true;
            if (Native.ClassOf(top) == "Progman") return true; // icons still on Progman: no classic layer
            result = Native.FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            return false;
        }, IntPtr.Zero);
        return result;
    }

    private static void MakeChildStyle(IntPtr hwnd)
    {
        long style = Native.GetWindowLong(hwnd, Native.GWL_STYLE);
        style &= ~(Native.WS_POPUP | Native.WS_CAPTION | Native.WS_THICKFRAME | Native.WS_SYSMENU);
        style |= Native.WS_CHILD;
        Native.SetWindowLong(hwnd, Native.GWL_STYLE, style);

        long ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
        ex &= ~Native.WS_EX_APPWINDOW;
        ex |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
        Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex);
    }

    /// <summary>Moves/resizes the attached window to cover <paramref name="screen"/> (screen coordinates, physical pixels).</summary>
    public static void Position(IntPtr hwnd, IntPtr parent, int x, int y, int w, int h)
    {
        var p = new Native.POINT { X = x, Y = y };
        if (parent != IntPtr.Zero) Native.ScreenToClient(parent, ref p);
        Native.SetWindowPos(hwnd, IntPtr.Zero, p.X, p.Y, w, h, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
    }

    /// <summary>Re-asserts the z-order (e.g. after Explorer re-arranged its children).</summary>
    public static void ReassertZOrder(IntPtr hwnd, IntPtr parent, AttachMode mode)
    {
        try
        {
            if (mode == AttachMode.RaisedDesktop || mode == AttachMode.ProgmanFallback)
            {
                IntPtr defView = Native.FindWindowEx(parent, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView != IntPtr.Zero)
                    Native.SetWindowPos(hwnd, defView, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                if (mode == AttachMode.RaisedDesktop)
                {
                    IntPtr worker = Native.FindWindowEx(parent, IntPtr.Zero, "WorkerW", null);
                    if (worker != IntPtr.Zero)
                        Native.SetWindowPos(worker, hwnd, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                }
            }
            else if (mode == AttachMode.ClassicWorkerW)
            {
                Native.SetWindowPos(hwnd, Native.HWND_TOP, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
        }
        catch (Exception ex) { Logger.Log("ReassertZOrder failed", ex); }
    }

    public static bool IsHealthy(IntPtr hwnd, IntPtr parent)
    {
        if (hwnd == IntPtr.Zero || parent == IntPtr.Zero) return false;
        if (!Native.IsWindow(hwnd) || !Native.IsWindow(parent)) return false;
        return Native.GetParent(hwnd) == parent;
    }

    /// <summary>Makes Explorer repaint the normal wallpaper (after we hide or remove our window).</summary>
    public static void RefreshWallpaper(IntPtr parent)
    {
        try
        {
            if (parent != IntPtr.Zero && Native.IsWindow(parent))
                Native.RedrawWindow(parent, IntPtr.Zero, IntPtr.Zero,
                    Native.RDW_INVALIDATE | Native.RDW_ERASE | Native.RDW_ALLCHILDREN | Native.RDW_UPDATENOW);
        }
        catch { }
    }

    /// <summary>Re-applies the current Windows wallpaper so no stale video frame stays on the desktop after exit.</summary>
    public static void ReapplySystemWallpaper()
    {
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            if (Native.SystemParametersInfoGet(Native.SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0))
            {
                string path = sb.ToString();
                if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
                    Native.SystemParametersInfoSet(Native.SPI_SETDESKWALLPAPER, 0, path, 0);
            }
        }
        catch (Exception ex) { Logger.Log("ReapplySystemWallpaper failed", ex); }
    }
}

/// <summary>Shows/hides the desktop icon list view.</summary>
internal static class DesktopIcons
{
    private static IntPtr FindListView()
    {
        IntPtr progman = Native.FindWindow("Progman", null);
        IntPtr defView = progman != IntPtr.Zero ? Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) : IntPtr.Zero;
        if (defView != IntPtr.Zero)
        {
            IntPtr lv = Native.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
            if (lv != IntPtr.Zero) return lv;
        }

        IntPtr found = IntPtr.Zero;
        Native.EnumWindows((h, _) =>
        {
            IntPtr dv = Native.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (dv == IntPtr.Zero) return true;
            found = Native.FindWindowEx(dv, IntPtr.Zero, "SysListView32", null);
            return found == IntPtr.Zero;
        }, IntPtr.Zero);
        return found;
    }

    public static bool? IsVisible()
    {
        IntPtr lv = FindListView();
        return lv == IntPtr.Zero ? null : Native.IsWindowVisible(lv);
    }

    public static void SetVisible(bool visible)
    {
        IntPtr lv = FindListView();
        if (lv == IntPtr.Zero)
        {
            Logger.Log("DesktopIcons: list view not found");
            return;
        }
        Native.ShowWindow(lv, visible ? Native.SW_SHOW : Native.SW_HIDE);
    }
}
