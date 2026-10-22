using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TvDesk.Core;

public enum ProxyMode { System, None, Custom }

/// <summary>Shared HTTP clients. Honours the proxy setting (system proxy by default, which matters for VPN users).</summary>
public static class Http
{
    public const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";

    private static ProxyMode _mode = ProxyMode.System;
    private static string _custom = "";

    /// <summary>For playlists, logos and downloads.</summary>
    public static HttpClient Client { get; private set; } = Create(TimeSpan.FromSeconds(90));

    /// <summary>Short-timeout client for stream health checks.</summary>
    public static HttpClient Probe { get; private set; } = Create(TimeSpan.FromSeconds(15));

    public static void Configure(ProxyMode mode, string? custom)
    {
        string c = (custom ?? "").Trim();
        if (mode == _mode && c == _custom) return;
        _mode = mode;
        _custom = c;
        var oldClient = Client;
        var oldProbe = Probe;
        Client = Create(TimeSpan.FromSeconds(90));
        Probe = Create(TimeSpan.FromSeconds(15));
        oldClient.Dispose();
        oldProbe.Dispose();
        Logger.Log($"HTTP proxy mode = {mode}");
    }

    private static HttpClient Create(TimeSpan timeout)
    {
        if (_mode == ProxyMode.Custom && !IsValidProxy(_custom))
            return new HttpClient(new BlockedProxyHandler()) { Timeout = timeout };
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(12),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
        };
        switch (_mode)
        {
            case ProxyMode.None:
                handler.UseProxy = false;
                break;
            case ProxyMode.Custom:
                if (Uri.TryCreate(NormalizeProxy(_custom), UriKind.Absolute, out var p))
                {
                    handler.UseProxy = true;
                    handler.Proxy = new WebProxy(p);
                }
                break;
            default:
                handler.UseProxy = true; // system (WinINet) proxy
                break;
        }

        var client = new HttpClient(handler) { Timeout = timeout };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
        return client;
    }

    /// <summary>The proxy that should be used for <paramref name="target"/>, or null for a direct connection.
    /// Used to hand the same proxy to VLC and yt-dlp, which do not read Windows proxy settings themselves.</summary>
    public static string? ProxyFor(string target)
    {
        if (_mode == ProxyMode.Custom)
        {
            if (!IsValidProxy(_custom)) throw new InvalidOperationException("Invalid custom proxy; connection blocked.");
            return NormalizeProxy(_custom);
        }
        try
        {
            if (_mode == ProxyMode.None) return null;
            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
            var sys = HttpClient.DefaultProxy;
            if (sys.IsBypassed(uri)) return null;
            Uri? proxy = sys.GetProxy(uri);
            if (proxy == null || proxy == uri || proxy.Host.Equals(uri.Host, StringComparison.OrdinalIgnoreCase)) return null;
            return $"{proxy.Scheme}://{proxy.Host}:{proxy.Port}";
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeProxy(string p)
    {
        p = p.Trim();
        if (p.Length == 0) return p;
        return p.Contains("://") ? p : "http://" + p;
    }

    public static bool IsValidProxy(string value)
        => Uri.TryCreate(NormalizeProxy(value), UriKind.Absolute, out var uri)
           && (uri.Scheme == "http" || uri.Scheme == "https") && uri.Host.Length > 0
           && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0;

    private sealed class BlockedProxyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromException<HttpResponseMessage>(new HttpRequestException("Invalid custom proxy; connection blocked."));
    }
}
