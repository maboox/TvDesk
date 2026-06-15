using System;
using LibVLCSharp.Shared;

namespace TvDesk.Playback;

/// <summary>موتور پخش مبتنی بر LibVLC. اگر LibVLC در دسترس نباشد، اپ کرش نمی‌کند و فقط لاگ می‌گیرد.</summary>
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
            _libVLC = new LibVLC("--no-osd", "--network-caching=1500", "--quiet");
            Player = new MediaPlayer(_libVLC);
            TvDesk.Logger.Log("LibVLC initialized successfully");
        }
        catch (Exception ex)
        {
            TvDesk.Logger.Log("LibVLC Core.Initialize failed (احتمالاً پوشهٔ libvlc کنار exe نیست)", ex);
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
            using var media = new Media(_libVLC, uri);
            Player.Play(media);
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

    public void Dispose()
    {
        Player?.Dispose();
        _libVLC?.Dispose();
    }
}
