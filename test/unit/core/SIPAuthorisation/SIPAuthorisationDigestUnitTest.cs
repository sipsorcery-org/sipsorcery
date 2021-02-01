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

using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPAuthorisationDigestUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPAuthorisationDigestUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void KnownDigestTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = new SIPAuthorisationDigest(SIPAuthorisationHeadersEnum.ProxyAuthorization, "asterisk", "aaronxten2", "password", "sip:303@bluesipd", "17190028", "INVITE");

            string digest = authRequest.Digest;

            logger.LogDebug("Digest = " + digest + ".");
            logger.LogDebug(authRequest.ToString());

            Assert.True(true, "True was false.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void KnownDigestTestObscureChars()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = new SIPAuthorisationDigest(SIPAuthorisationHeadersEnum.ProxyAuthorization, "sip.blueface.ie", "aaronnetgear", "!\"$%^&*()_-+=}[{]~#@':;?><,.", "sip:sip.blueface.ie:5060", "1430352056", "REGISTER");

            string digest = authRequest.Digest;

            logger.LogDebug("Digest = " + digest + ".");
            logger.LogDebug(authRequest.ToString());

            Assert.True(digest == "500fd998b609a0f24b45edfe190f2a17", "The digest was incorrect.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void KnownDigestTestObscureChars2()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = new SIPAuthorisationDigest(SIPAuthorisationHeadersEnum.ProxyAuthorization, "sip.blueface.ie", "aaronxten", "_*!$%^()\"", "sip:sip.blueface.ie", "1263192143", "REGISTER");

            string digest = authRequest.Digest;

            logger.LogDebug("Digest = " + digest + ".");
            logger.LogDebug(authRequest.ToString());

            Assert.True(digest == "54b08b70ed1976068b9e18d38ea59849", "The digest was incorrect.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void KnownDigestTest2()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = new SIPAuthorisationDigest(SIPAuthorisationHeadersEnum.ProxyAuthorization, "asterisk", "aaronxten2", "password", "sip:303@213.168.225.133", "4a4ad124", "INVITE");

            string digest = authRequest.Digest;

            logger.LogDebug("Digest = " + digest + ".");
            logger.LogDebug(authRequest.ToString());

            Assert.True(true, "True was false.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void KnownRegisterDigestTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = new SIPAuthorisationDigest(SIPAuthorisationHeadersEnum.ProxyAuthorization, "asterisk", "aaron", "password", "sip:blueface", "1c8192c9", "REGISTER");

            string digest = authRequest.Digest;

            logger.LogDebug("Digest = " + digest + ".");
            logger.LogDebug(authRequest.ToString());

            Assert.True("08881d1d56c0b21f11d19f4067da7045" == digest, "Digest was incorrect.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void KnownRegisterDigestTest2()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = new SIPAuthorisationDigest(SIPAuthorisationHeadersEnum.ProxyAuthorization, "asterisk", "aaron", "password", "sip:blueface", "1c3c7a97", "REGISTER");

            string digest = authRequest.Digest;

            logger.LogDebug("Digest = " + digest + ".");
            logger.LogDebug(authRequest.ToString());

            Assert.True("1ef20beed71043225873e4f6712e4922" == digest, "Digest was incorrect.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void ParseWWWAuthenticateDigestTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
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
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = SIPAuthorisationDigest.ParseAuthorisationDigest(SIPAuthorisationHeadersEnum.WWWAuthenticate, @"Digest realm=""aol.com"",nonce=""48e757f3b95250379d63fe29f777984a93831b80"",qop=""auth"",opaque=""004533235332435434ffac663e"",algorithm=MD5");
            authRequest.SetCredentials("user@aim.com", "password", "sip:01135312222222@sip.aol.com;transport=udp", "INVITE");
            authRequest.Cnonce = "e66ea40d700e8ab69509df4893f4a821";

            string digest = authRequest.Digest;
            authRequest.Response = digest;

            logger.LogDebug("Digest = " + digest + ".");
            logger.LogDebug(authRequest.ToString());

            Assert.True("6221ea0348e2d5229dd1f3825d633295" == digest, "Digest was incorrect.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void AuthenticateHeaderToStringTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = SIPAuthorisationDigest.ParseAuthorisationDigest(SIPAuthorisationHeadersEnum.WWWAuthenticate, @"Digest realm=""aol.com"",nonce=""48e7541d4339e27ee7b520a4bf8a8e3c4fffcb90"",qop=""auth"",opaque=""004533235332435434ffac663e"",algorithm=MD5");
            authRequest.SetCredentials("user@aim.com", "password", "sip:01135312222222@sip.aol.com;transport=udp", "INVITE");
            authRequest.Cnonce = "cf2e005f1801550717cc8c59193aa9f4";

            string digest = authRequest.Digest;
            authRequest.Response = digest;

            logger.LogDebug("Digest = " + digest + ".");
            logger.LogDebug(authRequest.ToString());

            Assert.True(authRequest.ToString() == @"Digest username=""user@aim.com"",realm=""aol.com"",nonce=""48e7541d4339e27ee7b520a4bf8a8e3c4fffcb90"",uri=""sip:01135312222222@sip.aol.com;transport=udp"",response=""18ad0e62fcc9d7f141a72078c4a0784f"",cnonce=""cf2e005f1801550717cc8c59193aa9f4"",nc=00000001,qop=auth,opaque=""004533235332435434ffac663e"",algorithm=MD5", "The authorisation header was not put to a string correctly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void KnownQOPUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = SIPAuthorisationDigest.ParseAuthorisationDigest(SIPAuthorisationHeadersEnum.WWWAuthenticate, "Digest realm=\"jnctn.net\", nonce=\"4a597e1c0000a1636739088e9151ef2f319af257c8f585f1\", qop=\"auth\"");
            authRequest.SetCredentials("user", "password", "sip:user.onsip.com", "REGISTER");
            authRequest.Cnonce = "d3a1ca6af34e72e2461b794f48d5045d";

            string digest = authRequest.Digest;
            authRequest.Response = digest;

            logger.LogDebug("Digest = " + digest + ".");
            logger.LogDebug(authRequest.ToString());

            Assert.True(authRequest.Response == "7709215c1d58c1912dc59d1e8b5b6248", "The authentication response digest was not generated properly.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void KnownOpaqueTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = SIPAuthorisationDigest.ParseAuthorisationDigest(SIPAuthorisationHeadersEnum.WWWAuthenticate, @"digest realm=""Syndeo Corporation"", nonce=""1265068315059e3bbf3052cf13ea5ca22fb71669a7"", opaque=""09c0f23f71f89ce53baab5664c09cbfa"", algorithm=MD5");
            authRequest.SetCredentials("user", "pass", "sip:sip.ribbit.com", "REGISTER");

            string digest = authRequest.Digest;

            logger.LogDebug("Digest = " + digest + ".");
            logger.LogDebug(authRequest.ToString());

            Assert.True(true, "True was false.");

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void GenerateDigestTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = SIPAuthorisationDigest.ParseAuthorisationDigest(SIPAuthorisationHeadersEnum.WWWAuthenticate, @"digest realm=""sipsorcery.com"", nonce=""1265068315059e3bbf3052cf13ea5ca22fb71669a7"", opaque=""09c0f23f71f89ce53baab5664c09cbfa"", algorithm=MD5");
            authRequest.SetCredentials("username", "password", "sip:sipsorcery.com", "REGISTER");

            string digest = authRequest.Digest;

            logger.LogDebug("Digest = " + digest + ".");
            logger.LogDebug(authRequest.ToString());

            Assert.Equal("b1ea9d6b32e8dd0023a3feec14b16177", digest);

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void KnownHA1Digest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var digest = HTTPDigest.DigestCalcHA1("user", "sipsorcery.cloud", "password");

            logger.LogDebug($"Digest = {digest}.");

            Assert.Equal("f5732e14bef238badb2b4cb987d415f6", digest);

            logger.LogDebug("-----------------------------------------");
        }
    }
}
