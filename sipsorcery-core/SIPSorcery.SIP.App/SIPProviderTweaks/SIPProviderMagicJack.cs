using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIPSorcery.SIP;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP.App {
    
    /// <summary>
    /// Tweaks the nonce on requests for provider MagicJack.
    /// </summary>
    public class SIPProviderMagicJack {

        public static ILog logger = AppState.logger;

        public static bool IsMagicJackRequest(SIPResponse sipResponse) {
            if (sipResponse.Header.AuthenticationHeader != null &&
                sipResponse.Header.AuthenticationHeader.SIPDigest.Realm == "stratus.com" &&
                sipResponse.Header.To != null && 
                sipResponse.Header.To.ToURI != null &&
                sipResponse.Header.To.ToURI.Host.ToLower() == "talk4free.com") {
                return true;
            }

            return false;
        }

        /// <summary>
        /// MagicJack apply a custom algorithm when calculating their nonce seemingly in order to
        /// prevent other SIP UAs from being able to authenticate. This method attempts to apply the same
        /// algorithm to allow the authenticated requests from this stack.
        /// </summary>
        /// <remarks>
        /// MJ is modifying the nonce dynamically. They append an underscore, then 8 characters to the nonce before computing the MD5. The 8 characters come from the call id. 
        /// Use the first 8 bytes of the nonce as an index into your call id. 
        /// Assume your callid is: 
        /// callid: 9876ABC56738DD43... 
        /// index:  0123456789ABCDEF
        /// and your nonce is: 8765abc4_32190
        /// Take the first digit of your nonce (which in our example is 8 ), and find the value in the callid at index 8, which is 6. 
        /// So, append that to the nonce. 8765abc4_32190_6 
        /// Then move on to the second digit of the nonce, which is 7 in our example. Find the value at index 7 in the callid, which is 5, and append that: 
        /// 8765abc4_32190_65 continue until you have done 8 digits. Your new nonce would be: 
        /// 8765abc4_32190_65CB38DA Use this value when computing the MD5, but pass the original nonce to magicJack. 
        /// </remarks>
        /// <param name="authReqdResponse"></param>
        /// <returns></returns>
        public static SIPAuthenticationHeader GetAuthenticationHeader(SIPResponse authReqdResponse) {
            try {
                SIPAuthenticationHeader mjAuthHeader = new SIPAuthenticationHeader(authReqdResponse.Header.AuthenticationHeader.SIPDigest);
                string origNonce = mjAuthHeader.SIPDigest.Nonce;
                string mjNonce = GetNonce(origNonce, authReqdResponse.Header.CallId);
                mjAuthHeader.SIPDigest.Nonce = mjNonce;
                mjAuthHeader.SIPDigest.Response = mjAuthHeader.SIPDigest.Digest;
                mjAuthHeader.SIPDigest.Nonce = origNonce;

                return mjAuthHeader;
            }
            catch (Exception excp) {
                logger.Error("Exception SIPProviderMagicJack GetAuthenticationHeader. " + excp.Message);
                throw;
            }
        }

        private static string GetNonce(string origNonce, string callId) {
            string mjNonce = origNonce + "_";
            for (int index = 0; index < 8; index++) {
                int callIdIndex = Convert.ToInt32(origNonce[index].ToString(), 16);
                mjNonce += callId[callIdIndex];
            }
            return mjNonce;
        }

        #region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class SIPURIUnitTest
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

			[Test]
			public void MagicJackAuthUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
	
                string nonce = "8765abc4_32190";
                SIPResponse authReqdResponse = new SIPResponse(SIPResponseStatusCodesEnum.Unauthorised, null, null);
                authReqdResponse.Header.CallId = "9876ABC56738DD43";
                authReqdResponse.Header.AuthenticationHeader = SIPAuthenticationHeader.ParseSIPAuthenticationHeader(SIPAuthorisationHeadersEnum.WWWAuthenticate, "Digest nonce=\"" + nonce + "\",realm=\"stratus.com\",algorithm=MD5");
                authReqdResponse.Header.AuthenticationHeader.SIPDigest.SetCredentials("username", "password", "sip:123@talk4free.com", SIPMethodsEnum.INVITE.ToString());

                Assert.IsTrue(SIPProviderMagicJack.IsMagicJackRequest(authReqdResponse), "The SIP Response was not correctly identified as being for a MagicJack request.");

                string mjNonce = GetNonce(authReqdResponse.Header.AuthenticationHeader.SIPDigest.Nonce, authReqdResponse.Header.CallId);
                SIPAuthenticationHeader mjAuthheader = SIPProviderMagicJack.GetAuthenticationHeader(authReqdResponse);

                Assert.IsTrue(mjNonce == "8765abc4_32190_65CB38DA", "The MagicJack nonce was not correctly generated.");
                Assert.IsTrue(authReqdResponse.Header.AuthenticationHeader.SIPDigest.Nonce == nonce, "The nonce set in the MagicJack response was not preserved correctly.");
                
				Console.WriteLine("-----------------------------------------");
			}
        }

        #endif

        #endregion
    }
}
