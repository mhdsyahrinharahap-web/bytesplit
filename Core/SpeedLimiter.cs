using System.Diagnostics;

namespace ByteSplit.Core;

public sealed class SpeedLimiter
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _bytesPerSecond;
    private double _tokens;
    private long _lastTimestamp = Stopwatch.GetTimestamp();

    public long BytesPerSecond => Interlocked.Read(ref _bytesPerSecond);

    public SpeedLimiter(long bytesPerSecond = 0)
    {
        SetLimit(bytesPerSecond);
    }

   public long SetLimit(long bytesPerSecond)
{
    long clamped = Math.Max(0, bytesPerSecond);
    Interlocked.Exchange(ref _bytesPerSecond, clamped);
    _tokens = clamped;
    _lastTimestamp = Stopwatch.GetTimestamp();
    return clamped;
}

    public async ValueTask ConsumeAsync(int bytes, CancellationToken ct)
    {
        long limit = BytesPerSecond;
        if (limit <= 0 || bytes <= 0)
            return;

        int delayMs = 0;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            limit = BytesPerSecond;
            if (limit <= 0)
                return;

            long now = Stopwatch.GetTimestamp();
            double elapsedSec = (double)(now - _lastTimestamp) / Stopwatch.Frequency;
            _lastTimestamp = now;

            _tokens = Math.Min(limit, _tokens + elapsedSec * limit);
            _tokens -= bytes;

            if (_tokens < 0)
            {
                delayMs = (int)Math.Ceiling((-_tokens / limit) * 1000.0);
            }
        }
        finally
        {
            _gate.Release();
        }

        if (delayMs > 0)
        {
            await Task.Delay(delayMs, ct).ConfigureAwait(false);
        }
    }
}