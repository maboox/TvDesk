using System;
using System.IO;

namespace TvDesk.Core;

/// <summary>Simple thread-safe file log in %LocalAppData%\TvDesk\logs\TvDesk.log (rotated at ~2 MB).</summary>
public static class Logger
{
    private static readonly object Gate = new();
    private static string _path = "";

    public static string FilePath => _path;

    public static void Init()
    {
        try
        {
            string dir = AppPaths.Logs;
            _path = Path.Combine(dir, "TvDesk.log");
            if (File.Exists(_path) && new FileInfo(_path).Length > 2_000_000)
            {
                File.Copy(_path, _path + ".old", true);
                File.Delete(_path);
            }
            File.AppendAllText(_path,
                Environment.NewLine + "==========================================" + Environment.NewLine +
                $"TvDesk {typeof(Logger).Assembly.GetName().Version} — {DateTime.Now:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine +
                $"exe: {Environment.ProcessPath}" + Environment.NewLine +
                $"OS: {Environment.OSVersion} ({(Environment.Is64BitProcess ? "x64" : "x86")})" + Environment.NewLine);
        }
        catch
        {
            _path = "";
        }
    }

    public static void Log(string message)
    {
        if (string.IsNullOrEmpty(_path)) return;
        lock (Gate)
        {
            try { File.AppendAllText(_path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}"); }
            catch { }
        }
    }

    public static void Log(string context, Exception ex)
        => Log($"ERROR {context}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
}
