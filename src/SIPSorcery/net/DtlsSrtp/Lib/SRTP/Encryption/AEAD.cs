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
using System.Buffers.Binary;
#if NET8_0_OR_GREATER
using ReadOnlyBytes = System.ReadOnlySpan<byte>;
using Bytes = System.Span<byte>;
#else
using ReadOnlyBytes = System.ArraySegment<byte>;
using Bytes = System.ArraySegment<byte>;
#endif

namespace SIPSorcery.Net.SharpSRTP.SRTP.Encryption
{
    public static class AEAD
    {
        public const int BLOCK_SIZE = 12;

        public static void Encrypt(IAeadBlockCipher engine, bool encrypt, ReadOnlyBytes input, Bytes output, byte[] iv, byte[] K_e, int N_tag, ReadOnlyBytes associatedData)
        {
            Encrypt(engine, encrypt, input, output, iv, new KeyParameter(K_e), N_tag, associatedData);
        }

        public static void Encrypt(IAeadBlockCipher engine, bool encrypt, ReadOnlyBytes input, Bytes output, byte[] iv, KeyParameter K_e, int N_tag, ReadOnlyBytes associatedData)
        {
            var parameters = new AeadParameters(K_e, N_tag << 3, iv);
            engine.Init(encrypt, parameters);

            engine.ProcessAadBytes(associatedData);

            int len = engine.ProcessBytes(input, output);

            // throws when the MAC fails to match
            engine.DoFinal(output.Slice(len));
        }

        public static void GenerateMessageKeyIV(ReadOnlySpan<byte> k_s, uint ssrc, ulong index, Span<byte> iv)
        {
            k_s.Slice(0, BLOCK_SIZE).CopyTo(iv);

            // XOR ssrc at offset 2 (3 bytes for 48-bit index)
            var ssrcSpan = iv.Slice(2, 4);
            BinaryPrimitives.WriteUInt32BigEndian(ssrcSpan,
                BinaryPrimitives.ReadUInt32BigEndian(ssrcSpan) ^ ssrc);

            // XOR index at offset 6 (6 bytes for 48-bit index)
            var indexSpan = iv.Slice(4, 8);
            BinaryPrimitives.WriteUInt64BigEndian(indexSpan,
                BinaryPrimitives.ReadUInt64BigEndian(indexSpan) ^ (index & 0x0000_FFFF_FFFF_FFFF));
        }
    }
}
