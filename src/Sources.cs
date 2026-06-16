using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TvDesk.Sources;

/// <summary>یک کانال تلویزیونی.</summary>
public sealed class Channel
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Group { get; set; }
    public string? LogoUrl { get; set; }
    public string? TvgId { get; set; }
    public string Source { get; set; } = "";
    public bool IsFavorite { get; set; }
    public string FavoriteText => IsFavorite ? "★ منتخب" : "☆ افزودن";
    public override string ToString() => Name;
}

/// <summary>پارس فرمت M3U/M3U8 به لیست کانال‌ها.</summary>
public static class M3uParser
{
    private static readonly Regex AttrRegex = new(@"([\w-]+)=""([^""]*)""", RegexOptions.Compiled);

    public static List<Channel> Parse(string content, string sourceName)
    {
        var channels = new List<Channel>();
        if (string.IsNullOrWhiteSpace(content)) return channels;

        string[] lines = content.Replace("\r\n", "\n").Split('\n');
        Channel? current = null;

        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                current = new Channel { Source = sourceName };
                foreach (Match m in AttrRegex.Matches(line))
                {
                    string key = m.Groups[1].Value.ToLowerInvariant();
                    string val = m.Groups[2].Value;
                    switch (key)
                    {
                        case "tvg-id": current.TvgId = val; break;
                        case "tvg-logo": current.LogoUrl = val; break;
                        case "group-title": current.Group = val; break;
                        case "tvg-name": if (string.IsNullOrEmpty(current.Name)) current.Name = val; break;
                    }
                }
                int comma = line.LastIndexOf(',');
                if (comma >= 0 && comma < line.Length - 1)
                    current.Name = line[(comma + 1)..].Trim();
            }
            else if (!line.StartsWith("#"))
            {
                if (current != null)
                {
                    current.Url = line;
                    if (string.IsNullOrWhiteSpace(current.Name)) current.Name = line;
                    if (string.IsNullOrWhiteSpace(current.Group)) current.Group = "بدون دسته";
                    channels.Add(current);
                    current = null;
                }
            }
        }
        return channels;
    }
}

/// <summary>دانلود، کش و آپدیت خودکار پلی‌لیست‌های اپن‌سورس.</summary>
public static class PlaylistUpdater
{
    public record Source(string Name, string Url);

    // پلی‌لیست‌های عمومی و رایگان (تفکیک بر اساس کشور/دسته/زبان)
    public static readonly List<Source> Sources = new()
    {
        new("ایران (iptv-org)", "https://iptv-org.github.io/iptv/countries/ir.m3u"),
        new("دسته‌بندی‌ها (iptv-org)", "https://iptv-org.github.io/iptv/index.category.m3u"),
        new("زبان‌ها (iptv-org)", "https://iptv-org.github.io/iptv/index.language.m3u"),
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private static string CacheDir
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TvDesk", "cache");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static async Task<List<Channel>> GetChannelsAsync(int refreshDays)
    {
        var result = new List<Channel>();

        // کانال‌های مستقیم تلوبیون (همیشه موجود)
        result.AddRange(Telewebion.Channels());

        foreach (var src in Sources)
        {
            try
            {
                string text = await GetCachedOrDownloadAsync(src, refreshDays);
                result.AddRange(M3uParser.Parse(text, src.Name));
            }
            catch { /* اگر یک منبع دردسترس نبود، رد شو */ }
        }

        return result
            .Where(c => !string.IsNullOrWhiteSpace(c.Url))
            .GroupBy(c => c.Url)
            .Select(g => g.First())
            .ToList();
    }

    private static async Task<string> GetCachedOrDownloadAsync(Source src, int refreshDays)
    {
        string file = Path.Combine(CacheDir, Sanitize(src.Name) + ".m3u");
        if (File.Exists(file))
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(file);
            if (age.TotalDays < refreshDays)
                return await File.ReadAllTextAsync(file);
        }
        string text = await Http.GetStringAsync(src.Url);
        await File.WriteAllTextAsync(file, text);
        return text;
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
}

/// <summary>کانال‌های زنده‌ی تلوبیون (لینک m3u8 مستقیم). بعضی ممکن است region-lock روی ایران باشند.</summary>
public static class Telewebion
{
    private static Channel C(string name, string id) => new()
    {
        Name = name,
        Url = "https://cdnw.telewebion.com/" + id + "/live/playlist.m3u8",
        Group = "تلوبیون",
        Source = "Telewebion"
    };

    public static List<Channel> Channels() => new()
    {
        C("شبکه یک", "tv1"),
        C("شبکه دو", "tv2"),
        C("شبکه سه", "tv3"),
        C("شبکه چهار", "tv4"),
        C("شبکه پنج (تهران)", "tv5"),
        C("خبر", "irinn"),
        C("ورزش", "varzesh"),
        C("نسیم", "nasim"),
        C("تماشا", "hdtest"),
        C("پویا", "pooya"),
    };
}

/// <summary>کتابخانه‌ی کانال‌های درحال‌اجرا.</summary>
public sealed class ChannelLibrary
{
    public List<Channel> Channels { get; } = new();

    public async Task LoadAsync(int refreshDays)
    {
        Channels.Clear();
        Channels.AddRange(await PlaylistUpdater.GetChannelsAsync(refreshDays));
    }
}

/// <summary>تبدیل هر ورودی (فایل/IPTV/یوتیوب/آپارات/تلوبیون) به یک URL قابل‌پخش.</summary>
public static class SourceResolver
{
    public static async Task<string> ResolveAsync(string input, string quality = "Auto")
    {
        input = input.Trim();
        if (string.IsNullOrEmpty(input)) throw new ArgumentException("empty input");
        if (File.Exists(input)) return input;
        if (IsDirectStream(input)) return input;
        if (NeedsYtDlp(input)) return await ResolveWithYtDlpAsync(input, quality);
        return input;
    }

    private static bool IsDirectStream(string url)
    {
        string u = url.ToLowerInvariant();
        return u.Contains(".m3u8") || u.EndsWith(".ts") || u.EndsWith(".mp4")
            || u.EndsWith(".mkv") || u.StartsWith("rtmp://") || u.StartsWith("udp://");
    }

    private static bool NeedsYtDlp(string url)
    {
        string u = url.ToLowerInvariant();
        return u.Contains("youtube.com") || u.Contains("youtu.be")
            || u.Contains("aparat.com")
            || (u.Contains("telewebion.com") && !u.Contains(".m3u8"));
    }

    public static async Task<string> ResolveWithYtDlpAsync(string url, string quality)
    {
        string format = quality switch
        {
            "High" => "best[height<=1080]/best",
            "Medium" => "best[height<=720]/best",
            "Low" => "best[height<=480]/best",
            _ => "best"
        };

        string exe = Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe");
        if (!File.Exists(exe)) exe = "yt-dlp.exe"; // fall back to PATH

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exe,
            Arguments = $@"-g -f ""{format}"" ""{url}""",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("yt-dlp not found");
        string output = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();

        var first = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(first))
            throw new InvalidOperationException("yt-dlp returned no stream URL");
        return first.Trim();
    }
}

/// <summary>بررسی سلامت کانال (ایده ۳) — تشخیص کانال خراب.</summary>
public static class ChannelHealth
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public static async Task<bool> IsAliveAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}

// EPG / راهنمای برنامه (ایده ۱) — فاز ۳.
// iptv-org برای هر کشور فایل XMLTV دارد که با tvg-id به کانال‌ها مَپ می‌شود.
public static class Epg
{
    // TODO فاز ۳: دانلود XMLTV، پارس <programme>، مَپ با Channel.TvgId
}
