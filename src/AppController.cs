using System;
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

/// <summary>هماهنگ‌کنندهٔ مرکزی اپ: والپیپر، پنل کنترل، تری، رفتارهای هوشمند و هات‌کی‌ها.</summary>
public sealed class AppController : IDisposable
{
    public static AppController Instance { get; private set; } = null!;

    public AppSettings Settings { get; private set; } = new();
    public PlaybackEngine Playback { get; private set; } = null!;
    public ChannelLibrary Library { get; } = new();

    private DesktopHost? _desktop;
    private ControlWindow? _control;
    private TrayIconManager? _tray;
    private FocusFullscreenWatcher? _watcher;
    private HotkeyManager? _hotkeys;

    public AppController()
    {
        Instance = this;
    }

    public async void Start()
    {
        try
        {
            Logger.Log("Loading settings");
            Settings = SettingsStore.Load();

            Logger.Log("Initializing LibVLC playback engine");
            Playback = new PlaybackEngine();

            Logger.Log("Creating desktop (wallpaper) host");
            _desktop = new DesktopHost();
            _desktop.Show();
            _desktop.AttachToDesktop();   // اول پشت آیکون‌ها ببر
            _desktop.BindPlayback(Playback); // بعد ویدیو را به HWND وصل کن

            Playback.SetVolume(Settings.Volume);
            Playback.SetMuted(Settings.Muted);
            Playback.SetBrightness((float)(1.0 - Math.Clamp(Settings.WallpaperDim, 0, 0.85)));

            Logger.Log("Creating + showing control window");
            _control = new ControlWindow();
            _control.Show();
            _control.Activate();

            Logger.Log("Initializing tray icon");
            _tray = new TrayIconManager();
            _tray.Initialize();

            Logger.Log("Starting focus/fullscreen watcher");
            _watcher = new FocusFullscreenWatcher();
            _watcher.Start();

            Logger.Log("Registering global hotkeys");
            _hotkeys = new HotkeyManager();
            _hotkeys.Register();

            AutoStartManager.Apply(Settings.AutoStart);

            Logger.Log("Loading channel library (IPTV + Telewebion)");
            await Library.LoadAsync(Settings.PlaylistRefreshDays);
            Logger.Log($"Loaded {Library.Channels.Count} channels");
            _control.PopulateChannels(Library.Channels);

            if (!string.IsNullOrWhiteSpace(Settings.LastChannelUrl))
                await PlayAsync(Settings.LastChannelUrl!, Settings.LastChannelName ?? "");
            else if (Library.Channels.Count > 0)
                await PlayAsync(Library.Channels[0].Url, Library.Channels[0].Name);

            Logger.Log("Startup complete");
        }
        catch (Exception ex)
        {
            Logger.Log("AppController.Start", ex);
            try { ShowControl(); } catch { }
        }
    }

    public async Task PlayAsync(string input, string name)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        try
        {
            string playable = await SourceResolver.ResolveAsync(input, Settings.Quality);
            Playback.Play(playable);
            Playback.SetBrightness((float)(1.0 - Math.Clamp(Settings.WallpaperDim, 0, 0.85)));
            Settings.LastChannelUrl = input;
            Settings.LastChannelName = name;
            SettingsStore.Save(Settings);
        }
        catch (Exception ex)
        {
            Logger.Log($"PlayAsync failed for {input}", ex);
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
        Playback?.SetBrightness((float)(1.0 - Math.Clamp(dim, 0, 0.85)));
        SettingsStore.Save(Settings);
    }

    public void SetQuality(string quality)
    {
        Settings.Quality = quality;
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
        Playback?.Dispose();
        try { _desktop?.Close(); _desktop?.Dispose(); } catch { }
        SettingsStore.Save(Settings);
    }
}
