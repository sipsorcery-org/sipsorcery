using System;

namespace SIPSorcery.Net;

public static class SctpTransportExtensions
{
    extension(SctpTransport source)
    {
        public void Send(string? associationID, byte[] buffer, int offset, int length) => source.Send(associationID!, buffer.AsMemory(offset, length), default);
    }
}
