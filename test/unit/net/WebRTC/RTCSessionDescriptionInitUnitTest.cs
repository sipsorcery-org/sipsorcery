//-----------------------------------------------------------------------------
// Filename: RTCSessionDescriptionInitUnitTest.cs
//
// Description: Unit tests for the RTCSessionDescriptionInitUnitTest class.
//
// History:
// 05 Oct 2020	Aaron Clauson	Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCSessionDescriptionInitUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTCSessionDescriptionInitUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests serialising to and from JSON.
        /// </summary>
        [Fact]
        public void JsonRoundtripUnitTest()
        {
            RTCPeerConnection pcSrc = new RTCPeerConnection(null);
            var videoTrackSrc = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000) });
            pcSrc.addTrack(videoTrackSrc);
            
            var offer = pcSrc.createOffer(new RTCOfferOptions());

            Assert.NotNull(offer.toJSON());

            logger.LogDebug($"offer: {offer.toJSON()}");

            var parseResult = RTCSessionDescriptionInit.TryParse(offer.toJSON(), out var init);

            Assert.True(parseResult);

            Assert.Equal(RTCSdpType.offer, init.type);
            Assert.NotNull(init.sdp);

            SDP sdp = SDP.ParseSDPDescription(init.sdp);
            Assert.Equal(0, sdp.Version);
        }

        /// <summary>
        /// Tests that a JSON string from a web browser can be parsed correctly.
        /// </summary>
        [Fact]
        public void ParseJavascriptJsonTest()
        {
            string json = "{\"type\":\"answer\",\"sdp\":\"v=0\r\no=- 3619871509827895381 2 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\na=group:BUNDLE 0\r\na=msid-semantic: WMS\r\nm=video 9 UDP/TLS/RTP/SAVP 100\r\nc=IN IP4 0.0.0.0\r\na=rtcp:9 IN IP4 0.0.0.0\r\na=ice-ufrag:XqpN\r\na=ice-pwd:VOXKGB0AIf10Kqtv+zcQKgLF\r\na=ice-options:trickle\r\na=fingerprint:sha-256 D4:6E:F7:FD:B3:4F:4D:3D:3A:B3:92:2C:CC:F6:4E:46:88:B7:01:E9:B7:E1:77:03:8E:BB:AA:DC:26:1B:9D:2E\r\na=setup:active\r\na=mid:0\r\na=recvonly\r\na=rtcp-mux\r\na=rtpmap:100 VP8/90000\r\n\"}";

            var parseResult = RTCSessionDescriptionInit.TryParse(json, out var init);

            Assert.True(parseResult);

            Assert.Equal(RTCSdpType.answer, init.type);
            Assert.NotNull(init.sdp);
        }
    }
}
