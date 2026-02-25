using System;

namespace SIPSorcery.Net;

public static class STUNMessageExtensions
{
    extension(STUNMessage message)
    {
        public byte[] ToByteBuffer(byte[] messageIntegrityKey, bool addFingerprint)
        {
            var buffer = new byte[message.GetByteBufferSize(messageIntegrityKey, addFingerprint)];
            message.WriteToBuffer(buffer, messageIntegrityKey, addFingerprint);
            return buffer;
        }

        public byte[] ToByteBufferStringKey(string messageIntegrityKey, bool addFingerprint) => message.ToByteBuffer(System.Text.Encoding.UTF8.GetBytes(messageIntegrityKey), addFingerprint);

        public static STUNMessage? ParseSTUNMessage(byte[] buffer, int bufferLength) => STUNMessage.ParseSTUNMessage(buffer.AsSpan(0, bufferLength));
    }
}
