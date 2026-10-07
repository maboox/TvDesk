using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TvDesk.Core;

/// <summary>What to do with the wallpaper when something happens (another app focused, fullscreen game, ...).
/// Order matters: a higher value wins when several triggers are active.</summary>
public enum AutoAction { None = 0, Mute = 1, Pause = 2 }

public enum FitMode { Fill, Fit, Stretch }

public sealed class SavedLink
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Category { get; set; }
    public string? LogoUrl { get; set; }
}

public sealed class CustomSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 2;

    // General
    public string Language { get; set; } = "fa";
    public bool OnboardingComplete { get; set; }
    public bool AutoStart { get; set; }
    public bool ResumeOnStart { get; set; } = true;
    public bool TrayHintShown { get; set; }

    // Audio / video
    public int Volume { get; set; } = 60;
    public bool Muted { get; set; }
    public string Quality { get; set; } = "auto";      // auto | 1080 | 720 | 480
    public FitMode Fit { get; set; } = FitMode.Fill;
    public string Monitor { get; set; } = "primary";   // primary | all | \\.\DISPLAYn
    public bool HardwareDecoding { get; set; } = true;
    public string VideoOutput { get; set; } = "gdi";   // gdi (most compatible) | d3d11 | auto

    // Smart pause
    public AutoAction OnAppFocused { get; set; } = AutoAction.None;
    public AutoAction OnMaximized { get; set; } = AutoAction.None;
    public AutoAction OnFullscreen { get; set; } = AutoAction.Pause;
    public AutoAction OnBattery { get; set; } = AutoAction.None;
    public AutoAction OnLocked { get; set; } = AutoAction.Pause;

    // Library
    public bool HideAdult { get; set; } = true;
    public bool HideGeoBlocked { get; set; }
    public bool HideBroken { get; set; } = true;
    public int RefreshHours { get; set; } = 48;
    public Dictionary<string, bool> Sources { get; set; } = new();
    public List<CustomSource> CustomSources { get; set; } = new();
    public List<SavedLink> MyLinks { get; set; } = new();
    public List<string> Favorites { get; set; } = new();
    public List<string> Recent { get; set; } = new();
    public string? LastUrl { get; set; }
    public string? LastName { get; set; }

    // Network
    public ProxyMode Proxy { get; set; } = ProxyMode.System;
    public string CustomProxy { get; set; } = "";

    // Tools
    public DateTime YtDlpLastUpdate { get; set; }

    public void Normalize()
    {
        Language = Language == "en" ? "en" : "fa";
        Volume = Math.Clamp(Volume, 0, 100);
        Quality = (Quality ?? "auto").ToLowerInvariant() switch
        {
            "1080" or "high" => "1080",
            "720" or "medium" => "720",
            "480" or "low" => "480",
            _ => "auto"
        };
        if (string.IsNullOrWhiteSpace(Monitor)) Monitor = "primary";
        if (VideoOutput != "d3d11" && VideoOutput != "auto") VideoOutput = "gdi";
        if (RefreshHours < 1) RefreshHours = 48;
        Sources ??= new();
        CustomSources ??= new();
        MyLinks ??= new();
        Favorites ??= new();
        Recent ??= new();
        CustomProxy ??= "";
        Favorites = Favorites.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        Recent = Recent.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Take(30).ToList();
        MyLinks = MyLinks.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Url)).ToList();
        CustomSources = CustomSources.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Url)).ToList();
    }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        string path = AppPaths.SettingsFile;
        try
        {
            if (File.Exists(path))
            {
                string text = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(text, Options) ?? new AppSettings();
                JsonNode? node = JsonNode.Parse(text);
                if (node is JsonObject obj && !obj.ContainsKey("SchemaVersion"))
                    MigrateV1(obj, settings);
                settings.Normalize();
                return settings;
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Settings load failed — starting with defaults", ex);
            try { File.Copy(path, path + ".broken", true); } catch { }
        }
        var fresh = new AppSettings();
        fresh.Normalize();
        return fresh;
    }

    public static void Save(AppSettings settings)
    {
        string path = AppPaths.SettingsFile;
        try
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
            File.Move(tmp, path, true);
        }
        catch (Exception ex)
        {
            Logger.Log("Settings save failed", ex);
        }
    }

    /// <summary>Carry over what users configured in TvDesk 1.x.</summary>
    private static void MigrateV1(JsonObject o, AppSettings s)
    {
        Logger.Log("Migrating settings from TvDesk 1.x");
        s.SchemaVersion = 2;
        s.OnboardingComplete = Bool(o, "OnboardingComplete") ?? false;

        foreach (var item in Arr(o, "FavoriteItems"))
        {
            string? url = Str(item, "Url");
            if (string.IsNullOrWhiteSpace(url)) continue;
            string name = Str(item, "Name") ?? url;
            if (!s.MyLinks.Any(x => x.Url == url)) s.MyLinks.Add(new SavedLink { Name = name, Url = url });
            if (!s.Favorites.Contains(url)) s.Favorites.Add(url);
        }
        foreach (var item in Arr(o, "FavoriteUrls"))
        {
            string? url = item?.GetValueKind() == JsonValueKind.String ? item.GetValue<string>() : null;
            if (!string.IsNullOrWhiteSpace(url) && !s.Favorites.Contains(url)) s.Favorites.Add(url);
        }
        foreach (var item in Arr(o, "CustomPlaylistSources"))
        {
            string? url = Str(item, "Url");
            if (string.IsNullOrWhiteSpace(url)) continue;
            s.CustomSources.Add(new CustomSource { Name = Str(item, "Name") ?? "IPTV", Url = url });
        }

        s.LastUrl = Str(o, "LastChannelUrl");
        s.LastName = Str(o, "LastChannelName");

        void Src(string oldKey, string id) { var v = Bool(o, oldKey); if (v.HasValue) s.Sources[id] = v.Value; }
        Src("SourceTelewebion", "iran-irib");
        Src("SourceIptvOrgIran", "iptv-org-ir");
        Src("SourceIptvOrgCategories", "iptv-org");
        Src("SourceFreeTv", "free-tv");

        if (Bool(o, "MuteAudioOnFocusLoss") == true) s.OnAppFocused = AutoAction.Mute;
        if (Bool(o, "PauseVideoOnFocusLoss") == true) s.OnAppFocused = AutoAction.Pause;
        bool? fsMute = Bool(o, "MuteAudioOnFullscreen");
        bool? fsPause = Bool(o, "PauseVideoOnFullscreen");
        if (fsPause == true) s.OnFullscreen = AutoAction.Pause;
        else if (fsMute == true) s.OnFullscreen = AutoAction.Mute;
        else if (fsPause == false && fsMute == false) s.OnFullscreen = AutoAction.None;
    }

    private static IEnumerable<JsonNode?> Arr(JsonObject o, string key)
        => o.TryGetPropertyValue(key, out var n) && n is JsonArray a ? a : Enumerable.Empty<JsonNode?>();

    private static string? Str(JsonNode? n, string key)
    {
        try
        {
            if (n is JsonObject o && o.TryGetPropertyValue(key, out var v) && v != null && v.GetValueKind() == JsonValueKind.String)
                return v.GetValue<string>();
        }
        catch { }
        return null;
    }

    private static bool? Bool(JsonObject o, string key)
    {
        try
        {
            if (o.TryGetPropertyValue(key, out var v) && v != null)
            {
                var k = v.GetValueKind();
                if (k == JsonValueKind.True) return true;
                if (k == JsonValueKind.False) return false;
            }
        }
        catch { }
        return null;
    }
}

public static class AutoStartManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TvDesk";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true)
                            ?? Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (key == null) return;
            if (enabled)
            {
                string exe = Environment.ProcessPath ?? "";
                if (!string.IsNullOrEmpty(exe)) key.SetValue(ValueName, $"\"{exe}\" --autostart");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch (Exception ex)
        {
            Logger.Log("AutoStart apply failed", ex);
        }
    }
}
