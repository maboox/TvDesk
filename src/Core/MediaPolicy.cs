using System;
using System.IO;
using System.Linq;

namespace TvDesk.Core;

/// <summary>One trust boundary for user input and untrusted catalog/playlist entries.</summary>
public static class MediaPolicy
{
    private static readonly string[] Schemes = { "http", "https", "rtsp", "rtsps", "rtmp", "rtmps", "udp", "rtp" };

    public static bool IsRemoteMedia(string? value)
        => !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl)
           && Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && Schemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)
           && uri.Host.Length > 0;

    public static bool IsHttp(string? value)
        => IsRemoteMedia(value) && Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == "http" || uri.Scheme == "https");

    public static bool IsLocalPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) return false;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile)
            value = uri.LocalPath;
        // Never trigger SMB authentication through a playlist or pasted UNC path.
        return value.Length > 2 && char.IsAsciiLetter(value[0]) && value[1] == ':'
               && (value[2] == '\\' || value[2] == '/') && !value.StartsWith(@"\\");
    }

    public static bool IsPlayable(string? value) => IsRemoteMedia(value) || IsLocalPath(value);

    public static string? SafeHeader(string? value)
        => string.IsNullOrWhiteSpace(value) || value.Length > 2048 || value.Any(char.IsControl) ? null : value.Trim();
}
