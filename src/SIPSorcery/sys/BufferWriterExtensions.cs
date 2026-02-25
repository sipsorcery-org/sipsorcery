using System.Buffers;
using System.Buffers.Binary;

namespace System.Buffers;

internal static class BufferWriterExtensions
{
    extension(IBufferWriter<byte> writer)
    {
        public void WriteInt32BigEndian(int data)
        {
            var span = writer.GetSpan(sizeof(int));
            BinaryPrimitives.WriteInt32BigEndian(span, data);
            writer.Advance(sizeof(int));
        }

        public void WriteUInt32BigEndian(uint data)
        {
            var span = writer.GetSpan(sizeof(uint));
            BinaryPrimitives.WriteUInt32BigEndian(span, data);
            writer.Advance(sizeof(uint));
        }

        public void WriteUInt64BigEndian(ulong data)
        {
            var span = writer.GetSpan(sizeof(ulong));
            BinaryPrimitives.WriteUInt64BigEndian(span, data);
            writer.Advance(sizeof(ulong));
        }
    }
}
