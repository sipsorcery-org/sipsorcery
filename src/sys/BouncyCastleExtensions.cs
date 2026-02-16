using System;
using System.Buffers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Tls;

namespace SIPSorcery.Sys;

internal static class BouncyCastleExtensions
{
    extension(DatagramSender datagramSender)
    {
        public void Send(ReadOnlyMemory<byte> buffer)
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
    }

#if !NETSTANDARD2_0_OR_GREATER || !NETCOREAPP2_1_OR_GREATER
    extension(GeneralDigest digest)
    {
        public void BlockUpdate(ReadOnlySpan<byte> input)
        {
            digest.BlockUpdate(input.ToArray(), 0, input.Length);
        }

        public int DoFinal(Span<byte> output)
        {
            var tempBuffer = new byte[output.Length];
            var result = digest.DoFinal(tempBuffer, 0);
            tempBuffer.CopyTo(output);
            return result;
        }
    }
#endif

#if !NETCOREAPP2_1_OR_GREATER && !NETSTANDARD2_1_OR_GREATER
    extension(IBlockCipher blockCipher)
    {
        /// <summary>Process a block.</summary>
        /// <param name="input">The input block as a span.</param>
        /// <param name="output">The output span.</param>
        /// <exception cref="DataLengthException">If input block is wrong size, or output span too small.</exception>
        /// <returns>The number of bytes processed and produced.</returns>
        public int ProcessBlock(ReadOnlySpan<byte> input, Span<byte> output)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(output.Length);

            try
            {
                var result = blockCipher.ProcessBlock(input.ToArray(), 0, buffer, 0);
                buffer.AsSpan(0, result).CopyTo(output);
                return result;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, true);
            }
        }
    }
#endif
}
