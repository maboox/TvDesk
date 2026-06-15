using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using TvDesk.Behaviors;
using TvDesk.Interop;
using TvDesk.Playback;
using TvDesk.Settings;
using TvDesk.Sources;
using TvDesk.Tray;
using TvDesk.UI;

namespace TvDesk;

/// <summary>
/// هماهنگ‌کننده‌ی مرکزی اپ: پنجره‌ی والپیپر، پنل کنترل، تری، رفتارهای هوشمند و هات‌کی‌ها.
/// </summary>
public sealed class AppController : IDisposable
{
    public static AppController Instance { get; private set; } = null!;

    public AppSettings Settings { get; }
    public PlaybackEngine Playback { get; }
    public ChannelLibrary Library { get; }

    private DesktopWindow? _desktop;
    private ControlWindow? _control;
    private TrayIconManager? _tray;
    private FocusFullscreenWatcher? _watcher;
    private HotkeyManager? _hotkeys;

    public AppController()
    {
        Instance = this;
        Settings = SettingsStore.Load();
        Playback = new PlaybackEngine();
        Library = new ChannelLibrary();
    }

    public async void Start()
    {
        // پنجره‌ی والپیپر پشت آیکون‌های دسکتاپ
        _desktop = new DesktopWindow();
        _desktop.Show();
        _desktop.BindPlayback(Playback);
        _desktop.AttachToDesktop();
        _desktop.SetDim(Settings.WallpaperDim);

        Playback.SetVolume(Settings.Volume);
        Playback.SetMuted(Settings.Muted);

        _control = new ControlWindow();

        _tray = new TrayIconManager();
        _tray.Initialize();

        _watcher = new FocusFullscreenWatcher();
        _watcher.Start();

        _hotkeys = new HotkeyManager();
        _hotkeys.Register();

        AutoStartManager.Apply(Settings.AutoStart);

        // بارگذاری کتابخانه‌ی کانال‌ها (IPTV اپن‌سورس + تلوبیون)
        await Library.LoadAsync(Settings.PlaylistRefreshDays);
        _control.PopulateChannels(Library.Channels);

        // ادامه‌ی آخرین کانال یا اولین کانال
        if (!string.IsNullOrWhiteSpace(Settings.LastChannelUrl))
            await PlayAsync(Settings.LastChannelUrl!, Settings.LastChannelName ?? "");
        else if (Library.Channels.Count > 0)
            await PlayAsync(Library.Channels[0].Url, Library.Channels[0].Name);
    }

    public async Task PlayAsync(string input, string name)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        try
        {
            string playable = await SourceResolver.ResolveAsync(input, Settings.Quality);
            Playback.Play(playable);
            Settings.LastChannelUrl = input;
            Settings.LastChannelName = name;
            SettingsStore.Save(Settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Play failed for {input}: {ex.Message}");
        }
    }

    public async void ResumeLast()
    {
        if (!string.IsNullOrWhiteSpace(Settings.LastChannelUrl))
            await PlayAsync(Settings.LastChannelUrl!, Settings.LastChannelName ?? "");
    }

    public void ShowControl()
    {
        if (_control == null) return;
        _control.Show();
        _control.WindowState = WindowState.Normal;
        _control.Activate();
    }

    public void ToggleDesktopIcons() => DesktopIcons.Toggle();

    public void SetVolume(int v)
    {
        Playback.SetVolume(v);
        Settings.Volume = v;
        SettingsStore.Save(Settings);
    }

    public void ToggleMute()
    {
        Settings.Muted = !Settings.Muted;
        Playback.SetMuted(Settings.Muted);
        SettingsStore.Save(Settings);
    }

    public void SetDim(double dim)
    {
        Settings.WallpaperDim = dim;
        _desktop?.SetDim(dim);
        SettingsStore.Save(Settings);
    }

    public void ToggleFavorite(Channel c)
    {
        c.IsFavorite = !c.IsFavorite;
        if (c.IsFavorite)
        {
            if (!Settings.FavoriteUrls.Contains(c.Url)) Settings.FavoriteUrls.Add(c.Url);
        }
        else
        {
            Settings.FavoriteUrls.Remove(c.Url);
        }
        SettingsStore.Save(Settings);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        Playback.Dispose();
        SettingsStore.Save(Settings);
    }
}
