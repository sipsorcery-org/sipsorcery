using System;

namespace SIPSorcery.Net;

public static class SctpChunkExtensions
{
    extension(SctpChunk source)
    {
        public ushort GetChunkLength(bool padded) => source.GetByteCount(padded);

        public ushort WriteTo(byte[] buffer, int posn) => source.WriteBytes(buffer.AsSpan(posn));

        public static SctpChunk Parse(byte[] buffer, int posn)
            => SctpChunk.Parse(buffer.AsSpan(posn));
    }
}
