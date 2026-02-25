using System;
using System.Buffers;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
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
                datagramSender.Send(segment.Array, segment.Offset, segment.Count);
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

    extension(KeyParameter)
    {
#if NET8_0_OR_GREATER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static KeyParameter Create(ReadOnlyMemory<byte> key)
        {
            return new KeyParameter(key.Span);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static KeyParameter Create(ReadOnlySpan<byte> key)
        {
            return new KeyParameter(key);
        }
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static KeyParameter Create(ReadOnlyMemory<byte> memory)
        {
            if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment))
            {
                return KeyParameter.Create(segment);
            }
            // Fallback for non-array-backed memory
            return new KeyParameter(memory.ToArray());
        }
#endif
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static KeyParameter Create(ArraySegment<byte> key)
        {
            return new KeyParameter(key.Array, key.Offset, key.Count);
        }
    }

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

#if !NET8_0_OR_GREATER
    extension(IBlockCipher engine)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ProcessBlock(ArraySegment<byte> input, ArraySegment<byte> output)
        {
            return engine.ProcessBlock(input.Array, input.Offset, output.Array, output.Offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ProcessBlock(byte[] input, byte[] output)
        {
            return engine.ProcessBlock(input, 0, output, 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ProcessBlock(byte[] input, ArraySegment<byte> output)
        {
            return engine.ProcessBlock(input, 0, output.Array, output.Offset);
        }
    }

    extension(IAeadBlockCipher engine)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ProcessBytes(ArraySegment<byte> input, ArraySegment<byte> output)
        {
            return engine.ProcessBytes(input.Array, input.Offset, input.Count, output.Array, output.Offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ProcessAadBytes(ArraySegment<byte> input)
        {
            engine.ProcessAadBytes(input.Array, input.Offset, input.Count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int DoFinal(ArraySegment<byte> output)
        {
            return engine.DoFinal(output.Array, output.Offset);
        }
    }
#endif
}
