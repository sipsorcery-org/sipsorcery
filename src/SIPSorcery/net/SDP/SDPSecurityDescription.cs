//-----------------------------------------------------------------------------
// Filename: SDP.cs
//
// Description: (SDP) Security Descriptions for Media Streams implementation as basically defined in RFC 4568.
// https://tools.ietf.org/html/rfc4568
//
// Author(s):
// rj2

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Polyfills;

namespace SIPSorcery.Net
{
    /// <summary>
    /// (SDP) Security Descriptions for Media Streams implementation as basically defined in RFC 4568.
    /// <code>
    /// Example 1: Parse crypto attribute
    /// 
    /// string crypto = "a=crypto:1 AES_256_CM_HMAC_SHA1_80 inline:GTuZoqOsesiK4wfyL7Rsq6uHHwhqVGA+aVuAUnsmWktYacZyJu6/6tUQeUti0Q==";
    /// SDPSecurityDescription localcrypto = SDPSecurityDescription.Parse(crypto);
    /// 
    /// </code>
    /// <code>
    /// Example 2: Parse crypto attribute
    /// 
    /// SDPMediaAnnouncement mediaAudio = new SDPMediaAnnouncement();
    /// //[...]set some SDPMediaAnnouncement properties
    /// SDPSecurityDescription localcrypto = SDPSecurityDescription.CreateNew();
    /// localcrypto.KeyParams.Clear();
    /// localcrypto.KeyParams.Add(SDPSecurityDescription.KeyParameter.CreateNew(SDPSecurityDescription.CryptoSuites.AES_CM_128_HMAC_SHA1_32));
    /// mediaAudio.SecurityDescriptions.Add(localcrypto);
    /// mediaAudio.ToString();
    /// 
    /// string crypto = "a=crypto:1 AES_256_CM_HMAC_SHA1_80 inline:GTuZoqOsesiK4wfyL7Rsq6uHHwhqVGA+aVuAUnsmWktYacZyJu6/6tUQeUti0Q==";
    /// SDPSecurityDescription desc = SDPSecurityDescription.Parse(crypto);
    /// 
    /// </code>
    /// </summary>
    public class SDPSecurityDescription
    {
        public const string CRYPTO_ATTRIBUTE_NAME = "crypto";
        public const string CRYPTO_ATTRIBUE_PREFIX = $"a={CRYPTO_ATTRIBUTE_NAME}:";
        private static readonly char[] WHITE_SPACES = new char[] { ' ', '\t' };
        private const char SEMI_COLON = ';';
        private const string COLON = ":";
        private const string WHITE_SPACE = " ";
        public enum CryptoSuites
        {
            unknown,
            AES_CM_128_HMAC_SHA1_80, //https://tools.ietf.org/html/rfc4568
            AES_CM_128_HMAC_SHA1_32, //https://tools.ietf.org/html/rfc4568
            F8_128_HMAC_SHA1_80, //https://tools.ietf.org/html/rfc4568
            AEAD_AES_128_GCM, //https://tools.ietf.org/html/rfc7714
            AEAD_AES_256_GCM, //https://tools.ietf.org/html/rfc7714
            AES_192_CM_HMAC_SHA1_80, //https://tools.ietf.org/html/rfc6188
            AES_192_CM_HMAC_SHA1_32, //https://tools.ietf.org/html/rfc6188
            AES_256_CM_HMAC_SHA1_80, //https://tools.ietf.org/html/rfc6188
            AES_256_CM_HMAC_SHA1_32, //https://tools.ietf.org/html/rfc6188
                                     //duplicates, for wrong spelling in Ozeki-voip-sdk and who knows where else
            AES_CM_192_HMAC_SHA1_80, //https://tools.ietf.org/html/rfc6188
            AES_CM_192_HMAC_SHA1_32, //https://tools.ietf.org/html/rfc6188
            AES_CM_256_HMAC_SHA1_80, //https://tools.ietf.org/html/rfc6188
            AES_CM_256_HMAC_SHA1_32 //https://tools.ietf.org/html/rfc6188
        }

        private static readonly string[] s_cryptoSuiteNames = Enum.GetNames(typeof(CryptoSuites));

        public class KeyParameter
        {
            private const string COLON = ":";
            private const string PIPE = "|";
            public const string KEY_METHOD = "inline";
            private byte[] m_key = null;
            //128 bit for AES_CM_128_HMAC_SHA1_80, AES_CM_128_HMAC_SHA1_32, F8_128_HMAC_SHA1_80, AEAD_AES_128_GCM
            //192 bit for AES_192_CM_HMAC_SHA1_80, AES_192_CM_HMAC_SHA1_32
            //256 bit for AEAD_AES_256_GCM, AES_256_CM_HMAC_SHA1_80, AES_256_CM_HMAC_SHA1_32 
            //
            public byte[] Key
            {
                get
                {
                    return this.m_key;
                }
                set
                {
                    if (!IsValidKey(value))
                    {
                        throw value == null
                            ? new ArgumentNullException("Key", "Key must have a value")
                            : new ArgumentOutOfRangeException("Key", "Key must be at least 16 characters long");
                    }

                    this.m_key = value;
                }
            }
            private byte[] m_salt = null;
            //112 bit for AES_CM_128_HMAC_SHA1_80, AES_CM_128_HMAC_SHA1_32, F8_128_HMAC_SHA1_80
            //112 bit for AES_192_CM_HMAC_SHA1_80,AES_192_CM_HMAC_SHA1_32 , AES_256_CM_HMAC_SHA1_80, AES_256_CM_HMAC_SHA1_32 
            //96 bit for AEAD_AES_128_GCM
            //
            public byte[] Salt
            {
                get
                {
                    return this.m_salt;
                }
                set
                {
                    if (!IsValidSalt(value))
                    {
                        throw value == null
                            ? new ArgumentNullException("Salt", "Salt must have a value")
                            : new ArgumentOutOfRangeException("Salt", "Salt must be at least 12 characters long");
                    }

                    this.m_salt = value;
                }
            }
            public string KeySaltBase64
            {
                get
                {
                    byte[] b = new byte[this.Key.Length + this.Salt.Length];
                    Array.Copy(this.Key, 0, b, 0, this.Key.Length);
                    Array.Copy(this.Salt, 0, b, this.Key.Length, this.Salt.Length);
                    string s64 = Convert.ToBase64String(b);
                    //removal of Padding-Characters "=" happens when decoding of Base64-String
                    //https://tools.ietf.org/html/rfc4568 page 13
                    //s64 = s64.TrimEnd('=');
                    return s64;
                }
            }
            private ulong m_lifeTime = 0;
            public ulong LifeTime
            {
                get
                {
                    return this.m_lifeTime;
                }
                set
                {
                    if (!IsValidLifeTime(value))
                    {
                        throw new ArgumentOutOfRangeException("LifeTime", "LifeTime value must be power of 2 (excluding 2^0)");
                    }

                    int i = 0;
                    ulong temp = value;
                    while (temp > 1)
                    {
                        temp >>= 1;
                        i++;
                    }

                    this.m_lifeTime = value;
                    this.m_sLifeTime = s_lifeTimeStrings[i] ??= $"2^{i}";
                }
            }

            /// <summary>
            /// Formatted "2^n" lifetime strings indexed by exponent. Entries are created on first use and shared
            /// so that parsing key parameters with a lifetime does not allocate a new string each time.
            /// </summary>
            private static readonly string[] s_lifeTimeStrings = new string[64];

            private string m_sLifeTime = null;
            public string LifeTimeString
            {
                get
                {
                    return this.m_sLifeTime;
                }
                set
                {
                    if (!TryParseLifeTimeString(value.AsSpan(), out var lifeTime))
                    {
                        throw new ArgumentException("LifeTimeString must be in format '2^n' where n is a positive integer", "LifeTimeString");
                    }

                    this.m_lifeTime = lifeTime;
                    this.m_sLifeTime = value;
                }
            }
            public uint MkiValue
            {
                get;
                set;
            }
            private uint m_mkiLength = 0;
            public uint MkiLength
            {
                get
                {
                    return this.m_mkiLength;
                }
                set
                {
                    if (value > 0 && value <= 128)
                    {
                        this.m_mkiLength = value;
                    }
                    else
                    {
                        throw new ArgumentOutOfRangeException("MkiLength", "MkiLength value must between 1 and 128");
                    }
                }
            }
            public KeyParameter() : this(Sys.Crypto.GetRandomString(128 / 8), Sys.Crypto.GetRandomString(112 / 8))
            {

            }

            public KeyParameter(string key, string salt)
            {
                this.Key = Encoding.ASCII.GetBytes(key);
                this.Salt = Encoding.ASCII.GetBytes(salt);
            }

            public KeyParameter(byte[] key, byte[] salt)
            {
                this.Key = key;
                this.Salt = salt;
            }

            public override string ToString()
            {
                var builder = new StringBuilder();
                WriteString(builder);
                return builder.ToString();
            }

            public void WriteString(StringBuilder builder)
            {
                builder.Append(KEY_METHOD + COLON).Append(this.KeySaltBase64);
                if (!string.IsNullOrWhiteSpace(this.LifeTimeString))
                {
                    builder.Append(PIPE).Append(this.LifeTimeString);
                }
                else if (this.LifeTime > 0)
                {
                    builder.Append(PIPE).Append(this.LifeTime);
                }

                if (this.MkiLength > 0 && this.MkiValue > 0)
                {
                    builder.Append(PIPE).Append(this.MkiValue).Append(COLON).Append(this.MkiLength);
                }
            }

            public static KeyParameter Parse(string keyParamString, CryptoSuites cryptoSuite = CryptoSuites.AES_CM_128_HMAC_SHA1_80)
            {
                if (!TryParse(keyParamString, out var keyParam, cryptoSuite))
                {
                    throw new FormatException($"{nameof(keyParamString)} '{keyParamString}' is not recognized as a valid KEY_PARAM ");
                }
                return keyParam;
            }

            public static bool TryParse(string keyParamString, out KeyParameter keyParam, CryptoSuites cryptoSuite = CryptoSuites.AES_CM_128_HMAC_SHA1_80)
                => TryParse(keyParamString.AsSpan(), out keyParam, cryptoSuite);

            public static bool TryParse(ReadOnlySpan<char> keyParamText, out KeyParameter keyParam, CryptoSuites cryptoSuite = CryptoSuites.AES_CM_128_HMAC_SHA1_80)
            {
                keyParam = null;

                static bool CheckValidKeyInfoCharacters(ReadOnlySpan<char> keyInfo)
                {
                    foreach (var c in keyInfo)
                    {
                        if (c is < (char)0x21 or > (char)0x7e)
                        {
                            return false;
                        }
                    }
                    return true;
                }

                static bool ParseKeyInfo(ReadOnlySpan<char> keyInfo, out ReadOnlySpan<char> mkiValue, out ReadOnlySpan<char> mkiLen, out ReadOnlySpan<char> lifeTimeString, out ReadOnlySpan<char> base64KeySalt)
                {
                    mkiValue = default;
                    mkiLen = default;
                    lifeTimeString = default;
                    base64KeySalt = default;
                    // The Base64 portion contains concatenated key and salt; its length depends on the suite.
                    var pospipe1 = keyInfo.IndexOf(PIPE);
                    if (pospipe1 > 0)
                    {
                        base64KeySalt = keyInfo.Slice(0, pospipe1);
                        //find lifetime and mki
                        //both may be omitted, but mki is recognized by a colon
                        //usually lifetime comes before mki, if specified
                        var afterFirstPipe = pospipe1 + 1;
                        var keyInfoTail = keyInfo.Slice(afterFirstPipe);
                        var relativeColon = keyInfoTail.IndexOf(COLON);
                        var relativePipe = keyInfoTail.IndexOf(PIPE);
                        var posclnmki = relativeColon == -1 ? -1 : afterFirstPipe + relativeColon;
                        var pospipe2 = relativePipe == -1 ? -1 : afterFirstPipe + relativePipe;

                        if (posclnmki > 0 && pospipe2 < 0)
                        {
                            mkiValue = keyInfo.Slice(pospipe1 + 1, posclnmki - pospipe1 - 1);
                            mkiLen = keyInfo.Slice(posclnmki + 1);
                        }
                        else if (posclnmki > 0 && pospipe2 < posclnmki)
                        {
                            lifeTimeString = keyInfo.Slice(pospipe1 + 1, pospipe2 - pospipe1 - 1);
                            mkiValue = keyInfo.Slice(pospipe2 + 1, posclnmki - pospipe2 - 1);
                            mkiLen = keyInfo.Slice(posclnmki + 1);
                        }
                        else if (posclnmki > 0 && pospipe2 > posclnmki)
                        {
                            mkiValue = keyInfo.Slice(pospipe1 + 1, posclnmki - pospipe1 - 1);
                            mkiLen = keyInfo.Slice(posclnmki + 1, pospipe2 - posclnmki - 1);
                            lifeTimeString = keyInfo.Slice(pospipe2 + 1);
                        }
                        else if (posclnmki < 0 && pospipe2 < 0)
                        {
                            lifeTimeString = keyInfo.Slice(pospipe1 + 1);
                        }
                        else if (posclnmki < 0 && pospipe2 > 0)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        base64KeySalt = keyInfo;
                    }

                    return true;
                }

                if (!keyParamText.IsWhiteSpace())
                {
                    var p = keyParamText.Trim();
                    if (p.StartsWith(KEY_METHOD, StringComparison.Ordinal))
                    {
                        int poscln = p.IndexOf(COLON);
                        if (poscln == KEY_METHOD.Length)
                        {
                            var sKeyInfo = p.Slice(poscln + 1);
                            if (!sKeyInfo.Contains(SEMI_COLON))
                            {
                                if ((!CheckValidKeyInfoCharacters(sKeyInfo))
                                    || (!ParseKeyInfo(sKeyInfo, out var sMkiVal, out var sMkiLen, out var sLifeTime, out var sBase64KeySalt)))
                                {
                                    return false;
                                }

                                if (!sBase64KeySalt.IsWhiteSpace())
                                {
                                    if (!parseKeySaltBase64(cryptoSuite, sBase64KeySalt, out var bKey, out var bSalt)
                                        || !IsValidKey(bKey)
                                        || !IsValidSalt(bSalt))
                                    {
                                        return false;
                                    }

                                    keyParam = new KeyParameter(bKey, bSalt);
                                }
                                else
                                {
                                    keyParam = new KeyParameter();
                                }

                                if (!sMkiVal.IsWhiteSpace() && !sMkiLen.IsWhiteSpace())
                                {
                                    if (!uint.TryParse(sMkiVal, out var mkiValue)
                                        || !uint.TryParse(sMkiLen, out var mkiLen)
                                        || !(mkiLen > 0 && mkiLen <= 128))
                                    {
                                        keyParam = null;
                                        return false;
                                    }

                                    keyParam.MkiValue = mkiValue;
                                    keyParam.MkiLength = mkiLen;
                                }

                                if (!sLifeTime.IsWhiteSpace())
                                {
                                    if (sLifeTime.Contains('^'))
                                    {
                                        if (!TryParseLifeTimeString(sLifeTime, out var lifeTime))
                                        {
                                            keyParam = null;
                                            return false;
                                        }

                                        keyParam.LifeTime = lifeTime;
                                    }
                                    else
                                    {
                                        if (!uint.TryParse(sLifeTime, out var lifeTime)
                                            || !IsValidLifeTime(lifeTime))
                                        {
                                            keyParam = null;
                                            return false;
                                        }

                                        keyParam.LifeTime = lifeTime;
                                    }
                                }

                                return true;
                            }
                        }
                    }
                }

                keyParam = null;
                return false;
            }

            private static bool parseKeySaltBase64(CryptoSuites cryptoSuite, ReadOnlySpan<char> base64KeySalt, out byte[] key, out byte[] salt)
            {
                key = null;
                salt = null;

                int keyLength = 0;
                int saltLength = 0;

                switch (cryptoSuite)
                {
                    case CryptoSuites.AES_CM_128_HMAC_SHA1_32:
                    case CryptoSuites.AES_CM_128_HMAC_SHA1_80:
                    case CryptoSuites.F8_128_HMAC_SHA1_80:
                    case CryptoSuites.AEAD_AES_128_GCM:
                        keyLength = 128 / 8;
                        saltLength = (cryptoSuite == CryptoSuites.AEAD_AES_128_GCM) ? 96 / 8 : 112 / 8;
                        break;
                    case CryptoSuites.AES_192_CM_HMAC_SHA1_80:
                    case CryptoSuites.AES_192_CM_HMAC_SHA1_32:
                    case CryptoSuites.AES_CM_192_HMAC_SHA1_80:
                    case CryptoSuites.AES_CM_192_HMAC_SHA1_32:
                        keyLength = 192 / 8;
                        saltLength = 112 / 8;
                        break;
                    case CryptoSuites.AEAD_AES_256_GCM:
                        keyLength = 256 / 8;
                        saltLength = 96 / 8;
                        break;
                    case CryptoSuites.AES_256_CM_HMAC_SHA1_80:
                    case CryptoSuites.AES_256_CM_HMAC_SHA1_32:
                    case CryptoSuites.AES_CM_256_HMAC_SHA1_80:
                    case CryptoSuites.AES_CM_256_HMAC_SHA1_32:
                        keyLength = 256 / 8;
                        saltLength = 112 / 8;
                        break;
                    default:
                        return false;
                }

                const int stackBufferLength = 64;
                var maxDecodedLength = base64KeySalt.Length / 4 * 3;
                byte[] rentedBuffer = null;

                // Oversized key material is accepted, but must not cause unbounded stack allocation.
                Span<byte> keysalt = maxDecodedLength <= stackBufferLength
                    ? stackalloc byte[stackBufferLength]
                    : rentedBuffer = ArrayPool<byte>.Shared.Rent(maxDecodedLength);

                try
                {
                    if (!Convert.TryFromBase64Chars(base64KeySalt, keysalt, out var bytesWritten)
                        || bytesWritten < keyLength + saltLength)
                    {
                        return false;
                    }

                    key = keysalt.Slice(0, keyLength).ToArray();
                    salt = keysalt.Slice(keyLength, saltLength).ToArray();

                    return true;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(keysalt);
                    if (rentedBuffer is not null)
                    {
                        ArrayPool<byte>.Shared.Return(rentedBuffer);
                    }
                }
            }

            private static bool IsValidLifeTime(ulong value)
            {
                return value >= 2 && (value & (value - 1)) == 0;
            }

            private static bool IsValidKey(byte[] key)
            {
                return key != null && key.Length >= 16;
            }

            private static bool IsValidSalt(byte[] salt)
            {
                return salt != null && salt.Length >= 12;
            }

            private static bool TryParseLifeTimeString(ReadOnlySpan<char> lifeTimeString, out ulong lifeTime)
            {
                lifeTime = 0;

                var lifeTimeSpan = lifeTimeString.Trim();
                if (lifeTimeSpan.IsEmpty || !lifeTimeSpan.StartsWith("2^", StringComparison.Ordinal))
                {
                    return false;
                }

                if (!ulong.TryParse(lifeTimeSpan.Slice(2), out var exponent) || exponent < 1 || exponent >= 64)
                {
                    return false;
                }

                lifeTime = 1UL << (int)exponent;
                return true;
            }

            public static KeyParameter CreateNew(CryptoSuites cryptoSuite, string key = null, string salt = null)
            {
                switch (cryptoSuite)
                {
                    case CryptoSuites.AES_CM_128_HMAC_SHA1_32:
                    case CryptoSuites.AES_CM_128_HMAC_SHA1_80:
                    case CryptoSuites.F8_128_HMAC_SHA1_80:
                        if (string.IsNullOrWhiteSpace(key))
                        {
                            key = Sys.Crypto.GetRandomString(128 / 8);
                        }
                        if (string.IsNullOrWhiteSpace(salt))
                        {
                            salt = Sys.Crypto.GetRandomString(112 / 8);
                        }
                        return new KeyParameter(key, salt);
                    case CryptoSuites.AES_192_CM_HMAC_SHA1_80:
                    case CryptoSuites.AES_192_CM_HMAC_SHA1_32:
                    case CryptoSuites.AES_CM_192_HMAC_SHA1_80:
                    case CryptoSuites.AES_CM_192_HMAC_SHA1_32:
                        if (string.IsNullOrWhiteSpace(key))
                        {
                            key = Sys.Crypto.GetRandomString(192 / 8);
                        }
                        if (string.IsNullOrWhiteSpace(salt))
                        {
                            salt = Sys.Crypto.GetRandomString(112 / 8);
                        }
                        return new KeyParameter(key, salt);
                    case CryptoSuites.AEAD_AES_128_GCM:
                        if (string.IsNullOrWhiteSpace(key))
                        {
                            key = Sys.Crypto.GetRandomString(128 / 8);
                        }
                        if (string.IsNullOrWhiteSpace(salt))
                        {
                            salt = Sys.Crypto.GetRandomString(96 / 8);
                        }
                        return new KeyParameter(key, salt);
                    case CryptoSuites.AEAD_AES_256_GCM:
                        if (string.IsNullOrWhiteSpace(key))
                        {
                            key = Sys.Crypto.GetRandomString(256 / 8);
                        }
                        if (string.IsNullOrWhiteSpace(salt))
                        {
                            salt = Sys.Crypto.GetRandomString(96 / 8);
                        }
                        return new KeyParameter(key, salt);
                    case CryptoSuites.AES_256_CM_HMAC_SHA1_80:
                    case CryptoSuites.AES_256_CM_HMAC_SHA1_32:
                    case CryptoSuites.AES_CM_256_HMAC_SHA1_80:
                    case CryptoSuites.AES_CM_256_HMAC_SHA1_32:
                        if (string.IsNullOrWhiteSpace(key))
                        {
                            key = Sys.Crypto.GetRandomString(256 / 8);
                        }
                        if (string.IsNullOrWhiteSpace(salt))
                        {
                            salt = Sys.Crypto.GetRandomString(112 / 8);
                        }
                        return new KeyParameter(key, salt);

                }
                return null;
            }
        }

        public class SessionParameter
        {
            public enum SrtpSessionParams
            {
                unknown,
                kdr,
                UNENCRYPTED_SRTP,
                UNENCRYPTED_SRTCP,
                UNAUTHENTICATED_SRTP,
                fec_order,
                fec_key,
                wsh
            }
            public SrtpSessionParams SrtpSessionParam
            {
                get;
                set;
            }
            public enum FecTypes
            {
                unknown,
                FEC_SRTP,
                SRTP_FEC
            }
            public FecTypes FecOrder
            {
                get;
                set;
            }
            public const string FEC_KEY_PREFIX = "FEC_KEY=";
            public const string FEC_ORDER_PREFIX = "FEC_ORDER=";
            public const string WSH_PREFIX = "WSH=";
            public const string KDR_PREFIX = "KDR=";

            private ulong m_kdr = 0;
            public ulong Kdr
            {
                get
                {
                    return this.m_kdr;
                }
                set
                {
                    if (value < 0 || value > 24)
                    {
                        throw new ArgumentOutOfRangeException("Kdr", "Kdr must be between 0 and 24");
                    }

                    this.m_kdr = value;
                }
            }
            private ulong m_wsh = 64;
            public ulong Wsh
            {
                get
                {
                    return this.m_wsh;
                }
                set
                {
                    if (value < 64)
                    {
                        throw new ArgumentOutOfRangeException("WSH", "WSH must be greater than 64");
                    }

                    this.m_wsh = value;
                }
            }

            public KeyParameter FecKey
            {
                get;
                set;
            }

            public SessionParameter() : this(SrtpSessionParams.unknown)
            {

            }
            public SessionParameter(SrtpSessionParams paramType)
            {
                this.SrtpSessionParam = paramType;
            }
            public override string ToString()
            {
                var builder = new StringBuilder();
                WriteString(builder);
                return builder.ToString();
            }

            public void WriteString(StringBuilder builder)
            {
                switch (this.SrtpSessionParam)
                {
                    case SrtpSessionParams.UNAUTHENTICATED_SRTP:
                        builder.Append(nameof(SrtpSessionParams.UNAUTHENTICATED_SRTP));
                        break;
                    case SrtpSessionParams.UNENCRYPTED_SRTP:
                        builder.Append(nameof(SrtpSessionParams.UNENCRYPTED_SRTP));
                        break;
                    case SrtpSessionParams.UNENCRYPTED_SRTCP:
                        builder.Append(nameof(SrtpSessionParams.UNENCRYPTED_SRTCP));
                        break;
                    case SrtpSessionParams.wsh:
                        builder.Append(WSH_PREFIX).Append(this.Wsh);
                        break;
                    case SrtpSessionParams.kdr:
                        builder.Append(KDR_PREFIX).Append(this.Kdr);
                        break;
                    case SrtpSessionParams.fec_order:
                        builder.Append(FEC_ORDER_PREFIX).Append(this.FecOrder switch
                        {
                            FecTypes.FEC_SRTP => nameof(FecTypes.FEC_SRTP),
                            FecTypes.SRTP_FEC => nameof(FecTypes.SRTP_FEC),
                            _ => this.FecOrder.ToString()
                        });
                        break;
                    case SrtpSessionParams.fec_key:
                        builder.Append(FEC_KEY_PREFIX);
                        this.FecKey?.WriteString(builder);
                        break;
                }
            }

            public static SessionParameter Parse(string sessionParam, CryptoSuites cryptoSuite = CryptoSuites.AES_CM_128_HMAC_SHA1_80)
            {
                if (!TryParse(sessionParam, out var result, cryptoSuite))
                {
                    throw new FormatException($"sessionParam '{sessionParam}' is not recognized as a valid SRTP_SESSION_PARAM ");
                }

                return result;
            }

            public static bool TryParse(string sessionParam, out SessionParameter result, CryptoSuites cryptoSuite = CryptoSuites.AES_CM_128_HMAC_SHA1_80)
                => TryParse(sessionParam.AsSpan(), out result, cryptoSuite);

            public static bool TryParse(ReadOnlySpan<char> sessionParam, out SessionParameter result, CryptoSuites cryptoSuite = CryptoSuites.AES_CM_128_HMAC_SHA1_80)
            {
                result = null;

                if (sessionParam.IsWhiteSpace())
                {
                    return true;
                }

                var p = sessionParam.Trim();
                if (p.StartsWith(KDR_PREFIX, StringComparison.Ordinal))
                {
                    if (uint.TryParse(p.Slice(KDR_PREFIX.Length), out var kdr))
                    {
                        result = new SessionParameter(SrtpSessionParams.kdr) { Kdr = kdr };
                        return true;
                    }
                }
                else if (p.StartsWith(WSH_PREFIX, StringComparison.Ordinal))
                {
                    if (uint.TryParse(p.Slice(WSH_PREFIX.Length), out var wsh))
                    {
                        result = new SessionParameter(SrtpSessionParams.wsh) { Wsh = wsh };
                        return true;
                    }
                }
                else if (p.StartsWith(FEC_KEY_PREFIX, StringComparison.Ordinal))
                {
                    if (!KeyParameter.TryParse(p.Slice(FEC_KEY_PREFIX.Length), out var fecKey, cryptoSuite))
                    {
                        return false;
                    }
                    result = new SessionParameter(SrtpSessionParams.fec_key) { FecKey = fecKey };
                    return true;
                }
                else if (p.StartsWith(FEC_ORDER_PREFIX, StringComparison.Ordinal))
                {
                    // Exact, case-sensitive names only; the compiler lowers the span switch to length and character checks.
                    var fecOrder = p.Slice(FEC_ORDER_PREFIX.Length) switch
                    {
                        nameof(FecTypes.FEC_SRTP) => FecTypes.FEC_SRTP,
                        nameof(FecTypes.SRTP_FEC) => FecTypes.SRTP_FEC,
                        _ => FecTypes.unknown,
                    };
                    if (fecOrder == FecTypes.unknown)
                    {
                        return false;
                    }

                    result = new SessionParameter(SrtpSessionParams.fec_order) { FecOrder = fecOrder };
                    return true;
                }
                else if (p.SequenceEqual(nameof(SrtpSessionParams.UNAUTHENTICATED_SRTP).AsSpan()))
                {
                    result = new SessionParameter(SrtpSessionParams.UNAUTHENTICATED_SRTP);
                    return true;
                }
                else if (p.SequenceEqual(nameof(SrtpSessionParams.UNENCRYPTED_SRTCP).AsSpan()))
                {
                    result = new SessionParameter(SrtpSessionParams.UNENCRYPTED_SRTCP);
                    return true;
                }
                else if (p.SequenceEqual(nameof(SrtpSessionParams.UNENCRYPTED_SRTP).AsSpan()))
                {
                    result = new SessionParameter(SrtpSessionParams.UNENCRYPTED_SRTP);
                    return true;
                }

                return false;
            }
        }


        private uint m_iTag = 1;
        public uint Tag
        {
            get
            {
                return this.m_iTag;
            }
            set
            {
                if (value > 0 && value < 1000000000)
                {
                    this.m_iTag = value;
                }
                else
                {
                    throw new ArgumentOutOfRangeException("Tag", "Tag value must be greater than 0 and not exceed 9 digits");
                }
            }
        }


        public CryptoSuites CryptoSuite
        {
            get;
            set;
        }

        public List<KeyParameter> KeyParams
        {
            get;
            set;
        }
        public SessionParameter SessionParam
        {
            get;
            set;
        }
        public SDPSecurityDescription() : this(1, CryptoSuites.AES_CM_128_HMAC_SHA1_80)
        {

        }
        public SDPSecurityDescription(uint tag, CryptoSuites cryptoSuite)
        {
            this.Tag = tag;
            this.CryptoSuite = cryptoSuite;
            this.KeyParams = new List<KeyParameter>();
        }

        public static SDPSecurityDescription CreateNew(uint tag = 1, CryptoSuites cryptoSuite = CryptoSuites.AES_CM_128_HMAC_SHA1_80)
        {
            SDPSecurityDescription secdesc = new SDPSecurityDescription(tag, cryptoSuite);
            secdesc.KeyParams.Add(KeyParameter.CreateNew(cryptoSuite));
            return secdesc;
        }

        public override string ToString()
        {
            var builder = new StringBuilder();
            return WriteString(builder) ? builder.ToString() : null;
        }

        public bool WriteString(StringBuilder builder)
        {
            if (this.Tag < 1 || this.CryptoSuite == CryptoSuites.unknown || this.KeyParams.Count < 1)
            {
                return false;
            }

            var cryptoSuiteIndex = (int)this.CryptoSuite;
            var cryptoSuiteName = (uint)cryptoSuiteIndex < (uint)s_cryptoSuiteNames.Length
                ? s_cryptoSuiteNames[cryptoSuiteIndex]
                : this.CryptoSuite.ToString();

            builder.Append(CRYPTO_ATTRIBUE_PREFIX).Append(this.Tag).Append(WHITE_SPACE).Append(cryptoSuiteName).Append(WHITE_SPACE);
            for (var i = 0; i < this.KeyParams.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(SEMI_COLON);
                }

                this.KeyParams[i].WriteString(builder);
            }
            if (this.SessionParam != null)
            {
                builder.Append(WHITE_SPACE);
                this.SessionParam.WriteString(builder);
            }

            return true;
        }

        public static SDPSecurityDescription Parse(string cryptoLine)
        {
            if (!TryParse(cryptoLine, out var securityDescription))
            {
                throw new FormatException($"cryptoLine '{cryptoLine}' is not recognized as a valid SDP Security Description ");
            }

            return securityDescription;
        }

        public static SDPSecurityDescription Parse(ReadOnlySpan<char> cryptoLine)
        {
            if (!TryParse(cryptoLine, out var securityDescription))
            {
                throw new FormatException($"cryptoLine '{cryptoLine.ToString()}' is not recognized as a valid SDP Security Description ");
            }

            return securityDescription;
        }

        public static bool TryParse(string cryptoLine, out SDPSecurityDescription securityDescription)
        {
            securityDescription = null;
            if (string.IsNullOrWhiteSpace(cryptoLine))
            {
                return true;
            }

            // The string overload keeps its original prefix and colon matching so existing callers observe the same results.
            if (!cryptoLine.StartsWith(CRYPTO_ATTRIBUE_PREFIX))
            {
                return false;
            }

            return TryParseCryptoValue(cryptoLine.AsSpan(cryptoLine.IndexOf(COLON) + 1), out securityDescription);
        }

        public static bool TryParse(ReadOnlySpan<char> cryptoLine, out SDPSecurityDescription securityDescription)
        {
            securityDescription = null;
            if (cryptoLine.IsWhiteSpace())
            {
                return true;
            }

            if (!cryptoLine.StartsWith(CRYPTO_ATTRIBUE_PREFIX.AsSpan(), StringComparison.Ordinal))
            {
                return false;
            }

            return TryParseCryptoValue(cryptoLine.Slice(CRYPTO_ATTRIBUE_PREFIX.Length), out securityDescription);
        }

        /// <summary>
        /// Parses the value that follows the crypto attribute prefix.
        /// </summary>
        /// <param name="sCryptoValue">The crypto attribute value.</param>
        /// <param name="securityDescription">The parsed security description, or a partially populated instance if parsing fails after the value was accepted.</param>
        /// <returns>True if the value was a valid security description.</returns>
        private static bool TryParseCryptoValue(ReadOnlySpan<char> sCryptoValue, out SDPSecurityDescription securityDescription)
        {
            securityDescription = new SDPSecurityDescription();
            Span<Range> sCryptoPartRanges = stackalloc Range[5];
            var sCryptoPartCount = sCryptoValue.SplitAny(sCryptoPartRanges, WHITE_SPACES.AsSpan(), StringSplitOptions.RemoveEmptyEntries);
            if (sCryptoValue.Length < 2)
            {
                return false;
            }

            if (sCryptoPartCount < 2)
            {
                return false;
            }

            if (!uint.TryParse(sCryptoValue[sCryptoPartRanges[0]], out var tag))
            {
                return false;
            }
            securityDescription.Tag = tag;

            // Exact, case-sensitive suite names only (no numeric values and no "unknown"); the compiler
            // lowers the span switch to length and character checks, so no string or lookup is needed.
            var cryptoSuite = sCryptoValue[sCryptoPartRanges[1]] switch
            {
                nameof(CryptoSuites.AES_CM_128_HMAC_SHA1_80) => CryptoSuites.AES_CM_128_HMAC_SHA1_80,
                nameof(CryptoSuites.AES_CM_128_HMAC_SHA1_32) => CryptoSuites.AES_CM_128_HMAC_SHA1_32,
                nameof(CryptoSuites.F8_128_HMAC_SHA1_80) => CryptoSuites.F8_128_HMAC_SHA1_80,
                nameof(CryptoSuites.AEAD_AES_128_GCM) => CryptoSuites.AEAD_AES_128_GCM,
                nameof(CryptoSuites.AEAD_AES_256_GCM) => CryptoSuites.AEAD_AES_256_GCM,
                nameof(CryptoSuites.AES_192_CM_HMAC_SHA1_80) => CryptoSuites.AES_192_CM_HMAC_SHA1_80,
                nameof(CryptoSuites.AES_192_CM_HMAC_SHA1_32) => CryptoSuites.AES_192_CM_HMAC_SHA1_32,
                nameof(CryptoSuites.AES_256_CM_HMAC_SHA1_80) => CryptoSuites.AES_256_CM_HMAC_SHA1_80,
                nameof(CryptoSuites.AES_256_CM_HMAC_SHA1_32) => CryptoSuites.AES_256_CM_HMAC_SHA1_32,
                nameof(CryptoSuites.AES_CM_192_HMAC_SHA1_80) => CryptoSuites.AES_CM_192_HMAC_SHA1_80,
                nameof(CryptoSuites.AES_CM_192_HMAC_SHA1_32) => CryptoSuites.AES_CM_192_HMAC_SHA1_32,
                nameof(CryptoSuites.AES_CM_256_HMAC_SHA1_80) => CryptoSuites.AES_CM_256_HMAC_SHA1_80,
                nameof(CryptoSuites.AES_CM_256_HMAC_SHA1_32) => CryptoSuites.AES_CM_256_HMAC_SHA1_32,
                _ => CryptoSuites.unknown,
            };
            if (cryptoSuite == CryptoSuites.unknown)
            {
                return false;
            }
            securityDescription.CryptoSuite = cryptoSuite;

            if (sCryptoPartCount < 3)
            {
                return false;
            }

            var sKeyParams = sCryptoValue[sCryptoPartRanges[2]];
            var hasKeyParam = false;
            foreach (var keyParamRange in sKeyParams.Split(SEMI_COLON))
            {
                hasKeyParam = true;
                if (!KeyParameter.TryParse(sKeyParams[keyParamRange], out var keyParam, securityDescription.CryptoSuite))
                {
                    securityDescription = null;
                    return false;
                }
                securityDescription.KeyParams.Add(keyParam);
            }

            if (!hasKeyParam)
            {
                securityDescription = null;
                return false;
            }

            if (sCryptoPartCount > 3)
            {
                if (!SessionParameter.TryParse(sCryptoValue[sCryptoPartRanges[3]], out var sessionParam, securityDescription.CryptoSuite))
                {
                    securityDescription = null;
                    return false;
                }
                securityDescription.SessionParam = sessionParam;
            }

            return true;
        }
    }
}
