using System;
using System.Drawing;
using System.Windows.Forms;
using TvDesk.Interop;
using TvDesk.Playback;

namespace TvDesk.UI;

/// <summary>
/// پنجرهٔ میزبان والپیپر. WinForms + HWND مستقیم برای LibVLC.
/// </summary>
public sealed class DesktopHost : Form
{
    private readonly Label _statusLabel;
    private readonly PictureBox _freezeOverlay;

    public DesktopHost()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = false;
        BackColor = Color.Black;
        StartPosition = FormStartPosition.Manual;
        var b = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Bounds = b;

        _freezeOverlay = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            SizeMode = PictureBoxSizeMode.StretchImage,
            Visible = false,
            Enabled = false
        };
        Controls.Add(_freezeOverlay);

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
        var handle = Handle;
        bool ok = WorkerWHelper.AttachToDesktop(handle);
        TvDesk.Logger.Log($"DesktopHost AttachToDesktop ok={ok} handle={handle}");
        var b = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        WorkerWHelper.SetBounds(handle, 0, 0, b.Width, b.Height);
    }

    public void BindPlayback(PlaybackEngine engine) => engine.SetVideoHandle(Handle);

    /// <summary>فریم فعلی دسکتاپ را به‌صورت overlay نگه می‌دارد؛ stream می‌تواند پشت آن ادامه پیدا کند.</summary>
    public void FreezeFrame()
    {
        if (IsDisposed) return;
        if (IsHandleCreated && InvokeRequired) { BeginInvoke(new Action(FreezeFrame)); return; }
        try
        {
            var rect = RectangleToScreen(ClientRectangle);
            if (rect.Width <= 0 || rect.Height <= 0) return;
            var bmp = new Bitmap(rect.Width, rect.Height);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(rect.Left, rect.Top, 0, 0, rect.Size, CopyPixelOperation.SourceCopy);
            var old = _freezeOverlay.Image;
            _freezeOverlay.Image = bmp;
            old?.Dispose();
            _freezeOverlay.Visible = true;
            _freezeOverlay.BringToFront();
            if (_statusLabel.Visible) _statusLabel.BringToFront();
            TvDesk.Logger.Log("Desktop frame frozen (overlay on, playback continues)");
        }
        catch (Exception ex) { TvDesk.Logger.Log("FreezeFrame failed", ex); }
    }

    public void UnfreezeFrame()
    {
        if (IsDisposed) return;
        if (IsHandleCreated && InvokeRequired) { BeginInvoke(new Action(UnfreezeFrame)); return; }
        _freezeOverlay.Visible = false;
        var old = _freezeOverlay.Image;
        _freezeOverlay.Image = null;
        old?.Dispose();
        TvDesk.Logger.Log("Desktop frame unfrozen");
    }

    public void ShowStatus(string text)
    {
        if (IsDisposed) return;
        if (IsHandleCreated && InvokeRequired) { BeginInvoke(new Action(() => ShowStatus(text))); return; }
        _statusLabel.Text = text;
        _statusLabel.Visible = true;
        _statusLabel.BringToFront();
    }

    public void HideStatus()
    {
        if (IsDisposed) return;
        if (IsHandleCreated && InvokeRequired) { BeginInvoke(new Action(HideStatus)); return; }
        _statusLabel.Visible = false;
        if (_freezeOverlay.Visible) _freezeOverlay.BringToFront();
    }
}
