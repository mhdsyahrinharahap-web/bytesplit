using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using ByteSplit.Models;
using Microsoft.Win32.SafeHandles;

namespace ByteSplit.Core;

public static class DownloadEngine
{
    private const int MaxConnections = 16;
    private const long MinSplitSize = 1024 * 1024; // 1 MB
    private const int BufferSize = 64 * 1024; // 64 KB per bacaan agar hemat CPU/disk
    private const int MaxNetworkRetries = 4;
    private const int MaxRateLimitRetries = 8;

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 8,
            AutomaticDecompression = DecompressionMethods.None,
            MaxConnectionsPerServer = 32,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(20)
        };

        var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ByteSplit/2.0");
        return client;
    }

    private static List<ChunkInfo> BuildChunks(long totalSize, int requestedConnections, bool supportsRange)
    {
        if (!supportsRange || totalSize < MinSplitSize || requestedConnections <= 1)
        {
            return new List<ChunkInfo>
            {
                new()
                {
                    Id = 0,
                    Start = 0,
                    End = totalSize > 0 ? totalSize - 1 : -1,
                    Downloaded = 0,
                    Status = "pending"
                }
            };
        }

        int clamped = Math.Clamp(requestedConnections, 1, MaxConnections);
        int count = (int)Math.Min(clamped, Math.Max(1L, totalSize / (256 * 1024)));
        long chunkSize = totalSize / count;

        var chunks = new List<ChunkInfo>(count);
        for (int i = 0; i < count; i++)
        {
            long start = i * chunkSize;
            long end = (i == count - 1) ? totalSize - 1 : start + chunkSize - 1;
            chunks.Add(new ChunkInfo
            {
                Id = i,
                Start = start,
                End = end,
                Downloaded = 0,
                Status = "pending"
            });
        }
        return chunks;
    }

    private static string ResolveUniqueFilePath(string outputDir, string fileName)
    {
        string ext = Path.GetExtension(fileName);
        string baseName = Path.GetFileNameWithoutExtension(fileName);
        string candidate = Path.Combine(outputDir, fileName);
        int counter = 1;

        while (File.Exists(candidate) || File.Exists($"{candidate}.bytesplit.part"))
        {
            candidate = Path.Combine(outputDir, $"{baseName} ({counter}){ext}");
            counter++;
        }
        return candidate;
    }

    private static int ParseRetryAfterMs(HttpResponseMessage res, int attempt)
    {
        RetryConditionHeaderValue? retryAfter = res.Headers.RetryAfter;
        if (retryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
        {
            return (int)Math.Min(delta.TotalMilliseconds, 30_000);
        }
        if (retryAfter?.Date is DateTimeOffset date)
        {
            double diffMs = (date - DateTimeOffset.UtcNow).TotalMilliseconds;
            if (diffMs > 0)
                return (int)Math.Min(diffMs, 30_000);
        }

        int jitter = Random.Shared.Next(100, 500);
        return Math.Min(2000 * attempt + jitter, 15_000);
    }

    private static async Task DownloadChunkAsync(
        string url,
        ChunkInfo chunk,
        bool useRange,
        int totalChunks,
        SafeFileHandle fileHandle,
        SpeedLimiter limiter,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            if (chunk.Status != "done") chunk.Status = "paused";
            return;
        }

        if (useRange && chunk.Start + chunk.Downloaded > chunk.End)
        {
            chunk.Status = "done";
            return;
        }

        if (useRange && chunk.Id > 0)
        {
            await Task.Delay(chunk.Id * 150, ct).ConfigureAwait(false);
        }

        int networkAttempts = 0;
        int rateLimitAttempts = 0;
        byte[] buffer = new byte[BufferSize];

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            long currentPos = chunk.Start + chunk.Downloaded;
            if (useRange && currentPos > chunk.End)
            {
                chunk.Status = "done";
                return;
            }

            chunk.Status = "downloading";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (useRange)
            {
                req.Headers.Range = new RangeHeaderValue(currentPos, chunk.End);
            }

            int readTimeoutMs = limiter.BytesPerSecond > 0 ? 60_000 : 25_000;
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attemptCts.CancelAfter(readTimeoutMs);

            try
            {
                using var res = await Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, attemptCts.Token).ConfigureAwait(false);
                int status = (int)res.StatusCode;
                bool allowFullOk = !useRange || (totalChunks == 1 && currentPos == 0);
                bool validStatus = useRange
                    ? (status == 206 || (allowFullOk && status == 200))
                    : (status == 200);

                if (!validStatus)
                {
                    int retryDelay = ParseRetryAfterMs(res, rateLimitAttempts + 1);
                    bool isRateLimit = status == 429 || status == 503;
                    bool isFatal = status >= 400 && status < 500 && status != 408 && !isRateLimit;

                    throw new ChunkHttpException(
                        $"Chunk #{chunk.Id}: server merespons HTTP {status}.",
                        isFatal,
                        isRateLimit,
                        retryDelay);
                }

                await using Stream stream = await res.Content.ReadAsStreamAsync(attemptCts.Token).ConfigureAwait(false);
                long writePos = currentPos;

                while (true)
                {
                    attemptCts.CancelAfter(readTimeoutMs);
                    int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), attemptCts.Token).ConfigureAwait(false);
                    if (bytesRead == 0)
                        break;

                    if (useRange && writePos + bytesRead > chunk.End + 1)
                    {
                        throw new ChunkHttpException($"Chunk #{chunk.Id}: data melebihi batas segmen.", true, false, 0);
                    }

                    await RandomAccess.WriteAsync(fileHandle, buffer.AsMemory(0, bytesRead), writePos, ct).ConfigureAwait(false);
                    writePos += bytesRead;
                    chunk.Downloaded += bytesRead;

                    await limiter.ConsumeAsync(bytesRead, ct).ConfigureAwait(false);
                }

                if (useRange)
                {
                    long expectedLen = chunk.End - chunk.Start + 1;
                    if (chunk.Downloaded != expectedLen)
                    {
                        throw new IOException($"Chunk #{chunk.Id} terputus ({chunk.Downloaded}/{expectedLen} bytes).");
                    }
                }

                chunk.Status = "done";
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (chunk.Status != "done") chunk.Status = "paused";
                throw;
            }
            catch (ChunkHttpException ex) when (ex.IsFatal || !useRange)
            {
                chunk.Status = "error";
                throw;
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested || !useRange)
                {
                    chunk.Status = ct.IsCancellationRequested ? "paused" : "error";
                    throw;
                }

                int delayMs;
                if (ex is ChunkHttpException { IsRateLimit: true } rateEx)
                {
                    rateLimitAttempts++;
                    if (rateLimitAttempts > MaxRateLimitRetries)
                    {
                        chunk.Status = "error";
                        throw;
                    }
                    delayMs = rateEx.RetryAfterMs;
                }
                else
                {
                    networkAttempts++;
                    if (networkAttempts > MaxNetworkRetries)
                    {
                        chunk.Status = "error";
                        throw;
                    }
                    delayMs = 1000 * networkAttempts;
                }

                chunk.Status = "retrying";
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }
    }

    public static async Task RunTaskAsync(
        DownloadTaskItem task,
        SpeedLimiter limiter,
        Action<DownloadTaskItem> onProgress,
        Action onPersist,
        Func<bool> isCanceledStop,
        CancellationToken ct)
    {
        long lastBytes = task.Chunks.Sum(c => c.Downloaded);
        long lastTick = Stopwatch.GetTimestamp();
        long lastPersistTick = lastTick;

        void Notify(bool forcePersist)
        {
            long now = Stopwatch.GetTimestamp();
            long totalDownloaded = task.Chunks.Sum(c => c.Downloaded);
            double deltaSec = Math.Max((double)(now - lastTick) / Stopwatch.Frequency, 0.001);
            double speedBps = task.Status == "downloading"
                ? Math.Max(0, (totalDownloaded - lastBytes) / deltaSec)
                : 0;

            lastBytes = totalDownloaded;
            lastTick = now;

            task.Downloaded = totalDownloaded;
            task.SpeedBps = speedBps;
            task.EtaSec = (speedBps > 0 && task.TotalSize > 0)
                ? (int)Math.Ceiling(Math.Max(0, task.TotalSize - totalDownloaded) / speedBps)
                : null;

            onProgress(task);

            if (forcePersist || (double)(now - lastPersistTick) / Stopwatch.Frequency >= 2.0)
            {
                lastPersistTick = now;
                onPersist();
            }
        }

        try
        {
            task.Error = null;
            string baseDir = !string.IsNullOrWhiteSpace(task.BaseOutputDir) ? task.BaseOutputDir : task.OutputDir;
            if (string.IsNullOrWhiteSpace(baseDir))
                throw new InvalidOperationException("Folder tujuan penyimpanan tidak valid.");

            bool isRefreshingExisting =
                string.IsNullOrEmpty(task.FinalUrl) &&
                !string.IsNullOrEmpty(task.DestPath) &&
                !string.IsNullOrEmpty(task.TempPath) &&
                task.Chunks.Count > 0;

            if (isRefreshingExisting)
            {
                task.Status = "probing";
                Notify(true);

                ProbeResult meta = await HttpProber.ProbeAsync(task.Url, ct).ConfigureAwait(false);
                if (task.TotalSize > 0 && meta.TotalSize > 0 && meta.TotalSize != task.TotalSize)
                {
                    throw new InvalidOperationException("Ukuran file pada link baru berbeda dengan unduhan sebelumnya.");
                }

                task.FinalUrl = meta.FinalUrl;
                task.SupportsRange = meta.SupportsRange;
                Directory.CreateDirectory(task.OutputDir);
            }
            else if (string.IsNullOrEmpty(task.FinalUrl) || string.IsNullOrEmpty(task.DestPath))
            {
                task.Status = "probing";
                Notify(true);

                ProbeResult meta = await HttpProber.ProbeAsync(task.Url, ct).ConfigureAwait(false);

                string targetName = meta.FileName;
                if (!string.IsNullOrWhiteSpace(task.CustomFileName))
                {
                    string safeCustom = HttpProber.SanitizeFileName(task.CustomFileName);
                    string serverExt = Path.GetExtension(meta.FileName);
                    string customExt = Path.GetExtension(safeCustom);
                    targetName = (!string.IsNullOrEmpty(customExt) || string.IsNullOrEmpty(serverExt))
                        ? safeCustom
                        : $"{safeCustom}{serverExt}";
                }

                string detectedCat = CategoryHelper.DetectCategory(targetName);
                if (detectedCat == "Other")
                    detectedCat = CategoryHelper.DetectCategory(meta.FileName);

                task.Category = detectedCat;
                task.OutputDir = task.AutoCategorize
                    ? CategoryHelper.GetCategoryDir(baseDir, detectedCat)
                    : baseDir;

                Directory.CreateDirectory(task.OutputDir);

                string uniqueDest = ResolveUniqueFilePath(task.OutputDir, targetName);
                task.FinalUrl = meta.FinalUrl;
                task.FileName = Path.GetFileName(uniqueDest);
                task.DestPath = uniqueDest;
                task.TempPath = $"{uniqueDest}.bytesplit.part";
                task.TotalSize = meta.TotalSize;
                task.SupportsRange = meta.SupportsRange;
                task.Chunks = BuildChunks(meta.TotalSize, task.Connections, meta.SupportsRange);
            }
            else
            {
                Directory.CreateDirectory(task.OutputDir);
            }

            bool useRange = task.SupportsRange && task.Chunks.Count > 0 && task.TotalSize > 0;
            bool canResumeDisk = useRange && File.Exists(task.TempPath);

            using (SafeFileHandle fileHandle = File.OpenHandle(
                task.TempPath!,
                canResumeDisk ? FileMode.Open : FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.ReadWrite,
                FileOptions.Asynchronous))
            {
                if (!canResumeDisk)
                {
                    foreach (var c in task.Chunks)
                    {
                        c.Downloaded = 0;
                        c.Status = "pending";
                    }
                    lastBytes = 0;
                    if (task.TotalSize > 0 && useRange)
                    {
                        RandomAccess.SetLength(fileHandle, task.TotalSize);
                    }
                }

                task.Status = "downloading";
                Notify(true);

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                using var uiTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
                var timerTask = Task.Run(async () =>
                {
                    try
                    {
                        while (await uiTimer.WaitForNextTickAsync(linkedCts.Token).ConfigureAwait(false))
                        {
                            Notify(false);
                        }
                    }
                    catch (OperationCanceledException) { }
                }, CancellationToken.None);

                try
                {
                    var workers = task.Chunks.Select(async chunk =>
                    {
                        try
                        {
                            await DownloadChunkAsync(
                                task.FinalUrl!,
                                chunk,
                                useRange,
                                task.Chunks.Count,
                                fileHandle,
                                limiter,
                                linkedCts.Token).ConfigureAwait(false);
                        }
                        catch
                        {
                            linkedCts.Cancel();
                            throw;
                        }
                    });

                    await Task.WhenAll(workers).ConfigureAwait(false);
                }
                finally
                {
                    linkedCts.Cancel();
                    await timerTask.ConfigureAwait(false);
                }
            }

            if (ct.IsCancellationRequested)
            {
                HandleStopped(task, isCanceledStop(), Notify);
                return;
            }

            long totalDone = task.Chunks.Sum(c => c.Downloaded);
            if (task.TotalSize > 0 && totalDone != task.TotalSize)
            {
                throw new IOException($"Ukuran file akhir tidak cocok ({totalDone}/{task.TotalSize} bytes).");
            }

            File.Move(task.TempPath!, task.DestPath!, overwrite: true);
            task.Status = "completed";
            task.SpeedBps = 0;
            task.EtaSec = 0;
            task.CompletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Notify(true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            HandleStopped(task, isCanceledStop(), Notify);
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested)
            {
                HandleStopped(task, isCanceledStop(), Notify);
                return;
            }

            if (!task.SupportsRange && !string.IsNullOrEmpty(task.TempPath))
            {
                TryDeleteFile(task.TempPath);
            }

            task.Status = "error";
            task.Error = ex.Message;
            task.SpeedBps = 0;
            task.EtaSec = null;
            Notify(true);
        }
    }

    private static void HandleStopped(DownloadTaskItem task, bool isCanceled, Action<bool> notify)
    {
        if (isCanceled)
        {
            if (!string.IsNullOrEmpty(task.TempPath))
                TryDeleteFile(task.TempPath);
            task.Status = "canceled";
        }
        else
        {
            task.Status = "paused";
        }

        task.SpeedBps = 0;
        task.EtaSec = null;
        notify(true);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private sealed class ChunkHttpException : Exception
    {
        public bool IsFatal { get; }
        public bool IsRateLimit { get; }
        public int RetryAfterMs { get; }

        public ChunkHttpException(string message, bool isFatal, bool isRateLimit, int retryAfterMs)
            : base(message)
        {
            IsFatal = isFatal;
            IsRateLimit = isRateLimit;
            RetryAfterMs = retryAfterMs;
        }
    }
}