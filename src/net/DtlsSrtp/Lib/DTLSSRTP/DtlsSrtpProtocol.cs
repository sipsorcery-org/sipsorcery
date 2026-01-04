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
using SIPSorcery.Net.SharpSRTP.SRTP;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SIPSorcery.Net.SharpSRTP.DTLSSRTP
{
    /// <summary>
    /// Currently registered DTLS-SRTP profiles: https://www.iana.org/assignments/srtp-protection/srtp-protection.xhtml#srtp-protection-1
    /// </summary>
    public abstract class ExtendedSrtpProtectionProfile : SrtpProtectionProfile
    {
        // TODO: Remove this once BouncyCastle adds the constants
        public const int DRAFT_SRTP_AES256_CM_SHA1_80 = 0x0003;
        public const int DRAFT_SRTP_AES256_CM_SHA1_32 = 0x0004;
        public const int DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM = 0x0009;
        public const int DOUBLE_AEAD_AES_256_GCM_AEAD_AES_256_GCM = 0x000A;
        public const int SRTP_ARIA_128_CTR_HMAC_SHA1_80 = 0x000B;
        public const int SRTP_ARIA_128_CTR_HMAC_SHA1_32 = 0x000C;
        public const int SRTP_ARIA_256_CTR_HMAC_SHA1_80 = 0x000D;
        public const int SRTP_ARIA_256_CTR_HMAC_SHA1_32 = 0x000E;
        public const int SRTP_AEAD_ARIA_128_GCM = 0x000F;
        public const int SRTP_AEAD_ARIA_256_GCM = 0x0010;
    }

    public static class DtlsSrtpProtocol
    {
        public static readonly Dictionary<int, SrtpProtectionProfileConfiguration> DtlsProtectionProfiles;

        static DtlsSrtpProtocol()
        {
            // see https://www.iana.org/assignments/srtp-protection/srtp-protection.xhtml#srtp-protection-1
            DtlsProtectionProfiles = new Dictionary<int, SrtpProtectionProfileConfiguration>()
            {
                // https://www.rfc-editor.org/rfc/rfc8723.txt
                { ExtendedSrtpProtectionProfile.DOUBLE_AEAD_AES_256_GCM_AEAD_AES_256_GCM, new SrtpProtectionProfileConfiguration(SrtpCiphers.DOUBLE_AEAD_AES_256_GCM_AEAD_AES_256_GCM, 512, 192, int.MaxValue, SrtpAuth.NONE, 0, 256) },
                { ExtendedSrtpProtectionProfile.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM, new SrtpProtectionProfileConfiguration(SrtpCiphers.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM, 256, 192, int.MaxValue, SrtpAuth.NONE, 0, 256) },
                                
                // https://datatracker.ietf.org/doc/html/rfc8269
                { ExtendedSrtpProtectionProfile.SRTP_AEAD_ARIA_256_GCM, new SrtpProtectionProfileConfiguration(SrtpCiphers.AEAD_ARIA_256_GCM, 256, 96, int.MaxValue, SrtpAuth.NONE, 0, 128) },
                { ExtendedSrtpProtectionProfile.SRTP_AEAD_ARIA_128_GCM, new SrtpProtectionProfileConfiguration(SrtpCiphers.AEAD_ARIA_128_GCM, 128, 96, int.MaxValue, SrtpAuth.NONE, 0, 128) },
                { ExtendedSrtpProtectionProfile.SRTP_ARIA_256_CTR_HMAC_SHA1_80, new SrtpProtectionProfileConfiguration(SrtpCiphers.ARIA_256_CTR, 256, 112, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 80) },
                { ExtendedSrtpProtectionProfile.SRTP_ARIA_256_CTR_HMAC_SHA1_32, new SrtpProtectionProfileConfiguration(SrtpCiphers.ARIA_256_CTR, 256, 112, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 32) },
                { ExtendedSrtpProtectionProfile.SRTP_ARIA_128_CTR_HMAC_SHA1_80, new SrtpProtectionProfileConfiguration(SrtpCiphers.ARIA_128_CTR, 128, 112, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 80) },
                { ExtendedSrtpProtectionProfile.SRTP_ARIA_128_CTR_HMAC_SHA1_32, new SrtpProtectionProfileConfiguration(SrtpCiphers.ARIA_128_CTR, 128, 112, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 32) },

                // https://datatracker.ietf.org/doc/html/rfc7714
                { ExtendedSrtpProtectionProfile.SRTP_AEAD_AES_256_GCM, new SrtpProtectionProfileConfiguration(SrtpCiphers.AEAD_AES_256_GCM, 256, 96, int.MaxValue, SrtpAuth.NONE, 0, 128) },
                { ExtendedSrtpProtectionProfile.SRTP_AEAD_AES_128_GCM, new SrtpProtectionProfileConfiguration(SrtpCiphers.AEAD_AES_128_GCM, 128, 96, int.MaxValue, SrtpAuth.NONE, 0, 128) },

                // AES256 CM is specified in RFC 6188, but not included in IANA DTLS-SRTP registry https://www.iana.org/assignments/srtp-protection/srtp-protection.xhtml#srtp-protection-1
                // https://www.rfc-editor.org/rfc/rfc6188
                // AES192 CM is not supported in DTLS-SRTP
                // AES256 CM was removed in Draft 4 of RFC 5764
                // https://author-tools.ietf.org/iddiff?url1=draft-ietf-avt-dtls-srtp-04&url2=draft-ietf-avt-dtls-srtp-03&difftype=--html
                { ExtendedSrtpProtectionProfile.DRAFT_SRTP_AES256_CM_SHA1_80, new SrtpProtectionProfileConfiguration(SrtpCiphers.AES_256_CM, 256, 112, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 80) },
                { ExtendedSrtpProtectionProfile.DRAFT_SRTP_AES256_CM_SHA1_32, new SrtpProtectionProfileConfiguration(SrtpCiphers.AES_256_CM, 256, 112, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 32) },

                // https://datatracker.ietf.org/doc/html/rfc5764#section-9
                { ExtendedSrtpProtectionProfile.SRTP_AES128_CM_HMAC_SHA1_80, new SrtpProtectionProfileConfiguration(SrtpCiphers.AES_128_CM, 128, 112, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 80) },
                { ExtendedSrtpProtectionProfile.SRTP_AES128_CM_HMAC_SHA1_32, new SrtpProtectionProfileConfiguration(SrtpCiphers.AES_128_CM, 128, 112, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 32) },
                { ExtendedSrtpProtectionProfile.SRTP_NULL_HMAC_SHA1_80, new SrtpProtectionProfileConfiguration(SrtpCiphers.NULL, 0, 0, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 80) },
                { ExtendedSrtpProtectionProfile.SRTP_NULL_HMAC_SHA1_32, new SrtpProtectionProfileConfiguration(SrtpCiphers.NULL, 0, 0, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 32) },
            };
        }

        public static DtlsSrtpKeys CreateMasterKeys(int protectionProfile, byte[] mki, SecurityParameters dtlsSecurityParameters, bool requireExtendedMasterSecret = true)
        {
            // verify that we have extended master secret before computing the keys
            if (!dtlsSecurityParameters.IsExtendedMasterSecret && requireExtendedMasterSecret)
            {
                throw new InvalidOperationException();
            }

            // SRTP key derivation as described here https://datatracker.ietf.org/doc/html/rfc5764
            var srtpSecurityParams = DtlsProtectionProfiles[protectionProfile];

            // 2 * (SRTPSecurityParams.master_key_len + SRTPSecurityParams.master_salt_len) bytes of data
            int sharedSecretLength = (2 * (srtpSecurityParams.CipherKeyLength + srtpSecurityParams.CipherSaltLength)) >> 3;

            // EXTRACTOR-dtls_srtp https://datatracker.ietf.org/doc/html/rfc5705

            // TODO: If context is provided, it computes:
            /*
            PRF(SecurityParameters.master_secret, label,
                SecurityParameters.client_random +
                SecurityParameters.server_random +
                context_value_length + context_value
                )[length]
            */

            // derive shared secret
            /*
            PRF(SecurityParameters.master_secret, label,
               SecurityParameters.client_random +
               SecurityParameters.server_random
               )[length]
             */
            byte[] sharedSecret = TlsUtilities.Prf(
                dtlsSecurityParameters,
                dtlsSecurityParameters.MasterSecret,
                ExporterLabel.dtls_srtp, // The exporter label for this usage is "EXTRACTOR-dtls_srtp"
                dtlsSecurityParameters.ClientRandom.Concat(dtlsSecurityParameters.ServerRandom).ToArray(),
                sharedSecretLength
                ).Extract();

            return CreateMasterKeys(protectionProfile, mki, sharedSecret);
        }

        public static DtlsSrtpKeys CreateMasterKeys(int protectionProfile, byte[] mki, byte[] sharedSecret)
        {
            var srtpSecurityParams = DtlsProtectionProfiles[protectionProfile];

            if(sharedSecret == null)
            {
                throw new ArgumentNullException(nameof(sharedSecret));
            }

            int sharedSecretLength = (2 * (srtpSecurityParams.CipherKeyLength + srtpSecurityParams.CipherSaltLength)) >> 3;
            if(sharedSecret.Length < sharedSecretLength)
            {
                throw new ArgumentException("Invalid shared secret length.", nameof(sharedSecret));
            }

            DtlsSrtpKeys keys = new DtlsSrtpKeys(srtpSecurityParams, mki);

            if (srtpSecurityParams.Cipher >= SrtpCiphers.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM)
            {
                // we have to maintain separation of the inner and outer keys according to RFC8723
                // <inner client key> <inner server key> <inner client salt> <inner server salt> | <outer client key> <outer server key> <outer client salt> <outer server salt>
                Buffer.BlockCopy(sharedSecret, 0, keys.ClientWriteMasterKey, 0, keys.ClientWriteMasterKey.Length / 2); // inner
                Buffer.BlockCopy(sharedSecret, sharedSecretLength / 2, keys.ClientWriteMasterKey, keys.ClientWriteMasterKey.Length / 2, keys.ClientWriteMasterKey.Length / 2); // outer
                Buffer.BlockCopy(sharedSecret, keys.ClientWriteMasterKey.Length / 2, keys.ServerWriteMasterKey, 0, keys.ServerWriteMasterKey.Length / 2); // inner
                Buffer.BlockCopy(sharedSecret, sharedSecretLength / 2 + keys.ClientWriteMasterKey.Length / 2, keys.ServerWriteMasterKey, keys.ServerWriteMasterKey.Length / 2, keys.ServerWriteMasterKey.Length / 2); // outer
                Buffer.BlockCopy(sharedSecret, keys.ClientWriteMasterKey.Length / 2 + keys.ServerWriteMasterKey.Length / 2, keys.ClientWriteMasterSalt, 0, keys.ClientWriteMasterSalt.Length / 2); // inner
                Buffer.BlockCopy(sharedSecret, sharedSecretLength / 2 + keys.ClientWriteMasterKey.Length / 2 + keys.ServerWriteMasterKey.Length / 2, keys.ClientWriteMasterSalt, keys.ClientWriteMasterSalt.Length / 2, keys.ClientWriteMasterSalt.Length / 2); // outer
                Buffer.BlockCopy(sharedSecret, keys.ClientWriteMasterKey.Length / 2 + keys.ServerWriteMasterKey.Length / 2 + keys.ClientWriteMasterSalt.Length / 2, keys.ServerWriteMasterSalt, 0, keys.ServerWriteMasterSalt.Length / 2); // inner
                Buffer.BlockCopy(sharedSecret, sharedSecretLength / 2 + keys.ClientWriteMasterKey.Length / 2 + keys.ServerWriteMasterKey.Length / 2 + keys.ClientWriteMasterSalt.Length / 2, keys.ServerWriteMasterSalt, keys.ServerWriteMasterSalt.Length / 2, keys.ServerWriteMasterSalt.Length / 2); // outer
            }
            else
            {
                // <client key> <server key> <client salt> <server salt>
                Buffer.BlockCopy(sharedSecret, 0, keys.ClientWriteMasterKey, 0, keys.ClientWriteMasterKey.Length);
                Buffer.BlockCopy(sharedSecret, keys.ClientWriteMasterKey.Length, keys.ServerWriteMasterKey, 0, keys.ServerWriteMasterKey.Length);
                Buffer.BlockCopy(sharedSecret, keys.ClientWriteMasterKey.Length + keys.ServerWriteMasterKey.Length, keys.ClientWriteMasterSalt, 0, keys.ClientWriteMasterSalt.Length);
                Buffer.BlockCopy(sharedSecret, keys.ClientWriteMasterKey.Length + keys.ServerWriteMasterKey.Length + keys.ClientWriteMasterSalt.Length, keys.ServerWriteMasterSalt, 0, keys.ServerWriteMasterSalt.Length);
            }

            return keys;
        }

        public static byte[] GenerateMki(int length)
        {
            return SrtpProtocol.GenerateMki(length);
        }

        public static SrtpSessionContext CreateSrtpServerSessionContext(DtlsSrtpKeys keys)
        {
            var encodeRtpContext = new SrtpContext(SrtpContextType.RTP, keys.ProtectionProfile, keys.ServerWriteMasterKey, keys.ServerWriteMasterSalt, keys.Mki);
            var encodeRtcpContext = new SrtpContext(SrtpContextType.RTCP, keys.ProtectionProfile, keys.ServerWriteMasterKey, keys.ServerWriteMasterSalt, keys.Mki);
            var decodeRtpContext = new SrtpContext(SrtpContextType.RTP, keys.ProtectionProfile, keys.ClientWriteMasterKey, keys.ClientWriteMasterSalt, keys.Mki);
            var decodeRtcpContext = new SrtpContext(SrtpContextType.RTCP, keys.ProtectionProfile, keys.ClientWriteMasterKey, keys.ClientWriteMasterSalt, keys.Mki);

            return new SrtpSessionContext(encodeRtpContext, decodeRtpContext, encodeRtcpContext, decodeRtcpContext);
        }

        public static SrtpSessionContext CreateSrtpClientSessionContext(DtlsSrtpKeys keys)
        {
            var encodeRtpContext = new SrtpContext(SrtpContextType.RTP, keys.ProtectionProfile, keys.ClientWriteMasterKey, keys.ClientWriteMasterSalt, keys.Mki);
            var encodeRtcpContext = new SrtpContext(SrtpContextType.RTCP, keys.ProtectionProfile, keys.ClientWriteMasterKey, keys.ClientWriteMasterSalt, keys.Mki);
            var decodeRtpContext = new SrtpContext(SrtpContextType.RTP, keys.ProtectionProfile, keys.ServerWriteMasterKey, keys.ServerWriteMasterSalt, keys.Mki);
            var decodeRtcpContext = new SrtpContext(SrtpContextType.RTCP, keys.ProtectionProfile, keys.ServerWriteMasterKey, keys.ServerWriteMasterSalt, keys.Mki);

            return new SrtpSessionContext(encodeRtpContext, decodeRtpContext, encodeRtcpContext, decodeRtcpContext);
        }
    }
}
