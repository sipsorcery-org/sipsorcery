//-----------------------------------------------------------------------------
// Filename: SecureRtpParameters.cs
//-----------------------------------------------------------------------------

using System;
using Org.BouncyCastle.Crypto.Tls;

namespace SIPSorcery.Net
{
    public readonly struct SecureRtpParameters
    {
        // DTLS derived key and salt lengths for SRTP
        // http://tools.ietf.org/html/rfc5764#section-4.1.2

        public static SecureRtpParameters SRTP_AES128_CM_HMAC_SHA1_80 { get; } = new SecureRtpParameters(
            SrtpProtectionProfile.SRTP_AES128_CM_HMAC_SHA1_80, SrtpPolicy.AESCM_ENCRYPTION, 16,
            SrtpPolicy.HMACSHA1_AUTHENTICATION, 20, 10, 10, 14);

        public static SecureRtpParameters SRTP_AES128_CM_HMAC_SHA1_32 { get; } = new SecureRtpParameters(
            SrtpProtectionProfile.SRTP_AES128_CM_HMAC_SHA1_32, SrtpPolicy.AESCM_ENCRYPTION, 16,
            SrtpPolicy.HMACSHA1_AUTHENTICATION, 20, 4, 10, 14);

        public static SecureRtpParameters SRTP_NULL_HMAC_SHA1_80 { get; } = new SecureRtpParameters(
            SrtpProtectionProfile.SRTP_NULL_HMAC_SHA1_80, SrtpPolicy.NULL_ENCRYPTION, 0,
            SrtpPolicy.HMACSHA1_AUTHENTICATION, 20, 10, 10, 0);

        public static SecureRtpParameters SRTP_NULL_HMAC_SHA1_32 { get; } = new SecureRtpParameters(
            SrtpProtectionProfile.SRTP_NULL_HMAC_SHA1_32, SrtpPolicy.NULL_ENCRYPTION, 0,
            SrtpPolicy.HMACSHA1_AUTHENTICATION, 20, 4, 10, 0);

        public int Profile { get; }
        public int EncryptionType { get; }
        public int EncryptionKeyLength { get; }
        public int AuthenticationType { get; }
        public int AuthenticationKeyLength { get; }
        public int AuthenticationTagLength { get; }
        public int RtcpAuthenticationTagLength { get; }
        public int SaltLength { get; }

        private SecureRtpParameters(int profile, int encryptionType, int encryptionKeyLength, int authenticationType,
            int authenticationKeyLength, int authenticationTagLength, int rtcpAuthenticationTagLength, int saltLength)
        {
            Profile = profile;
            EncryptionType = encryptionType;
            EncryptionKeyLength = encryptionKeyLength;
            AuthenticationType = authenticationType;
            AuthenticationKeyLength = authenticationKeyLength;
            AuthenticationTagLength = authenticationTagLength;
            RtcpAuthenticationTagLength = rtcpAuthenticationTagLength;
            SaltLength = saltLength;
        }

        public static SecureRtpParameters GetParametersForProfile(int profileValue) =>
            profileValue switch
            {
                SrtpProtectionProfile.SRTP_AES128_CM_HMAC_SHA1_80 => SRTP_AES128_CM_HMAC_SHA1_80,
                SrtpProtectionProfile.SRTP_AES128_CM_HMAC_SHA1_32 => SRTP_AES128_CM_HMAC_SHA1_32,
                SrtpProtectionProfile.SRTP_NULL_HMAC_SHA1_80 => SRTP_NULL_HMAC_SHA1_80,
                SrtpProtectionProfile.SRTP_NULL_HMAC_SHA1_32 => SRTP_NULL_HMAC_SHA1_32,
                _ => throw new ArgumentOutOfRangeException(nameof(profileValue), $"SRTP Protection Profile value {profileValue} is not allowed for DTLS SRTP. See http://tools.ietf.org/html/rfc5764#section-4.1.2 for valid values.")
            };

        public SrtpPolicy GetPolicy() =>
            new SrtpPolicy(EncryptionType, EncryptionKeyLength, AuthenticationType, AuthenticationKeyLength, AuthenticationTagLength, SaltLength);

        public SrtpPolicy GetRtcpPolicy() =>
            new SrtpPolicy(EncryptionType, EncryptionKeyLength, AuthenticationType, AuthenticationKeyLength, RtcpAuthenticationTagLength, SaltLength);
    }
}
