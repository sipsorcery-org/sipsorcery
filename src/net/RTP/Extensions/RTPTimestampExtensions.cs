using System;

namespace SIPSorcery.Net;

/// <summary>
/// Extension methods for converting between RTP timestamp units and milliseconds.
/// </summary>
public static class RtpTimestampExtensions
{
    /// <summary>
    /// Converts a duration in milliseconds to the equivalent number of RTP timestamp units,
    /// given an RTP clock rate (e.g. 48000 for 48 kHz audio).
    /// </summary>
    /// <param name="durationMilliseconds">The duration in milliseconds.</param>
    /// <param name="rtpClockRate">The RTP clock rate (units per second), e.g. 48000.</param>
    /// <returns>
    /// The number of RTP timestamp units that corresponds to the given duration. 
    /// This value is rounded to the nearest whole unit.
    /// </returns>
    public static uint ToRtpUnits(this double durationMilliseconds, int rtpClockRate)
    {
        if (rtpClockRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rtpClockRate), "Clock rate must be positive.");
        }

        // Compute: rtpUnits = round(clockRate * (durationMs / 1000))
        double units = rtpClockRate * (durationMilliseconds / 1000.0);
        return (uint)Math.Round(units, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Converts a duration in milliseconds (integer) to the equivalent number of RTP timestamp units,
    /// given an RTP clock rate. Uses the same rounding logic as the double overload.
    /// </summary>
    /// <param name="durationMilliseconds">The duration in milliseconds.</param>
    /// <param name="rtpClockRate">The RTP clock rate (units per second).</param>
    /// <returns>
    /// The number of RTP timestamp units that corresponds to the given duration. 
    /// This value is rounded to the nearest whole unit.
    /// </returns>
    public static uint ToRtpUnits(this int durationMilliseconds, int rtpClockRate)
    {
        return ((double)durationMilliseconds).ToRtpUnits(rtpClockRate);
    }

    /// <summary>
    /// Converts an RTP timestamp‐unit count to the equivalent duration in milliseconds,
    /// given an RTP clock rate (e.g. 48000 for 48 kHz audio).
    /// </summary>
    /// <param name="rtpUnits">The number of RTP timestamp units.</param>
    /// <param name="rtpClockRate">The RTP clock rate (units per second), e.g. 48000.</param>
    /// <returns>
    /// The duration in milliseconds (double precision) that corresponds to the given RTP timestamp units.
    /// </returns>
    public static double ToDurationMilliseconds(this uint rtpUnits, int rtpClockRate)
    {
        if (rtpClockRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rtpClockRate), "Clock rate must be positive.");
        }

        // Compute: durationMs = (rtpUnits / clockRate) * 1000
        return (rtpUnits / (double)rtpClockRate) * 1000.0;
    }

    /// <summary>
    /// Converts an RTP timestamp‐unit count to the equivalent duration in milliseconds,
    /// rounded to the nearest integer, given an RTP clock rate.
    /// </summary>
    /// <param name="rtpUnits">The number of RTP timestamp units.</param>
    /// <param name="rtpClockRate">The RTP clock rate (units per second).</param>
    /// <returns>
    /// The duration in milliseconds (integer) that corresponds to the given RTP timestamp units,
    /// rounded to the nearest millisecond.
    /// </returns>
    public static uint ToDurationMillisecondsInt(this uint rtpUnits, int rtpClockRate)
    {
        double ms = rtpUnits.ToDurationMilliseconds(rtpClockRate);
        return (uint)Math.Round(ms, MidpointRounding.AwayFromZero);
    }
}
