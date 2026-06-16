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
    private readonly Label _statusLabel;

    public DesktopHost()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = false;
        BackColor = Color.Black;
        StartPosition = FormStartPosition.Manual;
        var b = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Bounds = b;

        // صفحهٔ تیرهٔ وضعیت/لودینگ — وقتی چیزی پخش نمی‌شود روی کل دسکتاپ دیده می‌شود
        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.FromArgb(11, 13, 18),
            ForeColor = Color.FromArgb(170, 190, 255),
            Font = new Font("Segoe UI", 22f, FontStyle.Bold),
            Text = "\uD83D\uDCFA  TvDesk\r\n\r\nدر حال راه‌اندازی…",
            Visible = true
        };
        Controls.Add(_statusLabel);
        _statusLabel.BringToFront();
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

    /// <summary>نمایش صفحهٔ تیره + متن وضعیت روی دسکتاپ (وقتی چیزی پخش نمی‌شود).</summary>
    public void ShowStatus(string text)
    {
        if (IsDisposed) return;
        if (IsHandleCreated && InvokeRequired) { BeginInvoke(new Action(() => ShowStatus(text))); return; }
        _statusLabel.Text = text;
        _statusLabel.Visible = true;
        _statusLabel.BringToFront();
    }

    /// <summary>پنهان کردن صفحهٔ تیره تا ویدیو دیده شود.</summary>
    public void HideStatus()
    {
        if (IsDisposed) return;
        if (IsHandleCreated && InvokeRequired) { BeginInvoke(new Action(HideStatus)); return; }
        _statusLabel.Visible = false;
    }
}
