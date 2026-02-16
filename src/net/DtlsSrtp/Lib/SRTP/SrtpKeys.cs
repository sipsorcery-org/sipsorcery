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

namespace SIPSorcery.Net.SharpSRTP.SRTP
{
    public class SrtpKeys
    {
        public SrtpProtectionProfileConfiguration ProtectionProfile { get; }
        public byte[] Mki { get; }

        public byte[] MasterKey { get { return MasterKeySalt.AsSpan(0, ProtectionProfile.CipherKeyLength >> 3).ToArray(); } }
        public byte[] MasterSalt { get { return MasterKeySalt.AsSpan(ProtectionProfile.CipherKeyLength >> 3).ToArray(); } }
        public byte[] MasterKeySalt { get; }

        public SrtpKeys(SrtpProtectionProfileConfiguration protectionProfile, byte[] mki = null)
        {
            this.ProtectionProfile = protectionProfile ?? throw new ArgumentNullException(nameof(protectionProfile));
            this.Mki = mki;

            int cipherKeyLen = protectionProfile.CipherKeyLength >> 3;
            int cipherSaltLen = protectionProfile.CipherSaltLength >> 3;
            this.MasterKeySalt = new byte[cipherKeyLen + cipherSaltLen];
        }
    }
}
