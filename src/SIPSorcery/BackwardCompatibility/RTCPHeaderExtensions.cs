using System;

namespace SIPSorcery.Net;

public static class RTCPHeaderExtensions
{
    extension(RTCPHeader source)
    {
        public byte[] GetHeader(int receptionReportCount, ushort length)
        {
            byte[] headerBuffer = new byte[source.GetByteCount()];
            source.WriteBytes(headerBuffer.AsSpan());
            return headerBuffer;
        }

        public static SctpHeader Parse(byte[] buffer, int posn) => SctpHeader.Parse(buffer.AsSpan(posn));
    }
}
