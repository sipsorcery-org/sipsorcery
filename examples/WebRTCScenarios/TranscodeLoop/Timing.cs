using System.Diagnostics;

namespace SIPSorcery.TranscodeLoop;

/// <summary>The app's one clock, in milliseconds since start. The page learns its offset from performance.now().</summary>
internal static class Clock
{
    private static readonly Stopwatch _watch = Stopwatch.StartNew();
    public static double NowMs => _watch.Elapsed.TotalMilliseconds;
}

/// <summary>The last few timings of something (decode, encode...), for their median and 95th percentile.</summary>
internal sealed class Timings(int capacity = 120)
{
    private readonly double[] _values = new double[capacity];
    private int _count, _next;

    public void Add(double ms)
    {
        lock (_values)
        {
            _values[_next] = ms;
            _next = (_next + 1) % _values.Length;
            if (_count < _values.Length) _count++;
        }
    }

    /// <summary>Median, 95th percentile and maximum, rounded to 0.1 ms; null when nothing has been timed.</summary>
    public object? Summary()
    {
        double[] sorted;
        lock (_values)
        {
            if (_count == 0) return null;
            sorted = _values.AsSpan(0, _count).ToArray();
        }
        Array.Sort(sorted);
        double At(double p) => Math.Round(sorted[Math.Min(sorted.Length - 1, (int)(p * sorted.Length))], 1);
        return new { p50 = At(0.5), p95 = At(0.95), max = Math.Round(sorted[^1], 1) };
    }
}
