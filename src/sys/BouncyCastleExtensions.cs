using System;
using System.Buffers;
using Org.BouncyCastle.Tls;

namespace SIPSorcery.Sys
{
    internal static class BouncyCastleExtensions
    {
        public static void Send(this DatagramSender datagramSender, ReadOnlyMemory<byte> buffer)
        {
#if NET6_0_OR_GREATER
            datagramSender.Send(buffer.Span);
#else
            var tempBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                buffer.CopyTo(tempBuffer);
                datagramSender.Send(tempBuffer, 0, buffer.Length);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(tempBuffer);
            }
#endif
        }
    }
}
