using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace TvDesk.Settings;

public sealed class AppSettings
{
    public int Volume { get; set; } = 80;
    public bool Muted { get; set; } = false;
    public string Quality { get; set; } = "Auto"; // Auto / High / Medium / Low
    public string? LastChannelUrl { get; set; }
    public string? LastChannelName { get; set; }
    public bool MuteOnFocusLoss { get; set; } = true;
    public bool StopOnFullscreen { get; set; } = true;
    public bool AutoStart { get; set; } = false;
    public double WallpaperDim { get; set; } = 0.0; // 0..0.85
    public List<string> FavoriteUrls { get; set; } = new();
    public int PlaylistRefreshDays { get; set; } = 2;
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
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
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

/// <summary>اجرای خودکار با ویندوز از طریق کلید Run در رجیستری.</summary>
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
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch { }
    }
}
