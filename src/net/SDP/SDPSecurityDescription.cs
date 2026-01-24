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
using System.Text;

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
        public const string CRYPTO_ATTRIBUE_PREFIX = "a=crypto:";
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
        private static readonly Dictionary<string, CryptoSuites> s_cryptoSuiteLookup = CreateCryptoSuiteLookup();
        private static Dictionary<string, CryptoSuites> CreateCryptoSuiteLookup()
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
            return lookup;
        }
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
                    this.m_sLifeTime = $"2^{i}";
                }
            }
            private string m_sLifeTime = null;
            public string LifeTimeString
            {
                get
                {
                    return this.m_sLifeTime;
                }
                set
                {
                    if (!TryParseLifeTimeString(value, out ulong lifeTime))
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
                string s = KEY_METHOD + COLON + this.KeySaltBase64;
                if (!string.IsNullOrWhiteSpace(this.LifeTimeString))
                {
                    s += PIPE + this.LifeTimeString;
                }
                else if (this.LifeTime > 0)
                {
                    s += PIPE + this.LifeTime;
                }

                if (this.MkiLength > 0 && this.MkiValue > 0)
                {
                    s += PIPE + this.MkiValue + COLON + this.MkiLength;
                }

                return s;
            }

            public static KeyParameter Parse(string keyParamString, CryptoSuites cryptoSuite = CryptoSuites.AES_CM_128_HMAC_SHA1_80)
            {
                if (!TryParse(keyParamString, out var keyParam, cryptoSuite))
                {
                    throw new FormatException($"keyParam '{keyParamString}' is not recognized as a valid KEY_PARAM ");
                }
                return keyParam;
            }

            public static bool TryParse(string keyParamString, out KeyParameter keyParam, CryptoSuites cryptoSuite = CryptoSuites.AES_CM_128_HMAC_SHA1_80)
            {
                keyParam = null;

                if (!string.IsNullOrWhiteSpace(keyParamString))
                {
                    string p = keyParamString.Trim();
                    if (p.StartsWith(KEY_METHOD))
                    {
                        string sKeyMethod = KEY_METHOD;
                        int poscln = p.IndexOf(COLON);
                        if (poscln == sKeyMethod.Length)
                        {
                            string sKeyInfo = p.Substring(poscln + 1);
                            if (!sKeyInfo.Contains(";"))
                            {
                                if ((!checkValidKeyInfoCharacters(sKeyInfo))
                                    || (!parseKeyInfo(sKeyInfo, out var sMkiVal, out var sMkiLen, out var sLifeTime, out var sBase64KeySalt)))
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
                                    if (sLifeTime.Contains("^"))
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
                }

                keyParam = null;
                return false;
            }

            private static bool parseKeySaltBase64(CryptoSuites cryptoSuite, string base64KeySalt, out byte[] key, out byte[] salt)
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
                        saltOffset = 128 / 8;
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

            private static bool checkValidKeyInfoCharacters(string keyInfo)
            {
                foreach (char c in keyInfo)
                {
                    if (c < 0x21 || c > 0x7e)
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

            private static bool TryParseLifeTimeString(string lifeTimeString, out ulong lifeTime)
            {
                lifeTime = 0;

                if (string.IsNullOrWhiteSpace(lifeTimeString) || !lifeTimeString.StartsWith("2^"))
                {
                    return false;
                }

                string exponentPart = lifeTimeString.Substring(2);
                if (!ulong.TryParse(exponentPart, out ulong exponent) || exponent < 1)
                {
                    return false;
                }

                lifeTime = (ulong)Math.Pow(2, (double)exponent);
                return true;
            }

            private static bool parseKeyInfo(string keyInfo, out string mkiValue, out string mkiLen, out string lifeTimeString, out string base64KeySalt)
            {
                mkiValue = null;
                mkiLen = null;
                lifeTimeString = null;
                base64KeySalt = null;
                //KeyInfo must only contain visible printing characters
                //and 40 char long, as its is the base64representation of concatenated Key and Salt
                int pospipe1 = keyInfo.IndexOf(PIPE);
                if (pospipe1 > 0)
                {
                    base64KeySalt = keyInfo.Substring(0, pospipe1);
                    //find lifetime and mki
                    //both may be omitted, but mki is recognized by a colon
                    //usually lifetime comes before mki, if specified
                    int posclnmki = keyInfo.IndexOf(COLON, pospipe1 + 1);
                    int pospipe2 = keyInfo.IndexOf(PIPE, pospipe1 + 1);

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
                        return false;
                    }
                }
                else
                {
                    base64KeySalt = keyInfo;
                }

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

            private static readonly Dictionary<string, FecTypes> s_fecTypesLookup = CreateFecTypesLookup();
            private static Dictionary<string, FecTypes> CreateFecTypesLookup()
            {
                var values = Enum.GetValues(typeof(FecTypes));
                var lookup = new Dictionary<string, FecTypes>(values.Length - 1, StringComparer.Ordinal);
                foreach (FecTypes ft in values)
                {
                    if (ft != FecTypes.unknown)
                    {
                        lookup[ft.ToString()] = ft;
                    }
                }
                return lookup;
            }

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
                if (this.SrtpSessionParam == SrtpSessionParams.unknown)
                {
                    return "";
                }

                switch (this.SrtpSessionParam)
                {
                    case SrtpSessionParams.UNAUTHENTICATED_SRTP:
                    case SrtpSessionParams.UNENCRYPTED_SRTP:
                    case SrtpSessionParams.UNENCRYPTED_SRTCP:
                        return this.SrtpSessionParam.ToString();
                    case SrtpSessionParams.wsh:
                        return $"{WSH_PREFIX}{this.Wsh}";
                    case SrtpSessionParams.kdr:
                        return $"{KDR_PREFIX}{this.Kdr}";
                    case SrtpSessionParams.fec_order:
                        return $"{FEC_ORDER_PREFIX}{this.FecOrder.ToString()}";
                    case SrtpSessionParams.fec_key:
                        return $"{FEC_KEY_PREFIX}{this.FecKey?.ToString()}";
                }
                return "";
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
            {
                result = null;

                if (string.IsNullOrWhiteSpace(sessionParam))
                {
                    return true;
                }

                string p = sessionParam.Trim();
                SessionParameter.SrtpSessionParams paramType = SrtpSessionParams.unknown;
                if (p.StartsWith(KDR_PREFIX))
                {
                    string sKdr = p.Substring(KDR_PREFIX.Length);
                    if (uint.TryParse(sKdr, out uint kdr))
                    {
                        result = new SessionParameter(SrtpSessionParams.kdr) { Kdr = kdr };
                        return true;
                    }
                }
                else if (p.StartsWith(WSH_PREFIX))
                {
                    string sWsh = p.Substring(WSH_PREFIX.Length);
                    if (uint.TryParse(sWsh, out uint wsh))
                    {
                        result = new SessionParameter(SrtpSessionParams.wsh) { Wsh = wsh };
                        return true;
                    }
                }
                else if (p.StartsWith(FEC_KEY_PREFIX))
                {
                    string sFecKey = p.Substring(FEC_KEY_PREFIX.Length);
                    if (!KeyParameter.TryParse(sFecKey, out var fecKey, cryptoSuite))
                    {
                        return false;
                    }
                    result = new SessionParameter(SrtpSessionParams.fec_key) { FecKey = fecKey };
                    return true;
                }
                else if (p.StartsWith(FEC_ORDER_PREFIX))
                {
                    string sFecOrder = p.Substring(FEC_ORDER_PREFIX.Length);
                    if (!s_fecTypesLookup.TryGetValue(sFecOrder, out var fecOrder))
                    {
                        return false;
                    }

                    result = new SessionParameter(SrtpSessionParams.fec_order) { FecOrder = fecOrder };
                    return true;
                }
                else
                {
                    if (!Enum.TryParse<SrtpSessionParams>(p, out paramType) || paramType.ToString() != p)
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
            if (this.Tag < 1 || this.CryptoSuite == CryptoSuites.unknown || this.KeyParams.Count < 1)
            {
                return null;
            }

            string s = CRYPTO_ATTRIBUE_PREFIX + this.Tag + WHITE_SPACE + this.CryptoSuite.ToString() + WHITE_SPACE;
            for (int i = 0; i < this.KeyParams.Count; i++)
            {
                if (i > 0)
                {
                    s += SEMI_COLON;
                }

                s += this.KeyParams[i].ToString();
            }
            if (this.SessionParam != null)
            {
                s += WHITE_SPACE + this.SessionParam.ToString();
            }
            return s;
        }

        public static SDPSecurityDescription Parse(string cryptoLine)
        {
            if (!TryParse(cryptoLine, out var securityDescription))
            {
                throw new FormatException($"cryptoLine '{cryptoLine}' is not recognized as a valid SDP Security Description ");
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

            if (!cryptoLine.StartsWith(CRYPTO_ATTRIBUE_PREFIX))
            {
                return false;
            }

            string sCryptoValue = cryptoLine.Substring(cryptoLine.IndexOf(COLON) + 1);

            securityDescription = new SDPSecurityDescription();
            string[] sCryptoParts = sCryptoValue.Split(WHITE_SPACES, StringSplitOptions.RemoveEmptyEntries);
            if (sCryptoValue.Length < 2)
            {
                return false;
            }

            if (!uint.TryParse(sCryptoParts[0], out var tag))
            {
                return false;
            }
            securityDescription.Tag = tag;

            if (!s_cryptoSuiteLookup.TryGetValue(sCryptoParts[1], out var cryptoSuite))
            {
                return false;
            }
            securityDescription.CryptoSuite = cryptoSuite;

            if (sCryptoParts.Length < 3)
            {
                return false;
            }

            string[] sKeyParams = sCryptoParts[2].Split(SEMI_COLON);
            if (sKeyParams.Length < 1)
            {
                securityDescription = null;
                return false;
            }
            foreach (string kp in sKeyParams)
            {
                if (!KeyParameter.TryParse(kp, out var keyParam, securityDescription.CryptoSuite))
                {
                    securityDescription = null;
                    return false;
                }
                securityDescription.KeyParams.Add(keyParam);
            }
            if (sCryptoParts.Length > 3)
            {
                if (!SessionParameter.TryParse(sCryptoParts[3], out var sessionParam, securityDescription.CryptoSuite))
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
