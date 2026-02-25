using System;

namespace SIPSorcery.Net;

public static class SctpHeaderExtensions
{
    extension(SctpHeader source)
    {
        public static SctpHeader Parse(byte[] buffer, int posn) => SctpHeader.Parse(buffer.AsSpan(posn));
    }
}
