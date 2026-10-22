using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TvDesk.Core;

namespace TvDesk.Sources;

/// <summary>Only executes the reviewed release pinned in tools.json, with configuration and plugins disabled.</summary>
public static class YtDlp
{
    private sealed record Release(string Version, string Url, string Sha256);
    private static readonly Release Pin = LoadRelease();
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly SemaphoreSlim Slots = new(3, 3);
    public static string ExePath => Path.Combine(AppPaths.Tools, "yt-dlp.exe");

    private static Release LoadRelease()
    {
        using var resource = typeof(YtDlp).Assembly.GetManifestResourceStream("TvDesk.tools.json")!;
        return JsonSerializer.Deserialize<Release>(resource, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    public static bool Verify(string path)
    {
        if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).Equals(Pin.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<string?> EnsureAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        string temp = ExePath + ".download";
        try
        {
            if (Verify(ExePath)) return ExePath;
            string bundled = Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe");
            if (Verify(bundled)) File.Copy(bundled, temp, true);
            else
            {
                Logger.Log("Installing the pinned yt-dlp release " + Pin.Version);
                byte[] bytes = await Download.BytesAsync(Http.Client, Pin.Url, 64_000_000, ct);
                await File.WriteAllBytesAsync(temp, bytes, ct);
            }
            if (!Verify(temp)) throw new InvalidDataException("yt-dlp integrity check failed.");
            File.Move(temp, ExePath, true);
            return ExePath;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Logger.Log("yt-dlp installation failed", ex); return null; }
        finally
        {
            try { File.Delete(temp); } catch { }
            Gate.Release();
        }
    }

    public static async Task<ResolvedStream> ResolveAsync(string url, string quality, CancellationToken ct)
    {
        if (!MediaPolicy.IsHttp(url)) throw new ArgumentException("Unsupported web address.");
        string? exe = await EnsureAsync(ct);
        if (exe == null) throw new InvalidOperationException(Loc.T("err_ytdlp_missing"));
        await Slots.WaitAsync(ct);
        try
        {
            string height = quality switch { "480" => "480", "720" => "720", _ => "1080" };
            var args = new List<string>
            {
                "--ignore-config", "--no-plugin-dirs", "--no-playlist", "--no-warnings", "--no-progress",
                "--socket-timeout", "15", "-f", $"bv*[height<={height}][vcodec^=avc1]+ba/bv*[height<={height}]+ba/b[height<={height}]", "-g"
            };
            string? proxy = Http.ProxyFor(url);
            // An explicit empty proxy also prevents yt-dlp inheriting an unrelated environment proxy.
            args.Add("--proxy"); args.Add(proxy ?? "");
            args.Add("--"); args.Add(url);
            var result = await RunAsync(exe, args, TimeSpan.FromSeconds(45), ct);
            var urls = result.stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(MediaPolicy.IsHttp).ToList();
            Logger.Log($"yt-dlp exit={result.code}, streams={urls.Count}");
            if (result.code != 0 || urls.Count == 0) throw new InvalidOperationException(Loc.T("st_resolve_failed"));
            return new ResolvedStream { Url = urls[0], AudioUrl = urls.Count > 1 ? urls[1] : null };
        }
        finally { Slots.Release(); }
    }

    public static Task<string?> VersionAsync()
    {
        // Opening Settings must not download or execute a helper.
        try
        {
            string bundled = Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe");
            return Task.FromResult<string?>(Verify(ExePath) || Verify(bundled) ? Pin.Version : null);
        }
        catch { return Task.FromResult<string?>(null); }
    }

    public static async Task<bool> UpdateAsync() => await EnsureAsync(CancellationToken.None) != null;

    private static async Task<(int code, string stdout)> RunAsync(string exe, IEnumerable<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var info = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppPaths.Tools
        };
        foreach (string arg in args) info.ArgumentList.Add(arg);
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        info.Environment["YTDLP_NO_PLUGINS"] = "1";
        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new InvalidOperationException("Could not start yt-dlp.");
        // Drain both pipes concurrently, with a bound even for a compromised remote provider.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        Task<string> stdout = ReadOutputAsync(process.StandardOutput, deadline.Token);
        Task<string> stderr = ReadOutputAsync(process.StandardError, deadline.Token);
        try
        {
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(deadline.Token));
            return (process.ExitCode, await stdout);
        }
        catch
        {
            try { process.Kill(true); } catch { }
            ct.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested) throw new TimeoutException("yt-dlp timed out.");
            throw;
        }
    }

    private static async Task<string> ReadOutputAsync(StreamReader reader, CancellationToken ct)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            // Keep draining after the retained output reaches its cap, avoiding pipe deadlock.
            int keep = Math.Min(count, 1_000_000 - output.Length);
            if (keep > 0) output.Append(buffer, 0, keep);
        }
        return output.ToString();
    }
}
