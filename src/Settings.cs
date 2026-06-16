using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace TvDesk.Settings;

public sealed class FavoriteItem
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
}

public sealed class AppSettings
{
    public int Volume { get; set; } = 80;
    public bool Muted { get; set; } = false;
    public string Quality { get; set; } = "Auto";
    public string? LastChannelUrl { get; set; }
    public string? LastChannelName { get; set; }
    public bool AutoStart { get; set; } = false;

    // First run / localization
    public bool OnboardingComplete { get; set; } = false;
    public string Language { get; set; } = "fa"; // fa / en

    // Independent behavior switches
    public bool MuteAudioOnFocusLoss { get; set; } = true;
    public bool PauseVideoOnFocusLoss { get; set; } = false;
    public bool MuteAudioOnFullscreen { get; set; } = true;
    public bool PauseVideoOnFullscreen { get; set; } = true;

    // Backward compatibility with older settings.json
    public bool MuteOnFocusLoss { get; set; } = true;
    public bool StopOnFullscreen { get; set; } = true;
    public double WallpaperDim { get; set; } = 0.0;

    public List<string> FavoriteUrls { get; set; } = new();
    public List<FavoriteItem> FavoriteItems { get; set; } = new();
    public int PlaylistRefreshDays { get; set; } = 2;

    // Source selection
    public bool SourceTelewebion { get; set; } = true;
    public bool SourceIptvOrgIran { get; set; } = true;
    public bool SourceIptvOrgCategories { get; set; } = true;
    public bool SourceIptvOrgLanguages { get; set; } = false;
    public bool SourceFreeTv { get; set; } = false;
    public List<FavoriteItem> CustomPlaylistSources { get; set; } = new();
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };

    private static string FilePath
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TvDesk");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
                s.FavoriteUrls ??= new List<string>();
                s.FavoriteItems ??= new List<FavoriteItem>();
                s.CustomPlaylistSources ??= new List<FavoriteItem>();

                // migrate old flags on first newer run
                if (!s.OnboardingComplete)
                {
                    s.MuteAudioOnFocusLoss = s.MuteOnFocusLoss;
                    s.PauseVideoOnFullscreen = s.StopOnFullscreen;
                }
                return s;
            }
        }
        catch { }
        return new AppSettings();
    }

    public static void Save(AppSettings s)
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(s, Opt)); }
        catch { }
    }
}

public static class AutoStartManager
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "TvDesk";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null) return;
            if (enabled)
            {
                string exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(exe)) key.SetValue(ValueName, $"\"{exe}\"");
            }
            else key.DeleteValue(ValueName, false);
        }
        catch { }
    }
}
