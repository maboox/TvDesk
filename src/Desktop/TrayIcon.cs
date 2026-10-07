using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TvDesk.Core;

namespace TvDesk.Desktop;

/// <summary>Notification-area icon with a dark context menu for quick control.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly AppController _app;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;

    public TrayIcon(AppController app)
    {
        _app = app;
        _menu = new ContextMenuStrip
        {
            Renderer = new ToolStripProfessionalRenderer(new DarkColors()) { RoundedEdges = false },
            ShowImageMargin = false,
            BackColor = Color.FromArgb(28, 33, 43),
            ForeColor = Color.FromArgb(236, 238, 243),
            Font = new Font("Segoe UI", 9.5f),
        };
        _menu.Opening += (_, _) => Rebuild();

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "TvDesk",
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _app.ShowMainWindow();
        };
        Rebuild();
    }

    private static Icon LoadIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (path != null)
            {
                var icon = Icon.ExtractAssociatedIcon(path);
                if (icon != null) return icon;
            }
        }
        catch { }
        return SystemIcons.Application;
    }

    public void RefreshTexts() => Rebuild();

    private void Rebuild()
    {
        _menu.Items.Clear();
        _menu.RightToLeft = Loc.I.IsRtl ? RightToLeft.Yes : RightToLeft.No;

        var current = _app.Current;
        var header = new ToolStripMenuItem(current?.Name ?? Loc.T("np_nothing")) { Enabled = false };
        _menu.Items.Add(header);
        _menu.Items.Add(new ToolStripSeparator());

        bool active = _app.State is not (PlayerState.Idle or PlayerState.Stopped or PlayerState.Error);
        _menu.Items.Add(Item(active ? Loc.T("tray_stop") : Loc.T("tray_play"), () => _app.TogglePlay()));
        _menu.Items.Add(Item(Loc.T("tray_reconnect"), () => _app.Reconnect(), current != null));
        _menu.Items.Add(Item(_app.Settings.Muted ? Loc.T("tray_unmute") : Loc.T("tray_mute"), () => _app.ToggleMute()));
        _menu.Items.Add(Item(_app.IconsHidden ? Loc.T("tray_show_icons") : Loc.T("tray_hide_icons"), () => _app.ToggleDesktopIcons()));

        var favs = _app.FavoriteChannels().Take(20).ToList();
        if (favs.Count > 0)
        {
            var sub = new ToolStripMenuItem(Loc.T("nav_favorites"));
            sub.DropDown.BackColor = _menu.BackColor;
            sub.DropDown.ForeColor = _menu.ForeColor;
            ((ToolStripDropDownMenu)sub.DropDown).ShowImageMargin = false;
            sub.DropDown.RightToLeft = _menu.RightToLeft;
            foreach (var c in favs)
            {
                var ch = c;
                var it = Item(c.Name, () => _app.Play(ch));
                it.Checked = ReferenceEquals(c, current);
                sub.DropDownItems.Add(it);
            }
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(sub);
        }

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Item(Loc.T("tray_open"), () => _app.ShowMainWindow()));
        _menu.Items.Add(Item(Loc.T("tray_exit"), () => _app.Exit()));

        string tip = "TvDesk" + (current != null ? " — " + current.Name : "");
        _icon.Text = tip.Length > 63 ? tip[..63] : tip;
    }

    private ToolStripMenuItem Item(string text, Action action, bool enabled = true)
    {
        var item = new ToolStripMenuItem(text) { Enabled = enabled, ForeColor = Color.FromArgb(236, 238, 243) };
        item.Click += (_, _) =>
        {
            try { action(); } catch (Exception ex) { Logger.Log("Tray action", ex); }
        };
        return item;
    }

    public void ShowBalloon(string title, string text)
    {
        try { _icon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info); } catch { }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        private static readonly Color Bg = Color.FromArgb(28, 33, 43);
        private static readonly Color Hover = Color.FromArgb(44, 51, 66);
        private static readonly Color Line = Color.FromArgb(48, 55, 70);

        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Hover;
        public override Color MenuItemPressedGradientEnd => Hover;
        public override Color MenuItemBorder => Hover;
        public override Color MenuBorder => Line;
        public override Color ToolStripDropDownBackground => Bg;
        public override Color ImageMarginGradientBegin => Bg;
        public override Color ImageMarginGradientMiddle => Bg;
        public override Color ImageMarginGradientEnd => Bg;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Bg;
        public override Color CheckBackground => Hover;
        public override Color CheckSelectedBackground => Hover;
        public override Color CheckPressedBackground => Hover;
    }
}
