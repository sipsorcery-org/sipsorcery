//-----------------------------------------------------------------------------
// Filename: SIPUserAgentMediaDirectionUnitTest.cs
//
// Description: Tests that a SIPUserAgent answering with an RTPSession gives the
// media lines of its answers the direction RFC 3264 section 6.1 requires, for
// the INVITE that sets up the call (a sendonly offer is answered recvonly) and
// for re-INVITEs, where its hold handling (the far end putting the call on hold
// and taking it off again, or this end holding it) has to keep working.
//
// History:
// 16 Nov 2026	Claude Code - Opus 5.5	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Net.UnitTests.Helpers;
using SIPSorcery.UnitTests;
using Xunit;
using Xunit.Abstractions;

namespace SIPSorcery.SIP.App.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPUserAgentMediaDirectionUnitTest
    {
        private static readonly IPEndPoint RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 5060);

        private readonly ILogger logger;

        // The far end's CSeq: 1 for the INVITE, then one more for each re-INVITE.
        private int _remoteCSeq = 1;

        public SIPUserAgentMediaDirectionUnitTest(ITestOutputHelper output)
        {
            logger = TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// The far end's SDP: audio and video, both with the same direction.
        /// </summary>
        private static string RemoteSdp(string direction, int version) =>
            "v=0\r\n" +
            $"o=- 3000 {version} IN IP4 127.0.0.1\r\n" +
            "s=-\r\n" +
            "c=IN IP4 127.0.0.1\r\n" +
            "t=0 0\r\n" +
            "m=audio 49170 RTP/AVP 0\r\n" +
            "a=rtpmap:0 PCMU/8000\r\n" +
            $"a={direction}\r\n" +
            "m=video 49172 RTP/AVP 96\r\n" +
            "a=rtpmap:96 VP8/90000\r\n" +
            $"a={direction}\r\n";

        private static RTPSession CreateMediaSession() =>
            new RtpSessionBuilder().WithAudioTrack().WithVideoTrack().Build();

        private static SIPRequest CreateInvite(string callId, IPEndPoint channelEndPoint, string sdp)
        {
            var uri = SIPURI.ParseSIPURI($"sip:user@{channelEndPoint}");
            var request = new SIPRequest(SIPMethodsEnum.INVITE, uri);
            var header = new SIPHeader(
                new SIPFromHeader(null, SIPURI.ParseSIPURI("sip:caller@127.0.0.1"), CallProperties.CreateNewTag()),
                new SIPToHeader(null, uri, null),
                1,
                callId);
            request.Header = header;
            header.CSeqMethod = SIPMethodsEnum.INVITE;
            header.Vias.PushViaHeader(new SIPViaHeader(channelEndPoint, CallProperties.CreateBranchId()));
            header.Contact = new List<SIPContactHeader> { new SIPContactHeader(null, uri) };
            header.ContentType = SDP.SDP_MIME_CONTENTTYPE;
            header.MaxForwards = 70;
            request.Body = sdp;
            return request;
        }

        /// <summary>
        /// A re-INVITE from the far end on the agent's established dialog.
        /// </summary>
        private static SIPRequest CreateReInvite(SIPDialogue dialogue, int cseq, IPEndPoint channelEndPoint, string sdp)
        {
            var uri = SIPURI.ParseSIPURI($"sip:user@{channelEndPoint}");
            var request = new SIPRequest(SIPMethodsEnum.INVITE, uri);
            var header = new SIPHeader(
                new SIPFromHeader(null, SIPURI.ParseSIPURI("sip:caller@127.0.0.1"), dialogue.RemoteTag),
                new SIPToHeader(null, uri, dialogue.LocalTag),
                cseq,
                dialogue.CallId);
            request.Header = header;
            header.CSeqMethod = SIPMethodsEnum.INVITE;
            header.Vias.PushViaHeader(new SIPViaHeader(channelEndPoint, CallProperties.CreateBranchId()));
            header.Contact = new List<SIPContactHeader> { new SIPContactHeader(null, uri) };
            header.ContentType = SDP.SDP_MIME_CONTENTTYPE;
            header.MaxForwards = 70;
            request.Body = sdp;
            return request;
        }

        private static void Inject(RecordingMockSIPChannel channel, IPEndPoint channelEndPoint, SIPRequest request) =>
            channel.FireMessageReceived(
                new SIPEndPoint(SIPProtocolsEnum.udp, channelEndPoint),
                new SIPEndPoint(SIPProtocolsEnum.udp, RemoteEndPoint),
                Encoding.UTF8.GetBytes(request.ToString()));

        /// <summary>
        /// Waits for the agent's 200 OK to an INVITE (by its Call-ID and CSeq) and returns the SDP
        /// answer in it.
        /// </summary>
        private async Task<SDP> WaitForOkAnswerAsync(RecordingMockSIPChannel channel, string callId, int cseq)
        {
            for (int i = 0; i < 100; i++)
            {
                var ok = channel.AllSentMessages.FirstOrDefault(m =>
                    m.StartsWith("SIP/2.0 200") &&
                    m.Contains(callId) &&
                    m.Contains($"CSeq: {cseq} INVITE"));

                if (ok != null)
                {
                    logger.LogDebug("200 OK for CSeq {CSeq}:\n{Response}", cseq, ok);
                    int bodyStart = ok.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
                    return SDP.ParseSDPDescription(ok.Substring(bodyStart));
                }

                await Task.Delay(50);
            }

            throw new TimeoutException($"No 200 OK was sent for the INVITE with CSeq {cseq}.");
        }

        /// <summary>
        /// Injects an INVITE carrying the given SDP offer, answers it with the media session and
        /// returns the SDP answer the agent sent.
        /// </summary>
        private async Task<SDP> AnswerCallAsync(SIPUserAgent agent, RecordingMockSIPChannel channel,
            IPEndPoint channelEndPoint, RTPSession mediaSession, string offer)
        {
            var incoming = new TaskCompletionSource<SIPRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
            agent.OnIncomingCall += (ua, req) => incoming.TrySetResult(req);

            string callId = CallProperties.CreateNewCallId();
            Inject(channel, channelEndPoint, CreateInvite(callId, channelEndPoint, offer));

            await Task.WhenAny(incoming.Task, Task.Delay(5000));
            Assert.True(incoming.Task.IsCompleted, "OnIncomingCall did not fire within timeout.");

            var uas = agent.AcceptCall(incoming.Task.Result);
            Assert.True(await agent.Answer(uas, mediaSession), "Failed to answer the call.");

            return await WaitForOkAnswerAsync(channel, callId, 1);
        }

        /// <summary>
        /// Sends the far end's re-INVITE with the given SDP offer and returns the SDP answer.
        /// </summary>
        private Task<SDP> ReInviteAsync(SIPUserAgent agent, RecordingMockSIPChannel channel,
            IPEndPoint channelEndPoint, string offer)
        {
            var reInvite = CreateReInvite(agent.Dialogue, ++_remoteCSeq, channelEndPoint, offer);
            Inject(channel, channelEndPoint, reInvite);
            return WaitForOkAnswerAsync(channel, reInvite.Header.CallId, reInvite.Header.CSeq);
        }

        private static void AssertDirection(SDP answer, MediaStreamStatusEnum expected)
        {
            SdpAssert.HasDirection(SdpAssert.Audio(answer), expected);
            SdpAssert.HasDirection(SdpAssert.Video(answer), expected);
        }

        /// <summary>
        /// The case that showed the bug: a call that's offered sendonly audio and video (a caller
        /// that only means to send, so this end can watch) is answered recvonly, not sendrecv, so
        /// the session doesn't send media back to a caller that doesn't want it.
        /// </summary>
        [Fact]
        public async Task SendOnlyInvite_AnsweredRecvOnly()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6170);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);
            var agent = new SIPUserAgent(transport, null);
            var mediaSession = CreateMediaSession();

            try
            {
                SDP answer = await AnswerCallAsync(agent, channel, channelEndPoint, mediaSession, RemoteSdp("sendonly", 0));

                AssertDirection(answer, MediaStreamStatusEnum.RecvOnly);
                Assert.Equal(MediaStreamStatusEnum.RecvOnly, mediaSession.AudioStream.LocalTrack.StreamStatus);
                Assert.Equal(MediaStreamStatusEnum.RecvOnly, mediaSession.VideoStream.LocalTrack.StreamStatus);
            }
            finally
            {
                agent.Close();
                mediaSession.Close(null);
                transport.Shutdown();
            }
        }

        /// <summary>
        /// The far end puts the call on hold with a sendonly re-INVITE, answered recvonly, and
        /// takes it off with a sendrecv one, answered sendrecv; the hold events fire each way.
        /// </summary>
        [Fact]
        public async Task RemoteHoldAndResume_ReInvitesAnsweredRecvOnlyThenSendRecv()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6171);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);
            var agent = new SIPUserAgent(transport, null);
            var mediaSession = CreateMediaSession();

            int putOnHold = 0, tookOffHold = 0;
            agent.RemotePutOnHold += () => putOnHold++;
            agent.RemoteTookOffHold += () => tookOffHold++;

            try
            {
                AssertDirection(await AnswerCallAsync(agent, channel, channelEndPoint, mediaSession, RemoteSdp("sendrecv", 0)),
                    MediaStreamStatusEnum.SendRecv);

                SDP holdAnswer = await ReInviteAsync(agent, channel, channelEndPoint, RemoteSdp("sendonly", 1));
                AssertDirection(holdAnswer, MediaStreamStatusEnum.RecvOnly);
                Assert.True(agent.IsOnRemoteHold);
                Assert.Equal(1, putOnHold);

                SDP resumeAnswer = await ReInviteAsync(agent, channel, channelEndPoint, RemoteSdp("sendrecv", 2));
                AssertDirection(resumeAnswer, MediaStreamStatusEnum.SendRecv);
                Assert.False(agent.IsOnRemoteHold);
                Assert.Equal(1, tookOffHold);
                Assert.Equal(MediaStreamStatusEnum.SendRecv, mediaSession.AudioStream.LocalTrack.StreamStatus);
                Assert.Equal(MediaStreamStatusEnum.SendRecv, mediaSession.VideoStream.LocalTrack.StreamStatus);
            }
            finally
            {
                agent.Close();
                mediaSession.Close(null);
                transport.Shutdown();
            }
        }

        /// <summary>
        /// The hold state alone would answer an inactive or recvonly re-INVITE sendrecv; the
        /// answer can't allow more than the offer, so they're answered inactive and sendonly.
        /// </summary>
        [Fact]
        public async Task InactiveAndRecvOnlyReInvites_AnsweredInactiveAndSendOnly()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6172);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);
            var agent = new SIPUserAgent(transport, null);
            var mediaSession = CreateMediaSession();

            try
            {
                await AnswerCallAsync(agent, channel, channelEndPoint, mediaSession, RemoteSdp("sendrecv", 0));

                AssertDirection(await ReInviteAsync(agent, channel, channelEndPoint, RemoteSdp("inactive", 1)),
                    MediaStreamStatusEnum.Inactive);

                AssertDirection(await ReInviteAsync(agent, channel, channelEndPoint, RemoteSdp("recvonly", 2)),
                    MediaStreamStatusEnum.SendOnly);

                AssertDirection(await ReInviteAsync(agent, channel, channelEndPoint, RemoteSdp("sendrecv", 3)),
                    MediaStreamStatusEnum.SendRecv);
            }
            finally
            {
                agent.Close();
                mediaSession.Close(null);
                transport.Shutdown();
            }
        }

        /// <summary>
        /// With the call on hold at this end, a sendrecv re-INVITE from the far end is answered
        /// sendonly: the hold stays.
        /// </summary>
        [Fact]
        public async Task LocalHold_SendRecvReInviteAnsweredSendOnly()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6173);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);
            var agent = new SIPUserAgent(transport, null);
            var mediaSession = CreateMediaSession();

            try
            {
                await AnswerCallAsync(agent, channel, channelEndPoint, mediaSession, RemoteSdp("sendrecv", 0));

                agent.PutOnHold();
                Assert.True(agent.IsOnLocalHold);

                AssertDirection(await ReInviteAsync(agent, channel, channelEndPoint, RemoteSdp("sendrecv", 1)),
                    MediaStreamStatusEnum.SendOnly);
            }
            finally
            {
                agent.Close();
                mediaSession.Close(null);
                transport.Shutdown();
            }
        }
    }
}
