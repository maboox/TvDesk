using System;
using System.Drawing;
using System.Windows.Forms;
using TvDesk.Interop;
using TvDesk.Playback;

namespace TvDesk.UI;

/// <summary>
/// پنجرهٔ میزبان والپیپر. از WinForms استفاده می‌کند چون LibVLC روی HWND سادهٔ WinForms
/// بسیار مطمئن‌تر از VideoView وپ‌اف (مشکل airspace) پشت آیکون‌ها رندر می‌شود.
/// </summary>
public sealed class DesktopHost : Form
{
    public DesktopHost()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = false;
        BackColor = Color.Black;
        StartPosition = FormStartPosition.Manual;
        var b = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Bounds = b;
    }

    public void AttachToDesktop()
    {
        var handle = Handle; // دسترسی به Handle باعث ساخت پنجره می‌شود
        bool ok = WorkerWHelper.AttachToDesktop(handle);
        TvDesk.Logger.Log($"DesktopHost AttachToDesktop ok={ok} handle={handle}");
        var b = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        // پس از reparent مختصات نسبت به WorkerW است → صفحهٔ اصلی از (0,0)
        WorkerWHelper.SetBounds(handle, 0, 0, b.Width, b.Height);
    }

    public void BindPlayback(PlaybackEngine engine)
    {
        engine.SetVideoHandle(Handle);
    }
}
