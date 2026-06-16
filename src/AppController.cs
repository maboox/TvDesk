using System;
using System.Linq;
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

    public event Action<string>? StatusChanged;
    public void SetStatus(string message)
    {
        if (!message.Contains("بافر")) Logger.Log($"STATUS: {message}");
        try { StatusChanged?.Invoke(message); } catch { }
        try
        {
            // نکته: بعد از شروع پخش، دیگر صفحهٔ وضعیت را روی دسکتاپ نمی‌آوریم تا آخرین فریم/ویدیو باقی بماند.
            // برای لود اولیه یا خطاهای قبل از پخش، نمایش وضعیت مجاز است.
            if (message.Contains("در حال پخش") || message.Contains("مکث") || message.Contains("بافر"))
                _desktop?.HideStatus();
            else if (!(Playback?.HasEverPlayed ?? false))
                _desktop?.ShowStatus(message);
        }
        catch { }
    }

    private DesktopHost? _desktop;
    private ControlWindow? _control;
    private TrayIconManager? _tray;
    private FocusFullscreenWatcher? _watcher;
    private HotkeyManager? _hotkeys;

    public AppController() => Instance = this;

    public async void Start()
    {
        try
        {
            Logger.Log("Loading settings");
            Settings = SettingsStore.Load();

            Logger.Log("Initializing LibVLC playback engine");
            Playback = new PlaybackEngine();
            Playback.StatusChanged += SetStatus;

            Logger.Log("Creating desktop (wallpaper) host");
            _desktop = new DesktopHost();
            _desktop.Show();
            _desktop.AttachToDesktop();
            _desktop.BindPlayback(Playback);
            _desktop.SetDim(Settings.WallpaperDim);

            Playback.SetVolume(Settings.Volume);
            Playback.SetMuted(Settings.Muted);

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
            string label = string.IsNullOrWhiteSpace(name) ? input : name;
            SetStatus($"\u23F3 در حال لود کانال: {label}…");
            string playable = await SourceResolver.ResolveAsync(input, Settings.Quality);
            Playback.Play(playable);
            Settings.LastChannelUrl = input;
            Settings.LastChannelName = name;
            SettingsStore.Save(Settings);
        }
        catch (Exception ex)
        {
            SetStatus($"\u2715 خطا در لود کانال: {name}");
            Logger.Log($"PlayAsync failed for {input}", ex);
        }
    }

    public async void ResumeLast()
    {
        if (!string.IsNullOrWhiteSpace(Settings.LastChannelUrl))
            await PlayAsync(Settings.LastChannelUrl!, Settings.LastChannelName ?? "");
    }

    public void PauseForFullscreen()
    {
        Logger.Log("PauseForFullscreen");
        Playback.PauseKeepFrame();
        SetStatus("⏸ مکث برای فول‌اسکرین");
    }

    public void ResumeAfterFullscreen()
    {
        Logger.Log("ResumeAfterFullscreen");
        Playback.Resume();
        SetStatus("▶ ادامهٔ پخش");
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
        Settings.WallpaperDim = Math.Clamp(dim, 0, 0.85);
        _desktop?.SetDim(Settings.WallpaperDim);
        SettingsStore.Save(Settings);
    }

    public void SetQuality(string quality)
    {
        Settings.Quality = quality;
        SettingsStore.Save(Settings);
    }

    public void AddFavoriteLink(string name, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        Settings.FavoriteUrls.Remove(url);
        Settings.FavoriteUrls.Insert(0, url);
        Settings.FavoriteItems.RemoveAll(x => x.Url == url);
        Settings.FavoriteItems.Insert(0, new FavoriteItem { Name = string.IsNullOrWhiteSpace(name) ? url : name, Url = url });
        SettingsStore.Save(Settings);
        _control?.RefreshFavoriteStates();
    }

    public void ToggleFavorite(Channel c)
    {
        if (string.IsNullOrWhiteSpace(c.Url)) return;
        c.IsFavorite = !c.IsFavorite;
        if (c.IsFavorite)
        {
            if (!Settings.FavoriteUrls.Contains(c.Url)) Settings.FavoriteUrls.Add(c.Url);
            if (!Settings.FavoriteItems.Any(x => x.Url == c.Url) && (c.Source == "Custom Favorite" || c.Group == "منتخب‌های دستی"))
                Settings.FavoriteItems.Add(new FavoriteItem { Name = c.Name, Url = c.Url });
        }
        else
        {
            Settings.FavoriteUrls.Remove(c.Url);
            Settings.FavoriteItems.RemoveAll(x => x.Url == c.Url);
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
