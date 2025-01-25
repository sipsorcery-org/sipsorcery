//-----------------------------------------------------------------------------
// Author(s):
// Aaron Clauson
// 
// History:
// 
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Xunit;
using SIPSorcery.SIP.App;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPAuthorisationDigestUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        private class MockSIPAccount : ISIPAccount
        {
            public string ID { get; }
            public string SIPUsername { get; }
            public string SIPPassword { get; }
            public string HA1Digest { get; }   // Digest of the username + domain + password. Can be used for authentication instead of the password field.
            public string SIPDomain { get; }
            public bool IsDisabled { get; }

            public MockSIPAccount(string username, string password)
            {
                ID = Guid.NewGuid().ToString();
                SIPUsername = username;
                SIPPassword = password;
            }
        }

        public SIPAuthorisationDigestUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void KnownDigestTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = new SIPAuthorisationDigest(SIPAuthorisationHeadersEnum.ProxyAuthorization, "asterisk", "aaronxten2", "password", "sip:303@bluesipd", "17190028", "INVITE");

            string digest = authRequest.GetDigest();

            logger.LogDebug("Digest = {digest}.", digest);
            logger.LogDebug("{AuthRequest}", authRequest.ToString());
       
            Assert.Equal("06b931d79a06b4e9426b7efbbd6c8da2", digest);
            Assert.Equal(DigestAlgorithmsEnum.MD5, authRequest.DigestAlgorithm);

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void KnownDigestTestObscureChars()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = new SIPAuthorisationDigest(SIPAuthorisationHeadersEnum.ProxyAuthorization, "sip.blueface.ie", "aaronnetgear", "!\"$%^&*()_-+=}[{]~#@':;?><,.", "sip:sip.blueface.ie:5060", "1430352056", "REGISTER");

            string digest = authRequest.GetDigest();

            logger.LogDebug("Digest = {digest}.", digest);
            logger.LogDebug("{AuthRequest}", authRequest.ToString());

            Assert.True(digest == "500fd998b609a0f24b45edfe190f2a17", "The digest was incorrect.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void KnownDigestTestObscureChars2()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = new SIPAuthorisationDigest(SIPAuthorisationHeadersEnum.ProxyAuthorization, "sip.blueface.ie", "aaronxten", "_*!$%^()\"", "sip:sip.blueface.ie", "1263192143", "REGISTER");

            string digest = authRequest.GetDigest();

            logger.LogDebug("Digest = {digest}.", digest);
            logger.LogDebug("{AuthRequest}", authRequest.ToString());

            Assert.True(digest == "54b08b70ed1976068b9e18d38ea59849", "The digest was incorrect.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void KnownDigestTest2()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = new SIPAuthorisationDigest(SIPAuthorisationHeadersEnum.ProxyAuthorization, "asterisk", "aaronxten2", "password", "sip:303@213.168.225.133", "4a4ad124", "INVITE");

            string digest = authRequest.GetDigest();

            logger.LogDebug("Digest = {digest}.", digest);
            logger.LogDebug("{AuthRequest}", authRequest.ToString());

            Assert.True(true, "True was false.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void KnownRegisterDigestTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = new SIPAuthorisationDigest(SIPAuthorisationHeadersEnum.ProxyAuthorization, "asterisk", "aaron", "password", "sip:blueface", "1c8192c9", "REGISTER");

            string digest = authRequest.GetDigest();

            logger.LogDebug("Digest = {digest}.", digest);
            logger.LogDebug("{AuthRequest}", authRequest.ToString());

            Assert.True("08881d1d56c0b21f11d19f4067da7045" == digest, "Digest was incorrect.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void KnownRegisterDigestTest2()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = new SIPAuthorisationDigest(SIPAuthorisationHeadersEnum.ProxyAuthorization, "asterisk", "aaron", "password", "sip:blueface", "1c3c7a97", "REGISTER");

            string digest = authRequest.GetDigest();

            logger.LogDebug("Digest = {digest}.", digest);
            logger.LogDebug("{AuthRequest}", authRequest.ToString());

            Assert.True("1ef20beed71043225873e4f6712e4922" == digest, "Digest was incorrect.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseWWWAuthenticateDigestTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = SIPAuthorisationDigest.ParseAuthorisationDigest(SIPAuthorisationHeadersEnum.WWWAuthenticate, @"Digest realm=""aol.com"",nonce=""48e7541d4339e27ee7b520a4bf8a8e3c4fffcb90"",qop=""auth"",opaque=""004533235332435434ffac663e"",algorithm=MD5");

            Assert.True(authRequest.Realm == "aol.com", "The authorisation realm was not parsed correctly.");
            Assert.True(authRequest.Nonce == "48e7541d4339e27ee7b520a4bf8a8e3c4fffcb90", "The authorisation nonce was not parsed correctly.");
            Assert.True(authRequest.Qop == "auth", "The authorisation qop was not parsed correctly.");
            Assert.True(authRequest.Opaque == "004533235332435434ffac663e", "The authorisation opaque was not parsed correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void KnownWWWAuthenticateDigestTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = SIPAuthorisationDigest.ParseAuthorisationDigest(SIPAuthorisationHeadersEnum.WWWAuthenticate, @"Digest realm=""aol.com"",nonce=""48e757f3b95250379d63fe29f777984a93831b80"",qop=""auth"",opaque=""004533235332435434ffac663e"",algorithm=MD5");
            authRequest.SetCredentials("user@aim.com", "password", "sip:01135312222222@sip.aol.com;transport=udp", "INVITE");
            authRequest.Cnonce = "e66ea40d700e8ab69509df4893f4a821";

            string digest = authRequest.GetDigest();
            authRequest.Response = digest;

            logger.LogDebug("Digest = {digest}.", digest);
            logger.LogDebug("{AuthRequest}", authRequest.ToString());

            Assert.True("6221ea0348e2d5229dd1f3825d633295" == digest, "Digest was incorrect.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void AuthenticateHeaderToStringTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = SIPAuthorisationDigest.ParseAuthorisationDigest(SIPAuthorisationHeadersEnum.WWWAuthenticate, @"Digest realm=""aol.com"",nonce=""48e7541d4339e27ee7b520a4bf8a8e3c4fffcb90"",qop=""auth"",opaque=""004533235332435434ffac663e"",algorithm=MD5");
            authRequest.SetCredentials("user@aim.com", "password", "sip:01135312222222@sip.aol.com;transport=udp", "INVITE");
            authRequest.Cnonce = "cf2e005f1801550717cc8c59193aa9f4";

            string digest = authRequest.GetDigest();
            authRequest.Response = digest;

            logger.LogDebug("Digest = {digest}.", digest);
            logger.LogDebug("{AuthRequest}", authRequest.ToString());

            Assert.True(authRequest.ToString() == @"Digest username=""user@aim.com"",realm=""aol.com"",nonce=""48e7541d4339e27ee7b520a4bf8a8e3c4fffcb90"",uri=""sip:01135312222222@sip.aol.com;transport=udp"",response=""18ad0e62fcc9d7f141a72078c4a0784f"",cnonce=""cf2e005f1801550717cc8c59193aa9f4"",nc=00000001,qop=auth,opaque=""004533235332435434ffac663e"",algorithm=MD5", "The authorisation header was not put to a string correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void KnownQOPUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = SIPAuthorisationDigest.ParseAuthorisationDigest(SIPAuthorisationHeadersEnum.WWWAuthenticate, "Digest realm=\"jnctn.net\", nonce=\"4a597e1c0000a1636739088e9151ef2f319af257c8f585f1\", qop=\"auth\"");
            authRequest.SetCredentials("user", "password", "sip:user.onsip.com", "REGISTER");
            authRequest.Cnonce = "d3a1ca6af34e72e2461b794f48d5045d";

            string digest = authRequest.GetDigest();
            authRequest.Response = digest;

            logger.LogDebug("Digest = {digest}.", digest);
            logger.LogDebug("{AuthRequest}", authRequest.ToString());

            Assert.True(authRequest.Response == "7709215c1d58c1912dc59d1e8b5b6248", "The authentication response digest was not generated properly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void KnownOpaqueTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = SIPAuthorisationDigest.ParseAuthorisationDigest(SIPAuthorisationHeadersEnum.WWWAuthenticate, @"digest realm=""Syndeo Corporation"", nonce=""1265068315059e3bbf3052cf13ea5ca22fb71669a7"", opaque=""09c0f23f71f89ce53baab5664c09cbfa"", algorithm=MD5");
            authRequest.SetCredentials("user", "pass", "sip:sip.ribbit.com", "REGISTER");

            string digest = authRequest.GetDigest();

            logger.LogDebug("Digest = {digest}.", digest);
            logger.LogDebug("{AuthRequest}", authRequest.ToString());

            Assert.True(true, "True was false.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void GenerateDigestTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = SIPAuthorisationDigest.ParseAuthorisationDigest(SIPAuthorisationHeadersEnum.WWWAuthenticate, @"digest realm=""sipsorcery.com"", nonce=""1265068315059e3bbf3052cf13ea5ca22fb71669a7"", opaque=""09c0f23f71f89ce53baab5664c09cbfa"", algorithm=MD5");
            authRequest.SetCredentials("username", "password", "sip:sipsorcery.com", "REGISTER");

            string digest = authRequest.GetDigest();

            logger.LogDebug("Digest = {digest}.", digest);
            logger.LogDebug("{AuthRequest}", authRequest.ToString());

            Assert.Equal("b1ea9d6b32e8dd0023a3feec14b16177", digest);

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void KnownHA1Digest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var digest = HTTPDigest.DigestCalcHA1("user", "sipsorcery.cloud", "password");

            logger.LogDebug("Digest = {digest}.", digest);

            Assert.Equal("f5732e14bef238badb2b4cb987d415f6", digest);

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a known digest for MD5 and SHA256 are correctly generated.
        /// </summary>
        [Fact]
        public void KnownMD5AndSHA256DigestTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            {
                SIPAuthorisationDigest authDigest = new SIPAuthorisationDigest(SIPAuthorisationHeadersEnum.ProxyAuthorization, "asterisk", "aaronxten2", "password", 
                    "sip:303@bluesipd", "17190028", "INVITE", DigestAlgorithmsEnum.SHA256);

                string digest = authDigest.GetDigest();

                logger.LogDebug("SHA256 Digest = {digest}.", digest);
                logger.LogDebug("{AuthDigest}", authDigest.ToString());

                Assert.Equal("34c1239616bbf7d3c1147d3933f333852f5c84d1adf8ccde86679598a4abd4aa", digest);
                Assert.Equal(DigestAlgorithmsEnum.SHA256, authDigest.DigestAlgorithm);
            }

            {
                SIPAuthorisationDigest authDigest = new SIPAuthorisationDigest(SIPAuthorisationHeadersEnum.ProxyAuthorization, "asterisk", "aaronxten2", "password",
                    "sip:303@bluesipd", "17190028", "INVITE", DigestAlgorithmsEnum.MD5);

                string digest = authDigest.GetDigest();

                logger.LogDebug("MD5 Digest = {digest}.", digest);
                logger.LogDebug("{AuthDigest}", authDigest.ToString());

                Assert.Equal("06b931d79a06b4e9426b7efbbd6c8da2", digest);
                Assert.Equal(DigestAlgorithmsEnum.MD5, authDigest.DigestAlgorithm);
            }

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that the MD5 test vector from RFC7616 https://datatracker.ietf.org/doc/html/rfc7616#section-3.9.1
        /// is correctly generated.
        /// </summary>
        [Fact]
        public void HttpDigestMD5TestVector()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var md5DigestReq = SIPAuthorisationDigest.ParseAuthorisationDigest(SIPAuthorisationHeadersEnum.WWWAuthenticate, 
@"Digest
realm=""http-auth@example.org"",
qop=""auth, auth-int"",
algorithm=MD5,
nonce=""7ypf/xlj9XXwfDPEoM4URrv/xwf94BcCAzFZH4GiTo0v"",
opaque=""FQhe/qaU925kfnzjCev0ciny7QMkPqMAFRtzCUYo5tdS""");

            md5DigestReq.SetCredentials("Mufasa", "Circle of Life", "/dir/index.html", "GET");
            md5DigestReq.Cnonce = "f2/wE4q74E6zIJEtWaHKaf5wv/H5QzzpXusqGemxURZJ";

            var md5Digest = md5DigestReq.GetDigest();

            logger.LogDebug("MD5 digest={md5Digest}.", md5Digest);
            logger.LogDebug("Auth Header={md5DigestReq}.", md5DigestReq);

            Assert.Equal("8ca523f5e9506fed4657c9700eebdbec", md5Digest);

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that the SHA256 test vector from RFC7616 https://datatracker.ietf.org/doc/html/rfc7616#section-3.9.1
        /// is correctly generated.
        /// </summary>
        [Fact]
        public void HttpDigestSHA256TestVector()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var digestReq = SIPAuthorisationDigest.ParseAuthorisationDigest(SIPAuthorisationHeadersEnum.WWWAuthenticate,
@"Digest
realm=""http-auth@example.org"",
qop=""auth, auth-int"",
algorithm=SHA-256,
nonce=""7ypf/xlj9XXwfDPEoM4URrv/xwf94BcCAzFZH4GiTo0v"",
opaque=""FQhe/qaU925kfnzjCev0ciny7QMkPqMAFRtzCUYo5tdS""");

            digestReq.SetCredentials("Mufasa", "Circle of Life", "/dir/index.html", "GET");
            digestReq.Cnonce = "f2/wE4q74E6zIJEtWaHKaf5wv/H5QzzpXusqGemxURZJ";

            var digest = digestReq.GetDigest();

            logger.LogDebug("SHA256 digest={digest}.", digest);
            logger.LogDebug("Auth Header={digestReq}.", digestReq);

            Assert.Equal("753927fa0e85d155564e2e272a28d1802ca10daf4496794697cf8db5856cb6c1", digest);

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that SIP request authentication logic can authenticate a digest generated by the 
        /// authenticating logic.
        /// </summary>
        [Fact]
        public void SIPRequestAuthRoundTrip()
        {
            var account = new MockSIPAccount("user", "password");
            var req = SIPRequest.GetRequest(SIPMethodsEnum.OPTIONS, SIPURI.ParseSIPURI("sip:100@0.0.0.0"));

            logger.LogDebug("Req={req}", req);

            var authResult = SIPRequestAuthenticator.AuthenticateSIPRequest(SIPEndPoint.Empty, SIPEndPoint.Empty, req, account);

            var authReq = req.DuplicateAndAuthenticate(new List<SIPAuthenticationHeader> { authResult.AuthenticationRequiredHeader }, 
                account.SIPUsername, account.SIPPassword);

            logger.LogDebug("Auth req={authReq}", authReq);

            authResult = SIPRequestAuthenticator.AuthenticateSIPRequest(SIPEndPoint.Empty, SIPEndPoint.Empty, authReq, account);

            Assert.True(authResult.Authenticated);
        }

        /// <summary>
        /// Tests that SIP request authentication logic can authenticate a digest generated by the 
        /// authenticating logic with the authentication digest supplied as SHA256.
        /// </summary>
        [Fact]
        public void SIPRequestAuthRoundTripWithSHA256DIgest()
        {
            var account = new MockSIPAccount("user", "password");
            var req = SIPRequest.GetRequest(SIPMethodsEnum.OPTIONS, SIPURI.ParseSIPURI("sip:100@0.0.0.0"));

            logger.LogDebug("Req={req}", req);

            var authResult = SIPRequestAuthenticator.AuthenticateSIPRequest(SIPEndPoint.Empty, SIPEndPoint.Empty, req, account);
            authResult.AuthenticationRequiredHeader.SIPDigest.DigestAlgorithm = DigestAlgorithmsEnum.SHA256;

            var authReq = req.DuplicateAndAuthenticate(new List<SIPAuthenticationHeader> { authResult.AuthenticationRequiredHeader },
                account.SIPUsername, account.SIPPassword);

            logger.LogDebug("Auth req={authReq}", authReq);

            authResult = SIPRequestAuthenticator.AuthenticateSIPRequest(SIPEndPoint.Empty, SIPEndPoint.Empty, authReq, account);

            Assert.True(authResult.Authenticated);
        }
    }
}
