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

namespace SIPSorcery.Net.SharpSRTP.SRTP.Encryption
{
    public static class F8
    {
        public const int BLOCK_SIZE = 16;

        public static byte[] GenerateRtpMessageKeyIV(IBlockCipher engine, byte[] k_e, byte[] k_s, byte[] rtpPacket, uint ROC)
        {
            byte[] iv = GenerateRtpIV(rtpPacket, ROC);            
            byte[] iv2 = GenerateIV2(engine, k_e, k_s, iv);
            return iv2;
        }

        private static byte[] GenerateRtpIV(byte[] rtpPacket, uint ROC)
        {
            byte[] iv = new byte[BLOCK_SIZE];
            iv[0] = 0;

            // M + PT + SEQ + TS + SSRC
            Buffer.BlockCopy(rtpPacket, 1, iv, 1, 11);
            
            // ROC
            iv[12] = (byte)((ROC >> 24) & 0xFF);
            iv[13] = (byte)((ROC >> 16) & 0xFF);
            iv[14] = (byte)((ROC >> 8) & 0xFF);
            iv[15] = (byte)(ROC & 0xFF);
            return iv;
        }

        public static byte[] GenerateRtcpMessageKeyIV(IBlockCipher engine, byte[] k_e, byte[] k_s, byte[] rtcpPacket, uint index)
        {
            byte[] iv = GenerateRtcpIV(rtcpPacket, index);
            byte[] iv2 = GenerateIV2(engine, k_e, k_s, iv);
            return iv2;
        }

        private static byte[] GenerateRtcpIV(byte[] rtcpPacket, uint index)
        {
            byte[] iv = new byte[BLOCK_SIZE];

            // 0..0
            iv[0] = 0;
            iv[1] = 0;
            iv[2] = 0;
            iv[3] = 0;

            // E + SRTCP index
            iv[4] = (byte)((index >> 24) & 0xFF);
            iv[5] = (byte)((index >> 16) & 0xFF);
            iv[6] = (byte)((index >> 8) & 0xFF);
            iv[7] = (byte)(index & 0xFF);

            // V + P + RC + PT + L + SSRC
            Buffer.BlockCopy(rtcpPacket, 0, iv, BLOCK_SIZE - 8, 8);
            return iv;
        }

        private static byte[] GenerateIV2(IBlockCipher engine, byte[] k_e, byte[] k_s, byte[] iv)
        {
            byte[] iv2 = new byte[BLOCK_SIZE];
            
            // IV' = E(k_e XOR m, IV)
            Buffer.BlockCopy(k_e, 0, iv2, 0, k_e.Length);

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

        public static void Encrypt(IBlockCipher aes, byte[] payload, int offset, int length, byte[] iv)
        {
            int payloadSize = length - offset;
            int blockCount = payloadSize / BLOCK_SIZE + payloadSize % BLOCK_SIZE;
            byte[] cipher = new byte[blockCount * BLOCK_SIZE];

            int blockNo = 0;
            byte[] iv2 = new byte[iv.Length];
            for (uint j = 0; j < blockCount; j++)
            {
                Buffer.BlockCopy(iv, 0, iv2, 0, iv.Length);

                // IV' xor j
                iv2[12] ^= (byte)((j >> 24) & 0xff);
                iv2[13] ^= (byte)((j >> 16) & 0xff);
                iv2[14] ^= (byte)((j >> 8) & 0xff);
                iv2[15] ^= (byte)(j & 0xff);

                // IV' xor S(-1) xor j
                if (blockNo > 0)
                {
                    int previousBlockIndex = BLOCK_SIZE * (blockNo - 1);
                    for (int i = 0; i < BLOCK_SIZE; i++)
                    {
                        iv2[i] = (byte)(iv2[i] ^ cipher[previousBlockIndex + i]);
                    }
                }

                aes.ProcessBlock(iv2, 0, cipher, BLOCK_SIZE * blockNo);
                blockNo++;
            }

            for (int i = 0; i < payloadSize; i++)
            {
                payload[offset + i] ^= cipher[i];
            }
        }
    }
}
