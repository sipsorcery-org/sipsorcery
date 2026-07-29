using System;
using CommunityToolkit.HighPerformance.Buffers;

namespace SIPSorcery.Net;

public static class SctpChunkExtensions
{
    extension(SctpChunk source)
    {
        public ushort GetChunkLength(bool padded) => source.GetByteCount(padded);

        public ushort WriteTo(byte[] buffer, int posn)
        {
            using var writer = new ArrayPoolBufferWriter<byte>(0);
            int written = source.WriteTo(writer);
            writer.WrittenSpan.CopyTo(buffer.AsSpan(posn));
            return (ushort)written;
        }

        public static SctpChunk Parse(byte[] buffer, int posn)
            => SctpChunk.Parse(buffer.AsSpan(posn));
    }
}
