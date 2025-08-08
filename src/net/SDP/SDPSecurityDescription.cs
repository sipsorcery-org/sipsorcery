//-----------------------------------------------------------------------------
// Filename: SDP.cs
//
// Description: (SDP) Security Descriptions for Media Streams implementation as basically defined in RFC 4568.
// https://tools.ietf.org/html/rfc4568
//
// Author(s):
// rj2

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using SIPSorcery.Sys;

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
    public class SDPSecurityDescription : IEquatable<SDPSecurityDescription>
    {
        public const string CRYPTO_ATTRIBUTE_NAME = "crypto";
        public const string CRYPTO_ATTRIBUE_PREFIX = "a=" + "crypto" + ":";
        private readonly char[] WHITE_SPACES = new char[] { ' ', '\t' };
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
        public class KeyParameter
        {
            private const string COLON = ":";
            private const string PIPE = "|";
            public const string KEY_METHOD = "inline";
            private byte[]? m_key;
            //128 bit for AES_CM_128_HMAC_SHA1_80, AES_CM_128_HMAC_SHA1_32, F8_128_HMAC_SHA1_80, AEAD_AES_128_GCM
            //192 bit for AES_192_CM_HMAC_SHA1_80, AES_192_CM_HMAC_SHA1_32
            //256 bit for AEAD_AES_256_GCM, AES_256_CM_HMAC_SHA1_80, AES_256_CM_HMAC_SHA1_32 
            //
            public byte[] Key
            {
                get
                {
                    Debug.Assert(this.m_key is { Length: >= 16 });
                    return this.m_key;
                }
                set
                {
                    if (value is null)
                    {
                        throw new ArgumentNullException("Key", "Key must have a value");
                    }

                    if (value.Length < 16)
                    {
                        throw new ArgumentOutOfRangeException("Key", "Key must be at least 16 characters long");
                    }

                    this.m_key = value;
                }
            }
            private byte[]? m_salt;
            //112 bit for AES_CM_128_HMAC_SHA1_80, AES_CM_128_HMAC_SHA1_32, F8_128_HMAC_SHA1_80
            //112 bit for AES_192_CM_HMAC_SHA1_80,AES_192_CM_HMAC_SHA1_32 , AES_256_CM_HMAC_SHA1_80, AES_256_CM_HMAC_SHA1_32 
            //96 bit for AEAD_AES_128_GCM
            //
            public byte[] Salt
            {
                get
                {
                    Debug.Assert(this.m_salt is { Length: >= 12 });
                    return this.m_salt;
                }
                set
                {
                    if (value is null)
                    {
                        throw new ArgumentNullException("Salt", "Salt must have a value");
                    }

                    if (value.Length < 12)
                    {
                        throw new ArgumentOutOfRangeException("Salt", "Salt must be at least 12 characters long");
                    }

                    this.m_salt = value;
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
            private ulong m_lifeTime;
            public ulong LifeTime
            {
                get
                {
                    return this.m_lifeTime;
                }
                set
                {
                    if (value < 1)
                    {
                        throw new ArgumentOutOfRangeException("LifeTime", "LifeTime value must be power of 2");
                    }

                    var ul = value;
                    var i = 0;
                    for (; i < 64; i++)
                    {
                        if ((ul & 0x1) == 0x1)
                        {
                            if (i == 0)//2^0 wollen wir nicht
                            {
                                throw new ArgumentOutOfRangeException("LifeTime", "LifeTime value must be power of 2");
                            }
                            else
                            {
                                ul = ul >> 1;
                                break;
                            }
                        }
                        else
                        {
                            ul = ul >> 1;
                        }
                    }
                    if (ul == 0)
                    {
                        this.m_lifeTime = value;
                        this.m_sLifeTime = $"2^{i}";
                    }
                    else
                    {
                        throw new ArgumentOutOfRangeException("LifeTime", "LifeTime value must be power of 2");
                    }
                }
            }
            private string? m_sLifeTime;
            public string? LifeTimeString
            {
                get
                {
                    return this.m_sLifeTime;
                }
                set
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        throw new ArgumentNullException("LifeTimeString", "LifeTimeString value must be power of 2 string");
                    }

                    if (!value.StartsWith("2^"))
                    {
                        throw new ArgumentException("LifeTimeString must begin with 2^", "LifeTimeString");
                    }

                    double d = ulong.Parse(value.Substring(2)); //let .net throw an exception if given value is not a number
                    if (d < 1)
                    {
                        throw new ArgumentOutOfRangeException("LifeTimeString", "LifeTimeString value must be power of 2");
                    }

                    this.m_lifeTime = (ulong)Math.Pow(2, d);
                    this.m_sLifeTime = $"2^{(ulong)d}";
                }
            }
            public uint MkiValue
            {
                get;
                set;
            }
            private uint m_mkiLength;
            public uint MkiLength
            {
                get
                {
                    return this.m_mkiLength;
                }
                set
                {
                    if (value is > 0 and <= 128)
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

            public static KeyParameter Parse(string keyParamString, CryptoSuites cryptoSuite = CryptoSuites.AES_CM_128_HMAC_SHA1_80)
            {
                if (!string.IsNullOrWhiteSpace(keyParamString))
                {
                    var p = keyParamString.Trim();
                    try
                    {
                        if (p.StartsWith(KEY_METHOD))
                        {
                            var sKeyMethod = KEY_METHOD;
                            var poscln = p.IndexOf(COLON);
                            if (poscln == sKeyMethod.Length)
                            {
                                var sKeyInfo = p.Substring(poscln + 1);
                                if (!sKeyInfo.Contains(';'))
                                {
                                    checkValidKeyInfoCharacters(keyParamString, sKeyInfo);
                                    parseKeyInfo(keyParamString, sKeyInfo, out var sMkiVal, out var sMkiLen, out var sLifeTime, out var sBase64KeySalt);
                                    if (!string.IsNullOrWhiteSpace(sBase64KeySalt))
                                    {
                                        parseKeySaltBase64(cryptoSuite, sBase64KeySalt, out var bKey, out var bSalt);
                                        Debug.Assert(bKey is { });
                                        Debug.Assert(bSalt is { });

                                        var kp = new KeyParameter(bKey, bSalt);
                                        if (!string.IsNullOrWhiteSpace(sMkiVal) && !string.IsNullOrWhiteSpace(sMkiLen))
                                        {
                                            kp.MkiValue = uint.Parse(sMkiVal);
                                            kp.MkiLength = uint.Parse(sMkiLen);
                                        }
                                        if (!string.IsNullOrWhiteSpace(sLifeTime))
                                        {
                                            if (sLifeTime.Contains('^'))
                                            {
                                                kp.LifeTimeString = sLifeTime;
                                            }
                                            else
                                            {
                                                kp.LifeTime = uint.Parse(sLifeTime);
                                            }
                                        }
                                        return kp;
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        //catch all errors and throw own FormatException
                    }
                }
                throw new FormatException($"keyParam '{keyParamString}' is not recognized as a valid KEY_PARAM ");
            }

            private static void parseKeySaltBase64(
                CryptoSuites cryptoSuite,
                string base64KeySalt,
                out byte[]? key,
                out byte[]? salt)
            {
                var keysalt = Convert.FromBase64String(base64KeySalt);
                switch (cryptoSuite)
                {
                    case CryptoSuites.AES_CM_128_HMAC_SHA1_32:
                    case CryptoSuites.AES_CM_128_HMAC_SHA1_80:
                    case CryptoSuites.F8_128_HMAC_SHA1_80:
                    case CryptoSuites.AEAD_AES_128_GCM:
                        key = new byte[128 / 8];
                        Array.Copy(keysalt, 0, key, 0, 128 / 8);
                        break;
                    case CryptoSuites.AES_192_CM_HMAC_SHA1_80:
                    case CryptoSuites.AES_192_CM_HMAC_SHA1_32:
                    case CryptoSuites.AES_CM_192_HMAC_SHA1_80:
                    case CryptoSuites.AES_CM_192_HMAC_SHA1_32:
                        key = new byte[192 / 8];
                        Array.Copy(keysalt, 0, key, 0, 192 / 8);
                        break;
                    case CryptoSuites.AEAD_AES_256_GCM:
                    case CryptoSuites.AES_256_CM_HMAC_SHA1_80:
                    case CryptoSuites.AES_256_CM_HMAC_SHA1_32:
                    case CryptoSuites.AES_CM_256_HMAC_SHA1_80:
                    case CryptoSuites.AES_CM_256_HMAC_SHA1_32:
                        key = new byte[256 / 8];
                        Array.Copy(keysalt, 0, key, 0, 256 / 8);
                        break;
                    default:
                        key = null;
                        break;
                }

                switch (cryptoSuite)
                {
                    case CryptoSuites.AES_CM_128_HMAC_SHA1_32:
                    case CryptoSuites.AES_CM_128_HMAC_SHA1_80:
                    case CryptoSuites.F8_128_HMAC_SHA1_80:
                        salt = new byte[112 / 8];
                        Array.Copy(keysalt, 128 / 8, salt, 0, 112 / 8);
                        break;
                    case CryptoSuites.AES_192_CM_HMAC_SHA1_80:
                    case CryptoSuites.AES_192_CM_HMAC_SHA1_32:
                    case CryptoSuites.AES_CM_192_HMAC_SHA1_80:
                    case CryptoSuites.AES_CM_192_HMAC_SHA1_32:
                        salt = new byte[112 / 8];
                        Array.Copy(keysalt, 192 / 8, salt, 0, 112 / 8);
                        break;
                    case CryptoSuites.AES_256_CM_HMAC_SHA1_80:
                    case CryptoSuites.AES_256_CM_HMAC_SHA1_32:
                    case CryptoSuites.AES_CM_256_HMAC_SHA1_80:
                    case CryptoSuites.AES_CM_256_HMAC_SHA1_32:
                        salt = new byte[256 / 8];
                        Array.Copy(keysalt, 256 / 8, salt, 0, 112 / 8);
                        break;
                    case CryptoSuites.AEAD_AES_256_GCM:
                    case CryptoSuites.AEAD_AES_128_GCM:
                        salt = new byte[96 / 8];
                        Array.Copy(keysalt, 128 / 8, salt, 0, 96 / 8);
                        break;
                    default:
                        salt = null;
                        break;
                }
            }

            private static void checkValidKeyInfoCharacters(string keyParameter, string keyInfo)
            {
                foreach (var c in keyInfo.AsSpan())
                {
                    if (c is < (char)0x21 or > (char)0x7e)
                    {
                        throw new FormatException($"keyParameter '{keyParameter}' is not recognized as a valid KEY_INFO ");
                    }
                }
            }

            private static void parseKeyInfo(string keyParamString, string keyInfo, out string? mkiValue, out string? mkiLen, out string? lifeTimeString, out string? base64KeySalt)
            {
                mkiValue = null;
                mkiLen = null;
                lifeTimeString = null;
                base64KeySalt = null;
                //KeyInfo must only contain visible printing characters
                //and 40 char long, as its is the base64representation of concatenated Key and Salt
                var pospipe1 = keyInfo.IndexOf(PIPE);
                if (pospipe1 > 0)
                {
                    base64KeySalt = keyInfo.Substring(0, pospipe1);
                    //find lifetime and mki
                    //both may be omitted, but mki is recognized by a colon
                    //usually lifetime comes before mki, if specified
                    var posclnmki = keyInfo.IndexOf(COLON, pospipe1 + 1);
                    var pospipe2 = keyInfo.IndexOf(PIPE, pospipe1 + 1);
                    if (posclnmki > 0 && pospipe2 < 0)
                    {
                        mkiValue = keyInfo.Substring(pospipe1 + 1, posclnmki - pospipe1 - 1);
                        mkiLen = keyInfo.Substring(posclnmki + 1);
                    }
                    else if (posclnmki > 0 && pospipe2 < posclnmki)
                    {
                        lifeTimeString = keyInfo.Substring(pospipe1 + 1, pospipe2 - pospipe1 - 1);
                        mkiValue = keyInfo.Substring(pospipe2 + 1, posclnmki - pospipe2 - 1);
                        mkiLen = keyInfo.Substring(posclnmki + 1);
                    }
                    else if (posclnmki > 0 && pospipe2 > posclnmki)
                    {
                        mkiValue = keyInfo.Substring(pospipe1 + 1, posclnmki - pospipe1 - 1);
                        mkiLen = keyInfo.Substring(posclnmki + 1, pospipe2 - posclnmki - 1);
                        lifeTimeString = keyInfo.Substring(pospipe2 + 1);
                    }
                    else if (posclnmki < 0 && pospipe2 < 0)
                    {
                        lifeTimeString = keyInfo.Substring(pospipe1 + 1);
                    }
                    else if (posclnmki < 0 && pospipe2 > 0)
                    {
                        throw new FormatException($"keyParameter '{keyParamString}' is not recognized as a valid SRTP_KEY_INFO ");
                    }
                }
                else
                {
                    base64KeySalt = keyInfo;
                }
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
            private ulong m_kdr;
            public ulong Kdr
            {
                get
                {
                    return this.m_kdr;
                }
                set
                {
                    /*if(value < 1 || value > Math.Pow(2, 24))
                        throw new ArgumentOutOfRangeException("Kdr", "Kdr must be power of 2 and less than 2^24");
                    ulong ul = value;
                    for(int i = 0; i < 64; i++)
                    {
                        if((ul & 0x1) == 0x1)
                        {
                            if(i == 0)//2^0 wollen wir nicht
                                throw new ArgumentOutOfRangeException("Kdr", "Kdr must be power of 2 and less than 2^24");
                            else
                            {
                                ul = ul >> 1;
                                break;
                            }
                        }
                        else
                        {
                            ul = ul >> 1;
                        }
                    }
                    if(ul == 0)
                        this.m_kdr = value;
                    else
                        throw new ArgumentOutOfRangeException("Kdr", "Kdr must be power of 2 and less than 2^24");
                    */
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

            public static SessionParameter? Parse(string sessionParam, CryptoSuites cryptoSuite = CryptoSuites.AES_CM_128_HMAC_SHA1_80)
            {
                if (string.IsNullOrWhiteSpace(sessionParam))
                {
                    return null;
                }

                var p = sessionParam.Trim();
                try
                {
                    var paramType = SrtpSessionParams.unknown;
                    if (p.StartsWith(KDR_PREFIX))
                    {
                        var sKdr = p.Substring(KDR_PREFIX.Length);
                        uint kdr = 0;
                        if (uint.TryParse(sKdr, out kdr))
                        {
                            return new SessionParameter(SrtpSessionParams.kdr) { Kdr = kdr };
                        }
                    }
                    else if (p.StartsWith(WSH_PREFIX))
                    {
                        var sWsh = p.Substring(WSH_PREFIX.Length);
                        uint wsh = 0;
                        if (uint.TryParse(sWsh, out wsh))
                        {
                            return new SessionParameter(SrtpSessionParams.wsh) { Wsh = wsh };
                        }
                    }
                    else if (p.StartsWith(FEC_KEY_PREFIX))
                    {
                        var sFecKey = p.Substring(FEC_KEY_PREFIX.Length);
                        var fecKey = KeyParameter.Parse(sFecKey, cryptoSuite);
                        return new SessionParameter(SrtpSessionParams.fec_key) { FecKey = fecKey };
                    }
                    else if (p.StartsWith(FEC_ORDER_PREFIX))
                    {
                        var sFecOrder = p.Substring(FEC_ORDER_PREFIX.Length);
                        var fecOrder = (from e in Enum.GetNames(typeof(FecTypes)) where e.CompareTo(sFecOrder) == 0 select (FecTypes)Enum.Parse(typeof(FecTypes), e)).FirstOrDefault();
                        if (fecOrder == FecTypes.unknown)
                        {
                            throw new FormatException($"sessionParam '{sessionParam}' is not recognized as a valid SRTP_SESSION_PARAM ");
                        }

                        return new SessionParameter(SrtpSessionParams.fec_order) { FecOrder = fecOrder };
                    }
                    else
                    {
                        paramType = (from e in Enum.GetNames(typeof(SrtpSessionParams)) where e.CompareTo(p) == 0 select (SrtpSessionParams)Enum.Parse(typeof(SrtpSessionParams), e)).FirstOrDefault();
                        if (paramType == SrtpSessionParams.unknown)
                        {
                            throw new FormatException($"sessionParam '{sessionParam}' is not recognized as a valid SRTP_SESSION_PARAM ");
                        }

                        switch (paramType)
                        {
                            case SrtpSessionParams.UNAUTHENTICATED_SRTP:
                            case SrtpSessionParams.UNENCRYPTED_SRTCP:
                            case SrtpSessionParams.UNENCRYPTED_SRTP:
                                return new SessionParameter(paramType);
                        }
                    }
                }
                catch
                {
                    //catch all errors and throw own FormatException
                }

                throw new FormatException($"sessionParam '{sessionParam}' is not recognized as a valid SRTP_SESSION_PARAM ");
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

        public static SDPSecurityDescription? Parse(string cryptoLine)
        {
            if (string.IsNullOrWhiteSpace(cryptoLine))
            {
                return null;
            }

            if (!cryptoLine.StartsWith(CRYPTO_ATTRIBUE_PREFIX))
            {
                throw new FormatException($"cryptoLine '{cryptoLine}' is not recognized as a valid SDP Security Description ");
            }

            var sCryptoValue = cryptoLine.Substring(cryptoLine.IndexOf(COLON) + 1);

            var sdpCryptoAttribute = new SDPSecurityDescription();
            var sCryptoParts = sCryptoValue.Split(sdpCryptoAttribute.WHITE_SPACES, StringSplitOptions.RemoveEmptyEntries);
            if (sCryptoValue.Length < 2)
            {
                throw new FormatException($"cryptoLine '{cryptoLine}' is not recognized as a valid SDP Security Description ");
            }

            try
            {
                sdpCryptoAttribute.Tag = uint.Parse(sCryptoParts[0]);
                sdpCryptoAttribute.CryptoSuite = (from e in Enum.GetNames(typeof(CryptoSuites)) where e.CompareTo(sCryptoParts[1]) == 0 select (CryptoSuites)Enum.Parse(typeof(CryptoSuites), e)).FirstOrDefault();

                if (sdpCryptoAttribute.CryptoSuite == CryptoSuites.unknown)
                {
                    throw new FormatException($"cryptoLine '{cryptoLine}' is not recognized as a valid SDP Security Description ");
                }

                var sKeyParams = sCryptoParts[2].Split(SEMI_COLON);
                if (sKeyParams.Length < 1)
                {
                    throw new FormatException($"cryptoLine '{cryptoLine}' is not recognized as a valid SDP Security Description ");
                }

                foreach (var kp in sKeyParams)
                {
                    var keyParam = KeyParameter.Parse(kp, sdpCryptoAttribute.CryptoSuite);
                    sdpCryptoAttribute.KeyParams.Add(keyParam);
                }
                if (sCryptoParts.Length > 3)
                {
                    sdpCryptoAttribute.SessionParam = SessionParameter.Parse(sCryptoParts[3], sdpCryptoAttribute.CryptoSuite);
                }

                return sdpCryptoAttribute;
            }
            catch
            {
                //catch all errors and throw own FormatException
            }
            throw new FormatException($"cryptoLine '{cryptoLine}' is not recognized as a valid SDP Security Description ");
        }

        public static bool TryParse(string cryptoLine, [NotNullWhen(true)] out SDPSecurityDescription? securityDescription)
        {
            securityDescription = null;
            if (string.IsNullOrWhiteSpace(cryptoLine))
            {
                return false;
            }

            if (!cryptoLine.StartsWith(CRYPTO_ATTRIBUE_PREFIX))
            {
                throw new FormatException($"cryptoLine '{cryptoLine}' is not recognized as a valid SDP Security Description ");
            }

            var sCryptoValue = cryptoLine.Substring(cryptoLine.IndexOf(COLON) + 1);

            securityDescription = new SDPSecurityDescription();
            var sCryptoParts = sCryptoValue.Split(securityDescription.WHITE_SPACES, StringSplitOptions.RemoveEmptyEntries);
            if (sCryptoValue.Length < 2)
            {
                throw new FormatException($"cryptoLine '{cryptoLine}' is not recognized as a valid SDP Security Description ");
            }

            try
            {
                securityDescription.Tag = uint.Parse(sCryptoParts[0]);
                securityDescription.CryptoSuite = (from e in Enum.GetNames(typeof(CryptoSuites)) where e.CompareTo(sCryptoParts[1]) == 0 select (CryptoSuites)Enum.Parse(typeof(CryptoSuites), e)).FirstOrDefault();

                if (securityDescription.CryptoSuite == CryptoSuites.unknown)
                {
                    //this may not be a reason to return FALSE
                    //there might be a new crypto key used
                }

                var sKeyParams = sCryptoParts[2].Split(SEMI_COLON);
                if (sKeyParams.Length < 1)
                {
                    securityDescription = null;
                    return false;
                }
                foreach (var kp in sKeyParams)
                {
                    var keyParam = KeyParameter.Parse(kp, securityDescription.CryptoSuite);
                    securityDescription.KeyParams.Add(keyParam);
                }
                if (sCryptoParts.Length > 3)
                {
                    securityDescription.SessionParam = SessionParameter.Parse(sCryptoParts[3], securityDescription.CryptoSuite);
                }

                return true;
            }
            catch
            {
                //catch all errors and throw own FormatException
            }
            return false;
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
}
