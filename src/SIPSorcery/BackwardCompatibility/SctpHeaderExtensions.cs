using System;
using CommunityToolkit.HighPerformance.Buffers;

namespace SIPSorcery.Net;

public static class SctpHeaderExtensions
{
    extension(SctpHeader source)
    {
        public void WriteToBuffer(byte[] buffer, int posn)
        {
            using var writer = new ArrayPoolBufferWriter<byte>(0);
            source.WriteTo(writer);
            writer.WrittenSpan.CopyTo(buffer.AsSpan(posn));
        }

        public static SctpHeader Parse(byte[] buffer, int posn) => SctpHeader.Parse(buffer.AsSpan(posn));
    }
}
