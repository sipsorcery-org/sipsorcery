using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using System;
using System.Runtime.CompilerServices;

public static class BouncyCastleExtensions
{
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
