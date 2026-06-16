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
    private readonly Panel _dimOverlay;
    private double _dim;

    public DesktopHost()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = false;
        BackColor = Color.Black;
        StartPosition = FormStartPosition.Manual;
        var b = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Bounds = b;

        // تاریکی مستقل از VLC. این overlay روی پنجرهٔ والپیپر می‌نشیند تا حتی با wingdi هم کار کند.
        _dimOverlay = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Visible = false,
            Enabled = false
        };
        _dimOverlay.Paint += (_, e) =>
        {
            int alpha = Math.Clamp((int)(_dim * 255), 0, 220);
            if (alpha <= 0) return;
            using var brush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
            e.Graphics.FillRectangle(brush, _dimOverlay.ClientRectangle);
        };
        Controls.Add(_dimOverlay);

        // صفحهٔ وضعیت اولیه. بعد از شروع پخش دیگر در حالت pause/قطع روی ویدیو نمی‌آید تا فریم آخر باقی بماند.
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

    public void BindPlayback(PlaybackEngine engine)
    {
        engine.SetVideoHandle(Handle);
    }

    public void SetDim(double dim)
    {
        if (IsDisposed) return;
        if (IsHandleCreated && InvokeRequired) { BeginInvoke(new Action(() => SetDim(dim))); return; }
        _dim = Math.Clamp(dim, 0, 0.85);
        _dimOverlay.Visible = _dim > 0.001;
        if (_dimOverlay.Visible)
        {
            _dimOverlay.BringToFront();
            if (_statusLabel.Visible) _statusLabel.BringToFront();
        }
        _dimOverlay.Invalidate();
        TvDesk.Logger.Log($"Desktop dim set = {_dim:0.00}");
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
        if (_dimOverlay.Visible) _dimOverlay.BringToFront();
    }
}
