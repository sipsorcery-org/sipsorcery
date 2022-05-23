﻿//-----------------------------------------------------------------------------
// Filename: RTPSession.cs
//
// Description: Represents an RTP session constituted of a single media stream. The session
// does not control the sockets as they may be shared by multiple sessions.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 25 Aug 2019	Aaron Clauson	    Created, Montreux, Switzerland.
// 12 Nov 2019  Aaron Clauson       Added send event method.
// 07 Dec 2019  Aaron Clauson       Big refactor. Brought in a lot of functions previously
//                                  in the RTPChannel class.
// 26 Jul 2021  Kurt Kießling       Added secure media negotiation.
// 05 Apr 2022  Christophe Irles    Simplify file using MediaSteam, AudioStream and Video Stream
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.net.RTP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Net
{
    public delegate int ProtectRtpPacket(byte[] payload, int length, out int outputBufferLength);

    public enum SetDescriptionResultEnum
    {
        /// <summary>
        /// At least one media stream with a compatible format was available.
        /// </summary>
        OK,

        /// <summary>
        /// Both parties had audio but no compatible format was available.
        /// </summary>
        AudioIncompatible,

        /// <summary>
        /// Both parties had video but no compatible format was available.
        /// </summary>
        VideoIncompatible,

        /// <summary>
        /// No media tracks are available on the local session.
        /// </summary>
        NoLocalMedia,

        /// <summary>
        /// The remote description did not contain any media announcements.
        /// </summary>
        NoRemoteMedia,

        /// <summary>
        /// Indicates there was no media type match. For example only have audio locally
        /// but video remote or vice-versa.
        /// </summary>
        NoMatchingMediaType,

        /// <summary>
        /// An unknown error.
        /// </summary>
        Error,

        /// <summary>
        /// A required DTLS fingerprint was missing from the session description.
        /// </summary>
        DtlsFingerprintMissing,

        /// <summary>
        /// The DTLS fingerprint was present but the format was not recognised.
        /// </summary>
        DtlsFingerprintInvalid,

        /// <summary>
        /// The DTLS fingerprint was provided with an unsupported digest. It won't
        /// be possible to check that the certificate supplied during the DTLS handshake
        /// matched the fingerprint.
        /// </summary>
        DtlsFingerprintDigestNotSupported,

        /// <summary>
        /// An unsupported data channel transport was requested (at the time of writing only
        /// SCTP over DTLS is supported, no TCP option).
        /// </summary>
        DataChannelTransportNotSupported,

        /// <summary>
        /// An SDP offer was received when the local agent had already entered have local offer state.
        /// </summary>
        WrongSdpTypeOfferAfterOffer,

        /// <summary>
        /// Crypto attributes invalid or not compatible.
        /// </summary>
        CryptoNegotiationFailed,
    }

    /// <summary>
    /// The RTPSession class is the primary point for interacting with the Real-Time
    /// Protocol. It manages all the resources required for setting up and then sending
    /// and receiving RTP packets. This class IS designed to be inherited by child 
    /// classes and for child classes to add audio and video processing logic.
    /// </summary>
    /// <remarks>
    /// The setting up of an RTP stream involved the exchange of Session Descriptions 
    /// (SDP) with the remote party. This class has adopted the mechanism used by WebRTC.
    /// The steps are:
    /// 1. If acting as the initiator:
    ///   a. Create offer,
    ///   b. Send offer to remote party and get their answer (external to this class, requires signalling),
    ///   c. Set remote description,
    ///   d. Optionally perform any additional set up, such as negotiating SRTP keying material,
    ///   e. Call Start to commence RTCP reporting.
    /// 2. If acting as the recipient:
    ///   a. Receive offer,
    ///   b. Set remote description. This step MUST be done before an SDP answer can be generated.
    ///      This step can also result in an error condition if the codecs/formats offered aren't supported,
    ///   c. Create answer,
    ///   d. Send answer to remote party (external to this class, requires signalling),
    ///   e. Optionally perform any additional set up, such as negotiating SRTP keying material,
    ///   f. Call Start to commence RTCP reporting.
    /// </remarks>
    public class RTPSession : IMediaSession, IDisposable
    {
        internal const int RTP_MAX_PAYLOAD = 1400;

        /// <summary>
        /// From libsrtp: SRTP_MAX_TRAILER_LEN is the maximum length of the SRTP trailer
        /// (authentication tag and MKI) supported by libSRTP.This value is
        /// the maximum number of octets that will be added to an RTP packet by
        /// srtp_protect().
        /// 
        /// srtp_protect():
        /// @warning This function assumes that it can write SRTP_MAX_TRAILER_LEN
        /// into the location in memory immediately following the RTP packet.
        /// Callers MUST ensure that this much writeable memory is available in
        /// the buffer that holds the RTP packet.
        /// 
        /// srtp_protect_rtcp():
        /// @warning This function assumes that it can write SRTP_MAX_TRAILER_LEN+4
        /// to the location in memory immediately following the RTCP packet.
        /// Callers MUST ensure that this much writeable memory is available in
        /// the buffer that holds the RTCP packet.
        /// </summary>
        public const int SRTP_MAX_PREFIX_LENGTH = 148;
        internal const int DEFAULT_AUDIO_CLOCK_RATE = 8000;
        public const int RTP_EVENT_DEFAULT_SAMPLE_PERIOD_MS = 50; // Default sample period for an RTP event as specified by RFC2833.
        public const SDPMediaTypesEnum DEFAULT_MEDIA_TYPE = SDPMediaTypesEnum.audio; // If we can't match an RTP payload ID assume it's audio.
        public const int DEFAULT_DTMF_EVENT_PAYLOAD_ID = 101;
        public const string RTP_MEDIA_PROFILE = "RTP/AVP";
        public const string RTP_SECUREMEDIA_PROFILE = "RTP/SAVP";
        private const int SDP_SESSIONID_LENGTH = 10;             // The length of the pseudo-random string to use for the session ID.
        public const int DTMF_EVENT_DURATION = 1200;            // Default duration for a DTMF event.
        public const int DTMF_EVENT_PAYLOAD_ID = 101;

        /// <summary>
        /// When there are no RTP packets being sent for an audio or video stream webrtc.lib
        /// still sends RTCP Receiver Reports with this hard coded SSRC. No doubt it's defined
        /// in an RFC somewhere but I wasn't able to find it from a quick search.
        /// </summary>
        public const uint RTCP_RR_NOSTREAM_SSRC = 4195875351U;

        private static ILogger logger = Log.Logger;

        protected RtpSessionConfig rtpSessionConfig;

        private string m_sdpSessionID = null;           // Need to maintain the same SDP session ID for all offers and answers.
        private int m_sdpAnnouncementVersion = 0;       // The SDP version needs to increase whenever the local SDP is modified (see https://tools.ietf.org/html/rfc6337#section-5.2.5).
        internal int m_rtpChannelsCount = 0;            // Need to know the number of RTP Channels

        protected RTPChannel MultiplexRtpChannel = null;

        /// <summary>
        /// Track if current remote description is invalid (used in Renegotiation logic)
        /// </summary>
        public virtual bool RequireRenegotiation { get; protected internal set; }

        /// <summary>
        /// The Audio Stream for this session
        /// </summary>
        public AudioStream AudioStream { get; set; }

        /// <summary>
        /// The Video Stream for this session
        /// </summary>
        public VideoStream VideoStream { get; set; }

        /// <summary>
        /// The local audio stream for this session. Will be null if we are not sending audio.
        /// </summary>
        public MediaStreamTrack AudioLocalTrack => AudioStream.LocalTrack;

        /// <summary>
        /// The remote audio track for this session. Will be null if the remote party is not sending audio.
        /// </summary>
        public MediaStreamTrack AudioRemoteTrack => AudioStream.RemoteTrack;

        /// <summary>
        /// The reporting session for the audio stream. Will be null if only video is being sent.
        /// </summary>
        public RTCPSession AudioRtcpSession => AudioStream.RtcpSession;

        /// <summary>
        /// The primary Audio remote RTP end point this stream is sending media to.
        /// </summary>
        public IPEndPoint AudioDestinationEndPoint => AudioStream.DestinationEndPoint;

        /// <summary>
        /// The primary Audio remote RTP control end point this stream is sending to RTCP reports for the media stream to.
        /// </summary>
        public IPEndPoint AudioControlDestinationEndPoint => AudioStream.ControlDestinationEndPoint;

        /// <summary>
        /// The local video track for this session. Will be null if we are not sending video.
        /// </summary>
        public MediaStreamTrack VideoLocalTrack => VideoStream.LocalTrack;

        /// <summary>
        /// The remote video track for this session. Will be null if the remote party is not sending video.
        /// </summary>
        public MediaStreamTrack VideoRemoteTrack => VideoStream.RemoteTrack;

        /// <summary>
        /// The reporting session for the video stream. Will be null if only audio is being sent.
        /// </summary>
        public RTCPSession VideoRtcpSession => VideoStream.RtcpSession;

        /// <summary>
        /// The primary Video remote RTP end point this stream is sending media to.
        /// </summary>
        public IPEndPoint VideoDestinationEndPoint => VideoStream?.DestinationEndPoint;

        /// <summary>
        /// The primary Video remote RTP control end point this stream is sending to RTCP reports for the media stream to.
        /// </summary>
        public IPEndPoint VideoControlDestinationEndPoint => VideoStream?.ControlDestinationEndPoint;

        /// <summary>
        /// The SDP offered by the remote call party for this session.
        /// </summary>
        public SDP RemoteDescription { get; protected set; }

        /// <summary>
        /// If this session is using a secure context this flag MUST be set to indicate
        /// the security delegate (SrtpProtect, SrtpUnprotect etc) have been set.
        /// </summary>
        public bool IsSecureContextReady()
        {
            if (HasAudio && !AudioStream.IsSecurityContextReady())
            {
                return false;
            }

            if (HasVideo && !VideoStream.IsSecurityContextReady())
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// If this session is using a secure context this list MAY contain custom
        /// Crypto Suites
        /// </summary>
        public List<SDPSecurityDescription.CryptoSuites> SrtpCryptoSuites { get; set; }

        /// <summary>
        /// Indicates the maximum frame size that can be reconstructed from RTP packets during the depacketisation
        /// process.
        /// </summary>
        public int MaxReconstructedVideoFrameSize { get => VideoStream.MaxReconstructedVideoFrameSize; set => VideoStream.MaxReconstructedVideoFrameSize = value; }

        /// <summary>
        /// Indicates whether the session has been closed. Once a session is closed it cannot
        /// be restarted.
        /// </summary>
        public bool IsClosed { get; private set; }

        /// <summary>
        /// Indicates whether the session has been started. Starting a session tells the RTP 
        /// socket to start receiving,
        /// </summary>
        public bool IsStarted { get; private set; }

        /// <summary>
        /// Indicates whether this session is using audio.
        /// </summary>
        public bool HasAudio
        {
            get
            {
                return AudioStream?.HasAudio == true;
            }
        }

        /// <summary>
        /// Indicates whether this session is using video.
        /// </summary>
        public bool HasVideo
        {
            get
            {
                return VideoStream?.HasVideo == true;
            }
        }

        /// <summary>
        /// If set to true RTP will be accepted from ANY remote end point. If false
        /// certain rules are used to determine whether RTP should be accepted for 
        /// a particular audio or video stream. It is recommended to leave the
        /// value to false unless a specific need exists.
        /// </summary>
        public bool AcceptRtpFromAny 
        { 
            get
            {
                return AudioStream.AcceptRtpFromAny;
            }

            set
            {
                AudioStream.AcceptRtpFromAny = value;
                VideoStream.AcceptRtpFromAny = value;
            }
        }

        /// <summary>
        /// Set if the session has been bound to a specific IP address.
        /// Normally not required but some esoteric call or network set ups may need.
        /// </summary>
        public IPAddress RtpBindAddress => rtpSessionConfig.BindAddress;

        /// <summary>
        /// Gets fired when the remote SDP is received and the set of common audio formats is set.
        /// </summary>
        public event Action<List<AudioFormat>> OnAudioFormatsNegotiated;

        /// <summary>
        /// Gets fired when the remote SDP is received and the set of common video formats is set.
        /// </summary>
        public event Action<List<VideoFormat>> OnVideoFormatsNegotiated;

        /// <summary>
        /// Gets fired when a full video frame is reconstructed from one or more RTP packets
        /// received from the remote party.
        /// </summary>
        /// <remarks>
        ///  - Received from end point,
        ///  - The frame timestamp,
        ///  - The encoded video frame payload.
        ///  - The video format of the encoded frame.
        /// </remarks>
        public event Action<IPEndPoint, uint, byte[], VideoFormat> OnVideoFrameReceived;

        /// <summary>
        /// Gets fired when an RTP packet is received from a remote party.
        /// Parameters are:
        ///  - Remote endpoint packet was received from,
        ///  - The media type the packet contains, will be audio or video,
        ///  - The full RTP packet.
        /// </summary>
        public event Action<IPEndPoint, SDPMediaTypesEnum, RTPPacket> OnRtpPacketReceived;

        /// <summary>
        /// Gets fired when an RTP event is detected on the remote call party's RTP stream.
        /// </summary>
        public event Action<IPEndPoint, RTPEvent, RTPHeader> OnRtpEvent;

        /// <summary>
        /// Gets fired when the RTP session and underlying channel are closed.
        /// </summary>
        public event Action<string> OnRtpClosed;

        /// <summary>
        /// Gets fired when an RTCP BYE packet is received from the remote party.
        /// The string parameter contains the BYE reason. Normally a BYE
        /// report means the RTP session is finished. But... cases have been observed where
        /// an RTCP BYE is received when a remote party is put on hold and then the session
        /// resumes when take off hold. It's up to the application to decide what action to
        /// take when n RTCP BYE is received.
        /// </summary>
        public event Action<string> OnRtcpBye;

        /// <summary>
        /// Fires when the connection for a media type is classified as timed out due to not
        /// receiving any RTP or RTCP packets within the given period.
        /// </summary>
        public event Action<SDPMediaTypesEnum> OnTimeout;

        /// <summary>
        /// Gets fired when an RTCP report is received. This event is for diagnostics only.
        /// </summary>
        public event Action<IPEndPoint, SDPMediaTypesEnum, RTCPCompoundPacket> OnReceiveReport;

        /// <summary>
        /// Gets fired when an RTCP report is sent. This event is for diagnostics only.
        /// </summary>
        public event Action<SDPMediaTypesEnum, RTCPCompoundPacket> OnSendReport;

        /// <summary>
        /// Gets fired when the start method is called on the session. This is the point
        /// audio and video sources should commence generating samples.
        /// </summary>
        public event Action OnStarted;

        /// <summary>
        /// Gets fired when the session is closed. This is the point audio and video
        /// source should stop generating samples.
        /// </summary>
        public event Action OnClosed;

        /// <summary>
        /// Creates a new RTP session. The synchronisation source and sequence number are initialised to
        /// pseudo random values.
        /// </summary>
        /// <param name="isRtcpMultiplexed">If true RTCP reports will be multiplexed with RTP on a single channel.
        /// If false (standard mode) then a separate socket is used to send and receive RTCP reports.</param>
        /// <param name="isSecure">If true indicated this session is using SRTP to encrypt and authorise
        /// RTP and RTCP packets. No communications or reporting will commence until the 
        /// is explicitly set as complete.</param>
        /// <param name="isMediaMultiplexed">If true only a single RTP socket will be used for both audio
        /// and video (standard case for WebRTC). If false two separate RTP sockets will be used for
        /// audio and video (standard case for VoIP).</param>
        /// <param name="bindAddress">Optional. If specified this address will be used as the bind address for any RTP
        /// and control sockets created. Generally this address does not need to be set. The default behaviour
        /// is to bind to [::] or 0.0.0.0,d depending on system support, which minimises network routing
        /// causing connection issues.</param>
        /// <param name="bindPort">Optional. If specified a single attempt will be made to bind the RTP socket
        /// on this port. It's recommended to leave this parameter as the default of 0 to let the Operating
        /// System select the port number.</param>
        public RTPSession(bool isMediaMultiplexed, bool isRtcpMultiplexed, bool isSecure, IPAddress bindAddress = null, int bindPort = 0, PortRange portRange = null)
            : this(new RtpSessionConfig
            {
                IsMediaMultiplexed = isMediaMultiplexed,
                IsRtcpMultiplexed = isRtcpMultiplexed,
                RtpSecureMediaOption = isSecure ? RtpSecureMediaOptionEnum.DtlsSrtp : RtpSecureMediaOptionEnum.None,
                BindAddress = bindAddress,
                BindPort = bindPort,
                RtpPortRange = portRange
            })
        {
        }

        /// <summary>
        /// Creates a new RTP session. The synchronisation source and sequence number are initialised to
        /// pseudo random values.
        /// </summary>
        /// <param name="config">Contains required settings.</param>
        public RTPSession(RtpSessionConfig config)
        {
            rtpSessionConfig = config;
            m_sdpSessionID = Crypto.GetRandomInt(SDP_SESSIONID_LENGTH).ToString();

            if (rtpSessionConfig.UseSdpCryptoNegotiation)
            {
                SrtpCryptoSuites = new List<SDPSecurityDescription.CryptoSuites>();
                SrtpCryptoSuites.Add(SDPSecurityDescription.CryptoSuites.AES_CM_128_HMAC_SHA1_80);
                SrtpCryptoSuites.Add(SDPSecurityDescription.CryptoSuites.AES_CM_128_HMAC_SHA1_32);
            }

            AudioStream = new AudioStream(config);
            VideoStream = new VideoStream(config);
        }

        private void CreateRtcpSession(MediaStream mediaStream)
        {
            if (mediaStream.CreateRtcpSession())
            {
                mediaStream.OnTimeout += RaiseOnTimeOut;
                mediaStream.OnSendReport += RaiseOnSendReport;
                mediaStream.OnRtpEvent += RaisedOnRtpEvent;
                mediaStream.OnRtpPacketReceived += RaisedOnRtpPacketReceived;
                mediaStream.OnReceiveReport += RaisedOnOnReceiveReport;
                
                if(mediaStream.MediaType == SDPMediaTypesEnum.audio)
                {
                    var audioStream = mediaStream as AudioStream;
                    if (audioStream != null)
                    {
                        audioStream.OnAudioFormatsNegotiated += RaisedOnAudioFormatsNegotiated;
                    }
                }
                else
                {
                    var videoStream = mediaStream as VideoStream;
                    if (videoStream != null)
                    {
                        videoStream.OnVideoFormatsNegotiated += RaisedOnVideoFormatsNegotiated;
                        videoStream.OnVideoFrameReceived += RaisedOnOnVideoFrameReceived;
                    }
                }
            }
        }

        /// <summary>
        /// Sets the Secure RTP (SRTP) delegates for a specific media type
        /// </summary>
        /// <param name="mediaType">The media type to set the secure context for.</param>
        /// <param name="protectRtp">SRTP encrypt RTP packet delegate.</param>
        /// <param name="unprotectRtp">SRTP decrypt RTP packet delegate.</param>
        /// <param name="protectRtcp">SRTP encrypt RTCP packet delegate.</param>
        /// <param name="unprotectRtcp">SRTP decrypt RTCP packet delegate.</param>
        public void SetSecurityContext(SDPMediaTypesEnum mediaType, ProtectRtpPacket protectRtp, ProtectRtpPacket unprotectRtp, ProtectRtpPacket protectRtcp, ProtectRtpPacket unprotectRtcp)
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                AudioStream.SetSecurityContext(protectRtp,
                    unprotectRtp,
                    protectRtcp,
                    unprotectRtcp);
            }
            else if (mediaType == SDPMediaTypesEnum.video)
            {
                VideoStream.SetSecurityContext(protectRtp,
                    unprotectRtp,
                    protectRtcp,
                    unprotectRtcp);
            }
        }

        private void CloseRtcpSession(MediaStream mediaStream, string reason)
        {
            if (mediaStream.RtcpSession != null)
            {
                mediaStream.OnTimeout -= RaiseOnTimeOut;
                mediaStream.OnSendReport -= RaiseOnSendReport;
                mediaStream.OnRtpEvent -= RaisedOnRtpEvent;
                mediaStream.OnRtpPacketReceived -= RaisedOnRtpPacketReceived;
                mediaStream.OnReceiveReport -= RaisedOnOnReceiveReport;

                if (mediaStream.MediaType == SDPMediaTypesEnum.audio)
                {
                    var audioStream = mediaStream as AudioStream;
                    if (audioStream != null)
                    {
                        audioStream.OnAudioFormatsNegotiated -= RaisedOnAudioFormatsNegotiated;
                    }
                }
                else
                {
                    var videoStream = mediaStream as VideoStream;
                    if (videoStream != null)
                    {
                        videoStream.OnVideoFormatsNegotiated -= RaisedOnVideoFormatsNegotiated;
                        videoStream.OnVideoFrameReceived -= RaisedOnOnVideoFrameReceived;
                    }
                }

                mediaStream.RtcpSession.Close(reason);
                mediaStream.RtcpSession = null;
            }
        }

        private void RaiseOnTimeOut(SDPMediaTypesEnum media)
        {
            OnTimeout?.Invoke(media);
        }

        private void RaiseOnSendReport(SDPMediaTypesEnum media, RTCPCompoundPacket report)
        {
            OnSendReport?.Invoke(media, report);
        }

        private void RaisedOnRtpEvent(IPEndPoint ipEndPoint, RTPEvent rtpEvent, RTPHeader rtpHeader)
        {
            OnRtpEvent?.Invoke(ipEndPoint, rtpEvent, rtpHeader);
        }

        private void RaisedOnRtpPacketReceived(IPEndPoint ipEndPoint, SDPMediaTypesEnum media, RTPPacket rtpPacket)
        {
            OnRtpPacketReceived?.Invoke(ipEndPoint, media, rtpPacket);
        }

        private void RaisedOnOnReceiveReport(IPEndPoint ipEndPoint, SDPMediaTypesEnum media, RTCPCompoundPacket report)
        {
            OnReceiveReport?.Invoke(ipEndPoint, media, report);
        }

        private void RaisedOnAudioFormatsNegotiated(List<AudioFormat> audioFormats)
        {
            OnAudioFormatsNegotiated?.Invoke(audioFormats);
        }

        private void RaisedOnVideoFormatsNegotiated(List<VideoFormat> videoFormats)
        {
            OnVideoFormatsNegotiated?.Invoke(videoFormats);
        }

        private void RaisedOnOnVideoFrameReceived(IPEndPoint ipEndPoint, uint timestamp, byte[] frame, VideoFormat videoFormat)
        {
            OnVideoFrameReceived?.Invoke(ipEndPoint, timestamp, frame, videoFormat);
        }

        private SrtpHandler GetOrCreateSrtpHandler(SDPMediaTypesEnum mediaType)
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                return AudioStream.GetOrCreateSrtpHandler();
            }
            else if (mediaType == SDPMediaTypesEnum.video)
            {
                return VideoStream.GetOrCreateSrtpHandler();
            }
            return null;
        }

        /// <summary>
        /// Generates the SDP for an offer that can be made to a remote user agent.
        /// </summary>
        /// <param name="connectionAddress">Optional. If specified this IP address
        /// will be used as the address advertised in the SDP offer. If not provided
        /// the kernel routing table will be used to determine the local IP address used
        /// for Internet access. Any and IPv6Any are special cases. If they are set the respective
        /// Internet facing IPv4 or IPv6 address will be used.</param>
        /// <returns>A task that when complete contains the SDP offer.</returns>
        public virtual SDP CreateOffer(IPAddress connectionAddress)
        {
            if (AudioStream.LocalTrack == null && VideoStream.LocalTrack == null)
            {
                logger.LogWarning("No local media tracks available for create offer.");
                return null;
            }
            else
            {
                List<MediaStreamTrack> localTracks = GetLocalTracks();

                //Revert to DefaultStreamStatus
                foreach (var localTrack in localTracks)
                {
                    if (localTrack != null && localTrack.StreamStatus == MediaStreamStatusEnum.Inactive)
                    {
                        localTrack.StreamStatus = localTrack.DefaultStreamStatus;
                    }
                }

                var offerSdp = GetSessionDescription(localTracks, connectionAddress);
                return offerSdp;
            }
        }

        /// <summary>
        /// Generates an SDP answer in response to an offer. The remote description MUST be set 
        /// prior to calling this method.
        /// </summary>
        /// <param name="connectionAddress">Optional. If set this address will be used as 
        /// the SDP Connection address. If not specified the Operating System routing table
        /// will be used to lookup the address used to connect to the SDP connection address
        /// from the remote offer. Any and IPv6Any are special cases. If they are set the respective
        /// Internet facing IPv4 or IPv6 address will be used.</param>
        /// <returns>A task that when complete contains the SDP answer.</returns>
        /// <remarks>As specified in https://tools.ietf.org/html/rfc3264#section-6.1.
        ///  "If the answerer has no media formats in common for a particular
        ///   offered stream, the answerer MUST reject that media stream by setting
        ///   the port to zero."
        /// </remarks>
        public virtual SDP CreateAnswer(IPAddress connectionAddress)
        {
            if (RemoteDescription == null)
            {
                throw new ApplicationException("The remote description is not set, cannot create SDP answer.");
            }
            else
            {
                var offer = RemoteDescription;

                List<MediaStreamTrack> tracks = new List<MediaStreamTrack>();

                // The order of the announcements in the answer must match the order in the offer.
                foreach (var announcement in offer.Media)
                {
                    // Adjust the local audio tracks to only include compatible capabilities.
                    if (announcement.Media == SDPMediaTypesEnum.audio)
                    {
                        if (AudioStream.LocalTrack != null)
                        {
                            tracks.Add(AudioStream.LocalTrack);
                        }
                    }
                    else if (announcement.Media == SDPMediaTypesEnum.video)
                    {
                        if (VideoStream.LocalTrack != null)
                        {
                            tracks.Add(VideoStream.LocalTrack);
                        }
                    }
                }

                if (connectionAddress == null)
                {
                    // No specific connection address supplied. Lookup the local address to connect to the offer address.
                    var offerConnectionAddress = (offer.Connection?.ConnectionAddress != null) ? IPAddress.Parse(offer.Connection.ConnectionAddress) : null;

                    if (offerConnectionAddress == null || offerConnectionAddress == IPAddress.Any || offerConnectionAddress == IPAddress.IPv6Any)
                    {
                        connectionAddress = NetServices.InternetDefaultAddress;
                    }
                    else
                    {
                        connectionAddress = NetServices.GetLocalAddressForRemote(offerConnectionAddress);
                    }
                }

                var answerSdp = GetSessionDescription(tracks, connectionAddress);

                return answerSdp;
            }
        }

        /// <summary>
        /// Sets the remote SDP description for this session.
        /// </summary>
        /// <param name="sdpType">Whether the remote SDP is an offer or answer.</param>
        /// <param name="sessionDescription">The SDP that will be set as the remote description.</param>
        /// <returns>If successful an OK enum result. If not an enum result indicating the failure cause.</returns>
        public virtual SetDescriptionResultEnum SetRemoteDescription(SdpType sdpType, SDP sessionDescription)
        {
            if (sessionDescription == null)
            {
                throw new ArgumentNullException("sessionDescription", "The session description cannot be null for SetRemoteDescription.");
            }

            try
            {
                if (sessionDescription.Media?.Count == 0)
                {
                    return SetDescriptionResultEnum.NoRemoteMedia;
                }
                else if (sessionDescription.Media?.Count == 1)
                {
                    var remoteMediaType = sessionDescription.Media.First().Media;
                    if (remoteMediaType == SDPMediaTypesEnum.audio && AudioStream.LocalTrack == null)
                    {
                        return SetDescriptionResultEnum.NoMatchingMediaType;
                    }
                    else if (remoteMediaType == SDPMediaTypesEnum.video && VideoStream.LocalTrack == null)
                    {
                        return SetDescriptionResultEnum.NoMatchingMediaType;
                    }
                }

                // Pre-flight checks have passed. Move onto matching up the local and remote media streams.
                IPAddress connectionAddress = null;
                if (sessionDescription.Connection != null && !string.IsNullOrEmpty(sessionDescription.Connection.ConnectionAddress))
                {
                    connectionAddress = IPAddress.Parse(sessionDescription.Connection.ConnectionAddress);
                }

                IPEndPoint remoteAudioRtpEP = null;
                IPEndPoint remoteAudioRtcpEP = null;
                IPEndPoint remoteVideoRtpEP = null;
                IPEndPoint remoteVideoRtcpEP = null;

                //Remove Remote Tracks before add new one (this was added to implement renegotiation logic)
                AudioStream.RemoteTrack = null;
                VideoStream.RemoteTrack = null;

                foreach (var announcement in sessionDescription.Media.Where(x => x.Media == SDPMediaTypesEnum.audio || x.Media == SDPMediaTypesEnum.video))
                {
                    MediaStreamStatusEnum mediaStreamStatus = announcement.MediaStreamStatus.HasValue ? announcement.MediaStreamStatus.Value : MediaStreamStatusEnum.SendRecv;
                    var remoteTrack = new MediaStreamTrack(announcement.Media, true, announcement.MediaFormats.Values.ToList(), mediaStreamStatus, announcement.SsrcAttributes, announcement.HeaderExtensions);
                    addTrack(remoteTrack);

                    if (rtpSessionConfig.UseSdpCryptoNegotiation)
                    {
                        if (announcement.Transport != RTP_SECUREMEDIA_PROFILE)
                        {
                            logger.LogError($"Error negotiating secure media. Invalid Transport {announcement.Transport}.");
                            return SetDescriptionResultEnum.CryptoNegotiationFailed;
                        }
                        
                        if (announcement.SecurityDescriptions.Count(s => SrtpCryptoSuites.Contains(s.CryptoSuite)) > 0)
                        {
                            // Setup the appropriate srtp handler
                            var mediaType = announcement.Media;
                            var srtpHandler = GetOrCreateSrtpHandler(mediaType);
                            if (!srtpHandler.SetupRemote(announcement.SecurityDescriptions, sdpType))
                            {
                                logger.LogError($"Error negotiating secure media for type {mediaType}. Incompatible crypto parameter.");
                                return SetDescriptionResultEnum.CryptoNegotiationFailed;
                            }

                            if (srtpHandler.IsNegotiationComplete)
                            {
                                SetSecurityContext(mediaType,
                                        srtpHandler.ProtectRTP,
                                        srtpHandler.UnprotectRTP,
                                        srtpHandler.ProtectRTCP,
                                        srtpHandler.UnprotectRTCP);
                            }
                        }
                        // If we had no crypto but we were definetely expecting something since we had a port value
                        else if (announcement.Port != 0)
                        {
                            logger.LogError("Error negotiating secure media. No compatible crypto suite.");
                            return SetDescriptionResultEnum.CryptoNegotiationFailed;
                        }
                    }

                    if (announcement.Media == SDPMediaTypesEnum.audio)
                    {
                        if (AudioStream.LocalTrack == null)
                        {
                            // We don't have an audio track BUT we must have another track (which has to be video). The choices are
                            // to reject the offer or to set audio stream as inactive and accept the video. We accept the video.
                            var inactiveLocalAudioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, remoteTrack.Capabilities, MediaStreamStatusEnum.Inactive);
                            addTrack(inactiveLocalAudioTrack);
                        }
                        else
                        {
                            AudioStream.LocalTrack.Capabilities = SDPAudioVideoMediaFormat.GetCompatibleFormats(announcement.MediaFormats.Values.ToList(), AudioStream.LocalTrack?.Capabilities);
                            remoteAudioRtpEP = GetAnnouncementRTPDestination(announcement, connectionAddress);

                            // Check whether RTP events can be supported and adjust our parameters to match the remote party if we can.
                            SDPAudioVideoMediaFormat commonEventFormat = SDPAudioVideoMediaFormat.GetCommonRtpEventFormat(announcement.MediaFormats.Values.ToList(), AudioStream.LocalTrack.Capabilities);
                            if (!commonEventFormat.IsEmpty())
                            {
                                AudioStream.RemoteRtpEventPayloadID = commonEventFormat.ID;
                            }

                            SetLocalTrackStreamStatus(AudioStream.LocalTrack, remoteTrack.StreamStatus, remoteAudioRtpEP);
                            if (remoteTrack.StreamStatus != MediaStreamStatusEnum.Inactive && AudioStream.LocalTrack.StreamStatus != MediaStreamStatusEnum.Inactive)
                            {
                                remoteAudioRtcpEP = (rtpSessionConfig.IsRtcpMultiplexed) ? remoteAudioRtpEP : new IPEndPoint(remoteAudioRtpEP.Address, remoteAudioRtpEP.Port + 1);
                            }
                        }
                    }
                    else if (announcement.Media == SDPMediaTypesEnum.video)
                    {
                        if (VideoStream.LocalTrack == null)
                        {
                            // We don't have a video track BUT we must have another track (which has to be audio). The choices are
                            // to reject the offer or to set video stream as inactive and accept the audio. We accept the audio.
                            var inactiveLocalVideoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, remoteTrack.Capabilities, MediaStreamStatusEnum.Inactive);
                            addTrack(inactiveLocalVideoTrack);
                        }
                        else
                        {
                            VideoStream.LocalTrack.Capabilities = SDPAudioVideoMediaFormat.GetCompatibleFormats(announcement.MediaFormats.Values.ToList(), VideoStream.LocalTrack?.Capabilities);
                            remoteVideoRtpEP = GetAnnouncementRTPDestination(announcement, connectionAddress);

                            SetLocalTrackStreamStatus(VideoStream.LocalTrack, remoteTrack.StreamStatus, remoteVideoRtpEP);
                            if (remoteTrack.StreamStatus != MediaStreamStatusEnum.Inactive && VideoStream.LocalTrack.StreamStatus != MediaStreamStatusEnum.Inactive)
                            {
                                remoteVideoRtcpEP = (rtpSessionConfig.IsRtcpMultiplexed) ? remoteVideoRtpEP : new IPEndPoint(remoteVideoRtpEP.Address, remoteVideoRtpEP.Port + 1);
                            }
                        }
                    }
                }

                //Close old RTCPSessions opened
                if (AudioStream.RtcpSession != null && AudioStream.RemoteTrack == null && AudioStream.LocalTrack == null)
                {
                    AudioStream.RtcpSession.Close(null);
                }

                if (VideoStream.RtcpSession != null && VideoStream.RemoteTrack == null && VideoStream.LocalTrack == null)
                {
                    VideoStream.RtcpSession.Close(null);
                }

                if (VideoStream.LocalTrack == null && AudioStream.LocalTrack != null
                    && AudioStream.LocalTrack.Capabilities?.Where(x => x.Name().ToLower() != SDP.TELEPHONE_EVENT_ATTRIBUTE).Count() == 0)
                {
                    return SetDescriptionResultEnum.AudioIncompatible;
                }
                else if (AudioStream.LocalTrack == null && VideoStream.LocalTrack != null && VideoStream.LocalTrack.Capabilities?.Count == 0)
                {
                    return SetDescriptionResultEnum.VideoIncompatible;
                }
                else
                {
                    AudioStream.CheckAudioFormatsNegotiation();

                    VideoStream.CheckVideoFormatsNegotiation();


                    // If we get to here then the remote description was compatible with the local media tracks.
                    // Set the remote description and end points.
                    RequireRenegotiation = false;
                    RemoteDescription = sessionDescription;
                    AudioStream.DestinationEndPoint =
                        (remoteAudioRtpEP != null && remoteAudioRtpEP.Port != SDP.IGNORE_RTP_PORT_NUMBER) ? remoteAudioRtpEP : AudioStream.DestinationEndPoint;
                    AudioStream.ControlDestinationEndPoint =
                        (remoteAudioRtcpEP != null && remoteAudioRtcpEP.Port != SDP.IGNORE_RTP_PORT_NUMBER) ? remoteAudioRtcpEP : AudioStream.ControlDestinationEndPoint;
                    VideoStream.DestinationEndPoint =
                        (remoteVideoRtpEP != null && remoteVideoRtpEP.Port != SDP.IGNORE_RTP_PORT_NUMBER) ? remoteVideoRtpEP : VideoStream.DestinationEndPoint;
                    VideoStream.ControlDestinationEndPoint =
                         (remoteVideoRtcpEP != null && remoteVideoRtcpEP.Port != SDP.IGNORE_RTP_PORT_NUMBER) ? remoteVideoRtcpEP : VideoStream.ControlDestinationEndPoint;

                    return SetDescriptionResultEnum.OK;
                }
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception in RTPSession SetRemoteDescription. {excp.Message}.");
                return SetDescriptionResultEnum.Error;
            }
        }

        /// <summary>
        /// Sets the stream status on a local audio or video media track.
        /// </summary>
        /// <param name="kind">The type of the media track. Must be audio or video.</param>
        /// <param name="status">The stream status for the media track.</param>
        public void SetMediaStreamStatus(SDPMediaTypesEnum kind, MediaStreamStatusEnum status)
        {
            if (kind == SDPMediaTypesEnum.audio && AudioStream.LocalTrack != null)
            {
                AudioStream.LocalTrack.StreamStatus = status;
                m_sdpAnnouncementVersion++;
            }
            else if (kind == SDPMediaTypesEnum.video && VideoStream.LocalTrack != null)
            {
                VideoStream.LocalTrack.StreamStatus = status;
                m_sdpAnnouncementVersion++;
            }
        }

        /// <summary>
        /// Gets the RTP end point for an SDP media announcement from the remote peer.
        /// </summary>
        /// <param name="announcement">The media announcement to get the connection address for.</param>
        /// <param name="connectionAddress">The remote SDP session level connection address. Will be null if not available.</param>
        /// <returns>An IP end point for an SDP media announcement from the remote peer.</returns>
        private IPEndPoint GetAnnouncementRTPDestination(SDPMediaAnnouncement announcement, IPAddress connectionAddress)
        {
            SDPMediaTypesEnum kind = announcement.Media;
            IPEndPoint rtpEndPoint = null;

            var remoteAddr = (announcement.Connection != null) ? IPAddress.Parse(announcement.Connection.ConnectionAddress) : connectionAddress;

            if (remoteAddr != null)
            {
                if (announcement.Port < IPEndPoint.MinPort || announcement.Port > IPEndPoint.MaxPort)
                {
                    logger.LogWarning($"Remote {kind} announcement contained an invalid port number {announcement.Port}.");

                    // Set the remote port number to "9" which means ignore and wait for it be set some other way
                    // such as when a remote RTP packet or arrives or ICE negotiation completes.
                    rtpEndPoint = new IPEndPoint(remoteAddr, SDP.IGNORE_RTP_PORT_NUMBER);
                }
                else
                {
                    rtpEndPoint = new IPEndPoint(remoteAddr, announcement.Port);
                }
            }

            return rtpEndPoint;
        }

        /// <summary>
        /// Used for child classes that require a single RTP channel for all RTP (audio and video)
        /// and RTCP communications.
        /// </summary>
        protected void addSingleTrack()
        {
            // We use audio as the media type when multiplexing.
            AudioStream.AddRtpChannel(CreateRtpChannel());
            CreateRtcpSession(AudioStream);
        }

        /// <summary>
        /// Adds a media track to this session. A media track represents an audio or video
        /// stream and can be a local (which means we're sending) or remote (which means
        /// we're receiving).
        /// </summary>
        /// <param name="track">The media track to add to the session.</param>
        public virtual void addTrack(MediaStreamTrack track)
        {
            if (track == null)
            {
                return;
            }
            if (track.IsRemote)
            {
                AddRemoteTrack(track);
            }
            else
            {
                AddLocalTrack(track);
            }
        }

        /// <summary>
        /// Removes a media track from this session. A media track represents an audio or video
        /// stream and can be a local (which means we're sending) or remote (which means
        /// we're receiving).
        /// </summary>
        /// <param name="track">The media track to add to the session.</param>
        public virtual bool removeTrack(MediaStreamTrack track)
        {
            if (track == null)
            {
                return false;
            }
            if (track.IsRemote)
            {
                return RemoveRemoteTrack(track);
            }
            else
            {
                return RemoveLocalTrack(track);
            }
        }

        /// <summary>
        /// Removes a local media stream to this session.
        /// </summary>
        /// <param name="track">The local track to remove.</param>
        private bool RemoveLocalTrack(MediaStreamTrack track)
        {
            //const string REMOVE_TRACK_CLOSE_REASON = "Track Removed";
            bool willRemoveTrack = (track.Kind == SDPMediaTypesEnum.audio && AudioStream.LocalTrack == track) ||
                (track.Kind == SDPMediaTypesEnum.video && VideoStream.LocalTrack == track);

            if (!willRemoveTrack)
            {
                return false;
            }

            if (track.Kind == SDPMediaTypesEnum.audio)
            {
                if (AudioStream.LocalTrack != null)
                {
                    RequireRenegotiation = true;
                    AudioStream.LocalTrack = null;
                }

                /*if (AudioRtcpSession != null && AudioLocalTrack == null && AudioRemoteTrack == null)
                {
                    if (!AudioRtcpSession.IsClosed)
                    {
                        AudioRtcpSession.Close(null);
                    }
                    AudioRtcpSession = null;
                }*/
            }
            else if (track.Kind == SDPMediaTypesEnum.video)
            {
                if (VideoStream.LocalTrack != null)
                {
                    RequireRenegotiation = true;
                    VideoStream.LocalTrack = null;
                }

                /*if (VideoRtcpSession != null && VideoLocalTrack == null && VideoRemoteTrack == null)
                {
                    if (!VideoRtcpSession.IsClosed)
                    {
                        VideoRtcpSession.Close(null);
                    }
                    VideoRtcpSession = null;
                }*/
            }

            //Remove Channel as we don't need this connection anymore
            /*if ((m_isMediaMultiplexed && VideoLocalTrack == null && AudioLocalTrack == null) ||
                (!m_isMediaMultiplexed && m_rtpChannels.ContainsKey(track.Kind)))
            {
                RTPChannel channel;
                if (m_rtpChannels.TryGetValue(track.Kind, out channel))
                {
                    m_rtpChannels.Remove(track.Kind);
                    channel.Close(REMOVE_TRACK_CLOSE_REASON);
                }
            }*/
            return true;
        }

        /// <summary>
        /// Removes a remote media stream to this session.
        /// </summary>
        /// <param name="track">The remote track to remove.</param>
        private bool RemoveRemoteTrack(MediaStreamTrack track)
        {
            //const string REMOVE_TRACK_CLOSE_REASON = "Track Removed";
            bool willRemoveTrack = (track.Kind == SDPMediaTypesEnum.audio && AudioStream.RemoteTrack == track) ||
                (track.Kind == SDPMediaTypesEnum.video && VideoStream.RemoteTrack == track);

            if (!willRemoveTrack)
            {
                return false;
            }

            if (track.Kind == SDPMediaTypesEnum.audio)
            {
                if (AudioStream.RemoteTrack != null)
                {
                    RequireRenegotiation = true;
                    AudioStream.RemoteTrack = null;
                }

                /*if (AudioRtcpSession != null && AudioLocalTrack == null && AudioRemoteTrack == null)
                {
                    if (!AudioRtcpSession.IsClosed)
                    {
                        AudioRtcpSession.Close(null);
                    }
                    AudioRtcpSession = null;
                }*/
            }
            else if (track.Kind == SDPMediaTypesEnum.video)
            {
                if (VideoStream.RemoteTrack != null)
                {
                    RequireRenegotiation = true;
                    VideoStream.RemoteTrack = null;
                }

                /*if (VideoRtcpSession != null && VideoLocalTrack == null && VideoRemoteTrack == null)
                {
                    if (!VideoRtcpSession.IsClosed)
                    {
                        VideoRtcpSession.Close(null);
                    }
                    VideoRtcpSession = null;
                }*/
            }
            return true;
        }

        /// <summary>
        /// Adds a local media stream to this session. Local media tracks should be added by the
        /// application to control what session description offers and answers can be made as
        /// well as being used to match up with remote tracks.
        /// </summary>
        /// <param name="track">The local track to add.</param>
        private void AddLocalTrack(MediaStreamTrack track)
        {
            if (track.Kind == SDPMediaTypesEnum.audio && AudioStream.LocalTrack != null)
            {
                throw new ApplicationException("A local audio track has already been set on this session.");
            }
            else if (track.Kind == SDPMediaTypesEnum.video && VideoStream.LocalTrack != null)
            {
                throw new ApplicationException("A local video track has already been set on this session.");
            }

            if (track.StreamStatus == MediaStreamStatusEnum.Inactive)
            {
                // Inactive tracks don't use/require any local resources. Instead they are place holders
                // so that the session description offers/answers can be balanced with the remote party.
                // For example if the remote party offers audio and video but we only support audio we
                // can reject the call or we can accept the audio and answer with an inactive video
                // announcement.
                if (track.Kind == SDPMediaTypesEnum.audio)
                {
                    RequireRenegotiation = true;
                    AudioStream.LocalTrack = track;
                }
                else if (track.Kind == SDPMediaTypesEnum.video)
                {
                    RequireRenegotiation = true;
                    VideoStream.LocalTrack = track;
                }
            }
            else
            {
                if (track.Kind == SDPMediaTypesEnum.audio)
                {
                    if (!AudioStream.HasRtpChannel())
                    {
                        AudioStream.AddRtpChannel(CreateRtpChannel());
                    }

                    CreateRtcpSession(AudioStream);

                    RequireRenegotiation = true;
                    AudioStream.LocalTrack = track;
                }
                else if (track.Kind == SDPMediaTypesEnum.video)
                {
                    // We need to get EndPoints and Security Context 
                    InitIPEndPointAndSecurityContext(VideoStream);

                    // Only create the RTP socket, RTCP session etc. if a non-inactive local track is added
                    // to the session.
                    if (!VideoStream.HasRtpChannel())
                    {
                        VideoStream.AddRtpChannel(CreateRtpChannel());
                    }

                    CreateRtcpSession(VideoStream);

                    RequireRenegotiation = true;
                    VideoStream.LocalTrack = track;
                }
            }
        }

        /// <summary>
        /// Adds a remote media stream to this session. Typically the only way remote tracks
        /// should get added is from setting the remote session description. Adding a remote
        /// track does not cause the creation of any local resources.
        /// </summary>
        /// <param name="track">The remote track to add.</param>
        private void AddRemoteTrack(MediaStreamTrack track)
        {
            if (track.Kind == SDPMediaTypesEnum.audio)
            {
                RequireRenegotiation = true;
                if (AudioStream.RemoteTrack != null)
                {
                    //throw new ApplicationException("A remote audio track has already been set on this session.");
                    logger.LogDebug($"Replacing existing remote audio track for ssrc {AudioStream.RemoteTrack.Ssrc}.");
                }

                AudioStream.RemoteTrack = track;

                // Even if there's no local audio track an RTCP session can still be required 
                // in case the remote party send reports (presumably in case we decide we do want
                // to send or receive audio on this session at some later stage).
                CreateRtcpSession(AudioStream);

            }
            else if (track.Kind == SDPMediaTypesEnum.video)
            {
                RequireRenegotiation = true;
                if (VideoStream.RemoteTrack != null)
                {
                    logger.LogDebug($"Replacing existing remote video track for ssrc {VideoStream.RemoteTrack.Ssrc}.");
                }

                VideoStream.RemoteTrack = track;

                // Even if there's no local video track an RTCP session can still be required 
                // in case the remote party send reports (presumably in case we decide we do want
                // to send or receive video on this session at some later stage).
                CreateRtcpSession(VideoStream);
            }
        }

        private Boolean InitIPEndPointAndSecurityContext(MediaStream mediaStream)
        {
            // Get primary AudioStream
            if ( (AudioStream != null) && (mediaStream != null) )
            {
                var secureContext = AudioStream.GetSecurityContext();
                if (secureContext != null)
                {
                    mediaStream.SetSecurityContext(secureContext.ProtectRtpPacket, secureContext.UnprotectRtpPacket, secureContext.ProtectRtcpPacket, secureContext.UnprotectRtcpPacket);
                }
                mediaStream.SetDestination(AudioStream.DestinationEndPoint, AudioStream.ControlDestinationEndPoint);

                return true;
            }
            return false;
        }

        /// <summary>
        /// Adjust the stream status of the local media tracks based on the remote tracks.
        /// </summary>
        private void SetLocalTrackStreamStatus(MediaStreamTrack localTrack, MediaStreamStatusEnum remoteTrackStatus, IPEndPoint remoteRTPEndPoint)
        {
            if (localTrack != null)
            {
                if (localTrack.StreamStatus == MediaStreamStatusEnum.Inactive)
                {
                    localTrack.StreamStatus = localTrack.DefaultStreamStatus;
                }

                if (remoteTrackStatus == MediaStreamStatusEnum.Inactive)
                {
                    // The remote party does not support this media type. Set the local stream status to inactive.
                    localTrack.StreamStatus = MediaStreamStatusEnum.Inactive;
                }
                else if (remoteRTPEndPoint != null)
                {
                    if (IPAddress.Any.Equals(remoteRTPEndPoint.Address) || IPAddress.IPv6Any.Equals(remoteRTPEndPoint.Address))
                    {
                        // A connection address of 0.0.0.0 or [::], which is unreachable, means the media is inactive, except
                        // if a special port number is used (defined as "9") which indicates that the media announcement is not 
                        // responsible for setting the remote end point for the audio stream. Instead it's most likely being set 
                        // using ICE.
                        if (remoteRTPEndPoint.Port != SDP.IGNORE_RTP_PORT_NUMBER)
                        {
                            localTrack.StreamStatus = MediaStreamStatusEnum.Inactive;
                        }
                    }
                    else if (remoteRTPEndPoint.Port == 0)
                    {
                        localTrack.StreamStatus = MediaStreamStatusEnum.Inactive;
                    }
                }
            }
        }

        /// <summary>
        /// Generates a session description from the provided list of tracks.
        /// </summary>
        /// <param name="tracks">The list of tracks to generate the session description for.</param>
        /// <param name="connectionAddress">Optional. If set this address will be used as 
        /// the SDP Connection address. If not specified the Internet facing address will
        /// be used. IPAddress.Any and IPAddress. Any and IPv6Any are special cases. If they are set the respective
        /// Internet facing IPv4 or IPv6 address will be used.</param>
        /// <returns>A session description payload.</returns>
        private SDP GetSessionDescription(List<MediaStreamTrack> tracks, IPAddress connectionAddress)
        {
            IPAddress localAddress = connectionAddress;

            if (localAddress == null || localAddress == IPAddress.Any || localAddress == IPAddress.IPv6Any)
            {
                if (rtpSessionConfig.BindAddress != null)
                {
                    localAddress = rtpSessionConfig.BindAddress;
                }
                else if (AudioStream.DestinationEndPoint != null && AudioStream.DestinationEndPoint.Address != null)
                {
                    if (IPAddress.Any.Equals(AudioStream.DestinationEndPoint.Address) || IPAddress.IPv6Any.Equals(AudioStream.DestinationEndPoint.Address))
                    {
                        // If the remote party has set an inactive media stream via the connection address then we do the same.
                        localAddress = AudioStream.DestinationEndPoint.Address;
                    }
                    else
                    {
                        localAddress = NetServices.GetLocalAddressForRemote(AudioStream.DestinationEndPoint.Address);
                    }
                }
                else if (VideoStream.DestinationEndPoint != null && VideoStream.DestinationEndPoint.Address != null)
                {
                    if (IPAddress.Any.Equals(VideoStream.DestinationEndPoint.Address) || IPAddress.IPv6Any.Equals(VideoStream.DestinationEndPoint.Address))
                    {
                        // If the remote party has set an inactive media stream via the connection address then we do the same.
                        localAddress = VideoStream.DestinationEndPoint.Address;
                    }
                    else
                    {
                        localAddress = NetServices.GetLocalAddressForRemote(VideoStream.DestinationEndPoint.Address);
                    }
                }
                else
                {
                    if (localAddress == IPAddress.IPv6Any && NetServices.InternetDefaultIPv6Address != null)
                    {
                        // If an IPv6 address has been requested AND there is a public IPv6 address available use it.
                        localAddress = NetServices.InternetDefaultIPv6Address;
                    }
                    else
                    {
                        localAddress = NetServices.InternetDefaultAddress;
                    }
                }
            }

            SDP sdp = new SDP(IPAddress.Loopback);
            sdp.SessionId = m_sdpSessionID;
            sdp.AnnouncementVersion = m_sdpAnnouncementVersion;

            sdp.Connection = new SDPConnectionInformation(localAddress);

            int mediaIndex = 0;

            foreach (var track in tracks)
            {
                (int mindex, string midTag) = RemoteDescription == null ? (mediaIndex++, mediaIndex.ToString()) : RemoteDescription.GetIndexForMediaType(track.Kind);

                int rtpPort = 0; // A port of zero means the media type is not supported.
                if (track.Capabilities != null && track.Capabilities.Count() > 0 && track.StreamStatus != MediaStreamStatusEnum.Inactive)
                {
                    if(rtpSessionConfig.IsMediaMultiplexed || track.Kind == SDPMediaTypesEnum.audio)
                    {
                        rtpPort = AudioStream.GetRTPChannel().RTPPort;
                    }
                    else if (track.Kind == SDPMediaTypesEnum.video)
                    {
                        rtpPort = VideoStream.GetRTPChannel().RTPPort;
                    }
                }

                SDPMediaAnnouncement announcement = new SDPMediaAnnouncement(
                   track.Kind,
                   rtpPort,
                   track.Capabilities);

                announcement.Transport = rtpSessionConfig.UseSdpCryptoNegotiation ? RTP_SECUREMEDIA_PROFILE : RTP_MEDIA_PROFILE;
                announcement.MediaStreamStatus = track.StreamStatus;
                announcement.MLineIndex = mindex;

                if (track.MaximumBandwidth > 0)
                {
                    announcement.TIASBandwidth = track.MaximumBandwidth;
                }

                if (track.Ssrc != 0)
                {
                    string trackCname = track.Kind == SDPMediaTypesEnum.video ?
                        VideoStream.RtcpSession?.Cname : AudioStream.RtcpSession?.Cname;

                    if (trackCname != null)
                    {
                        announcement.SsrcAttributes.Add(new SDPSsrcAttribute(track.Ssrc, trackCname, null));
                    }
                }

                if (rtpSessionConfig.UseSdpCryptoNegotiation)
                {
                    var sdpType = RemoteDescription == null || RequireRenegotiation ? SdpType.offer : SdpType.answer;

                    if (sdpType == SdpType.offer)
                    {
                        uint tag = 1;
                        foreach (SDPSecurityDescription.CryptoSuites cryptoSuite in SrtpCryptoSuites)
                        {
                            announcement.SecurityDescriptions.Add(SDPSecurityDescription.CreateNew(tag, cryptoSuite));
                            tag++;
                        }
                    }
                    else
                    {
                        var sel = RemoteDescription?.Media.FirstOrDefault(a => a.MLineIndex == mindex)?.SecurityDescriptions
                                                          .FirstOrDefault(s => SrtpCryptoSuites.Contains(s.CryptoSuite));

                        if (sel == null)
                        {
                            throw new ApplicationException("Error creating crypto attribute. No compatible offer.");
                        }
                        else
                        {
                            announcement.SecurityDescriptions.Add(SDPSecurityDescription.CreateNew(sel.Tag, sel.CryptoSuite));
                        }
                    }

                    var handler = GetOrCreateSrtpHandler(announcement.Media);
                    handler.SetupLocal(announcement.SecurityDescriptions, sdpType);
                    
                    if (handler.IsNegotiationComplete)
                    {
                        SetSecurityContext(announcement.Media, 
                            handler.ProtectRTP,
                            handler.UnprotectRTP,
                            handler.ProtectRTCP,
                            handler.UnprotectRTCP);
                    }
                }

                sdp.Media.Add(announcement);
            }

            return sdp;
        }

        /// <summary>
        /// Creates a new RTP channel (which manages the UDP socket sending and receiving RTP
        /// packets) for use with this session.
        /// </summary>
        /// <param name="mediaType">The type of media the RTP channel is for. Must be audio or video.</param>
        /// <returns>A new RTPChannel instance.</returns>
        protected virtual RTPChannel CreateRtpChannel()
        {
            if (rtpSessionConfig.IsMediaMultiplexed)
            {
                if (MultiplexRtpChannel != null)
                {
                    return MultiplexRtpChannel;
                }
            }

            // If RTCP is multiplexed we don't need a control socket.
            int bindPort = (rtpSessionConfig.BindPort == 0) ? 0 : rtpSessionConfig.BindPort + m_rtpChannelsCount * 2;
            var rtpChannel = new RTPChannel(!rtpSessionConfig.IsRtcpMultiplexed, rtpSessionConfig.BindAddress, bindPort, rtpSessionConfig.RtpPortRange);


            if (rtpSessionConfig.IsMediaMultiplexed)
            {
                MultiplexRtpChannel = rtpChannel;
            }

            rtpChannel.OnRTPDataReceived += OnReceive;
            rtpChannel.OnControlDataReceived += OnReceive; // RTCP packets could come on RTP or control socket.
            rtpChannel.OnClosed += OnRTPChannelClosed;

            // Start the RTP, and if required the Control, socket receivers and the RTCP session.
            rtpChannel.Start();


            m_rtpChannelsCount++;

            return rtpChannel;
        }

        /// <summary>
        /// Gets the local tracks available in this session. Will only be audio, video or both.
        /// Local tracks represent an audio or video source that we are sending to the remote party.
        /// </summary>
        /// <returns>A list of the local tracks that have been added to this session.</returns>
        protected List<MediaStreamTrack> GetLocalTracks()
        {
            List<MediaStreamTrack> localTracks = new List<MediaStreamTrack>();

            if (AudioStream.LocalTrack != null)
            {
                localTracks.Add(AudioStream.LocalTrack);
            }
            else if (AudioStream.RtcpSession != null && !AudioStream.RtcpSession.IsClosed && AudioStream.RemoteTrack != null)
            {
                var inactiveAudioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, AudioStream.RemoteTrack.Capabilities, MediaStreamStatusEnum.Inactive);
                localTracks.Add(inactiveAudioTrack);
            }

            if (VideoStream.LocalTrack != null)
            {
                localTracks.Add(VideoStream.LocalTrack);
            }
            else if (VideoStream.RtcpSession != null && !VideoStream.RtcpSession.IsClosed && VideoStream.RemoteTrack != null)
            {
                var inactiveVideoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, VideoStream.RemoteTrack.Capabilities, MediaStreamStatusEnum.Inactive);
                localTracks.Add(inactiveVideoTrack);
            }

            return localTracks;
        }

        /// <summary>
        /// Starts the RTCP session(s) that monitor this RTP session.
        /// </summary>
        public virtual Task Start()
        {
            if (!IsStarted)
            {
                IsStarted = true;

                if (HasAudio && AudioStream.RtcpSession != null && AudioStream.LocalTrack.StreamStatus != MediaStreamStatusEnum.Inactive)
                {
                    // The local audio track may have been disabled if there were no matching capabilities with
                    // the remote party.
                    AudioStream.RtcpSession.Start();
                }

                if (HasVideo && VideoStream.RtcpSession != null && VideoStream.LocalTrack.StreamStatus != MediaStreamStatusEnum.Inactive)
                {
                    // The local video track may have been disabled if there were no matching capabilities with
                    // the remote party.
                    VideoStream.RtcpSession.Start();
                }

                OnStarted?.Invoke();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Sends an audio sample to the remote peer.
        /// </summary>
        /// <param name="durationRtpUnits">The duration in RTP timestamp units of the audio sample. This
        /// value is added to the previous RTP timestamp when building the RTP header.</param>
        /// <param name="sample">The audio sample to set as the RTP packet payload.</param>
        public void SendAudio(uint durationRtpUnits, byte[] sample)
        {
            AudioStream?.SendAudio(durationRtpUnits, sample);
        }

        /// <summary>
        /// Sends a video sample to the remote peer.
        /// </summary>
        /// <param name="durationRtpUnits">The duration in RTP timestamp units of the video sample. This
        /// value is added to the previous RTP timestamp when building the RTP header.</param>
        /// <param name="sample">The video sample to set as the RTP packet payload.</param>
        public void SendVideo(uint durationRtpUnits, byte[] sample)
        {
            VideoStream?.SendVideo(durationRtpUnits, sample);
        }

        /// <summary>
        /// Sends a DTMF tone as an RTP event to the remote party.
        /// </summary>
        /// <param name="key">The DTMF tone to send.</param>
        /// <param name="ct">RTP events can span multiple RTP packets. This token can
        /// be used to cancel the send.</param>
        public Task SendDtmf(byte key, CancellationToken ct)
        {
            return AudioStream?.SendDtmf(key, ct);
        }

        public Task SendDtmfEvent(RTPEvent rtpEvent, CancellationToken cancellationToken, int clockRate = RTPSession.DEFAULT_AUDIO_CLOCK_RATE, int samplePeriod = RTPSession.RTP_EVENT_DEFAULT_SAMPLE_PERIOD_MS)
        {
            return AudioStream?.SendDtmfEvent(rtpEvent, cancellationToken, clockRate, samplePeriod);
        }

        /// <summary>
        /// Close the session and RTP channel.
        /// </summary>
        public virtual void Close(string reason)
        {
            if (!IsClosed)
            {
                IsClosed = true;

                AudioStream.IsClosed = true;
                VideoStream.IsClosed = true;

                CloseRtcpSession(AudioStream, reason);
                CloseRtcpSession(VideoStream, reason);

                if (AudioStream.HasRtpChannel())
                {
                    var rtpChannel = AudioStream.GetRTPChannel();
                    rtpChannel.OnRTPDataReceived -= OnReceive;
                    rtpChannel.OnControlDataReceived -= OnReceive;
                    rtpChannel.OnClosed -= OnRTPChannelClosed;
                    rtpChannel.Close(reason);
                    rtpChannel = null;
                }

                if (VideoStream.HasRtpChannel())
                {
                    var rtpChannel = VideoStream.GetRTPChannel();
                    rtpChannel.OnRTPDataReceived -= OnReceive;
                    rtpChannel.OnControlDataReceived -= OnReceive;
                    rtpChannel.OnClosed -= OnRTPChannelClosed;
                    rtpChannel.Close(reason);
                    rtpChannel = null;
                }


                OnRtpClosed?.Invoke(reason);

                OnClosed?.Invoke();
            }
        }
        
        protected void OnReceive(int localPort, IPEndPoint remoteEndPoint, byte[] buffer)
        {
            if (remoteEndPoint.Address.IsIPv4MappedToIPv6)
            {
                // Required for matching existing RTP end points (typically set from SDP) and
                // whether or not the destination end point should be switched.
                remoteEndPoint.Address = remoteEndPoint.Address.MapToIPv4();
            }

            // Quick sanity check on whether this is not an RTP or RTCP packet.
            if (buffer?.Length > RTPHeader.MIN_HEADER_LEN && buffer[0] >= 128 && buffer[0] <= 191)
            {
                if ((rtpSessionConfig.IsSecure || rtpSessionConfig.UseSdpCryptoNegotiation) && !IsSecureContextReady())
                {
                    logger.LogWarning("RTP or RTCP packet received before secure context ready.");
                }
                else
                {
                    if (buffer[1] == 0xC8 /* RTCP SR */ || buffer[1] == 0xC9 /* RTCP RR */)
                    {
                        OnReceiveRTCPPacket(localPort, remoteEndPoint, buffer);
                    }
                    else
                    {
                        OnReceiveRTPPacket(localPort, remoteEndPoint, buffer);
                    }
                }
            }
        }

        private void OnReceiveRTCPPacket(int localPort, IPEndPoint remoteEndPoint, byte[] buffer)
        {
            //logger.LogDebug($"RTCP packet received from {remoteEndPoint} {buffer.HexStr()}");

            #region RTCP packet.

            // Get the SSRC in order to be able to figure out which media type 
            // This will let us choose the apropriate unprotect methods
            uint ssrc;
            if (BitConverter.IsLittleEndian)
            {
                ssrc = NetConvert.DoReverseEndian(BitConverter.ToUInt32(buffer, 4));
            }
            else
            {
                ssrc = BitConverter.ToUInt32(buffer, 4);
            }

            MediaStream mediaStream = GetMediaStream(ssrc);
            if (mediaStream != null)
            {
                var secureContext = mediaStream.GetSecurityContext();
                if (secureContext != null)
                {
                    int res = secureContext.UnprotectRtcpPacket(buffer, buffer.Length, out int outBufLen);
                    if (res != 0)
                    {
                        logger.LogWarning($"SRTCP unprotect failed for {mediaStream.MediaType} track, result {res}.");
                        return;
                    }
                    else
                    {
                        buffer = buffer.Take(outBufLen).ToArray();
                    }
                }
            }
            else
            {
                logger.LogWarning("Could not find appropriate remote track for SSRC for RTCP packet");
            }

            var rtcpPkt = new RTCPCompoundPacket(buffer);
            if (rtcpPkt != null)
            {
                mediaStream = GetMediaStream(rtcpPkt);
                if (rtcpPkt.Bye != null)
                {
                    logger.LogDebug($"RTCP BYE received for SSRC {rtcpPkt.Bye.SSRC}, reason {rtcpPkt.Bye.Reason}.");

                    // In some cases, such as a SIP re-INVITE, it's possible the RTP session
                    // will keep going with a new remote SSRC. 
                    if (mediaStream?.RemoteTrack != null && rtcpPkt.Bye.SSRC == mediaStream.RemoteTrack.Ssrc)
                    {
                        mediaStream.RtcpSession?.RemoveReceptionReport(rtcpPkt.Bye.SSRC);
                        //AudioDestinationEndPoint = null;
                        //AudioControlDestinationEndPoint = null;
                        mediaStream.RemoteTrack.Ssrc = 0;
                    }
                    else
                    {
                        // We close peer connection only if there is no more audio local/remote tracks
                        if ((AudioStream.RemoteTrack == null) && (AudioStream.LocalTrack == null))
                        {
                            OnRtcpBye?.Invoke(rtcpPkt.Bye.Reason);
                        }
                    }
                }
                else if (!IsClosed)
                {
                    if (mediaStream?.RtcpSession != null)
                    {
                        if (mediaStream.RtcpSession.LastActivityAt == DateTime.MinValue)
                        {
                            // On the first received RTCP report for a session check whether the remote end point matches the
                            // expected remote end point. If not it's "likely" that a private IP address was specified in the SDP.
                            // Take the risk and switch the remote control end point to the one we are receiving from.
                            if ((mediaStream.ControlDestinationEndPoint == null ||
                                !mediaStream.ControlDestinationEndPoint.Address.Equals(remoteEndPoint.Address) ||
                                mediaStream.ControlDestinationEndPoint.Port != remoteEndPoint.Port))
                            {
                                logger.LogDebug($"{mediaStream.MediaType} control end point switched from {mediaStream.ControlDestinationEndPoint} to {remoteEndPoint}.");
                                mediaStream.ControlDestinationEndPoint = remoteEndPoint;
                            }
                        }

                        mediaStream.RtcpSession.ReportReceived(remoteEndPoint, rtcpPkt);
                        mediaStream.ReceiveReport(remoteEndPoint, rtcpPkt);
                    }
                    else if (rtcpPkt.ReceiverReport?.SSRC == RTCP_RR_NOSTREAM_SSRC)
                    {
                        // Ignore for the time being. Not sure what use an empty RTCP Receiver Report can provide.
                    }
                    else if (AudioStream.RtcpSession?.PacketsReceivedCount > 0 || VideoStream.RtcpSession?.PacketsReceivedCount > 0)
                    {
                        // Only give this warning if we've received at least one RTP packet.
                        logger.LogWarning("Could not match an RTCP packet against any SSRC's in the session.");
                        logger.LogTrace(rtcpPkt.GetDebugSummary());
                    }
                }
            }
            else
            {
                logger.LogWarning("Failed to parse RTCP compound report.");
            }

            #endregion
        }

        private void OnReceiveRTPPacket(int localPort, IPEndPoint remoteEndPoint, byte[] buffer)
        {
            if (!IsClosed)
            {
                var hdr = new RTPHeader(buffer);
                hdr.ReceivedTime = DateTime.Now;
                var avFormat = GetFormatForRtpPacket(hdr);

                if (avFormat != null)
                {
                    if (avFormat.Value.Kind == SDPMediaTypesEnum.audio)
                    {
                        AudioStream.OnReceiveRTPPacket(hdr, avFormat.Value, localPort, remoteEndPoint, buffer);
                    }
                    else
                    {
                        VideoStream.OnReceiveRTPPacket(hdr, avFormat.Value, localPort, remoteEndPoint, buffer);
                    }
                }
                return;
            }
        }

        /// <summary>
        /// Attempts to get the audio or video media format for an RTP packet.
        /// </summary>
        /// <param name="header">The header of the received RTP packet.</param>
        /// <returns>The audio or video format for the received packet or null if it could not be determined.</returns>
        private SDPAudioVideoMediaFormat? GetFormatForRtpPacket(RTPHeader header)
        {
            MediaStreamTrack matchingTrack = null;

            var mediaStream = GetMediaStream(header.SyncSource);
            if (mediaStream != null)
            {
                matchingTrack = mediaStream.RemoteTrack;
            }
            else 
            {
                matchingTrack = GetMediaStreamTrack(header.PayloadType);
            }

            if (matchingTrack != null)
            {
                var format = matchingTrack.GetFormatForPayloadID(header.PayloadType);

                if (format != null)
                {
                    return format;
                }
                else
                {
                    logger.LogWarning($"An RTP packet with SSRC {header.SyncSource} matched the {matchingTrack.Kind} track but no capability exists for payload ID {header.PayloadType}.");
                    return null;
                }
            }
            else
            {
                logger.LogWarning($"An RTP packet with SSRC {header.SyncSource} and payload ID {header.PayloadType} was received that could not be matched to an audio or video stream.");
                return null;
            }
        }

        private MediaStreamTrack GetMediaStreamTrack(int payloadId)
        {
            MediaStreamTrack matchingTrack = null;

            if (AudioStream.RemoteTrack != null && AudioStream.RemoteTrack.IsPayloadIDMatch(payloadId))
            {
                matchingTrack = AudioStream.RemoteTrack;
            }
            else if (VideoStream.RemoteTrack != null && VideoStream.RemoteTrack.IsPayloadIDMatch(payloadId))
            {
                matchingTrack = VideoStream.RemoteTrack;
            }
            else if (AudioStream.LocalTrack != null && AudioStream.LocalTrack.IsPayloadIDMatch(payloadId))
            {
                matchingTrack = AudioStream.LocalTrack;
            }
            else if (VideoStream.LocalTrack != null && VideoStream.LocalTrack.IsPayloadIDMatch(payloadId))
            {
                matchingTrack = VideoStream.LocalTrack;
            }
            return matchingTrack;
        }
        
        private MediaStream GetMediaStream(uint ssrc)
        {
            if(HasAudio && !HasVideo)
            {
                return AudioStream;
            }
            else if(!HasAudio && HasVideo)
            {
                return VideoStream;
            }
            else if (AudioStream?.RemoteTrack?.IsSsrcMatch(ssrc) == true)
            {
                return AudioStream;
            }
            else if (VideoStream?.RemoteTrack?.IsSsrcMatch(ssrc) == true)
            {
                return VideoStream;
            }
            return null;
        }

        /// <summary>
        /// Attempts to get MediaStream that matches a received RTCP report.
        /// </summary>
        /// <param name="rtcpPkt">The RTCP compound packet received from the remote party.</param>
        /// <returns>If a match could be found an SSRC the MediaStream otherwise null.</returns>
        private MediaStream GetMediaStream(RTCPCompoundPacket rtcpPkt)
        {
            if (rtcpPkt.SenderReport != null)
            {
                return GetMediaStream(rtcpPkt.SenderReport.SSRC);
            }
            else if (rtcpPkt.ReceiverReport != null)
            {
                return GetMediaStream(rtcpPkt.ReceiverReport.SSRC);
            }

            // No match on SR/RR SSRC. Check the individual reception reports for a known SSRC.
            List<ReceptionReportSample> receptionReports = null;

            if (rtcpPkt.SenderReport != null)
            {
                receptionReports = rtcpPkt.SenderReport.ReceptionReports;
            }
            else if (rtcpPkt.ReceiverReport != null)
            {
                receptionReports = rtcpPkt.ReceiverReport.ReceptionReports;
            }

            if (receptionReports != null && receptionReports.Count > 0)
            {
                foreach (var recRep in receptionReports)
                {
                    if (AudioStream?.LocalTrack?.Ssrc == recRep.SSRC)
                    {
                        return AudioStream;
                    }
                    else if (VideoStream?.LocalTrack?.Ssrc == recRep.SSRC)
                        {
                        return VideoStream;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Allows additional control for sending raw RTP payloads (on the primary one). No framing or other processing is carried out.
        /// </summary>
        /// <param name="mediaType">The media type of the RTP packet being sent. Must be audio or video.</param>
        /// <param name="payload">The RTP packet payload.</param>
        /// <param name="timestamp">The timestamp to set on the RTP header.</param>
        /// <param name="markerBit">The value to set on the RTP header marker bit, should be 0 or 1.</param>
        /// <param name="payloadTypeID">The payload ID to set in the RTP header.</param>
        public void SendRtpRaw(SDPMediaTypesEnum mediaType, byte[] payload, uint timestamp, int markerBit, int payloadTypeID)
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                AudioStream.SendRtpRaw(payload, timestamp, markerBit, payloadTypeID);
            }
            else if (mediaType == SDPMediaTypesEnum.video)
            {
                VideoStream?.SendRtpRaw(payload, timestamp, markerBit, payloadTypeID);
            }
        }

        /// <summary>
        /// Sets the remote end points for a media type supported by this RTP session. (on the primary one)
        /// </summary>
        /// <param name="mediaType">The media type, must be audio or video, to set the remote end point for.</param>
        /// <param name="rtpEndPoint">The remote end point for RTP packets corresponding to the media type.</param>
        /// <param name="rtcpEndPoint">The remote end point for RTCP packets corresponding to the media type.</param>
        public void SetDestination(SDPMediaTypesEnum mediaType, IPEndPoint rtpEndPoint, IPEndPoint rtcpEndPoint)
        {
            if (rtpSessionConfig.IsMediaMultiplexed)
            {
                AudioStream.SetDestination(rtpEndPoint, rtcpEndPoint);
                VideoStream.SetDestination(rtpEndPoint, rtcpEndPoint);
            }
            else
            {
                if (mediaType == SDPMediaTypesEnum.audio)
                {
                    AudioStream.SetDestination(rtpEndPoint, rtcpEndPoint);
                }
                else if (mediaType == SDPMediaTypesEnum.video)
                {
                    VideoStream.SetDestination(rtpEndPoint, rtcpEndPoint);
                }
            }
        }

        /// <summary>
        /// Allows sending of RTCP feedback reports.
        /// </summary>
        /// <param name="mediaType">The media type of the RTCP report  being sent. Must be audio or video.</param>
        /// <param name="feedback">The feedback report to send.</param>
        public void SendRtcpFeedback(SDPMediaTypesEnum mediaType, RTCPFeedback feedback)
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                AudioStream.SendRtcpFeedback(feedback);
            }
            else if (mediaType == SDPMediaTypesEnum.audio)
            {
                VideoStream.SendRtcpFeedback(feedback);
            }
        }

        /// <summary>
        /// Sends the RTCP report to the remote call party.
        /// </summary>
        /// <param name="report">RTCP report to send.</param>
        public void SendRtcpReport(SDPMediaTypesEnum mediaType, RTCPCompoundPacket report)
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                AudioStream.SendRtcpReport(report);
            }
            else if (mediaType == SDPMediaTypesEnum.video)
            {
                VideoStream.SendRtcpReport(report);
            }
        }

        /// <summary>
        /// Event handler for the RTP channel closure.
        /// </summary>
        private void OnRTPChannelClosed(string reason)
        {
            Close(reason);
        }

        /// <summary>
        /// Close the session if the instance is out of scope.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            Close("disposed");
        }

        /// <summary>
        /// Close the session if the instance is out of scope.
        /// </summary>
        public virtual void Dispose()
        {
            Close("disposed");
        }
    }
}
