//-----------------------------------------------------------------------------
// Filename: SDPRtcpFeedbackUnitTest.cs
//
// Description: Unit tests for the RTCP feedback ("a=rtcp-fb") attributes SIPSorcery
// offers, parses and negotiates for video formats.
//
// Author(s):
// Aaron Clauson
//
// History:
// 05 Nov 2026	Aaron Clauson	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Net;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class SDPRtcpFeedbackUnitTest
    {
        private const string CRLF = "\r\n";

        private const string SessionHeader =
            "v=0\r\n" +
            "o=- 1 1 IN IP4 127.0.0.1\r\n" +
            "s=session\r\n" +
            "c=IN IP4 127.0.0.1\r\n" +
            "t=0 0\r\n";

        private static RTPSession CreateVideoSession()
        {
            var session = new RTPSession(false, false, false);
            session.addTrack(new MediaStreamTrack(new List<VideoFormat>
            {
                new VideoFormat(VideoCodecsEnum.VP8, 96),
                new VideoFormat(VideoCodecsEnum.H264, 100, parameters: "packetization-mode=1")
            }, MediaStreamStatusEnum.SendRecv));
            return session;
        }

        private static List<string> FeedbackLines(SDP sdp, int formatID) =>
            sdp.ToString().Split(new[] { CRLF }, System.StringSplitOptions.RemoveEmptyEntries)
                .Where(x => x.StartsWith($"a=rtcp-fb:{formatID} "))
                .Select(x => x.Substring($"a=rtcp-fb:{formatID} ".Length))
                .ToList();

        /// <summary>
        /// A locally generated video offer advertises generic NACK, key frame requests (PLI and FIR)
        /// plus transport-cc.
        /// </summary>
        [Fact]
        public void VideoOfferIncludesKeyFrameFeedback()
        {
            using var session = CreateVideoSession();

            var offer = session.CreateOffer(IPAddress.Loopback);

            foreach (var formatID in new[] { 96, 100 })
            {
                Assert.Equal(new[] { "nack", "nack pli", "ccm fir", "transport-cc" }, FeedbackLines(offer, formatID));
            }
        }

        /// <summary>
        /// Audio formats never get RTCP feedback attributes.
        /// </summary>
        [Fact]
        public void AudioOfferHasNoFeedback()
        {
            using var session = new RTPSession(false, false, false);
            session.addTrack(new MediaStreamTrack(SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) }));

            var offer = session.CreateOffer(IPAddress.Loopback);

            Assert.DoesNotContain("a=rtcp-fb", offer.ToString());
        }

        /// <summary>
        /// The answer only keeps the feedback types listed by both parties.
        /// </summary>
        [Fact]
        public void AnswerKeepsIntersectionOfFeedback()
        {
            var remoteOffer = SessionHeader +
                $"m=video 5000 RTP/AVPF 96 100{CRLF}" +
                $"a=rtpmap:96 VP8/90000{CRLF}" +
                $"a=rtcp-fb:96 goog-remb{CRLF}" +
                $"a=rtcp-fb:96 transport-cc{CRLF}" +
                $"a=rtcp-fb:96 ccm fir{CRLF}" +
                $"a=rtcp-fb:96 nack{CRLF}" +
                $"a=rtcp-fb:96 nack pli{CRLF}" +
                $"a=rtpmap:100 H264/90000{CRLF}" +
                $"a=fmtp:100 packetization-mode=1{CRLF}" +
                $"a=rtcp-fb:100 nack pli{CRLF}" +
                $"a=sendrecv{CRLF}";

            using var session = CreateVideoSession();
            var result = session.SetRemoteDescription(SdpType.offer, SDP.ParseSDPDescription(remoteOffer));
            Assert.Equal(SetDescriptionResultEnum.OK, result);

            var answer = session.CreateAnswer(IPAddress.Loopback);

            Assert.Equal(new[] { "transport-cc", "ccm fir", "nack", "nack pli" }, FeedbackLines(answer, 96));
            Assert.Equal(new[] { "nack pli" }, FeedbackLines(answer, 100));
        }

        /// <summary>
        /// A far end that lists no RTCP feedback, typical of a SIP desk phone, gets none in the answer.
        /// </summary>
        [Fact]
        public void AnswerToOfferWithoutFeedbackHasNone()
        {
            var remoteOffer = SessionHeader +
                $"m=video 5000 RTP/AVP 96{CRLF}" +
                $"a=rtpmap:96 VP8/90000{CRLF}" +
                $"a=sendrecv{CRLF}";

            using var session = CreateVideoSession();
            Assert.Equal(SetDescriptionResultEnum.OK, session.SetRemoteDescription(SdpType.offer, SDP.ParseSDPDescription(remoteOffer)));

            var answer = session.CreateAnswer(IPAddress.Loopback);

            Assert.DoesNotContain("a=rtcp-fb", answer.ToString());
        }

        /// <summary>
        /// The offerer's formats are narrowed to the answer's feedback, so a re-offer does not re-add types
        /// the far end declined.
        /// </summary>
        [Fact]
        public void OffererUsesFeedbackFromAnswer()
        {
            using var offerer = CreateVideoSession();
            var offer = offerer.CreateOffer(IPAddress.Loopback);

            var answer = SessionHeader +
                $"m=video 5000 RTP/AVPF 96{CRLF}" +
                $"a=rtpmap:96 VP8/90000{CRLF}" +
                $"a=rtcp-fb:96 ccm fir{CRLF}" +
                $"a=sendrecv{CRLF}";
            Assert.Equal(SetDescriptionResultEnum.OK, offerer.SetRemoteDescription(SdpType.answer, SDP.ParseSDPDescription(answer)));

            var vp8 = offerer.VideoLocalTrack.Capabilities.Single(x => x.ID == 96);
            Assert.Equal(new[] { "ccm fir" }, vp8.SupportedRtcpFeedbackMessages);
            Assert.NotEmpty(FeedbackLines(offer, 96));
        }

        /// <summary>
        /// Parsed feedback attributes are attached to their format, whether they come before or after the
        /// rtpmap, wildcard entries apply to every format and the SDP round trips without duplicates.
        /// </summary>
        [Fact]
        public void ParsedFeedbackRoundTrips()
        {
            var sdpText = SessionHeader +
                $"m=video 5000 RTP/AVPF 96 97{CRLF}" +
                $"a=rtcp-fb:96 nack{CRLF}" +
                $"a=rtpmap:96 VP8/90000{CRLF}" +
                $"a=rtcp-fb:96 nack   pli{CRLF}" +
                $"a=rtcp-fb:96 nack pli{CRLF}" +
                $"a=rtpmap:97 H264/90000{CRLF}" +
                $"a=rtcp-fb:* ccm fir{CRLF}";

            var sdp = SDP.ParseSDPDescription(sdpText);
            var video = sdp.Media.Single();

            Assert.Equal(new[] { "nack", "nack pli", "ccm fir" }, video.MediaFormats[96].SupportedRtcpFeedbackMessages);
            Assert.Equal(new[] { "ccm fir" }, video.MediaFormats[97].SupportedRtcpFeedbackMessages);
            Assert.True(video.MediaFormats[96].HasExplicitRtcpFeedbackMessages);
            Assert.DoesNotContain(video.ExtraMediaAttributes, x => x.StartsWith("a=rtcp-fb"));

            var roundTrip = SDP.ParseSDPDescription(sdp.ToString());

            Assert.Equal(new[] { "nack", "nack pli", "ccm fir" }, FeedbackLines(sdp, 96));
            Assert.Equal(new[] { "nack", "nack pli", "ccm fir" }, roundTrip.Media.Single().MediaFormats[96].SupportedRtcpFeedbackMessages);
            Assert.Equal(new[] { "ccm fir" }, roundTrip.Media.Single().MediaFormats[97].SupportedRtcpFeedbackMessages);
        }

        /// <summary>
        /// A parsed video format without feedback attributes does not pick up the local defaults.
        /// </summary>
        [Fact]
        public void ParsedFormatWithoutFeedbackHasEmptyList()
        {
            var sdp = SDP.ParseSDPDescription(SessionHeader +
                $"m=video 5000 RTP/AVP 96{CRLF}" +
                $"a=rtpmap:96 VP8/90000{CRLF}");

            var vp8 = sdp.Media.Single().MediaFormats[96];

            Assert.True(vp8.HasExplicitRtcpFeedbackMessages);
            Assert.Empty(vp8.SupportedRtcpFeedbackMessages);
            Assert.DoesNotContain("a=rtcp-fb", sdp.ToString());
        }

        /// <summary>
        /// Feedback for a payload type with no matching format is kept as an extra attribute.
        /// </summary>
        [Fact]
        public void FeedbackForUnknownFormatIsPreserved()
        {
            var sdp = SDP.ParseSDPDescription(SessionHeader +
                $"m=video 5000 RTP/AVP 96{CRLF}" +
                $"a=rtpmap:96 VP8/90000{CRLF}" +
                $"a=rtcp-fb:120 nack pli{CRLF}");

            Assert.Contains("a=rtcp-fb:120 nack pli", sdp.Media.Single().ExtraMediaAttributes);
            Assert.Contains("a=rtcp-fb:120 nack pli" + CRLF, sdp.ToString());
        }

        /// <summary>
        /// The feedback list survives the other With* copies and can be overridden per format.
        /// </summary>
        [Fact]
        public void FeedbackSurvivesFormatCopies()
        {
            var format = new SDPAudioVideoMediaFormat(new VideoFormat(VideoCodecsEnum.VP8, 96))
                .WithUpdatedRtcpFeedbackMessages(new[] { "nack pli", "NACK PLI", " " });

            Assert.Equal(new[] { "nack pli" }, format.SupportedRtcpFeedbackMessages);
            Assert.Equal(new[] { "nack pli" }, format.WithUpdatedID(98).WithUpdatedFmtp("x=1").WithUpdatedRtpmap("VP8/90000").SupportedRtcpFeedbackMessages);
            Assert.Equal(SDPAudioVideoMediaFormat.DefaultVideoRtcpFeedbackMessages,
                format.WithUpdatedRtcpFeedbackMessages(null).SupportedRtcpFeedbackMessages);
        }

        /// <summary>
        /// GetCompatibleFormats intersects video feedback and leaves audio formats untouched.
        /// </summary>
        [Fact]
        public void CompatibleFormatsIntersectFeedback()
        {
            var remote = new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8/90000")
                .WithUpdatedRtcpFeedbackMessages(new[] { "goog-remb", "Nack PLI", "nack" });
            var local = new SDPAudioVideoMediaFormat(new VideoFormat(VideoCodecsEnum.VP8, 96));

            var compatible = SDPAudioVideoMediaFormat.GetCompatibleFormats(
                new List<SDPAudioVideoMediaFormat> { remote }, new List<SDPAudioVideoMediaFormat> { local });

            Assert.Equal(new[] { "Nack PLI", "nack" }, compatible.Single().SupportedRtcpFeedbackMessages);

            var pcmu = new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU);
            var audio = SDPAudioVideoMediaFormat.GetCompatibleFormats(
                new List<SDPAudioVideoMediaFormat> { pcmu }, new List<SDPAudioVideoMediaFormat> { pcmu });
            Assert.Empty(audio.Single().SupportedRtcpFeedbackMessages);
            Assert.False(audio.Single().HasExplicitRtcpFeedbackMessages);
        }
    }
}
