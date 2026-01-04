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

using Org.BouncyCastle.Tls;
using SIPSorcery.Net.SharpSRTP.DTLS;
using SIPSorcery.Net.SharpSRTP.SRTP;
using System;

namespace SIPSorcery.Net.SharpSRTP.DTLSSRTP
{
    public class DtlsSessionStartedEventArgs : EventArgs
    {
        public SrtpSessionContext Context { get; private set; }
        public Certificate PeerCertificate { get; private set;  }

        public DtlsSessionStartedEventArgs(SrtpSessionContext context, Certificate peerCertificate)
        {
            this.Context = context ?? throw new ArgumentNullException(nameof(context));
            this.PeerCertificate = peerCertificate ?? throw new ArgumentNullException(nameof(peerCertificate));
        }
    }

    public interface IDtlsSrtpPeer : IDtlsPeer
    {
        event EventHandler<DtlsSessionStartedEventArgs> OnSessionStarted;
        SrtpSessionContext CreateSessionContext(SecurityParameters securityParameters);
    }
}
