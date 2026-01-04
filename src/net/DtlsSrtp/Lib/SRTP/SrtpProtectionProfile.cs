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
    public enum SrtpCiphers
    {
        NULL = 0,
        AES_128_CM = 1,
        AES_128_F8 = 2,
        SEED_128_CTR = 3,
        SEED_128_CCM = 4,
        SEED_128_GCM = 5,
        AES_192_CM = 6,
        AES_256_CM = 7,
        AEAD_AES_128_GCM = 8,
        AEAD_AES_256_GCM = 9,
        ARIA_128_CTR = 10,
        ARIA_256_CTR = 11,
        AEAD_ARIA_128_GCM = 12,
        AEAD_ARIA_256_GCM = 13,

        // double ciphers are numbered from 128 upwards
        DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM = 128,
        DOUBLE_AEAD_AES_256_GCM_AEAD_AES_256_GCM = 129,
    }

    public enum SrtpAuth
    {
        NONE = 0,
        HMAC_SHA1 = 1
    }

    public class SrtpProtectionProfileConfiguration
    {
        public SrtpCiphers Cipher { get; set; }
        public int CipherKeyLength { get; set; }
        public int CipherSaltLength { get; set; }
        public int MaximumLifetime { get; set; }
        public SrtpAuth Auth { get; set; }
        public int AuthKeyLength { get; set; }
        public int AuthTagLength { get; set; }
        public int SrtpPrefixLength { get; set; }

        public SrtpProtectionProfileConfiguration(
            SrtpCiphers cipher,
            int cipherKeyLength, 
            int cipherSaltLength,
            int maximumLifetime,
            SrtpAuth auth, 
            int authKeyLength, 
            int authTagLength, 
            int srtpPrefixLength = 0)
        {
            Cipher = cipher;
            CipherKeyLength = cipherKeyLength;
            CipherSaltLength = cipherSaltLength;
            MaximumLifetime = maximumLifetime;
            Auth = auth;
            AuthKeyLength = authKeyLength;
            AuthTagLength = authTagLength;
            SrtpPrefixLength = srtpPrefixLength;
        }
    }
}
