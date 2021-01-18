//-----------------------------------------------------------------------------
// Filename: SIPTransportUnitTest.cs
//
// Description: Unit tests for the SIPTransport class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 18 Jan 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "transport")]
    public class SIPTransportUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPTransportUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that setting a custom ContactHost gets correctly applied to a SIP Request.
        /// </summary>
        [Fact]
        public async Task TestSetRequestContactHostUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPEndPoint dummyEP = new SIPEndPoint(new IPEndPoint(IPAddress.Any, 5060));

            string inviteReqStr =
@"INVITE sip:dummy@127.0.0.1:12014 SIP/2.0
Via: SIP/2.0/UDP 127.0.0.1:1234;branch=z9hG4bK5f37455955ca433a902f8fea0ce2dc27
To: <sip:dummy@127.0.0.1:12014>
From: <sip:unittest@mysipswitch.com>;tag=2062917371
Call-ID: 8ae45c15425040179a4285d774ccbaf6
CSeq: 1 INVITE
Contact: <sip:127.0.0.1:1234>
Max-Forwards: 70
User-Agent: unittest
Content-Length: 5
Content-Type: application/sdp

dummy";
            var sipReqBuffer = SIPMessageBuffer.ParseSIPMessage(inviteReqStr, dummyEP, dummyEP);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipReqBuffer);

            using (var transport = new SIPTransport())
            {
                transport.AddSIPChannel(new MockSIPChannel(dummyEP.GetIPEndPoint()));

                transport.ContactHost = "custom.sipsorcery.com";

                await transport.SendRequestAsync(inviteReq);

                logger.LogDebug(inviteReq.ToString());

                Assert.Equal(transport.ContactHost, inviteReq.Header.Contact[0].ContactURI.Host);
            }
        }

        /// <summary>
        /// Tests that setting a custom ContactHost gets correctly applied to a SIP Request when
        /// an IP address is used.
        /// </summary>
        [Fact]
        public async Task TestSetRequestContactHostIPAddressUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPEndPoint dummyEP = new SIPEndPoint(new IPEndPoint(IPAddress.Any, 5060));

            string inviteReqStr =
@"INVITE sip:dummy@127.0.0.1:12014 SIP/2.0
Via: SIP/2.0/UDP 127.0.0.1:1234;branch=z9hG4bK5f37455955ca433a902f8fea0ce2dc27
To: <sip:dummy@127.0.0.1:12014>
From: <sip:unittest@mysipswitch.com>;tag=2062917371
Call-ID: 8ae45c15425040179a4285d774ccbaf6
CSeq: 1 INVITE
Contact: <sip:127.0.0.1:1234>
Max-Forwards: 70
User-Agent: unittest
Content-Length: 5
Content-Type: application/sdp

dummy";
            var sipReqBuffer = SIPMessageBuffer.ParseSIPMessage(inviteReqStr, dummyEP, dummyEP);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipReqBuffer);

            using (var transport = new SIPTransport())
            {
                transport.AddSIPChannel(new MockSIPChannel(dummyEP.GetIPEndPoint()));

                transport.ContactHost = "10.0.0.1";

                await transport.SendRequestAsync(inviteReq);

                logger.LogDebug(inviteReq.ToString());

                Assert.Equal($"{transport.ContactHost}:5060", inviteReq.Header.Contact[0].ContactURI.Host);
            }
        }

        /// <summary>
        /// Tests that setting a custom ContactHost gets correctly applied to a SIP Response
        /// </summary>
        [Fact]
        public async Task TestSetResponseContactHostUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPEndPoint dummyEP = new SIPEndPoint(new IPEndPoint(IPAddress.Any, 5060));

            string okRespStr =
@"SIP/2.0 200 OK
Via: SIP/2.0/UDP 127.0.0.1:1234;branch=z9hG4bK5f37455955ca433a902f8fea0ce2dc27;rport=12013
To: <sip:dummy@127.0.0.1:12014>
From: <sip:unittest@mysipswitch.com>;tag=2062917371
Call-ID: 8ae45c15425040179a4285d774ccbaf6
Contact: <sip:127.0.0.1:1234>
CSeq: 1 INVITE
Content-Length: 5
Content-Type: application/sdp

dummy";

            var sipRespBuffer = SIPMessageBuffer.ParseSIPMessage(okRespStr, dummyEP, dummyEP);
            SIPResponse okResponse = SIPResponse.ParseSIPResponse(sipRespBuffer);

            using (var transport = new SIPTransport())
            {
                transport.AddSIPChannel(new MockSIPChannel(dummyEP.GetIPEndPoint()));

                transport.ContactHost = "custom.sipsorcery.com";

                await transport.SendResponseAsync(okResponse);

                logger.LogDebug(okResponse.ToString());

                Assert.Equal(transport.ContactHost, okResponse.Header.Contact[0].ContactURI.Host);
            }
        }

        /// <summary>
        /// Tests that setting a custom ContactHost gets correctly applied to a SIP response when
        /// an IP address is used.
        /// </summary>
        [Fact]
        public async Task TestSetResponseContactHostIPAddressUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPEndPoint dummyEP = new SIPEndPoint(new IPEndPoint(IPAddress.Any, 5060));

            string okRespStr =
@"SIP/2.0 200 OK
Via: SIP/2.0/UDP 127.0.0.1:1234;branch=z9hG4bK5f37455955ca433a902f8fea0ce2dc27;rport=12013
To: <sip:dummy@127.0.0.1:12014>
From: <sip:unittest@mysipswitch.com>;tag=2062917371
Call-ID: 8ae45c15425040179a4285d774ccbaf6
Contact: <sip:127.0.0.1:1234>
CSeq: 1 INVITE
Content-Length: 5
Content-Type: application/sdp

dummy";

            var sipRespBuffer = SIPMessageBuffer.ParseSIPMessage(okRespStr, dummyEP, dummyEP);
            SIPResponse okResponse = SIPResponse.ParseSIPResponse(sipRespBuffer);

            using (var transport = new SIPTransport())
            {
                transport.AddSIPChannel(new MockSIPChannel(dummyEP.GetIPEndPoint()));

                transport.ContactHost = "192.168.0.12";

                await transport.SendResponseAsync(okResponse);

                logger.LogDebug(okResponse.ToString());

                Assert.Equal($"{transport.ContactHost}:5060", okResponse.Header.Contact[0].ContactURI.Host);
            }
        }

        /// <summary>
        /// Tests that setting a custom header callback gets correctly applied to a SIP Request.
        /// </summary>
        [Fact]
        public async Task TestSetRequestCustomHeaderFuncUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPEndPoint dummyEP = new SIPEndPoint(new IPEndPoint(IPAddress.Any, 5060));

            string inviteReqStr =
@"INVITE sip:dummy@127.0.0.1:12014 SIP/2.0
Via: SIP/2.0/UDP 127.0.0.1:1234;branch=z9hG4bK5f37455955ca433a902f8fea0ce2dc27
To: <sip:dummy@127.0.0.1:12014>
From: <sip:unittest@mysipswitch.com>;tag=2062917371
Call-ID: 8ae45c15425040179a4285d774ccbaf6
CSeq: 1 INVITE
Contact: <sip:127.0.0.1:1234>
Max-Forwards: 70
User-Agent: unittest
Content-Length: 5
Content-Type: application/sdp

dummy";
            var sipReqBuffer = SIPMessageBuffer.ParseSIPMessage(inviteReqStr, dummyEP, dummyEP);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipReqBuffer);

            using (var transport = new SIPTransport())
            {
                transport.AddSIPChannel(new MockSIPChannel(dummyEP.GetIPEndPoint()));

                string contactHost = "devcall.sipsorcery.com";
                transport.CustomiseRequestHeader = (local, dst, req) =>
                {
                    var hdr = req.Header.Copy();
                    hdr.Contact[0].ContactURI.Host = contactHost;
                    return hdr;
                };

                await transport.SendRequestAsync(inviteReq);

                logger.LogDebug(inviteReq.ToString());

                Assert.Equal(contactHost, inviteReq.Header.Contact[0].ContactURI.Host);
            }
        }

        /// <summary>
        /// Tests that setting a custom header callback gets correctly applied to a SIP Response.
        /// </summary>
        [Fact]
        public async Task TestSetResponseCustomHeaderFuncUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPEndPoint dummyEP = new SIPEndPoint(new IPEndPoint(IPAddress.Any, 5060));

            string okRespStr =
@"SIP/2.0 200 OK
Via: SIP/2.0/UDP 127.0.0.1:1234;branch=z9hG4bK5f37455955ca433a902f8fea0ce2dc27;rport=12013
To: <sip:dummy@127.0.0.1:12014>
From: <sip:unittest@mysipswitch.com>;tag=2062917371
Call-ID: 8ae45c15425040179a4285d774ccbaf6
Contact: <sip:127.0.0.1:1234>
CSeq: 1 INVITE
Content-Length: 5
Content-Type: application/sdp

dummy";

            var sipRespBuffer = SIPMessageBuffer.ParseSIPMessage(okRespStr, dummyEP, dummyEP);
            SIPResponse okResponse = SIPResponse.ParseSIPResponse(sipRespBuffer);

            using (var transport = new SIPTransport())
            {
                transport.AddSIPChannel(new MockSIPChannel(dummyEP.GetIPEndPoint()));

                string contactHost = "devcall.sipsorcery.com";
                transport.CustomiseResponseHeader = (local, dst, resp) =>
                {
                    var hdr = resp.Header.Copy();
                    hdr.Contact[0].ContactURI.Host = contactHost;
                    return hdr;
                };

                await transport.SendResponseAsync(okResponse);

                logger.LogDebug(okResponse.ToString());

                Assert.Equal(contactHost, okResponse.Header.Contact[0].ContactURI.Host);
            }
        }
    }
}
