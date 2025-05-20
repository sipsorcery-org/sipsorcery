using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using SIPSorceryMedia.Abstractions;
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

        #region ParseMediaFormats Tests

        /// <summary>
        /// Tests ParseMediaFormats with null input.
        /// </summary>
        [Fact]
        public void ParseMediaFormats_NullInput_Test()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var announcement = new SDPMediaAnnouncement(SDPMediaTypesEnum.audio, 5000, (List<SDPAudioVideoMediaFormat>)null);

            announcement.ParseMediaFormats(null);

            Assert.Empty(announcement.MediaFormats);
        }

        /// <summary>
        /// Tests ParseMediaFormats with empty string.
        /// </summary>
        [Fact]
        public void ParseMediaFormats_EmptyString_Test()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var announcement = new SDPMediaAnnouncement(SDPMediaTypesEnum.audio, 5000, (List<SDPAudioVideoMediaFormat>)null);

            announcement.ParseMediaFormats(string.Empty);

            Assert.Empty(announcement.MediaFormats);
        }

        /// <summary>
        /// Tests ParseMediaFormats with whitespace only.
        /// </summary>
        [Fact]
        public void ParseMediaFormats_WhitespaceOnly_Test()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var announcement = new SDPMediaAnnouncement(SDPMediaTypesEnum.audio, 5000, (List<SDPAudioVideoMediaFormat>)null);

            announcement.ParseMediaFormats("   \t  ");

            Assert.Empty(announcement.MediaFormats);
        }

        /// <summary>
        /// Tests ParseMediaFormats with a single well-known audio format (PCMU).
        /// </summary>
        [Fact]
        public void ParseMediaFormats_SingleWellKnownAudioFormat_Test()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var announcement = new SDPMediaAnnouncement(SDPMediaTypesEnum.audio, 5000, (List<SDPAudioVideoMediaFormat>)null);

            announcement.ParseMediaFormats("0");

            Assert.Single(announcement.MediaFormats);
            Assert.True(announcement.MediaFormats.ContainsKey(0));
            Assert.Equal(0, announcement.MediaFormats[0].ID);
            Assert.Equal("PCMU", announcement.MediaFormats[0].Name());
        }

        /// <summary>
        /// Tests ParseMediaFormats with multiple well-known audio formats.
        /// </summary>
        [Fact]
        public void ParseMediaFormats_MultipleWellKnownAudioFormats_Test()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var announcement = new SDPMediaAnnouncement(SDPMediaTypesEnum.audio, 5000, (List<SDPAudioVideoMediaFormat>)null);

            announcement.ParseMediaFormats("0 8 18");

            Assert.Equal(3, announcement.MediaFormats.Count);
            Assert.True(announcement.MediaFormats.ContainsKey(0));
            Assert.True(announcement.MediaFormats.ContainsKey(8));
            Assert.True(announcement.MediaFormats.ContainsKey(18));
            Assert.Equal("PCMU", announcement.MediaFormats[0].Name());
            Assert.Equal("PCMA", announcement.MediaFormats[8].Name());
        }

        /// <summary>
        /// Tests ParseMediaFormats with leading and trailing whitespace.
        /// </summary>
        [Fact]
        public void ParseMediaFormats_LeadingTrailingWhitespace_Test()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var announcement = new SDPMediaAnnouncement(SDPMediaTypesEnum.audio, 5000, (List<SDPAudioVideoMediaFormat>)null);

            announcement.ParseMediaFormats("  0 8 18  ");

            Assert.Equal(3, announcement.MediaFormats.Count);
            Assert.True(announcement.MediaFormats.ContainsKey(0));
            Assert.True(announcement.MediaFormats.ContainsKey(8));
            Assert.True(announcement.MediaFormats.ContainsKey(18));
        }

        /// <summary>
        /// Tests ParseMediaFormats with multiple spaces between formats.
        /// </summary>
        [Fact]
        public void ParseMediaFormats_MultipleSpacesBetweenFormats_Test()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var announcement = new SDPMediaAnnouncement(SDPMediaTypesEnum.audio, 5000, (List<SDPAudioVideoMediaFormat>)null);

            announcement.ParseMediaFormats("0    8    18");

            Assert.Equal(3, announcement.MediaFormats.Count);
            Assert.True(announcement.MediaFormats.ContainsKey(0));
            Assert.True(announcement.MediaFormats.ContainsKey(8));
            Assert.True(announcement.MediaFormats.ContainsKey(18));
        }

        /// <summary>
        /// Tests ParseMediaFormats with tabs and spaces mixed.
        /// </summary>
        [Fact]
        public void ParseMediaFormats_MixedWhitespace_Test()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var announcement = new SDPMediaAnnouncement(SDPMediaTypesEnum.audio, 5000, (List<SDPAudioVideoMediaFormat>)null);

            announcement.ParseMediaFormats("0\t8 \t 18");

            Assert.Equal(3, announcement.MediaFormats.Count);
            Assert.True(announcement.MediaFormats.ContainsKey(0));
            Assert.True(announcement.MediaFormats.ContainsKey(8));
            Assert.True(announcement.MediaFormats.ContainsKey(18));
        }

        /// <summary>
        /// Tests ParseMediaFormats with video media type.
        /// </summary>
        [Fact]
        public void ParseMediaFormats_VideoMediaType_Test()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var announcement = new SDPMediaAnnouncement(SDPMediaTypesEnum.video, 5000, (List<SDPAudioVideoMediaFormat>)null);

            announcement.ParseMediaFormats("96 97");

            // Dynamic format IDs (>= 96) are not added for video as they are out of the well-known range
            Assert.Empty(announcement.MediaFormats);
        }

        /// <summary>
        /// Tests ParseMediaFormats with application media type.
        /// </summary>
        [Fact]
        public void ParseMediaFormats_ApplicationMediaType_Test()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var announcement = new SDPMediaAnnouncement(SDPMediaTypesEnum.application, 5000, (List<SDPApplicationMediaFormat>)null);

            announcement.ParseMediaFormats("webrtc-datachannel sctp");

            Assert.Equal(2, announcement.ApplicationMediaFormats.Count);
            Assert.True(announcement.ApplicationMediaFormats.ContainsKey("webrtc-datachannel"));
            Assert.True(announcement.ApplicationMediaFormats.ContainsKey("sctp"));
            Assert.Equal("webrtc-datachannel", announcement.ApplicationMediaFormats["webrtc-datachannel"].ID);
            Assert.Equal("sctp", announcement.ApplicationMediaFormats["sctp"].ID);
        }

        /// <summary>
        /// Tests ParseMediaFormats with numeric format IDs for application media.
        /// </summary>
        [Fact]
        public void ParseMediaFormats_ApplicationMediaTypeWithNumericIDs_Test()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var announcement = new SDPMediaAnnouncement(SDPMediaTypesEnum.application, 5000, (List<SDPApplicationMediaFormat>)null);

            announcement.ParseMediaFormats("96 97 98");

            Assert.Equal(3, announcement.ApplicationMediaFormats.Count);
            Assert.True(announcement.ApplicationMediaFormats.ContainsKey("96"));
            Assert.True(announcement.ApplicationMediaFormats.ContainsKey("97"));
            Assert.True(announcement.ApplicationMediaFormats.ContainsKey("98"));
        }

        /// <summary>
        /// Tests ParseMediaFormats with message media type (should not add any formats).
        /// </summary>
        [Fact]
        public void ParseMediaFormats_MessageMediaType_Test()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var connection = new SDPConnectionInformation(IPAddress.Loopback);
            var announcement = new SDPMediaAnnouncement(SDPMediaTypesEnum.message, connection, 5000, new SDPMessageMediaFormat());

            announcement.ParseMediaFormats("*");

            // Message media formats are handled differently and not added via ParseMediaFormats
            Assert.Empty(announcement.MediaFormats);
        }

        /// <summary>
        /// Tests ParseMediaFormats that duplicate format IDs are not added twice.
        /// </summary>
        [Fact]
        public void ParseMediaFormats_DuplicateFormatIDs_Test()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var announcement = new SDPMediaAnnouncement(SDPMediaTypesEnum.audio, 5000, (List<SDPAudioVideoMediaFormat>)null);

            announcement.ParseMediaFormats("0 8 0 18 8");

            // Duplicates should not be added; only unique IDs should be present
            Assert.Equal(3, announcement.MediaFormats.Count);
            Assert.True(announcement.MediaFormats.ContainsKey(0));
            Assert.True(announcement.MediaFormats.ContainsKey(8));
            Assert.True(announcement.MediaFormats.ContainsKey(18));
        }

        /// <summary>
        /// Tests ParseMediaFormats with format IDs at the boundary of well-known (95) and dynamic (96).
        /// </summary>
        [Fact]
        public void ParseMediaFormats_BoundaryBetweenWellKnownAndDynamic_Test()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var announcement = new SDPMediaAnnouncement(SDPMediaTypesEnum.audio, 5000, (List<SDPAudioVideoMediaFormat>)null);

            // Format 95 is the highest well-known format ID
            // Format 96 and above are dynamic and won't be added without an rtpmap attribute
            announcement.ParseMediaFormats("95 96 97");

            // Only well-known formats below DYNAMIC_ID_MIN (96) should be added
            // Format IDs >= DYNAMIC_ID_MIN are not added in ParseMediaFormats
            Assert.Empty(announcement.MediaFormats.Where(f => f.Key >= SDPAudioVideoMediaFormat.DYNAMIC_ID_MIN).ToList());
        }

        /// <summary>
        /// Tests ParseMediaFormats preserves existing formats when called multiple times.
        /// </summary>
        [Fact]
        public void ParseMediaFormats_MultipleCallsAdditive_Test()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var announcement = new SDPMediaAnnouncement(SDPMediaTypesEnum.audio, 5000, (List<SDPAudioVideoMediaFormat>)null);

            announcement.ParseMediaFormats("0 8");
            var countAfterFirst = announcement.MediaFormats.Count;

            announcement.ParseMediaFormats("18");

            // Second call should add new format without removing previous ones
            Assert.True(announcement.MediaFormats.Count >= countAfterFirst);
            Assert.True(announcement.MediaFormats.ContainsKey(0));
            Assert.True(announcement.MediaFormats.ContainsKey(8));
            Assert.True(announcement.MediaFormats.ContainsKey(18));
        }

        /// <summary>
        /// Tests ParseMediaFormats with format list from a real SDP example.
        /// </summary>
        [Fact]
        public void ParseMediaFormats_RealSdpExample_Test()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var announcement = new SDPMediaAnnouncement(SDPMediaTypesEnum.audio, 5000, (List<SDPAudioVideoMediaFormat>)null);

            // Typical audio SDP format list - using well-known formats only (0=PCMU, 18=G729)
            announcement.ParseMediaFormats("0 18");

            Assert.Equal(2, announcement.MediaFormats.Count);
            Assert.True(announcement.MediaFormats.ContainsKey(0));
            Assert.True(announcement.MediaFormats.ContainsKey(18));
            Assert.Equal("PCMU", announcement.MediaFormats[0].Name());
        }

        /// <summary>
        /// Tests ParseMediaFormats with a single format.
        /// </summary>
        [Fact]
        public void ParseMediaFormats_SingleFormat_Test()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var announcement = new SDPMediaAnnouncement(SDPMediaTypesEnum.audio, 5000, (List<SDPAudioVideoMediaFormat>)null);

            announcement.ParseMediaFormats("8");

            Assert.Single(announcement.MediaFormats);
            Assert.True(announcement.MediaFormats.ContainsKey(8));
            Assert.Equal("PCMA", announcement.MediaFormats[8].Name());
        }

        #endregion
    }
}
