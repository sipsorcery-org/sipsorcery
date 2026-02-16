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
        public static void Encrypt(IAeadBlockCipher engine, bool encrypt, byte[] payload, int offset, int length, byte[] iv, byte[] K_e, int N_tag, byte[] associatedData)
        {
            int payloadSize = length - offset;

            int expectedLength = engine.GetOutputSize(payloadSize);
            if (offset + expectedLength > payload.Length)
            {
                throw new ArgumentOutOfRangeException("Payload is too small!");
            }

            var parameters = new AeadParameters(new KeyParameter(K_e), N_tag << 3, iv, associatedData);
            engine.Init(encrypt, parameters);

            int len = engine.ProcessBytes(payload, offset, payloadSize, payload, offset);

            // throws when the MAC fails to match
            engine.DoFinal(payload, offset + len);
        }

        public static byte[] GenerateMessageKeyIV(byte[] k_s, uint ssrc, ulong index)
        {
            var iv = GC.AllocateUninitializedArray<byte>(12);
            Buffer.BlockCopy(k_s, 0, iv, 0, 12);

            // XOR in SSRC (big-endian)
            var ssrcSpan = iv.AsSpan(2, 4);
            var ssrcVal = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(ssrcSpan);
            ssrcVal ^= ssrc;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ssrcSpan, ssrcVal);

            // XOR in index high 48bits using big-endian 32-bit and 16-bit segments
            var hiSpan = iv.AsSpan(6, 4);
            var hi = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(hiSpan);
            hi ^= (uint)(index >> 16);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(hiSpan, hi);

            var loSpan = iv.AsSpan(10, 2);
            var lo = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(loSpan);
            lo ^= (ushort)(index & 0xFFFF);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(loSpan, lo);

            return iv;
        }
    }
}
