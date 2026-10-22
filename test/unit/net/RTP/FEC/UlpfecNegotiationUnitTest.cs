//-----------------------------------------------------------------------------
// Filename: UlpfecNegotiationUnitTest.cs
//
// Description: Unit tests verifying SDP offer/answer negotiation, auxiliary format
// matching, payload type remapping, and graceful fallback when peer lacks FEC.
//
// Author(s):
// SIPSorcery contributors
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP.App;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class UlpfecNegotiationUnitTest
    {
        private readonly ILogger logger;

        public UlpfecNegotiationUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = TestLogHelper.InitTestLogger(output);
        }

        private static SDP CreateRemoteOffer(int h264Pt, int? redPt, int? ulpfecPt)
        {
            var sdp = new SDP(IPAddress.Loopback);
            sdp.Connection = new SDPConnectionInformation(IPAddress.Loopback);
            var videoAnn = new SDPMediaAnnouncement
            {
                Media = SDPMediaTypesEnum.video,
                Port = 9000,
                Transport = "UDP/TLS/RTP/SAVPF"
            };

            videoAnn.MediaFormats.Add(h264Pt, new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, h264Pt, "H264", 90000, 0, "packetization-mode=1"));

            if (redPt.HasValue)
            {
                videoAnn.MediaFormats.Add(redPt.Value, SDPAudioVideoMediaFormat.CreateRedFormat(redPt.Value));
            }
            if (ulpfecPt.HasValue)
            {
                videoAnn.MediaFormats.Add(ulpfecPt.Value, SDPAudioVideoMediaFormat.CreateUlpfecFormat(ulpfecPt.Value));
            }

            sdp.Media.Add(videoAnn);
            return sdp;
        }

        /// <summary>
        /// Tests that when the remote browser offers H264, RED, and ULPFEC, and the local
        /// track includes FEC formats, RTPSession successfully negotiates all three and activates FEC.
        /// </summary>
        [Fact]
        public void NegotiatesUlpfecWhenOfferedByPeerTest()
        {
            var session = new RTPSession(new RtpSessionConfig());

            var videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "H264", 90000, 0, "packetization-mode=1")
            });
            videoTrack.Redundancy = RtpRedundancyOptions.Red | RtpRedundancyOptions.Ulpfec;
            videoTrack.FecConfig = new RtpUlpfecConfig { TargetOverheadFraction = 0.20f };
            session.addTrack(videoTrack);

            var offer = CreateRemoteOffer(h264Pt: 96, redPt: 122, ulpfecPt: 127);
            var result = session.SetRemoteDescription(SdpType.offer, offer);

            Assert.Equal(SetDescriptionResultEnum.OK, result);
            Assert.NotNull(session.VideoStream);
            Assert.NotNull(session.VideoStream.FecEncoder);
            Assert.True(session.VideoStream.FecEncoder.IsActive);
            Assert.Equal(122, session.VideoStream.FecEncoder.RedPayloadType);
            Assert.Equal(127, session.VideoStream.FecEncoder.UlpfecPayloadType);
            Assert.Equal(96, session.VideoStream.FecEncoder.MediaPayloadType);

            // Generate answer and verify RED and ULPFEC are present
            var answer = session.CreateAnswer(null);
            var videoMedia = answer.Media.First(m => m.Media == SDPMediaTypesEnum.video);
            Assert.Contains(videoMedia.MediaFormats.Values, f => f.IsRed);
            Assert.Contains(videoMedia.MediaFormats.Values, f => f.IsUlpfec);
        }

        /// <summary>
        /// Tests that when the peer uses alternate dynamic payload types (e.g. Chrome's 114 for RED,
        /// 116 for ULPFEC, 102 for H264), the answer adopts the peer's payload types and configures
        /// the encoder with the peer's assigned IDs.
        /// </summary>
        [Fact]
        public void AdoptsPeersDynamicPayloadTypeIDsTest()
        {
            var session = new RTPSession(new RtpSessionConfig());

            var videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "H264", 90000, 0, "packetization-mode=1")
            });
            videoTrack.Redundancy = RtpRedundancyOptions.Red | RtpRedundancyOptions.Ulpfec;
            session.addTrack(videoTrack);

            // Peer uses PT 102 for H264, 114 for RED, 116 for ULPFEC
            var offer = CreateRemoteOffer(h264Pt: 102, redPt: 114, ulpfecPt: 116);
            var result = session.SetRemoteDescription(SdpType.offer, offer);

            Assert.Equal(SetDescriptionResultEnum.OK, result);
            Assert.NotNull(session.VideoStream.FecEncoder);
            Assert.Equal(114, session.VideoStream.FecEncoder.RedPayloadType);
            Assert.Equal(116, session.VideoStream.FecEncoder.UlpfecPayloadType);
            Assert.Equal(102, session.VideoStream.FecEncoder.MediaPayloadType);
        }

        /// <summary>
        /// Tests that when the peer does NOT offer RED/ULPFEC (plain H.264 only),
        /// SIPSorcery gracefully declines FEC, generates an answer without FEC formats,
        /// and leaves FecEncoder null (pure plain H.264 fallback).
        /// </summary>
        [Fact]
        public void GracefullyFallsBackWhenPeerLacksFecSupportTest()
        {
            var session = new RTPSession(new RtpSessionConfig());

            var videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "H264", 90000, 0, "packetization-mode=1")
            });
            videoTrack.Redundancy = RtpRedundancyOptions.Red | RtpRedundancyOptions.Ulpfec;
            session.addTrack(videoTrack);

            // Peer offers ONLY H264 (no red, no ulpfec)
            var offer = CreateRemoteOffer(h264Pt: 96, redPt: null, ulpfecPt: null);
            var result = session.SetRemoteDescription(SdpType.offer, offer);

            Assert.Equal(SetDescriptionResultEnum.OK, result);
            Assert.NotNull(session.VideoStream);
            Assert.Null(session.VideoStream.FecEncoder); // Cleanly disabled!

            var answer = session.CreateAnswer(null);
            var videoMedia = answer.Media.First(m => m.Media == SDPMediaTypesEnum.video);
            Assert.DoesNotContain(videoMedia.MediaFormats.Values, f => f.IsRed);
            Assert.DoesNotContain(videoMedia.MediaFormats.Values, f => f.IsUlpfec);
        }

        [Fact]
        public void NegotiatesVp8WithRedAndUlpfecTest()
        {
            var session = new RTPSession(new RtpSessionConfig());

            var videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000)
            });
            videoTrack.Redundancy = RtpRedundancyOptions.Red | RtpRedundancyOptions.Ulpfec;
            session.addTrack(videoTrack);

            var offer = new SDP(System.Net.IPAddress.Loopback);
            offer.SessionId = "12345";
            offer.Connection = new SDPConnectionInformation(System.Net.IPAddress.Loopback);
            var announcement = new SDPMediaAnnouncement
            {
                Media = SDPMediaTypesEnum.video,
                Port = 9000,
                Transport = "UDP/TLS/RTP/SAVPF"
            };
            announcement.MediaFormats.Add(96, new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000));
            announcement.MediaFormats.Add(122, SDPAudioVideoMediaFormat.CreateRedFormat(122));
            announcement.MediaFormats.Add(127, SDPAudioVideoMediaFormat.CreateUlpfecFormat(127));
            offer.Media.Add(announcement);

            var result = session.SetRemoteDescription(SdpType.offer, offer);

            Assert.Equal(SetDescriptionResultEnum.OK, result);
            Assert.NotNull(session.VideoStream);
            Assert.NotNull(session.VideoStream.FecEncoder);
            Assert.NotNull(session.VideoStream.FecDecoder);
            Assert.Equal(122, session.VideoStream.FecEncoder.RedPayloadType);
            Assert.Equal(127, session.VideoStream.FecEncoder.UlpfecPayloadType);
            Assert.Equal(96, session.VideoStream.FecEncoder.MediaPayloadType);
        }
    }
}
