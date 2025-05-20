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
using System;
using System.Buffers.Binary;

namespace SIPSorcery.Net.SharpSRTP.SRTP.Encryption
{
    public static class F8
    {
        public const int BLOCK_SIZE = 16;

        public static byte[] GenerateRtpMessageKeyIV(IBlockCipher engine, ReadOnlySpan<byte> k_e, ReadOnlySpan<byte> k_s, ReadOnlySpan<byte> rtpPacket, uint ROC)
        {
            var iv = GenerateRtpIV(rtpPacket, ROC);
            var iv2 = GenerateIV2(engine, k_e, k_s, iv);
            return iv2;
        }

        private static byte[] GenerateRtpIV(ReadOnlySpan<byte> rtpPacket, uint ROC)
        {
            var iv = GC.AllocateUninitializedArray<byte>(BLOCK_SIZE);
            iv[0] = 0;

            // M + PT + SEQ + TS + SSRC
            rtpPacket.Slice(1, 11).CopyTo(iv.AsSpan(1));
            
            // ROC (big-endian)
            BinaryPrimitives.WriteUInt32BigEndian(iv.AsSpan(12, 4), ROC);

            return iv;
        }

        public static byte[] GenerateRtcpMessageKeyIV(IBlockCipher engine, ReadOnlySpan<byte> k_e, ReadOnlySpan<byte> k_s, ReadOnlySpan<byte> rtcpPacket, uint index)
        {
            var iv = GenerateRtcpIV(rtcpPacket, index);
            var iv2 = GenerateIV2(engine, k_e, k_s, iv);
            return iv2;
        }

        private static byte[] GenerateRtcpIV(ReadOnlySpan<byte> rtcpPacket, uint index)
        {
            var iv = GC.AllocateUninitializedArray<byte>(BLOCK_SIZE);

            // 0..0
            iv.AsSpan(0, 4).Clear();

            // E + SRTCP index (big-endian)
            BinaryPrimitives.WriteUInt32BigEndian(iv.AsSpan(4, 4), index);

            // V + P + RC + PT + L + SSRC
            rtcpPacket.Slice(0, 8).CopyTo(iv.AsSpan(BLOCK_SIZE - 8, 8));
            return iv;
        }

        private static byte[] GenerateIV2(IBlockCipher engine, ReadOnlySpan<byte> k_e, ReadOnlySpan<byte> k_s, byte[] iv)
        {
            var iv2 = GC.AllocateUninitializedArray<byte>(BLOCK_SIZE);
            
            // IV' = E(k_e XOR m, IV)
            k_e.CopyTo(iv2.AsSpan());

            // m = k_s || 0x555..5
            for (int i = 0; i < BLOCK_SIZE; i++)
            {
                if (i < k_s.Length)
                {
                    iv2[i] ^= k_s[i];
                }
                else
                {
                    iv2[i] ^= 0x55;
                }
            }

            engine.Init(true, new Org.BouncyCastle.Crypto.Parameters.KeyParameter(iv2));
            engine.ProcessBlock(iv, 0, iv2, 0);

            return iv2;
        }

        public static void Encrypt(IBlockCipher aes, Span<byte> payload, int offset, int length, ReadOnlySpan<byte> iv)
        {
            var payloadSize = length - offset;
            var blockCount = payloadSize / BLOCK_SIZE + payloadSize % BLOCK_SIZE;
            var cipher = GC.AllocateUninitializedArray<byte>(blockCount * BLOCK_SIZE);

            var blockNo = 0;
            var iv2 = GC.AllocateUninitializedArray<byte>(iv.Length);
            for (var j = 0U; j < blockCount; j++)
            {
                iv.CopyTo(iv2.AsSpan());

                // IV' xor j (big-endian)
                {
                    var span = iv2.AsSpan(12, 4);
                    var value = BinaryPrimitives.ReadUInt32BigEndian(span);
                    value ^= j;
                    BinaryPrimitives.WriteUInt32BigEndian(span, value);
                }

                // IV' xor S(-1) xor j
                if (blockNo > 0)
                {
                    var previousBlockIndex = BLOCK_SIZE * (blockNo - 1);
                    for (var i = 0; i < BLOCK_SIZE; i++)
                    {
                        iv2[i] = (byte)(iv2[i] ^ cipher[previousBlockIndex + i]);
                    }
                }

                aes.ProcessBlock(iv2, 0, cipher, BLOCK_SIZE * blockNo);
                blockNo++;
            }

            for (var i = 0; i < payloadSize; i++)
            {
                payload[offset + i] ^= cipher[i];
            }
        }
    }
}
