using System;
using System.Collections.Generic;
using System.Text;
using SIPSorcery.Net;

namespace SIPSorcery.net.RTP.RTPHeaderExtensions
{
    // AbsSendTimeExtension is a extension payload format in
    // http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time
    // Code refrence: https://chromium.googlesource.com/external/webrtc/+/e2a017725570ead5946a4ca8235af27470ca0df9/webrtc/modules/rtp_rtcp/source/rtp_header_extensions.cc#49
    public class AbsSendTimeExtension: RTPHeaderExtension
    {
        public const string RTP_HEADER_EXTENSION_URI    = "http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time";

        public AbsSendTimeExtension(int id): base(id, RTP_HEADER_EXTENSION_URI, RTPHeaderExtensionType.OneByte)
        {
        }

        public override byte[] WriteHeader()
        {
            return AbsSendTimePayload(Id, DateTimeOffset.Now);
        }

        public override void ReadHeader(ref MediaStreamTrack localTrack, ref MediaStreamTrack remoteTrack, RTPHeader header, byte[] data)
        {
            var ntpTimestamp = GetUlong(data, 0);
            if (ntpTimestamp.HasValue)
            {
                remoteTrack.LastAbsoluteCaptureTimestamp = new TimestampPair() { NtpTimestamp = ntpTimestamp.Value, RtpTimestamp = header.Timestamp };
            }
        }

        // DateTimeOffset.UnixEpoch only available in newer target frameworks
        private static readonly DateTimeOffset UnixEpoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // inspired by https://github.com/pion/rtp/blob/master/abssendtimeextension.go
        private static byte[] AbsSendTimePayload(int id, DateTimeOffset now)
        {
            ulong unixNanoseconds = (ulong)((now - UnixEpoch).Ticks * 100L);
            var seconds = unixNanoseconds / (ulong)1e9;
            seconds += 0x83AA7E80UL; // offset in seconds between unix epoch and ntp epoch
            var f = unixNanoseconds % (ulong)1e9;
            f <<= 32;
            f /= (ulong)1e9;
            seconds <<= 32;
            var ntp = seconds | f;
            var abs = ntp >> 14;
            var length = 2; // extension length (3-1)

            return new[]
            {
                (byte)((id << 4) | length),
                (byte)((abs & 0xff0000UL) >> 16),
                (byte)((abs & 0xff00UL) >> 8),
                (byte)(abs & 0xffUL)
            };
        }

        private static ulong? GetUlong(byte[] data, int offset)
        {
            if (offset + sizeof(ulong) - 1 >= data.Length)
            {
                return null;
            }

            return BitConverter.IsLittleEndian ?
                SIPSorcery.Sys.NetConvert.DoReverseEndian(BitConverter.ToUInt64(data, offset)) :
                BitConverter.ToUInt64(data, offset);
        }
    }
}
