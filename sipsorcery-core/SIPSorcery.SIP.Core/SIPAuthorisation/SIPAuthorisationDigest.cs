//-----------------------------------------------------------------------------
// Filename: Digest.cs
//
// Description: Implements Digest Authentication as defined in RFC2617.
//
// History:
// 08 Sep 2005	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SIPSorcery.Sys;

#if UNITTEST
using NUnit.Framework;
#endif

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

	public class SIPAuthorisationDigest
	{
        public const string AUTH_ALGORITHM = "MD5";
        public const string QOP_AUTHENTICATION_VALUE = "auth";
        private const int NONCE_DEFAULT_COUNT = 1;

        private static char[] m_headerFieldRemoveChars = new char[] { ' ', '"', '\'' };

        public SIPAuthorisationHeadersEnum AuthorisationType { get; private set; }              // This is the type of authorisation request received.
        public SIPAuthorisationHeadersEnum AuthorisationResponseType { get; private set; }      // If this is set it's the type of authorisation response to use otherwise use the same as the request (God knows why you need a different response header?!?)

		public string Realm;
		public string Username;
		public string Password;
		public string DestinationURL;
		public string URI;
		public string Nonce;
		public string RequestType;
		public string Response;
        public string Algorithhm;

        public string Cnonce;        // Client nonce (used with WWW-Authenticate and qop=auth).
        public string Qop;           // Quality of Protection. Values permitted are auth (authentication) and auth-int (authentication with integrity protection).
        private int NonceCount = 0;  // Client nonce count.
        public string Opaque;

		public string Digest
		{
			get
			{
                Algorithhm = AUTH_ALGORITHM;

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

				return HTTPDigest.DigestCalcResponse(
					AUTH_ALGORITHM,
					Username,
					Realm,
					Password,
					URI,
					Nonce,
					nonceCountStr,
					Cnonce,
					Qop,
					RequestType,
					null,
					null);
			}
		}

        public SIPAuthorisationDigest()
        {
            AuthorisationType = SIPAuthorisationHeadersEnum.ProxyAuthorization;
        }

        public SIPAuthorisationDigest(SIPAuthorisationHeadersEnum authorisationType)
		{
            AuthorisationType = authorisationType;
        }

        public static SIPAuthorisationDigest ParseAuthorisationDigest(SIPAuthorisationHeadersEnum authorisationType, string authorisationRequest)
		{
            SIPAuthorisationDigest authRequest = new SIPAuthorisationDigest(authorisationType);

            string noDigestHeader = Regex.Replace(authorisationRequest, @"^\s*Digest\s*", "", RegexOptions.IgnoreCase);
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
                            authRequest.Algorithhm = headerValue;
                        }
                    }
                }
            }

            return authRequest;
		}

        public SIPAuthorisationDigest(SIPAuthorisationHeadersEnum authorisationType, string realm, string username, string password, string uri, string nonce, string request)
		{
            AuthorisationType = authorisationType;
			Realm = realm;
			Username = username;
			Password = password;
			URI = uri;
			Nonce = nonce;
			RequestType = request;
		}

        public void SetCredentials(string username, string password, string uri, string method)
        {
            Username = username;
            Password = password;
            URI = uri;
            RequestType = method;
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
            authHeader += (Opaque != null) ? "," + AuthHeaders.AUTH_OPAQUE_KEY + "=\"" + Opaque + "\"": null;
            authHeader += (Algorithhm != null) ? "," + AuthHeaders.AUTH_ALGORITHM_KEY + "=" + Algorithhm : null;

            return authHeader;
		}

        private string GetPaddedNonceCount(int count)
        {
            return "00000000".Substring(0, 8 - NonceCount.ToString().Length) + count;
        }

		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
        public class SIPAuthorisationDigestUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{}

			[TestFixtureTearDown]
			public void Dispose()
			{}

            [Test]
            public void SampleTest() {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
                Assert.IsTrue(true, "True was false.");
                Console.WriteLine("-----------------------------------------");
            }

			[Test]
			public void KnownDigestTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
				
				SIPAuthorisationDigest authRequest = new SIPAuthorisationDigest(SIPAuthorisationHeadersEnum.ProxyAuthorization, "asterisk", "aaronxten2", "password", "sip:303@bluesipd", "17190028", "INVITE");

				string digest = authRequest.Digest;

				Console.WriteLine("Digest = " + digest + ".");
				Console.WriteLine(authRequest.ToString());

				Assert.IsTrue(true, "True was false.");

				Console.WriteLine("-----------------------------------------");
			}

			[Test]
			public void KnownDigestTestObscureChars()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPAuthorisationDigest authRequest = new SIPAuthorisationDigest(SIPAuthorisationHeadersEnum.ProxyAuthorization, "sip.blueface.ie", "aaronnetgear", "!\"$%^&*()_-+=}[{]~#@':;?><,.", "sip:sip.blueface.ie:5060", "1430352056", "REGISTER");

				string digest = authRequest.Digest;

				Console.WriteLine("Digest = " + digest + ".");
				Console.WriteLine(authRequest.ToString());

				Assert.IsTrue(digest == "500fd998b609a0f24b45edfe190f2a17", "The digest was incorrect.");

				Console.WriteLine("-----------------------------------------");
			}

			[Test]
			public void KnownDigestTestObscureChars2()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPAuthorisationDigest authRequest = new SIPAuthorisationDigest(SIPAuthorisationHeadersEnum.ProxyAuthorization, "sip.blueface.ie", "aaronxten", "_*!$%^()\"", "sip:sip.blueface.ie", "1263192143", "REGISTER");

				string digest = authRequest.Digest;

				Console.WriteLine("Digest = " + digest + ".");
				Console.WriteLine(authRequest.ToString());

				Assert.IsTrue(digest == "54b08b70ed1976068b9e18d38ea59849", "The digest was incorrect.");

				Console.WriteLine("-----------------------------------------");
			}


			[Test]
			public void KnownDigestTest2()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPAuthorisationDigest authRequest = new SIPAuthorisationDigest(SIPAuthorisationHeadersEnum.ProxyAuthorization, "asterisk", "aaronxten2", "password", "sip:303@213.168.225.133", "4a4ad124", "INVITE");

				string digest = authRequest.Digest;

				Console.WriteLine("Digest = " + digest + ".");
				Console.WriteLine(authRequest.ToString());

				Assert.IsTrue(true, "True was false.");

				Console.WriteLine("-----------------------------------------");
			}

			[Test]
			public void KnownRegisterDigestTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPAuthorisationDigest authRequest = new SIPAuthorisationDigest(SIPAuthorisationHeadersEnum.ProxyAuthorization, "asterisk", "aaron", "password", "sip:blueface", "1c8192c9", "REGISTER");

				string digest = authRequest.Digest;

				Console.WriteLine("Digest = " + digest + ".");
				Console.WriteLine(authRequest.ToString());

				Assert.IsTrue("08881d1d56c0b21f11d19f4067da7045" == digest, "Digest was incorrect.");

				Console.WriteLine("-----------------------------------------");
			}
		
			[Test]
			public void KnownRegisterDigestTest2()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPAuthorisationDigest authRequest = new SIPAuthorisationDigest(SIPAuthorisationHeadersEnum.ProxyAuthorization, "asterisk", "aaron", "password", "sip:blueface", "1c3c7a97", "REGISTER");

				string digest = authRequest.Digest;

				Console.WriteLine("Digest = " + digest + ".");
				Console.WriteLine(authRequest.ToString());

				Assert.IsTrue("1ef20beed71043225873e4f6712e4922" == digest, "Digest was incorrect.");

				Console.WriteLine("-----------------------------------------");
			}

            [Test]
            public void ParseWWWAuthenticateDigestTest()
            {
                 Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                 SIPAuthorisationDigest authRequest = SIPAuthorisationDigest.ParseAuthorisationDigest(SIPAuthorisationHeadersEnum.WWWAuthenticate, @"Digest realm=""aol.com"",nonce=""48e7541d4339e27ee7b520a4bf8a8e3c4fffcb90"",qop=""auth"",opaque=""004533235332435434ffac663e"",algorithm=MD5");

                 Assert.IsTrue(authRequest.Realm == "aol.com", "The authorisation realm was not parsed correctly.");
                 Assert.IsTrue(authRequest.Nonce == "48e7541d4339e27ee7b520a4bf8a8e3c4fffcb90", "The authorisation nonce was not parsed correctly.");
                 Assert.IsTrue(authRequest.Qop == "auth", "The authorisation qop was not parsed correctly.");
                 Assert.IsTrue(authRequest.Opaque == "004533235332435434ffac663e", "The authorisation opaque was not parsed correctly.");

                 Console.WriteLine("-----------------------------------------");
             }

            [Test]
            public void KnownWWWAuthenticateDigestTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPAuthorisationDigest authRequest = SIPAuthorisationDigest.ParseAuthorisationDigest(SIPAuthorisationHeadersEnum.WWWAuthenticate, @"Digest realm=""aol.com"",nonce=""48e757f3b95250379d63fe29f777984a93831b80"",qop=""auth"",opaque=""004533235332435434ffac663e"",algorithm=MD5");
                authRequest.SetCredentials("user@aim.com", "password", "sip:01135312222222@sip.aol.com;transport=udp", "INVITE");
                authRequest.Cnonce = "e66ea40d700e8ab69509df4893f4a821";

                string digest = authRequest.Digest;
                authRequest.Response = digest;

                Console.WriteLine("Digest = " + digest + ".");
                Console.WriteLine(authRequest.ToString());

                Assert.IsTrue("6221ea0348e2d5229dd1f3825d633295" == digest, "Digest was incorrect.");

                Console.WriteLine("-----------------------------------------");
            }
            
            [Test]
            public void AuthenticateHeaderToStringTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPAuthorisationDigest authRequest = SIPAuthorisationDigest.ParseAuthorisationDigest(SIPAuthorisationHeadersEnum.WWWAuthenticate, @"Digest realm=""aol.com"",nonce=""48e7541d4339e27ee7b520a4bf8a8e3c4fffcb90"",qop=""auth"",opaque=""004533235332435434ffac663e"",algorithm=MD5");
                authRequest.SetCredentials("user@aim.com", "password", "sip:01135312222222@sip.aol.com;transport=udp", "INVITE");
                authRequest.Cnonce = "cf2e005f1801550717cc8c59193aa9f4";

                string digest = authRequest.Digest;
                authRequest.Response = digest;

                Console.WriteLine("Digest = " + digest + ".");
                Console.WriteLine(authRequest.ToString());

                Assert.IsTrue(authRequest.ToString() == @"Digest username=""user@aim.com"",realm=""aol.com"",nonce=""48e7541d4339e27ee7b520a4bf8a8e3c4fffcb90"",uri=""sip:01135312222222@sip.aol.com;transport=udp"",response=""18ad0e62fcc9d7f141a72078c4a0784f"",cnonce=""cf2e005f1801550717cc8c59193aa9f4"",nc=00000001,qop=auth,opaque=""004533235332435434ffac663e"",algorithm=MD5", "The authorisation header was not put to a string correctly.");

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void KnownQOPUnitTest() {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPAuthorisationDigest authRequest = SIPAuthorisationDigest.ParseAuthorisationDigest(SIPAuthorisationHeadersEnum.WWWAuthenticate, "Digest realm=\"jnctn.net\", nonce=\"4a597e1c0000a1636739088e9151ef2f319af257c8f585f1\", qop=\"auth\"");
                authRequest.SetCredentials("user", "password", "sip:user.onsip.com", "REGISTER");
                authRequest.Cnonce = "d3a1ca6af34e72e2461b794f48d5045d";

                string digest = authRequest.Digest;
                authRequest.Response = digest;

                Console.WriteLine("Digest = " + digest + ".");
                Console.WriteLine(authRequest.ToString());

                Assert.IsTrue(authRequest.Response == "7709215c1d58c1912dc59d1e8b5b6248", "The authentication response digest was not generated properly.");

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void KnownOpaqueTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPAuthorisationDigest authRequest = SIPAuthorisationDigest.ParseAuthorisationDigest(SIPAuthorisationHeadersEnum.WWWAuthenticate, @"digest realm=""Syndeo Corporation"", nonce=""1265068315059e3bbf3052cf13ea5ca22fb71669a7"", opaque=""09c0f23f71f89ce53baab5664c09cbfa"", algorithm=MD5");
                authRequest.SetCredentials("user", "pass", "sip:sip.ribbit.com", "REGISTER");

                string digest = authRequest.Digest;

                Console.WriteLine("Digest = " + digest + ".");
                Console.WriteLine(authRequest.ToString());

                Assert.IsTrue(true, "True was false.");

                Console.WriteLine("-----------------------------------------");
            }
		}

		#endif

		#endregion
	}

	public class HTTPDigest
	{
		/// <summary>
		/// Calculate H(A1) as per HTTP Digest specification.
		/// </summary>
		public static string DigestCalcHA1(
			string username,
			string realm,
			string password)
		{
			string a1 = String.Format("{0}:{1}:{2}", username, realm, password);
			return GetMD5HashBinHex(a1);
		}

		/// <summary>
		/// Calculate H(A2) as per HTTP Digest specification.
		/// </summary>
		public static string DigestCalcHA2(
			string method,
			string uri)
		{
			string A2 = String.Format("{0}:{1}", method, uri);
			
			return GetMD5HashBinHex(A2);
		}

		public static string DigestCalcResponse(
			string algorithm,
			string username,
			string realm,
			string password,
			string uri,		
			string nonce,
			string nonceCount,
			string cnonce,
			string qop,			// qop-value: "", "auth", "auth-int".
			string method,
			string digestURL,
			string hEntity
			)
		{
			string HA1 = DigestCalcHA1(username, realm, password);
			string HA2 = DigestCalcHA2(method, uri);
			
            string unhashedDigest= null;
            if (nonceCount != null && cnonce != null && qop != null)
            {
                unhashedDigest = String.Format("{0}:{1}:{2}:{3}:{4}:{5}",
                HA1,
                nonce,
                nonceCount,
                cnonce,
                qop,
                HA2);
            }
            else
            {
                unhashedDigest = String.Format("{0}:{1}:{2}",
                HA1,
                nonce,
                HA2);
            }

			return GetMD5HashBinHex(unhashedDigest);
		}

		public static string GetMD5HashBinHex(string val)
		{
			MD5 md5 = new MD5CryptoServiceProvider();
			byte[] bHA1 = md5.ComputeHash(Encoding.UTF8.GetBytes(val));
			string HA1 = null;
			for (int i = 0 ; i < 16 ; i++)
				HA1 += String.Format("{0:x02}",bHA1[i]);
			return HA1;
		}

		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class HTTPDigestUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{}

			[TestFixtureTearDown]
			public void Dispose()
			{}

			[Test]
			public void SampleTest()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
				Assert.IsTrue(true, "True was false.");
			}
		}

		#endif

		#endregion
	}
}
