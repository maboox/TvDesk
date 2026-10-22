using System;
using System.Text.RegularExpressions;

namespace TvDesk.Core;

public static class Privacy
{
    private static readonly Regex Url = new(@"\b[a-z][a-z0-9+.-]*://[^\s<>""']+", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Path = new(@"(?:[A-Z]:[\\/]|\\\\)[^\r\n""<>]*", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        try { return Path.Replace(Url.Replace(text, "[link]"), "[local path]"); }
        catch (RegexMatchTimeoutException) { return "[private diagnostic omitted]"; }
    }
}
