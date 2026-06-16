using System;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;

namespace TvDesk.Playback;

/// <summary>موتور پخش مبتنی بر LibVLC. برای پایداری، هر Play یک MediaPlayer تازه می‌سازد.</summary>
public sealed class PlaybackEngine : IDisposable
{
    private LibVLC? _libVLC;
    private Media? _currentMedia;
    private readonly object _playerLock = new();
    private int _lastBufferBucket = -1;
    private IntPtr _videoHwnd;
    private int _volume = 80;
    private bool _muted;

    public MediaPlayer? Player { get; private set; }
    public bool Available => _libVLC != null;
    public bool HasEverPlayed { get; private set; }
    public bool IsPlaying => Player?.IsPlaying ?? false;

    public event Action<string>? StatusChanged;
    private void Report(string msg) => StatusChanged?.Invoke(msg);

    public PlaybackEngine()
    {
        try
        {
            Core.Initialize();
            _libVLC = new LibVLC(
                "--no-osd",
                "--network-caching=2000",
                "--quiet",
                "--no-video-title-show",
                "--avcodec-hw=none",
                "--vout=wingdi",
                "--no-overlay");
            Player = CreatePlayer();
            TvDesk.Logger.Log("LibVLC initialized successfully");
        }
        catch (Exception ex)
        {
            TvDesk.Logger.Log("LibVLC Core.Initialize failed", ex);
        }
    }

    private MediaPlayer? CreatePlayer()
    {
        if (_libVLC == null) return null;
        var p = new MediaPlayer(_libVLC) { EnableHardwareDecoding = false };
        if (_videoHwnd != IntPtr.Zero) p.Hwnd = _videoHwnd;
        p.Volume = Math.Clamp(_volume, 0, 100);
        p.Mute = _muted;
        HookEvents(p);
        return p;
    }

    private void HookEvents(MediaPlayer p)
    {
        p.Opening += (_, __) => Report("\u23F3 در حال اتصال به کانال…");
        p.Buffering += (_, e) => OnBuffering(e.Cache);
        p.Playing += (_, __) => { HasEverPlayed = true; _lastBufferBucket = -1; Report("\u25B6 در حال پخش"); };
        p.Paused += (_, __) => Report("\u23F8 مکث");
        p.EncounteredError += (_, __) => Report("\u2715 خطا در پخش این کانال (ممکن است خراب یا فیلتر باشد)");
        p.EndReached += (_, __) => Report("\u25A0 استریم قطع/تمام شد");
    }

    private void OnBuffering(float cache)
    {
        if (cache >= 100f) { _lastBufferBucket = -1; Report("\u25B6 در حال پخش"); return; }
        int bucket = (int)(cache / 20f);
        if (bucket == _lastBufferBucket) return;
        _lastBufferBucket = bucket;
        Report($"\u23F3 بافر کردن… {cache:0}%");
    }

    public void SetVideoHandle(IntPtr hwnd)
    {
        _videoHwnd = hwnd;
        lock (_playerLock)
        {
            if (Player != null) Player.Hwnd = hwnd;
        }
        TvDesk.Logger.Log($"Player.Hwnd set = {hwnd}");
    }

    /// <summary>
    /// هر بار یک MediaPlayer تازه می‌سازد. این برای جلوگیری از native crash هنگام Restart/Switch مهم است.
    /// </summary>
    public void Play(string url)
    {
        if (_libVLC == null)
        {
            Report("\u2715 موتور پخش در دسترس نیست (LibVLC لود نشد)");
            TvDesk.Logger.Log("Play skipped: LibVLC not available");
            return;
        }

        lock (_playerLock)
        {
            MediaPlayer? oldPlayer = null;
            Media? oldMedia = null;
            try
            {
                Report("\u23F3 در حال باز کردن استریم…");
                Uri uri = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u : new Uri(url);

                oldPlayer = Player;
                oldMedia = _currentMedia;

                try { oldPlayer?.Stop(); } catch (Exception ex) { TvDesk.Logger.Log("Old player stop failed", ex); }
                Thread.Sleep(180);

                var newPlayer = CreatePlayer() ?? throw new InvalidOperationException("Could not create VLC player");
                var newMedia = new Media(_libVLC, uri);

                Player = newPlayer;
                _currentMedia = newMedia;

                bool ok = newPlayer.Play(newMedia);
                TvDesk.Logger.Log($"Playing: {url} ok={ok} newPlayerCreated=True");

                // Dispose قدیمی‌ها با تأخیر، بیرون از مسیر native Play جدید.
                if (oldPlayer != null || oldMedia != null)
                {
                    Task.Run(async () =>
                    {
                        await Task.Delay(2500);
                        try { oldPlayer?.Stop(); } catch { }
                        try { oldPlayer?.Dispose(); } catch { }
                        try { oldMedia?.Dispose(); } catch { }
                    });
                }
            }
            catch (Exception ex)
            {
                // اگر قبل از ثبت Playing exception C# رخ دهد، حداقل لاگ می‌شود؛ native crash ممکن است اینجا نرسد.
                Report("\u2715 خطا در باز کردن این کانال");
                TvDesk.Logger.Log($"Play failed: {url}", ex);
                try { oldPlayer?.Dispose(); } catch { }
                try { oldMedia?.Dispose(); } catch { }
            }
        }
    }

    public void Stop()
    {
        lock (_playerLock)
        {
            try { Player?.Stop(); } catch (Exception ex) { TvDesk.Logger.Log("Stop failed", ex); }
        }
    }

    public void PauseKeepFrame()
    {
        try { if (Player?.IsPlaying == true) Player.Pause(); }
        catch (Exception ex) { TvDesk.Logger.Log("PauseKeepFrame failed", ex); }
    }

    public void Resume()
    {
        try { Player?.Play(); }
        catch (Exception ex) { TvDesk.Logger.Log("Resume failed", ex); }
    }

    public void SetVolume(int v)
    {
        _volume = Math.Clamp(v, 0, 100);
        lock (_playerLock) { if (Player != null) Player.Volume = _volume; }
    }

    public void SetMuted(bool muted)
    {
        _muted = muted;
        lock (_playerLock) { if (Player != null) Player.Mute = muted; }
    }

    public void SetBrightness(float brightness)
    {
        TvDesk.Logger.Log($"SetBrightness skipped: {brightness:0.00}");
    }

    public void Dispose()
    {
        lock (_playerLock)
        {
            try { Player?.Stop(); } catch { }
            try { Player?.Dispose(); } catch { }
            try { _currentMedia?.Dispose(); } catch { }
            try { _libVLC?.Dispose(); } catch { }
        }
    }
}
