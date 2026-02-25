using System;
using CommunityToolkit.HighPerformance.Buffers;

namespace SIPSorcery.Net;

public static class RTPHeaderExtensions
{
    extension(RTPHeader source)
    {
        public byte[] GetHeader(UInt16 sequenceNumber, uint timestamp, uint syncSource)
        {
            source.SetHeader(sequenceNumber, timestamp, syncSource);
            using var writer = new ArrayPoolBufferWriter<byte>(0);
            source.WriteTo(writer);
            return writer.WrittenSpan.ToArray();
        }
    }
}
