using System.Net;
using System.Net.Http.Headers;

namespace ByteSplit.Core;

public sealed record ProbeResult(
    string FinalUrl,
    string FileName,
    long TotalSize,
    bool SupportsRange);

public static class HttpProber
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 8,
            AutomaticDecompression = DecompressionMethods.None,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(20)
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(25)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ByteSplit/2.0");
        return client;
    }

    public static Uri ValidateHttpUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
            throw new ArgumentException("URL unduhan wajib diisi.");

        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("URL harus diawali http:// atau https://.");
        }

        return uri;
    }

    public static async Task<ProbeResult> ProbeAsync(string rawUrl, CancellationToken ct)
    {
        Uri initialUri = ValidateHttpUrl(rawUrl);

        // Coba HEAD terlebih dahulu
        try
        {
            using var headReq = new HttpRequestMessage(HttpMethod.Head, initialUri);
            using var headRes = await Client.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (headRes.IsSuccessStatusCode)
            {
                Uri finalUri = headRes.RequestMessage?.RequestUri ?? initialUri;
                string fileName = ExtractFileName(headRes, finalUri);
                long totalSize = headRes.Content.Headers.ContentLength ?? 0;
                bool supportsRange = totalSize > 0 &&
                    headRes.Headers.AcceptRanges.Any(v => v.Equals("bytes", StringComparison.OrdinalIgnoreCase));

                if (supportsRange)
                {
                    return new ProbeResult(finalUri.ToString(), fileName, totalSize, true);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Lanjut ke fallback GET Range 0-0 bila server menolak HEAD
        }

        using var getReq = new HttpRequestMessage(HttpMethod.Get, initialUri);
        getReq.Headers.Range = new RangeHeaderValue(0, 0);

        using var getRes = await Client.SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!getRes.IsSuccessStatusCode && getRes.StatusCode != HttpStatusCode.PartialContent)
        {
            throw new HttpRequestException($"Server merespons HTTP {(int)getRes.StatusCode} ({getRes.ReasonPhrase}).");
        }

        Uri resolvedUri = getRes.RequestMessage?.RequestUri ?? initialUri;
        string resolvedName = ExtractFileName(getRes, resolvedUri);

        if (getRes.StatusCode == HttpStatusCode.PartialContent)
        {
            long totalSize = getRes.Content.Headers.ContentRange?.Length
                ?? getRes.Content.Headers.ContentLength
                ?? 0;
            return new ProbeResult(resolvedUri.ToString(), resolvedName, totalSize, totalSize > 0);
        }

        long fullLength = getRes.Content.Headers.ContentLength ?? 0;
        return new ProbeResult(resolvedUri.ToString(), resolvedName, fullLength, false);
    }

    public static string SanitizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "download.bin";

        string trimmed = name.Trim().Trim('"');
        string baseName = Path.GetFileName(trimmed);
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            baseName = baseName.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(baseName) || baseName == "." || baseName == ".."
            ? "download.bin"
            : baseName;
    }

    private static string ExtractFileName(HttpResponseMessage res, Uri finalUri)
    {
        ContentDispositionHeaderValue? cd = res.Content.Headers.ContentDisposition;
        string? rawName = cd?.FileNameStar ?? cd?.FileName;
        if (!string.IsNullOrWhiteSpace(rawName))
        {
            return SanitizeFileName(rawName);
        }

        string urlFile = Uri.UnescapeDataString(Path.GetFileName(finalUri.AbsolutePath));
        return SanitizeFileName(urlFile);
    }
}