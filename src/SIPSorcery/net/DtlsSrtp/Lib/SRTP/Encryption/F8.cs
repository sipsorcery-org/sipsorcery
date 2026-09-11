// SharpSRTP
// Copyright (C) 2025 Lukas Volf
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE 
// SOFTWARE.

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using System;
using System.Buffers;
using System.Buffers.Binary;
#if NET8_0_OR_GREATER
using Bytes = System.Span<byte>;
using ReadOnlyBytes = System.ReadOnlySpan<byte>;
#else
using Bytes = byte[];
using ReadOnlyBytes = byte[];
#endif


namespace SIPSorcery.Net.SharpSRTP.SRTP.Encryption
{
    public static class F8
    {
        public const int BLOCK_SIZE = 16;

        public static void GenerateRtpMessageKeyIV(IBlockCipher engine, byte[] k_e, byte[] k_s, ReadOnlySpan<byte> rtpPacket, uint ROC, Bytes iv)
        {
#if NET8_0_OR_GREATER
            Span<byte> rtpIV = stackalloc byte[BLOCK_SIZE];
#else
            var rtpIV = new byte[BLOCK_SIZE];
#endif
            GenerateRtpIV(rtpIV, rtpPacket, ROC);

            GenerateIV2(engine, k_e, k_s, rtpIV, iv);
        }

        private static void GenerateRtpIV(Span<byte> iv, ReadOnlySpan<byte> rtpPacket, uint ROC)
        {
            iv[0] = 0;

            // M + PT + SEQ + TS + SSRC
            rtpPacket.Slice(1, 11).CopyTo(iv.Slice(1));

            // ROC (big-endian)
            BinaryPrimitives.WriteUInt32BigEndian(iv.Slice(12, 4), ROC);
        }

        public static byte[] GenerateRtcpMessageKeyIV(IBlockCipher engine, byte[] k_e, byte[] k_s, ReadOnlySpan<byte> rtcpPacket, uint index)
        {
#if NET8_0_OR_GREATER
            Span<byte> iv = stackalloc byte[BLOCK_SIZE];
#else
            var iv = new byte[BLOCK_SIZE];
#endif
            GenerateRtcpIV(iv, rtcpPacket, index);

            byte[] iv2 = new byte[BLOCK_SIZE];

            GenerateIV2(engine, k_e, k_s, iv, iv2);

            return iv2;
        }

        private static void GenerateRtcpIV(Span<byte> iv, ReadOnlySpan<byte> rtcpPacket, uint index)
        {
            // 0..0
            iv.Slice(0, 4).Clear();

            // E + SRTCP index
            BinaryPrimitives.WriteUInt32BigEndian(iv.Slice(4, 4), index);

            // V + P + RC + PT + L + SSRC
            rtcpPacket.Slice(0, 8).CopyTo(iv.Slice(BLOCK_SIZE - 8, 8));
        }

        private static void GenerateIV2(IBlockCipher engine, byte[] k_e, byte[] k_s, ReadOnlyBytes iv, Bytes iv2)
        {
            // IV' = E(k_e XOR m, IV)
            k_e.CopyTo(iv2);

            // m = k_s || 0x555..5
            Span<byte> k_s_temp = stackalloc byte[BLOCK_SIZE];
            k_s_temp.Fill(0x55);
            k_s.CopyTo(k_s_temp);

            BinaryExtensions.Xor128(iv2, k_s_temp);

            engine.Init(true, new KeyParameter(iv2));
            engine.ProcessBlock(iv, iv2);
        }

        public static void Encrypt(IBlockCipher aes, ReadOnlySpan<byte> input, Span<byte> output, ReadOnlySpan<byte> iv)
        {
            int payloadSize = input.Length;
            int blockCount = (payloadSize + BLOCK_SIZE - 1) / BLOCK_SIZE;
            byte[] cipher = ArrayPool<byte>.Shared.Rent(blockCount * BLOCK_SIZE);

            try
            {
                int blockNo = 0;
                byte[] iv2 = GC.AllocateUninitializedArray<byte>(iv.Length);
                for (uint j = 0; j < blockCount; j++)
                {
                    iv.CopyTo(iv2);

                    // IV' xor j
                    BinaryExtensions.Xor32(iv2.AsSpan(12, 4), j);

                    // IV' xor S(-1) xor j
                    if (blockNo > 0)
                    {
                        var previousBlockIndex = BLOCK_SIZE * (blockNo - 1);
                        BinaryExtensions.Xor128(iv2, cipher.AsSpan(previousBlockIndex + 0));
                    }

                    aes.ProcessBlock(iv2, 0, cipher, BLOCK_SIZE * blockNo);
                    blockNo++;
                }

                BinaryExtensions.Xor(
                    input,
                    cipher.AsSpan(0, payloadSize),
                    output.Slice(0, payloadSize));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(cipher);
            }
        }
    }
}
