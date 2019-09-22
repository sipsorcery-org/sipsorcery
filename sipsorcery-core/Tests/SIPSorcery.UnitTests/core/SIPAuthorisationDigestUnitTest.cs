using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.SIP.Core.UnitTests
{
    [TestClass]
    public class SIPAuthorisationDigestUnitTest
    {
        [TestMethod]
        public void SampleTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            Assert.IsTrue(true, "True was false.");
            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
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

        [TestMethod]
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

        [TestMethod]
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

        [TestMethod]
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

        [TestMethod]
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

        [TestMethod]
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

        [TestMethod]
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

        [TestMethod]
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

        [TestMethod]
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

        [TestMethod]
        public void KnownQOPUnitTest()
        {
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

        [TestMethod]
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

        [TestMethod]
        public void GenreateDigestTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPAuthorisationDigest authRequest = SIPAuthorisationDigest.ParseAuthorisationDigest(SIPAuthorisationHeadersEnum.WWWAuthenticate, @"digest realm=""sipsorcery.com"", nonce=""1265068315059e3bbf3052cf13ea5ca22fb71669a7"", opaque=""09c0f23f71f89ce53baab5664c09cbfa"", algorithm=MD5");
            authRequest.SetCredentials("username", "password", "sip:sipsorcery.com", "REGISTER");

            string digest = authRequest.Digest;

            Console.WriteLine("Digest = " + digest + ".");
            Console.WriteLine(authRequest.ToString());

            Assert.IsTrue(true, "True was false.");

            Console.WriteLine("-----------------------------------------");
        }
    }
}
