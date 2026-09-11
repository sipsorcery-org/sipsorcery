using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using SIPSorceryMedia.Abstractions;
using Xunit;

#pragma warning disable CS0618 // Type or member is obsolete

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

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" \t\r\n")]
        public void ParseMediaFormats_NullOrWhiteSpace_DoesNotAddFormats(string formatList)
        {
            var announcement = new SDPMediaAnnouncement();

            announcement.ParseMediaFormats(formatList);

            Assert.Empty(announcement.MediaFormats);
            Assert.Empty(announcement.ApplicationMediaFormats);
        }

        [Fact]
        public void ParseMediaFormats_ApplicationFormats_AddsEachFormat()
        {
            var firstFormatID = System.Guid.NewGuid().ToString("N");
            var secondFormatID = System.Guid.NewGuid().ToString("N");
            var announcement = new SDPMediaAnnouncement { Media = SDPMediaTypesEnum.application };

            announcement.ParseMediaFormats($"{firstFormatID}\t{secondFormatID}");

            Assert.Equal(2, announcement.ApplicationMediaFormats.Count);
            Assert.Equal(firstFormatID, announcement.ApplicationMediaFormats[firstFormatID].ID);
            Assert.Equal(secondFormatID, announcement.ApplicationMediaFormats[secondFormatID].ID);
            Assert.Empty(announcement.MediaFormats);
        }

        [Fact]
        public void ParseMediaFormats_MessageFormat_DoesNotAddFormats()
        {
            var announcement = new SDPMediaAnnouncement { Media = SDPMediaTypesEnum.message };

            announcement.ParseMediaFormats(System.Guid.NewGuid().ToString("N"));

            Assert.Empty(announcement.MediaFormats);
            Assert.Empty(announcement.ApplicationMediaFormats);
        }

        [Fact]
        public void ParseMediaFormats_AudioVideoFormats_AddsOnlyUniqueWellKnownFormats()
        {
            var wellKnownFormats = System.Enum.GetValues(typeof(SDPWellKnownMediaFormatsEnum))
                .Cast<SDPWellKnownMediaFormatsEnum>()
                .ToArray();
            var wellKnown = wellKnownFormats[(int)((uint)System.Guid.NewGuid().GetHashCode() % wellKnownFormats.Length)];
            var wellKnownID = (int)wellKnown;
            var unknownIDs = Enumerable.Range(0, SDPAudioVideoMediaFormat.DYNAMIC_ID_MIN)
                .Except(wellKnownFormats.Select(format => (int)format))
                .ToArray();
            var unknownID = unknownIDs[(int)((uint)System.Guid.NewGuid().GetHashCode() % unknownIDs.Length)];
            var dynamicID = SDPAudioVideoMediaFormat.DYNAMIC_ID_MIN
                + (int)((uint)System.Guid.NewGuid().GetHashCode()
                    % (SDPAudioVideoMediaFormat.DYNAMIC_ID_MAX - SDPAudioVideoMediaFormat.DYNAMIC_ID_MIN + 1));
            var invalidID = System.Guid.NewGuid().ToString("N");
            var announcement = new SDPMediaAnnouncement();

            announcement.ParseMediaFormats($"{wellKnownID} {wellKnownID}\t{unknownID}\r\n{dynamicID} {invalidID}");

            var mediaFormat = Assert.Single(announcement.MediaFormats);
            Assert.Equal(wellKnownID, mediaFormat.Key);
            Assert.Equal(wellKnownID, mediaFormat.Value.ID);
            Assert.Empty(announcement.ApplicationMediaFormats);
        }

        [Fact]
        public void ToString_DefaultAnnouncement_ReturnsMediaLine()
        {
            var announcement = new SDPMediaAnnouncement();

            var result = announcement.ToString();

            Assert.Equal($"m=audio 0 RTP/AVP {SDP.CRLF}", result);
        }

        [Fact]
        public void ToString_PopulatedAnnouncement_ReturnsAllAttributes()
        {
            var randomBytes = System.Guid.NewGuid().ToByteArray();
            var port = 1024 + (randomBytes[0] << 4) + randomBytes[1];
            var address = new IPAddress(new byte[] { 192, 0, 2, (byte)(1 + randomBytes[2] % 254) });
            var mediaDescription = System.Guid.NewGuid().ToString("N");
            var bandwidth = $"AS:{1 + (uint)System.Guid.NewGuid().GetHashCode() % 1000000}";
            var iceUfrag = System.Guid.NewGuid().ToString("N");
            var icePwd = System.Guid.NewGuid().ToString("N");
            var fingerprint = $"sha-256 {System.Guid.NewGuid():N}";
            var iceRoles = System.Enum.GetValues(typeof(IceRolesEnum)).Cast<IceRolesEnum>().ToArray();
            var iceRole = iceRoles[randomBytes[3] % iceRoles.Length];
            var candidate = System.Guid.NewGuid().ToString("N");
            var iceOptions = System.Guid.NewGuid().ToString("N");
            var mediaID = System.Guid.NewGuid().ToString("N");
            var extensionID = 1 + randomBytes[4] % 14;
            var headerExtension = new AbsSendTimeExtension(extensionID);
            var extraAttribute = $"a=x-{System.Guid.NewGuid():N}";
            var securityDescription = SDPSecurityDescription.CreateNew(
                1 + (uint)System.Guid.NewGuid().GetHashCode() % 999999998);
            var streamStatuses = System.Enum.GetValues(typeof(MediaStreamStatusEnum)).Cast<MediaStreamStatusEnum>().ToArray();
            var streamStatus = streamStatuses[randomBytes[5] % streamStatuses.Length];
            var firstSsrc = 1U + (uint)System.Guid.NewGuid().GetHashCode() % (uint.MaxValue - 1);
            var secondSsrc = firstSsrc == uint.MaxValue ? firstSsrc - 1 : firstSsrc + 1;
            var ssrcGroupID = System.Guid.NewGuid().ToString("N");
            var cname = System.Guid.NewGuid().ToString("N");
            var sctpPort = (ushort)(1 + (uint)System.Guid.NewGuid().GetHashCode() % ushort.MaxValue);
            var maxMessageSize = 1L + (uint)System.Guid.NewGuid().GetHashCode();
            var announcement = new SDPMediaAnnouncement
            {
                Media = SDPMediaTypesEnum.video,
                Port = port,
                Transport = System.Guid.NewGuid().ToString("N"),
                MediaDescription = mediaDescription,
                Connection = new SDPConnectionInformation(address),
                TIASBandwidth = 1U + (uint)System.Guid.NewGuid().GetHashCode() % (uint.MaxValue - 1),
                IceUfrag = iceUfrag,
                IcePwd = icePwd,
                DtlsFingerprint = fingerprint,
                IceRole = iceRole,
                IceCandidates = new List<string> { candidate },
                IceOptions = iceOptions,
                IceEndOfCandidates = true,
                MediaID = mediaID,
                MediaStreamStatus = streamStatus,
                SsrcGroupID = ssrcGroupID,
                SctpPort = sctpPort,
                MaxMessageSize = maxMessageSize
            };
            announcement.BandwidthAttributes.Add(bandwidth);
            announcement.HeaderExtensions.Add(extensionID, headerExtension);
            announcement.ExtraMediaAttributes.Add(" \t");
            announcement.ExtraMediaAttributes.Add(extraAttribute);
            announcement.SecurityDescriptions.Add(securityDescription);
            announcement.SsrcAttributes.Add(new SDPSsrcAttribute(firstSsrc, cname, ssrcGroupID));
            announcement.SsrcAttributes.Add(new SDPSsrcAttribute(secondSsrc, null, ssrcGroupID));

            var result = announcement.ToString();

            var expected =
                $"m={announcement.Media} {port} {announcement.Transport} {SDP.CRLF}" +
                $"i={mediaDescription}{SDP.CRLF}" +
                announcement.Connection.ToString() +
                $"{SDPMediaAnnouncement.TIAS_BANDWIDTH_ATTRIBUE_PREFIX}{announcement.TIASBandwidth}{SDP.CRLF}" +
                $"b={bandwidth}{SDP.CRLF}" +
                $"a={SDP.ICE_UFRAG_ATTRIBUTE_PREFIX}:{iceUfrag}{SDP.CRLF}" +
                $"a={SDP.ICE_PWD_ATTRIBUTE_PREFIX}:{icePwd}{SDP.CRLF}" +
                $"a={SDP.DTLS_FINGERPRINT_ATTRIBUTE_PREFIX}:{fingerprint}{SDP.CRLF}" +
                $"a={SDP.ICE_SETUP_ATTRIBUTE_PREFIX}:{iceRole}{SDP.CRLF}" +
                $"a={SDP.ICE_CANDIDATE_ATTRIBUTE_PREFIX}:{candidate}{SDP.CRLF}" +
                $"a={SDP.ICE_OPTIONS}:{iceOptions}{SDP.CRLF}" +
                $"a={SDP.END_ICE_CANDIDATES_ATTRIBUTE}{SDP.CRLF}" +
                $"a={SDP.MEDIA_ID_ATTRIBUTE_PREFIX}:{mediaID}{SDP.CRLF}" +
                $"{SDPMediaAnnouncement.MEDIA_EXTENSION_MAP_ATTRIBUE_PREFIX}{extensionID} {headerExtension.Uri}{SDP.CRLF}" +
                $"{extraAttribute}{SDP.CRLF}" +
                $"{securityDescription}{SDP.CRLF}" +
                $"{MediaStreamStatusType.GetAttributeForMediaStreamStatus(streamStatus)}{SDP.CRLF}" +
                $"{SDPMediaAnnouncement.MEDIA_FORMAT_SSRC_GROUP_ATTRIBUE_PREFIX}{ssrcGroupID} {firstSsrc} {secondSsrc}{SDP.CRLF}" +
                $"{SDPMediaAnnouncement.MEDIA_FORMAT_SSRC_ATTRIBUE_PREFIX}{firstSsrc} {SDPSsrcAttribute.MEDIA_CNAME_ATTRIBUE_PREFIX}:{cname}{SDP.CRLF}" +
                $"{SDPMediaAnnouncement.MEDIA_FORMAT_SSRC_ATTRIBUE_PREFIX}{secondSsrc}{SDP.CRLF}" +
                $"{SDPMediaAnnouncement.MEDIA_FORMAT_SCTP_PORT_ATTRIBUE_PREFIX}{sctpPort}{SDP.CRLF}" +
                $"{SDPMediaAnnouncement.MEDIA_FORMAT_MAX_MESSAGE_SIZE_ATTRIBUE_PREFIX}{maxMessageSize}{SDP.CRLF}";
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ToString_EmptyCollectionsAndSctpMap_ReturnsOnlySctpMapAttribute()
        {
            var sctpMap = System.Guid.NewGuid().ToString("N");
            var announcement = new SDPMediaAnnouncement
            {
                MediaDescription = " \t",
                IceUfrag = " \t",
                IcePwd = " \t",
                DtlsFingerprint = " \t",
                IceCandidates = new List<string>(),
                MediaID = " \t",
                SsrcGroupID = System.Guid.NewGuid().ToString("N"),
                SctpMap = sctpMap,
                SctpPort = (ushort)(1 + (uint)System.Guid.NewGuid().GetHashCode() % ushort.MaxValue),
                MaxMessageSize = 1L + (uint)System.Guid.NewGuid().GetHashCode()
            };

            var result = announcement.ToString();

            Assert.Equal(
                $"m=audio 0 RTP/AVP {SDP.CRLF}" +
                $"{SDPMediaAnnouncement.MEDIA_FORMAT_SCTP_MAP_ATTRIBUE_PREFIX}{sctpMap}{SDP.CRLF}",
                result);
        }
    }
}
