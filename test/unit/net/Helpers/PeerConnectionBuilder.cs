//-----------------------------------------------------------------------------
// Filename: PeerConnectionBuilder.cs
//
// Description: Fluent builders that remove the boilerplate of constructing
// RTCPeerConnection / RTPSession instances populated with the right mix of
// audio / video / text tracks for SDP-negotiation tests. The builders
// deliberately do NOT cover every parameter — only the ones that the SDP
// negotiation code actually branches on, plus a couple of defaults that
// keep tests deterministic.
//
// Add new With* methods here when a test scenario needs a knob; do NOT
// create test-local builders that duplicate this shape.
//
// History:
// 20 May 2026	Claude Code - Opus 4.7	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Net.UnitTests.Helpers
{
    /// <summary>
    /// Fluent builder for <see cref="RTCPeerConnection"/> instances used in
    /// SDP-negotiation tests. Defaults match a typical "browser-shaped" peer:
    /// WebRTC profile, bundle, rtcp-mux, DTLS. Tracks added in declared order.
    /// </summary>
    public sealed class PeerConnectionBuilder
    {
        private RTCConfiguration _configuration;
        private int _bindPort;
        private bool _videoAsPrimary;
        private readonly List<MediaStreamTrack> _tracks = new List<MediaStreamTrack>();

        public PeerConnectionBuilder WithConfiguration(RTCConfiguration configuration)
        {
            _configuration = configuration;
            return this;
        }

        public PeerConnectionBuilder WithBindPort(int port)
        {
            _bindPort = port;
            return this;
        }

        public PeerConnectionBuilder WithVideoAsPrimary(bool value = true)
        {
            _videoAsPrimary = value;
            return this;
        }

        public PeerConnectionBuilder WithTrack(MediaStreamTrack track)
        {
            _tracks.Add(track);
            return this;
        }

        /// <summary>
        /// Adds an audio track with the named well-known codec (PCMU/PCMA/G722/etc.)
        /// in the given direction. Convenience over hand-rolling a
        /// MediaStreamTrack.
        /// </summary>
        public PeerConnectionBuilder WithAudioTrack(
            SDPWellKnownMediaFormatsEnum codec = SDPWellKnownMediaFormatsEnum.PCMU,
            MediaStreamStatusEnum direction = MediaStreamStatusEnum.SendRecv)
        {
            var track = new MediaStreamTrack(
                SDPMediaTypesEnum.audio,
                false,
                new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(codec) },
                direction);
            _tracks.Add(track);
            return this;
        }

        /// <summary>
        /// Adds a video track with a single dynamic codec.
        /// </summary>
        public PeerConnectionBuilder WithVideoTrack(
            int payloadId = 96,
            string codecName = "VP8",
            int clockRate = 90000,
            MediaStreamStatusEnum direction = MediaStreamStatusEnum.SendRecv)
        {
            var track = new MediaStreamTrack(
                SDPMediaTypesEnum.video,
                false,
                new List<SDPAudioVideoMediaFormat> {
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, payloadId, codecName, clockRate)
                },
                direction);
            _tracks.Add(track);
            return this;
        }

        public RTCPeerConnection Build()
        {
            RTCPeerConnection pc = new RTCPeerConnection(_configuration, _bindPort, null, _videoAsPrimary);
            foreach (MediaStreamTrack t in _tracks)
            {
                pc.addTrack(t);
            }
            return pc;
        }
    }

    /// <summary>
    /// Fluent builder for plain <see cref="RTPSession"/> instances. Used for
    /// the non-WebRTC SDP-negotiation tests (vanilla SIP RTP, optional SDES).
    /// </summary>
    public sealed class RtpSessionBuilder
    {
        private bool _isMediaMultiplexed;
        private bool _isRtcpMultiplexed;
        private RtpSecureMediaOptionEnum _secureMediaOption = RtpSecureMediaOptionEnum.None;
        private IPAddress _bindAddress;
        private int _bindPort;
        private readonly List<MediaStreamTrack> _tracks = new List<MediaStreamTrack>();

        public RtpSessionBuilder WithMediaMultiplexed(bool value = true)
        {
            _isMediaMultiplexed = value;
            return this;
        }

        public RtpSessionBuilder WithRtcpMultiplexed(bool value = true)
        {
            _isRtcpMultiplexed = value;
            return this;
        }

        public RtpSessionBuilder WithSecure(bool value = true)
        {
            _secureMediaOption = value ? RtpSecureMediaOptionEnum.DtlsSrtp : RtpSecureMediaOptionEnum.None;
            return this;
        }

        /// <summary>
        /// Enables SDES (a=crypto)-based SRTP negotiation. The simple
        /// "secure" toggle only covers DTLS-SRTP; this is the other branch
        /// of the negotiation code in <c>RTPSession.SetRemoteDescription</c>.
        /// </summary>
        public RtpSessionBuilder WithSdpCryptoNegotiation(bool value = true)
        {
            _secureMediaOption = value ? RtpSecureMediaOptionEnum.SdpCryptoNegotiation : RtpSecureMediaOptionEnum.None;
            return this;
        }

        public RtpSessionBuilder WithBindAddress(IPAddress address)
        {
            _bindAddress = address;
            return this;
        }

        public RtpSessionBuilder WithBindPort(int port)
        {
            _bindPort = port;
            return this;
        }

        public RtpSessionBuilder WithTrack(MediaStreamTrack track)
        {
            _tracks.Add(track);
            return this;
        }

        public RtpSessionBuilder WithAudioTrack(
            SDPWellKnownMediaFormatsEnum codec = SDPWellKnownMediaFormatsEnum.PCMU,
            MediaStreamStatusEnum direction = MediaStreamStatusEnum.SendRecv)
        {
            var track = new MediaStreamTrack(
                SDPMediaTypesEnum.audio,
                false,
                new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(codec) },
                direction);
            _tracks.Add(track);
            return this;
        }

        public RtpSessionBuilder WithVideoTrack(
            int payloadId = 96,
            string codecName = "VP8",
            int clockRate = 90000,
            MediaStreamStatusEnum direction = MediaStreamStatusEnum.SendRecv)
        {
            var track = new MediaStreamTrack(
                SDPMediaTypesEnum.video,
                false,
                new List<SDPAudioVideoMediaFormat> {
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, payloadId, codecName, clockRate)
                },
                direction);
            _tracks.Add(track);
            return this;
        }

        public RTPSession Build()
        {
            RtpSessionConfig config = new RtpSessionConfig
            {
                IsMediaMultiplexed = _isMediaMultiplexed,
                IsRtcpMultiplexed = _isRtcpMultiplexed,
                RtpSecureMediaOption = _secureMediaOption,
                BindAddress = _bindAddress,
                BindPort = _bindPort,
            };
            RTPSession session = new RTPSession(config);
            foreach (MediaStreamTrack t in _tracks)
            {
                session.addTrack(t);
            }
            return session;
        }
    }
}
