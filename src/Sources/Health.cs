using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TvDesk.Core;

namespace TvDesk.Sources;

/// <summary>Remembers which streams worked / failed recently so "hide broken" survives restarts.</summary>
public sealed class HealthStore
{
    public sealed class Record
    {
        public bool Alive { get; set; }
        public DateTime At { get; set; }
    }

    private readonly ConcurrentDictionary<string, Record> _records = new();
    private bool _dirty;

    public void Load()
    {
        try
        {
            if (!File.Exists(AppPaths.HealthFile)) return;
            var data = JsonSerializer.Deserialize<Dictionary<string, Record>>(File.ReadAllText(AppPaths.HealthFile));
            if (data == null) return;
            foreach (var kv in data)
                if (IsFresh(kv.Value)) _records[kv.Key] = kv.Value;
        }
        catch (Exception ex) { Logger.Log("Health load failed", ex); }
    }

    public void Save()
    {
        if (!_dirty) return;
        try
        {
            var snapshot = _records.Where(kv => IsFresh(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value);
            File.WriteAllText(AppPaths.HealthFile, JsonSerializer.Serialize(snapshot));
            _dirty = false;
        }
        catch (Exception ex) { Logger.Log("Health save failed", ex); }
    }

    private static bool IsFresh(Record r)
        => (DateTime.UtcNow - r.At) < (r.Alive ? TimeSpan.FromDays(7) : TimeSpan.FromDays(3));

    public bool? Get(string url)
        => _records.TryGetValue(url, out var r) && IsFresh(r) ? r.Alive : null;

    public void Set(string url, bool alive)
    {
        _records[url] = new Record { Alive = alive, At = DateTime.UtcNow };
        _dirty = true;
    }

    public void Clear()
    {
        _records.Clear();
        _dirty = true;
    }
}

/// <summary>Quick "is this stream up?" probe: resolves web links with yt-dlp, then requests the stream URL.</summary>
public static class HealthChecker
{
    public static async Task<bool?> CheckAsync(Channel c, CancellationToken ct)
    {
        string url = c.Url.Trim();
        if (StreamResolver.IsLocalFile(url)) return File.Exists(StreamResolver.LocalPath(url));
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null; // rtmp/udp/rtsp: can't probe cheaply

        try
        {
            if (StreamResolver.NeedsYtDlp(url))
            {
                var r = await YtDlp.ResolveAsync(url, "480", ct);
                url = r.Url;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(12));
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(c.UserAgent)) req.Headers.TryAddWithoutValidation("User-Agent", c.UserAgent);
            if (!string.IsNullOrWhiteSpace(c.Referrer)) req.Headers.TryAddWithoutValidation("Referer", c.Referrer);

            using var resp = await Http.Probe.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!resp.IsSuccessStatusCode) return false;

            string path = resp.RequestMessage?.RequestUri?.AbsolutePath ?? uri.AbsolutePath;
            string? mediaType = resp.Content.Headers.ContentType?.MediaType;
            bool isPlaylist = path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
                              || (mediaType != null && mediaType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase));
            if (!isPlaylist) return true;

            using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            var buffer = new byte[1024];
            int read = 0;
            while (read < buffer.Length)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cts.Token);
                if (n == 0) break;
                read += n;
            }
            string head = Encoding.UTF8.GetString(buffer, 0, read).TrimStart('﻿', ' ', '\r', '\n', '\t');
            return head.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Tests many channels with limited parallelism. <paramref name="onResult"/> is called from worker threads.</summary>
    public static async Task RunAsync(IReadOnlyList<Channel> channels, Action<Channel, bool?> onResult, CancellationToken ct, int parallelism = 8)
    {
        using var gate = new SemaphoreSlim(parallelism);
        var tasks = channels.Select(async c =>
        {
            await gate.WaitAsync(ct);
            try
            {
                bool? alive = await CheckAsync(c, ct);
                onResult(c, alive);
            }
            finally
            {
                gate.Release();
            }
        }).ToList();
        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { }
    }
}
