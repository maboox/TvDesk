using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TvDesk.Interop;

/// <summary>توابع Win32 برای نشاندن پنجرهٔ TvDesk روی دسکتاپ.</summary>
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
    static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc cb, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? win);

    [DllImport("user32.dll")]
    static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int left, top, right, bottom; }

    const int GWL_STYLE = -16;
    const int GWL_EXSTYLE = -20;
    const long WS_CHILD = 0x40000000L;
    const long WS_POPUP = 0x80000000L;
    const long WS_VISIBLE = 0x10000000L;
    const long WS_EX_APPWINDOW = 0x00040000L;
    const long WS_EX_TOOLWINDOW = 0x00000080L;
    const long WS_EX_NOACTIVATE = 0x08000000L;

    const int SW_SHOW = 5;
    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOACTIVATE = 0x0010;
    const uint SWP_FRAMECHANGED = 0x0020;
    const uint SWP_SHOWWINDOW = 0x0040;

    static readonly IntPtr HWND_TOP = IntPtr.Zero;

    static IntPtr GetWindowLongPtr(IntPtr hWnd, int index)
        => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, index) : new IntPtr(GetWindowLong32(hWnd, index));

    static void SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(hWnd, index, value);
        else SetWindowLong32(hWnd, index, value.ToInt32());
    }

    static string ClassOf(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    static string RectOf(IntPtr h)
    {
        if (!GetWindowRect(h, out RECT r)) return "rect=?";
        return $"({r.left},{r.top})-({r.right},{r.bottom})";
    }

    public static void LogDesktopTree(string phase)
    {
        try
        {
            IntPtr progman = FindWindow("Progman", null);
            TvDesk.Logger.Log($"--- Desktop tree {phase} (Progman={progman}) ---");
            if (progman == IntPtr.Zero) return;
            EnumChildWindows(progman, (h, _) =>
            {
                TvDesk.Logger.Log($"  {h} '{ClassOf(h)}' parent={GetParent(h)} vis={IsWindowVisible(h)} {RectOf(h)}");
                return true;
            }, IntPtr.Zero);
            TvDesk.Logger.Log("--- end tree ---");
        }
        catch (Exception ex) { TvDesk.Logger.Log("LogDesktopTree", ex); }
    }

    static IntPtr GetClassicWorkerW()
    {
        IntPtr progman = FindWindow("Progman", null);
        TvDesk.Logger.Log($"Progman = {progman}");
        if (progman == IntPtr.Zero) return IntPtr.Zero;

        SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0x0000, 1000, out _);
        SendMessageTimeout(progman, 0x052C, new IntPtr(0x0000000D), new IntPtr(0x00000001), 0x0000, 1000, out _);

        IntPtr workerw = IntPtr.Zero;
        EnumWindows((tophandle, _) =>
        {
            IntPtr shellView = FindWindowEx(tophandle, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView != IntPtr.Zero)
            {
                IntPtr sibling = FindWindowEx(IntPtr.Zero, tophandle, "WorkerW", null);
                if (sibling != IntPtr.Zero) workerw = sibling;
            }
            return true;
        }, IntPtr.Zero);
        if (workerw != IntPtr.Zero) TvDesk.Logger.Log($"WorkerW classic sibling = {workerw}");
        return workerw;
    }

    /// <summary>
    /// Adaptive attach:
    /// - Windows 10 / older Windows 11 often expose a classic top-level WorkerW behind desktop icons.
    /// - Windows 11 24H2 can expose WorkerW as a child of Progman; parenting there can become invisible.
    /// In that case we use Progman and z-order relative to SHELLDLL_DefView.
    /// </summary>
    public static bool AttachToDesktop(IntPtr myWindowHandle)
    {
        IntPtr progman = FindWindow("Progman", null);
        TvDesk.Logger.Log($"AttachToDesktop: hwnd={myWindowHandle}, Progman={progman}");
        if (progman == IntPtr.Zero) return false;

        IntPtr classicWorkerW = GetClassicWorkerW();
        LogDesktopTree("before attach");

        long style = GetWindowLongPtr(myWindowHandle, GWL_STYLE).ToInt64();
        long exStyle = GetWindowLongPtr(myWindowHandle, GWL_EXSTYLE).ToInt64();
        TvDesk.Logger.Log($"Before attach style=0x{style:X}, ex=0x{exStyle:X}, parent={GetParent(myWindowHandle)}, vis={IsWindowVisible(myWindowHandle)} {RectOf(myWindowHandle)}");

        style &= ~WS_POPUP;
        style |= WS_CHILD | WS_VISIBLE;
        exStyle &= ~WS_EX_APPWINDOW;
        exStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        SetWindowLongPtr(myWindowHandle, GWL_STYLE, new IntPtr(style));
        SetWindowLongPtr(myWindowHandle, GWL_EXSTYLE, new IntPtr(exStyle));

        // Classic WorkerW path: best for Windows 10 / older Windows 11.
        if (classicWorkerW != IntPtr.Zero && GetParent(classicWorkerW) == IntPtr.Zero)
        {
            IntPtr oldParent = SetParent(myWindowHandle, classicWorkerW);
            TvDesk.Logger.Log($"Attach route=classic WorkerW; workerw={classicWorkerW}; oldParent={oldParent}; newParent={GetParent(myWindowHandle)}");
            SetWindowPos(myWindowHandle, HWND_TOP, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
        }
        else
        {
            // Windows 11 24H2 fallback that worked on the user's build 26200.
            IntPtr oldParent = SetParent(myWindowHandle, progman);
            TvDesk.Logger.Log($"Attach route=Progman fallback; oldParent={oldParent}; newParent={GetParent(myWindowHandle)}");
            SetWindowPos(myWindowHandle, HWND_TOP, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
        }

        ShowWindow(myWindowHandle, SW_SHOW);
        UpdateWindow(myWindowHandle);
        TvDesk.Logger.Log($"After attach style=0x{GetWindowLongPtr(myWindowHandle, GWL_STYLE).ToInt64():X}, ex=0x{GetWindowLongPtr(myWindowHandle, GWL_EXSTYLE).ToInt64():X}, parent={GetParent(myWindowHandle)}, vis={IsWindowVisible(myWindowHandle)} {RectOf(myWindowHandle)}");
        LogDesktopTree("after attach");
        return true;
    }

    public static void PlaceBehindDesktopIcons(IntPtr hWnd)
    {
        try
        {
            IntPtr progman = FindWindow("Progman", null);
            IntPtr parent = GetParent(hWnd);
            IntPtr defView = progman != IntPtr.Zero ? FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) : IntPtr.Zero;

            // If TvDesk is parented to Progman (Win11 24H2 fallback), z-order relative to DefView.
            if (progman != IntPtr.Zero && parent == progman && defView != IntPtr.Zero)
            {
                SetWindowPos(hWnd, defView, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
                TvDesk.Logger.Log($"TvDesk placed behind desktop icons (Progman route): hwnd={hWnd}, defView={defView}");
            }
            else
            {
                // If parented to WorkerW (Win10/classic), keep it top within that WorkerW.
                SetWindowPos(hWnd, HWND_TOP, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
                TvDesk.Logger.Log($"TvDesk placed HWND_TOP within parent={parent}: hwnd={hWnd}");
            }
            ShowWindow(hWnd, SW_SHOW);
            UpdateWindow(hWnd);
        }
        catch (Exception ex) { TvDesk.Logger.Log("PlaceBehindDesktopIcons failed", ex); }
    }

    public static void SetBounds(IntPtr hWnd, int x, int y, int w, int h)
    {
        SetWindowPos(hWnd, HWND_TOP, x, y, w, h, SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
        PlaceBehindDesktopIcons(hWnd);
        TvDesk.Logger.Log($"SetBounds hwnd={hWnd} -> {x},{y},{w},{h}; insertAfter=behind-icons; vis={IsWindowVisible(hWnd)} {RectOf(hWnd)} parent={GetParent(hWnd)}");
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

    public static bool Toggle()
    {
        IntPtr lv = GetDesktopListView();
        if (lv == IntPtr.Zero) { TvDesk.Logger.Log("DesktopIcons.Toggle: list view not found"); return false; }
        bool visible = IsWindowVisible(lv);
        bool newVisible = !visible;
        ShowWindow(lv, newVisible ? SW_SHOW : SW_HIDE);
        TvDesk.Logger.Log($"DesktopIcons.Toggle: {visible} -> {newVisible}, hwnd={lv}");
        return newVisible;
    }

    public static void SetVisible(bool visible)
    {
        IntPtr lv = GetDesktopListView();
        if (lv == IntPtr.Zero) { TvDesk.Logger.Log("DesktopIcons.SetVisible: list view not found"); return; }
        ShowWindow(lv, visible ? SW_SHOW : SW_HIDE);
        TvDesk.Logger.Log($"DesktopIcons.SetVisible: {visible}, hwnd={lv}");
    }
}
