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

using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using System;

namespace SIPSorcery.Net.SharpSRTP.SRTP.Encryption
{
    public static class AEAD
    {
        public static void Encrypt(IAeadBlockCipher engine, byte[] payload, int offset, int length, byte[] iv, byte[] K_e, int N_tag, byte[] associatedData)
        {
            int payloadSize = length - offset;

            int expectedLength = engine.GetOutputSize(payloadSize);
            if (offset + expectedLength > payload.Length)
            {
                throw new ArgumentOutOfRangeException("Payload is too small!");
            }

            var parameters = new AeadParameters(new KeyParameter(K_e), N_tag << 3, iv, associatedData);
            engine.Init(true, parameters);

            int len = engine.ProcessBytes(payload, offset, payloadSize, payload, offset);
            
            // throws when the MAC fails to match
            engine.DoFinal(payload, offset + len);
        }

        public static byte[] GenerateMessageKeyIV(byte[] k_s, uint ssrc, ulong index)
        {
            byte[] iv = new byte[12];
            Buffer.BlockCopy(k_s, 0, iv, 0, 12);

            iv[2] ^= (byte)((ssrc >> 24) & 0xFF);
            iv[3] ^= (byte)((ssrc >> 16) & 0xFF);
            iv[4] ^= (byte)((ssrc >> 8) & 0xFF);
            iv[5] ^= (byte)(ssrc & 0xFF);
            iv[6] ^= (byte)((index >> 40) & 0xFF);
            iv[7] ^= (byte)((index >> 32) & 0xFF);
            iv[8] ^= (byte)((index >> 24) & 0xFF);
            iv[9] ^= (byte)((index >> 16) & 0xFF);
            iv[10] ^= (byte)((index >> 8) & 0xFF);
            iv[11] ^= (byte)(index & 0xFF);

            return iv;
        }
    }
}
