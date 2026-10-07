using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace TvDesk.Sources;

public sealed class M3uEntry
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public Dictionary<string, string> Attrs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Group { get; set; }
    public string? UserAgent { get; set; }
    public string? Referrer { get; set; }

    public string? Attr(string key) => Attrs.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
}

/// <summary>Tolerant M3U / M3U8 (IPTV) playlist parser.
/// Handles #EXTINF attributes, titles that contain commas, #EXTVLCOPT / #EXTGRP lines,
/// Kodi-style "url|User-Agent=..." suffixes and bare URL lists.</summary>
public static class M3uParser
{
    private static readonly Regex AttrRegex = new(@"([A-Za-z0-9_\-]+)\s*=\s*""([^""]*)""", RegexOptions.Compiled);

    public static bool LooksLikePlaylist(string text)
        => text.Contains("#EXTINF", StringComparison.OrdinalIgnoreCase);

    public static List<M3uEntry> Parse(string text)
    {
        var list = new List<M3uEntry>();
        if (string.IsNullOrWhiteSpace(text)) return list;

        M3uEntry? current = null;
        string? pendingGroup = null, pendingUa = null, pendingRef = null;

        using var reader = new StringReader(text);
        string? raw;
        while ((raw = reader.ReadLine()) != null)
        {
            string line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0) continue;

            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                current = new M3uEntry();
                int colon = line.IndexOf(':');
                string body = colon >= 0 ? line[(colon + 1)..] : "";
                int comma = FindTitleComma(body);
                string attrPart = comma >= 0 ? body[..comma] : body;
                current.Title = comma >= 0 ? body[(comma + 1)..].Trim() : "";
                foreach (Match m in AttrRegex.Matches(attrPart))
                    current.Attrs[m.Groups[1].Value] = m.Groups[2].Value;
                current.Group = current.Attr("group-title");
                current.UserAgent = current.Attr("http-user-agent") ?? current.Attr("user-agent");
                current.Referrer = current.Attr("http-referrer") ?? current.Attr("http-referer") ?? current.Attr("referer");
                continue;
            }

            if (line.StartsWith("#EXTVLCOPT", StringComparison.OrdinalIgnoreCase))
            {
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string opt = line[(colon + 1)..].Trim();
                int eq = opt.IndexOf('=');
                if (eq <= 0) continue;
                string key = opt[..eq].Trim().ToLowerInvariant();
                string val = opt[(eq + 1)..].Trim();
                if (key == "http-user-agent") { if (current != null) current.UserAgent = val; else pendingUa = val; }
                else if (key == "http-referrer" || key == "http-referer") { if (current != null) current.Referrer = val; else pendingRef = val; }
                continue;
            }

            if (line.StartsWith("#EXTGRP:", StringComparison.OrdinalIgnoreCase))
            {
                string g = line[8..].Trim();
                if (current != null) { if (string.IsNullOrEmpty(current.Group)) current.Group = g; }
                else pendingGroup = g;
                continue;
            }

            if (line.StartsWith("#")) continue;

            // A URL line.
            var entry = current ?? new M3uEntry();
            string url = line;
            int pipe = url.IndexOf('|');
            if (pipe > 0)
            {
                ParseKodiHeaders(url[(pipe + 1)..], entry);
                url = url[..pipe];
            }
            entry.Url = url.Trim();
            entry.Group ??= pendingGroup;
            entry.UserAgent ??= pendingUa;
            entry.Referrer ??= pendingRef;
            if (string.IsNullOrWhiteSpace(entry.Title)) entry.Title = entry.Attr("tvg-name") ?? TitleFromUrl(entry.Url);
            if (entry.Url.Length > 0) list.Add(entry);

            current = null;
            pendingGroup = pendingUa = pendingRef = null;
        }
        return list;
    }

    /// <summary>First comma that is not inside a quoted attribute value.</summary>
    private static int FindTitleComma(string s)
    {
        bool quoted = false;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '"') quoted = !quoted;
            else if (s[i] == ',' && !quoted) return i;
        }
        return -1;
    }

    private static void ParseKodiHeaders(string headerPart, M3uEntry e)
    {
        foreach (var pair in headerPart.Split('&'))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            string k = pair[..eq].Trim().ToLowerInvariant();
            string v = Uri.UnescapeDataString(pair[(eq + 1)..].Trim());
            if (k == "user-agent") e.UserAgent = v;
            else if (k == "referer" || k == "referrer") e.Referrer = v;
        }
    }

    public static string TitleFromUrl(string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var u))
            {
                string last = Path.GetFileNameWithoutExtension(u.AbsolutePath);
                if (last.Length > 2 && !last.Equals("playlist", StringComparison.OrdinalIgnoreCase)
                    && !last.Equals("index", StringComparison.OrdinalIgnoreCase)
                    && !last.Equals("master", StringComparison.OrdinalIgnoreCase))
                    return u.Host + " · " + last;
                return u.Host;
            }
        }
        catch { }
        return url;
    }
}

/// <summary>Cleans display names: "Foo TV (1080p) [Geo-blocked]" → "Foo TV" + quality + flags.</summary>
public static class NameCleaner
{
    private static readonly Regex QualityRegex = new(@"\((\d{3,4}[pi])\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BracketRegex = new(@"\[([^\]]+)\]", RegexOptions.Compiled);

    public static string Clean(string raw, out string? quality, out bool geoBlocked, out bool notAlways)
    {
        quality = null;
        geoBlocked = false;
        notAlways = false;
        string s = raw ?? "";

        var q = QualityRegex.Match(s);
        if (q.Success)
        {
            quality = q.Groups[1].Value.ToLowerInvariant();
            s = s.Remove(q.Index, q.Length);
        }

        foreach (Match m in BracketRegex.Matches(s))
        {
            string tag = m.Groups[1].Value.ToLowerInvariant();
            if (tag.Contains("geo")) geoBlocked = true;
            if (tag.Contains("not 24/7")) notAlways = true;
        }
        s = BracketRegex.Replace(s, "");

        // Free-TV markers: Ⓖ geo-blocked, Ⓢ SD, Ⓨ YouTube, Ⓣ Twitch ... (circled capital letters U+24B6..U+24CF)
        if (s.IndexOf('Ⓖ') >= 0) geoBlocked = true;
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char ch in s)
            if (ch < 'Ⓐ' || ch > 'ⓩ') sb.Append(ch);
        s = sb.ToString();

        s = Regex.Replace(s, @"\s{2,}", " ").Trim().Trim('-', '|', '·').Trim();
        return s.Length == 0 ? (raw ?? "").Trim() : s;
    }
}
