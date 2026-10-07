using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TvDesk.Core;
using TvDesk.Desktop;
using TvDesk.Sources;

namespace TvDesk.UI;

/// <summary>Settings page bindings.</summary>
public sealed partial class MainViewModel
{
    public RelayCommand OpenLogsCommand { get; private set; } = null!;
    public RelayCommand UpdateYtDlpCommand { get; private set; } = null!;
    public RelayCommand ClearCacheCommand { get; private set; } = null!;
    public RelayCommand ReattachCommand { get; private set; } = null!;
    public RelayCommand ResetHealthCommand { get; private set; } = null!;
    public RelayCommand OpenSettingsFolderCommand { get; private set; } = null!;

    private void InitSettingsCommands()
    {
        OpenLogsCommand = new RelayCommand(() => OpenFolder(AppPaths.Logs));
        OpenSettingsFolderCommand = new RelayCommand(() => OpenFolder(AppPaths.Roaming));
        UpdateYtDlpCommand = new RelayCommand(() => _ = UpdateYtDlpAsync());
        ClearCacheCommand = new RelayCommand(() =>
        {
            ChannelLibrary.ClearCache();
            Toast(Loc.T("toast_cache_cleared"));
            _ = C.ReloadLibraryAsync(true);
        });
        ReattachCommand = new RelayCommand(() =>
        {
            C.RebuildHost("user request", force: true);
            Toast(Loc.T("toast_reattached"));
        });
        ResetHealthCommand = new RelayCommand(() =>
        {
            C.Health.Clear();
            C.Health.Save();
            foreach (var c in C.Library.Channels) c.IsAlive = null;
            RebuildFacets();
            Refilter();
            Toast(Loc.T("toast_health_reset"));
        });
    }

    private static void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Logger.Log("Open folder", ex); }
    }

    private void RaiseSettingsOptions()
    {
        foreach (var name in new[]
                 {
                     nameof(MonitorOptions), nameof(FitOptions), nameof(QualityOptions), nameof(VideoOutputOptions),
                     nameof(ActionOptions), nameof(RefreshOptions), nameof(ProxyOptions), nameof(CategoryOptions),
                     nameof(Monitor), nameof(Fit), nameof(Quality), nameof(VideoOutput), nameof(OnAppFocused), nameof(OnMaximized),
                     nameof(OnFullscreen), nameof(OnBattery), nameof(OnLocked), nameof(RefreshHours), nameof(Proxy),
                     nameof(IsFarsi), nameof(IsEnglish),
                 })
            Raise(name);
    }

    // ---------------------------------------------------------------- language

    public bool IsFarsi
    {
        get => C.Settings.Language == "fa";
        set { if (value && C.Settings.Language != "fa") C.SetLanguage("fa"); }
    }

    public bool IsEnglish
    {
        get => C.Settings.Language == "en";
        set { if (value && C.Settings.Language != "en") C.SetLanguage("en"); }
    }

    // ---------------------------------------------------------------- wallpaper

    public List<Option> MonitorOptions
    {
        get
        {
            var list = new List<Option> { new("primary", Loc.T("opt_monitor_primary")) };
            var monitors = WallpaperHost.Monitors();
            if (monitors.Count > 1)
            {
                list.Add(new Option("all", Loc.T("opt_monitor_all")));
                foreach (var m in monitors)
                    list.Add(new Option(m.Key, Loc.F("opt_monitor_n", m.Label) + (m.Primary ? " ★" : "")));
            }
            return list;
        }
    }

    public string Monitor
    {
        get => C.Settings.Monitor;
        set
        {
            if (string.IsNullOrEmpty(value) || value == C.Settings.Monitor) return;
            C.Settings.Monitor = value;
            C.SaveSoon();
            C.ApplyDisplaySettings();
            Raise();
        }
    }

    public List<Option> FitOptions => new()
    {
        new("Fill", Loc.T("opt_fit_fill")),
        new("Fit", Loc.T("opt_fit_fit")),
        new("Stretch", Loc.T("opt_fit_stretch")),
    };

    public string Fit
    {
        get => C.Settings.Fit.ToString();
        set
        {
            if (!Enum.TryParse<FitMode>(value, out var f) || f == C.Settings.Fit) return;
            C.Settings.Fit = f;
            C.SaveSoon();
            C.ApplyDisplaySettings();
            Raise();
        }
    }

    public List<Option> QualityOptions => new()
    {
        new("auto", Loc.T("opt_quality_auto")),
        new("1080", "1080p"),
        new("720", "720p"),
        new("480", Loc.T("opt_quality_480")),
    };

    public string Quality
    {
        get => C.Settings.Quality;
        set
        {
            if (string.IsNullOrEmpty(value) || value == C.Settings.Quality) return;
            C.Settings.Quality = value;
            C.SaveSoon();
            Raise();
            if (C.Current != null && IsActive) C.Reconnect();
        }
    }

    public List<Option> VideoOutputOptions => new()
    {
        new("gdi", Loc.T("opt_vout_gdi")),
        new("d3d11", Loc.T("opt_vout_d3d11")),
        new("auto", Loc.T("opt_vout_auto")),
    };

    public string VideoOutput
    {
        get => C.Settings.VideoOutput;
        set
        {
            if (string.IsNullOrEmpty(value) || value == C.Settings.VideoOutput) return;
            C.Settings.VideoOutput = value;
            C.SaveSoon();
            Raise();
            _ = C.RecreateEngineAsync();
        }
    }

    public bool HardwareDecoding
    {
        get => C.Settings.HardwareDecoding;
        set
        {
            if (value == C.Settings.HardwareDecoding) return;
            C.Settings.HardwareDecoding = value;
            C.SaveSoon();
            Raise();
            _ = C.RecreateEngineAsync();
        }
    }

    // ---------------------------------------------------------------- smart pause

    public List<Option> ActionOptions => new()
    {
        new("None", Loc.T("act_none")),
        new("Mute", Loc.T("act_mute")),
        new("Pause", Loc.T("act_pause")),
    };

    private string GetAction(AutoAction a) => a.ToString();

    private void SetAction(string? value, Action<AutoAction> apply, AutoAction current, string propertyName)
    {
        if (!Enum.TryParse<AutoAction>(value, out var a) || a == current) return;
        apply(a);
        C.SaveSoon();
        C.SettingsChangedForWatcher();
        Raise(propertyName);
    }

    public string OnAppFocused
    {
        get => GetAction(C.Settings.OnAppFocused);
        set => SetAction(value, a => C.Settings.OnAppFocused = a, C.Settings.OnAppFocused, nameof(OnAppFocused));
    }

    public string OnMaximized
    {
        get => GetAction(C.Settings.OnMaximized);
        set => SetAction(value, a => C.Settings.OnMaximized = a, C.Settings.OnMaximized, nameof(OnMaximized));
    }

    public string OnFullscreen
    {
        get => GetAction(C.Settings.OnFullscreen);
        set => SetAction(value, a => C.Settings.OnFullscreen = a, C.Settings.OnFullscreen, nameof(OnFullscreen));
    }

    public string OnBattery
    {
        get => GetAction(C.Settings.OnBattery);
        set => SetAction(value, a => C.Settings.OnBattery = a, C.Settings.OnBattery, nameof(OnBattery));
    }

    public string OnLocked
    {
        get => GetAction(C.Settings.OnLocked);
        set => SetAction(value, a => C.Settings.OnLocked = a, C.Settings.OnLocked, nameof(OnLocked));
    }

    // ---------------------------------------------------------------- general

    public bool AutoStart
    {
        get => C.Settings.AutoStart;
        set
        {
            if (value == C.Settings.AutoStart) return;
            C.Settings.AutoStart = value;
            AutoStartManager.Apply(value);
            C.SaveSoon();
            Raise();
        }
    }

    public bool ResumeOnStart
    {
        get => C.Settings.ResumeOnStart;
        set { if (value == C.Settings.ResumeOnStart) return; C.Settings.ResumeOnStart = value; C.SaveSoon(); Raise(); }
    }

    public bool HideAdult
    {
        get => C.Settings.HideAdult;
        set { if (value == C.Settings.HideAdult) return; C.Settings.HideAdult = value; C.SaveSoon(); Raise(); RebuildFacets(); Refilter(); }
    }

    public bool HideGeoBlocked
    {
        get => C.Settings.HideGeoBlocked;
        set { if (value == C.Settings.HideGeoBlocked) return; C.Settings.HideGeoBlocked = value; C.SaveSoon(); Raise(); RebuildFacets(); Refilter(); }
    }

    public bool HideBroken
    {
        get => C.Settings.HideBroken;
        set { if (value == C.Settings.HideBroken) return; C.Settings.HideBroken = value; C.SaveSoon(); Raise(); RebuildFacets(); Refilter(); }
    }

    public List<Option> RefreshOptions => new()
    {
        new("6", Loc.F("opt_hours", 6)),
        new("24", Loc.T("opt_day1")),
        new("48", Loc.F("opt_days", 2)),
        new("168", Loc.T("opt_week1")),
    };

    public string RefreshHours
    {
        get => C.Settings.RefreshHours.ToString();
        set
        {
            if (!int.TryParse(value, out int h) || h == C.Settings.RefreshHours) return;
            C.Settings.RefreshHours = h;
            C.SaveSoon();
            Raise();
        }
    }

    // ---------------------------------------------------------------- network

    public List<Option> ProxyOptions => new()
    {
        new("System", Loc.T("opt_proxy_system")),
        new("None", Loc.T("opt_proxy_none")),
        new("Custom", Loc.T("opt_proxy_custom")),
    };

    public string Proxy
    {
        get => C.Settings.Proxy.ToString();
        set
        {
            if (!Enum.TryParse<ProxyMode>(value, out var p) || p == C.Settings.Proxy) return;
            C.Settings.Proxy = p;
            C.ApplyProxy();
            Raise();
            Raise(nameof(IsCustomProxy));
        }
    }

    public bool IsCustomProxy => C.Settings.Proxy == ProxyMode.Custom;

    public string CustomProxy
    {
        get => C.Settings.CustomProxy;
        set
        {
            value = (value ?? "").Trim();
            if (value == C.Settings.CustomProxy) return;
            C.Settings.CustomProxy = value;
            C.ApplyProxy();
            Raise();
        }
    }

    // ---------------------------------------------------------------- add dialog helpers

    public List<Option> CategoryOptions =>
        new[] { new Option("", Loc.T("cat_none")) }
            .Concat(Categories.KnownCategories.Where(c => c.Key != Categories.Other).OrderBy(c => c.Order)
                .Select(c => new Option(c.Key, Categories.Label(c.Key))))
            .ToList();

    // ---------------------------------------------------------------- tools

    private string _ytDlpVersion = "…";
    public string YtDlpVersion { get => _ytDlpVersion; private set => Set(ref _ytDlpVersion, value); }

    private bool _updatingYtDlp;
    public bool UpdatingYtDlp
    {
        get => _updatingYtDlp;
        private set { if (Set(ref _updatingYtDlp, value)) Raise(nameof(CanUpdateYtDlp)); }
    }

    public bool CanUpdateYtDlp => !_updatingYtDlp;

    private async Task LoadYtDlpVersionAsync()
    {
        string? v = await YtDlp.VersionAsync();
        YtDlpVersion = v ?? Loc.T("ytdlp_missing");
    }

    private async Task UpdateYtDlpAsync()
    {
        if (_updatingYtDlp) return;
        UpdatingYtDlp = true;
        YtDlpVersion = Loc.T("ytdlp_updating");
        bool ok = await YtDlp.UpdateAsync();
        if (ok)
        {
            C.Settings.YtDlpLastUpdate = DateTime.UtcNow;
            C.SaveSoon();
        }
        UpdatingYtDlp = false;
        await LoadYtDlpVersionAsync();
        Toast(ok ? Loc.T("toast_ytdlp_ok") : Loc.T("toast_ytdlp_fail"));
    }

    public string AppVersion => "TvDesk " + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "2.0");
    public string LogPath => Logger.FilePath;
}
