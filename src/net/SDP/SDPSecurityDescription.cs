//-----------------------------------------------------------------------------
// Filename: SDP.cs
//
// Description: (SDP) Security Descriptions for Media Streams implementation as basically defined in RFC 4568.
// https://tools.ietf.org/html/rfc4568
//
// Author(s):
// rj2

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

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
public class SDPSecurityDescription : IEquatable<SDPSecurityDescription>
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
    private static readonly FrozenDictionary<string, CryptoSuites> s_cryptoSuiteLookup = CreateCryptoSuiteLookup();
    private static FrozenDictionary<string, CryptoSuites> CreateCryptoSuiteLookup()
    {
        var values = Enum.GetValues(typeof(CryptoSuites));
        var lookup = new Dictionary<string, CryptoSuites>(values.Length - 1, StringComparer.Ordinal);
        foreach (CryptoSuites cs in values)
        {
            if (cs != CryptoSuites.unknown)
            {
                lookup[cs.ToString()] = cs;
            }
        }
        return lookup.ToFrozenDictionary();
    }
    public class KeyParameter
    {
        private const char COLON = ':';
        private const char PIPE = '|';
        public const string KEY_METHOD = "inline";

        //128 bit for AES_CM_128_HMAC_SHA1_80, AES_CM_128_HMAC_SHA1_32, F8_128_HMAC_SHA1_80, AEAD_AES_128_GCM
        //192 bit for AES_192_CM_HMAC_SHA1_80, AES_192_CM_HMAC_SHA1_32
        //256 bit for AEAD_AES_256_GCM, AES_256_CM_HMAC_SHA1_80, AES_256_CM_HMAC_SHA1_32 
        //
        public byte[] Key
        {
            get
            {
                Debug.Assert(field is { Length: >= 16 });
                return field;
            }
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                ArgumentOutOfRangeException.ThrowIfLessThan(value.Length, 16);
                if (!IsValidKey(value))
                {
                    throw value == null
                        ? new ArgumentNullException("Key", "Key must have a value")
                        : new ArgumentOutOfRangeException("Key", "Key must be at least 16 characters long");
                }

                field = value;
            }
        }

        //112 bit for AES_CM_128_HMAC_SHA1_80, AES_CM_128_HMAC_SHA1_32, F8_128_HMAC_SHA1_80
        //112 bit for AES_192_CM_HMAC_SHA1_80,AES_192_CM_HMAC_SHA1_32 , AES_256_CM_HMAC_SHA1_80, AES_256_CM_HMAC_SHA1_32 
        //96 bit for AEAD_AES_128_GCM
        //
        public byte[] Salt
        {
            get
            {
                Debug.Assert(field is { Length: >= 12 });
                return field;
            }
            set
            {
                if (!IsValidSalt(value))
                {
                    throw value == null
                        ? new ArgumentNullException("Salt", "Salt must have a value")
                        : new ArgumentOutOfRangeException("Salt", "Salt must be at least 12 characters long");
                }

                field = value;
            }
        }

        public string KeySaltBase64
        {
            get
            {
                var b = new byte[this.Key.Length + this.Salt.Length];
                Array.Copy(this.Key, 0, b, 0, this.Key.Length);
                Array.Copy(this.Salt, 0, b, this.Key.Length, this.Salt.Length);
                var s64 = Convert.ToBase64String(b);
                //removal of Padding-Characters "=" happens when decoding of Base64-String
                //https://tools.ietf.org/html/rfc4568 page 13
                //s64 = s64.TrimEnd('=');
                return s64;
            }
        }

        public ulong LifeTime
        {
            get
            {
                return field;
            }
            set
            {
                if (!IsValidLifeTime(value))
                {
                    throw new ArgumentOutOfRangeException("LifeTime", "LifeTime value must be power of 2");
                }

                var i = 0;
                var temp = value;
                while (temp > 1)
                {
                    temp >>= 1;
                    i++;
                }

                field = value;
                string lifeTimeString = $"2^{i}";

                if (lifeTimeString != LifeTimeString)
                {
                    LifeTimeString = lifeTimeString;
                }
            }
        }

        public string? LifeTimeString
        {
            get
            {
                return field;
            }
            set
            {
                if (!TryParseLifeTimeString(value, out ulong lifeTime))
                {
                    throw new ArgumentException("LifeTimeString must be in format '2^n' where n is a positive integer", "LifeTimeString");
                }

                field = value;

                if (lifeTime != LifeTime)
                {
                    LifeTime = lifeTime;
                }

            }
        }

        public uint MkiValue { get; set; }

        public uint MkiLength
        {
            get
            {
                return field;
            }
            set
            {
                if (value is > 0 and <= 128)
                {
                    field = value;
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
            var builder = new ValueStringBuilder(stackalloc char[128]);

            try
            {
                ToString(ref builder);
                return builder.ToString();
            }
            finally
            {
                builder.Dispose();
            }
        }

        internal void ToString(ref ValueStringBuilder builder)
        {
            builder.Append(KEY_METHOD);
            builder.Append(COLON);
            builder.Append(this.KeySaltBase64);

            if (!string.IsNullOrWhiteSpace(this.LifeTimeString))
            {
                builder.Append(PIPE);
                builder.Append(this.LifeTimeString);
            }
            else if (this.LifeTime > 0)
            {
                builder.Append(PIPE);
                builder.Append(this.LifeTime);
            }

            if (this.MkiLength > 0 && this.MkiValue > 0)
            {
                builder.Append(PIPE);
                builder.Append(this.MkiValue);
                builder.Append(COLON);
                builder.Append(this.MkiLength);
            }
        }

        public static KeyParameter? Parse(ReadOnlySpan<char> keyParamString, CryptoSuites cryptoSuite = CryptoSuites.AES_CM_128_HMAC_SHA1_80)
        {
            if (!TryParse(keyParamString, out var result, cryptoSuite))
            {
                throw new FormatException($"keyParam '{keyParamString}' is not recognized as a valid KEY_PARAM ");
            }

            return result!;
        }

        public static bool TryParse(ReadOnlySpan<char> keyParamString, out KeyParameter? keyParam, CryptoSuites cryptoSuite = CryptoSuites.AES_CM_128_HMAC_SHA1_80)
        {
            keyParam = null;

            keyParamString = keyParamString.Trim();
            if (keyParamString.IsEmpty)
            {
                return false;
            }

            if (keyParamString.StartsWith(KEY_METHOD, StringComparison.Ordinal))
            {
                var poscln = keyParamString.IndexOf(COLON);
                if (poscln == KEY_METHOD.Length)
                {
                    var sKeyInfo = keyParamString.Slice(poscln + 1);
                    if (!sKeyInfo.Contains(SEMI_COLON))
                    {
                        if (!checkValidKeyInfoCharacters(sKeyInfo)
                            || !parseKeyInfo(sKeyInfo, out var sMkiVal, out var sMkiLen, out var sLifeTime, out var sBase64KeySalt))
                        {
                            return false;
                        }

                        if (!string.IsNullOrWhiteSpace(sBase64KeySalt))
                        {
                            if (!parseKeySaltBase64(cryptoSuite, sBase64KeySalt, out var bKey, out var bSalt)
                                || !IsValidKey(bKey)
                                || !IsValidSalt(bSalt))
                            {
                                return false;
                            }

                            Debug.Assert(bKey is { });
                            Debug.Assert(bSalt is { });

                            keyParam = new KeyParameter(bKey, bSalt);
                        }
                        else
                        {
                            keyParam = new KeyParameter();
                        }

                        if (!string.IsNullOrWhiteSpace(sMkiVal) && !string.IsNullOrWhiteSpace(sMkiLen))
                        {
                            if (!uint.TryParse(sMkiVal, out uint mkiValue)
                                || !uint.TryParse(sMkiLen, out uint mkiLen)
                                || !(mkiLen > 0 && mkiLen <= 128))
                            {
                                keyParam = null;
                                return false;
                            }

                            keyParam.MkiValue = mkiValue;
                            keyParam.MkiLength = mkiLen;
                        }

                        if (!string.IsNullOrWhiteSpace(sLifeTime))
                        {
                            if (sLifeTime.Contains('^'))
                            {
                                if (!TryParseLifeTimeString(sLifeTime, out ulong lifeTime))
                                {
                                    keyParam = null;
                                    return false;
                                }

                                keyParam.LifeTime = lifeTime;
                            }
                            else
                            {
                                if (!uint.TryParse(sLifeTime, out uint lifeTime)
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

            keyParam = null;
            return false;
        }

        private static bool parseKeySaltBase64(
            CryptoSuites cryptoSuite,
            string base64KeySalt,
            out byte[]? key,
            out byte[]? salt)
        {
            key = null;
            salt = null;

            byte[] keysalt;
            try
            {
                keysalt = Convert.FromBase64String(base64KeySalt);
            }
            catch
            {
                return false;
            }

            int keyLength = 0;
            int saltLength = 0;
            int saltOffset = 0;

            switch (cryptoSuite)
            {
                case CryptoSuites.AES_CM_128_HMAC_SHA1_32:
                case CryptoSuites.AES_CM_128_HMAC_SHA1_80:
                case CryptoSuites.F8_128_HMAC_SHA1_80:
                case CryptoSuites.AEAD_AES_128_GCM:
                    keyLength = 128 / 8;
                    saltOffset = 128 / 8;
                    saltLength = (cryptoSuite == CryptoSuites.AEAD_AES_128_GCM) ? 96 / 8 : 112 / 8;
                    break;
                case CryptoSuites.AES_192_CM_HMAC_SHA1_80:
                case CryptoSuites.AES_192_CM_HMAC_SHA1_32:
                case CryptoSuites.AES_CM_192_HMAC_SHA1_80:
                case CryptoSuites.AES_CM_192_HMAC_SHA1_32:
                    keyLength = 192 / 8;
                    saltOffset = 192 / 8;
                    saltLength = 112 / 8;
                    break;
                case CryptoSuites.AEAD_AES_256_GCM:
                    keyLength = 256 / 8;
                        saltOffset = 256 / 8;
                    saltLength = 96 / 8;
                    break;
                case CryptoSuites.AES_256_CM_HMAC_SHA1_80:
                case CryptoSuites.AES_256_CM_HMAC_SHA1_32:
                case CryptoSuites.AES_CM_256_HMAC_SHA1_80:
                case CryptoSuites.AES_CM_256_HMAC_SHA1_32:
                    keyLength = 256 / 8;
                    saltOffset = 256 / 8;
                    saltLength = 112 / 8;
                    break;
                default:
                    return false;
            }

            if (keysalt.Length < keyLength + saltLength)
            {
                return false;
            }

            key = new byte[keyLength];
            Array.Copy(keysalt, 0, key, 0, keyLength);

            salt = new byte[saltLength];
            Array.Copy(keysalt, saltOffset, salt, 0, saltLength);

            return true;
        }

        private static bool checkValidKeyInfoCharacters(ReadOnlySpan<char> keyInfo)
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

            if (lifeTimeString.IsEmptyOrWhiteSpace() || !lifeTimeString.StartsWith("2^"))
            {
                return false;
            }

            var exponentPart = lifeTimeString.Slice(2);
            if (!ulong.TryParse(exponentPart, out ulong exponent) || exponent < 1)
            {
                return false;
            }

            lifeTime = (ulong)Math.Pow(2, (double)exponent);
            return true;
        }

        private static bool parseKeyInfo(ReadOnlySpan<char> keyInfo, out string? mkiValue, out string? mkiLen, out string? lifeTimeString, out string? base64KeySalt)
        {
            // Examples of keyInfo formats:
            // - "WVNfX19zZW1jdGwgKCkgewkyMjA7fQp9CnVubGVz" (base64 only)
            // - "WVNfX19zZW1jdGwgKCkgewkyMjA7fQp9CnVubGVz|2^20" (base64|lifetime)
            // - "WVNfX19zZW1jdGwgKCkgewkyMjA7fQp9CnVubGVz|2^20|1:4" (base64|lifetime|mki:mkilen)
            // - "WVNfX19zZW1jdGwgKCkgewkyMjA7fQp9CnVubGVz|1:4" (base64|mki:mkilen)

            mkiValue = null;
            mkiLen = null;
            lifeTimeString = null;
            base64KeySalt = null;
            //KeyInfo must only contain visible printing characters
            //and 40 char long, as its is the base64representation of concatenated Key and Salt
            var pospipe1 = keyInfo.IndexOf(PIPE);
            if (pospipe1 > 0)
            {
                base64KeySalt = keyInfo.Slice(0, pospipe1).ToString();
                //find lifetime and mki
                //both may be omitted, but mki is recognized by a colon
                //usually lifetime comes before mki, if specified
                var remaining = keyInfo.Slice(pospipe1 + 1);
                var posclnmki = remaining.IndexOf(COLON);
                var pospipe2 = remaining.IndexOf(PIPE);
                if (posclnmki > 0 && pospipe2 < 0)
                {
                    mkiValue = remaining.Slice(0, posclnmki).ToString();
                    mkiLen = remaining.Slice(posclnmki + 1).ToString();
                }
                else if (posclnmki > 0 && pospipe2 < posclnmki)
                {
                    lifeTimeString = remaining.Slice(0, pospipe2).ToString();
                    mkiValue = remaining.Slice(pospipe2 + 1, posclnmki - pospipe2 - 1).ToString();
                    mkiLen = remaining.Slice(posclnmki + 1).ToString();
                }
                else if (posclnmki > 0 && pospipe2 > posclnmki)
                {
                    mkiValue = remaining.Slice(0, posclnmki).ToString();
                    mkiLen = remaining.Slice(posclnmki + 1, pospipe2 - posclnmki - 1).ToString();
                    lifeTimeString = remaining.Slice(pospipe2 + 1).ToString();
                }
                else if (posclnmki < 0 && pospipe2 < 0)
                {
                    lifeTimeString = remaining.ToString();
                }
                else if (posclnmki < 0 && pospipe2 > 0)
                {
                    return false;
                }
            }
            else
            {
                base64KeySalt = keyInfo.ToString();
            }

            return true;
        }

        public static KeyParameter? CreateNew(CryptoSuites cryptoSuite, string? key = null, string? salt = null)
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
                if (value is < 0 or > 24)
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

        public KeyParameter? FecKey
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
            if (this.SrtpSessionParam == SrtpSessionParams.unknown)
            {
                return "";
            }

            var builder = new ValueStringBuilder(stackalloc char[64]);

            try
            {
                ToString(ref builder);
                return builder.ToString();
            }
            finally
            {
                builder.Dispose();
            }
        }

        internal void ToString(ref ValueStringBuilder builder)
        {
            if (this.SrtpSessionParam == SrtpSessionParams.unknown)
            {
                return;
            }

            switch (this.SrtpSessionParam)
            {
                case SrtpSessionParams.UNAUTHENTICATED_SRTP:
                case SrtpSessionParams.UNENCRYPTED_SRTP:
                case SrtpSessionParams.UNENCRYPTED_SRTCP:
                    builder.Append(this.SrtpSessionParam.ToStringFast());
                    break;
                case SrtpSessionParams.wsh:
                    builder.Append(WSH_PREFIX);
                    builder.Append(this.Wsh);
                    break;
                case SrtpSessionParams.kdr:
                    builder.Append(KDR_PREFIX);
                    builder.Append(this.Kdr);
                    break;
                case SrtpSessionParams.fec_order:
                    builder.Append(FEC_ORDER_PREFIX);
                    builder.Append(this.FecOrder.ToStringFast());
                    break;
                case SrtpSessionParams.fec_key:
                    builder.Append(FEC_KEY_PREFIX);
                    if (this.FecKey is { })
                    {
                        this.FecKey.ToString(ref builder);
                    }
                    break;
            }
        }

        public static SessionParameter? Parse(ReadOnlySpan<char> sessionParam, CryptoSuites cryptoSuite = CryptoSuites.AES_CM_128_HMAC_SHA1_80)
        {
            if (!TryParse(sessionParam, out var result, cryptoSuite))
            {
                throw new FormatException($"sessionParam is not recognized as a valid SRTP_SESSION_PARAM ");
            }

            return result;
        }

        public static bool TryParse(ReadOnlySpan<char> sessionParam, out SessionParameter? result, CryptoSuites cryptoSuite = CryptoSuites.AES_CM_128_HMAC_SHA1_80)
        {
            result = null;

            sessionParam = sessionParam.Trim();
            if (sessionParam.IsEmpty)
            {
                return true;
            }

            var paramType = SrtpSessionParams.unknown;
            if (sessionParam.StartsWith(KDR_PREFIX, StringComparison.Ordinal))
            {
                if (uint.TryParse(sessionParam.Slice(KDR_PREFIX.Length), System.Globalization.NumberStyles.None, null, out var kdr))
                {
                    result = new SessionParameter(SrtpSessionParams.kdr) { Kdr = kdr };
                    return true;
                }
            }
            else if (sessionParam.StartsWith(WSH_PREFIX, StringComparison.Ordinal))
            {
                if (uint.TryParse(sessionParam.Slice(WSH_PREFIX.Length), System.Globalization.NumberStyles.None, null, out var wsh))
                {
                    result = new SessionParameter(SrtpSessionParams.wsh) { Wsh = wsh };
                    return true;
                }
            }
            else if (sessionParam.StartsWith(FEC_KEY_PREFIX, StringComparison.Ordinal))
            {
                var sFecKey = sessionParam.Slice(FEC_KEY_PREFIX.Length);
                if (!KeyParameter.TryParse(sFecKey, out var fecKey, cryptoSuite))
                {
                    return false;
                }
                result = new SessionParameter(SrtpSessionParams.fec_key) { FecKey = fecKey };
                return true;
            }
            else if (sessionParam.StartsWith(FEC_ORDER_PREFIX, StringComparison.Ordinal))
            {
                var sFecOrder = sessionParam.Slice(FEC_ORDER_PREFIX.Length);
                if (!FecTypesExtensions.TryParse(sFecOrder, out var fecOrder, ignoreCase: false)
                    || fecOrder == FecTypes.unknown
                    || !FecTypesExtensions.IsDefined(sFecOrder))
                {
                    return false;
                }

                result = new SessionParameter(SrtpSessionParams.fec_order) { FecOrder = fecOrder };
                return true;
            }
            else
            {
                if (!SrtpSessionParamsExtensions.TryParse(sessionParam.ToString(), out paramType, ignoreCase: false) || paramType == SrtpSessionParams.unknown)
                {
                    return false;
                }

                switch (paramType)
                {
                    case SrtpSessionParams.UNAUTHENTICATED_SRTP:
                    case SrtpSessionParams.UNENCRYPTED_SRTCP:
                    case SrtpSessionParams.UNENCRYPTED_SRTP:
                        result = new SessionParameter(paramType);
                        return true;
                }
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
            if (value is > 0 and < 1000000000)
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
    public SessionParameter? SessionParam
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
        var secdesc = new SDPSecurityDescription(tag, cryptoSuite);
        var keyParameter = KeyParameter.CreateNew(cryptoSuite);
        Debug.Assert(keyParameter is { });
        secdesc.KeyParams.Add(keyParameter);
        return secdesc;
    }

    public override string? ToString()
    {
        if (this.Tag < 1 || this.CryptoSuite == CryptoSuites.unknown || !(this.KeyParams is { Count: > 0 }))
        {
            return null;
        }

        var builder = new ValueStringBuilder(stackalloc char[256]);

        try
        {
            ToString(ref builder);

            return builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }
    }

    internal void ToString(ref ValueStringBuilder builder)
    {
        if (this.Tag < 1 || this.CryptoSuite == CryptoSuites.unknown || !(this.KeyParams is { Count: > 0 }))
        {
            return;
        }

        builder.Append(CRYPTO_ATTRIBUE_PREFIX);
        builder.Append(this.Tag);
        builder.Append(WHITE_SPACE);
        builder.Append(this.CryptoSuite.ToStringFast());
        builder.Append(WHITE_SPACE);

        for (var i = 0; i < this.KeyParams.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(SEMI_COLON);
            }

            this.KeyParams[i].ToString(ref builder);
        }

        if (this.SessionParam is { })
        {
            builder.Append(WHITE_SPACE);
            this.SessionParam.ToString(ref builder);
        }
    }

    public static SDPSecurityDescription? Parse(ReadOnlySpan<char> cryptoLine)
    {
        if (!TryParse(cryptoLine, out var securityDescription))
        {
            throw new FormatException($"cryptoLine '{cryptoLine}' is not recognized as a valid SDP Security Description ");
        }

        return securityDescription;
    }

    public static bool TryParse(ReadOnlySpan<char> cryptoLine, [NotNullWhen(true)] out SDPSecurityDescription? securityDescription)
    {
        securityDescription = null;
        if (cryptoLine.IsEmptyOrWhiteSpace())
        {
            return true;
        }

        if (!cryptoLine.StartsWith(CRYPTO_ATTRIBUE_PREFIX, StringComparison.Ordinal))
        {
            return false;
        }

        var sCryptoValue = cryptoLine.Slice(cryptoLine.IndexOf(COLON) + 1);

        Span<Range> sCryptoParts = stackalloc Range[5];
        var sCryptoPartsCount = sCryptoValue.SplitAny(sCryptoParts, WHITE_SPACES, StringSplitOptions.RemoveEmptyEntries);

        if (sCryptoPartsCount < 2)
        {
            return false;
        }

        if (!uint.TryParse(sCryptoValue[sCryptoParts[0]], out var tag))
        {
            return false;
        }

        var cryptoSuiteSpan = sCryptoValue[sCryptoParts[1]];
        if (!CryptoSuitesExtensions.TryParse(cryptoSuiteSpan, out var cryptoSuite)
            || cryptoSuite == CryptoSuites.unknown
            || !CryptoSuitesExtensions.IsDefined(cryptoSuiteSpan))
        {
            return false;
        }

        if (sCryptoPartsCount < 3)
        {
            return false;
        }

        List<KeyParameter>? keyParams = null;
        var keyParamsSpan = sCryptoValue[sCryptoParts[2]];
        foreach (var keyRange in keyParamsSpan.SplitAny([SEMI_COLON]))
        {
            var keyParam = keyParamsSpan[keyRange];
            if (!KeyParameter.TryParse(keyParam, out var parsedKeyParam, cryptoSuite))
            {
                securityDescription = null;
                return false;
            }

            (keyParams ?? new()).Add(parsedKeyParam);
        }

        if (keyParams is null)
        {
            return false;
        }

        securityDescription = new SDPSecurityDescription();
        securityDescription.Tag = tag;
        securityDescription.CryptoSuite = cryptoSuite;
        securityDescription.KeyParams = keyParams;

        if (sCryptoPartsCount > 3)
        {
            var sessionParamSpan = sCryptoValue[sCryptoParts[3]];

            if (!sessionParamSpan.IsEmpty)
            {
                if (!SessionParameter.TryParse(sessionParamSpan, out var sessionParam, cryptoSuite))
                {
                    securityDescription = null;
                    return false;
                }
                securityDescription.SessionParam = sessionParam;
            }
        }

        return true;
    }
    /// <summary>
    /// Determines whether the specified SDPSecurityDescription is equal to the current SDPSecurityDescription.
    /// Equality is based on comparing the individual fields that make up the security description.
    /// </summary>
    /// <param name="other">The SDPSecurityDescription to compare with the current instance.</param>
    /// <returns>true if the specified SDPSecurityDescription is equal to the current instance; otherwise, false.</returns>
    public bool Equals(SDPSecurityDescription? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        // Compare core properties
        if (Tag != other.Tag || CryptoSuite != other.CryptoSuite)
        {
            return false;
        }

        // Compare KeyParams collections
        if (KeyParams.Count != other.KeyParams.Count)
        {
            return false;
        }

        for (var i = 0; i < KeyParams.Count; i++)
        {
            if (!AreKeyParametersEqual(KeyParams[i], other.KeyParams[i]))
            {
                return false;
            }
        }

        // Compare SessionParam (using null-safe comparison)
        return AreSessionParametersEqual(SessionParam, other.SessionParam);

        static bool AreKeyParametersEqual(KeyParameter left, KeyParameter right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            return left.Key.SequenceEqual(right.Key) &&
                   left.Salt.SequenceEqual(right.Salt) &&
                   left.LifeTime == right.LifeTime &&
                   string.Equals(left.LifeTimeString, right.LifeTimeString, StringComparison.Ordinal) &&
                   left.MkiValue == right.MkiValue &&
                   left.MkiLength == right.MkiLength;
        }

        static bool AreSessionParametersEqual(SessionParameter? left, SessionParameter? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left is null || right is null)
            {
                return false;
            }

            if (left.SrtpSessionParam != right.SrtpSessionParam)
            {
                return false;
            }

            return left.SrtpSessionParam switch
            {
                SessionParameter.SrtpSessionParams.kdr => left.Kdr == right.Kdr,
                SessionParameter.SrtpSessionParams.wsh => left.Wsh == right.Wsh,
                SessionParameter.SrtpSessionParams.fec_order => left.FecOrder == right.FecOrder,
                SessionParameter.SrtpSessionParams.fec_key => AreKeyParametersEqual(left.FecKey!, right.FecKey!),
                _ => true // For simple enum-only parameters
            };
        }
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current SDPSecurityDescription.
    /// </summary>
    /// <param name="obj">The object to compare with the current instance.</param>
    /// <returns>true if the specified object is equal to the current instance; otherwise, false.</returns>
    public override bool Equals(object? obj)
    {
        return Equals(obj as SDPSecurityDescription);
    }

    /// <summary>
    /// Returns a hash code for the current SDPSecurityDescription.
    /// The hash code is based on the individual fields that make up the security description.
    /// </summary>
    /// <returns>A hash code for the current instance.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Tag);
        hash.Add(CryptoSuite);

        // Add each KeyParameter's hash contribution
        foreach (var keyParam in KeyParams)
        {
            var keyParamHash = new HashCode();

            // Hash the key bytes
            foreach (var b in keyParam.Key)
            {
                keyParamHash.Add(b);
            }

            // Hash the salt bytes
            foreach (var b in keyParam.Salt)
            {
                keyParamHash.Add(b);
            }

            keyParamHash.Add(keyParam.LifeTime);
            keyParamHash.Add(keyParam.LifeTimeString);
            keyParamHash.Add(keyParam.MkiValue);
            keyParamHash.Add(keyParam.MkiLength);

            hash.Add(keyParamHash.ToHashCode());
        }

        // Add SessionParam hash if present
        if (SessionParam is not null)
        {
            var sessionParamHash = new HashCode();
            sessionParamHash.Add(SessionParam.SrtpSessionParam);

            switch (SessionParam.SrtpSessionParam)
            {
                case SessionParameter.SrtpSessionParams.kdr:
                    sessionParamHash.Add(SessionParam.Kdr);
                    break;
                case SessionParameter.SrtpSessionParams.wsh:
                    sessionParamHash.Add(SessionParam.Wsh);
                    break;
                case SessionParameter.SrtpSessionParams.fec_order:
                    sessionParamHash.Add(SessionParam.FecOrder);
                    break;
                case SessionParameter.SrtpSessionParams.fec_key:
                    if (SessionParam.FecKey is not null)
                    {
                        var fecKeyHash = new HashCode();

                        // Hash the FecKey bytes
                        foreach (var b in SessionParam.FecKey.Key)
                        {
                            fecKeyHash.Add(b);
                        }

                        // Hash the FecKey salt bytes
                        foreach (var b in SessionParam.FecKey.Salt)
                        {
                            fecKeyHash.Add(b);
                        }

                        fecKeyHash.Add(SessionParam.FecKey.LifeTime);
                        fecKeyHash.Add(SessionParam.FecKey.LifeTimeString);
                        fecKeyHash.Add(SessionParam.FecKey.MkiValue);
                        fecKeyHash.Add(SessionParam.FecKey.MkiLength);

                        sessionParamHash.Add(fecKeyHash.ToHashCode());
                    }
                    break;
            }

            hash.Add(sessionParamHash.ToHashCode());
        }

        return hash.ToHashCode();
    }
}
