using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TvDesk.Core;
using TvDesk.Desktop;
using TvDesk.Playback;
using TvDesk.Sources;
using TvDesk.UI;

namespace TvDesk;

public enum PlayerState { Idle, Resolving, Connecting, Buffering, Playing, Paused, Stopped, Reconnecting, Error }

/// <summary>
/// Owns everything: settings, library, playback engine, wallpaper window, tray, hotkeys and the smart-pause watcher.
/// All public members are used from the UI thread.
/// </summary>
public sealed class AppController : IDisposable
{
    public static AppController Instance { get; private set; } = null!;

    public AppSettings Settings { get; private set; } = new();
    public Catalog Catalog { get; private set; } = new();
    public ChannelLibrary Library { get; } = new();
    public HealthStore Health { get; } = new();

    private readonly Dispatcher _ui;
    private PlaybackEngine? _engine;
    private WallpaperHost? _host;
    private TrayIcon? _tray;
    private ShellWindow? _shell;
    private ActivityWatcher? _activity;
    private MainWindow? _window;
    private MainViewModel? _vm;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _watchdog;
    private bool _disposed;

    // ---------------- player state ----------------
    public Channel? Current { get; private set; }
    public PlayerState State { get; private set; } = PlayerState.Idle;
    public string StatusDetail { get; private set; } = "";
    public bool IconsHidden { get; private set; }
    public bool IsLibraryLoading { get; private set; }
    public AutoAction ActiveAutoAction => _activity?.Current ?? AutoAction.None;

    public event Action? PlayerChanged;
    public event Action? LibraryChanged;
    public event Action? FavoritesChanged;

    private int _expectedGen;
    private bool _userStopped;
    private bool _autoPaused;
    private bool _autoMuted;
    private int _retry;
    private bool _playedThisChannel;
    private bool _hasVout;
    private DateTime _stateSince = DateTime.UtcNow;
    private DateTime _playingSince = DateTime.UtcNow;
    private long _lastTime = -1;
    private DateTime _lastTimeChange = DateTime.UtcNow;
    private CancellationTokenSource? _playCts;
    private DispatcherTimer? _retryTimer;
    private DateTime _lastRebuild = DateTime.MinValue;
    private CancellationTokenSource? _libraryCts;

    public AppController()
    {
        Instance = this;
        _ui = Dispatcher.CurrentDispatcher;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SettingsStore.Save(Settings); };
        _watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _watchdog.Tick += (_, _) => Watchdog();
    }

    // ======================================================================================= startup

    public async void Start(bool launchedAtStartup)
    {
        try
        {
            Settings = SettingsStore.Load();
            Loc.I.SetLanguage(Settings.Language);
            Http.Configure(Settings.Proxy, Settings.CustomProxy);
            Catalog = Catalog.Load();
            Health.Load();

            _vm = new MainViewModel(this);
            _window = new MainWindow(_vm);
            if (!launchedAtStartup || !Settings.OnboardingComplete)
            {
                _window.Show();
                _window.Activate();
            }

            CreateHost();
            CreateEngine();

            _tray = new TrayIcon(this);

            _shell = new ShellWindow();
            _shell.ExplorerRestarted += () => Delay(2500, () => RebuildHost("Explorer restarted", force: true));
            _shell.DisplayChanged += () => Delay(1200, OnDisplayChanged);
            _shell.Hotkey += OnHotkey;
            _shell.RegisterHotkey(1, ShellWindow.MOD_CONTROL | ShellWindow.MOD_ALT, 0x44); // D: desktop icons
            _shell.RegisterHotkey(2, ShellWindow.MOD_CONTROL | ShellWindow.MOD_ALT, 0x4D); // M: mute
            _shell.RegisterHotkey(3, ShellWindow.MOD_CONTROL | ShellWindow.MOD_ALT, 0x54); // T: open TvDesk
            _shell.RegisterHotkey(4, ShellWindow.MOD_CONTROL | ShellWindow.MOD_ALT, 0x50); // P: play / stop
            _shell.RegisterHotkey(5, ShellWindow.MOD_CONTROL | ShellWindow.MOD_ALT, 0x27); // →: next favourite
            _shell.RegisterHotkey(6, ShellWindow.MOD_CONTROL | ShellWindow.MOD_ALT, 0x25); // ←: previous favourite

            _activity = new ActivityWatcher(() => Settings, () => _host?.Target);
            _activity.ActionChanged += OnAutoAction;
            _activity.Start();

            IconsHidden = DesktopIcons.IsVisible() == false;
            AutoStartManager.Apply(Settings.AutoStart);
            _watchdog.Start();

            if (Settings.OnboardingComplete)
            {
                await ReloadLibraryAsync(false);
                if (Settings.ResumeOnStart && !string.IsNullOrWhiteSpace(Settings.LastUrl))
                {
                    var c = FindChannel(Settings.LastUrl!) ?? AdHocChannel(Settings.LastUrl!, Settings.LastName);
                    Play(c);
                }
            }

            _ = MaintainYtDlpAsync();
            Logger.Log("Startup complete");
        }
        catch (Exception ex)
        {
            // Never stay alive invisibly: tell the user and quit (otherwise the next launch only finds "already running").
            Logger.Log("Startup failed", ex);
            try
            {
                MessageBox.Show("TvDesk could not start / TvDesk اجرا نشد:\n\n" + ex.Message + "\n\nLog: " + Logger.FilePath,
                    "TvDesk", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            Exit();
        }
    }

    private void CreateHost()
    {
        _host = new WallpaperHost();
        var target = WallpaperHost.ComputeTarget(Settings.Monitor);
        if (!_host.Attach(target))
            Logger.Log("Wallpaper attach failed — will retry from the watchdog");
        _lastRebuild = DateTime.UtcNow;
    }

    private void CreateEngine()
    {
        _engine = new PlaybackEngine(Settings.HardwareDecoding, Settings.VideoOutput);
        _engine.Event += (gen, ev, value) => _ui.BeginInvoke(new Action(() => OnEngineEvent(gen, ev, value)));
        if (_host != null)
        {
            _engine.SetVideoWindow(_host.VideoHandle);
            _engine.SetFit(Settings.Fit, _host.Target.Width, _host.Target.Height);
        }
        _engine.SetVolume(Settings.Volume);
        ApplyMute();
        if (!_engine.Available)
            _vm?.Toast(Loc.T("toast_vlc_failed"));
    }

    private async Task MaintainYtDlpAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20));
            if (await YtDlp.EnsureAsync(CancellationToken.None) == null) return;
            if ((DateTime.UtcNow - Settings.YtDlpLastUpdate).TotalDays >= 3)
            {
                bool ok = await YtDlp.UpdateAsync();
                if (ok)
                {
                    Settings.YtDlpLastUpdate = DateTime.UtcNow;
                    SaveSoon();
                }
            }
        }
        catch (Exception ex) { Logger.Log("yt-dlp maintenance", ex); }
    }

    // ======================================================================================= library

    public async Task ReloadLibraryAsync(bool force)
    {
        _libraryCts?.Cancel();
        var cts = _libraryCts = new CancellationTokenSource();
        IsLibraryLoading = true;
        LibraryChanged?.Invoke();
        try
        {
            await Library.LoadAsync(Settings, Catalog, Health, force, cts.Token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { Logger.Log("Library load failed", ex); }
        if (cts != _libraryCts) return;

        IsLibraryLoading = false;
        // Re-link the playing channel to the fresh instance so its row shows "playing".
        if (Current != null)
        {
            var fresh = FindChannel(Current.Url);
            if (fresh != null && !ReferenceEquals(fresh, Current))
            {
                Current.IsPlaying = false;
                Current = fresh;
                Current.IsPlaying = State != PlayerState.Stopped && State != PlayerState.Idle;
            }
        }
        LibraryChanged?.Invoke();
        PlayerChanged?.Invoke();
    }

    public Channel? FindChannel(string url) => Library.Channels.FirstOrDefault(c => c.Url == url);

    private static Channel AdHocChannel(string url, string? name)
    {
        var c = new Channel
        {
            Name = string.IsNullOrWhiteSpace(name) ? MuText(url) : name!,
            Url = url,
            SourceName = Loc.T("src_link"),
        };
        c.Categories.Add(Categories.Other);
        return c;
    }

    private static string MuText(string url) => M3uParser.TitleFromUrl(url);

    public void SetSourceEnabled(string id, bool enabled)
    {
        if (id.StartsWith("custom:"))
        {
            var cs = Settings.CustomSources.FirstOrDefault(x => "custom:" + x.Id == id);
            if (cs != null) cs.Enabled = enabled;
        }
        else
        {
            Settings.Sources[id] = enabled;
        }
        SaveSoon();
        _ = ReloadLibraryAsync(false);
    }

    public void AddCustomSource(string name, string url)
    {
        url = StreamResolver.Sanitize(url);
        if (url.Length == 0) return;
        Settings.CustomSources.RemoveAll(x => x.Url == url);
        Settings.CustomSources.Add(new CustomSource
        {
            Name = string.IsNullOrWhiteSpace(name) ? M3uParser.TitleFromUrl(url) : name.Trim(),
            Url = url,
        });
        SaveSoon();
        _ = ReloadLibraryAsync(false);
    }

    public void RemoveCustomSource(string customId)
    {
        var cs = Settings.CustomSources.FirstOrDefault(x => x.Id == customId);
        if (cs == null) return;
        Settings.CustomSources.Remove(cs);
        try { File.Delete(ChannelLibrary.CacheFileFor(cs.Url)); } catch { }
        SaveSoon();
        _ = ReloadLibraryAsync(false);
    }

    public Channel AddLink(string name, string url, string? category)
    {
        url = StreamResolver.Sanitize(url);
        string finalName = string.IsNullOrWhiteSpace(name) ? M3uParser.TitleFromUrl(url) : name.Trim();
        Settings.MyLinks.RemoveAll(x => x.Url == url);
        var link = new SavedLink { Name = finalName, Url = url, Category = category };
        Settings.MyLinks.Insert(0, link);
        SaveSoon();
        var c = Library.AddUserLink(link);
        c.IsFavorite = Settings.Favorites.Contains(url);
        LibraryChanged?.Invoke();
        return c;
    }

    public void RemoveLink(Channel c)
    {
        Settings.MyLinks.RemoveAll(x => x.Url == c.Url);
        Library.RemoveUserLink(c.Url);
        SaveSoon();
        LibraryChanged?.Invoke();
    }

    public void ToggleFavorite(Channel c)
    {
        c.IsFavorite = !c.IsFavorite;
        Settings.Favorites.Remove(c.Url);
        if (c.IsFavorite) Settings.Favorites.Insert(0, c.Url);
        SaveSoon();
        FavoritesChanged?.Invoke();
    }

    public IEnumerable<Channel> FavoriteChannels()
    {
        foreach (var url in Settings.Favorites)
        {
            var c = FindChannel(url);
            if (c != null) yield return c;
        }
    }

    public IEnumerable<Channel> RecentChannels()
    {
        foreach (var url in Settings.Recent)
        {
            var c = FindChannel(url);
            if (c != null) yield return c;
        }
    }

    public void MarkHealth(Channel c, bool? alive)
    {
        c.IsAlive = alive;
        if (alive.HasValue) Health.Set(c.Url, alive.Value);
    }

    // ======================================================================================= playback

    public void Play(Channel c)
    {
        if (c == null || _disposed) return;
        if (Current != null && !ReferenceEquals(Current, c)) Current.IsPlaying = false;
        Current = c;
        c.IsPlaying = true;
        _userStopped = false;
        _autoPaused = false;
        _retry = 0;
        _playedThisChannel = false;

        Settings.LastUrl = c.Url;
        Settings.LastName = c.Name;
        Settings.Recent.Remove(c.Url);
        Settings.Recent.Insert(0, c.Url);
        if (Settings.Recent.Count > 30) Settings.Recent.RemoveRange(30, Settings.Recent.Count - 30);
        SaveSoon();

        EnsureHost();
        _host?.ShowOverlay(OverlayMode.Connecting, c.Name, Loc.T("ov_connecting"));
        _host?.ShowHost();

        // If something already says "pause" (fullscreen game, locked...), remember the choice but don't start yet.
        if (ActiveAutoAction == AutoAction.Pause)
        {
            _autoPaused = true;
            SetState(PlayerState.Paused);
            return;
        }
        _ = StartAsync();
    }

    public void PlayUrl(string url, string? name)
    {
        url = StreamResolver.Sanitize(url);
        if (url.Length == 0) return;
        Play(FindChannel(url) ?? AdHocChannel(url, name));
    }

    private async Task StartAsync()
    {
        CancelRetry();
        _playCts?.Cancel();
        var cts = _playCts = new CancellationTokenSource();
        var c = Current;
        var engine = _engine;
        if (c == null || engine == null) return;
        if (!engine.Available)
        {
            SetState(PlayerState.Error, engine.InitError ?? "LibVLC");
            _host?.ShowOverlay(OverlayMode.Error, c.Name, Loc.T("toast_vlc_failed"));
            return;
        }

        int gen = ++_expectedGen;
        _hasVout = false;
        SetState(PlayerState.Resolving);

        ResolvedStream resolved;
        try
        {
            resolved = await StreamResolver.ResolveAsync(c.Url, Settings.Quality, cts.Token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            if (cts.IsCancellationRequested || gen != _expectedGen) return;
            Logger.Log($"Resolve failed for {c.Url}: {ex.Message}");
            OnStreamFailed(Loc.T("st_resolve_failed") + " " + Short(ex.Message));
            return;
        }
        if (cts.IsCancellationRequested || gen != _expectedGen || _disposed) return;

        var req = new PlayRequest { Generation = gen, Url = resolved.Url, IsLocalFile = resolved.IsLocalFile };
        if (!resolved.IsLocalFile)
        {
            // Only override VLC's own User-Agent when the playlist asks for one (some IPTV servers whitelist VLC).
            if (!string.IsNullOrWhiteSpace(c.UserAgent)) req.Options.Add(":http-user-agent=" + c.UserAgent);
            if (!string.IsNullOrWhiteSpace(c.Referrer)) req.Options.Add(":http-referrer=" + c.Referrer);
            string? proxy = Http.ProxyFor(resolved.Url);
            if (proxy != null) req.Options.Add(":http-proxy=" + proxy);
            // Adaptive (HLS/DASH) streams: never pick more than the chosen height; "auto" means up to 1080p.
            string q = Settings.Quality == "auto" ? "1080" : Settings.Quality;
            req.Options.Add(":adaptive-maxheight=" + q);
            if (resolved.AudioUrl != null) req.Options.Add(":input-slave=" + resolved.AudioUrl);
        }
        if (resolved.IsLocalFile || !LooksLive(resolved.Url))
            req.Options.Add(":input-repeat=65535"); // loop files / VOD clips forever

        SetState(PlayerState.Connecting);
        await engine.PlayAsync(req);
    }

    private static bool LooksLive(string url)
    {
        string u = url.ToLowerInvariant();
        if (u.Contains("googlevideo.com/videoplayback")) return false; // a normal (non-live) YouTube video
        string path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath.ToLowerInvariant() : u;
        string[] vod = { ".mp4", ".m4v", ".mkv", ".webm", ".mov", ".avi", ".mp3", ".m4a", ".flac", ".wav", ".ogg" };
        return !vod.Any(ext => path.EndsWith(ext, StringComparison.Ordinal));
    }

    private void OnEngineEvent(int gen, EngineEvent ev, float value)
    {
        if (_disposed || gen != _expectedGen || Current == null || _userStopped || _autoPaused) return;
        switch (ev)
        {
            case EngineEvent.Opening:
                if (State != PlayerState.Playing) SetState(PlayerState.Connecting);
                break;
            case EngineEvent.Buffering:
                if (State != PlayerState.Playing)
                {
                    StatusDetail = $"{value:0}%";
                    if (State != PlayerState.Buffering) SetState(PlayerState.Buffering, StatusDetail);
                    else PlayerChanged?.Invoke();
                }
                break;
            case EngineEvent.Playing:
                _playingSince = DateTime.UtcNow;
                _lastTime = -1;
                _lastTimeChange = DateTime.UtcNow;
                _playedThisChannel = true;
                SetState(PlayerState.Playing);
                _engine?.ReapplyAudio();
                MarkHealth(Current, true);
                ScheduleOverlayHide(gen);
                break;
            case EngineEvent.Vout:
                if (value > 0)
                {
                    _hasVout = true;
                    if (State == PlayerState.Playing) ScheduleOverlayHide(gen);
                }
                break;
            case EngineEvent.EndReached:
                OnStreamFailed(Loc.T("st_ended"));
                break;
            case EngineEvent.Error:
                OnStreamFailed(Loc.T("st_error"));
                break;
        }
    }

    private void ScheduleOverlayHide(int gen)
    {
        Delay(_hasVout ? 450 : 900, () =>
        {
            if (gen != _expectedGen || State != PlayerState.Playing || _host == null) return;
            if (_hasVout)
            {
                _host.HideOverlay();
                return;
            }
            // No picture yet: give it a few more seconds, then treat it as an audio-only stream.
            Delay(3500, () =>
            {
                if (gen != _expectedGen || State != PlayerState.Playing || _host == null) return;
                if (_hasVout) _host.HideOverlay();
                else _host.ShowOverlay(OverlayMode.AudioOnly, Current?.Name ?? "", Loc.T("ov_audio_only"));
            });
        });
    }

    private void OnStreamFailed(string reason)
    {
        if (_userStopped || _autoPaused || Current == null) return;
        _retry++;
        Logger.Log($"Stream failure #{_retry} ({reason}) for {Current.Url}");

        if (!_playedThisChannel && _retry >= 3)
        {
            // Never played: give up instead of hammering a dead stream.
            int gen = ++_expectedGen;
            if (_engine != null) _ = _engine.StopAsync(gen);
            MarkHealth(Current, false);
            SetState(PlayerState.Error, reason);
            _host?.ShowOverlay(OverlayMode.Error, Current.Name, Loc.T("ov_failed"));
            Delay(7000, () =>
            {
                if (State == PlayerState.Error && gen == _expectedGen) _host?.HideHost();
            });
            return;
        }

        int seconds = Math.Min(60, 1 << Math.Min(_retry, 6)); // 2, 4, 8, 16, 32, 60...
        SetState(PlayerState.Reconnecting, Loc.F("st_retry_in", seconds));
        if (!_playedThisChannel || _host?.CurrentOverlay == OverlayMode.None)
            _host?.ShowOverlay(OverlayMode.Connecting, Current.Name, Loc.F("ov_retry", seconds));
        CancelRetry();
        _retryTimer = Delay(seconds * 1000, () =>
        {
            if (!_userStopped && !_autoPaused && State == PlayerState.Reconnecting) _ = StartAsync();
        });
    }

    private void CancelRetry()
    {
        _retryTimer?.Stop();
        _retryTimer = null;
    }

    public void Stop()
    {
        _userStopped = true;
        _autoPaused = false;
        CancelRetry();
        _playCts?.Cancel();
        int gen = ++_expectedGen;
        if (_engine != null) _ = _engine.StopAsync(gen);
        _host?.HideOverlay();
        _host?.HideHost();
        if (Current != null) Current.IsPlaying = false;
        SetState(PlayerState.Stopped);
    }

    public void TogglePlay()
    {
        if (State is PlayerState.Stopped or PlayerState.Idle or PlayerState.Error)
        {
            if (Current != null) Play(Current);
            else if (!string.IsNullOrWhiteSpace(Settings.LastUrl)) PlayUrl(Settings.LastUrl!, Settings.LastName);
            else ShowMainWindow();
        }
        else
        {
            Stop();
        }
    }

    public void Reconnect()
    {
        if (Current != null) Play(Current);
    }

    public void StepFavorite(int direction)
    {
        var favs = FavoriteChannels().ToList();
        if (favs.Count == 0) return;
        int idx = Current == null ? -1 : favs.FindIndex(c => c.Url == Current.Url);
        int next = idx < 0 ? 0 : ((idx + direction) % favs.Count + favs.Count) % favs.Count;
        Play(favs[next]);
    }

    private void SetState(PlayerState state, string detail = "")
    {
        if (State != state) _stateSince = DateTime.UtcNow;
        State = state;
        StatusDetail = detail;
        PlayerChanged?.Invoke();
    }

    // ---------------- smart pause ----------------

    private void OnAutoAction(AutoAction action)
    {
        Logger.Log($"Smart pause: {action}");
        _autoMuted = action == AutoAction.Mute;
        ApplyMute();
        if (action == AutoAction.Pause) _ = AutoPauseAsync();
        else AutoResume();
        PlayerChanged?.Invoke();
    }

    private async Task AutoPauseAsync()
    {
        if (_autoPaused || _userStopped || Current == null) return;
        if (State is PlayerState.Idle or PlayerState.Stopped or PlayerState.Error) return;

        _autoPaused = true;
        CancelRetry();
        _playCts?.Cancel();
        int gen = ++_expectedGen;
        bool hadPicture = State == PlayerState.Playing && _hasVout;
        SetState(PlayerState.Paused);
        var engine = _engine;
        if (engine == null) return;

        if (hadPicture)
        {
            string path = Path.Combine(AppPaths.Cache, "freeze.png");
            bool ok = await engine.SnapshotAsync(path);
            if (!_autoPaused || gen != _expectedGen) return; // resumed meanwhile
            _host?.ShowFrozen(ok ? path : null, Current?.Name ?? "");
        }
        else
        {
            _host?.ShowFrozen(null, Current?.Name ?? "");
        }
        await engine.StopAsync(gen);
    }

    private void AutoResume()
    {
        if (!_autoPaused) return;
        _autoPaused = false;
        if (_userStopped || Current == null) return;
        _retry = 0;
        // The frozen frame stays up until the stream shows a picture again.
        if (_host?.CurrentOverlay == OverlayMode.None)
            _host.ShowOverlay(OverlayMode.Connecting, Current.Name, Loc.T("ov_connecting"));
        _host?.ShowHost();
        _ = StartAsync();
    }

    // ---------------- audio ----------------

    public void SetVolume(int volume)
    {
        Settings.Volume = Math.Clamp(volume, 0, 100);
        _engine?.SetVolume(Settings.Volume);
        if (Settings.Muted && volume > 0)
        {
            Settings.Muted = false;
            ApplyMute();
        }
        SaveSoon();
        PlayerChanged?.Invoke();
    }

    public void ToggleMute()
    {
        Settings.Muted = !Settings.Muted;
        ApplyMute();
        SaveSoon();
        PlayerChanged?.Invoke();
    }

    private void ApplyMute() => _engine?.SetMute(Settings.Muted || _autoMuted);

    // ---------------- desktop ----------------

    public void ToggleDesktopIcons()
    {
        IconsHidden = !IconsHidden;
        DesktopIcons.SetVisible(!IconsHidden);
        _host?.ReassertZOrder();
        PlayerChanged?.Invoke();
    }

    public void ApplyDisplaySettings()
    {
        if (_host == null) return;
        var target = WallpaperHost.ComputeTarget(Settings.Monitor);
        _host.Reposition(target);
        _engine?.SetFit(Settings.Fit, target.Width, target.Height);
    }

    private void OnDisplayChanged()
    {
        Logger.Log("Display configuration changed");
        if (_host == null || !_host.IsHealthy) RebuildHost("display change", force: true);
        else ApplyDisplaySettings();
    }

    private void EnsureHost()
    {
        if (_host == null || !_host.IsHealthy) RebuildHost("host missing", force: true);
    }

    /// <summary>Re-creates the wallpaper window (after Explorer restarts, wallpaper slideshows etc. destroy its parent).</summary>
    public void RebuildHost(string reason, bool force = false)
    {
        if (_disposed) return;
        if (!force && (DateTime.UtcNow - _lastRebuild).TotalSeconds < 10) return;
        Logger.Log($"Rebuilding wallpaper window: {reason}");
        var old = _host;
        _host = null;
        try { old?.Close(); old?.Dispose(); } catch (Exception ex) { Logger.Log("Old host dispose", ex); }

        CreateHost();
        if (_host == null || _engine == null) return;
        _engine.SetVideoWindow(_host.VideoHandle);
        _engine.SetFit(Settings.Fit, _host.Target.Width, _host.Target.Height);

        bool active = Current != null && !_userStopped &&
                      State is not (PlayerState.Idle or PlayerState.Stopped or PlayerState.Error);
        if (!active) return;
        if (_autoPaused)
        {
            _host.ShowFrozen(null, Current!.Name);
            _host.ShowHost();
            return;
        }
        _host.ShowOverlay(OverlayMode.Connecting, Current!.Name, Loc.T("ov_connecting"));
        _host.ShowHost();
        _ = StartAsync();
    }

    private void Watchdog()
    {
        if (_disposed) return;
        var now = DateTime.UtcNow;

        // 1) Wallpaper window still where it belongs?
        if (_host == null || !_host.IsHealthy)
        {
            if ((now - _lastRebuild).TotalSeconds > 6) RebuildHost("watchdog: wallpaper window lost", force: true);
            return;
        }

        if (_userStopped || _autoPaused || Current == null) return;

        // 2) Stuck while connecting?
        if ((State == PlayerState.Connecting || State == PlayerState.Buffering) && (now - _stateSince).TotalSeconds > 45)
        {
            OnStreamFailed(Loc.T("st_timeout"));
            return;
        }

        // 3) Frozen stream while "playing"?
        if (State == PlayerState.Playing && _engine != null)
        {
            if ((now - _playingSince).TotalSeconds > 40) _retry = 0;

            var vlcState = _engine.State;
            if (vlcState == LibVLCSharp.Shared.VLCState.Error || vlcState == LibVLCSharp.Shared.VLCState.Ended)
            {
                OnStreamFailed(Loc.T("st_error"));
                return;
            }

            long t = _engine.Time;
            if (t > 0)
            {
                if (t != _lastTime) { _lastTime = t; _lastTimeChange = now; }
                else if ((now - _lastTimeChange).TotalSeconds > 30)
                {
                    _lastTimeChange = now;
                    OnStreamFailed(Loc.T("st_stalled"));
                }
            }
        }
    }

    // ---------------- settings that need more than a save ----------------

    public void SetLanguage(string lang)
    {
        Settings.Language = lang == "en" ? "en" : "fa";
        Loc.I.SetLanguage(Settings.Language);
        SaveSoon();
        foreach (var c in Library.Channels) c.RefreshTexts();
        _tray?.RefreshTexts();
    }

    public async Task RecreateEngineAsync()
    {
        Logger.Log("Re-creating playback engine");
        bool wasActive = Current != null && !_userStopped && !_autoPaused &&
                         State is not (PlayerState.Idle or PlayerState.Stopped or PlayerState.Error);
        _expectedGen++;
        var old = _engine;
        _engine = null;
        if (old != null) await Task.Run(() => old.Dispose());
        CreateEngine();
        if (wasActive && Current != null) Play(Current);
    }

    public void ApplyProxy()
    {
        Http.Configure(Settings.Proxy, Settings.CustomProxy);
        SaveSoon();
    }

    public void SettingsChangedForWatcher() => _activity?.Poke();

    public void SaveSoon()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void FinishOnboarding()
    {
        Settings.OnboardingComplete = true;
        SaveSoon();
        _ = FirstLoadAsync();
    }

    private async Task FirstLoadAsync()
    {
        await ReloadLibraryAsync(false);
        _vm?.Toast(Loc.T("toast_pick_channel"));
    }

    // ---------------- windows / misc ----------------

    public void ShowMainWindow()
    {
        if (_window == null) return;
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    public void OnMainWindowHidden()
    {
        if (!Settings.TrayHintShown)
        {
            Settings.TrayHintShown = true;
            SaveSoon();
            _tray?.ShowBalloon(Loc.T("tray_hint_title"), Loc.T("tray_hint_body"));
        }
    }

    private void OnHotkey(int id)
    {
        switch (id)
        {
            case 1: ToggleDesktopIcons(); break;
            case 2: ToggleMute(); break;
            case 3: ShowMainWindow(); break;
            case 4: TogglePlay(); break;
            case 5: StepFavorite(+1); break;
            case 6: StepFavorite(-1); break;
        }
    }

    public void Exit()
    {
        MainWindow.AllowClose = true;
        Application.Current.Shutdown();
    }

    private DispatcherTimer Delay(int ms, Action action)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms)) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (_disposed) return;
            try { action(); } catch (Exception ex) { Logger.Log("Delayed action", ex); }
        };
        t.Start();
        return t;
    }

    private static string Short(string s)
    {
        s = (s ?? "").Replace('\n', ' ').Trim();
        return s.Length > 120 ? s[..120] + "…" : s;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _watchdog.Stop();
            _saveTimer.Stop();
            CancelRetry();
            _playCts?.Cancel();
            _libraryCts?.Cancel();
            _activity?.Dispose();
            _shell?.Dispose();
            SettingsStore.Save(Settings);
            Health.Save();
            _tray?.Dispose();
            _engine?.Dispose();
            var parent = _host?.ParentHwnd ?? IntPtr.Zero;
            bool attached = _host != null;
            try { _host?.Close(); _host?.Dispose(); } catch { }
            if (IconsHidden) DesktopIcons.SetVisible(true);
            if (attached)
            {
                DesktopAttacher.RefreshWallpaper(parent);
                DesktopAttacher.ReapplySystemWallpaper();
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Dispose", ex);
        }
    }
}
