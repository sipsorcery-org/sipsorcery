//-----------------------------------------------------------------------------
// Filename: RTPSessionSdesBestEffortUnitTest.cs
//
// Description: Unit tests for best-effort SRTP with SDP crypto attributes
// (RtpSecureMediaOptionEnum.SdpCryptoBestEffort): crypto offered on RTP/AVP so
// a far end without SRTP ignores it and the call goes ahead as plain RTP, and
// one with SRTP takes it up. Each media stream settles on SRTP or plain RTP on
// its own.
//
// History:
// 12 Nov 2026	Claude Code - Opus 5.5	Created.
// 14 Nov 2026	Claude Code - Opus 5.5	A new far end's keys on a session's next offer, and video on one side only.
// 17 Nov 2026	Claude Code - Opus 5.5	An answer that leaves out one of our m-lines.
// 17 Nov 2026	Claude Code - Opus 5.5	A declined video stream a re-offer sets to send and receive.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net.UnitTests.Helpers;
using SIPSorcery.SIP.App;
using SIPSorcery.UnitTests;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTPSessionSdesBestEffortUnitTest
    {
        private const string CRYPTO_80 = "a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:WVNfX19zZW1jdGwgKCkgewkyMjA7fQp9CnVubGVz|2^20|1:32";

        private readonly ILogger logger;

        public RTPSessionSdesBestEffortUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = TestLogHelper.InitTestLogger(output);
        }

        private static RTPSession BestEffort(bool video = false)
        {
            var builder = new RtpSessionBuilder().WithSdpCryptoBestEffort().WithBindAddress(IPAddress.Loopback).WithAudioTrack();
            if (video)
            {
                builder.WithVideoTrack();
            }
            return builder.Build();
        }

        private static SDP Audio(string transport, bool crypto, string direction = "sendrecv", int sessionVersion = 0) =>
            SDP.ParseSDPDescription(
$@"v=0
o=- 5000 {sessionVersion} IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20030 {transport} 0
a=rtpmap:0 PCMU/8000
{(crypto ? CRYPTO_80 + "\n" : "")}a={direction}");

        /// <summary>
        /// Our offer is RTP/AVP, which every SIP phone understands, with crypto lines for one that
        /// does SRTP to pick from.
        /// </summary>
        [Fact]
        public void Offer_IsRtpAvpWithCrypto()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = BestEffort())
            {
                var audio = session.CreateOffer(IPAddress.Loopback).Media.Single();

                Assert.Equal("RTP/AVP", audio.Transport);
                Assert.Equal(2, audio.SecurityDescriptions.Count);
            }
        }

        /// <summary>
        /// A far end without SRTP answers plain RTP/AVP: the call goes ahead unencrypted rather
        /// than failing, and media isn't held back waiting for keys.
        /// </summary>
        [Fact]
        public void Offerer_PlainAnswer_UsesPlainRtp()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = BestEffort())
            {
                session.CreateOffer(IPAddress.Loopback);

                Assert.Equal(SetDescriptionResultEnum.OK, session.SetRemoteDescription(SdpType.answer, Audio("RTP/AVP", false)));
                Assert.False(session.AudioStream.IsSecurityContextReady());
                Assert.False(session.AudioStream.RequiresSecureContext);
                Assert.False(session.AudioStream.IsAwaitingSecureContext);
            }
        }

        [Theory]
        [InlineData("RTP/AVP")]
        [InlineData("RTP/SAVP")]
        public void Offerer_AnswerWithCrypto_UsesSrtp(string transport)
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = BestEffort())
            {
                session.CreateOffer(IPAddress.Loopback);

                Assert.Equal(SetDescriptionResultEnum.OK, session.SetRemoteDescription(SdpType.answer, Audio(transport, true)));
                Assert.True(session.AudioStream.IsSecurityContextReady());
            }
        }

        /// <summary>
        /// Until the answer comes the stream is treated as secure, so a far end that answers with
        /// crypto and sends straight away doesn't have its first packets read as plain RTP.
        /// </summary>
        [Fact]
        public void Offerer_BeforeAnswer_AwaitsKeys()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = BestEffort())
            {
                session.CreateOffer(IPAddress.Loopback);

                Assert.True(session.AudioStream.IsAwaitingSecureContext);
            }
        }

        /// <summary>
        /// A far end whose answer used RTP/SAVP is kept on it in our re-offers (hold, say).
        /// </summary>
        [Fact]
        public void Offerer_SavpAnswer_ReofferKeepsSavp()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = BestEffort())
            {
                session.CreateOffer(IPAddress.Loopback);
                session.SetRemoteDescription(SdpType.answer, Audio("RTP/SAVP", true));

                var reoffer = session.CreateOffer(IPAddress.Loopback).Media.Single();

                Assert.Equal("RTP/SAVP", reoffer.Transport);
                Assert.Single(reoffer.SecurityDescriptions);
            }
        }

        /// <summary>
        /// Once a stream has settled on plain RTP our re-offers leave crypto out, so media isn't
        /// held back waiting for keys a far end without SRTP will never send.
        /// </summary>
        [Fact]
        public void Offerer_PlainAnswer_ReofferHasNoCrypto()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = BestEffort())
            {
                session.CreateOffer(IPAddress.Loopback);
                session.SetRemoteDescription(SdpType.answer, Audio("RTP/AVP", false));

                var reoffer = session.CreateOffer(IPAddress.Loopback).Media.Single();

                Assert.Equal("RTP/AVP", reoffer.Transport);
                Assert.Empty(reoffer.SecurityDescriptions);
                Assert.False(session.AudioStream.RequiresSecureContext);
            }
        }

        [Fact]
        public void Answerer_PlainOffer_AnswersPlain()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = BestEffort())
            {
                Assert.Equal(SetDescriptionResultEnum.OK, session.SetRemoteDescription(SdpType.offer, Audio("RTP/AVP", false)));
                var answer = session.CreateAnswer(IPAddress.Loopback).Media.Single();

                Assert.Equal("RTP/AVP", answer.Transport);
                Assert.Empty(answer.SecurityDescriptions);
                Assert.False(session.AudioStream.RequiresSecureContext);
            }
        }

        /// <summary>
        /// Crypto on RTP/AVP (another best-effort phone) is answered in kind; on RTP/SAVP
        /// (a phone set to require SRTP) with RTP/SAVP.
        /// </summary>
        [Theory]
        [InlineData("RTP/AVP")]
        [InlineData("RTP/SAVP")]
        public void Answerer_OfferWithCrypto_AnswersWithCryptoOnSameProfile(string transport)
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = BestEffort())
            {
                Assert.Equal(SetDescriptionResultEnum.OK, session.SetRemoteDescription(SdpType.offer, Audio(transport, true)));
                var answer = session.CreateAnswer(IPAddress.Loopback).Media.Single();

                Assert.Equal(transport, answer.Transport);
                Assert.Single(answer.SecurityDescriptions);
                Assert.True(session.AudioStream.IsSecurityContextReady());
            }
        }

        /// <summary>
        /// RTP/SAVP without a crypto suite we can use can't be answered: SRTP is required and
        /// there are no keys.
        /// </summary>
        [Fact]
        public void Answerer_SavpOfferWithoutCrypto_Fails()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = BestEffort())
            {
                Assert.Equal(SetDescriptionResultEnum.CryptoNegotiationFailed,
                    session.SetRemoteDescription(SdpType.offer, Audio("RTP/SAVP", false)));
            }
        }

        /// <summary>
        /// A re-offer without crypto on an SRTP call (a far end that's been transferred to one
        /// without SRTP, say) changes the stream to plain RTP rather than failing.
        /// </summary>
        [Fact]
        public void Answerer_ReofferWithoutCrypto_ChangesToPlainRtp()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = BestEffort())
            {
                session.SetRemoteDescription(SdpType.offer, Audio("RTP/AVP", true));
                session.CreateAnswer(IPAddress.Loopback);
                Assert.True(session.AudioStream.IsSecurityContextReady());

                Assert.Equal(SetDescriptionResultEnum.OK, session.SetRemoteDescription(SdpType.offer, Audio("RTP/AVP", false, sessionVersion: 1)));
                var answer = session.CreateAnswer(IPAddress.Loopback).Media.Single();

                Assert.Empty(answer.SecurityDescriptions);
                Assert.False(session.AudioStream.IsSecurityContextReady());
                Assert.False(session.AudioStream.RequiresSecureContext);
            }
        }

        /// <summary>
        /// A local track the far end's offer didn't include (our video on an audio call) mustn't
        /// hold back received media waiting for keys that will never come.
        /// </summary>
        [Fact]
        public void Answerer_LocalTrackNotOffered_DoesNotHoldBackMedia()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = BestEffort(video: true))
            {
                session.SetRemoteDescription(SdpType.offer, Audio("RTP/AVP", false));
                session.CreateAnswer(IPAddress.Loopback);

                Assert.False(session.AudioStream.IsAwaitingSecureContext);
                Assert.False(session.VideoStream.IsAwaitingSecureContext);
            }
        }

        /// <summary>
        /// An answer that leaves out one of our m-lines instead of declining it with port zero
        /// (LiveKit's SIP answers an audio and text offer with plain audio alone): the left-out
        /// stream is taken as declined, and doesn't hold back the audio waiting for keys.
        /// </summary>
        [Fact]
        public void Offerer_AnswerLeavesOutStream_IsDeclinedAndDoesNotHoldBackMedia()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = new RtpSessionBuilder().WithSdpCryptoBestEffort().WithBindAddress(IPAddress.Loopback).WithAudioTrack()
                .WithTrack(new MediaStreamTrack(new TextFormat(TextCodecsEnum.T140, 98))).Build())
            {
                Assert.Equal(2, session.CreateOffer(IPAddress.Loopback).Media.Count);

                Assert.Equal(SetDescriptionResultEnum.OK, session.SetRemoteDescription(SdpType.answer, Audio("RTP/AVP", false)));
                Assert.False(session.AudioStream.IsAwaitingSecureContext);
                Assert.False(session.TextStream.IsAwaitingSecureContext);
                Assert.Equal(MediaStreamStatusEnum.Inactive, session.TextStream.LocalTrack.StreamStatus);
            }
        }

        /// <summary>
        /// The same, end to end: the far end's audio gets through, both ways.
        /// </summary>
        [Fact]
        public async Task Offerer_AnswerLeavesOutStream_AudioGetsThroughBothWays()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var offerer = new RtpSessionBuilder().WithSdpCryptoBestEffort().WithBindAddress(IPAddress.Loopback).WithAudioTrack()
                .WithTrack(new MediaStreamTrack(new TextFormat(TextCodecsEnum.T140, 98))).Build())
            using (var answerer = new RtpSessionBuilder().WithBindAddress(IPAddress.Loopback).WithAudioTrack().Build())
            {
                Assert.Equal(SetDescriptionResultEnum.OK, answerer.SetRemoteDescription(SdpType.offer, offerer.CreateOffer(IPAddress.Loopback)));
                var answer = answerer.CreateAnswer(IPAddress.Loopback);
                answer.Media.RemoveAll(m => m.Media == SDPMediaTypesEnum.text);
                Assert.Equal(SetDescriptionResultEnum.OK, offerer.SetRemoteDescription(SdpType.answer, answer));

                var atAnswerer = new ConcurrentQueue<byte[]>();
                var atOfferer = new ConcurrentQueue<byte[]>();
                answerer.OnRtpPacketReceived += (ep, kind, pkt) => { if (kind == SDPMediaTypesEnum.audio) { atAnswerer.Enqueue(pkt.Payload.ToArray()); } };
                offerer.OnRtpPacketReceived += (ep, kind, pkt) => { if (kind == SDPMediaTypesEnum.audio) { atOfferer.Enqueue(pkt.Payload.ToArray()); } };
                await offerer.Start();
                await answerer.Start();

                var payload = Enumerable.Range(0, 160).Select(i => (byte)i).ToArray();
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while ((atAnswerer.IsEmpty || atOfferer.IsEmpty) && DateTime.UtcNow < deadline)
                {
                    offerer.SendAudio(160, payload);
                    answerer.SendAudio(160, payload);
                    await Task.Delay(20);
                }

                Assert.False(atAnswerer.IsEmpty, "No audio at the answerer.");
                Assert.False(atOfferer.IsEmpty, "No audio at the offerer.");

                offerer.Close("done");
                answerer.Close("done");
            }
        }

        /// <summary>
        /// Two best-effort sessions agree SRTP; best effort answering mandatory SDES agrees SRTP;
        /// best effort with no SRTP agrees plain RTP both ways round.
        /// </summary>
        [Theory]
        [InlineData(RtpSecureMediaOptionEnum.SdpCryptoBestEffort, RtpSecureMediaOptionEnum.SdpCryptoBestEffort, true)]
        [InlineData(RtpSecureMediaOptionEnum.SdpCryptoNegotiation, RtpSecureMediaOptionEnum.SdpCryptoBestEffort, true)]
        [InlineData(RtpSecureMediaOptionEnum.SdpCryptoBestEffort, RtpSecureMediaOptionEnum.None, false)]
        [InlineData(RtpSecureMediaOptionEnum.None, RtpSecureMediaOptionEnum.SdpCryptoBestEffort, false)]
        public void OfferAnswer_BetweenModes(RtpSecureMediaOptionEnum offerer, RtpSecureMediaOptionEnum answerer, bool srtp)
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var a = Session(offerer))
            using (var b = Session(answerer))
            {
                Assert.Equal(SetDescriptionResultEnum.OK, b.SetRemoteDescription(SdpType.offer, a.CreateOffer(IPAddress.Loopback)));
                Assert.Equal(SetDescriptionResultEnum.OK, a.SetRemoteDescription(SdpType.answer, b.CreateAnswer(IPAddress.Loopback)));

                Assert.Equal(srtp, a.AudioStream.IsSecurityContextReady());
                Assert.Equal(srtp, b.AudioStream.IsSecurityContextReady());
            }
        }

        /// <summary>
        /// The limit of best effort: a far end that requires SRTP (mandatory SDES) only takes
        /// RTP/SAVP offers, so it refuses ours, as a phone set to require SRTP would (488).
        /// </summary>
        [Fact]
        public void Offer_ToMandatorySdes_IsRefused()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var a = Session(RtpSecureMediaOptionEnum.SdpCryptoBestEffort))
            using (var b = Session(RtpSecureMediaOptionEnum.SdpCryptoNegotiation))
            {
                Assert.Equal(SetDescriptionResultEnum.CryptoNegotiationFailed,
                    b.SetRemoteDescription(SdpType.offer, a.CreateOffer(IPAddress.Loopback)));
            }
        }

        /// <summary>
        /// Media between two best-effort sessions is encrypted and decrypted end to end, and
        /// between best effort and a session without SRTP it gets through as plain RTP.
        /// </summary>
        [Theory]
        [InlineData(RtpSecureMediaOptionEnum.SdpCryptoBestEffort, RtpSecureMediaOptionEnum.SdpCryptoBestEffort)]
        [InlineData(RtpSecureMediaOptionEnum.SdpCryptoBestEffort, RtpSecureMediaOptionEnum.None)]
        [InlineData(RtpSecureMediaOptionEnum.None, RtpSecureMediaOptionEnum.SdpCryptoBestEffort)]
        public async Task Media_GetsThrough(RtpSecureMediaOptionEnum sender, RtpSecureMediaOptionEnum receiver)
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var tx = Session(sender, MediaStreamStatusEnum.SendOnly))
            using (var rx = Session(receiver, MediaStreamStatusEnum.RecvOnly))
            {
                var received = new ConcurrentQueue<byte[]>();
                rx.OnRtpPacketReceived += (ep, kind, pkt) => received.Enqueue(pkt.Payload.ToArray());

                rx.SetRemoteDescription(SdpType.offer, tx.CreateOffer(IPAddress.Loopback));
                tx.SetRemoteDescription(SdpType.answer, rx.CreateAnswer(IPAddress.Loopback));
                await tx.Start();
                await rx.Start();

                var payload = Enumerable.Range(0, 160).Select(i => (byte)i).ToArray();
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while (received.IsEmpty && DateTime.UtcNow < deadline)
                {
                    tx.SendAudio(160, payload);
                    await Task.Delay(20);
                }

                Assert.True(received.TryPeek(out var first), "No media received.");
                Assert.Equal(payload, first);

                tx.Close("done");
                rx.Close("done");
            }
        }

        /// <summary>
        /// Another far end taking over an SRTP call (the INVITE with Replaces after an attended
        /// transfer, answered on the replaced call's session) brings new keys: media gets through
        /// both ways with them. Before, the answer reused our key without building the contexts
        /// again, so what the new far end sent failed to decrypt ("SRTP unprotect failed").
        /// </summary>
        [Theory]
        [InlineData(RtpSecureMediaOptionEnum.SdpCryptoBestEffort)]
        [InlineData(RtpSecureMediaOptionEnum.SdpCryptoNegotiation)]
        public async Task Answerer_NewFarEndTakesOver_MediaGetsThroughBothWays(RtpSecureMediaOptionEnum option)
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var bob = Session(option))
            using (var dave = Session(option))
            using (var alice = Session(option))
            {
                // Dave's call with Bob.
                Assert.Equal(SetDescriptionResultEnum.OK, dave.SetRemoteDescription(SdpType.offer, bob.CreateOffer(IPAddress.Loopback)));
                Assert.Equal(SetDescriptionResultEnum.OK, bob.SetRemoteDescription(SdpType.answer, dave.CreateAnswer(IPAddress.Loopback)));

                // Alice takes it over, with keys of her own.
                Assert.Equal(SetDescriptionResultEnum.OK, dave.SetRemoteDescription(SdpType.offer, alice.CreateOffer(IPAddress.Loopback)));
                Assert.Equal(SetDescriptionResultEnum.OK, alice.SetRemoteDescription(SdpType.answer, dave.CreateAnswer(IPAddress.Loopback)));
                Assert.True(dave.AudioStream.IsSecurityContextReady());

                var atDave = new ConcurrentQueue<byte[]>();
                var atAlice = new ConcurrentQueue<byte[]>();
                dave.OnRtpPacketReceived += (ep, kind, pkt) => atDave.Enqueue(pkt.Payload.ToArray());
                alice.OnRtpPacketReceived += (ep, kind, pkt) => atAlice.Enqueue(pkt.Payload.ToArray());
                await dave.Start();
                await alice.Start();

                var payload = Enumerable.Range(0, 160).Select(i => (byte)i).ToArray();
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while ((atDave.IsEmpty || atAlice.IsEmpty) && DateTime.UtcNow < deadline)
                {
                    alice.SendAudio(160, payload);
                    dave.SendAudio(160, payload);
                    await Task.Delay(20);
                }

                Assert.True(atDave.TryPeek(out var fromAlice), "Nothing from Alice decrypted at Dave.");
                Assert.Equal(payload, fromAlice);
                Assert.True(atAlice.TryPeek(out var fromDave), "Nothing from Dave decrypted at Alice.");
                Assert.Equal(payload, fromDave);

                alice.Close("done");
                dave.Close("done");
            }
        }

        /// <summary>
        /// A re-offer with the same keys (hold, say) keeps the SRTP contexts as they are, rollover
        /// counters and all, rather than starting them again.
        /// </summary>
        [Fact]
        public void Answerer_ReofferWithSameKeys_KeepsTheContexts()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = BestEffort())
            {
                session.SetRemoteDescription(SdpType.offer, Audio("RTP/AVP", true));
                session.CreateAnswer(IPAddress.Loopback);
                var context = session.AudioStream.GetOrCreateSrtpHandler().Context;
                Assert.NotNull(context);

                Assert.Equal(SetDescriptionResultEnum.OK, session.SetRemoteDescription(SdpType.offer, Audio("RTP/AVP", true, "sendonly", sessionVersion: 1)));
                session.CreateAnswer(IPAddress.Loopback);

                Assert.Same(context, session.AudioStream.GetOrCreateSrtpHandler().Context);
            }
        }

        /// <summary>
        /// Video on one side only, with best-effort and mandatory SDES: audio gets through both
        /// ways. The stream without a local track (answered with port 0) or with one the far end
        /// never offered mustn't hold back the session's media waiting for keys that never come.
        /// Before, an audio-only session answering an audio and video offer (an INVITE with
        /// Replaces from a video phone, after an attended transfer) received no audio at all.
        /// </summary>
        [Theory]
        [InlineData(RtpSecureMediaOptionEnum.SdpCryptoBestEffort, true)]
        [InlineData(RtpSecureMediaOptionEnum.SdpCryptoBestEffort, false)]
        [InlineData(RtpSecureMediaOptionEnum.SdpCryptoNegotiation, true)]
        [InlineData(RtpSecureMediaOptionEnum.SdpCryptoNegotiation, false)]
        public async Task VideoOnOneSideOnly_AudioGetsThroughBothWays(RtpSecureMediaOptionEnum option, bool videoAtOfferer)
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            RtpSessionBuilder Builder(bool video)
            {
                var b = new RtpSessionBuilder().WithBindAddress(IPAddress.Loopback).WithAudioTrack();
                b = option == RtpSecureMediaOptionEnum.SdpCryptoBestEffort ? b.WithSdpCryptoBestEffort() : b.WithSdpCryptoNegotiation();
                return video ? b.WithVideoTrack() : b;
            }

            using (var offerer = Builder(videoAtOfferer).Build())
            using (var answerer = Builder(!videoAtOfferer).Build())
            {
                Assert.Equal(SetDescriptionResultEnum.OK, answerer.SetRemoteDescription(SdpType.offer, offerer.CreateOffer(IPAddress.Loopback)));
                Assert.Equal(SetDescriptionResultEnum.OK, offerer.SetRemoteDescription(SdpType.answer, answerer.CreateAnswer(IPAddress.Loopback)));

                var atAnswerer = new ConcurrentQueue<byte[]>();
                var atOfferer = new ConcurrentQueue<byte[]>();
                answerer.OnRtpPacketReceived += (ep, kind, pkt) => { if (kind == SDPMediaTypesEnum.audio) { atAnswerer.Enqueue(pkt.Payload.ToArray()); } };
                offerer.OnRtpPacketReceived += (ep, kind, pkt) => { if (kind == SDPMediaTypesEnum.audio) { atOfferer.Enqueue(pkt.Payload.ToArray()); } };
                await offerer.Start();
                await answerer.Start();

                var payload = Enumerable.Range(0, 160).Select(i => (byte)i).ToArray();
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while ((atAnswerer.IsEmpty || atOfferer.IsEmpty) && DateTime.UtcNow < deadline)
                {
                    offerer.SendAudio(160, payload);
                    answerer.SendAudio(160, payload);
                    await Task.Delay(20);
                }

                Assert.False(atAnswerer.IsEmpty, "No audio at the answerer.");
                Assert.False(atOfferer.IsEmpty, "No audio at the offerer.");

                offerer.Close("done");
                answerer.Close("done");
            }
        }

        /// <summary>
        /// A video stream we declined (port 0: we have no video) whose local track a later
        /// re-INVITE sets to send and receive, as the SIP user agent does from the offer's
        /// direction. It carries nothing, so it mustn't wait for keys: its wait held back all the
        /// call's received audio. Seen when a softphone put a call on hold with video added (as
        /// it started a transfer), then took it off hold.
        /// </summary>
        [Theory]
        [InlineData(RtpSecureMediaOptionEnum.SdpCryptoBestEffort)]
        [InlineData(RtpSecureMediaOptionEnum.SdpCryptoNegotiation)]
        public async Task Answerer_DeclinedVideoSetSendRecvByReoffer_AudioGetsThroughBothWays(RtpSecureMediaOptionEnum option)
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            RtpSessionBuilder Builder(bool video)
            {
                var b = new RtpSessionBuilder().WithBindAddress(IPAddress.Loopback).WithAudioTrack();
                b = option == RtpSecureMediaOptionEnum.SdpCryptoBestEffort ? b.WithSdpCryptoBestEffort() : b.WithSdpCryptoNegotiation();
                return video ? b.WithVideoTrack() : b;
            }

            using (var offerer = Builder(true).Build())
            using (var answerer = Builder(false).Build())
            {
                Assert.Equal(SetDescriptionResultEnum.OK, answerer.SetRemoteDescription(SdpType.offer, offerer.CreateOffer(IPAddress.Loopback)));
                var answer = answerer.CreateAnswer(IPAddress.Loopback);
                Assert.Equal(0, answer.Media.Single(m => m.Media == SDPMediaTypesEnum.video).Port);
                Assert.Equal(SetDescriptionResultEnum.OK, offerer.SetRemoteDescription(SdpType.answer, answer));

                // Their re-offer, and the user agent setting each stream's direction from it.
                Assert.Equal(SetDescriptionResultEnum.OK, answerer.SetRemoteDescription(SdpType.offer, offerer.CreateOffer(IPAddress.Loopback)));
                answerer.SetMediaStreamStatus(SDPMediaTypesEnum.video, MediaStreamStatusEnum.SendRecv);
                answer = answerer.CreateAnswer(IPAddress.Loopback);
                Assert.Equal(0, answer.Media.Single(m => m.Media == SDPMediaTypesEnum.video).Port);
                Assert.Equal(SetDescriptionResultEnum.OK, offerer.SetRemoteDescription(SdpType.answer, answer));

                Assert.False(answerer.VideoStream.IsAwaitingSecureContext);

                var atAnswerer = new ConcurrentQueue<byte[]>();
                var atOfferer = new ConcurrentQueue<byte[]>();
                answerer.OnRtpPacketReceived += (ep, kind, pkt) => { if (kind == SDPMediaTypesEnum.audio) { atAnswerer.Enqueue(pkt.Payload.ToArray()); } };
                offerer.OnRtpPacketReceived += (ep, kind, pkt) => { if (kind == SDPMediaTypesEnum.audio) { atOfferer.Enqueue(pkt.Payload.ToArray()); } };
                await offerer.Start();
                await answerer.Start();

                var payload = Enumerable.Range(0, 160).Select(i => (byte)i).ToArray();
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while ((atAnswerer.IsEmpty || atOfferer.IsEmpty) && DateTime.UtcNow < deadline)
                {
                    offerer.SendAudio(160, payload);
                    answerer.SendAudio(160, payload);
                    await Task.Delay(20);
                }

                Assert.False(atAnswerer.IsEmpty, "No audio at the answerer.");
                Assert.False(atOfferer.IsEmpty, "No audio at the offerer.");

                offerer.Close("done");
                answerer.Close("done");
            }
        }

        private static RTPSession Session(RtpSecureMediaOptionEnum option, MediaStreamStatusEnum direction = MediaStreamStatusEnum.SendRecv)
        {
            var session = new RTPSession(new RtpSessionConfig { RtpSecureMediaOption = option, BindAddress = IPAddress.Loopback });
            session.addTrack(new MediaStreamTrack(SDPMediaTypesEnum.audio, false,
                new System.Collections.Generic.List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) }, direction));
            return session;
        }
    }
}
