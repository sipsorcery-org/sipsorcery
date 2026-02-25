using System;
using CommunityToolkit.HighPerformance.Buffers;

namespace SIPSorcery.Sys;

public static class ByteSerializableExtensions
{
    extension(IByteSerializable source)
    {
        public byte[] GetBytes()
        {
            using var writer = new ArrayPoolBufferWriter<byte>(0);
            source.WriteTo(writer);
            return writer.WrittenSpan.ToArray();
        }
    }
}
