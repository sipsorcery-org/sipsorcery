// SharpSRTP
// Copyright (C) 2026 Lukas Volf
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
#if NET8_0_OR_GREATER
using ReadOnlyBytes = System.ReadOnlySpan<byte>;
using Bytes = System.Span<byte>;
#else
using ReadOnlyBytes = System.ArraySegment<byte>;
using Bytes = byte[];
#endif

namespace SIPSorcery.Net.SharpSRTP.SRTP
{
    /// <summary>
    /// Thread-safe SRTP session context wrapper. Use it when the same SRTP session context is accessed from multiple threads concurrently. 
    /// This class ensures that the underlying SRTP session context is accessed in a thread-safe manner by using locks for each of the 
    /// protect/unprotect operations.
    /// </summary>
    public class ThreadSafeSrtpSessionContext : ISrtpContext
    {
        private object _syncEncodeRtpContext = new object();
        private object _syncDecodeRtpContext = new object();
        private object _syncEncodeRtcpContext = new object();
        private object _syncDecodeRtcpContext = new object();

        private ISrtpContext _srtpSessionContext;

        public ThreadSafeSrtpSessionContext(ISrtpContext srtpSessionContext)
        {
            _srtpSessionContext = srtpSessionContext ?? throw new ArgumentNullException(nameof(srtpSessionContext));
        }

        public int CalculateRequiredSrtpPayloadLength(int rtpLen)
        {
            lock (_syncEncodeRtpContext)
            {
                return _srtpSessionContext.CalculateRequiredSrtpPayloadLength(rtpLen);
            }
        }

        public int ProtectRtp(ReadOnlyBytes input, Bytes output, out int outputBufferLength)
        {
            lock (_syncEncodeRtpContext)
            {
                return _srtpSessionContext.ProtectRtp(input, output, out outputBufferLength);
            }
        }

        public int UnprotectRtp(ReadOnlyBytes input, Bytes output, out int outputBufferLength)
        {
            lock (_syncDecodeRtpContext)
            {
                return _srtpSessionContext.UnprotectRtp(input, output, out outputBufferLength);
            }
        }

        public int CalculateRequiredSrtcpPayloadLength(int rtpLen)
        {
            lock (_syncEncodeRtcpContext)
            {
                return _srtpSessionContext.CalculateRequiredSrtcpPayloadLength(rtpLen);
            }
        }

        public int ProtectRtcp(ReadOnlyBytes input, Bytes output, out int outputBufferLength)
        {
            lock (_syncEncodeRtcpContext)
            {
                return _srtpSessionContext.ProtectRtcp(input, output, out outputBufferLength);
            }
        }

        public int UnprotectRtcp(ReadOnlyBytes input, Bytes output, out int outputBufferLength)
        {
            lock (_syncDecodeRtcpContext)
            {
                return _srtpSessionContext.UnprotectRtcp(input, output, out outputBufferLength);
            }
        }
    }
}
