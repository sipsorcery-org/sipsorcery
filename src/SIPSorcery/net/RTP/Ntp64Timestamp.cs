//-----------------------------------------------------------------------------
// Filename: Ntp64Timestamp.cs
//
// Description: Helpers for the 64 bit NTP timestamp format used by RTCP sender
// reports and the abs-capture-time RTP header extension. The format is a 32 bit
// count of seconds since 1 Jan 1900 followed by a 32 bit fraction of a second.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

namespace SIPSorcery.Net
{
    public class TimestampPair
    {
        public uint RtpTimestamp { get; set; }
        public ulong NtpTimestamp { get; set; }
    }

    public class Ntp64Timestamp
    {
        /// <summary>
        /// The number of units in the 32 bit fractional field of an NTP timestamp that
        /// make up one second, i.e. 2^32.
        /// </summary>
        private const double FRACTIONS_PER_SECOND = 4294967296.0;

        private const ulong SECONDS_MASK = 0xFFFFFFFF00000000;
        private const ulong FRACTION_MASK = 0x00000000FFFFFFFF;

        /// <summary>
        /// Adds a fractional number of seconds to an NTP timestamp.
        /// </summary>
        /// <param name="timestamp">The NTP timestamp to add to.</param>
        /// <param name="fraction">The number of seconds to add. Must not be negative. A value
        /// of one or more is carried into the seconds field.</param>
        /// <returns>The NTP timestamp with the fraction added.</returns>
        public static ulong AddFraction(ulong timestamp, double fraction)
        {
            if (fraction < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fraction), "An NTP timestamp fraction cannot be negative.");
            }

            // Accumulate in 64 bits so that a carry out of the 32 bit fractional field is
            // not silently dropped, which would lose a whole second.
            var total = GetFraction(timestamp) + (ulong)(fraction * FRACTIONS_PER_SECOND);

            var withFraction = (timestamp & SECONDS_MASK) | (total & FRACTION_MASK);
            return AddSeconds(withFraction, (uint)(total >> 32));
        }

        public static ulong AddSeconds(ulong timestamp, uint seconds) {
            var sec = (ulong) GetSeconds(timestamp) + seconds;
            return (sec << 32) | (timestamp & FRACTION_MASK);
        }

        public static uint GetSeconds(ulong timestamp) {
            return (uint)((timestamp & SECONDS_MASK) >> 32);
        }

        public static uint GetFraction(ulong timestamp)
        {
            return (uint)(timestamp & FRACTION_MASK);
        }

        /// <summary>
        /// Gets an NTP timestamp as a number of seconds, including the fractional part.
        /// </summary>
        /// <param name="timestamp">The NTP timestamp to convert.</param>
        /// <returns>The timestamp as seconds since the start of the NTP era.</returns>
        public static double ToSeconds(ulong timestamp)
        {
            return GetSeconds(timestamp) + GetFraction(timestamp) / FRACTIONS_PER_SECOND;
        }

        /// <summary>
        /// Gets the number of seconds that elapsed between two NTP timestamps.
        /// </summary>
        /// <param name="from">The earlier NTP timestamp.</param>
        /// <param name="to">The later NTP timestamp.</param>
        /// <returns>The elapsed seconds, negative if <paramref name="to"/> is the earlier of
        /// the two. Both timestamps must be from the same NTP era.</returns>
        public static double GetElapsedSeconds(ulong from, ulong to)
        {
            return ToSeconds(to) - ToSeconds(from);
        }

        public static DateTime GetDatetime(ulong timestamp) {
            var epochStart = new DateTime(1900, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            var millis = ((double)GetFraction(timestamp) * 1000 / FRACTIONS_PER_SECOND);
            var time = epochStart + TimeSpan.FromSeconds(GetSeconds(timestamp)) + TimeSpan.FromMilliseconds(millis);
            return time;
        }

        public static ulong InterpolateNtpTime(TimestampPair lastTimestampPair, uint currentRtpTimestamp, int clockrate)
        {
            var diff = currentRtpTimestamp - lastTimestampPair.RtpTimestamp;
            var diffTime = diff / (double)clockrate;
            var seconds = Math.Truncate(diffTime);
            var fractionalPart = (diffTime - seconds);

            var withSeconds = AddSeconds(lastTimestampPair.NtpTimestamp, (uint)seconds);
            return AddFraction(withSeconds, fractionalPart);
        }

        public static DateTime InterpolateDatetime(TimestampPair lastTimestampPair, uint currentRtpTimestamp, int clockrate) {
            var time = InterpolateNtpTime(lastTimestampPair, currentRtpTimestamp, clockrate);
            return GetDatetime(time);
        }
    }
}
