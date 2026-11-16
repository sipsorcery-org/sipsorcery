//-----------------------------------------------------------------------------
// Filename: SIPRequestMandatoryHeadersUnitTest.cs
//
// Description: Unit tests for the validation of the mandatory SIP request
// headers (Via, To, From, Call-ID and CSeq). See RFC 3261 section 8.1.1 and
// security advisory GHSA-vc6g-m22x-pm7r.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPRequestMandatoryHeadersUnitTest
    {
        private static readonly string CRLF = SIPConstants.CRLF;

        private const string VIA = "Via: SIP/2.0/UDP 127.0.0.1:5070;branch=z9hG4bK776asdhds";
        private const string TO = "To: <sip:bob@127.0.0.1>";
        private const string FROM = "From: <sip:alice@127.0.0.1>;tag=1928301774";
        private const string CALLID = "Call-ID: a84b4c76e66710@127.0.0.1";
        private const string CSEQ = "CSeq: 314159 INVITE";

        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPRequestMandatoryHeadersUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Builds a raw INVITE request with the mandatory headers, optionally swapping
        /// one of them out for a replacement line (or removing it if the replacement is null).
        /// </summary>
        private static string BuildInvite(string headerToReplace = null, string replacement = null)
        {
            var sb = new StringBuilder();
            sb.Append($"INVITE sip:bob@127.0.0.1 SIP/2.0{CRLF}");

            foreach (var header in new[] { VIA, TO, FROM, CALLID, CSEQ })
            {
                if (header == headerToReplace)
                {
                    if (replacement != null)
                    {
                        sb.Append(replacement).Append(CRLF);
                    }
                }
                else
                {
                    sb.Append(header).Append(CRLF);
                }
            }

            sb.Append($"Max-Forwards: 70{CRLF}");
            sb.Append($"Content-Length: 0{CRLF}{CRLF}");

            return sb.ToString();
        }

        private static SIPRequest Parse(string sipMsg)
        {
            var buffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            return SIPRequest.ParseSIPRequest(buffer);
        }

        /// <summary>
        /// Gets a programmatically constructed request that has all the mandatory headers.
        /// </summary>
        private static SIPRequest GetValidRequest(SIPMethodsEnum method = SIPMethodsEnum.INVITE)
        {
            var req = SIPRequest.GetRequest(
                method,
                SIPURI.ParseSIPURI("sip:bob@127.0.0.1"),
                new SIPToHeader(null, SIPURI.ParseSIPURI("sip:bob@127.0.0.1"), null),
                new SIPFromHeader(null, SIPURI.ParseSIPURI("sip:alice@127.0.0.1"), CallProperties.CreateNewTag()));

            req.Header.Vias.PushViaHeader(SIPViaHeader.GetDefaultSIPViaHeader(SIPProtocolsEnum.udp));

            return req;
        }

        /// <summary>
        /// A request with all the mandatory headers should pass validation.
        /// </summary>
        [Fact]
        public void ValidRequestPassesValidationUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var req = Parse(BuildInvite());

            Assert.True(req.IsValid(out var errorField, out var errorMessage));
            Assert.Equal(SIPValidationFieldsEnum.Unknown, errorField);
            Assert.Null(errorMessage);
        }

        /// <summary>
        /// A parsed request that is missing any one of the mandatory headers must fail validation
        /// and report the missing header.
        /// </summary>
        [Theory]
        [InlineData(TO, SIPValidationFieldsEnum.ToHeader)]
        [InlineData(FROM, SIPValidationFieldsEnum.FromHeader)]
        [InlineData(CALLID, SIPValidationFieldsEnum.CallIdHeader)]
        [InlineData(CSEQ, SIPValidationFieldsEnum.CSeqHeader)]
        public void ParsedRequestMissingMandatoryHeaderFailsValidationUnitTest(string missingHeader, SIPValidationFieldsEnum expectedField)
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var req = Parse(BuildInvite(missingHeader));

            Assert.False(req.IsValid(out var errorField, out var errorMessage));
            Assert.Equal(expectedField, errorField);
            Assert.False(string.IsNullOrEmpty(errorMessage));
        }

        /// <summary>
        /// A CSeq header with a negative sequence number is not valid (RFC 3261 section 8.1.1.5).
        /// </summary>
        [Fact]
        public void NegativeCSeqFailsValidationUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var req = Parse(BuildInvite(CSEQ, "CSeq: -5 INVITE"));

            Assert.False(req.IsValid(out var errorField, out _));
            Assert.Equal(SIPValidationFieldsEnum.CSeqHeader, errorField);
        }

        /// <summary>
        /// A Call-ID header with an empty or whitespace value is treated the same as a missing one.
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("	  ")]
        public void EmptyOrWhitespaceCallIdFailsValidationUnitTest(string callId)
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var req = GetValidRequest();
            req.Header.CallId = callId;

            Assert.False(req.IsValid(out var errorField, out _));
            Assert.Equal(SIPValidationFieldsEnum.CallIdHeader, errorField);
        }

        /// <summary>
        /// A missing Via header is already rejected by the header parser.
        /// </summary>
        [Fact]
        public void ParsedRequestMissingViaThrowsUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var excp = Assert.Throws<SIPValidationException>(() => Parse(BuildInvite(VIA)));
            Assert.Equal(SIPValidationFieldsEnum.ViaHeader, excp.SIPErrorField);
        }

        /// <summary>
        /// The Via check takes precedence when several mandatory headers are missing.
        /// </summary>
        [Fact]
        public void MultipleMissingHeadersReportsViaFirstUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var req = GetValidRequest();
            req.Header.Vias = new SIPViaSet();
            req.Header.To = null;
            req.Header.CallId = null;

            Assert.False(req.IsValid(out var errorField, out _));
            Assert.Equal(SIPValidationFieldsEnum.ViaHeader, errorField);
        }

        /// <summary>
        /// Creating a UAS INVITE transaction directly (i.e. not via the SIPTransport receive path)
        /// for a request with no To header must throw a SIPValidationException rather than a
        /// NullReferenceException (GHSA-vc6g-m22x-pm7r).
        /// </summary>
        [Fact]
        public void UASInviteTransactionRejectsMissingToHeaderUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using var transport = new SIPTransport();
            transport.AddSIPChannel(new MockSIPChannel(new IPEndPoint(IPAddress.Loopback, 0)));

            var req = GetValidRequest();
            req.Header.To = null;

            var excp = Assert.Throws<SIPValidationException>(() => new UASInviteTransaction(transport, req, null));
            Assert.Equal(SIPValidationFieldsEnum.ToHeader, excp.SIPErrorField);
        }

        /// <summary>
        /// Same as above for a missing From header.
        /// </summary>
        [Fact]
        public void UASInviteTransactionRejectsMissingFromHeaderUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using var transport = new SIPTransport();
            transport.AddSIPChannel(new MockSIPChannel(new IPEndPoint(IPAddress.Loopback, 0)));

            var req = GetValidRequest();
            req.Header.From = null;

            var excp = Assert.Throws<SIPValidationException>(() => new UASInviteTransaction(transport, req, null));
            Assert.Equal(SIPValidationFieldsEnum.FromHeader, excp.SIPErrorField);
        }

        /// <summary>
        /// The base SIPTransaction validation also applies to non-INVITE transactions.
        /// </summary>
        [Fact]
        public void NonInviteTransactionRejectsMissingCallIdUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using var transport = new SIPTransport();
            transport.AddSIPChannel(new MockSIPChannel(new IPEndPoint(IPAddress.Loopback, 0)));

            var req = GetValidRequest(SIPMethodsEnum.OPTIONS);
            req.Header.CallId = null;

            var excp = Assert.Throws<SIPValidationException>(() => new SIPNonInviteTransaction(transport, req, null));
            Assert.Equal(SIPValidationFieldsEnum.CallIdHeader, excp.SIPErrorField);
        }

        /// <summary>
        /// A valid request can still be used to create a UAS INVITE transaction.
        /// </summary>
        [Fact]
        public void UASInviteTransactionAcceptsValidRequestUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using var transport = new SIPTransport();
            transport.AddSIPChannel(new MockSIPChannel(new IPEndPoint(IPAddress.Loopback, 0)));

            var tx = new UASInviteTransaction(transport, GetValidRequest(), null);

            Assert.NotNull(tx);
        }

        /// <summary>
        /// An INVITE without a To header arriving at the SIP transport must be answered with a
        /// 400 Bad Request and must not be passed up to the application (GHSA-vc6g-m22x-pm7r).
        /// </summary>
        [Theory]
        [InlineData(TO)]
        [InlineData(FROM)]
        [InlineData(CALLID)]
        [InlineData(CSEQ)]
        public void TransportRejectsRequestMissingMandatoryHeaderWith400UnitTest(string missingHeader)
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using var transport = new SIPTransport();
            var channel = new MockSIPChannel(new IPEndPoint(IPAddress.Loopback, 5060));
            transport.AddSIPChannel(channel);

            bool requestReceived = false;
            transport.SIPTransportRequestReceived += (localEP, remoteEP, req) =>
            {
                requestReceived = true;
                return System.Threading.Tasks.Task.CompletedTask;
            };

            var localEP = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.Loopback, 5060));
            var remoteEP = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.Loopback, 5070));

            channel.FireMessageReceived(localEP, remoteEP, Encoding.UTF8.GetBytes(BuildInvite(missingHeader)));

            Assert.True(channel.SIPMessageSent.WaitOne(TimeSpan.FromSeconds(5)), "No response was sent for the invalid request.");
            Assert.StartsWith("SIP/2.0 400", channel.LastSIPMessageSent);
            Assert.False(requestReceived, "The invalid request should not have been passed to the application.");
        }
    }
}
