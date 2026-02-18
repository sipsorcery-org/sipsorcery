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

namespace SIPSorcery.Net.SharpSRTP.SRTP
{
    public class SrtpSessionContext : ISrtpContext
    {
        public SrtpContext EncodeRtpContext { get; private set; }
        public SrtpContext EncodeRtcpContext { get; private set; }
        public SrtpContext DecodeRtpContext { get; private set; }
        public SrtpContext DecodeRtcpContext { get; private set; }

        public SrtpSessionContext(SrtpContext encodeRtpContext, SrtpContext decodeRtpContext, SrtpContext encodeRtcpContext, SrtpContext decodeRtcpContext)
        {
            this.EncodeRtpContext = encodeRtpContext;
            this.DecodeRtpContext = decodeRtpContext;
            this.EncodeRtcpContext = encodeRtcpContext;
            this.DecodeRtcpContext = decodeRtcpContext;
        }

        public int CalculateRequiredSrtpPayloadLength(int rtpLen)
        {
            return EncodeRtpContext.CalculateRequiredSrtpPayloadLength(rtpLen);
        }

        public int ProtectRtp(byte[] payload, int length, out int outputBufferLength)
        {
            return EncodeRtpContext.ProtectRtp(payload, length, out outputBufferLength);
        }

        public int UnprotectRtp(byte[] payload, int length, out int outputBufferLength)
        {
            return DecodeRtpContext.UnprotectRtp(payload, length, out outputBufferLength);
        }

        public int CalculateRequiredSrtcpPayloadLength(int rtpLen)
        {
            return EncodeRtcpContext.CalculateRequiredSrtcpPayloadLength(rtpLen);
        }

        public int ProtectRtcp(byte[] payload, int length, out int outputBufferLength)
        {
            return EncodeRtcpContext.ProtectRtcp(payload, length, out outputBufferLength);
        }

        public int UnprotectRtcp(byte[] payload, int length, out int outputBufferLength)
        {
            return DecodeRtcpContext.UnprotectRtcp(payload, length, out outputBufferLength);
        }
    }
}
