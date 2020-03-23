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
using System.Linq;
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
                    if (value == null)
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
                    if (value == null)
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
                    if (value < 1)
                    {
                        throw new ArgumentOutOfRangeException("LifeTime", "LifeTime value must be power of 2");
                    }

                    ulong ul = value;
                    int i = 0;
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
            private string m_sLifeTime = null;
            public string LifeTimeString
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
                if (!string.IsNullOrWhiteSpace(keyParamString))
                {
                    string p = keyParamString.Trim();
                    try
                    {
                        if (p.StartsWith(KEY_METHOD))
                        {
                            string sKeyMethod = KEY_METHOD;
                            int poscln = p.IndexOf(COLON);
                            if (poscln == sKeyMethod.Length)
                            {
                                string sKeyInfo = p.Substring(poscln + 1);
                                if (!sKeyInfo.Contains(";"))
                                {
                                    string sMkiVal, sMkiLen, sLifeTime, sBase64KeySalt;
                                    checkValidKeyInfoCharacters(keyParamString, sKeyInfo);
                                    parseKeyInfo(keyParamString, sKeyInfo, out sMkiVal, out sMkiLen, out sLifeTime, out sBase64KeySalt);
                                    if (!string.IsNullOrWhiteSpace(sBase64KeySalt))
                                    {
                                        byte[] bKey, bSalt;
                                        parseKeySaltBase64(cryptoSuite, sBase64KeySalt, out bKey, out bSalt);

                                        KeyParameter kp = new KeyParameter(bKey, bSalt);
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

            private static void parseKeySaltBase64(CryptoSuites cryptoSuite, string base64KeySalt, out byte[] key, out byte[] salt)
            {
                byte[] keysalt = Convert.FromBase64String(base64KeySalt);
                key = null;
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
                }
                salt = null;
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
                }
            }

            private static void checkValidKeyInfoCharacters(string keyParameter, string keyInfo)
            {
                foreach (char c in keyInfo.ToCharArray())
                {
                    if (c < 0x21 || c > 0x7e)
                    {
                        throw new FormatException($"keyParameter '{keyParameter}' is not recognized as a valid KEY_INFO ");
                    }
                }
            }

            public static bool TryParse(string keyParamString, out KeyParameter keyParam, CryptoSuites cryptoSuite = CryptoSuites.AES_CM_128_HMAC_SHA1_80)
            {
                keyParam = null;

                if (!string.IsNullOrWhiteSpace(keyParamString))
                {
                    string p = keyParamString.Trim();
                    try
                    {
                        if (p.StartsWith(KEY_METHOD))
                        {
                            string sKeyMethod = KEY_METHOD;
                            int poscln = p.IndexOf(COLON);
                            if (poscln == sKeyMethod.Length)
                            {
                                string sKeyInfo = p.Substring(poscln + 1);
                                if (!sKeyInfo.Contains(";"))
                                {
                                    checkValidKeyInfoCharacters(keyParamString, sKeyInfo);
                                    string sMkiVal, sMkiLen, sLifeTime, sBase64KeySalt;
                                    parseKeyInfo(keyParamString, sKeyInfo, out sMkiVal, out sMkiLen, out sLifeTime, out sBase64KeySalt);
                                    if (!string.IsNullOrWhiteSpace(sBase64KeySalt))
                                    {
                                        byte[] bKey, bSalt;
                                        parseKeySaltBase64(cryptoSuite, sBase64KeySalt, out bKey, out bSalt);

                                        keyParam = new KeyParameter(bKey, bSalt);
                                        if (!string.IsNullOrWhiteSpace(sMkiVal) && !string.IsNullOrWhiteSpace(sMkiLen))
                                        {
                                            keyParam.MkiValue = uint.Parse(sMkiVal);
                                            keyParam.MkiLength = uint.Parse(sMkiLen);
                                        }
                                        if (!string.IsNullOrWhiteSpace(sLifeTime))
                                        {
                                            if (sLifeTime.Contains('^'))
                                            {
                                                keyParam.LifeTimeString = sLifeTime;
                                            }
                                            else
                                            {
                                                keyParam.LifeTime = uint.Parse(sLifeTime);
                                            }
                                        }
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
                return false;
            }

            private static void parseKeyInfo(string keyParamString, string keyInfo, out string mkiValue, out string mkiLen, out string lifeTimeString, out string base64KeySalt)
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
                        throw new FormatException($"keyParameter '{keyParamString}' is not recognized as a valid SRTP_KEY_INFO ");
                    }
                }
                else
                {
                    base64KeySalt = keyInfo;
                }
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
                if (string.IsNullOrWhiteSpace(sessionParam))
                {
                    return null;
                }

                string p = sessionParam.Trim();
                try
                {
                    SessionParameter.SrtpSessionParams paramType = SrtpSessionParams.unknown;
                    if (p.StartsWith(KDR_PREFIX))
                    {
                        string sKdr = p.Substring(KDR_PREFIX.Length);
                        uint kdr = 0;
                        if (uint.TryParse(sKdr, out kdr))
                        {
                            return new SessionParameter(SrtpSessionParams.kdr) { Kdr = kdr };
                        }
                    }
                    else if (p.StartsWith(WSH_PREFIX))
                    {
                        string sWsh = p.Substring(WSH_PREFIX.Length);
                        uint wsh = 0;
                        if (uint.TryParse(sWsh, out wsh))
                        {
                            return new SessionParameter(SrtpSessionParams.wsh) { Wsh = wsh };
                        }
                    }
                    else if (p.StartsWith(FEC_KEY_PREFIX))
                    {
                        string sFecKey = p.Substring(FEC_KEY_PREFIX.Length);
                        KeyParameter fecKey = KeyParameter.Parse(sFecKey, cryptoSuite);
                        return new SessionParameter(SrtpSessionParams.fec_key) { FecKey = fecKey };
                    }
                    else if (p.StartsWith(FEC_ORDER_PREFIX))
                    {
                        string sFecOrder = p.Substring(FEC_ORDER_PREFIX.Length);
                        SessionParameter.FecTypes fecOrder = (from e in Enum.GetNames(typeof(FecTypes)) where e.CompareTo(sFecOrder) == 0 select (FecTypes)Enum.Parse(typeof(FecTypes), e)).FirstOrDefault();
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
            if (string.IsNullOrWhiteSpace(cryptoLine))
            {
                return null;
            }

            if (!cryptoLine.StartsWith(CRYPTO_ATTRIBUE_PREFIX))
            {
                throw new FormatException($"cryptoLine '{cryptoLine}' is not recognized as a valid SDP Security Description ");
            }

            string sCryptoValue = cryptoLine.Substring(cryptoLine.IndexOf(COLON) + 1);

            SDPSecurityDescription sdpCryptoAttribute = new SDPSecurityDescription();
            string[] sCryptoParts = sCryptoValue.Split(sdpCryptoAttribute.WHITE_SPACES, StringSplitOptions.RemoveEmptyEntries);
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

                string[] sKeyParams = sCryptoParts[2].Split(SEMI_COLON);
                if (sKeyParams.Length < 1)
                {
                    throw new FormatException($"cryptoLine '{cryptoLine}' is not recognized as a valid SDP Security Description ");
                }

                foreach (string kp in sKeyParams)
                {
                    KeyParameter keyParam = KeyParameter.Parse(kp, sdpCryptoAttribute.CryptoSuite);
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

        public static bool TryParse(string cryptoLine, out SDPSecurityDescription securityDescription)
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

            string sCryptoValue = cryptoLine.Substring(cryptoLine.IndexOf(COLON) + 1);

            securityDescription = new SDPSecurityDescription();
            string[] sCryptoParts = sCryptoValue.Split(securityDescription.WHITE_SPACES, StringSplitOptions.RemoveEmptyEntries);
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

                string[] sKeyParams = sCryptoParts[2].Split(SEMI_COLON);
                if (sKeyParams.Length < 1)
                {
                    securityDescription = null;
                    return false;
                }
                foreach (string kp in sKeyParams)
                {
                    KeyParameter keyParam = KeyParameter.Parse(kp, securityDescription.CryptoSuite);
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
    }
}
