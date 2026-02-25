using System;

namespace SIPSorcery.Net;

public static class RTPHeaderExtensions
{
    extension(RTPHeader source)
    {
        public byte[] GetHeader(UInt16 sequenceNumber, uint timestamp, uint syncSource)
        {
            source.SetHeader(sequenceNumber, timestamp, syncSource);
            var buffer = new byte[source.GetByteCount()];
            source.WriteBytes(buffer.AsSpan());
            return buffer;
        }
    }
}
