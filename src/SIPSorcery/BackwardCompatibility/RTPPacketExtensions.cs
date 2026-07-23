using System;

namespace SIPSorcery.Net;

public static class RTPPacketExtensions
{
    extension(RTPPacket source)
    {
        public byte[] GetPayloadBytes()
        {
            var buffer = new byte[source.GetPayloadSize()];
            source.WritePayload(buffer.AsSpan());
            return buffer;
        }
    }
}
