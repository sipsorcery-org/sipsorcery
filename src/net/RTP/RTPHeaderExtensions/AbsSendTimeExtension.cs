using System;
using SIPSorcery.Net;

namespace SIPSorcery.Net
{
    // AbsSendTimeExtension is a extension payload format in
    // http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time
    // Code reference: https://chromium.googlesource.com/external/webrtc/+/e2a017725570ead5946a4ca8235af27470ca0df9/webrtc/modules/rtp_rtcp/source/rtp_header_extensions.cc#19
    public class AbsSendTimeExtension: RTPHeaderExtension
    {
        public const string RTP_HEADER_EXTENSION_URI = "http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time";
        internal const int RTP_HEADER_EXTENSION_SIZE = 3;

        public AbsSendTimeExtension(int id): base(id, RTP_HEADER_EXTENSION_URI, RTP_HEADER_EXTENSION_SIZE, RTPHeaderExtensionType.OneByte)
        {
        }

        public override void Set(Object value)
        {
            // Nothing to do here 
        }

        internal static byte[] AbsSendTime(int id, int extensionSize, DateTimeOffset now)
        {
            // inspired by https://github.com/pion/rtp/blob/master/abssendtimeextension.go
            ulong unixNanoseconds = (ulong)((now - UnixEpoch).Ticks * 100L);
            var seconds = unixNanoseconds / (ulong)1e9;
            seconds += 0x83AA7E80UL; // offset in seconds between unix epoch and ntp epoch
            var f = unixNanoseconds % (ulong)1e9;
            f <<= 32;
            f /= (ulong)1e9;
            seconds <<= 32;
            var ntp = seconds | f;
            var abs = ntp >> 14;

            return new[]
            {
                (byte)((id << 4) | extensionSize - 1),
                (byte)((abs & 0xff0000UL) >> 16),
                (byte)((abs & 0xff00UL) >> 8),
                (byte)(abs & 0xffUL)
            };
        }
        
        public override byte[] Marshal()
        {
            return AbsSendTime(Id, ExtensionSize, DateTimeOffset.Now);
        }

        public override Object Unmarshal(RTPHeader header, byte[] data)
        {
            var ntpTimestamp = GetUlong(data);
            return new TimestampPair() { NtpTimestamp = ntpTimestamp.HasValue ? ntpTimestamp.Value : 0, RtpTimestamp = header.Timestamp };
        }

        // DateTimeOffset.UnixEpoch only available in newer target frameworks
        private static readonly DateTimeOffset UnixEpoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private ulong? GetUlong(byte[] data)
        {
            if ( (data.Length != ExtensionSize) || ((sizeof(ulong) - 1) > data.Length) )
            {
                return null;
            }

            return BitConverter.IsLittleEndian ?
                SIPSorcery.Sys.NetConvert.DoReverseEndian(BitConverter.ToUInt64(data, 0)) :
                BitConverter.ToUInt64(data, 0);
        }
    }
}
