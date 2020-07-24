using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.SIP.App.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPClientUserAgentUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPClientUserAgentUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void CallWithAdjustedInviteHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPTransport transport = new SIPTransport();
            MockSIPChannel channel = new MockSIPChannel(new System.Net.IPEndPoint(IPAddress.Any, 0));
            transport.AddSIPChannel(channel);
            SIPClientUserAgent userAgent = new SIPClientUserAgent(
                transport,
                new SIPEndPoint(new IPEndPoint(new IPAddress(new byte[] { 192, 168, 11, 50 }), 5060)),
                "owner",
                "admin",
                null);
            SIPContactHeader testHeader = new SIPContactHeader("Contact Name", new SIPURI("User", "Host", "Param=Value"));
            userAgent.AdjustInvite = invite =>
            {
                invite.Header.Contact = new List<SIPContactHeader>
                {
                    testHeader
                };
                return invite;
            };

            var desc = new SIPCallDescriptor(
                "user",
                "pass",
                "sip:user@host",
                "sip:user@host",
                "sip:destination@destinationhost",
                null,
                new List<string>(),
                "user",
                SIPCallDirection.Out,
                null,
                null,
                null);
            userAgent.Call(desc);

            channel.SIPMessageSent.WaitOne(5000);
            Assert.Contains(testHeader.ToString(), channel.LastSIPMessageSent);
        }
    }
}
