using System;
using System.Runtime.InteropServices;

namespace TvDesk.Interop;

/// <summary>ترفند WorkerW برای نشاندن پنجرهٔ ویدیو پشت آیکون‌های دسکتاپ.</summary>
public static class WorkerWHelper
{
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam,
        IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? win);

    [DllImport("user32.dll")]
    static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public static IntPtr GetWorkerW()
    {
        IntPtr progman = FindWindow("Progman", null);
        TvDesk.Logger.Log($"Progman = {progman}");
        // درخواست ساخت WorkerW پشت آیکون‌ها
        SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0x0000, 1000, out _);
        SendMessageTimeout(progman, 0x052C, new IntPtr(0x0000000D), new IntPtr(0x00000001), 0x0000, 1000, out _);

        IntPtr workerw = IntPtr.Zero;
        EnumWindows((tophandle, _) =>
        {
            IntPtr shellView = FindWindowEx(tophandle, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView != IntPtr.Zero)
                workerw = FindWindowEx(IntPtr.Zero, tophandle, "WorkerW", null);
            return true;
        }, IntPtr.Zero);

        return workerw;
    }

    /// <summary>پنجره را پشت آیکون‌ها می‌برد. اگر WorkerW پیدا نشد، fallback به Progman.</summary>
    public static bool AttachToDesktop(IntPtr myWindowHandle)
    {
        IntPtr workerw = GetWorkerW();
        TvDesk.Logger.Log($"WorkerW = {workerw}");
        if (workerw != IntPtr.Zero)
        {
            SetParent(myWindowHandle, workerw);
            return true;
        }

        IntPtr progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            TvDesk.Logger.Log("WorkerW not found → fallback: والد کردن زیر Progman");
            SetParent(myWindowHandle, progman);
            return true;
        }

        TvDesk.Logger.Log("نه WorkerW و نه Progman پیدا نشد — اتصال والپیپر ناموفق");
        return false;
    }

    public static void SetBounds(IntPtr hWnd, int x, int y, int w, int h)
    {
        const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
        SetWindowPos(hWnd, IntPtr.Zero, x, y, w, h, SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }
}

/// <summary>هاید/آنهاید آیکون‌های دسکتاپ با toggle کردن لایهٔ SysListView32.</summary>
public static class DesktopIcons
{
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
    [DllImport("user32.dll")]
    static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? win);
    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    const int SW_HIDE = 0;
    const int SW_SHOW = 5;

    static IntPtr GetDesktopListView()
    {
        IntPtr progman = FindWindow("Progman", null);
        IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView != IntPtr.Zero)
        {
            IntPtr lv = FindWindowEx(defView, IntPtr.Zero, "SysListView32", "FolderView");
            if (lv != IntPtr.Zero) return lv;
        }

        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            IntPtr dv = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (dv != IntPtr.Zero)
            {
                IntPtr lv = FindWindowEx(dv, IntPtr.Zero, "SysListView32", "FolderView");
                if (lv != IntPtr.Zero) { found = lv; return false; }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static void Toggle()
    {
        IntPtr lv = GetDesktopListView();
        if (lv == IntPtr.Zero) return;
        bool visible = IsWindowVisible(lv);
        ShowWindow(lv, visible ? SW_HIDE : SW_SHOW);
    }

    public static void SetVisible(bool visible)
    {
        IntPtr lv = GetDesktopListView();
        if (lv == IntPtr.Zero) return;
        ShowWindow(lv, visible ? SW_SHOW : SW_HIDE);
    }
}
