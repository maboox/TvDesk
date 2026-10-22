using System;
using System.IO;
using System.Linq;
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
        return s;
    }

    public static bool IsLocalFile(string s)
    {
        return MediaPolicy.IsLocalPath(s);
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
        if (!MediaPolicy.IsPlayable(url)) throw new ArgumentException("Unsupported media address.");

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
