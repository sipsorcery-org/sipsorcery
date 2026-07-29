using CommunityToolkit.HighPerformance.Buffers;

namespace SIPSorcery.Net;

public static class RtpVideoFramerExtensions
{
    extension(RtpVideoFramer source)
    {
        public byte[]? GotRtpPacket(RTPPacket rtpPacket)
        {
            using var bufferWriter = new ArrayPoolBufferWriter<byte>(0);
            var gotFrame = source.GotRtpPacket(bufferWriter, rtpPacket);
            return gotFrame ? bufferWriter.WrittenMemory.ToArray() : null;
        }
    }
}
