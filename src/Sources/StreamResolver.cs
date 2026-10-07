using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TvDesk.Core;

namespace TvDesk.Sources;

public sealed class ResolvedStream
{
    public string Url { get; set; } = "";
    public string? AudioUrl { get; set; }
    public bool IsLocalFile { get; set; }
    public bool ViaYtDlp { get; set; }
}

/// <summary>Turns what the user picked (YouTube page, Twitch channel, m3u8, local file...) into something VLC can open.</summary>
public static class StreamResolver
{
    private static readonly string[] YtDlpHosts =
    {
        "youtube.com", "youtu.be", "twitch.tv", "dailymotion.com", "dai.ly", "aparat.com", "vimeo.com",
        "facebook.com", "fb.watch", "kick.com", "ok.ru", "rumble.com", "telewebion.com", "x.com", "twitter.com",
        "instagram.com", "tiktok.com", "bilibili.com", "nicovideo.jp", "streamable.com", "livestream.com",
        "trovo.live", "vk.com", "vkvideo.ru", "rutube.ru", "bitchute.com", "odysee.com",
    };

    private static readonly string[] MediaExtensions =
    {
        ".m3u8", ".m3u", ".mpd", ".mp4", ".m4v", ".mkv", ".ts", ".webm", ".mov", ".flv", ".avi",
        ".mp3", ".aac", ".m4a", ".ogg", ".opus", ".wav", ".flac",
    };

    public static string Sanitize(string? input)
    {
        string s = (input ?? "").Trim().Trim('"', '\'', '<', '>');
        // Pasted links often carry trailing punctuation: "youtube.com/live/ID:"
        while (s.Length > 0 && ":،,؛;.".IndexOf(s[^1]) >= 0) s = s[..^1].TrimEnd();
        return s;
    }

    public static bool IsLocalFile(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (s.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.Contains("://")) return false;
        return s.Length > 2 && (s[1] == ':' || s.StartsWith(@"\\"));
    }

    public static string LocalPath(string s)
    {
        if (s.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            try { return new Uri(s).LocalPath; } catch { }
        }
        return s;
    }

    public static bool NeedsYtDlp(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
        string path = u.AbsolutePath.ToLowerInvariant();
        if (MediaExtensions.Any(ext => path.EndsWith(ext, StringComparison.Ordinal))) return false;
        string host = u.Host.ToLowerInvariant();
        return YtDlpHosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal));
    }

    public static bool LooksLikePlaylistUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return url.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase);
        string path = u.AbsolutePath.ToLowerInvariant();
        return path.EndsWith(".m3u") || path.Contains("get.php") || u.Query.Contains("type=m3u", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<ResolvedStream> ResolveAsync(string input, string quality, CancellationToken ct)
    {
        string url = Sanitize(input);
        if (url.Length == 0) throw new ArgumentException("empty link");

        if (IsLocalFile(url))
        {
            string path = LocalPath(url);
            if (!File.Exists(path)) throw new FileNotFoundException(path);
            return new ResolvedStream { Url = path, IsLocalFile = true };
        }

        if (NeedsYtDlp(url))
        {
            var r = await YtDlp.ResolveAsync(url, quality, ct);
            r.ViaYtDlp = true;
            return r;
        }

        return new ResolvedStream { Url = url };
    }
}

/// <summary>Manages yt-dlp: finds/downloads it, keeps it updated (YouTube breaks old versions), runs it.</summary>
public static class YtDlp
{
    private const string DownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

    private static readonly SemaphoreSlim EnsureLock = new(1, 1);
    private static readonly SemaphoreSlim UpdateLock = new(1, 1);
    private static readonly SemaphoreSlim Slots = new(3, 3);

    public static string ExePath => Path.Combine(AppPaths.Tools, "yt-dlp.exe");

    public static async Task<string?> EnsureAsync(CancellationToken ct)
    {
        if (File.Exists(ExePath)) return ExePath;
        await EnsureLock.WaitAsync(ct);
        try
        {
            if (File.Exists(ExePath)) return ExePath;

            string bundled = Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe");
            if (File.Exists(bundled))
            {
                try
                {
                    File.Copy(bundled, ExePath, true);
                    return ExePath;
                }
                catch (Exception ex)
                {
                    Logger.Log("Copying bundled yt-dlp failed; using it in place", ex);
                    return bundled;
                }
            }

            Logger.Log("yt-dlp not found — downloading");
            string tmp = ExePath + ".download";
            using (var resp = await Http.Client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(tmp);
                await resp.Content.CopyToAsync(fs, ct);
            }
            File.Move(tmp, ExePath, true);
            return ExePath;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.Log("yt-dlp download failed", ex);
            return null;
        }
        finally
        {
            EnsureLock.Release();
        }
    }

    public static async Task<ResolvedStream> ResolveAsync(string url, string quality, CancellationToken ct)
    {
        string? exe = await EnsureAsync(ct);
        if (exe == null) throw new InvalidOperationException(Loc.T("err_ytdlp_missing"));

        // Wait if an update is replacing the exe right now.
        await UpdateLock.WaitAsync(ct);
        UpdateLock.Release();

        await Slots.WaitAsync(ct);
        try
        {
            string h = quality switch { "720" => "720", "480" => "480", _ => "1080" };
            // Prefer H.264 (cheap to decode) video + separate audio; fall back to a single muxed stream (live HLS).
            string format = $"bv*[height<={h}][vcodec^=avc1]+ba/bv*[height<={h}]+ba/b[height<={h}]/b";
            var args = new List<string>
            {
                "--no-playlist", "--no-warnings", "--no-progress", "--socket-timeout", "15",
                "-f", format, "-g"
            };
            string? proxy = Http.ProxyFor(url);
            if (proxy != null) { args.Add("--proxy"); args.Add(proxy); }
            args.Add(url);

            var (code, stdout, stderr) = await RunAsync(exe, args, TimeSpan.FromSeconds(45), ct);
            var urls = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                             .Select(x => x.Trim())
                             .Where(x => x.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                             .ToList();
            Logger.Log($"yt-dlp exit={code} urls={urls.Count} for {url}");
            if (urls.Count == 0)
            {
                string err = stderr.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                             ?? stderr.Trim().Split('\n').FirstOrDefault() ?? "";
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(err) ? "yt-dlp returned nothing" : err.Trim());
            }
            return new ResolvedStream { Url = urls[0], AudioUrl = urls.Count > 1 ? urls[1] : null };
        }
        finally
        {
            Slots.Release();
        }
    }

    public static async Task<string?> VersionAsync()
    {
        try
        {
            string? exe = await EnsureAsync(CancellationToken.None);
            if (exe == null) return null;
            var (_, stdout, _) = await RunAsync(exe, new[] { "--version" }, TimeSpan.FromSeconds(20), CancellationToken.None);
            return stdout.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Runs "yt-dlp -U". Blocks new resolves until finished.</summary>
    public static async Task<bool> UpdateAsync()
    {
        string? exe = await EnsureAsync(CancellationToken.None);
        if (exe == null) return false;
        await UpdateLock.WaitAsync();
        int acquired = 0;
        try
        {
            for (; acquired < 3; acquired++) await Slots.WaitAsync();
            var args = new List<string> { "-U" };
            string? proxy = Http.ProxyFor("https://github.com/");
            if (proxy != null) { args.Add("--proxy"); args.Add(proxy); }
            var (code, stdout, stderr) = await RunAsync(exe, args, TimeSpan.FromMinutes(3), CancellationToken.None);
            Logger.Log($"yt-dlp -U exit={code}: {stdout.Trim()} {stderr.Trim()}");
            return code == 0;
        }
        catch (Exception ex)
        {
            Logger.Log("yt-dlp update failed", ex);
            return false;
        }
        finally
        {
            if (acquired > 0) Slots.Release(acquired);
            UpdateLock.Release();
        }
    }

    private static async Task<(int code, string stdout, string stderr)> RunAsync(string exe, IEnumerable<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppPaths.Tools,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        using var proc = new Process { StartInfo = psi };
        if (!proc.Start()) throw new InvalidOperationException("could not start yt-dlp");
        Task<string> stdout = proc.StandardOutput.ReadToEndAsync();
        Task<string> stderr = proc.StandardError.ReadToEndAsync();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(true); } catch { }
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException("yt-dlp timed out");
        }
        return (proc.ExitCode, await stdout, await stderr);
    }
}
