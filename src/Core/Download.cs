using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TvDesk.Core;

public static class Download
{
    /// <summary>Limits decompressed bytes while reading, including chunked responses.</summary>
    public static async Task<byte[]> ReadLimitedAsync(Stream stream, int limit, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            int count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (count == 0) return output.ToArray();
            if (output.Length + count > limit) throw new InvalidDataException("Download exceeds the allowed size.");
            await output.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
        }
    }

    public static async Task<byte[]> BytesAsync(HttpClient client, string url, int limit, CancellationToken ct)
    {
        if (!MediaPolicy.IsHttp(url)) throw new InvalidDataException("Only HTTP(S) downloads are supported.");
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Download exceeds the allowed size.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await ReadLimitedAsync(stream, limit, ct).ConfigureAwait(false);
    }
}
