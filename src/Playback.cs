using System;
using LibVLCSharp.Shared;

namespace TvDesk.Playback;

/// <summary>موتور پخش مبتنی بر LibVLC. رندر مستقیم روی HWND (برای والپیپر). اگر LibVLC نباشد، کرش نمی‌کند.</summary>
public sealed class PlaybackEngine : IDisposable
{
    private LibVLC? _libVLC;
    private Media? _currentMedia;
    private int _lastBufferBucket = -1;
    public MediaPlayer? Player { get; private set; }
    public bool Available => Player != null;

    /// <summary>گزارش وضعیت پخش (اتصال/بافر/پخش/خطا) برای نمایش در پنل کنترل.</summary>
    public event Action<string>? StatusChanged;
    private void Report(string msg) => StatusChanged?.Invoke(msg);

    public PlaybackEngine()
    {
        try
        {
            Core.Initialize();
            // نسخهٔ نجات/دیباگ: هیچ مسیر D3D استفاده نمی‌کنیم. wingdi کندتر است، ولی برای HWNDهای reparent شدهٔ Explorer
            // کم‌ریسک‌ترین خروجی VLC است و native crashهای بی‌لاگ D3D را دور می‌زند.
            _libVLC = new LibVLC("--no-osd", "--network-caching=2000", "--quiet", "--no-video-title-show", "--avcodec-hw=none", "--vout=wingdi", "--no-overlay");
            Player = new MediaPlayer(_libVLC) { EnableHardwareDecoding = false };
            HookEvents();
            TvDesk.Logger.Log("LibVLC initialized successfully");
        }
        catch (Exception ex)
        {
            TvDesk.Logger.Log("LibVLC Core.Initialize failed (احتمالاً پوشهٔ libvlc کنار exe نیست)", ex);
        }
    }

    /// <summary>اشتراک در رویدادهای پخش‌کننده برای گزارش وضعیت. روی ترد پس‌زمینهٔ VLC اجرا می‌شوند.</summary>
    private void HookEvents()
    {
        if (Player == null) return;
        Player.Opening += (_, __) => Report("\u23F3 در حال اتصال به کانال…");
        Player.Buffering += (_, e) => OnBuffering(e.Cache);
        Player.Playing += (_, __) => { _lastBufferBucket = -1; Report("\u25B6 در حال پخش"); };
        Player.Paused += (_, __) => Report("\u23F8 مکث");
        Player.EncounteredError += (_, __) => Report("\u2715 خطا در پخش این کانال (ممکن است خراب یا فیلتر باشد)");
        Player.EndReached += (_, __) => Report("\u25A0 استریم قطع/تمام شد");
    }

    /// <summary>گزارش بافر با throttle (هر ۲۰٪ یک‌بار) تا UI و صف dispatcher غرق نشوند.</summary>
    private void OnBuffering(float cache)
    {
        if (cache >= 100f) { _lastBufferBucket = -1; Report("\u25B6 در حال پخش"); return; }
        int bucket = (int)(cache / 20f);
        if (bucket == _lastBufferBucket) return;
        _lastBufferBucket = bucket;
        Report($"\u23F3 بافر کردن… {cache:0}%");
    }

    /// <summary>رندر ویدیو را به یک HWND معین وصل می‌کند (پنجرهٔ والپیپر).</summary>
    public void SetVideoHandle(IntPtr hwnd)
    {
        if (Player != null)
        {
            Player.Hwnd = hwnd;
            TvDesk.Logger.Log($"Player.Hwnd set = {hwnd}");
        }
    }

    /// <summary>هر کانال/منبع را آنی پخش می‌کند (بدون play/pause/seek).</summary>
    public void Play(string url)
    {
        if (_libVLC == null || Player == null)
        {
            Report("\u2715 موتور پخش در دسترس نیست (LibVLC لود نشد)");
            TvDesk.Logger.Log("Play skipped: LibVLC not available");
            return;
        }
        try
        {
            Report("\u23F3 در حال باز کردن استریم…");
            Uri uri = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u : new Uri(url);
            var media = new Media(_libVLC, uri);
            Player.Play(media);
            // مدیای *قبلی* را آزاد کن، نه مدیای فعلی را. dispose فوریِ مدیای درحال‌پخش
            // باعث کرش native (access violation) می‌شود — علت کرش نسخهٔ قبل.
            var previous = _currentMedia;
            _currentMedia = media;
            previous?.Dispose();
            TvDesk.Logger.Log($"Playing: {url}");
        }
        catch (Exception ex)
        {
            Report("\u2715 خطا در باز کردن این کانال");
            TvDesk.Logger.Log($"Play failed: {url}", ex);
        }
    }

    public void Stop() => Player?.Stop();
    public bool IsPlaying => Player?.IsPlaying ?? false;
    public void SetVolume(int v) { if (Player != null) Player.Volume = Math.Clamp(v, 0, 100); }
    public void SetMuted(bool muted) { if (Player != null) Player.Mute = muted; }

    /// <summary>تاریک‌کردن والپیپر با کاهش brightness (0..2 والد، 1=عادی).</summary>
    public void SetBrightness(float brightness)
    {
        // موقتاً غیرفعال: VideoAdjust روی بعضی خروجی‌های VLC/والپیپر می‌تواند native crash بدهد.
        TvDesk.Logger.Log($"SetBrightness skipped in rescue build: {brightness:0.00}");
    }

    public void Dispose()
    {
        try { Player?.Stop(); } catch { }
        Player?.Dispose();
        _currentMedia?.Dispose();
        _libVLC?.Dispose();
    }
}
