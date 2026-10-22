using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TvDesk.Core;

/// <summary>Windows current-user protection. Never silently fall back to plaintext.</summary>
public static class PrivateFile
{
    private const string Prefix = "TvDesk.DPAPI.v1:";

    public static string Encode(string text)
        => Prefix + System.Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(text), null, DataProtectionScope.CurrentUser));

    public static string Decode(string text)
        => text.StartsWith(Prefix, System.StringComparison.Ordinal)
            ? Encoding.UTF8.GetString(ProtectedData.Unprotect(System.Convert.FromBase64String(text[Prefix.Length..]), null, DataProtectionScope.CurrentUser))
            : text; // migrate older plaintext installations on the next save

    public static string Read(string path) => Decode(File.ReadAllText(path));
}
