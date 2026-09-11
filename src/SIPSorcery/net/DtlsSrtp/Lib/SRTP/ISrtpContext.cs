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

#if NET8_0_OR_GREATER
using ReadOnlyBytes = System.ReadOnlySpan<byte>;
using Bytes = System.Span<byte>;
#else
using ReadOnlyBytes = System.ArraySegment<byte>;
using Bytes = byte[];
#endif

namespace SIPSorcery.Net.SharpSRTP.SRTP
{
    public interface ISrtpContext
    {
        int CalculateRequiredSrtpPayloadLength(int rtpLen);
        int ProtectRtp(ReadOnlyBytes input, Bytes output, out int outputBufferLength);
        int UnprotectRtp(ReadOnlyBytes input, Bytes output, out int outputBufferLength);
        int CalculateRequiredSrtcpPayloadLength(int rtpLen);
        int ProtectRtcp(ReadOnlyBytes input, Bytes output, out int outputBufferLength);
        int UnprotectRtcp(ReadOnlyBytes input, Bytes output, out int outputBufferLength);
    }

    public static class SrtpContextExtensions
    {
        public static int ProtectRtp(this ISrtpContext context, Bytes payload, int length, out int outputBufferLength)
            => context.ProtectRtp(payload.Slice(0, length), payload, out outputBufferLength);

        public static int UnprotectRtp(this ISrtpContext context, Bytes payload, int length, out int outputBufferLength)
            => context.UnprotectRtp(payload.Slice(0, length), payload, out outputBufferLength);

        public static int ProtectRtcp(this ISrtpContext context, Bytes payload, int length, out int outputBufferLength)
            => context.ProtectRtcp(payload.Slice(0, length), payload, out outputBufferLength);

        public static int UnprotectRtcp(this ISrtpContext context, Bytes payload, int length, out int outputBufferLength)
            => context.UnprotectRtcp(payload.Slice(0, length), payload, out outputBufferLength);
    }
}
