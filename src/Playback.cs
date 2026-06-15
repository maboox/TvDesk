using System;
using LibVLCSharp.Shared;

namespace TvDesk.Playback;

/// <summary>موتور پخش مبتنی بر LibVLC که هم فایل محلی و هم استریم HLS را پخش می‌کند.</summary>
public sealed class PlaybackEngine : IDisposable
{
    private readonly LibVLC _libVLC;
    public MediaPlayer Player { get; }

    public PlaybackEngine()
    {
        Core.Initialize();
        _libVLC = new LibVLC("--no-osd", "--network-caching=1500", "--quiet");
        Player = new MediaPlayer(_libVLC);
    }

    /// <summary>هر کانال/منبع را آنی پخش می‌کند (بدون play/pause/seek).</summary>
    public void Play(string url)
    {
        Uri uri = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u : new Uri(url);
        using var media = new Media(_libVLC, uri);
        Player.Play(media);
    }

    public void Stop() => Player.Stop();
    public bool IsPlaying => Player.IsPlaying;
    public void SetVolume(int v) => Player.Volume = Math.Clamp(v, 0, 100);
    public void SetMuted(bool muted) => Player.Mute = muted;

    public void Dispose()
    {
        Player.Dispose();
        _libVLC.Dispose();
    }
}
