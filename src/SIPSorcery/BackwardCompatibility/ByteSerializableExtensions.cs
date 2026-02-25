using System;

namespace SIPSorcery.Sys;

public static class ByteSerializableExtensions
{
    extension(IByteSerializable source)
    {
        public byte[] GetBytes()
        {
            var buffer = new byte[source.GetByteCount()];
            source.WriteBytes(buffer);
            return buffer;
        }

        public int ToByteBuffer(byte[] buffer, int startIndex)
        {
            return source.WriteBytes(buffer.AsSpan(startIndex));
        }
    }
}
