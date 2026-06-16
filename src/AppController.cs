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
            if (message.Contains("در حال پخش") || message.Contains("ادامه") || message.Contains("مکث") || message.Contains("بافر"))
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

            await ReloadLibraryAsync();

            if (Settings.OnboardingComplete)
            {
                if (!string.IsNullOrWhiteSpace(Settings.LastChannelUrl))
                    await PlayAsync(Settings.LastChannelUrl!, Settings.LastChannelName ?? "");
                else if (Library.Channels.Count > 0)
                    await PlayAsync(Library.Channels[0].Url, Library.Channels[0].Name);
            }

            Logger.Log("Startup complete");
        }
        catch (Exception ex)
        {
            Logger.Log("AppController.Start", ex);
            try { ShowControl(); } catch { }
        }
    }

    public async Task ReloadLibraryAsync()
    {
        Logger.Log("Loading channel library");
        await Library.LoadAsync(Settings);
        Logger.Log($"Loaded {Library.Channels.Count} channels");
        _control?.PopulateChannels(Library.Channels);
        SetStatus($"کتابخانه آماده است: {Library.Channels.Count} کانال");
    }

    public async Task PlayAsync(string input, string name)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        try
        {
            string label = string.IsNullOrWhiteSpace(name) ? input : name;
            SetStatus($"\u23F3 در حال لود: {label}…");
            string playable = await SourceResolver.ResolveAsync(input, Settings.Quality);
            Playback.Play(playable);
            Settings.LastChannelUrl = input;
            Settings.LastChannelName = name;
            SettingsStore.Save(Settings);
        }
        catch (Exception ex)
        {
            SetStatus($"\u2715 خطا در لود: {name}");
            Logger.Log($"PlayAsync failed for {input}", ex);
        }
    }

    public void PauseVideo(string reason)
    {
        Logger.Log($"PauseVideo: {reason}");
        Playback.PauseKeepFrame();
        SetStatus($"⏸ مکث تصویر: {reason}");
    }

    public void ResumeVideo(string reason)
    {
        Logger.Log($"ResumeVideo: {reason}");
        Playback.Resume();
        SetStatus($"▶ ادامه تصویر: {reason}");
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

    public void AddPlaylistSource(string name, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        Settings.CustomPlaylistSources.RemoveAll(x => x.Url == url);
        Settings.CustomPlaylistSources.Add(new FavoriteItem { Name = string.IsNullOrWhiteSpace(name) ? "Custom IPTV" : name, Url = url });
        SettingsStore.Save(Settings);
        Logger.Log($"Added custom playlist source: {name} {url}");
    }

    public void ToggleFavorite(Channel c)
    {
        if (string.IsNullOrWhiteSpace(c.Url)) return;
        c.IsFavorite = !c.IsFavorite;
        if (c.IsFavorite)
        {
            if (!Settings.FavoriteUrls.Contains(c.Url)) Settings.FavoriteUrls.Add(c.Url);
            if (!Settings.FavoriteItems.Any(x => x.Url == c.Url) && c.Source == "Custom Favorite")
                Settings.FavoriteItems.Add(new FavoriteItem { Name = c.Name, Url = c.Url });
        }
        else
        {
            Settings.FavoriteUrls.Remove(c.Url);
            Settings.FavoriteItems.RemoveAll(x => x.Url == c.Url && c.Source == "Custom Favorite");
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
