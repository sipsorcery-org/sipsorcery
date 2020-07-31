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

using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    /// <summary>
    /// Torture tests from RFC4475 https://tools.ietf.org/html/rfc4475
    /// Tests must be extracted from the base64 blob at the bottom of the RFC:
    /// $ cat torture.b64 | base64 -d > torture.tar.gz  
    /// $ tar zxvf torture.tar.gz
    /// Which gives the dat files needed.
    /// Cutting and pasting is no good as things like white space getting interpreted as end of line screws up
    /// intent of the tests.
    /// </summary>
    [Trait("Category", "unit")]
    public class SIPTortureTests
    {
        private static string CRLF = SIPConstants.CRLF;
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPTortureTests(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Torture test 3.1.1.1. with file wsinv.dat.
        /// </summary>
        [Fact(Skip = "Bit trickier to pass than anticipated.")]
        public void ShortTorturousInvite()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            Assert.True(File.Exists("wsinv.dat"), "The wsinv.dat torture test input file was missing.");

            string raw = File.ReadAllText("wsinv.dat");

            logger.LogDebug(raw);

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(raw), null, null);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(raw);

            Assert.NotNull(sipMessageBuffer);
            Assert.NotNull(inviteReq);

            logger.LogDebug("-----------------------------------------");
        }

        //rj2: RFC5118 (SIP) Torture Test Messages for IPv6
        //https://tools.ietf.org/html/rfc5118

        /// <summary>
        /// 4.1.  Valid SIP Message with an IPv6 Reference
        /// The request below is well-formatted according to the grammar in
        /// [RFC3261].  An IPv6 reference appears in the Request-URI(R-URI), Via
        /// header field, and Contact header field.
        /// </summary>
        [Fact]
        [Trait("Category", "IPv6Torture")]
        public void RFC5118_4_1()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
"REGISTER sip:[2001:db8::10] SIP/2.0" + CRLF +
"To: sip:user@example.com" + CRLF +
"From: sip:user@example.com;tag=81x2" + CRLF +
"Via: SIP/2.0/UDP [2001:db8::9:1];branch=z9hG4bKas3-111" + CRLF +
"Call-ID: SSG9559905523997077@hlau_4100" + CRLF +
"Max-Forwards: 70" + CRLF +
"Contact: \"Caller\" <sip:caller@[2001:db8::1]>" + CRLF +
"CSeq: 98176 REGISTER" + CRLF +
"Content-Length: 0";


            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            Assert.True(sipMessageBuffer != null, "The SIP message not parsed correctly.");
            SIPRequest sipRequest = SIPRequest.ParseSIPRequest(sipMessageBuffer);
            Assert.Equal(SIPMethodsEnum.REGISTER, sipRequest.Method);
            IPAddress ip6;
            Assert.NotEmpty(sipRequest.Header.Vias.Via);
            Assert.True(IPAddress.TryParse(sipRequest.Header.Vias.TopViaHeader.ReceivedFromAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.NotEmpty(sipRequest.Header.Contact);
            Assert.True(IPAddress.TryParse(sipRequest.Header.Contact[0].ContactURI.HostAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.True(IPAddress.TryParse(sipRequest.URI.HostAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// 4.2.  Invalid SIP Message with an IPv6 Reference
        /// The request below is not well-formatted according to the grammar in
        /// [RFC3261].  The IPv6 reference in the R-URI does not contain the
        /// mandated delimiters for an IPv6 reference("[" and "]").
        /// A SIP implementation receiving this request should respond with a 400
        /// Bad Request error.
        /// </summary>
        [Fact]
        [Trait("Category", "IPv6Torture")]
        public void RFC5118_4_2()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
"REGISTER sip:2001:db8::10 SIP/2.0" + CRLF +
"To: sip:user@example.com" + CRLF +
"From: sip:user@example.com;tag=81x2" + CRLF +
"Via: SIP/2.0/UDP [2001:db8::9:1];branch=z9hG4bKas3-111" + CRLF +
"Call-ID: SSG9559905523997077@hlau_4100" + CRLF +
"Max-Forwards: 70" + CRLF +
"Contact: \"Caller\" <sip:caller@[2001:db8::1]>" + CRLF +
"CSeq: 98176 REGISTER" + CRLF +
"Content-Length: 0";

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            Assert.True(sipMessageBuffer != null, "The SIP message not parsed correctly.");
            Assert.Throws<SIPValidationException>(() => SIPRequest.ParseSIPRequest(sipMessageBuffer));

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// 4.3.  Port Ambiguous in a SIP URI
        /// From a parsing perspective, the request below is well-formed.
        /// However, from a semantic point of view, it will not yield the desired
        /// result.Implementations must ensure that when a raw IPv6 address
        /// appears in a SIP URI, then a port number, if required, appears
        /// outside the closing "]" delimiting the IPv6 reference.  Raw IPv6
        /// addresses can occur in many header fields, including the Contact,
        /// Route, and Record-Route header fields.They also can appear as the
        /// result of the "sent-by" production rule of the Via header field.
        /// Implementers are urged to consult the ABNF in [RFC3261] for a
        /// complete list of fields where a SIP URI can appear.
        /// </summary>
        [Fact]
        [Trait("Category", "IPv6Torture")]
        public void RFC5118_4_3()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
"REGISTER sip:[2001:db8::10:5070] SIP/2.0" + CRLF +
"To: sip:user@example.com" + CRLF +
"From: sip:user@example.com;tag=81x2" + CRLF +
"Via: SIP/2.0/UDP [2001:db8::9:1];branch=z9hG4bKas3-111" + CRLF +
"Call-ID: SSG9559905523997077@hlau_4100" + CRLF +
"Contact: \"Caller\" <sip:caller@[2001:db8::1]>" + CRLF +
"Max-Forwards: 70" + CRLF +
"CSeq: 98176 REGISTER" + CRLF +
"Content-Length: 0";

            //parsing is correct, but port is ambiguous, 
            //intention was to target port 5070
            //but that's nothing a program can find out

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            Assert.True(sipMessageBuffer != null, "The SIP message not parsed correctly.");
            SIPRequest sipRequest = SIPRequest.ParseSIPRequest(sipMessageBuffer);
            Assert.Equal(SIPMethodsEnum.REGISTER, sipRequest.Method);
            IPAddress ip6;
            Assert.NotEmpty(sipRequest.Header.Vias.Via);
            Assert.True(IPAddress.TryParse(sipRequest.Header.Vias.TopViaHeader.ReceivedFromAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.NotEmpty(sipRequest.Header.Contact);
            Assert.True(IPAddress.TryParse(sipRequest.Header.Contact[0].ContactURI.HostAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.True(IPAddress.TryParse(sipRequest.URI.HostAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// 4.4.  Port Unambiguous in a SIP URI
        /// In contrast to the example in Section 4.3, the following REGISTER
        /// request leaves no ambiguity whatsoever on where the IPv6 address ends
        /// and the port number begins.This REGISTER request is well formatted
        /// per the grammar in [RFC3261].
        /// </summary>
        [Fact]
        [Trait("Category", "IPv6Torture")]
        public void RFC5118_4_4()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
"REGISTER sip:[2001:db8::10]:5070 SIP/2.0" + CRLF +
"To: sip:user@example.com" + CRLF +
"From: sip:user@example.com;tag=81x2" + CRLF +
"Via: SIP/2.0/UDP [2001:db8::9:1];branch=z9hG4bKas3-111" + CRLF +
"Call-ID: SSG9559905523997077@hlau_4100" + CRLF +
"Contact: \"Caller\" <sip:caller@[2001:db8::1]>" + CRLF +
"Max-Forwards: 70" + CRLF +
"CSeq: 98176 REGISTER" + CRLF +
"Content-Length: 0";


            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            Assert.True(sipMessageBuffer != null, "The SIP message not parsed correctly.");
            SIPRequest sipRequest = SIPRequest.ParseSIPRequest(sipMessageBuffer);
            Assert.Equal("5070", sipRequest.URI.HostPort);
            Assert.Equal(SIPMethodsEnum.REGISTER, sipRequest.Method);
            IPAddress ip6;
            Assert.NotEmpty(sipRequest.Header.Vias.Via);
            Assert.True(IPAddress.TryParse(sipRequest.Header.Vias.TopViaHeader.ReceivedFromAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.NotEmpty(sipRequest.Header.Contact);
            Assert.True(IPAddress.TryParse(sipRequest.Header.Contact[0].ContactURI.HostAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.True(IPAddress.TryParse(sipRequest.URI.HostAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// 4.5.  IPv6 Reference Delimiters in Via Header
        /// The request below contains an IPv6 address in the Via "received"
        /// parameter.The IPv6 address is delimited by "[" and "]".  Even
        /// though this is not a valid request based on a strict interpretation
        /// of the grammar in [RFC3261], robust implementations must nonetheless
        /// be able to parse the topmost Via header field and continue processing
        /// the request.
        /// </summary>
        [Fact]
        [Trait("Category", "IPv6Torture")]
        public void RFC5118_4_5_1()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
"BYE sip:[2001:db8::10] SIP/2.0" + CRLF +
"To: sip:user@example.com;tag=bd76ya" + CRLF +
"From: sip:user@example.com;tag=81x2" + CRLF +
"Via: SIP/2.0/UDP [2001:db8::9:1];received=[2001:db8::9:255];branch=z9hG4bKas3-111" + CRLF +
"Call-ID: SSG9559905523997077@hlau_4100" + CRLF +
"Max-Forwards: 70" + CRLF +
"CSeq: 321 BYE" + CRLF +
"Content-Length: 0";

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            Assert.True(sipMessageBuffer != null, "The SIP message not parsed correctly.");
            SIPRequest sipRequest = SIPRequest.ParseSIPRequest(sipMessageBuffer);
            Assert.Equal(SIPMethodsEnum.BYE, sipRequest.Method);
            IPAddress ip6;
            Assert.NotEmpty(sipRequest.Header.Vias.Via);
            Assert.True(IPAddress.TryParse(sipRequest.Header.Vias.TopViaHeader.ReceivedFromAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.True(IPAddress.TryParse(sipRequest.Header.Vias.TopViaHeader.ReceivedFromIPAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.True(IPAddress.TryParse(sipRequest.URI.HostAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// 4.5.  IPv6 Reference Delimiters in Via Header
        /// The OPTIONS request below contains an IPv6 address in the Via
        /// "received" parameter without the adorning "[" and "]".  This request
        /// is valid according to the grammar in [RFC3261].
        /// </summary>
        [Fact]
        [Trait("Category", "IPv6Torture")]
        public void RFC5118_4_5_2()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
"OPTIONS sip:[2001:db8::10] SIP/2.0" + CRLF +
"To: sip:user @example.com" + CRLF +
"From: sip:user @example.com; tag=81x2" + CRLF +
"Via: SIP/2.0/UDP [2001:db8::9:1];received=2001:db8::9:255;branch=z9hG4bKas3" + CRLF +
"Call-ID: SSG95523997077 @hlau_4100" + CRLF +
"Max-Forwards: 70" + CRLF +
"Contact: \"Caller\" <sip:caller@[2001:db8::9:1]>" + CRLF +
"CSeq: 921 OPTIONS" + CRLF +
"Content-Length: 0";

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            Assert.True(sipMessageBuffer != null, "The SIP message not parsed correctly.");
            SIPRequest sipRequest = SIPRequest.ParseSIPRequest(sipMessageBuffer);
            Assert.Equal(SIPMethodsEnum.OPTIONS, sipRequest.Method);
            IPAddress ip6;
            Assert.NotEmpty(sipRequest.Header.Vias.Via);
            Assert.True(IPAddress.TryParse(sipRequest.Header.Vias.TopViaHeader.ReceivedFromAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.True(IPAddress.TryParse(sipRequest.Header.Vias.TopViaHeader.ReceivedFromIPAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.NotEmpty(sipRequest.Header.Contact);
            Assert.True(IPAddress.TryParse(sipRequest.Header.Contact[0].ContactURI.HostAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.True(IPAddress.TryParse(sipRequest.URI.HostAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// 4.6.  SIP Request with IPv6 Addresses in Session Description Protocol
        /// This request below is valid and well-formed according to the grammar
        /// in [RFC3261].  Note that the IPv6 addresses in the SDP[RFC4566] body
        /// do not have the delimiting "[" and "]".
        /// </summary>
        [Fact]
        [Trait("Category", "IPv6Torture")]
        public void RFC5118_4_6()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
"INVITE sip:user@[2001:db8::10] SIP/2.0" + CRLF +
"To: sip:user@[2001:db8::10]" + CRLF +
"From: sip:user@example.com;tag=81x2" + CRLF +
"Via: SIP/2.0/UDP [2001:db8::20];branch=z9hG4bKas3-111" + CRLF +
"Call-ID: SSG9559905523997077@hlau_4100" + CRLF +
"Contact: \"Caller\" <sip:caller@[2001:db8::20]>" + CRLF +
"CSeq: 8612 INVITE" + CRLF +
"Max-Forwards: 70" + CRLF +
"Content-Type: application/sdp" + CRLF +
"Content-Length: 268" + CRLF +
CRLF +
"v=0" + CRLF +
"o=assistant 971731711378798081 0 IN IP6 2001:db8::20" + CRLF +
"s=Live video feed for today's meeting" + CRLF +
"c=IN IP6 2001:db8::20" + CRLF +
"t=3338481189 3370017201" + CRLF +
"m=audio 6000 RTP/AVP 2" + CRLF +
"a=rtpmap:2 G726-32/8000" + CRLF +
"m=video 6024 RTP/AVP 107" + CRLF +
"a=rtpmap:107 H263-1998/90000";


            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            Assert.True(sipMessageBuffer != null, "The SIP message not parsed correctly.");
            SIPRequest sipRequest = SIPRequest.ParseSIPRequest(sipMessageBuffer);
            Assert.Equal(SIPMethodsEnum.INVITE, sipRequest.Method);
            IPAddress ip6;
            Assert.NotEmpty(sipRequest.Header.Vias.Via);
            Assert.True(IPAddress.TryParse(sipRequest.Header.Vias.TopViaHeader.ReceivedFromAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.NotEmpty(sipRequest.Header.Contact);
            Assert.True(IPAddress.TryParse(sipRequest.Header.Contact[0].ContactURI.HostAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.True(IPAddress.TryParse(sipRequest.URI.HostAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.False(string.IsNullOrWhiteSpace(sipRequest.Body));
            SDP sdp = SDP.ParseSDPDescription(sipRequest.Body);
            Assert.NotNull(sdp);
            Assert.NotNull(sdp.Connection);
            Assert.True(IPAddress.TryParse(sdp.Connection.ConnectionAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.NotEmpty(sdp.Media);

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// 4.7.  Multiple IP Addresses in SIP Headers
        /// The request below is valid and well-formed according to the grammar
        /// in [RFC3261].  The Via list contains a mix of IPv4 addresses and IPv6
        /// references.
        /// </summary>
        [Fact]
        [Trait("Category", "IPv6Torture")]
        public void RFC5118_4_7()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
"BYE sip:user@host.example.net SIP/2.0" + CRLF +
"Via: SIP/2.0/UDP [2001:db8::9:1]:6050;branch=z9hG4bKas3-111" + CRLF +
"Via: SIP/2.0/UDP 192.0.2.1;branch=z9hG4bKjhja8781hjuaij65144" + CRLF +
"Via: SIP/2.0/TCP [2001:db8::9:255];branch=z9hG4bK451jj;received=192.0.2.200" + CRLF +
"Call-ID: 997077@lau_4100" + CRLF +
"Max-Forwards: 70" + CRLF +
"CSeq: 89187 BYE" + CRLF +
"To: sip:user@example.net;tag=9817--94" + CRLF +
"From: sip:user@example.com;tag=81x2" + CRLF +
"Content-Length: 0";


            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            Assert.True(sipMessageBuffer != null, "The SIP message not parsed correctly.");
            SIPRequest sipRequest = SIPRequest.ParseSIPRequest(sipMessageBuffer);
            Assert.Equal(SIPMethodsEnum.BYE, sipRequest.Method);
            IPAddress ip6, ip4;
            Assert.NotEmpty(sipRequest.Header.Vias.Via);
            Assert.True(sipRequest.Header.Vias.Length == 3);
            Assert.True(IPAddress.TryParse(sipRequest.Header.Vias.TopViaHeader.ReceivedFromAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.Equal(SIPProtocolsEnum.udp, sipRequest.Header.Vias.TopViaHeader.Transport);
            Assert.Equal(6050, sipRequest.Header.Vias.TopViaHeader.Port);
            Assert.True(IPAddress.TryParse(sipRequest.Header.Vias.BottomViaHeader.Host, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.True(IPAddress.TryParse(sipRequest.Header.Vias.BottomViaHeader.ReceivedFromIPAddress, out ip4));
            Assert.Equal(AddressFamily.InterNetwork, ip4.AddressFamily);
            Assert.Equal(SIPProtocolsEnum.tcp, sipRequest.Header.Vias.BottomViaHeader.Transport);
            sipRequest.Header.Vias.PopTopViaHeader();
            Assert.True(IPAddress.TryParse(sipRequest.Header.Vias.TopViaHeader.ReceivedFromAddress, out ip4));
            Assert.Equal(AddressFamily.InterNetwork, ip4.AddressFamily);
            Assert.Equal(SIPProtocolsEnum.udp, sipRequest.Header.Vias.TopViaHeader.Transport);
            Assert.False(IPAddress.TryParse(sipRequest.URI.HostAddress, out ip6));

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// 4.8.  Multiple IP Addresses in SDP
        /// The request below is valid and well-formed according to the grammar
        /// in [RFC3261].  The SDP contains multiple media lines, and each media
        /// line is identified by a different network connection address.
        /// </summary>
        [Fact]
        [Trait("Category", "IPv6Torture")]
        public void RFC5118_4_8()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
"INVITE sip:user@[2001:db8::10] SIP/2.0" + CRLF +
"To: sip:user@[2001:db8::10]" + CRLF +
"From: sip:user@example.com;tag=81x2" + CRLF +
"Via: SIP/2.0/UDP [2001:db8::9:1];branch=z9hG4bKas3-111" + CRLF +
"Call-ID: SSG9559905523997077@hlau_4100" + CRLF +
"Contact: \"Caller\" <sip:caller@[2001:db8::9:1]>" + CRLF +
"Max-Forwards: 70" + CRLF +
"CSeq: 8912 INVITE" + CRLF +
"Content-Type: application/sdp" + CRLF +
"Content-Length: 181" + CRLF +
CRLF +
"v=0" + CRLF +
"o=bob 280744730 28977631 IN IP4 host.example.com" + CRLF +
"s=" + CRLF +
"t=0 0" + CRLF +
"m=audio 22334 RTP/AVP 0" + CRLF +
"c=IN IP4 192.0.2.1" + CRLF +
"m=video 6024 RTP/AVP 107" + CRLF +
"c=IN IP6 2001:db8::1" + CRLF +
"a=rtpmap:107 H263-1998/90000";


            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            Assert.True(sipMessageBuffer != null, "The SIP message not parsed correctly.");
            SIPRequest sipRequest = SIPRequest.ParseSIPRequest(sipMessageBuffer);
            Assert.Equal(SIPMethodsEnum.INVITE, sipRequest.Method);
            IPAddress ip6, ip4;
            Assert.NotEmpty(sipRequest.Header.Vias.Via);
            Assert.True(IPAddress.TryParse(sipRequest.Header.Vias.TopViaHeader.ReceivedFromAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.NotEmpty(sipRequest.Header.Contact);
            Assert.True(IPAddress.TryParse(sipRequest.Header.Contact[0].ContactURI.HostAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.True(IPAddress.TryParse(sipRequest.URI.HostAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.False(string.IsNullOrWhiteSpace(sipRequest.Body));
            SDP sdp = SDP.ParseSDPDescription(sipRequest.Body);
            Assert.NotNull(sdp);
            //Assert.NotNull(sdp.Connection);
            //Assert.True(IPAddress.TryParse(sdp.Connection.ConnectionAddress, out ip4));
            //Assert.Equal(AddressFamily.InterNetwork, ip4.AddressFamily);
            Assert.NotEmpty(sdp.Media);
            Assert.NotNull(sdp.Media[0].Connection);
            Assert.True(IPAddress.TryParse(sdp.Media[0].Connection.ConnectionAddress, out ip4));
            Assert.Equal(AddressFamily.InterNetwork, ip4.AddressFamily);
            Assert.NotNull(sdp.Media[1].Connection);
            Assert.True(IPAddress.TryParse(sdp.Media[1].Connection.ConnectionAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// 4.9.  IPv4-Mapped IPv6 Addresses
        /// The message below is well-formed according to the grammar in
        /// [RFC3261].  The Via list contains two Via headers, both of which
        /// include an IPv4-mapped IPv6 address.An IPv4-mapped IPv6 address
        /// also appears in the Contact header and the SDP.The topmost Via
        /// header includes a port number that is appropriately delimited by "]".
        /// </summary>
        [Fact]
        [Trait("Category", "IPv6Torture")]
        public void RFC5118_4_9()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
"INVITE sip:user@example.com SIP/2.0" + CRLF +
"To: sip:user@example.com" + CRLF +
"From: sip:user@east.example.com;tag=81x2" + CRLF +
"Via: SIP/2.0/UDP [::ffff:192.0.2.10]:19823;branch=z9hG4bKbh19" + CRLF +
"Via: SIP/2.0/UDP [::ffff:192.0.2.2];branch=z9hG4bKas3-111" + CRLF +
"Call-ID: SSG9559905523997077@hlau_4100" + CRLF +
"Contact: \"T. desk phone\" <sip:ted@[::ffff:192.0.2.2]>" + CRLF +
"CSeq: 612 INVITE" + CRLF +
"Max-Forwards: 70" + CRLF +
"Content-Type: application/sdp" + CRLF +
"Content-Length: 236" + CRLF +
CRLF +
"v=0" + CRLF +
"o=assistant 971731711378798081 0 IN IP6 ::ffff:192.0.2.2" + CRLF +
"s=Call me soon, please!" + CRLF +
"c=IN IP6 ::ffff:192.0.2.2" + CRLF +
"t=3338481189 3370017201" + CRLF +
"m=audio 6000 RTP/AVP 2" + CRLF +
"a=rtpmap:2 G726-32/8000" + CRLF +
"m=video 6024 RTP/AVP 107" + CRLF +
"a=rtpmap:107 H263-1998/90000";


            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            Assert.True(sipMessageBuffer != null, "The SIP message not parsed correctly.");
            SIPRequest sipRequest = SIPRequest.ParseSIPRequest(sipMessageBuffer);
            Assert.Equal(SIPMethodsEnum.INVITE, sipRequest.Method);
            IPAddress ip6;
            Assert.NotEmpty(sipRequest.Header.Vias.Via);
            Assert.True(IPAddress.TryParse(sipRequest.Header.Vias.TopViaHeader.Host, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.Equal(19823, sipRequest.Header.Vias.TopViaHeader.Port);
            Assert.True(IPAddress.TryParse(sipRequest.Header.Vias.BottomViaHeader.ReceivedFromAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.NotEmpty(sipRequest.Header.Contact);
            Assert.True(IPAddress.TryParse(sipRequest.Header.Contact[0].ContactURI.HostAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.False(IPAddress.TryParse(sipRequest.URI.HostAddress, out ip6));
            Assert.False(string.IsNullOrWhiteSpace(sipRequest.Body));
            SDP sdp = SDP.ParseSDPDescription(sipRequest.Body);
            Assert.NotNull(sdp);
            Assert.NotNull(sdp.Connection);
            Assert.True(IPAddress.TryParse(sdp.Connection.ConnectionAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);
            Assert.NotEmpty(sdp.Media);

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// 4.10.  IPv6 Reference Bug in RFC 3261 ABNF
        /// The message below includes an extra colon in the IPv6 reference.  A
        /// SIP implementation receiving such a message may exhibit robustness by
        /// successfully parsing the IPv6 reference(it can choose to ignore the
        /// extra colon when parsing the IPv6 reference.If the SIP
        /// implementation is acting in the role of a proxy, it may additionally
        /// serialize the message without the extra colon to aid the next
        /// downstream server).
        /// </summary>
        [Fact]
        [Trait("Category", "IPv6Torture")]
        public void RFC5118_4_10_1()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
"OPTIONS sip:user@[2001:db8:::192.0.2.1] SIP/2.0" + CRLF +
"To: sip:user@[2001:db8:::192.0.2.1]" + CRLF +
"From: sip:user@example.com;tag=810x2" + CRLF +
"Via: SIP/2.0/UDP lab1.east.example.com;branch=z9hG4bKas3-111" + CRLF +
"Call-ID: G9559905523997077@hlau_4100" + CRLF +
"CSeq: 689 OPTIONS" + CRLF +
"Max-Forwards: 70" + CRLF +
"Content-Length: 0";


            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            Assert.True(sipMessageBuffer != null, "The SIP message not parsed correctly.");
            SIPRequest sipRequest = SIPRequest.ParseSIPRequest(sipMessageBuffer);
            Assert.Equal(SIPMethodsEnum.OPTIONS, sipRequest.Method);
            IPAddress ip6;
            Assert.NotEmpty(sipRequest.Header.Vias.Via);
            Assert.False(IPAddress.TryParse(sipRequest.Header.Vias.TopViaHeader.ReceivedFromAddress, out ip6));
            Assert.True(IPAddress.TryParse(sipRequest.URI.HostAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);


            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// 4.10.  IPv6 Reference Bug in RFC 3261 ABNF
        /// The next message has the correct syntax for the IPv6 reference in the
        /// R-URI.
        /// </summary>
        [Fact]
        [Trait("Category", "IPv6Torture")]
        public void RFC5118_4_10_2()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
"OPTIONS sip:user@[2001:db8::192.0.2.1] SIP/2.0" + CRLF +
"To: sip:user@[2001:db8::192.0.2.1]" + CRLF +
"From: sip:user@example.com;tag=810x2" + CRLF +
"Via: SIP/2.0/UDP lab1.east.example.com;branch=z9hG4bKas3-111" + CRLF +
"Call-ID: G9559905523997077@hlau_4100" + CRLF +
"CSeq: 689 OPTIONS" + CRLF +
"Max-Forwards: 70" + CRLF +
"Content-Length: 0";

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
            Assert.True(sipMessageBuffer != null, "The SIP message not parsed correctly.");
            SIPRequest sipRequest = SIPRequest.ParseSIPRequest(sipMessageBuffer);
            Assert.Equal(SIPMethodsEnum.OPTIONS, sipRequest.Method);
            IPAddress ip6;
            Assert.NotEmpty(sipRequest.Header.Vias.Via);
            Assert.False(IPAddress.TryParse(sipRequest.Header.Vias.TopViaHeader.ReceivedFromAddress, out ip6));
            Assert.True(IPAddress.TryParse(sipRequest.URI.HostAddress, out ip6));
            Assert.Equal(AddressFamily.InterNetworkV6, ip6.AddressFamily);


            logger.LogDebug("-----------------------------------------");
        }
    }
}
