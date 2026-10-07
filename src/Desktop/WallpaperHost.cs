using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TvDesk.Core;

namespace TvDesk.Desktop;

public enum OverlayMode { None, Connecting, Error, Frozen, AudioOnly }

public sealed record MonitorInfo(string Key, string Label, Rectangle Bounds, bool Primary);

/// <summary>
/// The wallpaper window: a borderless WinForms form re-parented behind the desktop icons.
/// It contains the VLC video panel and, on top of it, an overlay used for "connecting", errors,
/// audio-only streams and the frozen frame shown while paused.
/// </summary>
public sealed class WallpaperHost : Form
{
    private readonly Panel _video;
    private readonly OverlayPanel _overlay;
    private IntPtr _hwnd;

    public AttachMode Mode { get; private set; }
    public IntPtr ParentHwnd { get; private set; }
    public Rectangle Target { get; private set; }
    public IntPtr VideoHandle { get; }

    public WallpaperHost()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Black;
        Text = "TvDesk Wallpaper";
        Location = new Point(-32000, -32000);
        Size = new Size(16, 16);

        _video = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black, Margin = Padding.Empty };
        _overlay = new OverlayPanel { Dock = DockStyle.Fill, Visible = false };
        Controls.Add(_video);
        Controls.Add(_overlay);
        _overlay.BringToFront();

        // Create the native windows now (off-screen, tiny, never activated); Attach() re-parents it.
        Show();
        _hwnd = Handle;
        VideoHandle = _video.Handle;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= (int)(Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    /// <summary>Attaches behind the desktop icons and covers <paramref name="target"/>. Stays hidden until <see cref="ShowHost"/>.</summary>
    public bool Attach(Rectangle target)
    {
        _hwnd = Handle;
        Mode = DesktopAttacher.Attach(_hwnd, out var parent);
        ParentHwnd = parent;
        Reposition(target);
        Native.ShowWindow(_hwnd, Native.SW_HIDE);
        return Mode != AttachMode.None;
    }

    public void Reposition(Rectangle target)
    {
        Target = target;
        if (ParentHwnd == IntPtr.Zero) return;
        DesktopAttacher.Position(_hwnd, ParentHwnd, target.X, target.Y, target.Width, target.Height);
        Logger.Log($"Wallpaper positioned at {target} (mode {Mode})");
    }

    public bool IsHealthy => DesktopAttacher.IsHealthy(_hwnd, ParentHwnd);

    public bool IsShown => _hwnd != IntPtr.Zero && Native.IsWindowVisible(_hwnd);

    public void ShowHost()
    {
        if (_hwnd == IntPtr.Zero) return;
        Native.ShowWindow(_hwnd, Native.SW_SHOWNOACTIVATE);
        DesktopAttacher.ReassertZOrder(_hwnd, ParentHwnd, Mode);
    }

    public void HideHost()
    {
        if (_hwnd == IntPtr.Zero) return;
        Native.ShowWindow(_hwnd, Native.SW_HIDE);
        DesktopAttacher.RefreshWallpaper(ParentHwnd);
    }

    public void ReassertZOrder() => DesktopAttacher.ReassertZOrder(_hwnd, ParentHwnd, Mode);

    // ------------------------------------------------------------------ overlay

    public void ShowOverlay(OverlayMode mode, string title, string subtitle)
    {
        if (mode == OverlayMode.None) { HideOverlay(); return; }
        _overlay.Set(mode, title, subtitle, null);
        _overlay.Visible = true;
        _overlay.BringToFront();
    }

    /// <summary>Shows a still frame (from a VLC snapshot file). Falls back to a dark card if the file can't be read.</summary>
    public void ShowFrozen(string? imagePath, string title)
    {
        Image? img = null;
        try
        {
            if (imagePath != null && File.Exists(imagePath))
            {
                byte[] bytes = File.ReadAllBytes(imagePath);
                using var ms = new MemoryStream(bytes);
                using var tmp = Image.FromStream(ms);
                img = new Bitmap(tmp);
            }
        }
        catch (Exception ex) { Logger.Log("Loading freeze frame failed", ex); }

        _overlay.Set(OverlayMode.Frozen, title, "", img);
        _overlay.Visible = true;
        _overlay.BringToFront();
    }

    public OverlayMode CurrentOverlay => _overlay.Visible ? _overlay.Mode : OverlayMode.None;

    public void HideOverlay()
    {
        _overlay.Visible = false;
        _overlay.Set(OverlayMode.None, "", "", null);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _overlay.Set(OverlayMode.None, "", "", null);
        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------ monitors

    public static List<MonitorInfo> Monitors()
    {
        var list = new List<MonitorInfo>();
        int i = 1;
        foreach (var s in Screen.AllScreens.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y))
        {
            list.Add(new MonitorInfo(s.DeviceName, $"{i} — {s.Bounds.Width}×{s.Bounds.Height}", s.Bounds, s.Primary));
            i++;
        }
        return list;
    }

    /// <summary>Screen rectangle for the "Monitor" setting: primary | all | device name.</summary>
    public static Rectangle ComputeTarget(string monitor)
    {
        if (monitor == "all") return SystemInformation.VirtualScreen;
        if (monitor != "primary")
        {
            var s = Screen.AllScreens.FirstOrDefault(x => x.DeviceName == monitor);
            if (s != null) return s.Bounds;
        }
        return (Screen.PrimaryScreen ?? Screen.AllScreens[0]).Bounds;
    }
}

/// <summary>Double-buffered overlay painted with GDI+ (no WPF here: this lives inside Explorer's window tree).</summary>
internal sealed class OverlayPanel : Control
{
    private readonly Timer _anim = new() { Interval = 450 };
    private int _tick;
    private Image? _image;
    private string _title = "";
    private string _subtitle = "";

    public OverlayMode Mode { get; private set; }

    public OverlayPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Black;
        _anim.Tick += (_, _) => { _tick++; Invalidate(); };
    }

    public void Set(OverlayMode mode, string title, string subtitle, Image? image)
    {
        var old = _image;
        Mode = mode;
        _title = title ?? "";
        _subtitle = subtitle ?? "";
        _image = image;
        if (!ReferenceEquals(old, image)) old?.Dispose();
        _anim.Enabled = mode == OverlayMode.Connecting;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = ClientRectangle;
        if (r.Width <= 0 || r.Height <= 0) return;

        if (Mode == OverlayMode.Frozen && _image != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(_image, CoverRect(_image.Size, r));
            return;
        }

        // Background: deep navy gradient with a warm glow.
        using (var bg = new LinearGradientBrush(r, Color.FromArgb(14, 17, 26), Color.FromArgb(5, 6, 10), LinearGradientMode.Vertical))
            g.FillRectangle(bg, r);
        using (var path = new GraphicsPath())
        {
            int gw = (int)(r.Width * 0.9), gh = (int)(r.Height * 0.9);
            path.AddEllipse(r.Width / 2 - gw / 2, -gh / 2, gw, gh);
            using var glow = new PathGradientBrush(path)
            {
                CenterColor = Color.FromArgb(48, 255, 106, 61),
                SurroundColors = new[] { Color.FromArgb(0, 255, 106, 61) }
            };
            g.FillPath(glow, path);
        }
        if (Mode == OverlayMode.Frozen) return; // frozen without image: just the backdrop

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        float unit = Math.Max(10f, r.Height / 54f);
        int cx = r.Width / 2, cy = r.Height / 2;

        // Brand mark
        using (var brand = new Font("Segoe UI Semibold", unit * 0.9f, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            string text = "TvDesk";
            var size = TextRenderer.MeasureText(text, brand);
            int bx = cx - size.Width / 2, by = (int)(cy - unit * 5.2f);
            using var dot = new SolidBrush(Color.FromArgb(255, 106, 61));
            g.FillEllipse(dot, bx - unit * 1.1f, by + size.Height / 2f - unit * 0.3f, unit * 0.6f, unit * 0.6f);
            TextRenderer.DrawText(g, text, brand, new Point(bx, by), Color.FromArgb(170, 178, 196), TextFormatFlags.NoPadding);
        }

        // Icon
        string glyph = Mode switch
        {
            OverlayMode.Error => "",
            OverlayMode.AudioOnly => "",
            _ => "",
        };
        using (var icon = new Font("Segoe MDL2 Assets", unit * 3.2f, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            var col = Mode == OverlayMode.Error ? Color.FromArgb(255, 90, 110) : Color.FromArgb(235, 238, 245);
            var rect = new Rectangle(0, (int)(cy - unit * 3.6f), r.Width, (int)(unit * 4.2f));
            TextRenderer.DrawText(g, glyph, icon, rect, col, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        // Title (channel name)
        using (var title = new Font("Segoe UI Semibold", unit * 1.7f, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            var rect = new Rectangle(r.Width / 10, (int)(cy + unit * 1.0f), r.Width * 8 / 10, (int)(unit * 2.8f));
            TextRenderer.DrawText(g, _title, title, rect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        // Subtitle (status) + animated dots while connecting
        string sub = _subtitle;
        if (Mode == OverlayMode.Connecting) sub += new string('.', _tick % 4).PadRight(3);
        using (var subtitle = new Font("Segoe UI", unit * 1.0f, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            var rect = new Rectangle(r.Width / 10, (int)(cy + unit * 3.8f), r.Width * 8 / 10, (int)(unit * 2.0f));
            var col = Mode == OverlayMode.Error ? Color.FromArgb(255, 160, 170) : Color.FromArgb(150, 160, 180);
            TextRenderer.DrawText(g, sub, subtitle, rect, col,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        // Progress bar while connecting
        if (Mode == OverlayMode.Connecting)
        {
            int bw = (int)(unit * 10), bh = Math.Max(3, (int)(unit * 0.22f));
            int bx = cx - bw / 2, by = (int)(cy + unit * 6.4f);
            using var track = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
            g.FillRectangle(track, bx, by, bw, bh);
            int seg = bw / 3;
            int pos = (int)((_tick % 8) / 7f * (bw - seg));
            using var bar = new SolidBrush(Color.FromArgb(255, 106, 61));
            g.FillRectangle(bar, bx + pos, by, seg, bh);
        }
    }

    private static Rectangle CoverRect(Size img, Rectangle box)
    {
        if (img.Width <= 0 || img.Height <= 0) return box;
        double scale = Math.Max((double)box.Width / img.Width, (double)box.Height / img.Height);
        int w = (int)Math.Ceiling(img.Width * scale), h = (int)Math.Ceiling(img.Height * scale);
        return new Rectangle(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _anim.Dispose();
            _image?.Dispose();
            _image = null;
        }
        base.Dispose(disposing);
    }
}
