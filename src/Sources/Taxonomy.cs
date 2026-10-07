using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TvDesk.Core;

namespace TvDesk.Sources;

public sealed record CategoryInfo(string Key, string Fa, string En, string Glyph, int Order);

/// <summary>Canonical channel categories. Playlists use wildly different group names ("News", "اخبار", "News;Kids"...);
/// everything is mapped to one of these keys. Unknown groups are kept as-is (custom categories).</summary>
public static class Categories
{
    public const string All = "*";
    public const string Other = "other";
    public const string Adult = "xxx";

    private static readonly CategoryInfo[] Known =
    {
        new("ambient",       "آرامش و منظره",   "Ambient & scenic", "", 1),
        new("general",       "عمومی",           "General",          "", 2),
        new("news",          "خبری",            "News",             "", 3),
        new("music",         "موسیقی",          "Music",            "", 4),
        new("movies",        "فیلم و سینما",     "Movies",           "", 5),
        new("series",        "سریال",           "Series",           "", 6),
        new("sports",        "ورزشی",           "Sports",           "", 7),
        new("kids",          "کودک",            "Kids",             "", 8),
        new("animation",     "انیمیشن",         "Animation",        "", 9),
        new("documentary",   "مستند",           "Documentary",      "", 10),
        new("entertainment", "سرگرمی",          "Entertainment",    "", 11),
        new("comedy",        "کمدی",            "Comedy",           "", 12),
        new("education",     "آموزشی",          "Education",        "", 13),
        new("science",       "علم و فناوری",     "Science",          "", 14),
        new("culture",       "فرهنگی",          "Culture",          "", 15),
        new("lifestyle",     "سبک زندگی",       "Lifestyle",        "", 16),
        new("travel",        "سفر و گردشگری",    "Travel",           "", 17),
        new("outdoor",       "طبیعت",           "Outdoor",          "", 18),
        new("cooking",       "آشپزی",           "Cooking",          "", 19),
        new("family",        "خانواده",         "Family",           "", 20),
        new("classic",       "کلاسیک",          "Classic",          "", 21),
        new("business",      "اقتصادی",         "Business",         "", 22),
        new("auto",          "خودرو",           "Auto",             "", 23),
        new("weather",       "آب و هوا",        "Weather",          "", 24),
        new("religious",     "مذهبی",           "Religious",        "", 25),
        new("legislative",   "پارلمانی",        "Legislative",      "", 26),
        new("public",        "عمومی-دولتی",      "Public",           "", 27),
        new("shop",          "خرید",            "Shopping",         "", 28),
        new("relax",         "آرامش",           "Relax",            "", 29),
        new("interactive",   "تعاملی",          "Interactive",      "", 30),
        new(Other,           "سایر",            "Other",            "", 90),
        new(Adult,           "بزرگسالان",        "Adult",            "", 99),
    };

    private static readonly Dictionary<string, CategoryInfo> ByKey = Known.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> Aliases = BuildAliases();

    private static Dictionary<string, string> BuildAliases()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in Known)
        {
            d[c.Key] = c.Key;
            d[c.En] = c.Key;
            d[c.Fa] = c.Key;
        }
        void A(string key, params string[] names) { foreach (var n in names) d[n] = key; }
        A("general", "عمومي", "general tv", "generalist", "tv");
        A("news", "اخبار", "خبر", "noticias", "nachrichten", "actualités", "info", "news & politics");
        A("music", "موزیک", "music tv", "radio", "رادیو", "musica", "música", "musik", "lofi");
        A("movies", "movie", "film", "films", "فیلم", "سینما", "cinema", "vod movies", "vod");
        A("series", "serie", "series & shows", "tv shows", "shows");
        A("sports", "sport", "ورزش", "deportes", "football", "soccer", "فوتبال");
        A("kids", "children", "child", "کودکان", "کودک و نوجوان", "cartoon", "cartoons", "kinder", "infantil");
        A("animation", "anime", "انیمه");
        A("documentary", "documentaries", "docs", "مستندات");
        A("entertainment", "سرگرمی و تفریح", "entretenimiento", "unterhaltung", "variety");
        A("education", "educational", "آموزش", "learning");
        A("science", "tech", "technology", "علمی");
        A("culture", "arts", "art", "هنری", "هنر و فرهنگ");
        A("travel", "tourism", "گردشگری");
        A("outdoor", "nature", "طبیعت‌گردی", "wildlife");
        A("religious", "religion", "مذهب", "faith", "christian", "islamic", "اسلامی", "quran", "قرآن");
        A("business", "finance", "economy", "اقتصاد");
        A("ambient", "scenic", "webcam", "webcams", "live cams", "relaxing", "chill");
        A(Other, "undefined", "uncategorized", "بدون دسته", "misc", "none", "others");
        A(Adult, "adult", "xxx", "18+", "nsfw");
        return d;
    }

    /// <summary>Returns the canonical key for a raw group name, or null when it is not a known category.</summary>
    public static string? Normalize(string? raw)
    {
        string g = (raw ?? "").Trim();
        if (g.Length == 0) return null;
        return Aliases.TryGetValue(g, out var key) ? key : null;
    }

    public static bool IsKnown(string key) => ByKey.ContainsKey(key);

    public static string Label(string key)
    {
        if (key == All) return Loc.T("cat_all");
        if (ByKey.TryGetValue(key, out var c)) return Loc.I.IsRtl ? c.Fa : c.En;
        return key;
    }

    public static string Glyph(string key)
    {
        if (key == All) return "";
        return ByKey.TryGetValue(key, out var c) ? c.Glyph : "";
    }

    public static int Order(string key) => ByKey.TryGetValue(key, out var c) ? c.Order : 50;

    public static IEnumerable<CategoryInfo> KnownCategories => Known.Where(c => c.Key != Adult);
}

/// <summary>Country codes (ISO 3166-1 alpha-2) and names.</summary>
public static class Countries
{
    public const string International = "INT";

    private static readonly Dictionary<string, string> Persian = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IR"] = "ایران", ["AF"] = "افغانستان", ["TJ"] = "تاجیکستان", ["US"] = "آمریکا", ["GB"] = "بریتانیا",
        ["DE"] = "آلمان", ["FR"] = "فرانسه", ["IT"] = "ایتالیا", ["ES"] = "اسپانیا", ["TR"] = "ترکیه",
        ["RU"] = "روسیه", ["CN"] = "چین", ["JP"] = "ژاپن", ["KR"] = "کره جنوبی", ["IN"] = "هند",
        ["PK"] = "پاکستان", ["IQ"] = "عراق", ["SA"] = "عربستان", ["AE"] = "امارات", ["QA"] = "قطر",
        ["CA"] = "کانادا", ["AU"] = "استرالیا", ["BR"] = "برزیل", ["AR"] = "آرژانتین", ["MX"] = "مکزیک",
        ["NL"] = "هلند", ["SE"] = "سوئد", ["NO"] = "نروژ", ["CH"] = "سوئیس", ["AT"] = "اتریش",
        ["GR"] = "یونان", ["PT"] = "پرتغال", ["AZ"] = "آذربایجان", ["AM"] = "ارمنستان", ["GE"] = "گرجستان",
        ["EG"] = "مصر", ["LB"] = "لبنان", ["SY"] = "سوریه", ["KW"] = "کویت", ["BH"] = "بحرین",
        ["OM"] = "عمان", ["UA"] = "اوکراین", ["PL"] = "لهستان", ["BE"] = "بلژیک", ["DK"] = "دانمارک",
        ["FI"] = "فنلاند", ["IE"] = "ایرلند", ["HU"] = "مجارستان", ["RO"] = "رومانی", ["CZ"] = "چک",
        ["ID"] = "اندونزی", ["MY"] = "مالزی", ["TH"] = "تایلند", ["PH"] = "فیلیپین", ["VN"] = "ویتنام",
        ["KZ"] = "قزاقستان", ["UZ"] = "ازبکستان", ["TM"] = "ترکمنستان", ["KG"] = "قرقیزستان", ["JO"] = "اردن",
        ["IL"] = "اسرائیل", ["PS"] = "فلسطین", ["YE"] = "یمن", ["LY"] = "لیبی", ["TN"] = "تونس",
        ["DZ"] = "الجزایر", ["MA"] = "مراکش", ["SD"] = "سودان", ["NG"] = "نیجریه", ["ZA"] = "آفریقای جنوبی",
        ["CL"] = "شیلی", ["CO"] = "کلمبیا", ["PE"] = "پرو", ["VE"] = "ونزوئلا", ["CU"] = "کوبا",
        ["RS"] = "صربستان", ["HR"] = "کرواسی", ["BG"] = "بلغارستان", ["SK"] = "اسلواکی", ["SI"] = "اسلوونی",
        ["AL"] = "آلبانی", ["BA"] = "بوسنی", ["MK"] = "مقدونیه شمالی", ["CY"] = "قبرس", ["MT"] = "مالت",
        ["LU"] = "لوکزامبورگ", ["IS"] = "ایسلند", ["EE"] = "استونی", ["LV"] = "لتونی", ["LT"] = "لیتوانی",
        ["BY"] = "بلاروس", ["MD"] = "مولداوی", ["MN"] = "مغولستان", ["HK"] = "هنگ‌کنگ", ["TW"] = "تایوان",
        ["SG"] = "سنگاپور", ["NZ"] = "نیوزیلند", ["BD"] = "بنگلادش", ["LK"] = "سری‌لانکا", ["NP"] = "نپال",
        ["ET"] = "اتیوپی", ["KE"] = "کنیا", ["GH"] = "غنا", ["DO"] = "دومینیکن", ["EC"] = "اکوادور",
        ["BO"] = "بولیوی", ["PY"] = "پاراگوئه", ["UY"] = "اروگوئه", ["CR"] = "کاستاریکا", ["PA"] = "پاناما",
        ["GT"] = "گواتمالا", ["HN"] = "هندوراس", ["SV"] = "السالوادور", ["NI"] = "نیکاراگوئه", ["PR"] = "پورتوریکو",
        ["XK"] = "کوزوو", ["ME"] = "مونته‌نگرو", ["AD"] = "آندورا", ["MC"] = "موناکو", ["SM"] = "سان‌مارینو",
        [International] = "بین‌المللی",
    };

    private static readonly Dictionary<string, string> EnglishOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        [International] = "International", ["XK"] = "Kosovo", ["HK"] = "Hong Kong", ["TW"] = "Taiwan",
        ["MO"] = "Macao", ["PS"] = "Palestine", ["KR"] = "South Korea", ["KP"] = "North Korea",
    };

    private static readonly object Gate = new();
    private static Dictionary<string, string>? _nameToCode;
    private static readonly Dictionary<string, string?> EnglishCache = new(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> NameToCode
    {
        get
        {
            lock (Gate)
            {
                if (_nameToCode != null) return _nameToCode;
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
                    {
                        try
                        {
                            var r = new RegionInfo(culture.Name);
                            string code = r.TwoLetterISORegionName.ToUpperInvariant();
                            if (code.Length != 2) continue;
                            d[r.EnglishName] = code;
                            d[r.NativeName] = code;
                            int paren = r.EnglishName.IndexOf(" (", StringComparison.Ordinal);
                            if (paren > 0) d[r.EnglishName[..paren]] = code;
                        }
                        catch { }
                    }
                }
                catch { }
                foreach (var kv in Persian) d[kv.Value] = kv.Key;
                foreach (var kv in EnglishOverrides) d[kv.Value] = kv.Key;
                void A(string code, params string[] names) { foreach (var n in names) d[n] = code; }
                A("US", "USA", "United States of America", "America", "U.S.");
                A("GB", "UK", "England", "Great Britain", "Britain", "Scotland", "Wales", "Northern Ireland");
                A("KR", "Korea", "Korea, Republic of", "Republic of Korea");
                A("TR", "Turkey", "Türkiye", "Turkiye");
                A("RU", "Russia", "Russian Federation");
                A("CZ", "Czech Republic", "Czechia");
                A("IR", "Iran", "Persia", "Iran, Islamic Republic of");
                A("MK", "Macedonia", "North Macedonia");
                A("VN", "Vietnam", "Viet Nam");
                A("BA", "Bosnia", "Bosnia and Herzegovina", "Bosnia & Herzegovina");
                A("CI", "Ivory Coast", "Côte d'Ivoire");
                A("AE", "UAE", "United Arab Emirates");
                A("SA", "Saudi Arabia", "KSA");
                A("CD", "DR Congo", "Congo (DRC)");
                A("FO", "Faroe Islands");
                A("GL", "Greenland");
                A("TT", "Trinidad", "Trinidad and Tobago", "Trinidad & Tobago");
                A("MO", "Macau", "Macao");
                A(International, "International", "Worldwide", "World", "Global");
                _nameToCode = d;
                return d;
            }
        }
    }

    /// <summary>Normalizes a code ("ir", "UK") or a name ("Iran", "日本 / Japan") to an upper-case ISO2 code, or "" if unknown.</summary>
    public static string Normalize(string? value)
    {
        string v = (value ?? "").Trim();
        if (v.Length == 0) return "";
        if (v.Length == 2 && char.IsLetter(v[0]) && char.IsLetter(v[1]))
        {
            string code = v.ToUpperInvariant();
            if (code == "UK") return "GB";
            return IsValidCode(code) ? code : "";
        }
        if (v.Equals("int", StringComparison.OrdinalIgnoreCase)) return International;
        if (NameToCode.TryGetValue(v, out var c)) return c;
        if (v.Contains('/'))
        {
            foreach (var part in v.Split('/'))
            {
                string p = part.Trim();
                if (p.Length > 0 && NameToCode.TryGetValue(p, out var c2)) return c2;
            }
        }
        return "";
    }

    public static bool IsValidCode(string code)
    {
        if (code == International) return true;
        return EnglishName(code) != null;
    }

    private static string? EnglishName(string code)
    {
        lock (Gate)
        {
            if (EnglishCache.TryGetValue(code, out var cached)) return cached;
            string? name = null;
            if (EnglishOverrides.TryGetValue(code, out var o)) name = o;
            else
            {
                try
                {
                    var r = new RegionInfo(code);
                    name = r.EnglishName;
                }
                catch { name = null; }
            }
            EnglishCache[code] = name;
            return name;
        }
    }

    public static string Name(string code)
    {
        if (string.IsNullOrEmpty(code)) return "";
        if (Loc.I.IsRtl && Persian.TryGetValue(code, out var fa)) return fa;
        return EnglishName(code) ?? code;
    }
}
