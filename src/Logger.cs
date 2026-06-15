using System;
using System.IO;

namespace TvDesk;

/// <summary>لاگ‌گیری ساده در فایل TvDesk.log کنار خود exe (با fallback به AppData).</summary>
public static class Logger
{
    private static readonly object Lock = new();
    private static string _path = "";

    public static string FilePath => _path;

    public static void Init()
    {
        // اول کنار خود exe امتحان می‌کنیم (چیزی که کاربر می‌بیند)
        string exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
        string exeDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
        string candidate = Path.Combine(exeDir, "TvDesk.log");
        if (TryStart(candidate)) return;

        // اگر پوشه‌ی exe فقط‌خواندنی بود (مثلاً Program Files) → AppData
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TvDesk");
            Directory.CreateDirectory(dir);
            TryStart(Path.Combine(dir, "TvDesk.log"));
        }
        catch { }
    }

    private static bool TryStart(string path)
    {
        try
        {
            File.WriteAllText(path,
                $"TvDesk log — {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
                $"exe: {Environment.ProcessPath}{Environment.NewLine}" +
                $"baseDir: {AppContext.BaseDirectory}{Environment.NewLine}" +
                $"OS: {Environment.OSVersion}{Environment.NewLine}" +
                $"---{Environment.NewLine}");
            _path = path;
            return true;
        }
        catch { return false; }
    }

    public static void Log(string msg)
    {
        lock (Lock)
        {
            try
            {
                if (string.IsNullOrEmpty(_path)) return;
                File.AppendAllText(_path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}");
            }
            catch { }
        }
    }

    public static void Log(string context, Exception ex)
        => Log($"ERROR {context}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
}
