using System;
using System.IO;

namespace TvDesk.Core;

public static class AppPaths
{
    /// <summary>%AppData%\TvDesk — settings (small, roams).</summary>
    public static string Roaming => Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TvDesk"));

    /// <summary>%LocalAppData%\TvDesk — caches, tools, logs.</summary>
    public static string Local => Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TvDesk"));

    public static string Cache => Ensure(Path.Combine(Local, "cache"));
    public static string Logos => Ensure(Path.Combine(Local, "logos"));
    public static string Tools => Ensure(Path.Combine(Local, "tools"));
    public static string Logs => Ensure(Path.Combine(Local, "logs"));

    public static string SettingsFile => Path.Combine(Roaming, "settings.json");
    public static string CatalogOverrideFile => Path.Combine(Roaming, "catalog.json");
    public static string HealthFile => Path.Combine(Local, "health.json");

    private static string Ensure(string path)
    {
        try { Directory.CreateDirectory(path); } catch { }
        return path;
    }
}
