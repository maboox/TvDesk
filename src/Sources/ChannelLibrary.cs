using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TvDesk.Core;

namespace TvDesk.Sources;

/// <summary>Load status of one source, shown on the Sources page.</summary>
public sealed class SourceStatus
{
    public bool Loading { get; set; }
    public int Count { get; set; }
    public string? Error { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool FromStaleCache { get; set; }
}

/// <summary>Downloads (with caching), parses and merges all enabled sources into one channel list.</summary>
public sealed class ChannelLibrary
{
    public const string MineSourceId = "mine";

    public IReadOnlyList<Channel> Channels { get; private set; } = Array.Empty<Channel>();
    public Dictionary<string, SourceStatus> Status { get; private set; } = new();

    private static readonly Regex TvgIdCountry = new(@"\.([a-z]{2})(?:@|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>All sources that should be loaded: enabled catalog entries + enabled custom playlists.</summary>
    public static List<SourceDef> EnabledSources(AppSettings settings, Catalog catalog)
    {
        var list = catalog.Sources.Where(d => catalog.IsEnabled(settings, d)).ToList();
        foreach (var cs in settings.CustomSources.Where(c => c.Enabled))
        {
            list.Add(new SourceDef
            {
                Id = "custom:" + cs.Id,
                Name = new LocalText { Fa = cs.Name, En = cs.Name },
                Priority = 30,
                IsCustom = true,
                Playlists = new List<PlaylistRef> { new() { Url = cs.Url, GroupMeans = "auto" } }
            });
        }
        return list;
    }

    public async Task LoadAsync(AppSettings settings, Catalog catalog, HealthStore health, bool forceRefresh, CancellationToken ct)
    {
        var defs = EnabledSources(settings, catalog);
        var status = defs.ToDictionary(d => d.Id, _ => new SourceStatus { Loading = true });
        Status = status;

        // 1) Fetch every playlist text in parallel.
        var fetches = new List<(SourceDef def, PlaylistRef pl, Task<FetchResult> task)>();
        foreach (var def in defs)
            foreach (var pl in def.Playlists.Where(p => !string.IsNullOrWhiteSpace(p.Url)))
                fetches.Add((def, pl, FetchAsync(pl.Url, settings.RefreshHours, forceRefresh, ct)));

        try { await Task.WhenAll(fetches.Select(f => f.task)); }
        catch { /* individual failures are recorded below */ }
        ct.ThrowIfCancellationRequested();

        // 2) Parse + merge off the UI thread.
        var favorites = new HashSet<string>(settings.Favorites);
        var myLinks = settings.MyLinks.ToList();
        var result = await Task.Run(() =>
        {
            var byUrl = new Dictionary<string, Channel>(StringComparer.Ordinal);
            var ordered = new List<Channel>();

            Channel? Add(Channel c)
            {
                if (byUrl.TryGetValue(c.Url, out var existing))
                {
                    Merge(existing, c);
                    return existing;
                }
                byUrl[c.Url] = c;
                ordered.Add(c);
                return c;
            }

            // User's own links first.
            foreach (var link in myLinks)
            {
                var c = new Channel
                {
                    Name = string.IsNullOrWhiteSpace(link.Name) ? link.Url : link.Name,
                    Url = link.Url.Trim(),
                    LogoUrl = link.LogoUrl,
                    IsUserLink = true,
                    SourceId = MineSourceId,
                    SourceName = Loc.T("src_mine"),
                    SourcePriority = -1,
                };
                c.Categories.Add(string.IsNullOrWhiteSpace(link.Category) ? Categories.Other : link.Category!);
                c.SourceIds.Add(MineSourceId);
                Add(c);
            }

            foreach (var def in defs.OrderBy(d => d.Priority))
            {
                var st = status[def.Id];
                int count = 0;

                foreach (var ic in def.Channels)
                {
                    if (string.IsNullOrWhiteSpace(ic.Url)) continue;
                    var c = new Channel
                    {
                        Name = ic.Name,
                        Url = ic.Url.Trim(),
                        LogoUrl = string.IsNullOrWhiteSpace(ic.Logo) ? null : ic.Logo,
                        SourceId = def.Id,
                        SourceName = def.DisplayName,
                        SourcePriority = def.Priority,
                    };
                    c.Categories.Add(Categories.Normalize(ic.Category) ?? (string.IsNullOrWhiteSpace(ic.Category) ? Categories.Other : ic.Category!));
                    string cc = Countries.Normalize(ic.Country);
                    if (cc.Length > 0) c.Countries.Add(cc);
                    if (!string.IsNullOrWhiteSpace(ic.Language)) c.Languages.Add(ic.Language!);
                    c.SourceIds.Add(def.Id);
                    Add(c);
                    count++;
                }

                // Normal playlists first, then the ones that only enrich.
                foreach (var f in fetches.Where(x => x.def == def).OrderBy(x => x.pl.EnrichOnly ? 1 : 0))
                {
                    FetchResult fr;
                    try { fr = f.task.Result; }
                    catch (AggregateException ae) { fr = FetchResult.Failed(ae.InnerException?.Message ?? ae.Message); }

                    if (fr.Text == null)
                    {
                        st.Error = fr.Error ?? "download failed";
                        continue;
                    }
                    if (fr.Stale) st.FromStaleCache = true;
                    st.UpdatedAt = fr.UpdatedAt;

                    foreach (var e in M3uParser.Parse(fr.Text))
                    {
                        var c = Build(e, f.pl, def);
                        if (c == null) continue;
                        if (f.pl.EnrichOnly)
                        {
                            if (byUrl.TryGetValue(c.Url, out var existing)) Merge(existing, c);
                            continue;
                        }
                        Add(c);
                        count++;
                    }
                }
                st.Count = count;
                st.Loading = false;
            }

            // Post-process: defaults, favourites, health.
            int i = 0;
            foreach (var c in ordered)
            {
                if (c.Categories.Count == 0) c.Categories.Add(Categories.Other);
                c.IsFavorite = favorites.Contains(c.Url);
                c.IsAlive = health.Get(c.Url);
                c.SortIndex = i++;
            }
            return ordered;
        }, ct);

        foreach (var st in status.Values) st.Loading = false;
        Channels = result;
        Logger.Log($"Library loaded: {result.Count} channels from {defs.Count} sources");
    }

    /// <summary>Adds a user link without reloading everything.</summary>
    public Channel AddUserLink(SavedLink link)
    {
        var existing = Channels.FirstOrDefault(c => c.Url == link.Url);
        if (existing != null)
        {
            existing.IsUserLink = true;
            existing.SourceIds.Add(MineSourceId);
            return existing;
        }
        var c = new Channel
        {
            Name = link.Name,
            Url = link.Url,
            LogoUrl = link.LogoUrl,
            IsUserLink = true,
            SourceId = MineSourceId,
            SourceName = Loc.T("src_mine"),
            SourcePriority = -1,
            SortIndex = -1,
        };
        c.Categories.Add(string.IsNullOrWhiteSpace(link.Category) ? Categories.Other : link.Category!);
        c.SourceIds.Add(MineSourceId);
        var list = new List<Channel>(Channels.Count + 1) { c };
        list.AddRange(Channels);
        Channels = list;
        return c;
    }

    public void RemoveUserLink(string url)
    {
        var c = Channels.FirstOrDefault(x => x.Url == url);
        if (c == null) return;
        if (c.SourceIds.Count <= 1)
        {
            Channels = Channels.Where(x => !ReferenceEquals(x, c)).ToList();
        }
        else
        {
            c.IsUserLink = false;
            c.SourceIds.Remove(MineSourceId);
        }
    }

    // ----------------------------------------------------------------------------------------------

    private static Channel? Build(M3uEntry e, PlaylistRef pl, SourceDef def)
    {
        string url = e.Url.Trim();
        if (url.Length == 0 || url.StartsWith("#")) return null;

        string rawName = string.IsNullOrWhiteSpace(e.Title) ? (e.Attr("tvg-name") ?? url) : e.Title;
        string name = NameCleaner.Clean(rawName, out var quality, out var geo, out var notAlways);

        var c = new Channel
        {
            Name = name,
            Url = url,
            LogoUrl = e.Attr("tvg-logo"),
            TvgId = e.Attr("tvg-id"),
            UserAgent = e.UserAgent,
            Referrer = e.Referrer,
            Quality = quality,
            GeoBlocked = geo,
            NotAlways24x7 = notAlways,
            SourceId = def.Id,
            SourceName = def.DisplayName,
            SourcePriority = def.Priority,
        };
        c.SourceIds.Add(def.Id);

        // Countries: explicit attribute, playlist-wide country, tvg-id suffix ("CNN.us@SD").
        foreach (var part in Split(e.Attr("tvg-country")))
        {
            string cc = Countries.Normalize(part);
            if (cc.Length > 0) c.Countries.Add(cc);
        }
        if (!string.IsNullOrWhiteSpace(pl.Country))
        {
            string cc = Countries.Normalize(pl.Country);
            if (cc.Length > 0) c.Countries.Add(cc);
        }
        if (c.TvgId != null)
        {
            var m = TvgIdCountry.Match(c.TvgId);
            if (m.Success)
            {
                string cc = Countries.Normalize(m.Groups[1].Value);
                if (cc.Length > 0) c.Countries.Add(cc);
            }
        }
        foreach (var lang in Split(e.Attr("tvg-language"))) c.Languages.Add(lang);

        // group-title meaning depends on the playlist.
        foreach (var g in Split(e.Group))
        {
            switch (pl.GroupMeans.ToLowerInvariant())
            {
                case "category":
                    AddCategoryOrCountry(c, g);
                    break;
                case "country":
                {
                    string cc = Countries.Normalize(g);
                    if (cc.Length > 0) c.Countries.Add(cc);
                    break;
                }
                case "language":
                    if (!g.Equals("Undefined", StringComparison.OrdinalIgnoreCase)) c.Languages.Add(g);
                    break;
                default:
                    AddAuto(c, g);
                    break;
            }
        }
        return c;
    }

    /// <summary>"News (AR)" → "News", "Documentaries (EN)" → "Documentaries".</summary>
    private static string StripSuffix(string g)
    {
        int p = g.LastIndexOf(" (", StringComparison.Ordinal);
        return p > 0 && g.EndsWith(")") ? g[..p].Trim() : g;
    }

    private static void AddCategoryOrCountry(Channel c, string g)
    {
        g = StripSuffix(g);
        string? key = Categories.Normalize(g);
        if (key != null) { c.Categories.Add(key); return; }
        if (g.Length > 2)
        {
            string cc = Countries.Normalize(g);
            if (cc.Length > 0) { c.Countries.Add(cc); return; }
        }
        c.Categories.Add(g);
    }

    private static void AddAuto(Channel c, string g)
    {
        g = StripSuffix(g);
        if (g.Equals("UK", StringComparison.OrdinalIgnoreCase) || g.Equals("US", StringComparison.OrdinalIgnoreCase))
        {
            c.Countries.Add(g.Equals("UK", StringComparison.OrdinalIgnoreCase) ? "GB" : "US");
            return;
        }
        string? key = Categories.Normalize(g);
        if (key != null) { c.Categories.Add(key); return; }

        if (g.StartsWith("VOD", StringComparison.OrdinalIgnoreCase))
        {
            c.Categories.Add("movies");
            string rest = g[3..].Trim(' ', '-', ':');
            string vc = Countries.Normalize(rest);
            if (vc.Length > 0) c.Countries.Add(vc);
            return;
        }
        if (g.Length > 2)
        {
            string cc = Countries.Normalize(g);
            if (cc.Length > 0) { c.Countries.Add(cc); return; }
        }
        c.Categories.Add(g);
    }

    private static IEnumerable<string> Split(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) yield break;
        foreach (var p in value.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string t = p.Trim();
            if (t.Length > 0) yield return t;
        }
    }

    private static void Merge(Channel target, Channel other)
    {
        target.Categories.UnionWith(other.Categories);
        target.Countries.UnionWith(other.Countries);
        target.Languages.UnionWith(other.Languages);
        target.SourceIds.UnionWith(other.SourceIds);
        if (target.Categories.Count > 1) target.Categories.Remove(Categories.Other);
        if (string.IsNullOrWhiteSpace(target.LogoUrl)) target.LogoUrl = other.LogoUrl;
        target.UserAgent ??= other.UserAgent;
        target.Referrer ??= other.Referrer;
        target.Quality ??= other.Quality;
        target.TvgId ??= other.TvgId;
        target.GeoBlocked |= other.GeoBlocked;
    }

    // ---------------------------------------------------------------------------------------------- cache

    private sealed class FetchResult
    {
        public string? Text { get; init; }
        public string? Error { get; init; }
        public bool Stale { get; init; }
        public DateTime? UpdatedAt { get; init; }
        public static FetchResult Failed(string error) => new() { Error = error };
    }

    public static string CacheFileFor(string url)
    {
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(url));
        return Path.Combine(AppPaths.Cache, Convert.ToHexString(hash).ToLowerInvariant() + ".m3u");
    }

    private static async Task<FetchResult> FetchAsync(string url, int refreshHours, bool force, CancellationToken ct)
    {
        url = url.Trim();
        try
        {
            // Local playlist file.
            if (!url.Contains("://") || url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                string path = url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(url).LocalPath : url;
                if (!File.Exists(path)) return FetchResult.Failed("file not found");
                return new FetchResult { Text = await File.ReadAllTextAsync(path, ct), UpdatedAt = File.GetLastWriteTime(path) };
            }

            string file = CacheFileFor(url);
            bool haveCache = File.Exists(file);
            if (haveCache && !force)
            {
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(file);
                if (age.TotalHours < refreshHours)
                    return new FetchResult { Text = await File.ReadAllTextAsync(file, ct), UpdatedAt = File.GetLastWriteTime(file) };
            }

            try
            {
                string text = await Http.Client.GetStringAsync(url, ct);
                if (text.Length < 10 || (!M3uParser.LooksLikePlaylist(text) && !text.Contains("://")))
                    throw new InvalidDataException("not an M3U playlist");
                string tmp = file + ".tmp";
                await File.WriteAllTextAsync(tmp, text, ct);
                File.Move(tmp, file, true);
                Logger.Log($"Downloaded playlist {url} ({text.Length / 1024} KB)");
                return new FetchResult { Text = text, UpdatedAt = DateTime.Now };
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Logger.Log($"Playlist download failed: {url}: {ex.Message}");
                if (haveCache)
                    return new FetchResult { Text = await File.ReadAllTextAsync(file, ct), Stale = true, UpdatedAt = File.GetLastWriteTime(file) };
                return FetchResult.Failed(ex is HttpRequestException hre && hre.StatusCode != null
                    ? $"HTTP {(int)hre.StatusCode.Value}"
                    : ex.Message);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return FetchResult.Failed(ex.Message);
        }
    }

    public static void ClearCache()
    {
        try
        {
            foreach (var f in Directory.GetFiles(AppPaths.Cache)) { try { File.Delete(f); } catch { } }
            foreach (var f in Directory.GetFiles(AppPaths.Logos)) { try { File.Delete(f); } catch { } }
        }
        catch { }
    }
}
