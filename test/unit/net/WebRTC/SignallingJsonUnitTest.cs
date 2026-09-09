//-----------------------------------------------------------------------------
// Filename: SignallingJsonUnitTest.cs
//
// Description: Unit tests for the hand rolled JSON used by the WebRTC signalling
// types, RTCIceCandidateInit and RTCSessionDescriptionInit.
//
// The emphasis is on the tolerances that matter when interoperating with other
// WebRTC stacks: free member order, unknown members, escaping, and the shapes
// that browsers and general purpose serialisers actually put on the wire.
//
// History:
// 09 Sep 2026	Aaron Clauson	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class SignallingJsonUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SignallingJsonUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        #region RTCIceCandidateInit

        /// <summary>
        /// A candidate as produced by Chrome, with the members in the order the browser emits.
        /// </summary>
        [Fact]
        public void CandidateInit_ChromeShape_Parses()
        {
            const string json = "{\"candidate\":\"candidate:842163049 1 udp 1677729535 203.0.113.20 54321 typ srflx raddr 0.0.0.0 rport 0 generation 0 ufrag Xqp3 network-cost 999\",\"sdpMid\":\"0\",\"sdpMLineIndex\":0,\"usernameFragment\":\"Xqp3\"}";

            Assert.True(RTCIceCandidateInit.TryParse(json, out var init));
            Assert.StartsWith("candidate:842163049 1 udp", init.candidate);
            Assert.Equal("0", init.sdpMid);
            Assert.Equal(0, init.sdpMLineIndex);
            Assert.Equal("Xqp3", init.usernameFragment);
        }

        /// <summary>
        /// Member order is not significant.
        /// </summary>
        [Fact]
        public void CandidateInit_ReorderedMembers_Parses()
        {
            const string json = "{\"usernameFragment\":\"ufrag\",\"sdpMLineIndex\":3,\"sdpMid\":\"video\",\"candidate\":\"candidate:1 1 udp 100 192.0.2.1 5000 typ host\"}";

            Assert.True(RTCIceCandidateInit.TryParse(json, out var init));
            Assert.Equal("video", init.sdpMid);
            Assert.Equal(3, init.sdpMLineIndex);
            Assert.Equal("ufrag", init.usernameFragment);
        }

        /// <summary>
        /// Pretty printed JSON, as a signalling server might forward it.
        /// </summary>
        [Fact]
        public void CandidateInit_Whitespace_Parses()
        {
            const string json = "  {\n  \"candidate\" : \"candidate:1 1 udp 100 192.0.2.1 5000 typ host\" ,\n  \"sdpMid\" : \"0\" ,\n  \"sdpMLineIndex\" : 1\n}  ";

            Assert.True(RTCIceCandidateInit.TryParse(json, out var init));
            Assert.Equal("0", init.sdpMid);
            Assert.Equal(1, init.sdpMLineIndex);
        }

        /// <summary>
        /// Members the library does not know about must not prevent a parse, including
        /// nested objects and arrays.
        /// </summary>
        [Fact]
        public void CandidateInit_UnknownMembers_AreSkipped()
        {
            const string json = "{\"candidate\":\"candidate:1 1 udp 100 192.0.2.1 5000 typ host\",\"extra\":{\"nested\":{\"deep\":[1,2,{\"a\":\"}\"}]},\"b\":true},\"list\":[\"x\",\"y\"],\"sdpMid\":\"0\",\"sdpMLineIndex\":2,\"flag\":null}";

            Assert.True(RTCIceCandidateInit.TryParse(json, out var init));
            Assert.Equal("0", init.sdpMid);
            Assert.Equal(2, init.sdpMLineIndex);
        }

        /// <summary>
        /// An explicit JSON null for an optional member leaves it unset.
        /// </summary>
        [Fact]
        public void CandidateInit_NullUsernameFragment_Parses()
        {
            const string json = "{\"candidate\":\"candidate:1 1 udp 100 192.0.2.1 5000 typ host\",\"sdpMid\":\"0\",\"sdpMLineIndex\":0,\"usernameFragment\":null}";

            Assert.True(RTCIceCandidateInit.TryParse(json, out var init));
            Assert.Null(init.usernameFragment);
        }

        /// <summary>
        /// Some stacks quote the m line index rather than sending it as a number.
        /// </summary>
        [Fact]
        public void CandidateInit_QuotedLineIndex_Parses()
        {
            const string json = "{\"candidate\":\"candidate:1 1 udp 100 192.0.2.1 5000 typ host\",\"sdpMid\":\"0\",\"sdpMLineIndex\":\"7\"}";

            Assert.True(RTCIceCandidateInit.TryParse(json, out var init));
            Assert.Equal(7, init.sdpMLineIndex);
        }

        /// <summary>
        /// A null sdpMid fails the required member check but still yields an object, which
        /// is the behaviour callers depend on to distinguish "not for me" from "malformed".
        /// </summary>
        [Fact]
        public void CandidateInit_NullSdpMid_ReturnsFalseWithObject()
        {
            const string json = "{\"candidate\":\"candidate:1 1 udp 100 192.0.2.1 5000 typ host\",\"sdpMid\":null}";

            Assert.False(RTCIceCandidateInit.TryParse(json, out var init));
            Assert.NotNull(init);
            Assert.Null(init.sdpMid);
        }

        [Theory]
        [InlineData("{")]
        [InlineData("{\"candidate\":}")]
        [InlineData("{\"candidate\" \"x\"}")]
        [InlineData("{\"candidate\":\"unterminated}")]
        [InlineData("{\"a\":1,}")]
        [InlineData("[]")]
        [InlineData("\"a string\"")]
        [InlineData("42")]
        public void CandidateInit_Malformed_ReturnsFalseWithNull(string json)
        {
            Assert.False(RTCIceCandidateInit.TryParse(json, out var init));
            Assert.Null(init);
        }

        /// <summary>
        /// A null optional member is omitted from the output rather than written as null,
        /// which is the shape a browser produces.
        /// </summary>
        [Fact]
        public void CandidateInit_NullMembers_AreOmittedFromJson()
        {
            var init = new RTCIceCandidateInit
            {
                candidate = "candidate:1 1 udp 100 192.0.2.1 5000 typ host",
                sdpMid = "0",
                sdpMLineIndex = 0
            };

            Assert.Equal(
                "{\"candidate\":\"candidate:1 1 udp 100 192.0.2.1 5000 typ host\",\"sdpMid\":\"0\",\"sdpMLineIndex\":0}",
                init.toJSON());
        }

        #endregion

        #region RTCSessionDescriptionInit

        /// <summary>
        /// The session description type must go on the wire as a string. A browser rejects
        /// the underlying integer, which is what a general purpose serialiser emits by
        /// default for an enum.
        /// </summary>
        [Theory]
        [InlineData(RTCSdpType.offer, "offer")]
        [InlineData(RTCSdpType.answer, "answer")]
        [InlineData(RTCSdpType.pranswer, "pranswer")]
        [InlineData(RTCSdpType.rollback, "rollback")]
        public void SessionDescription_TypeIsWrittenAsString(RTCSdpType type, string expected)
        {
            var init = new RTCSessionDescriptionInit { type = type, sdp = "v=0" };

            Assert.Equal("{\"type\":\"" + expected + "\",\"sdp\":\"v=0\"}", init.toJSON());
        }

        /// <summary>
        /// Read side tolerance for a peer that serialised the enum as its underlying value.
        /// </summary>
        [Theory]
        [InlineData(0, RTCSdpType.answer)]
        [InlineData(1, RTCSdpType.offer)]
        [InlineData(2, RTCSdpType.pranswer)]
        [InlineData(3, RTCSdpType.rollback)]
        public void SessionDescription_NumericType_Parses(int numeric, RTCSdpType expected)
        {
            string json = "{\"type\":" + numeric + ",\"sdp\":\"v=0\"}";

            Assert.True(RTCSessionDescriptionInit.TryParse(json, out var init));
            Assert.Equal(expected, init.type);
        }

        /// <summary>
        /// The CRLF sequences that fill an SDP body must survive a round trip. This is the
        /// escaping the previous hand built attempt in the source comments got wrong.
        /// </summary>
        [Fact]
        public void SessionDescription_SdpCrLf_RoundTrips()
        {
            var init = new RTCSessionDescriptionInit
            {
                type = RTCSdpType.offer,
                sdp = "v=0\r\no=- 1 2 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\n"
            };

            var json = init.toJSON();

            Assert.DoesNotContain("\r", json);
            Assert.DoesNotContain("\n", json);
            Assert.Contains("\\r\\n", json);

            Assert.True(RTCSessionDescriptionInit.TryParse(json, out var parsed));
            Assert.Equal(init.sdp, parsed.sdp);
            Assert.Equal(RTCSdpType.offer, parsed.type);
        }

        /// <summary>
        /// Quotes, backslashes and control characters in a value must not break the output.
        /// </summary>
        [Fact]
        public void SessionDescription_SpecialCharacters_RoundTrip()
        {
            var init = new RTCSessionDescriptionInit
            {
                type = RTCSdpType.answer,
                sdp = "s=a \"quoted\" name\r\ni=back\\slash\r\na=tab\there\r\na=unit\u0001sep\r\n"
            };

            var json = init.toJSON();

            logger.LogDebug("json: {Json}", json);

            Assert.Contains("\\\"quoted\\\"", json);
            Assert.Contains("back\\\\slash", json);
            Assert.Contains("\\t", json);
            Assert.Contains("\\u0001", json);

            Assert.True(RTCSessionDescriptionInit.TryParse(json, out var parsed));
            Assert.Equal(init.sdp, parsed.sdp);
        }

        /// <summary>
        /// Non-ASCII is emitted unescaped but a peer is free to send it escaped.
        /// </summary>
        [Fact]
        public void SessionDescription_UnicodeEscapes_Parse()
        {
            const string json = "{\"type\":\"offer\",\"sdp\":\"s=caf\\u00e9 \\ud83d\\ude00\\r\\nv=0\\r\\n\"}";

            Assert.True(RTCSessionDescriptionInit.TryParse(json, out var init));
            Assert.Equal("s=café 😀\r\nv=0\r\n", init.sdp);

            // And that value survives being written back out.
            Assert.True(RTCSessionDescriptionInit.TryParse(init.toJSON(), out var round));
            Assert.Equal(init.sdp, round.sdp);
        }

        /// <summary>
        /// A forward slash may be escaped by the sender. It is a legal escape that this
        /// library does not itself produce.
        /// </summary>
        [Fact]
        public void SessionDescription_EscapedSolidus_Parses()
        {
            const string json = "{\"type\":\"offer\",\"sdp\":\"a=rtpmap:96 VP8\\/90000\\r\\n\"}";

            Assert.True(RTCSessionDescriptionInit.TryParse(json, out var init));
            Assert.Equal("a=rtpmap:96 VP8/90000\r\n", init.sdp);
        }

        [Fact]
        public void SessionDescription_UnknownMembers_AreSkipped()
        {
            const string json = "{\"id\":7,\"meta\":{\"from\":\"peer\",\"tags\":[\"a\",\"b\"]},\"type\":\"answer\",\"sdp\":\"v=0\\r\\n\"}";

            Assert.True(RTCSessionDescriptionInit.TryParse(json, out var init));
            Assert.Equal(RTCSdpType.answer, init.type);
            Assert.Equal("v=0\r\n", init.sdp);
        }

        [Theory]
        [InlineData("{")]
        [InlineData("{\"sdp\":}")]
        [InlineData("not json at all")]
        [InlineData("[{\"sdp\":\"v=0\"}]")]
        public void SessionDescription_Malformed_ReturnsFalseWithNull(string json)
        {
            Assert.False(RTCSessionDescriptionInit.TryParse(json, out var init));
            Assert.Null(init);
        }

        /// <summary>
        /// A full offer produced by the library must survive a round trip unchanged.
        /// </summary>
        [Fact]
        public void SessionDescription_LibraryOffer_RoundTrips()
        {
            var pc = new RTCPeerConnection(null);
            pc.addTrack(new MediaStreamTrack(SDPMediaTypesEnum.video, false,
                new System.Collections.Generic.List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000)
                }));

            var offer = pc.createOffer(new RTCOfferOptions());
            var json = offer.toJSON();

            Assert.True(RTCSessionDescriptionInit.TryParse(json, out var parsed));
            Assert.Equal(offer.type, parsed.type);
            Assert.Equal(offer.sdp, parsed.sdp);
            Assert.Equal(json, parsed.toJSON());
        }

        #endregion
    }
}
