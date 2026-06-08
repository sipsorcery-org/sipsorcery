//-----------------------------------------------------------------------------
// Filename: SIPTransportContactHeaderUnitTest.cs
//
// Description: Regression tests for SIPTransport.AdjustHeadersForEndPoint Contact
// header handling. These pin two independent adjustments the method makes to a
// single Contact header before a request/response is sent:
//
//   1. Host override   - a custom SIPTransport.ContactHost (or a wildcard host)
//                        is replaced with the appropriate value.
//   2. Transport override - for a sip-scheme Contact sent over a non-UDP
//                        transport (and not a REGISTER) the Contact URI's
//                        transport is set to the send protocol so the peer
//                        replies over the same connection.
//
// The transport override (2) is INDEPENDENT of the host override (1): in
// particular it must still be applied when a custom ContactHost is configured.
// PR #1682 moved the transport-override block inside the "no ContactHost" branch,
// which silently dropped the transport parameter whenever ContactHost was set and
// the send was over TCP/TLS/WS. RegressionGuard below fails against that change.
//
// AdjustHeadersForEndPoint is internal (InternalsVisibleTo SIPSorcery.UnitTests) and is a pure
// function of its arguments and the ContactHost field (no I/O), so it is called directly.
//
// Author(s):
// Aaron Clauson
//
// History:
// 08 Jun 2026	Aaron Clauson	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPTransportContactHeaderUnitTest
    {
        private static readonly string m_CRLF = SIPConstants.CRLF;

        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPTransportContactHeaderUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// REGRESSION GUARD (PR #1682).
        /// With a custom ContactHost configured AND the send going over a non-UDP transport, both the host
        /// override and the transport override must be applied: the Contact host becomes the ContactHost and
        /// the Contact URI transport becomes the send protocol (tcp here). If the transport override is
        /// (incorrectly) skipped when ContactHost is set, the peer would default to UDP for replies.
        /// </summary>
        [Fact]
        public void ContactHostSet_NonUdpSend_StillAppliesTransportOverride()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var transport = new SIPTransport();
            try
            {
                transport.ContactHost = "sipsorcery.example.com";
                var header = ParseRequest("INVITE", "<sip:alice@192.168.1.2:5065>").Header;
                var sendFrom = new SIPEndPoint(SIPProtocolsEnum.tcp, IPAddress.Loopback, 5060);

                var adjusted = transport.AdjustHeadersForEndPoint(sendFrom, header);

                Assert.Single(adjusted.Contact);
                // Host override applied (confirms the ContactHost branch executed).
                Assert.Equal("sipsorcery.example.com", adjusted.Contact[0].ContactURI.Host);
                // Transport override applied (the regression): must be tcp, not the default udp.
                Assert.Equal(SIPProtocolsEnum.tcp, adjusted.Contact[0].ContactURI.Protocol);
            }
            finally
            {
                transport.Shutdown();
            }
        }

        /// <summary>
        /// Characterization: with no ContactHost configured, a sip-scheme Contact sent over a non-UDP
        /// transport (non-REGISTER) still gets its transport set to the send protocol. (Passes both before
        /// and after PR #1682; documents the baseline behaviour the regression guard relies on.)
        /// </summary>
        [Fact]
        public void NoContactHost_NonUdpSend_AppliesTransportOverride()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var transport = new SIPTransport();
            try
            {
                var header = ParseRequest("INVITE", "<sip:alice@192.168.1.2:5065>").Header;
                var sendFrom = new SIPEndPoint(SIPProtocolsEnum.tcp, IPAddress.Loopback, 5060);

                var adjusted = transport.AdjustHeadersForEndPoint(sendFrom, header);

                Assert.Equal(SIPProtocolsEnum.tcp, adjusted.Contact[0].ContactURI.Protocol);
            }
            finally
            {
                transport.Shutdown();
            }
        }

        /// <summary>
        /// Characterization: REGISTER requests are exempt from the transport override (the caller may want
        /// to choose the registration scheme), so the Contact transport is left at its original value even
        /// when sending over a non-UDP transport. (Stable across PR #1682.)
        /// </summary>
        [Fact]
        public void RegisterRequest_NonUdpSend_DoesNotApplyTransportOverride()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var transport = new SIPTransport();
            try
            {
                transport.ContactHost = "sipsorcery.example.com";
                var header = ParseRequest("REGISTER", "<sip:alice@192.168.1.2:5065>").Header;
                var sendFrom = new SIPEndPoint(SIPProtocolsEnum.tcp, IPAddress.Loopback, 5060);

                var adjusted = transport.AdjustHeadersForEndPoint(sendFrom, header);

                Assert.Equal("sipsorcery.example.com", adjusted.Contact[0].ContactURI.Host);
                // REGISTER is excluded from the transport override, so it stays at the default udp.
                Assert.Equal(SIPProtocolsEnum.udp, adjusted.Contact[0].ContactURI.Protocol);
            }
            finally
            {
                transport.Shutdown();
            }
        }

        /// <summary>
        /// Characterization: a UDP send leaves the Contact transport untouched (the override only fires for
        /// non-UDP send protocols).
        /// </summary>
        [Fact]
        public void ContactHostSet_UdpSend_DoesNotApplyTransportOverride()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var transport = new SIPTransport();
            try
            {
                transport.ContactHost = "sipsorcery.example.com";
                var header = ParseRequest("INVITE", "<sip:alice@192.168.1.2:5065>").Header;
                var sendFrom = new SIPEndPoint(SIPProtocolsEnum.udp, IPAddress.Loopback, 5060);

                var adjusted = transport.AdjustHeadersForEndPoint(sendFrom, header);

                Assert.Equal("sipsorcery.example.com", adjusted.Contact[0].ContactURI.Host);
                Assert.Equal(SIPProtocolsEnum.udp, adjusted.Contact[0].ContactURI.Protocol);
            }
            finally
            {
                transport.Shutdown();
            }
        }

        // ---- helpers ----

        private static SIPRequest ParseRequest(string method, string contact)
        {
            string sipMsg =
                $"{method} sip:bob@example.com SIP/2.0{m_CRLF}" +
                $"Via: SIP/2.0/UDP 192.168.1.2:5065;branch=z9hG4bKregressiontest{m_CRLF}" +
                $"From: <sip:alice@example.com>;tag=1{m_CRLF}" +
                $"To: <sip:bob@example.com>{m_CRLF}" +
                $"Contact: {contact}{m_CRLF}" +
                $"Call-ID: regression-test@192.168.1.2{m_CRLF}" +
                $"CSeq: 1 {method}{m_CRLF}" +
                $"Max-Forwards: 70{m_CRLF}{m_CRLF}";

            var buffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            return SIPRequest.ParseSIPRequest(buffer);
        }
    }
}
