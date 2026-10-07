using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using TvDesk.Core;

namespace TvDesk.Playback;

public enum EngineEvent { Opening, Buffering, Playing, Paused, Stopped, EndReached, Error, Vout }

public sealed class PlayRequest
{
    public int Generation { get; init; }
    public string Url { get; init; } = "";
    public bool IsLocalFile { get; init; }
    public List<string> Options { get; } = new();
}

/// <summary>
/// One LibVLC instance + one MediaPlayer for the whole app lifetime.
/// Every LibVLC call runs on a background worker, one at a time: Stop/Play can block for seconds on dead streams
/// and must never run on the UI thread or inside a LibVLC event callback.
/// </summary>
public sealed class PlaybackEngine : IDisposable
{
    private static bool _coreInitialized;
    private static readonly object CoreLock = new();

    private readonly LibVLC? _vlc;
    private readonly MediaPlayer? _player;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Media? _media;
    private IntPtr _hwnd;
    private volatile int _activeGeneration;
    private volatile bool _disposed;

    private FitMode _fit = FitMode.Fill;
    private int _fitW, _fitH;
    private int _volume = 60;
    private bool _mute;

    public bool Available => _player != null;
    public string? InitError { get; }

    /// <summary>Raised on a LibVLC thread. Handlers must only post work elsewhere (e.g. Dispatcher.BeginInvoke).</summary>
    public event Action<int, EngineEvent, float>? Event;

    public PlaybackEngine(bool hardwareDecoding, string videoOutput)
    {
        try
        {
            lock (CoreLock)
            {
                if (!_coreInitialized)
                {
                    LibVLCSharp.Shared.Core.Initialize();
                    _coreInitialized = true;
                }
            }

            var args = new List<string>
            {
                "--no-osd",
                "--no-video-title-show",
                "--no-snapshot-preview",
                "--no-stats",
                "--quiet",
                "--no-sub-autodetect-file",
                "--network-caching=3000",
                "--live-caching=3000",
                "--http-reconnect",
                "--avcodec-hw=" + (hardwareDecoding ? "any" : "none"),
            };
            switch (videoOutput)
            {
                case "d3d11": args.Add("--vout=direct3d11"); break;
                case "auto": break;
                default: args.Add("--vout=wingdi"); break; // most compatible: works behind the desktop on every Windows build
            }

            _vlc = new LibVLC(args.ToArray());
            _player = new MediaPlayer(_vlc)
            {
                EnableHardwareDecoding = hardwareDecoding,
                EnableKeyInput = false,
                EnableMouseInput = false,
            };
            Hook(_player);
            Logger.Log($"LibVLC {_vlc.Version} ready (hw={hardwareDecoding}, vout={videoOutput})");
        }
        catch (Exception ex)
        {
            InitError = ex.Message;
            Logger.Log("LibVLC initialisation failed", ex);
        }
    }

    private void Hook(MediaPlayer p)
    {
        p.Opening += (_, _) => Fire(EngineEvent.Opening, 0);
        p.Buffering += (_, e) => Fire(EngineEvent.Buffering, e.Cache);
        p.Playing += (_, _) => Fire(EngineEvent.Playing, 0);
        p.Paused += (_, _) => Fire(EngineEvent.Paused, 0);
        p.Stopped += (_, _) => Fire(EngineEvent.Stopped, 0);
        p.EndReached += (_, _) => Fire(EngineEvent.EndReached, 0);
        p.EncounteredError += (_, _) => Fire(EngineEvent.Error, 0);
        p.Vout += (_, e) => Fire(EngineEvent.Vout, e.Count);
    }

    private void Fire(EngineEvent ev, float value)
    {
        if (_disposed) return;
        try { Event?.Invoke(_activeGeneration, ev, value); } catch { }
    }

    /// <summary>Current playback position in ms (cheap, non-blocking). -1 when unknown.</summary>
    public long Time
    {
        get
        {
            try { return _player?.Time ?? -1; } catch { return -1; }
        }
    }

    public VLCState State
    {
        get
        {
            try { return _player?.State ?? VLCState.NothingSpecial; } catch { return VLCState.NothingSpecial; }
        }
    }

    public void SetVideoWindow(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _ = Run(p => p.Hwnd = hwnd);
    }

    public Task PlayAsync(PlayRequest req) => Run(p =>
    {
        _activeGeneration = req.Generation;
        var media = req.IsLocalFile
            ? new Media(_vlc!, req.Url, FromType.FromPath)
            : new Media(_vlc!, req.Url, FromType.FromLocation);
        foreach (var o in req.Options) media.AddOption(o);

        var old = _media;
        _media = media;
        if (p.Hwnd != _hwnd) p.Hwnd = _hwnd;
        ApplyFit(p);
        p.Play(media);
        ApplyAudio(p);
        try { old?.Dispose(); } catch { }
        Logger.Log($"VLC play gen={req.Generation}: {Shorten(req.Url)}");
    });

    public Task StopAsync(int generation) => Run(p =>
    {
        _activeGeneration = generation;
        p.Stop();
    });

    public void SetVolume(int volume)
    {
        _volume = Math.Clamp(volume, 0, 100);
        _ = Run(ApplyAudio);
    }

    public void SetMute(bool mute)
    {
        _mute = mute;
        _ = Run(ApplyAudio);
    }

    /// <summary>VLC forgets volume/mute when the audio output is (re)created — re-apply after Playing.</summary>
    public void ReapplyAudio() => _ = Run(ApplyAudio);

    private void ApplyAudio(MediaPlayer p)
    {
        try
        {
            p.Mute = _mute;
            p.Volume = _volume;
        }
        catch { }
    }

    public void SetFit(FitMode fit, int width, int height)
    {
        _fit = fit;
        _fitW = width;
        _fitH = height;
        _ = Run(ApplyFit);
    }

    private void ApplyFit(MediaPlayer p)
    {
        string? ratio = null;
        if (_fitW > 0 && _fitH > 0)
        {
            int g = Gcd(_fitW, _fitH);
            ratio = $"{_fitW / g}:{_fitH / g}";
        }
        try
        {
            switch (_fit)
            {
                case FitMode.Fill:
                    p.AspectRatio = null;
                    p.CropGeometry = ratio;
                    break;
                case FitMode.Stretch:
                    p.CropGeometry = null;
                    p.AspectRatio = ratio;
                    break;
                default:
                    p.CropGeometry = null;
                    p.AspectRatio = null;
                    break;
            }
        }
        catch (Exception ex) { Logger.Log("ApplyFit failed", ex); }
    }

    /// <summary>Saves the current video frame to <paramref name="path"/>. Returns false when there is no picture.</summary>
    public async Task<bool> SnapshotAsync(string path)
    {
        bool ok = false;
        await Run(p =>
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            ok = p.TakeSnapshot(0, path, 0, 0);
        });
        if (!ok) return false;
        for (int i = 0; i < 20; i++)
        {
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > 0) return true;
            }
            catch { }
            await Task.Delay(100);
        }
        return false;
    }

    private Task Run(Action<MediaPlayer> action)
    {
        return Task.Run(async () =>
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var p = _player;
                if (p != null && !_disposed) action(p);
            }
            catch (Exception ex)
            {
                Logger.Log("VLC operation failed", ex);
            }
            finally
            {
                _gate.Release();
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var p = _player;
        var m = _media;
        var vlc = _vlc;
        var t = Task.Run(async () =>
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                try { p?.Stop(); } catch { }
                try { p?.Dispose(); } catch { }
                try { m?.Dispose(); } catch { }
                try { vlc?.Dispose(); } catch { }
            }
            finally
            {
                _gate.Release();
            }
        });
        try { t.Wait(TimeSpan.FromSeconds(4)); } catch { }
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0) { int t = a % b; a = b; b = t; }
        return Math.Max(1, a);
    }

    private static string Shorten(string s) => s.Length > 140 ? s[..140] + "…" : s;
}
