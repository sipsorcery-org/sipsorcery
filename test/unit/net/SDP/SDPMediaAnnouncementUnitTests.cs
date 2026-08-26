using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    /// <summary>This class contains unit tests for SDPMediaAnnouncement</summary>
    [Trait("Category", "unit")]
    public class SDPMediaAnnouncementUnitTests
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SDPMediaAnnouncementUnitTests(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Checks that the SDP with Message Media is well formatted.
        /// </summary>
        [Fact]
        public void InvalidPortInRemoteOfferTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var remoteOffer = new SDP();

            var sessionPort = 5523;
            var sessionEndpoint = "10vMB2Ee;tcp";

            remoteOffer.Connection = new SDPConnectionInformation(IPAddress.Loopback);

            var messageMediaFormat = new SDPMessageMediaFormat();
            messageMediaFormat.IP = remoteOffer.Connection.ConnectionAddress;
            messageMediaFormat.Port = sessionPort.ToString();
            messageMediaFormat.Endpoint = sessionEndpoint;
            messageMediaFormat.AcceptTypes = new List<string>
            {
                "text/plain",
                "text/x-msrp-heartbeat"
            };

            SDPMediaAnnouncement messageAnnouncement = new SDPMediaAnnouncement(
                SDPMediaTypesEnum.message,
                remoteOffer.Connection,
                sessionPort,
                messageMediaFormat);

            messageAnnouncement.Transport = "TCP/MSRP";

            remoteOffer.Media.Add(messageAnnouncement);

            var sdpOffer = remoteOffer.ToString();
            var msrpMediaAttribute =
                $"{SDPMediaAnnouncement.MEDIA_FORMAT_PATH_MSRP_PREFIX}//{remoteOffer.Connection.ConnectionAddress}:{sessionPort}/{sessionEndpoint}";
            var msrpMediaTypes = $"{SDPMediaAnnouncement.MEDIA_FORMAT_PATH_ACCEPT_TYPES_PREFIX}text/plain text/x-msrp-heartbeat";
            var mediaDescription = $"m=message {sessionPort} TCP/MSRP *";
            Assert.Contains(msrpMediaAttribute, sdpOffer);
            Assert.Contains(msrpMediaTypes, sdpOffer);
            Assert.Contains(mediaDescription, sdpOffer);
        }

        [Fact]
        public void AddExtra_Attribute_AddsMediaAttribute()
        {
            var videoAttribute = $"a=x-video-{System.Guid.NewGuid():N}";
            var audioAttribute = $"a=x-audio-{System.Guid.NewGuid():N}";
            var sdp = SDP.ParseSDPDescription(
                $"v=0{SDP.CRLF}" +
                $"o=- {(uint)System.Guid.NewGuid().GetHashCode()} 0 IN IP4 127.0.0.1{SDP.CRLF}" +
                $"s=sipsorcery{SDP.CRLF}" +
                $"t=0 0{SDP.CRLF}" +
                $"a=group:BUNDLE 0 1{SDP.CRLF}" +
                $"m=video 9 UDP/TLS/RTP/SAVP 96{SDP.CRLF}" +
                $"c=IN IP4 0.0.0.0{SDP.CRLF}" +
                $"a=ice-ufrag:LYMS{SDP.CRLF}" +
                $"a=ice-pwd:PAZQAZXCCWZZZIPRTUKOBHRH{SDP.CRLF}" +
                $"a=mid:0{SDP.CRLF}" +
                $"a=rtpmap:96 VP8/90000{SDP.CRLF}" +
                $"a=sendonly{SDP.CRLF}" +
                $"m=audio 9 UDP/TLS/RTP/SAVP 0{SDP.CRLF}" +
                $"c=IN IP4 0.0.0.0{SDP.CRLF}" +
                $"a=mid:1{SDP.CRLF}" +
                $"a=rtpmap:0 PCMU/8000{SDP.CRLF}" +
                "a=recvonly");
            var before = sdp.ToString();

            sdp.Media[0].AddExtra(videoAttribute);
            sdp.Media[1].AddExtra(audioAttribute);

            var expected = before
                .Replace(
                    $"{SDP.CRLF}a=sendonly",
                    $"{SDP.CRLF}{videoAttribute}{SDP.CRLF}a=sendonly")
                .Replace(
                    $"{SDP.CRLF}a=recvonly",
                    $"{SDP.CRLF}{audioAttribute}{SDP.CRLF}a=recvonly");
            Assert.Equal(expected, sdp.ToString());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void AddExtra_NullOrWhiteSpaceAttribute_DoesNotAddMediaAttribute(string attribute)
        {
            var sdp = SDP.ParseSDPDescription(
                $"v=0{SDP.CRLF}" +
                $"o=- {(uint)System.Guid.NewGuid().GetHashCode()} 0 IN IP4 127.0.0.1{SDP.CRLF}" +
                $"s=sipsorcery{SDP.CRLF}" +
                $"t=0 0{SDP.CRLF}" +
                $"m=audio 9 RTP/AVP 0{SDP.CRLF}" +
                "a=rtpmap:0 PCMU/8000");
            var before = sdp.ToString();

            sdp.Media[0].AddExtra(attribute);

            Assert.Equal(before, sdp.ToString());
        }
    }
}
