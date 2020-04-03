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

using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPContactHeaderUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPContactHeaderUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void ParseContactHeaderDomainForUserTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testContactHeader = "<sip:sip.domain.com@sip.domain.com>";

            List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

            Assert.True(sipContactHeaderList[0].ContactName == null, "The Contact header name was not parsed correctly.");
            Assert.True(sipContactHeaderList[0].ContactURI.ToString() == "sip:sip.domain.com@sip.domain.com", "The Contact header URI was not parsed correctly.");
        }

        [Fact]
        public void ParseBadAastraContactHeaderUserTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testContactHeader = "<sip:10001@127.0.0.1:5060\n";

            Assert.Throws<SIPValidationException>(() => SIPContactHeader.ParseContactHeader(testContactHeader));
        }

        [Fact]
        public void ParseNoAngleQuotesContactHeaderUserTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testContactHeader = "sip:10001@127.0.0.1:5060";

            List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

            logger.LogDebug("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI);

            Assert.True(sipContactHeaderList[0].ContactName == null, "The Contact header name was not parsed correctly.");
            Assert.True(sipContactHeaderList[0].ContactURI.ToString() == "sip:10001@127.0.0.1:5060", "The Contact header URI was not parsed correctly.");
        }

        [Fact]
        public void ParseCiscoContactHeaderUserTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testContactHeader = "<sip:user@127.0.0.1:5060;user=phone;transport=udp>;+sip.instance=\"<urn:uuid:00000000-0000-0000-0000-0006d74b0e72>\";+u.sip!model.ccm.cisco.com=\"7\"";

            List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

            logger.LogDebug("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI.ToString());
            logger.LogDebug("Contact Header ContactParams = " + sipContactHeaderList[0].ContactParameters.ToString());

            Assert.True(sipContactHeaderList[0].ContactName == null, "The Contact header name was not parsed correctly.");
            Assert.True(sipContactHeaderList[0].ContactURI.ToString() == "sip:user@127.0.0.1:5060;user=phone;transport=udp", "The Contact header URI was not parsed correctly, parsed valued = " + sipContactHeaderList[0].ContactURI.ToString() + ".");
            Assert.True(sipContactHeaderList[0].ContactParameters.ToString() == ";+sip.instance=\"<urn:uuid:00000000-0000-0000-0000-0006d74b0e72>\";+u.sip!model.ccm.cisco.com=\"7\"", "The Contact header Parameters were not parsed correctly.");
        }

        [Fact]
        public void ParseNoLineBreakContactHeaderUserTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testContactHeader = "<sip:10001@127.0.0.1:5060\nAllow: OPTIONS";

            logger.LogDebug("Contact Header = " + testContactHeader + ".");

            Assert.Throws<SIPValidationException>(() => SIPContactHeader.ParseContactHeader(testContactHeader));
        }

        [Fact]
        public void ParseContactWithParamHeaderUserTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testContactHeader = "<sip:user@127.0.0.1:5060;ftag=1233>";

            List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

            logger.LogDebug("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI.ToString());
            logger.LogDebug("Contact Header ContactParams = " + sipContactHeaderList[0].ContactParameters.ToString());

            Assert.True(sipContactHeaderList[0].ContactName == null, "The Contact header name was not parsed correctly.");
            Assert.True(sipContactHeaderList[0].ContactURI.ToString() == "sip:user@127.0.0.1:5060;ftag=1233", "The Contact header URI was not parsed correctly, parsed valued = " + sipContactHeaderList[0].ContactURI.ToString() + ".");
            Assert.True(sipContactHeaderList[0].ContactURI.Parameters.Get("ftag") == "1233", "The Contact header ftag URI parameter was not parsed correctly.");
        }

        [Fact]
        public void ParseExpiresContactHeaderUserTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testContactHeader = "<sip:user@127.0.0.1:5060>; expires=60";

            List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

            logger.LogDebug("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI.ToString());
            logger.LogDebug("Contact Header ContactParams = " + sipContactHeaderList[0].ContactParameters.ToString());
            logger.LogDebug("Contact = " + sipContactHeaderList[0].ToString());

            Assert.True(sipContactHeaderList[0].ContactName == null, "The Contact header name was not parsed correctly.");
            Assert.True(sipContactHeaderList[0].ContactURI.ToString() == "sip:user@127.0.0.1:5060", "The Contact header URI was not parsed correctly, parsed valued = " + sipContactHeaderList[0].ContactURI.ToString() + ".");
            Assert.True(sipContactHeaderList[0].Expires == 60, "The Contact header Expires parameter was not parsed correctly.");
        }

        [Fact]
        public void ParseZeroExpiresContactHeaderUserTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testContactHeader = "<sip:user@127.0.0.1:5060>; expires=0";

            List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

            logger.LogDebug("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI.ToString());
            logger.LogDebug("Contact Header ContactParams = " + sipContactHeaderList[0].ContactParameters.ToString());
            logger.LogDebug("Contact = " + sipContactHeaderList[0].ToString());

            Assert.True(sipContactHeaderList[0].ContactName == null, "The Contact header name was not parsed correctly.");
            Assert.True(sipContactHeaderList[0].ContactURI.ToString() == "sip:user@127.0.0.1:5060", "The Contact header URI was not parsed correctly, parsed valued = " + sipContactHeaderList[0].ContactURI.ToString() + ".");
            Assert.True(sipContactHeaderList[0].Expires == 0, "The Contact header Expires parameter was not parsed correctly.");
        }

        [Fact]
        public void MultipleContactsHeaderUserTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testContactHeader = "\"Mr. Watson\" <sip:watson@worcester.bell-telephone.com>;q=0.7; expires=3600, \"Mr. Watson\" <sip:watson@bell-telephone.com> ;q=0.1";

            List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

            logger.LogDebug("Contact Header Count=" + sipContactHeaderList.Count + ".");
            logger.LogDebug("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI.ToString());
            logger.LogDebug("Contact Header ContactParams = " + sipContactHeaderList[0].ContactParameters.ToString());

            Assert.True(sipContactHeaderList[0].ContactName == "Mr. Watson", "The Contact header name was not parsed correctly.");
            Assert.True(sipContactHeaderList[0].ContactURI.ToString() == "sip:watson@worcester.bell-telephone.com", "The Contact header URI was not parsed correctly, parsed valued = " + sipContactHeaderList[0].ContactURI.ToString() + ".");
            Assert.True(sipContactHeaderList[0].Expires == 3600, "The Contact header Expires parameter was not parsed correctly.");
            Assert.True(sipContactHeaderList[0].Q == "0.7", "The Contact header Q parameter was not parsed correctly.");
        }

        [Fact]
        public void MultipleContactsWithURIParamsHeaderUserTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testContactHeader = "\"Mr. Watson\" <sip:watson@worcester.bell-telephone.com;ftag=1232>;q=0.7; expires=3600, \"Mr. Watson\" <sip:watson@bell-telephone.com?nonsense=yes> ;q=0.1";

            List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

            logger.LogDebug("Contact Header Count=" + sipContactHeaderList.Count + ".");
            logger.LogDebug("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI.ToString());
            logger.LogDebug("Contact Header ContactParams = " + sipContactHeaderList[0].ContactParameters.ToString());

            Assert.True(sipContactHeaderList[0].ContactName == "Mr. Watson", "The Contact header name was not parsed correctly.");
            Assert.True(sipContactHeaderList[0].ContactURI.ToString() == "sip:watson@worcester.bell-telephone.com;ftag=1232", "The Contact header URI was not parsed correctly, parsed valued = " + sipContactHeaderList[0].ContactURI.ToString() + ".");
            Assert.True(sipContactHeaderList[0].Expires == 3600, "The Contact header Expires parameter was not parsed correctly.");
            Assert.True(sipContactHeaderList[0].Q == "0.7", "The Contact header Q parameter was not parsed correctly.");
            Assert.True(sipContactHeaderList[0].ContactURI.Parameters.Get("ftag") == "1232", "The Contact header URI ftag parameter was not parsed correctly.");
            Assert.True(sipContactHeaderList[1].ContactURI.Headers.Get("nonsense") == "yes", "The Contact header URI nonsense header was not parsed correctly.");
        }

        [Fact]
        public void SimpleAreEqualUserTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPContactHeader contactHeader1 = new SIPContactHeader(null, SIPURI.ParseSIPURI("sip:user@127.0.0.1:5060"));
            SIPContactHeader contactHeader2 = new SIPContactHeader(null, SIPURI.ParseSIPURI("sip:user@127.0.0.1:5060"));

            Assert.True(SIPContactHeader.AreEqual(contactHeader1, contactHeader2), "The Contact headers were not correctly identified as equal.");
        }

        [Fact]
        public void SimpleNotEqualUserTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPContactHeader contactHeader1 = new SIPContactHeader(null, SIPURI.ParseSIPURI("sip:user@127.0.0.1:5060"));
            SIPContactHeader contactHeader2 = new SIPContactHeader(null, SIPURI.ParseSIPURI("sip:user@127.0.0.2:5060"));

            Assert.False(SIPContactHeader.AreEqual(contactHeader1, contactHeader2), "The Contact headers were not correctly identified as equal.");
        }

        [Fact]
        public void WithParametersAreEqualUserTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPContactHeader contactHeader1 = new SIPContactHeader(SIPUserField.ParseSIPUserField("<sip:user@127.0.0.1:5060>;param1=value1"));
            SIPContactHeader contactHeader2 = new SIPContactHeader(SIPUserField.ParseSIPUserField("<sip:user@127.0.0.1:5060>;param1=value1"));

            Assert.True(SIPContactHeader.AreEqual(contactHeader1, contactHeader2), "The Contact headers were not correctly identified as equal.");
        }

        [Fact]
        public void WithExpiresParametersAreEqualUserTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPContactHeader contactHeader1 = new SIPContactHeader(SIPUserField.ParseSIPUserField("<sip:user@127.0.0.1:5060> ;expires=0; param1=value1"));
            SIPContactHeader contactHeader2 = new SIPContactHeader(SIPUserField.ParseSIPUserField("<sip:user@127.0.0.1:5060>;expires=50;param1=value1"));

            Assert.True(SIPContactHeader.AreEqual(contactHeader1, contactHeader2), "The Contact headers were not correctly identified as equal.");
        }

        [Fact]
        public void WithDifferentNamesAreEqualUserTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPContactHeader contactHeader1 = new SIPContactHeader(SIPUserField.ParseSIPUserField("\"Joe Bloggs\" <sip:user@127.0.0.1:5060> ;expires=0; param1=value1"));
            SIPContactHeader contactHeader2 = new SIPContactHeader(SIPUserField.ParseSIPUserField("\"Jane Doe\" <sip:user@127.0.0.1:5060>;expires=50;param1=value1"));

            Assert.True(SIPContactHeader.AreEqual(contactHeader1, contactHeader2), "The Contact headers were not correctly identified as equal.");
        }
    }
}
