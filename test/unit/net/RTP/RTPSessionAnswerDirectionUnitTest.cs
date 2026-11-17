//-----------------------------------------------------------------------------
// Filename: RTPSessionAnswerDirectionUnitTest.cs
//
// Description: Tests that an RTPSession answering an offer gives each media
// line the direction RFC 3264 section 6.1 requires: recvonly (or inactive) to a
// sendonly offer, sendonly (or inactive) to a recvonly offer and inactive to an
// inactive offer, never more than the local track allows, and that a later
// offer (taking a call off hold) can widen it again.
//
// History:
// 16 Nov 2026	Claude Code - Opus 5.5	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net.UnitTests.Helpers;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTPSessionAnswerDirectionUnitTest
    {
        private readonly ILogger logger;

        public RTPSessionAnswerDirectionUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// An audio and video offer with the same direction on both media lines, the way the
        /// sipsorcery CLI's one-way sip: sink offers them (sendonly).
        /// </summary>
        private static string AudioVideoSdp(string direction, int version = 0) =>
$@"v=0
o=- 2000 {version} IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20000 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a={direction}
m=video 20002 RTP/AVP 96
a=rtpmap:96 VP8/90000
a={direction}";

        private static string AudioSdp(string direction, int version = 0) =>
$@"v=0
o=- 2001 {version} IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20000 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a={direction}";

        private static RTPSession AudioVideoSession(MediaStreamStatusEnum direction = MediaStreamStatusEnum.SendRecv) =>
            new RtpSessionBuilder()
                .WithAudioTrack(direction: direction)
                .WithVideoTrack(direction: direction)
                .Build();

        /// <summary>
        /// RFC 3264 section 6.1, for a session that would send and receive: each media line in the
        /// answer takes the direction the offer leaves it, and so does the local track, which is
        /// what decides whether the session sends.
        /// </summary>
        [Theory]
        [InlineData("sendonly", MediaStreamStatusEnum.RecvOnly)]
        [InlineData("recvonly", MediaStreamStatusEnum.SendOnly)]
        [InlineData("inactive", MediaStreamStatusEnum.Inactive)]
        [InlineData("sendrecv", MediaStreamStatusEnum.SendRecv)]
        public void InitialOffer_AnswerHonoursOfferedDirection(string offered, MediaStreamStatusEnum expected)
        {
            using (var session = AudioVideoSession())
            {
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, SDP.ParseSDPDescription(AudioVideoSdp(offered))));

                SDP answer = session.CreateAnswer(IPAddress.Loopback);
                logger.LogDebug("Answer SDP:\n{Sdp}", answer);

                SdpAssert.HasMediaLines(answer, "audio", "video");
                SdpAssert.HasDirection(SdpAssert.Audio(answer), expected);
                SdpAssert.HasDirection(SdpAssert.Video(answer), expected);
                Assert.Equal(expected, session.AudioStream.LocalTrack.StreamStatus);
                Assert.Equal(expected, session.VideoStream.LocalTrack.StreamStatus);
            }
        }

        /// <summary>
        /// A sendonly offer is answered recvonly, not declined: the media lines keep their ports so
        /// the offerer's media can arrive.
        /// </summary>
        [Fact]
        public void SendOnlyOffer_AnswerIsRecvOnlyAndMediaPortStaysOpen()
        {
            using (var session = AudioVideoSession())
            {
                session.SetRemoteDescription(SdpType.offer, SDP.ParseSDPDescription(AudioVideoSdp("sendonly")));

                SDP answer = session.CreateAnswer(IPAddress.Loopback);

                // Recvonly, not declined: the port stays open so their media can arrive.
                SdpAssert.IsAccepted(SdpAssert.Audio(answer));
                SdpAssert.IsAccepted(SdpAssert.Video(answer));
                Assert.Equal(MediaStreamStatusEnum.SendOnly, session.AudioStream.RemoteTrack.StreamStatus);
            }
        }

        /// <summary>
        /// A track the application made more restrictive stays at least that restrictive: a
        /// recvonly track answers a sendrecv offer recvonly, a sendonly offer recvonly, and a
        /// recvonly offer (neither side sending) inactive.
        /// </summary>
        [Theory]
        [InlineData("sendrecv", MediaStreamStatusEnum.RecvOnly)]
        [InlineData("sendonly", MediaStreamStatusEnum.RecvOnly)]
        [InlineData("recvonly", MediaStreamStatusEnum.Inactive)]
        [InlineData("inactive", MediaStreamStatusEnum.Inactive)]
        public void RecvOnlyTrack_AnswerIsNoMoreThanTheTrackAllows(string offered, MediaStreamStatusEnum expected)
        {
            using (var session = AudioVideoSession(MediaStreamStatusEnum.RecvOnly))
            {
                session.SetRemoteDescription(SdpType.offer, SDP.ParseSDPDescription(AudioVideoSdp(offered)));

                SDP answer = session.CreateAnswer(IPAddress.Loopback);

                SdpAssert.HasDirection(SdpAssert.Audio(answer), expected);
                SdpAssert.HasDirection(SdpAssert.Video(answer), expected);
            }
        }

        /// <summary>
        /// The same for a track the application restricted after creating it, with
        /// SetMediaStreamStatus.
        /// </summary>
        [Fact]
        public void TrackSetSendOnly_SendRecvOfferAnsweredSendOnly()
        {
            using (var session = AudioVideoSession())
            {
                session.SetMediaStreamStatus(SDPMediaTypesEnum.video, MediaStreamStatusEnum.SendOnly);

                session.SetRemoteDescription(SdpType.offer, SDP.ParseSDPDescription(AudioVideoSdp("sendrecv")));
                SDP answer = session.CreateAnswer(IPAddress.Loopback);

                SdpAssert.HasDirection(SdpAssert.Audio(answer), MediaStreamStatusEnum.SendRecv);
                SdpAssert.HasDirection(SdpAssert.Video(answer), MediaStreamStatusEnum.SendOnly);
            }
        }

        /// <summary>
        /// Hold and resume without a SIPUserAgent: the far end's re-INVITE puts the call on hold
        /// (sendonly, answered recvonly) and a second one takes it off (sendrecv), which is
        /// answered sendrecv again rather than staying at the recvonly the hold left the track at.
        /// </summary>
        [Fact]
        public void RemoteHoldThenResume_AnswerGoesBackToSendRecv()
        {
            using (var session = AudioVideoSession())
            {
                session.SetRemoteDescription(SdpType.offer, SDP.ParseSDPDescription(AudioVideoSdp("sendrecv", 0)));
                SdpAssert.HasDirection(SdpAssert.Audio(session.CreateAnswer(IPAddress.Loopback)), MediaStreamStatusEnum.SendRecv);

                session.SetRemoteDescription(SdpType.offer, SDP.ParseSDPDescription(AudioVideoSdp("sendonly", 1)));
                SDP holdAnswer = session.CreateAnswer(IPAddress.Loopback);
                SdpAssert.HasDirection(SdpAssert.Audio(holdAnswer), MediaStreamStatusEnum.RecvOnly);
                SdpAssert.HasDirection(SdpAssert.Video(holdAnswer), MediaStreamStatusEnum.RecvOnly);

                session.SetRemoteDescription(SdpType.offer, SDP.ParseSDPDescription(AudioVideoSdp("sendrecv", 2)));
                SDP resumeAnswer = session.CreateAnswer(IPAddress.Loopback);
                SdpAssert.HasDirection(SdpAssert.Audio(resumeAnswer), MediaStreamStatusEnum.SendRecv);
                SdpAssert.HasDirection(SdpAssert.Video(resumeAnswer), MediaStreamStatusEnum.SendRecv);
                Assert.Equal(MediaStreamStatusEnum.SendRecv, session.AudioStream.LocalTrack.StreamStatus);
            }
        }

        /// <summary>
        /// A re-INVITE that narrows the call to inactive, then one that resumes it, puts back the
        /// direction the application chose, not the track's default. (The track was created
        /// sendrecv and the application made it recvonly.)
        /// </summary>
        [Fact]
        public void InactiveThenResume_RestoresApplicationsDirectionNotDefault()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                session.SetMediaStreamStatus(SDPMediaTypesEnum.audio, MediaStreamStatusEnum.RecvOnly);

                session.SetRemoteDescription(SdpType.offer, SDP.ParseSDPDescription(AudioSdp("inactive", 0)));
                Assert.Equal(MediaStreamStatusEnum.Inactive, session.AudioStream.LocalTrack.StreamStatus);

                session.SetRemoteDescription(SdpType.offer, SDP.ParseSDPDescription(AudioSdp("sendrecv", 1)));
                Assert.Equal(MediaStreamStatusEnum.RecvOnly, session.AudioStream.LocalTrack.StreamStatus);
                SdpAssert.HasDirection(SdpAssert.Audio(session.CreateAnswer(IPAddress.Loopback)), MediaStreamStatusEnum.RecvOnly);
            }
        }

        /// <summary>
        /// The application changing a track's direction between offers is what the next offer is
        /// answered from, not what the track had before the last answer narrowed it.
        /// </summary>
        [Fact]
        public void ApplicationChangeAfterNarrowing_IsWhatTheNextOfferIsAnsweredFrom()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                session.SetRemoteDescription(SdpType.offer, SDP.ParseSDPDescription(AudioSdp("sendonly", 0)));
                Assert.Equal(MediaStreamStatusEnum.RecvOnly, session.AudioStream.LocalTrack.StreamStatus);

                // The application decides to only send from now on.
                session.SetMediaStreamStatus(SDPMediaTypesEnum.audio, MediaStreamStatusEnum.SendOnly);

                session.SetRemoteDescription(SdpType.offer, SDP.ParseSDPDescription(AudioSdp("sendrecv", 1)));
                Assert.Equal(MediaStreamStatusEnum.SendOnly, session.AudioStream.LocalTrack.StreamStatus);
            }
        }

        /// <summary>
        /// Our own offer's answer isn't narrowed by this: as the offerer we already said what we
        /// want, and a recvonly answer to it leaves the local track as we offered it.
        /// </summary>
        [Fact]
        public void RemoteAnswer_DoesNotChangeLocalDirection()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                session.CreateOffer(IPAddress.Loopback);

                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.answer, SDP.ParseSDPDescription(AudioSdp("recvonly"))));

                Assert.Equal(MediaStreamStatusEnum.SendRecv, session.AudioStream.LocalTrack.StreamStatus);
            }
        }

        /// <summary>
        /// Every combination of offered and wanted direction, against RFC 3264 section 6.1.
        /// </summary>
        [Theory]
        [InlineData(MediaStreamStatusEnum.SendRecv, MediaStreamStatusEnum.SendRecv, MediaStreamStatusEnum.SendRecv)]
        [InlineData(MediaStreamStatusEnum.SendRecv, MediaStreamStatusEnum.SendOnly, MediaStreamStatusEnum.SendOnly)]
        [InlineData(MediaStreamStatusEnum.SendRecv, MediaStreamStatusEnum.RecvOnly, MediaStreamStatusEnum.RecvOnly)]
        [InlineData(MediaStreamStatusEnum.SendRecv, MediaStreamStatusEnum.Inactive, MediaStreamStatusEnum.Inactive)]
        [InlineData(MediaStreamStatusEnum.SendOnly, MediaStreamStatusEnum.SendRecv, MediaStreamStatusEnum.RecvOnly)]
        [InlineData(MediaStreamStatusEnum.SendOnly, MediaStreamStatusEnum.SendOnly, MediaStreamStatusEnum.Inactive)]
        [InlineData(MediaStreamStatusEnum.SendOnly, MediaStreamStatusEnum.RecvOnly, MediaStreamStatusEnum.RecvOnly)]
        [InlineData(MediaStreamStatusEnum.SendOnly, MediaStreamStatusEnum.Inactive, MediaStreamStatusEnum.Inactive)]
        [InlineData(MediaStreamStatusEnum.RecvOnly, MediaStreamStatusEnum.SendRecv, MediaStreamStatusEnum.SendOnly)]
        [InlineData(MediaStreamStatusEnum.RecvOnly, MediaStreamStatusEnum.SendOnly, MediaStreamStatusEnum.SendOnly)]
        [InlineData(MediaStreamStatusEnum.RecvOnly, MediaStreamStatusEnum.RecvOnly, MediaStreamStatusEnum.Inactive)]
        [InlineData(MediaStreamStatusEnum.RecvOnly, MediaStreamStatusEnum.Inactive, MediaStreamStatusEnum.Inactive)]
        [InlineData(MediaStreamStatusEnum.Inactive, MediaStreamStatusEnum.SendRecv, MediaStreamStatusEnum.Inactive)]
        [InlineData(MediaStreamStatusEnum.Inactive, MediaStreamStatusEnum.SendOnly, MediaStreamStatusEnum.Inactive)]
        [InlineData(MediaStreamStatusEnum.Inactive, MediaStreamStatusEnum.RecvOnly, MediaStreamStatusEnum.Inactive)]
        [InlineData(MediaStreamStatusEnum.Inactive, MediaStreamStatusEnum.Inactive, MediaStreamStatusEnum.Inactive)]
        public void GetAnswerMediaStreamStatus_MatchesRfc3264(MediaStreamStatusEnum offered, MediaStreamStatusEnum local, MediaStreamStatusEnum expected)
        {
            Assert.Equal(expected, MediaStreamStatusType.GetAnswerMediaStreamStatus(offered, local));
        }
    }
}
