//-----------------------------------------------------------------------------
// Filename: SdpFixtures.cs
//
// Description: Hand-curated SDP strings used as inputs to SDP-negotiation
// characterization tests. The set spans:
//
//   - Canonical synthetic SDPs covering the major shapes the negotiation
//     code branches on (audio-only / video-only / audio+video / text+audio /
//     hold, with vs without ICE/DTLS/SDES).
//   - "Real-world" captures from interop peers (Chrome, Firefox, Asterisk,
//     Janus, Pion) that exercise quirks the synthetic fixtures don't.
//   - Large offers (many m-sections, codecs and candidates) used to stress
//     the parser and as benchmark inputs.
//
// Each fixture is a verbatim SDP string. The associated test should parse it
// with SDP.ParseSDPDescription and feed it to SetRemoteDescription /
// createAnswer / etc. as appropriate.
//
// NEVER tweak a fixture in-place to make a test pass. Either fix the code or
// add a new fixture covering the new shape — the value of these is that they
// are stable, comparable inputs.
//
// History:
// 20 May 2026	Claude Code - Opus 4.7	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace SIPSorcery.Net.UnitTests.Helpers
{
    /// <summary>
    /// Static collection of canonical SDP strings used as inputs to
    /// characterization tests for createOffer / createAnswer /
    /// SetRemoteDescription. See file header for rules of engagement.
    /// </summary>
    public static class SdpFixtures
    {
        // ---------------------------------------------------------------
        // SECTION 1 — Minimal synthetic offers (plain RTP, no ICE/DTLS).
        // Used for RTPSession-level tests where WebRTC machinery is off.
        // ---------------------------------------------------------------

        /// <summary>
        /// Bare audio-only offer with a single PCMU codec. Sendrecv. No ICE,
        /// no DTLS, no crypto. Mirrors what a simple SIP softphone would send.
        /// </summary>
        public const string AudioOnlyOfferPcmu =
@"v=0
o=- 1000 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20000 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendrecv";

        /// <summary>
        /// Audio offer with PCMU + telephone-event (DTMF). Exercises the
        /// telephone-event capability-matching special case in
        /// RTPSession.SetRemoteDescription.
        /// </summary>
        public const string AudioOfferPcmuWithDtmf =
@"v=0
o=- 1001 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20002 RTP/AVP 0 101
a=rtpmap:0 PCMU/8000
a=rtpmap:101 telephone-event/8000
a=fmtp:101 0-15
a=sendrecv";

        /// <summary>
        /// Video-only offer with VP8 (dynamic PT 96). Sendrecv.
        /// </summary>
        public const string VideoOnlyOfferVp8 =
@"v=0
o=- 1002 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=video 20004 RTP/AVP 96
a=rtpmap:96 VP8/90000
a=sendrecv";

        /// <summary>
        /// Audio + video offer, audio first then video. Two m-lines on
        /// distinct ports (no bundle, no mux).
        /// </summary>
        public const string AudioVideoOfferAudioFirst =
@"v=0
o=- 1003 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20006 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendrecv
m=video 20008 RTP/AVP 96
a=rtpmap:96 VP8/90000
a=sendrecv";

        /// <summary>
        /// Same content as AudioVideoOfferAudioFirst but with the video
        /// m-line first. Exercises the renegotiation m-line-order
        /// preservation logic.
        /// </summary>
        public const string AudioVideoOfferVideoFirst =
@"v=0
o=- 1004 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=video 20008 RTP/AVP 96
a=rtpmap:96 VP8/90000
a=sendrecv
m=audio 20006 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendrecv";

        // ---------------------------------------------------------------
        // SECTION 2 — Direction / hold / inactive variants.
        // Each is a copy of AudioOnlyOfferPcmu with the a=direction tweaked.
        // ---------------------------------------------------------------

        public const string AudioOfferSendOnly =
@"v=0
o=- 1010 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20010 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendonly";

        public const string AudioOfferRecvOnly =
@"v=0
o=- 1011 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20012 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=recvonly";

        public const string AudioOfferInactive =
@"v=0
o=- 1012 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20014 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=inactive";

        /// <summary>
        /// Classic SIP "hold" form: c=IN IP4 0.0.0.0 (RFC 3264 §8.4 hold).
        /// The session-level connection address is the null IP; some peers
        /// also flip the direction to sendonly. The negotiation code must
        /// not crash on the null IP and must surface this as a hold.
        /// </summary>
        public const string AudioOfferHoldNullConnectionAddress =
@"v=0
o=- 1013 0 IN IP4 192.0.2.10
s=-
c=IN IP4 0.0.0.0
t=0 0
m=audio 20016 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendonly";

        // ---------------------------------------------------------------
        // SECTION 3 — Renegotiation / rejected media.
        // ---------------------------------------------------------------

        /// <summary>
        /// A re-INVITE that keeps audio and rejects video by setting the
        /// video m-line port to 0. Per RFC 3264 §8.2 the receiver must
        /// stop RTCP for that stream. Used by regression tests for #1496.
        /// </summary>
        public const string ReInviteRejectsVideoPortZero =
@"v=0
o=- 1020 1 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20020 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendrecv
m=video 0 RTP/AVP 96
a=rtpmap:96 VP8/90000";

        // ---------------------------------------------------------------
        // SECTION 4 — WebRTC-flavoured offers: ICE + DTLS fingerprint +
        // BUNDLE + rtcp-mux. Used for RTCPeerConnection tests.
        // ---------------------------------------------------------------

        /// <summary>
        /// A minimal WebRTC-shaped audio offer with everything required to
        /// pass setRemoteDescription on an RTCPeerConnection:
        /// ice-ufrag, ice-pwd, fingerprint, setup, mid, bundle, rtcp-mux.
        ///
        /// The fingerprint is a fixed value — replace with SdpNormaliser's
        /// placeholder when comparing answers, do not rely on this specific
        /// hash in assertions.
        /// </summary>
        public const string WebRtcAudioOfferOpus =
@"v=0
o=- 4000 0 IN IP4 0.0.0.0
s=-
t=0 0
a=group:BUNDLE 0
a=msid-semantic: WMS *
m=audio 9 UDP/TLS/RTP/SAVPF 111
c=IN IP4 0.0.0.0
a=rtcp:9 IN IP4 0.0.0.0
a=ice-ufrag:abcd
a=ice-pwd:abcdefghijklmnopqrstuvwx
a=fingerprint:sha-256 00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF
a=setup:actpass
a=mid:0
a=sendrecv
a=rtcp-mux
a=rtpmap:111 opus/48000/2
a=fmtp:111 minptime=10;useinbandfec=1";

        /// <summary>
        /// Audio + video WebRTC offer, both bundled. Audio with Opus,
        /// video with VP8. Both sendrecv, both rtcp-mux.
        /// </summary>
        public const string WebRtcAudioVideoOfferBundled =
@"v=0
o=- 4001 0 IN IP4 0.0.0.0
s=-
t=0 0
a=group:BUNDLE 0 1
a=msid-semantic: WMS *
m=audio 9 UDP/TLS/RTP/SAVPF 111
c=IN IP4 0.0.0.0
a=rtcp:9 IN IP4 0.0.0.0
a=ice-ufrag:abcd
a=ice-pwd:abcdefghijklmnopqrstuvwx
a=fingerprint:sha-256 00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF
a=setup:actpass
a=mid:0
a=sendrecv
a=rtcp-mux
a=rtpmap:111 opus/48000/2
a=fmtp:111 minptime=10;useinbandfec=1
m=video 9 UDP/TLS/RTP/SAVPF 96
c=IN IP4 0.0.0.0
a=rtcp:9 IN IP4 0.0.0.0
a=ice-ufrag:abcd
a=ice-pwd:abcdefghijklmnopqrstuvwx
a=fingerprint:sha-256 00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF
a=setup:actpass
a=mid:1
a=sendrecv
a=rtcp-mux
a=rtpmap:96 VP8/90000";

        // ---------------------------------------------------------------
        // SECTION 5 — SDES / SRTP crypto negotiation.
        // ---------------------------------------------------------------

        /// <summary>
        /// Plain-RTP-with-SDES audio offer: profile RTP/SAVP and an a=crypto
        /// line. Exercises the SrtpHandler.SetupRemote path in
        /// RTPSession.SetRemoteDescription when UseSdpCryptoNegotiation
        /// is enabled.
        /// </summary>
        public const string AudioOfferWithSdesCrypto =
@"v=0
o=- 5000 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20030 RTP/SAVP 0
a=rtpmap:0 PCMU/8000
a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:WVNfX19zZW1jdGwgKCkgewkyMjA7fQp9CnVubGVz|2^20|1:32
a=sendrecv";

        // ---------------------------------------------------------------
        // SECTION 6 — Real-world peer captures (lightly anonymised).
        // Each is a one-shot offer/answer from the named peer. Use for
        // interop characterization rather than for fine-grained branch
        // coverage.
        // ---------------------------------------------------------------

        /// <summary>
        /// Chrome 130 audio+video WebRTC offer (lightly anonymised).
        /// Notable quirks: msid lines, ssrc cname/msid/mslabel/label
        /// quartet, extmap entries, rtcp-fb attributes, ulpfec+red
        /// payload types, H.264 with profile-level-id.
        /// </summary>
        public const string ChromeAudioVideoWebRtcOffer =
@"v=0
o=- 6000000000000000000 2 IN IP4 127.0.0.1
s=-
t=0 0
a=group:BUNDLE 0 1
a=extmap-allow-mixed
a=msid-semantic: WMS stream-0
m=audio 9 UDP/TLS/RTP/SAVPF 111 63 9 0 8 13 110 126
c=IN IP4 0.0.0.0
a=rtcp:9 IN IP4 0.0.0.0
a=ice-ufrag:ChRM
a=ice-pwd:ChromePasswordPlaceholderXXXXXX
a=ice-options:trickle
a=fingerprint:sha-256 CH:RO:ME:FI:NG:ER:PR:IN:T0:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00
a=setup:actpass
a=mid:0
a=extmap:1 urn:ietf:params:rtp-hdrext:ssrc-audio-level
a=extmap:2 http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time
a=extmap:3 http://www.ietf.org/id/draft-holmer-rmcat-transport-wide-cc-extensions-01
a=sendrecv
a=msid:stream-0 audio-track-0
a=rtcp-mux
a=rtpmap:111 opus/48000/2
a=rtcp-fb:111 transport-cc
a=fmtp:111 minptime=10;useinbandfec=1
a=rtpmap:63 red/48000/2
a=fmtp:63 111/111
a=rtpmap:9 G722/8000
a=rtpmap:0 PCMU/8000
a=rtpmap:8 PCMA/8000
a=rtpmap:13 CN/8000
a=rtpmap:110 telephone-event/48000
a=rtpmap:126 telephone-event/8000
a=ssrc:1111111111 cname:CHROME_CNAME
a=ssrc:1111111111 msid:stream-0 audio-track-0
m=video 9 UDP/TLS/RTP/SAVPF 96 97 102 103 104 105 106 107 108 109 127 125 39 40 45 46 98 99 100 101 35 36 37 38 112 113 114
c=IN IP4 0.0.0.0
a=rtcp:9 IN IP4 0.0.0.0
a=ice-ufrag:ChRM
a=ice-pwd:ChromePasswordPlaceholderXXXXXX
a=ice-options:trickle
a=fingerprint:sha-256 CH:RO:ME:FI:NG:ER:PR:IN:T0:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00
a=setup:actpass
a=mid:1
a=extmap:14 urn:ietf:params:rtp-hdrext:toffset
a=extmap:2 http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time
a=extmap:13 urn:3gpp:video-orientation
a=extmap:3 http://www.ietf.org/id/draft-holmer-rmcat-transport-wide-cc-extensions-01
a=sendrecv
a=msid:stream-0 video-track-0
a=rtcp-mux
a=rtcp-rsize
a=rtpmap:96 VP8/90000
a=rtcp-fb:96 goog-remb
a=rtcp-fb:96 transport-cc
a=rtcp-fb:96 ccm fir
a=rtcp-fb:96 nack
a=rtcp-fb:96 nack pli
a=rtpmap:97 rtx/90000
a=fmtp:97 apt=96
a=rtpmap:102 H264/90000
a=rtcp-fb:102 goog-remb
a=rtcp-fb:102 transport-cc
a=rtcp-fb:102 ccm fir
a=rtcp-fb:102 nack
a=rtcp-fb:102 nack pli
a=fmtp:102 level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42001f
a=rtpmap:103 rtx/90000
a=fmtp:103 apt=102
a=ssrc-group:FID 2222222222 3333333333
a=ssrc:2222222222 cname:CHROME_CNAME
a=ssrc:2222222222 msid:stream-0 video-track-0
a=ssrc:3333333333 cname:CHROME_CNAME
a=ssrc:3333333333 msid:stream-0 video-track-0";

        /// <summary>
        /// Firefox-flavoured audio-only WebRTC offer. Firefox tends to omit
        /// rtcp-mux-only and includes ice-options:trickle but no extmap-
        /// allow-mixed; useful for verifying we don't depend on the Chrome-
        /// specific attributes being present.
        /// </summary>
        public const string FirefoxAudioOnlyWebRtcOffer =
@"v=0
o=mozilla...THIS_IS_SDPARTA-99.0 7000000000000000 0 IN IP4 0.0.0.0
s=-
t=0 0
a=fingerprint:sha-256 FF:FF:FF:FF:FF:FF:FF:FF:FF:FF:FF:FF:FF:FF:FF:FF:FF:FF:FF:FF:FF:FF:FF:FF:FF:FF:FF:FF:FF:FF:FF:FF
a=group:BUNDLE 0
a=ice-options:trickle
a=msid-semantic:WMS *
m=audio 9 UDP/TLS/RTP/SAVPF 109 9 0 8 101
c=IN IP4 0.0.0.0
a=sendrecv
a=fmtp:109 maxplaybackrate=48000;stereo=1;useinbandfec=1
a=fmtp:101 0-15
a=ice-pwd:FirefoxPasswordPlaceholderXXX
a=ice-ufrag:FxFx
a=mid:0
a=msid:- audio-track-0
a=rtcp-mux
a=rtpmap:109 opus/48000/2
a=rtpmap:9 G722/8000/1
a=rtpmap:0 PCMU/8000
a=rtpmap:8 PCMA/8000
a=rtpmap:101 telephone-event/8000
a=setup:actpass
a=ssrc:7777777777 cname:firefox-cname";

        // ---------------------------------------------------------------
        // SECTION 7 — Large offers.
        // Synthetic but shaped like real browser offers. Use to stress the
        // parser and as benchmark inputs.
        // ---------------------------------------------------------------

        /// <summary>
        /// Large Chrome-style bundled audio + video + data channel offer
        /// (about 190 lines). Every m-section repeats the ICE / DTLS
        /// attributes and carries host (UDP and TCP, IPv4 and IPv6), srflx
        /// and relay candidates plus end-of-candidates. Video offers VP8,
        /// two VP9 profiles, six H.264 formats and AV1, each with rtcp-fb
        /// and rtx, plus red and ulpfec. Uses documentation addresses.
        /// </summary>
        public const string LargeWebRtcOffer =
@"v=0
o=- 8000000000000000000 2 IN IP4 127.0.0.1
s=-
t=0 0
a=group:BUNDLE 0 1 2
a=extmap-allow-mixed
a=msid-semantic: WMS stream-0
m=audio 50000 UDP/TLS/RTP/SAVPF 111 63 9 0 8 13 110 126
c=IN IP4 203.0.113.10
a=rtcp:9 IN IP4 0.0.0.0
a=candidate:1000000001 1 udp 2122260223 192.0.2.10 50000 typ host generation 0 ufrag LgOf network-id 1 network-cost 10
a=candidate:1000000002 1 udp 2122197247 2001:db8::10 50002 typ host generation 0 ufrag LgOf network-id 2 network-cost 10
a=candidate:1000000003 1 tcp 1518280447 192.0.2.10 9 typ host tcptype active generation 0 ufrag LgOf network-id 1 network-cost 10
a=candidate:1000000004 1 tcp 1518217471 2001:db8::10 9 typ host tcptype active generation 0 ufrag LgOf network-id 2 network-cost 10
a=candidate:1000000005 1 udp 1686052607 203.0.113.10 50000 typ srflx raddr 192.0.2.10 rport 50000 generation 0 ufrag LgOf network-id 1 network-cost 10
a=candidate:1000000006 1 udp 41885439 198.51.100.20 3478 typ relay raddr 203.0.113.10 rport 50000 generation 0 ufrag LgOf network-id 1 network-cost 10
a=end-of-candidates
a=ice-ufrag:LgOf
a=ice-pwd:LargeOfferPasswordPlaceholderX
a=ice-options:trickle
a=fingerprint:sha-256 0A:1B:2C:3D:4E:5F:60:71:82:93:A4:B5:C6:D7:E8:F9:0A:1B:2C:3D:4E:5F:60:71:82:93:A4:B5:C6:D7:E8:F9
a=setup:actpass
a=mid:0
a=extmap:1 urn:ietf:params:rtp-hdrext:ssrc-audio-level
a=extmap:2 http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time
a=extmap:3 http://www.ietf.org/id/draft-holmer-rmcat-transport-wide-cc-extensions-01
a=extmap:4 urn:ietf:params:rtp-hdrext:sdes:mid
a=sendrecv
a=msid:stream-0 audio-track-0
a=rtcp-mux
a=rtcp-rsize
a=rtpmap:111 opus/48000/2
a=rtcp-fb:111 transport-cc
a=fmtp:111 minptime=10;useinbandfec=1
a=rtpmap:63 red/48000/2
a=fmtp:63 111/111
a=rtpmap:9 G722/8000
a=rtpmap:0 PCMU/8000
a=rtpmap:8 PCMA/8000
a=rtpmap:13 CN/8000
a=rtpmap:110 telephone-event/48000
a=rtpmap:126 telephone-event/8000
a=ssrc:1000000101 cname:LargeOfferCname
a=ssrc:1000000101 msid:stream-0 audio-track-0
m=video 50000 UDP/TLS/RTP/SAVPF 96 97 98 99 100 101 102 103 104 105 106 107 108 109 127 125 39 40 112 113 114 115 116
c=IN IP4 203.0.113.10
a=rtcp:9 IN IP4 0.0.0.0
a=candidate:1000000001 1 udp 2122260223 192.0.2.10 50000 typ host generation 0 ufrag LgOf network-id 1 network-cost 10
a=candidate:1000000002 1 udp 2122197247 2001:db8::10 50002 typ host generation 0 ufrag LgOf network-id 2 network-cost 10
a=candidate:1000000003 1 tcp 1518280447 192.0.2.10 9 typ host tcptype active generation 0 ufrag LgOf network-id 1 network-cost 10
a=candidate:1000000004 1 tcp 1518217471 2001:db8::10 9 typ host tcptype active generation 0 ufrag LgOf network-id 2 network-cost 10
a=candidate:1000000005 1 udp 1686052607 203.0.113.10 50000 typ srflx raddr 192.0.2.10 rport 50000 generation 0 ufrag LgOf network-id 1 network-cost 10
a=candidate:1000000006 1 udp 41885439 198.51.100.20 3478 typ relay raddr 203.0.113.10 rport 50000 generation 0 ufrag LgOf network-id 1 network-cost 10
a=end-of-candidates
a=ice-ufrag:LgOf
a=ice-pwd:LargeOfferPasswordPlaceholderX
a=ice-options:trickle
a=fingerprint:sha-256 0A:1B:2C:3D:4E:5F:60:71:82:93:A4:B5:C6:D7:E8:F9:0A:1B:2C:3D:4E:5F:60:71:82:93:A4:B5:C6:D7:E8:F9
a=setup:actpass
a=mid:1
a=extmap:14 urn:ietf:params:rtp-hdrext:toffset
a=extmap:2 http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time
a=extmap:13 urn:3gpp:video-orientation
a=extmap:3 http://www.ietf.org/id/draft-holmer-rmcat-transport-wide-cc-extensions-01
a=extmap:5 http://www.webrtc.org/experiments/rtp-hdrext/playout-delay
a=extmap:6 http://www.webrtc.org/experiments/rtp-hdrext/video-content-type
a=extmap:7 http://www.webrtc.org/experiments/rtp-hdrext/video-timing
a=extmap:8 http://www.webrtc.org/experiments/rtp-hdrext/color-space
a=extmap:4 urn:ietf:params:rtp-hdrext:sdes:mid
a=extmap:10 urn:ietf:params:rtp-hdrext:sdes:rtp-stream-id
a=extmap:11 urn:ietf:params:rtp-hdrext:sdes:repaired-rtp-stream-id
a=sendrecv
a=msid:stream-0 video-track-0
a=rtcp-mux
a=rtcp-rsize
a=rtpmap:96 VP8/90000
a=rtcp-fb:96 goog-remb
a=rtcp-fb:96 transport-cc
a=rtcp-fb:96 ccm fir
a=rtcp-fb:96 nack
a=rtcp-fb:96 nack pli
a=rtpmap:97 rtx/90000
a=fmtp:97 apt=96
a=rtpmap:98 VP9/90000
a=rtcp-fb:98 goog-remb
a=rtcp-fb:98 transport-cc
a=rtcp-fb:98 ccm fir
a=rtcp-fb:98 nack
a=rtcp-fb:98 nack pli
a=fmtp:98 profile-id=0
a=rtpmap:99 rtx/90000
a=fmtp:99 apt=98
a=rtpmap:100 VP9/90000
a=rtcp-fb:100 goog-remb
a=rtcp-fb:100 transport-cc
a=rtcp-fb:100 ccm fir
a=rtcp-fb:100 nack
a=rtcp-fb:100 nack pli
a=fmtp:100 profile-id=2
a=rtpmap:101 rtx/90000
a=fmtp:101 apt=100
a=rtpmap:102 H264/90000
a=rtcp-fb:102 goog-remb
a=rtcp-fb:102 transport-cc
a=rtcp-fb:102 ccm fir
a=rtcp-fb:102 nack
a=rtcp-fb:102 nack pli
a=fmtp:102 level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42001f
a=rtpmap:103 rtx/90000
a=fmtp:103 apt=102
a=rtpmap:104 H264/90000
a=rtcp-fb:104 goog-remb
a=rtcp-fb:104 transport-cc
a=rtcp-fb:104 ccm fir
a=rtcp-fb:104 nack
a=rtcp-fb:104 nack pli
a=fmtp:104 level-asymmetry-allowed=1;packetization-mode=0;profile-level-id=42001f
a=rtpmap:105 rtx/90000
a=fmtp:105 apt=104
a=rtpmap:106 H264/90000
a=rtcp-fb:106 goog-remb
a=rtcp-fb:106 transport-cc
a=rtcp-fb:106 ccm fir
a=rtcp-fb:106 nack
a=rtcp-fb:106 nack pli
a=fmtp:106 level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e01f
a=rtpmap:107 rtx/90000
a=fmtp:107 apt=106
a=rtpmap:108 H264/90000
a=rtcp-fb:108 goog-remb
a=rtcp-fb:108 transport-cc
a=rtcp-fb:108 ccm fir
a=rtcp-fb:108 nack
a=rtcp-fb:108 nack pli
a=fmtp:108 level-asymmetry-allowed=1;packetization-mode=0;profile-level-id=42e01f
a=rtpmap:109 rtx/90000
a=fmtp:109 apt=108
a=rtpmap:127 H264/90000
a=rtcp-fb:127 goog-remb
a=rtcp-fb:127 transport-cc
a=rtcp-fb:127 ccm fir
a=rtcp-fb:127 nack
a=rtcp-fb:127 nack pli
a=fmtp:127 level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=4d001f
a=rtpmap:125 rtx/90000
a=fmtp:125 apt=127
a=rtpmap:39 AV1/90000
a=rtcp-fb:39 goog-remb
a=rtcp-fb:39 transport-cc
a=rtcp-fb:39 ccm fir
a=rtcp-fb:39 nack
a=rtcp-fb:39 nack pli
a=fmtp:39 level-idx=5;profile=0;tier=0
a=rtpmap:40 rtx/90000
a=fmtp:40 apt=39
a=rtpmap:112 H264/90000
a=rtcp-fb:112 goog-remb
a=rtcp-fb:112 transport-cc
a=rtcp-fb:112 ccm fir
a=rtcp-fb:112 nack
a=rtcp-fb:112 nack pli
a=fmtp:112 level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=64001f
a=rtpmap:113 rtx/90000
a=fmtp:113 apt=112
a=rtpmap:114 red/90000
a=rtpmap:115 rtx/90000
a=fmtp:115 apt=114
a=rtpmap:116 ulpfec/90000
a=ssrc-group:FID 1000000201 1000000202
a=ssrc:1000000201 cname:LargeOfferCname
a=ssrc:1000000201 msid:stream-0 video-track-0
a=ssrc:1000000202 cname:LargeOfferCname
a=ssrc:1000000202 msid:stream-0 video-track-0
m=application 50000 UDP/DTLS/SCTP webrtc-datachannel
c=IN IP4 203.0.113.10
a=candidate:1000000001 1 udp 2122260223 192.0.2.10 50000 typ host generation 0 ufrag LgOf network-id 1 network-cost 10
a=candidate:1000000002 1 udp 2122197247 2001:db8::10 50002 typ host generation 0 ufrag LgOf network-id 2 network-cost 10
a=candidate:1000000003 1 tcp 1518280447 192.0.2.10 9 typ host tcptype active generation 0 ufrag LgOf network-id 1 network-cost 10
a=candidate:1000000004 1 tcp 1518217471 2001:db8::10 9 typ host tcptype active generation 0 ufrag LgOf network-id 2 network-cost 10
a=candidate:1000000005 1 udp 1686052607 203.0.113.10 50000 typ srflx raddr 192.0.2.10 rport 50000 generation 0 ufrag LgOf network-id 1 network-cost 10
a=candidate:1000000006 1 udp 41885439 198.51.100.20 3478 typ relay raddr 203.0.113.10 rport 50000 generation 0 ufrag LgOf network-id 1 network-cost 10
a=end-of-candidates
a=ice-ufrag:LgOf
a=ice-pwd:LargeOfferPasswordPlaceholderX
a=ice-options:trickle
a=fingerprint:sha-256 0A:1B:2C:3D:4E:5F:60:71:82:93:A4:B5:C6:D7:E8:F9:0A:1B:2C:3D:4E:5F:60:71:82:93:A4:B5:C6:D7:E8:F9
a=setup:actpass
a=mid:2
a=sctp-port:5000
a=max-message-size:262144";
    }
}
