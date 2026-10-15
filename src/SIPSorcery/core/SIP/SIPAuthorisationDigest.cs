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
using Microsoft.Extensions.Logging;
using Polyfills;
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

    /// <summary>
    /// Resolves the HA1 digest for a SIP authentication challenge. Return <c>null</c> when no
    /// credential is available for the supplied username, realm and digest algorithm.
    /// </summary>
    public delegate string GetHA1DigestDelegate(
        string username,
        string realm,
        DigestAlgorithmsEnum algorithm);

    public class SIPAuthorisationDigest
    {
        public const string METHOD = "Digest";
        public const string QOP_AUTHENTICATION_VALUE = "auth";
        private const int NONCE_DEFAULT_COUNT = 1;
        private const string SHA256_ALGORITHM_ID = "SHA-256";

        private static readonly ILogger logger = LogFactory.CreateLogger<SIPAuthorisationDigest>();

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

            ArgumentNullException.ThrowIfNull(authorisationRequest);

            var headerFields = authorisationRequest.AsSpan().TrimStart();
            if (headerFields.StartsWith(METHOD, StringComparison.OrdinalIgnoreCase))
            {
                headerFields = headerFields.Slice(METHOD.Length).TrimStart();
            }

            Span<Range> headerKeyValueRange = stackalloc Range[2];
            foreach (var headerFieldRange in headerFields.Split(','))
            {
                var headerField = headerFields[headerFieldRange];

                if (headerField.Split(headerKeyValueRange, '=') == 2)
                {
                    var headerName = headerField[headerKeyValueRange[0]].Trim().ToString();
                    var headerValue = headerField[headerKeyValueRange[1]].ToString().Trim(m_headerFieldRemoveChars);

                    if (string.Equals(headerName, AuthHeaders.AUTH_REALM_KEY, StringComparison.OrdinalIgnoreCase))
                    {
                        authRequest.Realm = headerValue;
                    }
                    else if (string.Equals(headerName, AuthHeaders.AUTH_NONCE_KEY, StringComparison.OrdinalIgnoreCase))
                    {
                        authRequest.Nonce = headerValue;
                    }
                    else if (string.Equals(headerName, AuthHeaders.AUTH_USERNAME_KEY, StringComparison.OrdinalIgnoreCase))
                    {
                        authRequest.Username = headerValue;
                    }
                    else if (string.Equals(headerName, AuthHeaders.AUTH_RESPONSE_KEY, StringComparison.OrdinalIgnoreCase))
                    {
                        authRequest.Response = headerValue;
                    }
                    else if (string.Equals(headerName, AuthHeaders.AUTH_URI_KEY, StringComparison.OrdinalIgnoreCase))
                    {
                        authRequest.URI = headerValue;
                    }
                    else if (string.Equals(headerName, AuthHeaders.AUTH_CNONCE_KEY, StringComparison.OrdinalIgnoreCase))
                    {
                        authRequest.Cnonce = headerValue;
                    }
                    else if (string.Equals(headerName, AuthHeaders.AUTH_NONCECOUNT_KEY, StringComparison.OrdinalIgnoreCase))
                    {
                        Int32.TryParse(headerValue, out authRequest.NonceCount);
                    }
                    else if (string.Equals(headerName, AuthHeaders.AUTH_QOP_KEY, StringComparison.OrdinalIgnoreCase))
                    {
                        authRequest.Qop = headerValue.ToLower();
                    }
                    else if (string.Equals(headerName, AuthHeaders.AUTH_OPAQUE_KEY, StringComparison.OrdinalIgnoreCase))
                    {
                        authRequest.Opaque = headerValue;
                    }
                    else if (string.Equals(headerName, AuthHeaders.AUTH_ALGORITHM_KEY, StringComparison.OrdinalIgnoreCase))
                    {
                        //authRequest.Algorithhm = headerValue;

                        if (Enum.TryParse<DigestAlgorithmsEnum>(headerValue.Replace("-", ""), true, out var alg))
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

                if (string.IsNullOrWhiteSpace(Cnonce))
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
            var authHeader = new StringBuilder();
            var hasParameter = false;

            void AppendSeparator()
            {
                if (hasParameter)
                {
                    authHeader.Append(',');
                }
                else
                {
                    hasParameter = true;
                }
            }

            void AppendQuotedParameter(string key, string value)
            {
                AppendSeparator();
                authHeader.Append(key).Append("=\"").Append(value).Append('"');
            }

            void AppendParameter(string key, string value)
            {
                AppendSeparator();
                authHeader.Append(key).Append('=').Append(value);
            }

            authHeader.Append(AuthHeaders.AUTH_DIGEST_KEY).Append(' ');

            if (!string.IsNullOrWhiteSpace(Username))
            {
                AppendQuotedParameter(AuthHeaders.AUTH_USERNAME_KEY, Username);
            }

            AppendQuotedParameter(AuthHeaders.AUTH_REALM_KEY, Realm);

            if (Nonce != null)
            {
                AppendQuotedParameter(AuthHeaders.AUTH_NONCE_KEY, Nonce);
            }

            if (!string.IsNullOrWhiteSpace(URI))
            {
                AppendQuotedParameter(AuthHeaders.AUTH_URI_KEY, URI);
            }

            if (Response != null && Response.Length != 0)
            {
                AppendQuotedParameter(AuthHeaders.AUTH_RESPONSE_KEY, Response);
            }

            if (Cnonce != null)
            {
                AppendQuotedParameter(AuthHeaders.AUTH_CNONCE_KEY, Cnonce);
            }

            if (NonceCount != 0)
            {
                AppendParameter(AuthHeaders.AUTH_NONCECOUNT_KEY, GetPaddedNonceCount(NonceCount));
            }

            if (Qop != null)
            {
                AppendParameter(AuthHeaders.AUTH_QOP_KEY, Qop);
            }

            if (Opaque != null)
            {
                AppendQuotedParameter(AuthHeaders.AUTH_OPAQUE_KEY, Opaque);
            }

            string algorithmID = (DigestAlgorithm == DigestAlgorithmsEnum.SHA256) ? SHA256_ALGORITHM_ID : DigestAlgorithm.ToString();
            if (Response != null)
            {
                AppendParameter(AuthHeaders.AUTH_ALGORITHM_KEY, algorithmID);
            }

            return authHeader.ToString();
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
            return $"{"00000000".Substring(0, 8 - NonceCount.ToString().Length)}{count}";
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
            return GetHashHex(hashAlg, $"{username}:{realm}:{password}");
        }

        /// <summary>
        /// Calculate H(A2) as per HTTP Digest specification.
        /// </summary>
        public static string DigestCalcHA2(
            string method,
            string uri,
            DigestAlgorithmsEnum hashAlg = DigestAlgorithmsEnum.MD5)
        {
            return GetHashHex(hashAlg, $"{method}:{uri}");
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
            var HA2 = DigestCalcHA2(method, uri, hashAlg);
            if (nonceCount != null && cnonce != null && qop != null)
            {
                return GetHashHex(hashAlg, $"{ha1}:{nonce}:{nonceCount}:{cnonce}:{qop}:{HA2}");
            }

            return GetHashHex(hashAlg, $"{ha1}:{nonce}:{HA2}");
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
