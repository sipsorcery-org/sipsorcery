using System;

namespace SIPSorcery.Net;

public static class AV1PacketiserExtensions
{
    extension(AV1Packetiser source)
    {
        public static byte[] WriteLeb128(int value)
        {
            var leb128Length = AV1Packetiser.GetLeb128Length(value);
            var buffer = new byte[leb128Length];
            AV1Packetiser.WriteLeb128(buffer.AsSpan(), value);
            return buffer;
        }
    }
}
