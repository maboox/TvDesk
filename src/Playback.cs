using System;
using LibVLCSharp.Shared;

namespace TvDesk.Playback;

/// <summary>موتور پخش مبتنی بر LibVLC. رندر مستقیم روی HWND (برای والپیپر). اگر LibVLC نباشد، کرش نمی‌کند.</summary>
public sealed class PlaybackEngine : IDisposable
{
    private LibVLC? _libVLC;
    public MediaPlayer? Player { get; private set; }
    public bool Available => Player != null;

    public PlaybackEngine()
    {
        try
        {
            Core.Initialize();
            _libVLC = new LibVLC("--no-osd", "--network-caching=1500", "--quiet", "--no-video-title-show");
            Player = new MediaPlayer(_libVLC) { EnableHardwareDecoding = true };
            TvDesk.Logger.Log("LibVLC initialized successfully");
        }
        catch (Exception ex)
        {
            TvDesk.Logger.Log("LibVLC Core.Initialize failed (احتمالاً پوشهٔ libvlc کنار exe نیست)", ex);
        }
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
            TvDesk.Logger.Log("Play skipped: LibVLC not available");
            return;
        }
        try
        {
            Uri uri = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u : new Uri(url);
            var media = new Media(_libVLC, uri);
            Player.Play(media);
            media.Dispose();
            TvDesk.Logger.Log($"Playing: {url}");
        }
        catch (Exception ex)
        {
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
        if (Player == null) return;
        try
        {
            Player.SetAdjustInt(VideoAdjustOption.Enable, 1);
            Player.SetAdjustFloat(VideoAdjustOption.Brightness, Math.Clamp(brightness, 0f, 2f));
        }
        catch (Exception ex)
        {
            TvDesk.Logger.Log("SetBrightness failed", ex);
        }
    }

    public void Dispose()
    {
        Player?.Dispose();
        _libVLC?.Dispose();
    }
}
