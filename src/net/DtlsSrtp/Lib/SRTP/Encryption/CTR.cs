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

using System;
using Org.BouncyCastle.Crypto;

namespace SIPSorcery.Net.SharpSRTP.SRTP.Encryption
{
    public static class CTR
    {
        public const int BLOCK_SIZE = 16;

        public static byte[] GenerateSessionKeyIV(ReadOnlySpan<byte> masterSalt, ulong index, ulong kdr, byte label)
        {
            var iv = GC.AllocateUninitializedArray<byte>(BLOCK_SIZE);

            // RFC 3711 - 4.3.1
            // Key derivation SHALL be defined as follows in terms of<label>, an
            // 8 - bit constant(see below), master_salt and key_derivation_rate, as
            // determined in the cryptographic context, and index, the packet index
            // (i.e., the 48 - bit ROC || SEQ for SRTP):

            // *Let r = index DIV key_derivation_rate(with DIV as defined above).
            ulong r = DIV(index, kdr);

            // *Let key_id = < label > || r.
            ulong keyId = ((ulong)label << 48) | r;

            // *Let x = key_id XOR master_salt, where key_id and master_salt are
            //  aligned so that their least significant bits agree(right-
            //  alignment).
            masterSalt.CopyTo(iv.AsSpan());

            // XOR keyId (56-bit) into iv using big-endian segments
            var hiSpan = iv.AsSpan(7, 4);
            var hi = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(hiSpan);
            hi ^= (uint)(keyId >> 24);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(hiSpan, hi);

            var midSpan = iv.AsSpan(11, 2);
            var mid = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(midSpan);
            mid ^= (ushort)((keyId >> 8) & 0xFFFF);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(midSpan, mid);

            iv[13] ^= (byte)keyId;

            iv[14] = 0;
            iv[15] = 0;

            return iv;
        }

        private static ulong DIV(ulong x, ulong y)
        {
            if (y == 0)
            {
                return 0;
            }
            else
            {
                return x / y;
            }
        }

        public static byte[] GenerateMessageKeyIV(ReadOnlySpan<byte> salt, uint ssrc, ulong index)
        {
            // RFC 3711 - 4.1.1
            // IV = (k_s * 2 ^ 16) XOR(SSRC * 2 ^ 64) XOR(i * 2 ^ 16)
            byte[] iv = GC.AllocateUninitializedArray<byte>(16);
            salt.Slice(0, 14).CopyTo(iv);

            // XOR SSRC big-endian
            var ssrcSpan = iv.AsSpan(4, 4);
            var ssrcVal = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(ssrcSpan);
            ssrcVal ^= ssrc;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ssrcSpan, ssrcVal);

            // XOR index big-endian (48-bit)
            var hiSpan2 = iv.AsSpan(8, 4);
            var hi2 = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(hiSpan2);
            hi2 ^= (uint)(index >> 16);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(hiSpan2, hi2);

            var loSpan2 = iv.AsSpan(12, 2);
            var lo2 = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(loSpan2);
            lo2 ^= (ushort)(index & 0xFFFF);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(loSpan2, lo2);

            iv[14] = 0;
            iv[15] = 0;

            return iv;
        }

        public static void Encrypt(IBlockCipher engine, Span<byte> payload, int offset, int length, Span<byte> iv)
        {
            int payloadSize = length - offset;
            byte[] cipher = GC.AllocateUninitializedArray<byte>(payloadSize);

            var ivWork = GC.AllocateUninitializedArray<byte>(iv.Length);
            iv.CopyTo(ivWork);

            int blockNo = 0;
            int fullBlocks = payloadSize / BLOCK_SIZE;
            for (int i = 0; i < fullBlocks; i++)
            {
                ivWork[14] = (byte)((i >> 8) & 0xff);
                ivWork[15] = (byte)(i & 0xff);
                engine.ProcessBlock(ivWork, 0, cipher, BLOCK_SIZE * blockNo);
                blockNo++;
            }

            int remaining = payloadSize % BLOCK_SIZE;
            if (remaining != 0)
            {
                ivWork[14] = (byte)((blockNo >> 8) & 0xff);
                ivWork[15] = (byte)(blockNo & 0xff);
                byte[] lastBlock = GC.AllocateUninitializedArray<byte>(BLOCK_SIZE);
                engine.ProcessBlock(ivWork, 0, lastBlock, 0);
                lastBlock.AsSpan(0, remaining).CopyTo(cipher.AsSpan(BLOCK_SIZE * blockNo));
            }

            for (int i = 0; i < payloadSize; i++)
            {
                payload[offset + i] ^= cipher[i];
            }
        }
    }
}
