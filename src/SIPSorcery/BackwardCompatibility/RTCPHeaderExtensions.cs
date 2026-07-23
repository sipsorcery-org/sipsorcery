using System;
using CommunityToolkit.HighPerformance.Buffers;

namespace SIPSorcery.Net;

public static class RTCPHeaderExtensions
{
    extension(RTCPHeader source)
    {
        public byte[] GetHeader(int receptionReportCount, ushort length)
        {
            using var writer = new ArrayPoolBufferWriter<byte>(0);
            source.WriteTo(writer);
            return writer.WrittenSpan.ToArray();
        }

        public static SctpHeader Parse(byte[] buffer, int posn) => SctpHeader.Parse(buffer.AsSpan(posn));
    }
}
