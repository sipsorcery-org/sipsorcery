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

using System.Linq;

namespace SIPSorcery.Net.SharpSRTP.SRTP.Readers
{
    public static class RtpReader
    {
        public static uint ReadSsrc(byte[] rtpPacket)
        {
            return (uint)((rtpPacket[8] << 24) | (rtpPacket[9] << 16) | (rtpPacket[10] << 8) | rtpPacket[11]);
        }

        public static ushort ReadSequenceNumber(byte[] rtpPacket)
        {
            return (ushort)((rtpPacket[2] << 8) | rtpPacket[3]);
        }

        public static int ReadHeaderLen(byte[] payload)
        {
            return ReadHeaderLenWithoutExtensions(payload) + ReadExtensionsLength(payload);
        }

        public static int ReadExtensionsLength(byte[] payload)
        {
            int length = ReadHeaderLenWithoutExtensions(payload);
            if ((payload[0] & 0x10) == 0x10)
            {
                return 4 + (payload[length + 2] << 8) | payload[length + 3];
            }
            else
            {
                return 0;
            }
        }

        public static int ReadHeaderLenWithoutExtensions(byte[] payload)
        {
            return 12 + 4 * (payload[0] & 0xf);
        }

        public static byte[] ReadHeaderExtensions(byte[] payload)
        {
            int length = ReadHeaderLenWithoutExtensions(payload);
            int extLen = ReadExtensionsLength(payload);
            return payload.Skip(length).Take(extLen).ToArray();
        }
    }
}
