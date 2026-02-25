using System;

namespace SIPSorcery.Net;

public static class SctpPacketExtensions
{
    extension(SctpPacket source)
    {
        public static bool VerifyChecksum(byte[] buffer, int posn, int length) => SctpPacket.VerifyChecksum(buffer.AsSpan(posn, length));

        public static uint GetVerificationTag(byte[] buffer, int posn, int length) => SctpPacket.GetVerificationTag(buffer.AsSpan(posn, length));

        public static bool IsValid(byte[] buffer, int posn, int length, uint requiredTag) => SctpPacket.IsValid(buffer.AsSpan(posn, length), requiredTag);

        public static SctpPacket Parse(byte[] buffer, int offset, int length) => SctpPacket.Parse(buffer.AsSpan(offset, length));
    }
}
