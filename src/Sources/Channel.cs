using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using TvDesk.Core;

namespace TvDesk.Sources;

public sealed class Channel : Observable
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string? LogoUrl { get; set; }
    public string? TvgId { get; set; }
    public string? UserAgent { get; set; }
    public string? Referrer { get; set; }
    public string? Quality { get; set; }
    public bool GeoBlocked { get; set; }
    public bool NotAlways24x7 { get; set; }
    public bool IsUserLink { get; set; }

    public HashSet<string> Categories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Countries { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Languages { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SourceIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Source of first appearance (used for the subtitle and ordering).</summary>
    public string SourceId { get; set; } = "";
    public string SourceName { get; set; } = "";
    public int SourcePriority { get; set; }
    public int SortIndex { get; set; }

    public bool IsNsfw => Categories.Contains(Sources.Categories.Adult);

    private string? _searchKey;
    public string SearchKey => _searchKey ??= TextUtil.SearchKey(Name + " " + (TvgId ?? ""));

    // ---------- Observable state ----------
    private bool _isFavorite;
    public bool IsFavorite
    {
        get => _isFavorite;
        set { if (Set(ref _isFavorite, value)) { Raise(nameof(FavGlyph)); Raise(nameof(FavBrush)); } }
    }
    public string FavGlyph => _isFavorite ? "" : "";
    public Brush FavBrush => _isFavorite ? Palette.Accent : Palette.Muted;

    private bool? _isAlive;
    public bool? IsAlive
    {
        get => _isAlive;
        set { if (Set(ref _isAlive, value)) { Raise(nameof(HealthBrush)); Raise(nameof(HealthText)); Raise(nameof(HasHealth)); } }
    }
    public bool HasHealth => _isAlive.HasValue;
    public Brush HealthBrush => _isAlive == true ? Palette.Good : _isAlive == false ? Palette.Bad : Palette.Transparent;
    public string HealthText => _isAlive == true ? Loc.T("health_ok") : _isAlive == false ? Loc.T("health_bad") : Loc.T("health_unknown");

    private bool _isPlaying;
    public bool IsPlaying { get => _isPlaying; set => Set(ref _isPlaying, value); }

    // ---------- Display ----------
    public string Initials => TextUtil.Initials(Name);
    public Brush AccentBrush => Palette.ForName(Name);

    public string PrimaryCategory =>
        Categories.Where(c => c != Sources.Categories.Other).OrderBy(k => Sources.Categories.Order(k)).FirstOrDefault()
        ?? (Categories.Count > 0 ? Categories.First() : Sources.Categories.Other);

    public string Subtitle
    {
        get
        {
            var parts = new List<string>(3) { Sources.Categories.Label(PrimaryCategory) };
            string country = Countries.Where(c => c != Sources.Countries.International).FirstOrDefault()
                             ?? Countries.FirstOrDefault() ?? "";
            if (country.Length > 0) parts.Add(Sources.Countries.Name(country));
            if (!string.IsNullOrEmpty(SourceName)) parts.Add(SourceName);
            return string.Join("  ·  ", parts);
        }
    }

    public void RefreshTexts()
    {
        Raise(nameof(Subtitle));
        Raise(nameof(HealthText));
    }

    // ---------- Logo (lazy, only for rows that are actually on screen) ----------
    private ImageSource? _logo;
    private bool _logoRequested;

    public ImageSource? Logo
    {
        get
        {
            if (_logo == null && !_logoRequested && !string.IsNullOrWhiteSpace(LogoUrl))
            {
                _logoRequested = true;
                _ = LoadLogoAsync();
            }
            return _logo;
        }
    }

    public bool HasLogo => _logo != null;

    private async Task LoadLogoAsync()
    {
        try
        {
            var img = await LogoCache.LoadAsync(LogoUrl!);
            if (img == null) return;
            var app = Application.Current;
            if (app == null) return;
            await app.Dispatcher.InvokeAsync(() =>
            {
                _logo = img;
                Raise(nameof(Logo));
                Raise(nameof(HasLogo));
            });
        }
        catch { }
    }

    public override string ToString() => Name;
}

public static class Palette
{
    private static SolidColorBrush B(uint argb)
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        b.Freeze();
        return b;
    }

    public static readonly Brush Accent = B(0xFFFF6A3D);
    public static readonly Brush Muted = B(0xFF5E6676);
    public static readonly Brush Good = B(0xFF3DDC97);
    public static readonly Brush Bad = B(0xFFFF5A6E);
    public static readonly Brush Warn = B(0xFFFFB547);
    public static readonly Brush Text2 = B(0xFFA3ABBB);
    public static readonly Brush Text3 = B(0xFF6C7486);
    public static readonly Brush Transparent = B(0x00000000);

    private static readonly Brush[] Tiles =
    {
        B(0xFF3B4A6B), B(0xFF5B3B6B), B(0xFF6B3B4A), B(0xFF3B6B5B), B(0xFF6B5A3B),
        B(0xFF3B5E6B), B(0xFF4E3B6B), B(0xFF6B463B), B(0xFF2F5D50), B(0xFF54436E),
    };

    public static Brush ForName(string name)
    {
        unchecked
        {
            int h = 17;
            foreach (char ch in name ?? "") h = h * 31 + ch;
            return Tiles[(h & 0x7fffffff) % Tiles.Length];
        }
    }
}

public static class TextUtil
{
    /// <summary>Lower-cased, Arabic/Persian letter variants unified, so "كيهان" finds "کیهان".</summary>
    public static string SearchKey(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s)
        {
            char c = ch switch
            {
                'ي' or 'ى' => 'ی',
                'ك' => 'ک',
                'ة' => 'ه',
                'أ' or 'إ' or 'آ' => 'ا',
                '‌' => ' ',
                _ => char.ToLowerInvariant(ch)
            };
            sb.Append(c);
        }
        return sb.ToString();
    }

    public static string Initials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "TV";
        var words = name.Split(new[] { ' ', '-', '_', '.', '|' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(w => w.Length > 0 && char.IsLetterOrDigit(w[0]))
                        .ToList();
        if (words.Count == 0) return name.Substring(0, 1).ToUpperInvariant();
        if (words.Count == 1)
        {
            string w = words[0];
            return (w.Length >= 2 && char.IsLetter(w[1]) ? w.Substring(0, 2) : w.Substring(0, 1)).ToUpperInvariant();
        }
        return (words[0].Substring(0, 1) + words[1].Substring(0, 1)).ToUpperInvariant();
    }
}
