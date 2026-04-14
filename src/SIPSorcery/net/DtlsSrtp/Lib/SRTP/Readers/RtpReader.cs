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

namespace SIPSorcery.Net.SharpSRTP.SRTP.Readers
{
    public static class RtpReader
    {
        public static uint ReadSsrc(ReadOnlySpan<byte> rtpPacket)
        {
            return System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(rtpPacket.Slice(8, 4));
        }

        public static ushort ReadSequenceNumber(ReadOnlySpan<byte> rtpPacket)
        {
            return System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(rtpPacket.Slice(2, 2));
        }

        public static int ReadHeaderLen(ReadOnlySpan<byte> payload)
        {
            return ReadHeaderLenWithoutExtensions(payload) + ReadExtensionsLength(payload);
        }

        public static int ReadExtensionsLength(ReadOnlySpan<byte> payload)
        {
            int length = ReadHeaderLenWithoutExtensions(payload);
            if ((payload[0] & 0x10) == 0x10)
            {
                var extensionLengthWords = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(length + 2, 2));
                return 4 + (extensionLengthWords * 4);
            }
            return 0;
        }

        public static int ReadHeaderLenWithoutExtensions(ReadOnlySpan<byte> payload)
        {
            return 12 + 4 * (payload[0] & 0xf);
        }

        public static ReadOnlySpan<byte> ReadHeaderExtensions(ReadOnlySpan<byte> payload)
        {
            int length = ReadHeaderLenWithoutExtensions(payload);
            int extLen = ReadExtensionsLength(payload);
            return payload.Slice(length, extLen);
        }
    }
}
