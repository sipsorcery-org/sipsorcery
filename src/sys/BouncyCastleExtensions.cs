using System;
using System.Buffers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Org.BouncyCastle.Crypto.Digests;
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
            if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> segment))
            {
                datagramSender.Send(segment.Array!, segment.Offset, segment.Count);
            }
            else
            {
                throw new NotSupportedException("Only array-backed memory is supported.");
            }
#endif
        }

#if !NETSTANDARD2_0_OR_GREATER || !NETCOREAPP2_1_OR_GREATER || NETFRAMEWORK
        public static void BlockUpdate(this GeneralDigest digest, ReadOnlySpan<byte> input)
        {
            digest.BlockUpdate(input.ToArray(), 0, input.Length);
        }

        public static int DoFinal(this GeneralDigest digest, Span<byte> output)
        {
            var tempBuffer = new byte[output.Length];
            var result = digest.DoFinal(tempBuffer, 0);
            tempBuffer.CopyTo(output);
            return result;
        }
#endif
    }
}
