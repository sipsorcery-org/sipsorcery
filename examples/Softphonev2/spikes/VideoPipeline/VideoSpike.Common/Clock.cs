using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VideoSpike;

/// <summary>
/// Wall-clock milliseconds since the Unix epoch, read from the high-resolution counter and
/// anchored to UTC once per process, so it never jumps when the system clock is adjusted.
/// Every timestamp the spike compares across components is in this timebase.
/// </summary>
public static class Clock
{
    private static readonly double _anchorMs = (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
    private static readonly long _anchorTicks = Stopwatch.GetTimestamp();

    public static double NowMs => _anchorMs + (Stopwatch.GetTimestamp() - _anchorTicks) * 1000.0 / Stopwatch.Frequency;

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);

    /// <summary>Raises the Windows timer resolution to 1 ms so Thread.Sleep(1) pacing is accurate.</summary>
    public static void UseHighResolutionTimer() => timeBeginPeriod(1);
}
