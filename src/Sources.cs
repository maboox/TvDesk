using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TvDesk.Settings;

namespace TvDesk.Sources;

public sealed class Channel
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Group { get; set; }
    public string? Country { get; set; }
    public string? LogoUrl { get; set; }
    public string? TvgId { get; set; }
    public string Source { get; set; } = "";
    public bool IsFavorite { get; set; }
    public bool? IsAlive { get; set; }
    public string FavoriteText => IsFavorite ? "★" : "☆";
    public string HealthText => IsAlive == true ? "✓" : IsAlive == false ? "×" : "";
    public string HealthBrush => IsAlive == true ? "#7CFFB2" : IsAlive == false ? "#FF6B7A" : "#596783";
    public override string ToString() => Name;
}

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
                        case "tvg-country": current.Country = val; break;
                        case "country": current.Country = val; break;
                    }
                }
                int comma = line.LastIndexOf(',');
                if (comma >= 0 && comma < line.Length - 1)
                    current.Name = line[(comma + 1)..].Trim();
            }
            else if (!line.StartsWith("#") && current != null)
            {
                current.Url = line;
                if (string.IsNullOrWhiteSpace(current.Name)) current.Name = line;
                current.Group = NormalizeGroup(current.Group);
                current.Country = NormalizeCountry(string.IsNullOrWhiteSpace(current.Country) ? GuessCountryFromSource(sourceName) : current.Country);
                if (LooksLikeCountry(current.Group))
                {
                    if (string.IsNullOrWhiteSpace(current.Country)) current.Country = NormalizeCountry(current.Group);
                    current.Group = "General";
                }
                channels.Add(current);
                current = null;
            }
        }
        return channels;
    }

    private static readonly HashSet<string> CountryLikeGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        "AF","AL","DZ","AD","AO","AR","AM","AU","AT","AZ","BH","BD","BY","BE","BO","BA","BR","BG","CA","CL","CN","CO","CR","HR","CU","CY","CZ","DK","DO","EC","EG","EE","FI","FR","GE","DE","GR","GT","HK","HU","IS","IN","ID","IR","IQ","IE","IL","IT","JP","JO","KZ","KR","KW","LV","LB","LT","LU","MY","MX","MA","NL","NZ","NO","PK","PA","PE","PH","PL","PT","QA","RO","RU","SA","RS","SG","SK","SI","ZA","ES","SE","CH","TH","TR","UA","AE","GB","UK","US","UY","VE","VN",
        "Afghanistan","Albania","Algeria","Argentina","Armenia","Australia","Austria","Azerbaijan","Bahrain","Bangladesh","Belarus","Belgium","Brazil","Bulgaria","Canada","Chile","China","Colombia","Croatia","Cuba","Cyprus","Czech Republic","Denmark","Dominican Republic","Ecuador","Egypt","Estonia","Finland","France","Georgia","Germany","Deutschland","آلمان","Greece","Hong Kong","Hungary","Iceland","India","Indonesia","Iran","ایران","Iraq","Ireland","Israel","Italy","Japan","Jordan","Kazakhstan","South Korea","Korea","Kuwait","Latvia","Lebanon","Lithuania","Luxembourg","Malaysia","Mexico","Morocco","Netherlands","New Zealand","Norway","Pakistan","Panama","Peru","Philippines","Poland","Portugal","Qatar","Romania","Russia","Saudi Arabia","Serbia","Singapore","Slovakia","Slovenia","South Africa","Spain","Sweden","Switzerland","Thailand","Turkey","Türkiye","ترکیه","Ukraine","United Arab Emirates","United Kingdom","England","United States","USA","America","Uruguay","Venezuela","Vietnam"
    };

    public static bool LooksLikeCountry(string? value)
    {
        var v = (value ?? "").Trim();
        if (v.Length == 0) return false;
        return CountryLikeGroups.Contains(v) || CountryLikeGroups.Contains(NormalizeCountry(v));
    }

    public static string NormalizeGroup(string? group)
    {
        string g = (group ?? "").Trim();
        if (g.Length == 0) return "بدون دسته";
        string l = g.ToLowerInvariant();
        return l switch
        {
            "music" or "موسیقی" => "Music",
            "news" or "اخبار" => "News",
            "sports" or "sport" or "ورزش" => "Sports",
            "movies" or "movie" or "فیلم" => "Movies",
            "kids" or "children" or "کودک" => "Kids",
            "general" or "عمومی" => "General",
            "documentary" or "مستند" => "Documentary",
            "education" or "آموزشی" => "Education",
            "entertainment" or "سرگرمی" => "Entertainment",
            "religious" or "مذهبی" => "Religious",
            _ => g
        };
    }

    public static string NormalizeCountry(string? country)
    {
        string c = (country ?? "").Trim();
        if (c.Length == 0) return "";
        string l = c.ToLowerInvariant();
        return l switch
        {
            "ir" or "iran" or "ایران" => "Iran",
            "de" or "deu" or "germany" or "deutschland" or "آلمان" => "Germany",
            "us" or "usa" or "united states" or "united states of america" or "america" => "United States",
            "gb" or "uk" or "united kingdom" or "england" => "United Kingdom",
            "fr" or "fra" or "france" => "France",
            "it" or "ita" or "italy" => "Italy",
            "es" or "esp" or "spain" => "Spain",
            "tr" or "turkey" or "turkiye" or "ترکیه" => "Turkey",
            "ru" or "russia" => "Russia",
            "cn" or "china" => "China",
            "jp" or "japan" => "Japan",
            "kr" or "korea" or "south korea" => "South Korea",
            "ca" or "canada" => "Canada",
            "br" or "brazil" => "Brazil",
            "in" or "india" => "India",
            _ => c.Length <= 3 ? c.ToUpperInvariant() : c
        };
    }

    private static string GuessCountryFromSource(string sourceName)
    {
        if (sourceName.Contains("ایران") || sourceName.Contains("Iran", StringComparison.OrdinalIgnoreCase)) return "Iran";
        return "";
    }
}

public static class PlaylistUpdater
{
    public record Source(string Name, string Url);

    public static List<Source> GetSources(AppSettings settings)
    {
        var list = new List<Source>();
        if (settings.SourceIptvOrgIran)
            list.Add(new("ایران · iptv-org", "https://iptv-org.github.io/iptv/countries/ir.m3u"));
        if (settings.SourceIptvOrgCategories)
            list.Add(new("دسته‌بندی‌ها · iptv-org", "https://iptv-org.github.io/iptv/index.category.m3u"));
        if (settings.SourceIptvOrgLanguages)
            list.Add(new("زبان‌ها · iptv-org", "https://iptv-org.github.io/iptv/index.language.m3u"));
        if (settings.SourceFreeTv)
            list.Add(new("Free-TV/IPTV", "https://raw.githubusercontent.com/Free-TV/IPTV/master/playlist.m3u8"));
        foreach (var custom in settings.CustomPlaylistSources.Where(x => !string.IsNullOrWhiteSpace(x.Url)))
            list.Add(new(string.IsNullOrWhiteSpace(custom.Name) ? "IPTV سفارشی" : custom.Name, custom.Url));
        return list;
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private static string CacheDir
    {
        get
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TvDesk", "cache");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static async Task<List<Channel>> GetChannelsAsync(AppSettings settings)
    {
        var result = new List<Channel>();
        if (settings.SourceTelewebion) result.AddRange(Telewebion.Channels());

        foreach (var src in GetSources(settings))
        {
            try
            {
                string text = await GetCachedOrDownloadAsync(src, settings.PlaylistRefreshDays);
                result.AddRange(M3uParser.Parse(text, src.Name));
            }
            catch (Exception ex) { TvDesk.Logger.Log($"Playlist source failed: {src.Name} {src.Url}", ex); }
        }

        return result.Where(c => !string.IsNullOrWhiteSpace(c.Url))
            .GroupBy(c => c.Url).Select(g => g.First()).ToList();
    }

    private static async Task<string> GetCachedOrDownloadAsync(Source src, int refreshDays)
    {
        string file = Path.Combine(CacheDir, Sanitize(src.Name + "_" + src.Url.GetHashCode()) + ".m3u");
        if (File.Exists(file))
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(file);
            if (age.TotalDays < refreshDays) return await File.ReadAllTextAsync(file);
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

public static class Telewebion
{
    private static Channel C(string name, string id) => new()
    {
        Name = name,
        Url = "https://ncdn.telewebion.ir/" + id + "/live/playlist.m3u8",
        Group = "Telewebion",
        Country = "Iran",
        Source = "Telewebion"
    };

    public static List<Channel> Channels() => new()
    {
        C("شبکه یک", "tv1"), C("شبکه دو", "tv2"), C("شبکه سه", "tv3"), C("شبکه چهار", "tv4"),
        C("شبکه پنج (تهران)", "tv5"), C("خبر", "irinn"), C("ورزش", "varzesh"), C("نسیم", "nasim"),
        C("تماشا", "hdtest"), C("پویا", "pooya"),
    };
}

public sealed class ChannelLibrary
{
    public List<Channel> Channels { get; } = new();

    public async Task LoadAsync(AppSettings settings)
    {
        Channels.Clear();
        Channels.AddRange(await PlaylistUpdater.GetChannelsAsync(settings));
    }
}

public static class SourceResolver
{
    public static string SanitizeInput(string input)
    {
        input = (input ?? "").Trim();
        // کاربر/Markdown گاهی آخر لینک ':' یا punctuation اضافه می‌کند: youtube.com/live/ID:
        while (input.Length > 0 && ":،,؛;".Contains(input[^1])) input = input[..^1].Trim();
        return input;
    }

    public static async Task<string> ResolveAsync(string input, string quality = "Auto")
    {
        input = SanitizeInput(input);
        if (string.IsNullOrEmpty(input)) throw new ArgumentException("empty input");
        if (File.Exists(input)) return input;
        if (IsDirectStream(input)) return input;
        if (NeedsYtDlp(input)) return await ResolveWithYtDlpAsync(input, quality);
        return input;
    }

    private static bool IsDirectStream(string url)
    {
        string u = url.ToLowerInvariant();
        return u.Contains(".m3u8") || u.EndsWith(".ts") || u.EndsWith(".mp4") || u.EndsWith(".mkv")
            || u.StartsWith("rtmp://") || u.StartsWith("udp://");
    }

    private static bool NeedsYtDlp(string url)
    {
        string u = url.ToLowerInvariant();
        return u.Contains("youtube.com") || u.Contains("youtu.be") || u.Contains("aparat.com")
            || (u.Contains("telewebion.com") && !u.Contains(".m3u8"));
    }

    public static async Task<string> ResolveWithYtDlpAsync(string url, string quality)
    {
        url = SanitizeInput(url);
        string format = quality switch
        {
            "High" => "best[height<=1080]/best",
            "Medium" => "best[height<=720]/best",
            "Low" => "best[height<=480]/best",
            _ => "best"
        };

        string exe = Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe");
        if (!File.Exists(exe)) exe = "yt-dlp.exe";

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $@"--no-playlist --force-ipv4 --socket-timeout 12 -g -f ""{format}"" ""{url}""",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("yt-dlp not found");
        Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = proc.StandardError.ReadToEndAsync();
        Task waitTask = proc.WaitForExitAsync();
        Task finished = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(25)));
        if (finished != waitTask)
        {
            try { proc.Kill(true); } catch { }
            throw new TimeoutException("yt-dlp timed out while resolving stream URL");
        }
        string output = await stdoutTask;
        string error = await stderrTask;
        TvDesk.Logger.Log($"yt-dlp exit={proc.ExitCode} url={url}");

        var first = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .FirstOrDefault(x => x.StartsWith("http", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(first))
            throw new InvalidOperationException("yt-dlp returned no stream URL" + (string.IsNullOrWhiteSpace(error) ? "" : ": " + error.Trim().Split('\n').FirstOrDefault()));
        return first.Trim();
    }
}

public static class ChannelHealth
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public static async Task<bool> IsAliveAsync(string url, CancellationToken ct = default)
    {
        try
        {
            string resolved = await SourceResolver.ResolveAsync(url, "Low");
            using var req = new HttpRequestMessage(HttpMethod.Get, resolved);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
