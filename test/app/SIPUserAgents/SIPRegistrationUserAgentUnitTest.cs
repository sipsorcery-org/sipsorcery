using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using Xunit;

namespace SIPSorcery.UnitTests.app.SIPUserAgents
{
    [Trait("Category", "unit")]
    public class SIPRegistrationUserAgentUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPRegistrationUserAgentUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void RegisterStartWithCustomHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPTransport transport = new SIPTransport();
            MockSIPChannel channel = new MockSIPChannel(new System.Net.IPEndPoint(IPAddress.Any, 0));
            transport.AddSIPChannel(channel);
            SIPRegistrationUserAgent userAgent = new SIPRegistrationUserAgent(
                transport,
                null,
                new SIPURI("alice", "192.168.11.50", null, SIPSchemesEnum.sip, SIPProtocolsEnum.udp),
                "alice",
                "password123",
                null,
                "192.168.11.50",
                new SIPURI(SIPSchemesEnum.sip, IPAddress.Any, 0),
                120,
                null,
                new[] { "My-Header: value" });

            userAgent.Start();
            
            channel.SIPMessageSent.WaitOne(5000);
            Assert.Contains("My-Header: value", channel.LastSIPMessageSent);

            userAgent.Stop();
        }
    }
}
