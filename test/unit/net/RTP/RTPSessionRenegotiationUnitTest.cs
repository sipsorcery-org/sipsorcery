//-----------------------------------------------------------------------------
// Filename: RTPSessionRenegotiationUnitTest.cs
//
// Description: Unit tests for SDP renegotiation scenarios, specifically
// verifying that rejected media streams (port 0) correctly stop RTCP
// monitoring. Regression test for issue #1496.
//
// History:
// 16 Feb 2026	Contributors	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTPSessionRenegotiationUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTPSessionRenegotiationUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Reproduces issue #1496: a re-INVITE that rejects video with m=video 0
        /// must close the video RTCP session so its inactivity timer does not fire
        /// a timeout that tears down the entire (audio-only) call.
        ///
        /// Per RFC 3264 Section 8.2: "In the case of RTP, RTCP transmission also
        /// ceases, as does processing of any received RTCP packets."
        /// </summary>
        [Fact]
        public void VideoRejectedByReInviteClosesRtcpSession()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // --- Local session with audio + video ---
            RTPSession rtpSession = new RTPSession(false, false, false);

            MediaStreamTrack localAudioTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat> {
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU)
                });
            rtpSession.addTrack(localAudioTrack);

            MediaStreamTrack localVideoTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.video, false,
                new List<SDPAudioVideoMediaFormat> {
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000)
                });
            rtpSession.addTrack(localVideoTrack);

            // --- Initial offer with both audio and video active ---
            string initialOfferSdp =
@"v=0
o=- 1000 0 IN IP4 127.0.0.1
s=-
c=IN IP4 127.0.0.1
t=0 0
m=audio 20000 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendrecv
m=video 20002 RTP/AVP 96
a=rtpmap:96 VP8/90000
a=sendrecv";

            var initialOffer = SDP.ParseSDPDescription(initialOfferSdp);
            var result = rtpSession.SetRemoteDescription(SdpType.offer, initialOffer);
            Assert.Equal(SetDescriptionResultEnum.OK, result);

            // Start the RTCP sessions (as would happen on a real call).
            rtpSession.Start();

            // Verify both RTCP sessions are running.
            Assert.NotNull(rtpSession.AudioStream.RtcpSession);
            Assert.False(rtpSession.AudioStream.RtcpSession.IsClosed);
            Assert.NotNull(rtpSession.VideoStream.RtcpSession);
            Assert.False(rtpSession.VideoStream.RtcpSession.IsClosed);

            // --- Re-INVITE: remote party rejects video with port 0 ---
            string reInviteOfferSdp =
@"v=0
o=- 1000 1 IN IP4 127.0.0.1
s=-
c=IN IP4 127.0.0.1
t=0 0
m=audio 20000 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendrecv
m=video 0 RTP/AVP 96
a=rtpmap:96 VP8/90000";

            var reInviteOffer = SDP.ParseSDPDescription(reInviteOfferSdp);
            result = rtpSession.SetRemoteDescription(SdpType.offer, reInviteOffer);
            Assert.Equal(SetDescriptionResultEnum.OK, result);

            // --- Verify: video RTCP session is closed, audio is still active ---
            Assert.True(rtpSession.VideoStream.RtcpSession.IsClosed,
                "Video RTCP session should be closed after remote party rejected video with port 0.");
            Assert.Equal(MediaStreamStatusEnum.Inactive, rtpSession.VideoStream.LocalTrack.StreamStatus);

            Assert.False(rtpSession.AudioStream.RtcpSession.IsClosed,
                "Audio RTCP session should remain active.");

            rtpSession.Close("normal");

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Verifies that when both audio and video remain active in a re-INVITE,
        /// neither RTCP session is closed.
        /// </summary>
        [Fact]
        public void BothStreamsActiveAfterReInviteKeepsRtcpRunning()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPSession rtpSession = new RTPSession(false, false, false);

            MediaStreamTrack localAudioTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat> {
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU)
                });
            rtpSession.addTrack(localAudioTrack);

            MediaStreamTrack localVideoTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.video, false,
                new List<SDPAudioVideoMediaFormat> {
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000)
                });
            rtpSession.addTrack(localVideoTrack);

            string offerSdp =
@"v=0
o=- 2000 0 IN IP4 127.0.0.1
s=-
c=IN IP4 127.0.0.1
t=0 0
m=audio 30000 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendrecv
m=video 30002 RTP/AVP 96
a=rtpmap:96 VP8/90000
a=sendrecv";

            var offer = SDP.ParseSDPDescription(offerSdp);
            var result = rtpSession.SetRemoteDescription(SdpType.offer, offer);
            Assert.Equal(SetDescriptionResultEnum.OK, result);
            rtpSession.Start();

            // Re-INVITE with both streams still active (different ports).
            string reInviteSdp =
@"v=0
o=- 2000 1 IN IP4 127.0.0.1
s=-
c=IN IP4 127.0.0.1
t=0 0
m=audio 30010 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendrecv
m=video 30012 RTP/AVP 96
a=rtpmap:96 VP8/90000
a=sendrecv";

            var reInvite = SDP.ParseSDPDescription(reInviteSdp);
            result = rtpSession.SetRemoteDescription(SdpType.offer, reInvite);
            Assert.Equal(SetDescriptionResultEnum.OK, result);

            Assert.False(rtpSession.AudioStream.RtcpSession.IsClosed);
            Assert.False(rtpSession.VideoStream.RtcpSession.IsClosed);

            rtpSession.Close("normal");

            logger.LogDebug("-----------------------------------------");
        }
    }
}
