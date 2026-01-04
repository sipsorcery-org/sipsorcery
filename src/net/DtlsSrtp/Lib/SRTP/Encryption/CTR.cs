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

namespace SIPSorcery.Net.SRTP.Encryption
{
    public static class CTR
    {
        public const int BLOCK_SIZE = 16;

        public static byte[] GenerateSessionKeyIV(byte[] masterSalt, ulong index, ulong kdr, byte label)
        {
            byte[] iv = new byte[BLOCK_SIZE];

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
            Buffer.BlockCopy(masterSalt, 0, iv, 0, masterSalt.Length);

            iv[7] ^= (byte)((keyId >> 48) & 0xFF);
            iv[8] ^= (byte)((keyId >> 40) & 0xFF);
            iv[9] ^= (byte)((keyId >> 32) & 0xFF);
            iv[10] ^= (byte)((keyId >> 24) & 0xFF);
            iv[11] ^= (byte)((keyId >> 16) & 0xFF);
            iv[12] ^= (byte)((keyId >> 8) & 0xFF);
            iv[13] ^= (byte)(keyId & 0xFF);

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

        public static byte[] GenerateMessageKeyIV(byte[] salt, uint ssrc, ulong index)
        {
            // RFC 3711 - 4.1.1
            // IV = (k_s * 2 ^ 16) XOR(SSRC * 2 ^ 64) XOR(i * 2 ^ 16)
            byte[] iv = new byte[16];

            Buffer.BlockCopy(salt, 0, iv, 0, 14);

            iv[4] ^= (byte)((ssrc >> 24) & 0xFF);
            iv[5] ^= (byte)((ssrc >> 16) & 0xFF);
            iv[6] ^= (byte)((ssrc >> 8) & 0xFF);
            iv[7] ^= (byte)(ssrc & 0xFF);

            iv[8] ^= (byte)((index >> 40) & 0xFF);
            iv[9] ^= (byte)((index >> 32) & 0xFF);
            iv[10] ^= (byte)((index >> 24) & 0xFF);
            iv[11] ^= (byte)((index >> 16) & 0xFF);
            iv[12] ^= (byte)((index >> 8) & 0xFF);
            iv[13] ^= (byte)(index & 0xFF);

            iv[14] = 0;
            iv[15] = 0;

            return iv;
        }

        public static void Encrypt(IBlockCipher engine, byte[] payload, int offset, int length, byte[] iv)
        {
            int payloadSize = length - offset;
            byte[] cipher = new byte[payloadSize];

            int blockNo = 0;
            for (int i = 0; i < payloadSize / BLOCK_SIZE; i++)
            {
                iv[14] = (byte)((i >> 8) & 0xff);
                iv[15] = (byte)(i & 0xff);
                engine.ProcessBlock(iv, 0, cipher, BLOCK_SIZE * blockNo);
                blockNo++;
            }

            if (payloadSize % BLOCK_SIZE != 0)
            {
                iv[14] = (byte)((blockNo >> 8) & 0xff);
                iv[15] = (byte)(blockNo & 0xff);
                byte[] lastBlock = new byte[BLOCK_SIZE];
                engine.ProcessBlock(iv, 0, lastBlock, 0);
                Buffer.BlockCopy(lastBlock, 0, cipher, BLOCK_SIZE * blockNo, payloadSize % BLOCK_SIZE);
            }

            for (int i = 0; i < payloadSize; i++)
            {
                payload[offset + i] ^= cipher[i];
            }
        }
    }
}
