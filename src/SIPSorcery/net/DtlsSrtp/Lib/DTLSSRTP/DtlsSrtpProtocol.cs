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

namespace SIPSorcery.Net.SharpSRTP.DTLSSRTP;

/// <summary>
/// Currently registered DTLS-SRTP profiles:
/// https://www.iana.org/assignments/srtp-protection/srtp-protection.xhtml#srtp-protection-1
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

            // for NULL we still need the keys (K_a) for auth, so we use the same key lengths as AES128 CM in order to derive non-zero master keys
            { ExtendedSrtpProtectionProfile.SRTP_NULL_HMAC_SHA1_80, new SrtpProtectionProfileConfiguration(SrtpCiphers.NULL, 128, 112, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 80) },
            { ExtendedSrtpProtectionProfile.SRTP_NULL_HMAC_SHA1_32, new SrtpProtectionProfileConfiguration(SrtpCiphers.NULL, 128, 112, int.MaxValue, SrtpAuth.HMAC_SHA1, 160, 32) },
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
        byte[] prfSeed = GC.AllocateUninitializedArray<byte>(dtlsSecurityParameters.ClientRandom.Length + dtlsSecurityParameters.ServerRandom.Length);
        Buffer.BlockCopy(dtlsSecurityParameters.ClientRandom, 0, prfSeed, 0, dtlsSecurityParameters.ClientRandom.Length);
        Buffer.BlockCopy(dtlsSecurityParameters.ServerRandom, 0, prfSeed, dtlsSecurityParameters.ClientRandom.Length, dtlsSecurityParameters.ServerRandom.Length);
        byte[] sharedSecret = TlsUtilities.Prf(
        dtlsSecurityParameters,
        dtlsSecurityParameters.MasterSecret,
        ExporterLabel.dtls_srtp, // The exporter label for this usage is "EXTRACTOR-dtls_srtp"
            prfSeed,
        sharedSecretLength
        ).Extract();

        return CreateMasterKeys(protectionProfile, mki, sharedSecret);
    }

    public static DtlsSrtpKeys CreateMasterKeys(int protectionProfile, byte[] mki, byte[] sharedSecret)
    {
        var srtpSecurityParams = DtlsProtectionProfiles[protectionProfile];

        if (sharedSecret == null)
        {
            throw new ArgumentNullException(nameof(sharedSecret));
        }

        int sharedSecretLength = (2 * (srtpSecurityParams.CipherKeyLength + srtpSecurityParams.CipherSaltLength)) >> 3;
        if (sharedSecret.Length < sharedSecretLength)
        {
            throw new ArgumentException("Invalid shared secret length.", nameof(sharedSecret));
        }

        var cipherKeyLen = srtpSecurityParams.CipherKeyLength >> 3;
        var cipherSaltLen = srtpSecurityParams.CipherSaltLength >> 3;


        ReadOnlyMemory<byte> clientWriteMasterKey, clientWriteMasterSalt, serverWriteMasterKey, serverWriteMasterSalt;

        if (srtpSecurityParams.Cipher >= SrtpCiphers.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM)
        {
            // we have to maintain separation of the inner and outer keys according to RFC8723
            // <inner client key> <inner server key> <inner client salt> <inner server salt> | <outer client key> <outer server key> <outer client salt> <outer server salt>
            int halfKeyLen = cipherKeyLen / 2;
            int halfSaltLen = cipherSaltLen / 2;
            int halfSecret = sharedSecretLength / 2;

            // ClientWriteMasterKey: inner + outer
            var clientKey = new byte[cipherKeyLen];
            Buffer.BlockCopy(sharedSecret, 0, clientKey, 0, halfKeyLen);
            Buffer.BlockCopy(sharedSecret, halfSecret, clientKey, halfKeyLen, halfKeyLen);
            clientWriteMasterKey = clientKey.AsMemory();

            // ServerWriteMasterKey: inner + outer
            var serverKey = new byte[cipherKeyLen];
            Buffer.BlockCopy(sharedSecret, halfKeyLen, serverKey, 0, halfKeyLen);
            Buffer.BlockCopy(sharedSecret, halfSecret + halfKeyLen, serverKey, halfKeyLen, halfKeyLen);
            serverWriteMasterKey = serverKey.AsMemory();

            // ClientWriteMasterSalt: inner + outer
            var clientSalt = new byte[cipherSaltLen];
            Buffer.BlockCopy(sharedSecret, 2 * halfKeyLen, clientSalt, 0, halfSaltLen);
            Buffer.BlockCopy(sharedSecret, halfSecret + 2 * halfKeyLen, clientSalt, halfSaltLen, halfSaltLen);
            clientWriteMasterSalt = clientSalt.AsMemory();

            // ServerWriteMasterSalt: inner + outer
            var serverSalt = new byte[cipherSaltLen];
            Buffer.BlockCopy(sharedSecret, 2 * halfKeyLen + halfSaltLen, serverSalt, 0, halfSaltLen);
            Buffer.BlockCopy(sharedSecret, halfSecret + 2 * halfKeyLen + halfSaltLen, serverSalt, halfSaltLen, halfSaltLen);
            serverWriteMasterSalt = serverSalt.AsMemory();
        }
        else
        {
            // <client key> <server key> <client salt> <server salt>
            int offset = 0;
            clientWriteMasterKey = sharedSecret.AsMemory(offset, cipherKeyLen);
            offset += cipherKeyLen;
            serverWriteMasterKey = sharedSecret.AsMemory(offset, cipherKeyLen);
            offset += cipherKeyLen;
            clientWriteMasterSalt = sharedSecret.AsMemory(offset, cipherSaltLen);
            offset += cipherSaltLen;
            serverWriteMasterSalt = sharedSecret.AsMemory(offset, cipherSaltLen);
        }

        var k = new DtlsSrtpKeys(
            srtpSecurityParams,
            clientWriteMasterKey,
            clientWriteMasterSalt,
            serverWriteMasterKey,
            serverWriteMasterSalt,
            mki == null ? default : mki.AsMemory());
        return k;
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
