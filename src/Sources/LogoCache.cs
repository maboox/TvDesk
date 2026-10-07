using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TvDesk.Core;

namespace TvDesk.Sources;

/// <summary>Downloads channel logos on demand (memory + disk cache, limited parallelism, small decode size).</summary>
public static class LogoCache
{
    private static readonly ConcurrentDictionary<string, ImageSource> Memory = new();
    private static readonly ConcurrentDictionary<string, byte> Failed = new();
    private static readonly ConcurrentDictionary<string, Task<ImageSource?>> InFlight = new();
    private static readonly SemaphoreSlim Slots = new(6);

    public static Task<ImageSource?> LoadAsync(string url)
    {
        if (Memory.TryGetValue(url, out var img)) return Task.FromResult<ImageSource?>(img);
        if (Failed.ContainsKey(url)) return Task.FromResult<ImageSource?>(null);
        return InFlight.GetOrAdd(url, u => LoadCoreAsync(u));
    }

    private static async Task<ImageSource?> LoadCoreAsync(string url)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                Failed[url] = 0;
                return null;
            }

            await Slots.WaitAsync().ConfigureAwait(false);
            try
            {
                string file = Path.Combine(AppPaths.Logos, Hash(url) + ".img");
                byte[]? data = null;
                if (File.Exists(file) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(file)).TotalDays < 30)
                {
                    data = await File.ReadAllBytesAsync(file).ConfigureAwait(false);
                }
                else
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    data = await Http.Client.GetByteArrayAsync(uri, cts.Token).ConfigureAwait(false);
                    if (data.Length == 0 || data.Length > 4_000_000) data = null;
                    else
                    {
                        try { await File.WriteAllBytesAsync(file, data).ConfigureAwait(false); } catch { }
                    }
                }

                var image = data == null ? null : Decode(data);
                if (image == null) { Failed[url] = 0; return null; }
                if (Memory.Count > 2500) Memory.Clear();
                Memory[url] = image;
                return image;
            }
            finally
            {
                Slots.Release();
            }
        }
        catch
        {
            Failed[url] = 0;
            return null;
        }
        finally
        {
            InFlight.TryRemove(url, out _);
        }
    }

    private static ImageSource? Decode(byte[] data)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.DecodePixelWidth = 96;
            bmp.StreamSource = new MemoryStream(data);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static string Hash(string s)
        => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}
