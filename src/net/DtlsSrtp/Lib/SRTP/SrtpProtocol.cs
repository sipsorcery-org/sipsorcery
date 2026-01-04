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

using Org.BouncyCastle.Security;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SIPSorcery.Net.SRTP
{
    /// <summary>
    /// Currently registered SRTP Crypto Suites https://www.iana.org/assignments/sdp-security-descriptions/sdp-security-descriptions.xhtml
    /// </summary>
    public abstract class SrtpCryptoSuites
    {
        public const string AES_CM_128_HMAC_SHA1_80 = "AES_CM_128_HMAC_SHA1_80";
        public const string AES_CM_128_HMAC_SHA1_32 = "AES_CM_128_HMAC_SHA1_32";
        public const string F8_128_HMAC_SHA1_80 = "F8_128_HMAC_SHA1_80";
        public const string SEED_CTR_128_HMAC_SHA1_80 = "SEED_CTR_128_HMAC_SHA1_80";
        public const string SEED_128_CCM_80 = "SEED_128_CCM_80";
        public const string SEED_128_GCM_96 = "SEED_128_GCM_96";
        public const string AES_192_CM_HMAC_SHA1_80 = "AES_192_CM_HMAC_SHA1_80";
        public const string AES_192_CM_HMAC_SHA1_32 = "AES_192_CM_HMAC_SHA1_32";
        public const string AES_256_CM_HMAC_SHA1_80 = "AES_256_CM_HMAC_SHA1_80";
        public const string AES_256_CM_HMAC_SHA1_32 = "AES_256_CM_HMAC_SHA1_32";
        // duplicates because some specifications seem to use these incorrectly
        public const string AES_CM_192_HMAC_SHA1_80 = "AES_CM_192_HMAC_SHA1_80";
        public const string AES_CM_192_HMAC_SHA1_32 = "AES_CM_192_HMAC_SHA1_32";
        public const string AES_CM_256_HMAC_SHA1_80 = "AES_CM_256_HMAC_SHA1_80";
        public const string AES_CM_256_HMAC_SHA1_32 = "AES_CM_256_HMAC_SHA1_32";
        public const string AEAD_AES_128_GCM = "AEAD_AES_128_GCM";
        public const string AEAD_AES_256_GCM = "AEAD_AES_256_GCM";
    }

    public static class SrtpProtocol
    {
        private static SecureRandom _rand = new SecureRandom();

        public static readonly Dictionary<string, SrtpProtectionProfileConfiguration> SrtpCryptoSuites;

        static SrtpProtocol()
        {
            // see https://www.iana.org/assignments/sdp-security-descriptions/sdp-security-descriptions.xhtml
            SrtpCryptoSuites = new Dictionary<string, SrtpProtectionProfileConfiguration>()
            {
                { SRTP.SrtpCryptoSuites.AEAD_AES_256_GCM, new SrtpProtectionProfileConfiguration(SrtpCiphers.AEAD_AES_256_GCM, 256, 96, int.MaxValue, SrtpAuth.NONE, 0, 128) },
                { SRTP.SrtpCryptoSuites.AEAD_AES_128_GCM, new SrtpProtectionProfileConfiguration(SrtpCiphers.AEAD_AES_128_GCM, 128, 96, int.MaxValue, SrtpAuth.NONE, 0, 128) },

                // https://datatracker.ietf.org/doc/html/rfc6188
                { SRTP.SrtpCryptoSuites.AES_256_CM_HMAC_SHA1_80, new SrtpProtectionProfileConfiguration(SrtpCiphers.AES_256_CM, 256, 112, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 80) },
                { SRTP.SrtpCryptoSuites.AES_256_CM_HMAC_SHA1_32, new SrtpProtectionProfileConfiguration(SrtpCiphers.AES_256_CM, 256, 112, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 32) },
                { SRTP.SrtpCryptoSuites.AES_192_CM_HMAC_SHA1_80, new SrtpProtectionProfileConfiguration(SrtpCiphers.AES_192_CM, 192, 112, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 80) },
                { SRTP.SrtpCryptoSuites.AES_192_CM_HMAC_SHA1_32, new SrtpProtectionProfileConfiguration(SrtpCiphers.AES_192_CM, 192, 112, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 32) },

                { SRTP.SrtpCryptoSuites.AES_CM_128_HMAC_SHA1_80, new SrtpProtectionProfileConfiguration(SrtpCiphers.AES_128_CM, 128, 112, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 80) },
                { SRTP.SrtpCryptoSuites.AES_CM_128_HMAC_SHA1_32, new SrtpProtectionProfileConfiguration(SrtpCiphers.AES_128_CM, 128, 112, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 32) },

                { SRTP.SrtpCryptoSuites.F8_128_HMAC_SHA1_80, new SrtpProtectionProfileConfiguration(SrtpCiphers.AES_128_F8, 128, 112, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 80) },

                // https://datatracker.ietf.org/doc/html/rfc5669
                { SRTP.SrtpCryptoSuites.SEED_CTR_128_HMAC_SHA1_80, new SrtpProtectionProfileConfiguration(SrtpCiphers.SEED_128_CTR, 128, 112, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 80) },
                { SRTP.SrtpCryptoSuites.SEED_128_CCM_80, new SrtpProtectionProfileConfiguration(SrtpCiphers.SEED_128_CCM, 128, 96, int.MaxValue, SrtpAuth.NONE, 0, 80) },
                { SRTP.SrtpCryptoSuites.SEED_128_GCM_96, new SrtpProtectionProfileConfiguration(SrtpCiphers.SEED_128_GCM, 128, 96, int.MaxValue, SrtpAuth.NONE, 0, 96) },

                // misspelled
                { SRTP.SrtpCryptoSuites.AES_CM_256_HMAC_SHA1_80, new SrtpProtectionProfileConfiguration(SrtpCiphers.AES_256_CM, 256, 112, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 80) },
                { SRTP.SrtpCryptoSuites.AES_CM_256_HMAC_SHA1_32, new SrtpProtectionProfileConfiguration(SrtpCiphers.AES_256_CM, 256, 112, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 32) },
                { SRTP.SrtpCryptoSuites.AES_CM_192_HMAC_SHA1_80, new SrtpProtectionProfileConfiguration(SrtpCiphers.AES_192_CM, 192, 112, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 80) },
                { SRTP.SrtpCryptoSuites.AES_CM_192_HMAC_SHA1_32, new SrtpProtectionProfileConfiguration(SrtpCiphers.AES_192_CM, 192, 112, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 32) },
            };
        }

        public static SrtpKeys CreateMasterKeys(string cryptoSuite, byte[] mki = null, byte[] useMasterKeySalt = null)
        {
            var srtpSecurityParams = SrtpCryptoSuites[cryptoSuite];
            int masterKeyLen = srtpSecurityParams.CipherKeyLength >> 3;
            int masterSaltLen = srtpSecurityParams.CipherSaltLength >> 3;

            byte[] masterKeySalt;
            if (useMasterKeySalt == null)
            {
                // derive the master key + master salt to be sent in SDP crypto: attribute as per RFC 4568
                masterKeySalt = new byte[masterKeyLen + masterSaltLen];
                _rand.NextBytes(masterKeySalt);
            }
            else
            {
                masterKeySalt = useMasterKeySalt;
            }

            SrtpKeys keys = new SrtpKeys(srtpSecurityParams, mki);
            Buffer.BlockCopy(masterKeySalt, 0, keys.MasterKeySalt, 0, masterKeySalt.Length);

            return keys;
        }

        public static byte[] GenerateMki(int length)
        {
            if(length > 255)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }    

            byte[] MKI = new byte[length];
            if (length > 0)
            {
                // ensure positive value of the generated BigInteger
                int mkiValue = _rand.Next(0, int.MaxValue);
                BigInteger bi = new BigInteger(mkiValue);
                byte[] mkiValueBytes = bi.ToByteArray();
                Buffer.BlockCopy(mkiValueBytes, 0, MKI, 0, Math.Min(mkiValueBytes.Length, MKI.Length));
            }
            return MKI;
        }

        public static SrtpSessionContext CreateSrtpSessionContext(SrtpKeys keys)
        {
            var encodeRtpContext = new SrtpContext(SrtpContextType.RTP, keys.ProtectionProfile, keys.MasterKey, keys.MasterSalt, keys.Mki);
            var encodeRtcpContext = new SrtpContext(SrtpContextType.RTCP, keys.ProtectionProfile, keys.MasterKey, keys.MasterSalt, keys.Mki);
            var decodeRtpContext = new SrtpContext(SrtpContextType.RTP, keys.ProtectionProfile, keys.MasterKey, keys.MasterSalt, keys.Mki);
            var decodeRtcpContext = new SrtpContext(SrtpContextType.RTCP, keys.ProtectionProfile, keys.MasterKey, keys.MasterSalt, keys.Mki);

            return new SrtpSessionContext(encodeRtpContext, decodeRtpContext, encodeRtcpContext, decodeRtcpContext);
        }
    }
}
