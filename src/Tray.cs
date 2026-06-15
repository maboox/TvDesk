using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TvDesk.Tray;

/// <summary>آیکون سینی سیستم با منوی دسترسی سریع.</summary>
public sealed class TrayIconManager : IDisposable
{
    private NotifyIcon? _icon;

    public void Initialize()
    {
        _icon = new NotifyIcon
        {
            Text = "TvDesk",
            Visible = true,
            Icon = LoadIcon()
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("باز کردن TvDesk", null, (_, _) => AppController.Instance.ShowControl());
        menu.Items.Add("هاید/آنهاید آیکون‌ها", null, (_, _) => AppController.Instance.ToggleDesktopIcons());
        menu.Items.Add("میوت/صدا", null, (_, _) => AppController.Instance.ToggleMute());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("خروج", null, (_, _) => System.Windows.Application.Current.Shutdown());
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => AppController.Instance.ShowControl();
    }

    private static Icon LoadIcon()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "icon.ico");
            if (File.Exists(path)) return new Icon(path);
        }
        catch { }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_icon != null) { _icon.Visible = false; _icon.Dispose(); }
    }
}
