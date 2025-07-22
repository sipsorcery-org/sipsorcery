using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    partial class STUNMessage
    {
        [Obsolete("Use ParseSTUNMessage(ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static STUNMessage? ParseSTUNMessage(byte[] buffer, int bufferLength)
        {
            if (buffer != null && buffer.Length >= bufferLength)
            {
                return ParseSTUNMessage(buffer.AsSpan(0, bufferLength));
            }

            return null;
        }

        [Obsolete("Use WriteToBufferStringKey(Span<byte>, string, bool) in conjunction with GetByteBufferSizeStringKey(string, bool) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public byte[] ToByteBufferStringKey(string messageIntegrityKey, bool addFingerprint)
        {
            var bufferSize = GetByteBufferSizeStringKey(messageIntegrityKey, addFingerprint);
            var buffer = new byte[bufferSize];
            WriteToBufferStringKey(buffer, messageIntegrityKey, addFingerprint);
            return buffer;
        }

        [Obsolete("Use WriteToBuffer(Span<byte>, ReadOnlySpan<byte>, bool) in conjunction with GetByteBufferSize(ReadOnlySpan<byte>, bool) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public byte[] ToByteBuffer(byte[] messageIntegrityKey, bool addFingerprint)
        {
            var messageIntegrityKeySpan = messageIntegrityKey.AsSpan();
            var result = new byte[GetByteBufferSize(messageIntegrityKeySpan, addFingerprint)];
            WriteToBuffer(result.AsSpan(), messageIntegrityKeySpan, addFingerprint);
            return result;
        }
    }
}
