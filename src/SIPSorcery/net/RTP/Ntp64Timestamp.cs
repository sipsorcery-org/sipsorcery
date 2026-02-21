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
        public static ulong AddFraction(ulong timestamp, double fraction) {
            var fractionalPart = GetFraction(timestamp);
            fractionalPart += ((uint) (fraction * uint.MaxValue)) & 0x00000000FFFFFFFF; 
            return (timestamp & 0xFFFFFFFF00000000) | fractionalPart;
        }

        public static ulong AddSeconds(ulong timestamp, uint seconds) {
            var sec = (ulong) GetSeconds(timestamp) + seconds;
            return (sec << 32) | (timestamp & 0x00000000FFFFFFFF);
        }

        public static uint GetSeconds(ulong timestamp) {
            return (uint)((timestamp & 0xFFFFFFFF00000000) >> 32);
        }

        public static uint GetFraction(ulong timestamp)
        {
            return (uint)(timestamp & 0x00000000FFFFFFFF);
        }

        public static DateTime GetDatetime(ulong timestamp) {
            var epochStart = new DateTime(1900, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            var millis = ((double)GetFraction(timestamp) * 1000 / (double)uint.MaxValue);
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
