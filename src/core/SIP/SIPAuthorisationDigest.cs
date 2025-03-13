//-----------------------------------------------------------------------------
// Filename: SIPAuthorisationDigest.cs
//
// Description: Implements Digest Authentication as defined in RFC2617.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com
// 
// History:
// 08 Sep 2005	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    public enum SIPAuthorisationHeadersEnum
    {
        Unknown = 0,
        Authorize = 1,
        ProxyAuthenticate = 2,
        ProxyAuthorization = 3,
        WWWAuthenticate = 4,
    }

    public enum DigestAlgorithmsEnum
    {
        MD5,
        SHA256,
        //SHA512
    }

    public class SIPAuthorisationDigest
    {
        public const string METHOD = "Digest";
        public const string QOP_AUTHENTICATION_VALUE = "auth";
        private const int NONCE_DEFAULT_COUNT = 1;
        private const string SHA256_ALGORITHM_ID = "SHA-256";

        private static ILogger logger = Log.Logger;

        private static char[] m_headerFieldRemoveChars = new char[] { ' ', '"', '\'' };

        public SIPAuthorisationHeadersEnum AuthorisationType { get; private set; } // This is the type of authorisation request received.
        public SIPAuthorisationHeadersEnum AuthorisationResponseType { get; private set; }      // If this is set it's the type of authorisation response to use otherwise use the same as the request (God knows why you need a different response header?!?)

        public string Realm;
        public string Username;
        public string Password;
        //public string DestinationURL;
        public string URI;
        public string Nonce;
        public string RequestType;
        public string Response;
        public string HA1;           // HTTP digest HA1. Contains the username, relam and password already hashed.

        public string Cnonce;        // Client nonce (used with WWW-Authenticate and qop=auth).
        public string Qop;           // Quality of Protection. Values permitted are auth (authentication) and auth-int (authentication with integrity protection).
        public int NonceCount = 0;  // Client nonce count.
        public string Opaque;

        public DigestAlgorithmsEnum DigestAlgorithm = DigestAlgorithmsEnum.MD5;

        public SIPAuthorisationDigest(DigestAlgorithmsEnum hashAlgorithm = DigestAlgorithmsEnum.MD5)
        {
            AuthorisationType = SIPAuthorisationHeadersEnum.ProxyAuthorization;
            DigestAlgorithm = hashAlgorithm;
        }

        public SIPAuthorisationDigest(SIPAuthorisationHeadersEnum authorisationType, DigestAlgorithmsEnum hashAlgorithm = DigestAlgorithmsEnum.MD5)
        {
            AuthorisationType = authorisationType;
            DigestAlgorithm = hashAlgorithm;
        }

        public static SIPAuthorisationDigest ParseAuthorisationDigest(SIPAuthorisationHeadersEnum authorisationType, string authorisationRequest)
        {
            SIPAuthorisationDigest authRequest = new SIPAuthorisationDigest(authorisationType);

            string noDigestHeader = Regex.Replace(authorisationRequest, $@"^\s*{METHOD}\s*", "", RegexOptions.IgnoreCase);
            string[] headerFields = noDigestHeader.Split(',');

            if (headerFields != null && headerFields.Length > 0)
            {
                foreach (string headerField in headerFields)
                {
                    int equalsIndex = headerField.IndexOf('=');

                    if (equalsIndex != -1 && equalsIndex < headerField.Length)
                    {
                        string headerName = headerField.Substring(0, equalsIndex).Trim();
                        string headerValue = headerField.Substring(equalsIndex + 1).Trim(m_headerFieldRemoveChars);

                        if (Regex.Match(headerName, "^" + AuthHeaders.AUTH_REALM_KEY + "$", RegexOptions.IgnoreCase).Success)
                        {
                            authRequest.Realm = headerValue;
                        }
                        else if (Regex.Match(headerName, "^" + AuthHeaders.AUTH_NONCE_KEY + "$", RegexOptions.IgnoreCase).Success)
                        {
                            authRequest.Nonce = headerValue;
                        }
                        else if (Regex.Match(headerName, "^" + AuthHeaders.AUTH_USERNAME_KEY + "$", RegexOptions.IgnoreCase).Success)
                        {
                            authRequest.Username = headerValue;
                        }
                        else if (Regex.Match(headerName, "^" + AuthHeaders.AUTH_RESPONSE_KEY + "$", RegexOptions.IgnoreCase).Success)
                        {
                            authRequest.Response = headerValue;
                        }
                        else if (Regex.Match(headerName, "^" + AuthHeaders.AUTH_URI_KEY + "$", RegexOptions.IgnoreCase).Success)
                        {
                            authRequest.URI = headerValue;
                        }
                        else if (Regex.Match(headerName, "^" + AuthHeaders.AUTH_CNONCE_KEY + "$", RegexOptions.IgnoreCase).Success)
                        {
                            authRequest.Cnonce = headerValue;
                        }
                        else if (Regex.Match(headerName, "^" + AuthHeaders.AUTH_NONCECOUNT_KEY + "$", RegexOptions.IgnoreCase).Success)
                        {
                            Int32.TryParse(headerValue, out authRequest.NonceCount);
                        }
                        else if (Regex.Match(headerName, "^" + AuthHeaders.AUTH_QOP_KEY + "$", RegexOptions.IgnoreCase).Success)
                        {
                            authRequest.Qop = headerValue.ToLower();
                        }
                        else if (Regex.Match(headerName, "^" + AuthHeaders.AUTH_OPAQUE_KEY + "$", RegexOptions.IgnoreCase).Success)
                        {
                            authRequest.Opaque = headerValue;
                        }
                        else if (Regex.Match(headerName, "^" + AuthHeaders.AUTH_ALGORITHM_KEY + "$", RegexOptions.IgnoreCase).Success)
                        {
                            //authRequest.Algorithhm = headerValue;

                            if (Enum.TryParse<DigestAlgorithmsEnum>(headerValue.Replace("-",""), true, out var alg))
                            {
                                authRequest.DigestAlgorithm = alg;
                            }
                            else
                            {
                                logger.LogWarning("SIPAuthorisationDigest did not recognised digest algorithm value of {DigestAlgorithms}, defaulting to {DigestAlgorithmsEnumMD5}.", headerValue, DigestAlgorithmsEnum.MD5);
                                authRequest.DigestAlgorithm = DigestAlgorithmsEnum.MD5;
                            }
                        }
                    }
                }
            }

            return authRequest;
        }

        public SIPAuthorisationDigest(
            SIPAuthorisationHeadersEnum authorisationType,
            string realm,
            string username,
            string password,
            string uri,
            string nonce,
            string request,
            DigestAlgorithmsEnum hashAlgorithm = DigestAlgorithmsEnum.MD5)
        {
            AuthorisationType = authorisationType;
            Realm = realm;
            Username = username;
            Password = password;
            URI = uri;
            Nonce = nonce;
            RequestType = request;

            DigestAlgorithm = hashAlgorithm;
        }

        public void SetCredentials(string username, string password, string uri, string method)
        {
            Username = username;
            Password = password;
            URI = uri;
            RequestType = method;
        }

        public void SetCredentials(string ha1, string uri, string method)
        {
            HA1 = ha1;
            URI = uri;
            RequestType = method;
        }

        public string GetDigest()
        {
            // Just to make things difficult For some authorisation requests the header changes when the authenticated response is generated.
            if (AuthorisationType == SIPAuthorisationHeadersEnum.ProxyAuthenticate)
            {
                AuthorisationResponseType = SIPAuthorisationHeadersEnum.ProxyAuthorization;
            }
            else if (AuthorisationType == SIPAuthorisationHeadersEnum.WWWAuthenticate)
            {
                AuthorisationResponseType = SIPAuthorisationHeadersEnum.Authorize;
            }

            // If the authorisation header has specified quality of protection equal to "auth" a client nonce needs to be supplied.
            string nonceCountStr = null;
            if (Qop == QOP_AUTHENTICATION_VALUE)
            {
                NonceCount = (NonceCount != 0) ? NonceCount : NONCE_DEFAULT_COUNT;
                nonceCountStr = GetPaddedNonceCount(NonceCount);

                if (Cnonce == null || Cnonce.Trim().Length == 0)
                {
                    Cnonce = Crypto.GetRandomInt().ToString();
                }
            }

            if (Nonce == null)
            {
                Nonce = Crypto.GetRandomString(12);
            }

            if (Password != null)
            {
                return HTTPDigest.DigestCalcResponse(
                    Username,
                    Realm,
                    Password,
                    URI,
                    Nonce,
                    nonceCountStr,
                    Cnonce,
                    Qop,
                    RequestType,
                    DigestAlgorithm);
            }
            else if (HA1 != null)
            {
                return HTTPDigest.DigestCalcResponse(
                    HA1,
                   URI,
                   Nonce,
                   nonceCountStr,
                   Cnonce,
                   Qop,
                   RequestType,
                   DigestAlgorithm);
            }
            else
            {
                throw new ApplicationException("SIP authorisation digest cannot be calculated. No password or HA1 available.");
            }
        }

        public override string ToString()
        {
            string authHeader = AuthHeaders.AUTH_DIGEST_KEY + " ";

            authHeader += (Username != null && Username.Trim().Length != 0) ? AuthHeaders.AUTH_USERNAME_KEY + "=\"" + Username + "\"" : null;
            authHeader += (authHeader.IndexOf('=') != -1) ? "," + AuthHeaders.AUTH_REALM_KEY + "=\"" + Realm + "\"" : AuthHeaders.AUTH_REALM_KEY + "=\"" + Realm + "\"";
            authHeader += (Nonce != null) ? "," + AuthHeaders.AUTH_NONCE_KEY + "=\"" + Nonce + "\"" : null;
            authHeader += (URI != null && URI.Trim().Length != 0) ? "," + AuthHeaders.AUTH_URI_KEY + "=\"" + URI + "\"" : null;
            authHeader += (Response != null && Response.Length != 0) ? "," + AuthHeaders.AUTH_RESPONSE_KEY + "=\"" + Response + "\"" : null;
            authHeader += (Cnonce != null) ? "," + AuthHeaders.AUTH_CNONCE_KEY + "=\"" + Cnonce + "\"" : null;
            authHeader += (NonceCount != 0) ? "," + AuthHeaders.AUTH_NONCECOUNT_KEY + "=" + GetPaddedNonceCount(NonceCount) : null;
            authHeader += (Qop != null) ? "," + AuthHeaders.AUTH_QOP_KEY + "=" + Qop : null;
            authHeader += (Opaque != null) ? "," + AuthHeaders.AUTH_OPAQUE_KEY + "=\"" + Opaque + "\"" : null;

            string algorithmID = (DigestAlgorithm == DigestAlgorithmsEnum.SHA256) ? SHA256_ALGORITHM_ID : DigestAlgorithm.ToString();
            authHeader += (Response != null) ? "," + AuthHeaders.AUTH_ALGORITHM_KEY + "=" + algorithmID : null;

            return authHeader;
        }

        public SIPAuthorisationDigest CopyOf()
        {
            var copy = new SIPAuthorisationDigest(AuthorisationType, Realm, Username, Password, URI, Nonce, RequestType, DigestAlgorithm);
            copy.Response = Response;
            copy.HA1 = HA1;
            copy.Cnonce = Cnonce;
            copy.Qop = Qop;
            copy.NonceCount = NonceCount;
            copy.Opaque = Opaque;

            return copy;
        }

        public void IncrementNonceCount()
        {
            NonceCount++;
        }

        private string GetPaddedNonceCount(int count)
        {
            return "00000000".Substring(0, 8 - NonceCount.ToString().Length) + count;
        }
    }

    public class HTTPDigest
    {
        /// <summary>
        /// Calculate H(A1) as per HTTP Digest specification.
        /// </summary>
        public static string DigestCalcHA1(
            string username,
            string realm,
            string password,
            DigestAlgorithmsEnum hashAlg = DigestAlgorithmsEnum.MD5)
        {
            string a1 = String.Format("{0}:{1}:{2}", username, realm, password);
            return GetHashHex(hashAlg, a1);
        }

        /// <summary>
        /// Calculate H(A2) as per HTTP Digest specification.
        /// </summary>
        public static string DigestCalcHA2(
            string method,
            string uri,
            DigestAlgorithmsEnum hashAlg = DigestAlgorithmsEnum.MD5)
        {
            string A2 = String.Format("{0}:{1}", method, uri);

            return GetHashHex(hashAlg, A2);
        }

        public static string DigestCalcResponse(
           string username,
           string realm,
           string password,
           string uri,
           string nonce,
           string nonceCount,
           string cnonce,
           string qop,         // qop-value: "", "auth", "auth-int".
           string method,
           DigestAlgorithmsEnum hashAlg = DigestAlgorithmsEnum.MD5)
        {
            string HA1 = DigestCalcHA1(username, realm, password, hashAlg);
            return DigestCalcResponse(HA1, uri, nonce, nonceCount, cnonce, qop, method, hashAlg);
        }

        public static string DigestCalcResponse(
            string ha1,
            string uri,
            string nonce,
            string nonceCount,
            string cnonce,
            string qop,         // qop-value: "", "auth", "auth-int".
            string method,
            DigestAlgorithmsEnum hashAlg = DigestAlgorithmsEnum.MD5)
        {
            string HA2 = DigestCalcHA2(method, uri, hashAlg);

            string unhashedDigest = null;
            if (nonceCount != null && cnonce != null && qop != null)
            {
                unhashedDigest = String.Format("{0}:{1}:{2}:{3}:{4}:{5}",
                ha1,
                nonce,
                nonceCount,
                cnonce,
                qop,
                HA2);
            }
            else
            {
                unhashedDigest = String.Format("{0}:{1}:{2}",
                ha1,
                nonce,
                HA2);
            }

            return GetHashHex(hashAlg, unhashedDigest);
        }

        public static string GetHashHex(DigestAlgorithmsEnum hashAlg, string val)
        {
            // TODO: When .NET Standard and Framework support are deprecated this pragma can be removed.
#pragma warning disable SYSLIB0021
            switch (hashAlg)
            {
                case DigestAlgorithmsEnum.SHA256:
                    using (var hash = new SHA256CryptoServiceProvider())
                    {
                        return hash.ComputeHash(Encoding.UTF8.GetBytes(val)).HexStr().ToLower();
                    }
                // This is commented because RFC8760 does not have an SHA-512 option. Instead it's HSA-512-sess which
                // means the SIP request body needs to be included in the digest as well. Including the body will require 
                // some additional changes that can be done at a later date.
                //case DigestAlgorithmsEnum.SHA512:
                //    using (var hash = new SHA512CryptoServiceProvider())
                //    {
                //        return hash.ComputeHash(Encoding.UTF8.GetBytes(val)).HexStr().ToLower();
                //    }
                case DigestAlgorithmsEnum.MD5:
                default:
                    using (var hash = new MD5CryptoServiceProvider())
                    {
                        return hash.ComputeHash(Encoding.UTF8.GetBytes(val)).HexStr().ToLower();
                    }
            }
#pragma warning restore SYSLIB0021
        }
    }
}
