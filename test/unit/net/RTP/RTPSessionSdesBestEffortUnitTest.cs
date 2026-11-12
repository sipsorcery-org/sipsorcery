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

        private static RTPSession Session(RtpSecureMediaOptionEnum option, MediaStreamStatusEnum direction = MediaStreamStatusEnum.SendRecv)
        {
            var session = new RTPSession(new RtpSessionConfig { RtpSecureMediaOption = option, BindAddress = IPAddress.Loopback });
            session.addTrack(new MediaStreamTrack(SDPMediaTypesEnum.audio, false,
                new System.Collections.Generic.List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) }, direction));
            return session;
        }
    }
}
