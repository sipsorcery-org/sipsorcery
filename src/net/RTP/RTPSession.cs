//-----------------------------------------------------------------------------
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

        private Boolean m_acceptRtpFromAny = false;
        private string m_sdpSessionID = null;           // Need to maintain the same SDP session ID for all offers and answers.
        private int m_sdpAnnouncementVersion = 0;       // The SDP version needs to increase whenever the local SDP is modified (see https://tools.ietf.org/html/rfc6337#section-5.2.5).
        internal int m_rtpChannelsCount = 0;            // Need to know the number of RTP Channels

        // The stream used for the underlying RTP session to create a single RTP channel that will
        // be used to multiplex all required media streams. (see addSingleTrack())
        private MediaStream m_primaryStream; 

        protected RTPChannel MultiplexRtpChannel = null;

        private List<List<SDPSsrcAttribute>> audioRemoteSDPSsrcAttributes = new List<List<SDPSsrcAttribute>> ();
        private List<List<SDPSsrcAttribute>> videoRemoteSDPSsrcAttributes = new List<List<SDPSsrcAttribute>> ();

        /// <summary>
        /// Track if current remote description is invalid (used in Renegotiation logic)
        /// </summary>
        public virtual bool RequireRenegotiation { get; protected internal set; }

        /// <summary>
        /// The primary stream for this session - can be an AudioStream or a VideoStream
        /// </summary>
        public MediaStream PrimaryStream
        {
            get
            {
                return m_primaryStream;
            }
        }

        /// <summary>
        /// The primary Audio Stream for this session
        /// </summary>
        public AudioStream AudioStream
        { 
            get
            {
                if (AudioStreamList.Count > 0)
                {
                    return AudioStreamList[0];
                }
                return null;
            }
        }

        /// <summary>
        /// The primary Video Stream for this session
        /// </summary>
        public VideoStream VideoStream
        {
            get
            {
                if (VideoStreamList.Count > 0)
                {
                    return VideoStreamList[0];
                }
                return null;
            }
        }

        /// <summary>
        /// The primary local audio stream for this session. Will be null if we are not sending audio.
        /// </summary>
        public MediaStreamTrack AudioLocalTrack => AudioStream?.LocalTrack;

        /// <summary>
        /// The primary remote audio track for this session. Will be null if the remote party is not sending audio.
        /// </summary>
        public MediaStreamTrack AudioRemoteTrack => AudioStream?.RemoteTrack;

        /// <summary>
        /// The primary reporting session for the audio stream. Will be null if only video is being sent.
        /// </summary>
        public RTCPSession AudioRtcpSession => AudioStream?.RtcpSession;

        /// <summary>
        /// The primary Audio remote RTP end point this stream is sending media to.
        /// </summary>
        public IPEndPoint AudioDestinationEndPoint => AudioStream?.DestinationEndPoint;

        /// <summary>
        /// The primary Audio remote RTP control end point this stream is sending to RTCP reports for the media stream to.
        /// </summary>
        public IPEndPoint AudioControlDestinationEndPoint => AudioStream?.ControlDestinationEndPoint;

        /// <summary>
        /// The primary local video track for this session. Will be null if we are not sending video.
        /// </summary>
        public MediaStreamTrack VideoLocalTrack => VideoStream?.LocalTrack;

        /// <summary>
        /// The primary remote video track for this session. Will be null if the remote party is not sending video.
        /// </summary>
        public MediaStreamTrack VideoRemoteTrack => VideoStream?.RemoteTrack;

        /// <summary>
        /// The primary reporting session for the video stream. Will be null if only audio is being sent.
        /// </summary>
        public RTCPSession VideoRtcpSession => VideoStream?.RtcpSession;

        /// <summary>
        /// The primary Video remote RTP end point this stream is sending media to.
        /// </summary>
        public IPEndPoint VideoDestinationEndPoint => VideoStream?.DestinationEndPoint;

        /// <summary>
        /// The primary Video remote RTP control end point this stream is sending to RTCP reports for the media stream to.
        /// </summary>
        public IPEndPoint VideoControlDestinationEndPoint => VideoStream?.ControlDestinationEndPoint;

        /// <summary>
        /// List of all Audio Streams for this session
        /// </summary>
        public List<AudioStream> AudioStreamList { get; private set; } = new List<AudioStream>();

        /// <summary>
        /// List of all Video Streams for this session
        /// </summary>
        public List<VideoStream> VideoStreamList { get; private set; } = new List<VideoStream>();

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
                return m_acceptRtpFromAny;
            }

            set
            {
                m_acceptRtpFromAny = value;
                foreach (var audioStream in AudioStreamList)
                {
                    audioStream.AcceptRtpFromAny = value;
                }
                foreach (var videoStream in VideoStreamList)
                {
                    videoStream.AcceptRtpFromAny = value;
                }
            }
        }

        /// <summary>
        /// Set if the session has been bound to a specific IP address.
        /// Normally not required but some esoteric call or network set ups may need.
        /// </summary>
        public IPAddress RtpBindAddress => rtpSessionConfig.BindAddress;

        /// <summary>
        /// Gets fired when the remote SDP is received and the set of common audio formats is set. (on the primary one)
        /// </summary>
        public event Action<List<AudioFormat>> OnAudioFormatsNegotiated;

        /// <summary>
        /// Gets fired when the remote SDP is received and the set of common audio formats is set. (using its index)
        /// </summary>
        public event Action<int, List<AudioFormat>> OnAudioFormatsNegotiatedByIndex;

        /// <summary>
        /// Gets fired when the remote SDP is received and the set of common video formats is set. (on the primary one)
        /// </summary>
        public event Action<List<VideoFormat>> OnVideoFormatsNegotiated;

        /// <summary>
        /// Gets fired when the remote SDP is received and the set of common video formats is set. (using its index)
        /// </summary>
        public event Action<int, List<VideoFormat>> OnVideoFormatsNegotiatedByIndex;

        /// <summary>
        /// Gets fired when a full video frame is reconstructed from one or more RTP packets
        /// received from the remote party. (on the primary one)
        /// </summary>
        /// <remarks>
        ///  - Received from end point,
        ///  - The frame timestamp,
        ///  - The encoded video frame payload.
        ///  - The video format of the encoded frame.
        /// </remarks>
        public event Action<IPEndPoint, uint, byte[], VideoFormat> OnVideoFrameReceived;

        /// <summary>
        /// Gets fired when a full video frame is reconstructed from one or more RTP packets
        /// received from the remote party. (using its index)
        /// </summary>
        /// <remarks>
        ///  - Index of the VideoStream
        ///  - Received from end point,
        ///  - The frame timestamp,
        ///  - The encoded video frame payload.
        ///  - The video format of the encoded frame.
        /// </remarks>
        public event Action<int, IPEndPoint, uint, byte[], VideoFormat> OnVideoFrameReceivedByIndex;

        /// <summary>
        /// Gets fired when an RTP packet is received from a remote party. (on the primary one)
        /// Parameters are:
        ///  - Remote endpoint packet was received from,
        ///  - The media type the packet contains, will be audio or video,
        ///  - The full RTP packet.
        /// </summary>
        public event Action<IPEndPoint, SDPMediaTypesEnum, RTPPacket> OnRtpPacketReceived;

        /// <summary>
        /// Gets fired when an RTP packet is received from a remote party (using its index).
        /// Parameters are:
        ///  - index of the AudioStream or VideoStream
        ///  - Remote endpoint packet was received from,
        ///  - The media type the packet contains, will be audio or video,
        ///  - The full RTP packet.
        /// </summary>
        public event Action<int, IPEndPoint, SDPMediaTypesEnum, RTPPacket> OnRtpPacketReceivedByIndex;

        /// <summary>
        /// Gets fired when an RTP event is detected on the remote call party's RTP stream (on the primary one).
        /// </summary>
        public event Action<IPEndPoint, RTPEvent, RTPHeader> OnRtpEvent;

        /// <summary>
        /// Gets fired when an RTP event is detected on the remote call party's RTP stream (using its index).
        /// </summary>
        public event Action<int, IPEndPoint, RTPEvent, RTPHeader> OnRtpEventByIndex;

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
        /// Fires when the connection for a media type (the primary one) is classified as timed out due to not
        /// receiving any RTP or RTCP packets within the given period.
        /// </summary>
        public event Action<SDPMediaTypesEnum> OnTimeout;

        /// <summary>
        /// Fires when the connection for a media type (using its index) is classified as timed out due to not
        /// receiving any RTP or RTCP packets within the given period.
        /// </summary>
        public event Action<int, SDPMediaTypesEnum> OnTimeoutByIndex;

        /// <summary>
        /// Gets fired when an RTCP report is received (the primary one). This event is for diagnostics only.
        /// </summary>
        public event Action<IPEndPoint, SDPMediaTypesEnum, RTCPCompoundPacket> OnReceiveReport;

        /// <summary>
        /// Gets fired when an RTCP report is received (using its index). This event is for diagnostics only.
        /// </summary>
        public event Action<int, IPEndPoint, SDPMediaTypesEnum, RTCPCompoundPacket> OnReceiveReportByIndex;

        /// <summary>
        /// Gets fired when an RTCP report is sent (the primary one). This event is for diagnostics only.
        /// </summary>
        public event Action<SDPMediaTypesEnum, RTCPCompoundPacket> OnSendReport;

        /// <summary>
        /// Gets fired when an RTCP report is sent (using its nidex). This event is for diagnostics only.
        /// </summary>
        public event Action<int, SDPMediaTypesEnum, RTCPCompoundPacket> OnSendReportByIndex;

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
        }


        protected void ResetRemoteSDPSsrcAttributes()
        {
            audioRemoteSDPSsrcAttributes.Clear();
            videoRemoteSDPSsrcAttributes.Clear();
        }

        protected void AddRemoteSDPSsrcAttributes(SDPMediaTypesEnum mediaType, List<SDPSsrcAttribute> sdpSsrcAttributes)
        {
            if(mediaType == SDPMediaTypesEnum.audio)
            {
                audioRemoteSDPSsrcAttributes.Add(sdpSsrcAttributes);
            }
            else if (mediaType == SDPMediaTypesEnum.video)
            {
                videoRemoteSDPSsrcAttributes.Add(sdpSsrcAttributes);
            }
        }

        protected void LogRemoteSDPSsrcAttributes()
        {
            string str = "Audio:[ ";
            foreach(var audioRemoteSDPSsrcAttribute in audioRemoteSDPSsrcAttributes)
            {
                foreach(var attr in audioRemoteSDPSsrcAttribute)
                {
                    str += attr.SSRC + " - ";
                }
            }
            str += "] \r\n Video: [ ";
            foreach (var videoRemoteSDPSsrcAttribute in videoRemoteSDPSsrcAttributes)
            {
                str += " [";
                foreach (var attr in videoRemoteSDPSsrcAttribute)
                {
                    str += attr.SSRC + " - ";
                }
                str += "] ";
            }
            str += " ]";
            logger.LogDebug($"LogRemoteSDPSsrcAttributes: {str}");
        }

        private void CreateRtcpSession(MediaStream mediaStream)
        {
            if (mediaStream.CreateRtcpSession())
            {
                mediaStream.OnTimeoutByIndex += RaiseOnTimeOut;
                mediaStream.OnSendReportByIndex += RaiseOnSendReport;
                mediaStream.OnRtpEventByIndex += RaisedOnRtpEvent;
                mediaStream.OnRtpPacketReceivedByIndex += RaisedOnRtpPacketReceived;
                mediaStream.OnReceiveReportByIndex += RaisedOnOnReceiveReport;

                if (mediaStream.MediaType == SDPMediaTypesEnum.audio)
                {
                    var audioStream = mediaStream as AudioStream;
                    if (audioStream != null)
                    {
                        audioStream.OnAudioFormatsNegotiatedByIndex += RaisedOnAudioFormatsNegotiated;
                    }
                }
                else
                {
                    var videoStream = mediaStream as VideoStream;
                    if (videoStream != null)
                    {
                        videoStream.OnVideoFormatsNegotiatedByIndex += RaisedOnVideoFormatsNegotiated;
                        videoStream.OnVideoFrameReceivedByIndex += RaisedOnOnVideoFrameReceived;
                    }
                }
            }
        }

        private void CloseRtcpSession(MediaStream mediaStream, string reason)
        {
            if (mediaStream.RtcpSession != null)
            {
                mediaStream.OnTimeoutByIndex -= RaiseOnTimeOut;
                mediaStream.OnSendReportByIndex -= RaiseOnSendReport;
                mediaStream.OnRtpEventByIndex -= RaisedOnRtpEvent;
                mediaStream.OnRtpPacketReceivedByIndex -= RaisedOnRtpPacketReceived;
                mediaStream.OnReceiveReportByIndex -= RaisedOnOnReceiveReport;

                if (mediaStream.MediaType == SDPMediaTypesEnum.audio)
                {
                    var audioStream = mediaStream as AudioStream;
                    if (audioStream != null)
                    {
                        audioStream.OnAudioFormatsNegotiatedByIndex -= RaisedOnAudioFormatsNegotiated;
                    }
                }
                else
                {
                    var videoStream = mediaStream as VideoStream;
                    if (videoStream != null)
                    {
                        videoStream.OnVideoFormatsNegotiatedByIndex -= RaisedOnVideoFormatsNegotiated;
                        videoStream.OnVideoFrameReceivedByIndex -= RaisedOnOnVideoFrameReceived;
                    }
                }

                mediaStream.RtcpSession.Close(reason);
                mediaStream.RtcpSession = null;
            }
        }
        
        private void RaiseOnTimeOut(int index, SDPMediaTypesEnum media)
        {
            if (index == 0)
            {
                OnTimeout?.Invoke(media);
            }
            OnTimeoutByIndex?.Invoke(index, media);
        }

        private void RaiseOnSendReport(int index, SDPMediaTypesEnum media, RTCPCompoundPacket report)
        {
            if (index == 0)
            {
                OnSendReport?.Invoke(media, report);
            }
            OnSendReportByIndex?.Invoke(index, media, report);
        }

        private void RaisedOnRtpEvent(int index, IPEndPoint ipEndPoint, RTPEvent rtpEvent, RTPHeader rtpHeader)
        {
            if (index == 0)
            {
                OnRtpEvent?.Invoke(ipEndPoint, rtpEvent, rtpHeader);
            }
            OnRtpEventByIndex?.Invoke(index, ipEndPoint, rtpEvent, rtpHeader);
        }

        private void RaisedOnRtpPacketReceived(int index, IPEndPoint ipEndPoint, SDPMediaTypesEnum media, RTPPacket rtpPacket)
        {
            if (index == 0)
            {
                OnRtpPacketReceived?.Invoke(ipEndPoint, media, rtpPacket);
            }
            OnRtpPacketReceivedByIndex?.Invoke(index, ipEndPoint, media, rtpPacket);
        }

        private void RaisedOnOnReceiveReport(int index, IPEndPoint ipEndPoint, SDPMediaTypesEnum media, RTCPCompoundPacket report)
        {
            if (index == 0)
            {
                OnReceiveReport?.Invoke(ipEndPoint, media, report);
            }
            OnReceiveReportByIndex?.Invoke(index, ipEndPoint, media, report);
        }

        private void RaisedOnAudioFormatsNegotiated(int index, List<AudioFormat> audioFormats)
        {
            if (index == 0)
            {
                OnAudioFormatsNegotiated?.Invoke(audioFormats);
            }
            OnAudioFormatsNegotiatedByIndex?.Invoke(index, audioFormats);
        }

        private void RaisedOnVideoFormatsNegotiated(int index, List<VideoFormat> videoFormats)
        {
            if (index == 0)
            {
                OnVideoFormatsNegotiated?.Invoke(videoFormats);
            }
            OnVideoFormatsNegotiatedByIndex?.Invoke(index, videoFormats);
        }

        private void RaisedOnOnVideoFrameReceived(int index, IPEndPoint ipEndPoint, uint timestamp, byte[] frame, VideoFormat videoFormat)
        {
            if (index == 0)
            {
                OnVideoFrameReceived?.Invoke(ipEndPoint, timestamp, frame, videoFormat);
            }
            OnVideoFrameReceivedByIndex?.Invoke(index, ipEndPoint, timestamp, frame, videoFormat);
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
            if ( ((AudioStream == null) || (AudioStream.LocalTrack == null)) && ((VideoStream == null) || (VideoStream.LocalTrack == null)) )
            {
                logger.LogWarning("No local media tracks available for create offer.");
                return null;
            }
            else
            {
                List<MediaStream> mediaStreams = GetMediaStreams();

                //Revert to DefaultStreamStatus
                foreach (var mediaStream in mediaStreams)
                {
                    if (mediaStream.LocalTrack != null && mediaStream.LocalTrack.StreamStatus == MediaStreamStatusEnum.Inactive)
                    {
                        mediaStream.LocalTrack.StreamStatus = mediaStream.LocalTrack.DefaultStreamStatus;
                    }
                }

                return GetSessionDescription(mediaStreams, connectionAddress);
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

                int currentAudioStreamCount = 0;
                int currentVideoStreamCount = 0;
                MediaStream currentMediaStream;

                List<MediaStream> mediaStreams = new List<MediaStream>();

                // The order of the announcements in the answer must match the order in the offer.
                foreach (var announcement in offer.Media)
                {
                    currentMediaStream = null;
                    // Adjust the local audio tracks to only include compatible capabilities.
                    if (announcement.Media == SDPMediaTypesEnum.audio)
                    {
                        currentMediaStream = GetOrCreateAudioStream(currentAudioStreamCount++);
                    }
                    else if (announcement.Media == SDPMediaTypesEnum.video)
                    {
                        currentMediaStream = GetOrCreateVideoStream(currentVideoStreamCount++);
                    }

                    if (currentMediaStream != null && currentMediaStream.LocalTrack != null)
                    {
                        mediaStreams.Add(currentMediaStream);
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

                return GetSessionDescription(mediaStreams, connectionAddress);
            }
        }

        private AudioStream GetOrCreateAudioStream(int index)
        {
            if (index < AudioStreamList.Count)
            {
                // We ask too fast a new AudioStram ...
                return AudioStreamList[index];
            }
            else if (index == AudioStreamList.Count)
            {
                AudioStream audioStream = new AudioStream(rtpSessionConfig, index);
                AudioStreamList.Add(audioStream);
                return audioStream;
            }
            return null;
        }

        private VideoStream GetOrCreateVideoStream(int index)
        {
            if (index < VideoStreamList.Count)
            {
                // We ask too fast a new AudioStram ...
                return VideoStreamList[index];
            }
            else if (index == VideoStreamList.Count)
            {
                VideoStream videoStream = new VideoStream(rtpSessionConfig, index);
                VideoStreamList.Add(videoStream);
                return videoStream;
            }
            return null;
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
                    if (remoteMediaType == SDPMediaTypesEnum.audio && ( (AudioStream == null) || (AudioStream.LocalTrack == null) ) )
                    {
                        return SetDescriptionResultEnum.NoMatchingMediaType;
                    }
                    else if (remoteMediaType == SDPMediaTypesEnum.video && ((VideoStream == null) || (VideoStream.LocalTrack == null)))
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


                //Remove Remote Tracks before add new one (this was added to implement renegotiation logic)
                foreach (var audioStream in AudioStreamList)
                {
                    audioStream.RemoteTrack = null;
                }

                foreach (var videoStream in VideoStreamList)
                {
                    videoStream.RemoteTrack = null;
                }

                int currentAudioStreamCount = 0;
                int currentVideoStreamCount = 0;
                MediaStream currentMediaStream;

                foreach (var announcement in sessionDescription.Media.Where(x => x.Media == SDPMediaTypesEnum.audio || x.Media == SDPMediaTypesEnum.video))
                {
                    if (announcement.Media == SDPMediaTypesEnum.audio)
                    {
                        currentMediaStream = GetOrCreateAudioStream(currentAudioStreamCount++);
                        if (currentMediaStream == null)
                        {
                            return SetDescriptionResultEnum.Error;
                        }
                    }
                    else
                    {
                        currentMediaStream = GetOrCreateVideoStream(currentVideoStreamCount++);
                        if (currentMediaStream == null)
                        {
                            return SetDescriptionResultEnum.Error;
                        }
                    }

                    MediaStreamStatusEnum mediaStreamStatus = announcement.MediaStreamStatus.HasValue ? announcement.MediaStreamStatus.Value : MediaStreamStatusEnum.SendRecv;
                    var remoteTrack = new MediaStreamTrack(announcement.Media, true, announcement.MediaFormats.Values.ToList(), mediaStreamStatus, announcement.SsrcAttributes, announcement.HeaderExtensions);

                    currentMediaStream.RemoteTrack = remoteTrack;

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
                            var srtpHandler = currentMediaStream.GetOrCreateSrtpHandler();
                            if (!srtpHandler.SetupRemote(announcement.SecurityDescriptions, sdpType))
                            {
                                logger.LogError($"Error negotiating secure media for type {mediaType}. Incompatible crypto parameter.");
                                return SetDescriptionResultEnum.CryptoNegotiationFailed;
                            }

                            if (srtpHandler.IsNegotiationComplete)
                            {
                                currentMediaStream.SetSecurityContext(srtpHandler.ProtectRTP, srtpHandler.UnprotectRTP, srtpHandler.ProtectRTCP, srtpHandler.UnprotectRTCP);
                            }
                        }
                        // If we had no crypto but we were definetely expecting something since we had a port value
                        else if (announcement.Port != 0)
                        {
                            logger.LogError("Error negotiating secure media. No compatible crypto suite.");
                            return SetDescriptionResultEnum.CryptoNegotiationFailed;
                        }
                    }

                    if (currentMediaStream.LocalTrack == null)
                    {
                        var inactiveLocalTrack = new MediaStreamTrack(currentMediaStream.MediaType, false, remoteTrack.Capabilities, MediaStreamStatusEnum.Inactive);
                        currentMediaStream.LocalTrack = inactiveLocalTrack;
                    }
                    else
                    {
                        currentMediaStream.LocalTrack.Capabilities = SDPAudioVideoMediaFormat.GetCompatibleFormats(announcement.MediaFormats.Values.ToList(), currentMediaStream.LocalTrack?.Capabilities);
                        

                        if (currentMediaStream.MediaType == SDPMediaTypesEnum.audio)
                        {
                            // Check whether RTP events can be supported and adjust our parameters to match the remote party if we can.
                            SDPAudioVideoMediaFormat commonEventFormat = SDPAudioVideoMediaFormat.GetCommonRtpEventFormat(announcement.MediaFormats.Values.ToList(), currentMediaStream.LocalTrack.Capabilities);
                            if (!commonEventFormat.IsEmpty())
                            {
                                currentMediaStream.RemoteRtpEventPayloadID = commonEventFormat.ID;
                            }
                        }

                        IPEndPoint remoteRtpEP = GetAnnouncementRTPDestination(announcement, connectionAddress);
                        SetLocalTrackStreamStatus(currentMediaStream.LocalTrack, remoteTrack.StreamStatus, remoteRtpEP);
                        IPEndPoint remoteRtcpEP = null;
                        if (remoteTrack.StreamStatus != MediaStreamStatusEnum.Inactive && currentMediaStream.LocalTrack.StreamStatus != MediaStreamStatusEnum.Inactive)
                        {
                            remoteRtcpEP = (rtpSessionConfig.IsRtcpMultiplexed) ? remoteRtpEP : new IPEndPoint(remoteRtpEP.Address, remoteRtpEP.Port + 1);
                        }

                        currentMediaStream.DestinationEndPoint = (remoteRtpEP != null && remoteRtpEP.Port != SDP.IGNORE_RTP_PORT_NUMBER) ? remoteRtpEP : currentMediaStream.DestinationEndPoint;
                        currentMediaStream.ControlDestinationEndPoint = (remoteRtcpEP != null && remoteRtcpEP.Port != SDP.IGNORE_RTP_PORT_NUMBER) ? remoteRtcpEP : currentMediaStream.ControlDestinationEndPoint;
                    }

                    if (currentMediaStream.MediaType == SDPMediaTypesEnum.audio)
                    {
                        if (currentMediaStream.LocalTrack.Capabilities?.Where(x => x.Name().ToLower() != SDP.TELEPHONE_EVENT_ATTRIBUTE).Count() == 0)
                        {
                            return SetDescriptionResultEnum.AudioIncompatible;
                        }
                    }
                    else if (currentMediaStream.LocalTrack == null && currentMediaStream.LocalTrack != null && currentMediaStream.LocalTrack.Capabilities?.Count == 0)
                    {
                        return SetDescriptionResultEnum.VideoIncompatible;

                    }
                }

                //Close old RTCPSessions opened
                foreach (var audioStream in AudioStreamList)
                {
                    if (audioStream.RtcpSession != null && audioStream.RemoteTrack == null && audioStream.LocalTrack == null)
                    {
                        audioStream.RtcpSession.Close(null);
                    }
                }

                //Close old RTCPSessions opened
                foreach (var videoStream in VideoStreamList)
                {
                    if (videoStream.RtcpSession != null && videoStream.RemoteTrack == null && videoStream.LocalTrack == null)
                    {
                        videoStream.RtcpSession.Close(null);
                    }
                }

                foreach (var audioStream in AudioStreamList)
                {
                    audioStream.CheckAudioFormatsNegotiation();
                }

                foreach (var videoStream in VideoStreamList)
                {
                    videoStream.CheckVideoFormatsNegotiation();
                }

                // If we get to here then the remote description was compatible with the local media tracks.
                // Set the remote description and end points.
                RequireRenegotiation = false;
                RemoteDescription = sessionDescription;

                return SetDescriptionResultEnum.OK;
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception in RTPSession SetRemoteDescription. {excp.Message}.");
                return SetDescriptionResultEnum.Error;
            }
        }

        /// <summary>
        /// Sets the stream status on the primary local audio or primary video media track.
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
            else if (kind == SDPMediaTypesEnum.video && VideoStream?.LocalTrack != null)
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
        protected void addSingleTrack(Boolean videoAsPrimary)
        {
            if (videoAsPrimary)
            {
                m_primaryStream = GetNextVideoStreamByLocalTrack();
            }
            else
            {
                m_primaryStream = GetNextAudioStreamByLocalTrack();
            }

            InitMediaStream(m_primaryStream);
        }

        private void InitMediaStream(MediaStream currentMediaStream)
        {
            var rtpChannel = CreateRtpChannel();
            currentMediaStream.AddRtpChannel(rtpChannel);
            CreateRtcpSession(currentMediaStream);
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
            // TODO - CI - Do we need to do something else ? How to remove an Audio/Video Stream ?

            if (track == null)
            {
                return false;
            }

            if (track.Kind == SDPMediaTypesEnum.audio)
            {
                foreach(var audioStream in AudioStreamList)
                {
                    if(audioStream.LocalTrack == track)
                    {
                        RequireRenegotiation = true;
                        audioStream.LocalTrack = null;
                        return true;
                    }
                }
            } else if (track.Kind == SDPMediaTypesEnum.video)
            {
                foreach (var videoStream in VideoStreamList)
                {
                    if (videoStream.LocalTrack == track)
                    {
                        RequireRenegotiation = true;
                        videoStream.LocalTrack = null;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Removes a remote media stream to this session.
        /// </summary>
        /// <param name="track">The remote track to remove.</param>
        private bool RemoveRemoteTrack(MediaStreamTrack track)
        {
            // TODO - CI - Do we need to do something else ? How to remove an Audio/Video Stream ?
            if (track == null)
            {
                return false;
            }

            if (track.Kind == SDPMediaTypesEnum.audio)
            {
                AudioStream audioStream = null;

                foreach (var checkAudioStream in AudioStreamList)
                {
                    if (checkAudioStream.RemoteTrack == track)
                    {
                        RequireRenegotiation = true;
                        checkAudioStream.RemoteTrack = null;
                        audioStream = checkAudioStream;
                        break;
                    }
                }

                if(audioStream != null)
                {
                    //if ( (audioStream.LocalTrack == null) && (audioStream.RemoteTrack == null) )
                    //{
                    //    AudioStreamList.Remove(audioStream);
                    //}
                    return true;
                }

            }
            else if (track.Kind == SDPMediaTypesEnum.video)
            {
                VideoStream videoStream = null;
                foreach (var checkVideoStream in VideoStreamList)
                {
                    if (checkVideoStream.RemoteTrack == track)
                    {
                        RequireRenegotiation = true;
                        checkVideoStream.RemoteTrack = null;
                        videoStream = checkVideoStream;
                        break;
                    }
                }

                if (videoStream != null)
                {
                    //if ( (videoStream.LocalTrack == null) && (videoStream.RemoteTrack == null) )
                    //{
                    //    VideoStreamList.Remove(videoStream);
                    //}
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Adds a local media stream to this session. Local media tracks should be added by the
        /// application to control what session description offers and answers can be made as
        /// well as being used to match up with remote tracks.
        /// </summary>
        /// <param name="track">The local track to add.</param>
        private void AddLocalTrack(MediaStreamTrack track)
        {
            MediaStream currentMediaStream;
            if (track.Kind == SDPMediaTypesEnum.audio)
            {
                currentMediaStream = GetNextAudioStreamByLocalTrack();
            }
            else if (track.Kind == SDPMediaTypesEnum.video)
            {
                currentMediaStream = GetNextVideoStreamByLocalTrack();
            }
            else
            {
                return;
            }

            if (track.StreamStatus == MediaStreamStatusEnum.Inactive)
            {
                // Inactive tracks don't use/require any local resources. Instead they are place holders
                // so that the session description offers/answers can be balanced with the remote party.
                // For example if the remote party offers audio and video but we only support audio we
                // can reject the call or we can accept the audio and answer with an inactive video
                // announcement.
                RequireRenegotiation = true;
                currentMediaStream.LocalTrack = track;
            }
            else
            {
                RequireRenegotiation = true;

                InitMediaStream(currentMediaStream);
                currentMediaStream.LocalTrack = track;
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
            MediaStream currentMediaStream;
            if (track.Kind == SDPMediaTypesEnum.audio)
            {
                currentMediaStream = GetNextAudioStreamByRemoteTrack();
            }
            else if (track.Kind == SDPMediaTypesEnum.video)
            {
                currentMediaStream = GetNextVideoStreamByRemoteTrack();
            }
            else
            {
                return;
            }

            RequireRenegotiation = true;
            currentMediaStream.RemoteTrack = track;

            // Even if there's no local audio/video track an RTCP session can still be required 
            // in case the remote party send reports (presumably in case we decide we do want
            // to send or receive audio on this session at some later stage).
            CreateRtcpSession(currentMediaStream);
            
        }

        protected void SetGlobalDestination(IPEndPoint rtpEndPoint, IPEndPoint rtcpEndPoint)
        {
            foreach (var audioStream in AudioStreamList)
            {
                audioStream.SetDestination(rtpEndPoint, rtcpEndPoint);
            }

            foreach (var videoStream in VideoStreamList)
            {
                videoStream.SetDestination(rtpEndPoint, rtcpEndPoint);
            }
        }

        protected void SetGlobalSecurityContext(ProtectRtpPacket protectRtp, ProtectRtpPacket unprotectRtp, ProtectRtpPacket protectRtcp, ProtectRtpPacket unprotectRtcp)
        {
            foreach(var audioStream in AudioStreamList)
            {
                audioStream.SetSecurityContext(protectRtp, unprotectRtp, protectRtcp, unprotectRtcp);
            }

            foreach (var videoStream in VideoStreamList)
            {
                videoStream.SetSecurityContext(protectRtp, unprotectRtp, protectRtcp, unprotectRtcp);
            }
        }

        private void InitIPEndPointAndSecurityContext(MediaStream mediaStream)
        {
            // Get primary AudioStream
            if ( (m_primaryStream != null) && (mediaStream != null) )
            {
                var secureContext = m_primaryStream.GetSecurityContext();
                if (secureContext != null)
                {
                    mediaStream.SetSecurityContext(secureContext.ProtectRtpPacket, secureContext.UnprotectRtpPacket, secureContext.ProtectRtcpPacket, secureContext.UnprotectRtcpPacket);
                }
                mediaStream.SetDestination(m_primaryStream.DestinationEndPoint, m_primaryStream.ControlDestinationEndPoint);
            }
        }

        private AudioStream GetNextAudioStreamByLocalTrack()
        {
            int index = AudioStreamList.Count;
            if (index > 0)
            {
                foreach(var audioStream in AudioStreamList)
                {
                    if (audioStream.LocalTrack == null)
                    {
                        return audioStream;
                    }
                }
            }

            // We need to create new AudioStream
            var newAudioStream = new AudioStream(rtpSessionConfig, index);
            newAudioStream.AcceptRtpFromAny = AcceptRtpFromAny;

            // If it's not the first one we need to init it
            if (index != 0)
            {
                InitIPEndPointAndSecurityContext(newAudioStream);
            }

            AudioStreamList.Add(newAudioStream);
            return newAudioStream;
        }

        private AudioStream GetNextAudioStreamByRemoteTrack()
        {
            int index = AudioStreamList.Count;
            if (index > 0)
            {
                foreach (var audioStream in AudioStreamList)
                {
                    if (audioStream.RemoteTrack == null)
                    {
                        return audioStream;
                    }
                }
            }

            // We need to create new AudioStream
            var newAudioStream = new AudioStream(rtpSessionConfig, index);
            newAudioStream.AcceptRtpFromAny = AcceptRtpFromAny;

            // If it's not the first one we need to init it
            if (index != 0)
            {
                InitIPEndPointAndSecurityContext(newAudioStream);
            }

            AudioStreamList.Add(newAudioStream);
            return newAudioStream;
        }

        private VideoStream GetNextVideoStreamByLocalTrack()
        {
            int index = VideoStreamList.Count;
            if (index > 0)
            {
                foreach (var videoStream in VideoStreamList)
                {
                    if (videoStream.LocalTrack == null)
                    {
                        return videoStream;
                    }
                }
            }

            // We need to create new VideoStream and Init it
            var newVideoStream = new VideoStream(rtpSessionConfig, index);
            newVideoStream.AcceptRtpFromAny = AcceptRtpFromAny;

            InitIPEndPointAndSecurityContext(newVideoStream);
            VideoStreamList.Add(newVideoStream);
            return newVideoStream;
        }

        private VideoStream GetNextVideoStreamByRemoteTrack()
        {
            int index = VideoStreamList.Count;
            if (index > 0)
            {
                foreach (var videoStream in VideoStreamList)
                {
                    if (videoStream.RemoteTrack == null)
                    {
                        return videoStream;
                    }
                }
            }

            // We need to create new VideoStream and Init it
            var newVideoStream = new VideoStream(rtpSessionConfig, index);
            newVideoStream.AcceptRtpFromAny = AcceptRtpFromAny;

            InitIPEndPointAndSecurityContext(newVideoStream);
            VideoStreamList.Add(newVideoStream);
            return newVideoStream;
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
        /// Generates a session description from the provided list of MediaStream.
        /// </summary>
        /// <param name="tracks">The list of tracks to generate the session description for.</param>
        /// <param name="connectionAddress">Optional. If set this address will be used as 
        /// the SDP Connection address. If not specified the Internet facing address will
        /// be used. IPAddress.Any and IPAddress. Any and IPv6Any are special cases. If they are set the respective
        /// Internet facing IPv4 or IPv6 address will be used.</param>
        /// <returns>A session description payload.</returns>
        private SDP GetSessionDescription(List<MediaStream> mediaStreamList, IPAddress connectionAddress)
        {
            IPAddress localAddress = connectionAddress;

            if (localAddress == null || localAddress == IPAddress.Any || localAddress == IPAddress.IPv6Any)
            {
                if (rtpSessionConfig.BindAddress != null)
                {
                    localAddress = rtpSessionConfig.BindAddress;
                }
                else
                {
                    localAddress = null;
                    foreach (var audioStream in AudioStreamList)
                    {
                        if (audioStream.DestinationEndPoint != null && audioStream.DestinationEndPoint.Address != null)
                        {
                            if (IPAddress.Any.Equals(audioStream.DestinationEndPoint.Address) || IPAddress.IPv6Any.Equals(audioStream.DestinationEndPoint.Address))
                            {
                                // If the remote party has set an inactive media stream via the connection address then we do the same.
                                localAddress = audioStream.DestinationEndPoint.Address;
                            }
                            else
                            {
                                localAddress = NetServices.GetLocalAddressForRemote(audioStream.DestinationEndPoint.Address);
                            }
                        }
                    }

                    if (localAddress == null)
                    {
                        foreach (var videoStream in VideoStreamList)
                        {
                            if (videoStream.DestinationEndPoint != null && videoStream.DestinationEndPoint.Address != null)
                            {
                                if (IPAddress.Any.Equals(videoStream.DestinationEndPoint.Address) || IPAddress.IPv6Any.Equals(videoStream.DestinationEndPoint.Address))
                                {
                                    // If the remote party has set an inactive media stream via the connection address then we do the same.
                                    localAddress = videoStream.DestinationEndPoint.Address;
                                }
                                else
                                {
                                    localAddress = NetServices.GetLocalAddressForRemote(videoStream.DestinationEndPoint.Address);
                                }
                            }
                        }
                    }

                    if (localAddress == null)
                    {
                        if (connectionAddress == IPAddress.IPv6Any && NetServices.InternetDefaultIPv6Address != null)
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
            }

            SDP sdp = new SDP(IPAddress.Loopback);
            sdp.SessionId = m_sdpSessionID;
            sdp.AnnouncementVersion = m_sdpAnnouncementVersion;

            sdp.Connection = new SDPConnectionInformation(localAddress);

            int mediaIndex = 0;
            int audioMediaIndex = 0;
            int videoMediaIndex = 0;

            foreach (var mediaStream in mediaStreamList)
            {
                int mindex = 0;
                string midTag = "0";

                if (RemoteDescription == null)
                {
                    mindex = mediaIndex;
                    midTag = mediaIndex.ToString();
                }
                else
                {
                    if (mediaStream.LocalTrack.Kind == SDPMediaTypesEnum.audio)
                    {
                        (mindex, midTag) = RemoteDescription.GetIndexForMediaType(mediaStream.LocalTrack.Kind, audioMediaIndex);
                        audioMediaIndex++;
                    }
                    else if (mediaStream.LocalTrack.Kind == SDPMediaTypesEnum.video)
                    {
                        (mindex, midTag) = RemoteDescription.GetIndexForMediaType(mediaStream.LocalTrack.Kind, videoMediaIndex);
                        videoMediaIndex++;
                    }
                }
                mediaIndex++;

                int rtpPort = 0; // A port of zero means the media type is not supported.
                if (mediaStream.LocalTrack.Capabilities != null && mediaStream.LocalTrack.Capabilities.Count() > 0 && mediaStream.LocalTrack.StreamStatus != MediaStreamStatusEnum.Inactive)
                {
                    if (rtpSessionConfig.IsMediaMultiplexed)
                    {
                        rtpPort = m_primaryStream.GetRTPChannel().RTPPort;
                    }
                    else 
                    {
                        rtpPort = mediaStream.GetRTPChannel().RTPPort;
                    }
                    
                }

                SDPMediaAnnouncement announcement = new SDPMediaAnnouncement(mediaStream.LocalTrack.Kind, rtpPort, mediaStream.LocalTrack.Capabilities);

                announcement.Transport = rtpSessionConfig.UseSdpCryptoNegotiation ? RTP_SECUREMEDIA_PROFILE : RTP_MEDIA_PROFILE;
                announcement.MediaStreamStatus = mediaStream.LocalTrack.StreamStatus;
                announcement.MediaID = midTag;
                announcement.MLineIndex = mindex;

                if (mediaStream.LocalTrack.MaximumBandwidth > 0)
                {
                    announcement.TIASBandwidth = mediaStream.LocalTrack.MaximumBandwidth;
                }

                if (mediaStream.LocalTrack.Ssrc != 0)
                {
                    string trackCname = mediaStream.RtcpSession?.Cname;

                    if (trackCname != null)
                    {
                        announcement.SsrcAttributes.Add(new SDPSsrcAttribute(mediaStream.LocalTrack.Ssrc, trackCname, null));
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

                    var handler = mediaStream.GetOrCreateSrtpHandler();
                    handler.SetupLocal(announcement.SecurityDescriptions, sdpType);

                    if (handler.IsNegotiationComplete)
                    {
                        mediaStream.SetSecurityContext(handler.ProtectRTP, handler.UnprotectRTP, handler.ProtectRTCP, handler.UnprotectRTCP);
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
        /// Gets the media streams available in this session. Will only be audio, video or both.
        /// media streams represent an audio or video source that we are sending to the remote party.
        /// </summary>
        /// <returns>A list of the local tracks that have been added to this session.</returns>
        protected List<MediaStream> GetMediaStreams()
        {

            List<MediaStream> mediaStream = new List<MediaStream>();

            foreach (var audioStream in AudioStreamList)
            {
                if (audioStream.LocalTrack != null)
                {
                    mediaStream.Add(audioStream);
                }
                else if (audioStream.RtcpSession != null && !audioStream.RtcpSession.IsClosed && audioStream.RemoteTrack != null)
                {
                    var inactiveAudioTrack = new MediaStreamTrack(audioStream.MediaType, false, audioStream.RemoteTrack.Capabilities, MediaStreamStatusEnum.Inactive);
                    audioStream.LocalTrack = inactiveAudioTrack;
                    mediaStream.Add(audioStream);
                }
            }

            foreach (var videoStream in VideoStreamList)
            {
                if (videoStream.LocalTrack != null)
                {
                    mediaStream.Add(videoStream);
                }
                else if (videoStream.RtcpSession != null && !videoStream.RtcpSession.IsClosed && videoStream.RemoteTrack != null)
                {
                    var inactiveAudioTrack = new MediaStreamTrack(videoStream.MediaType, false, videoStream.RemoteTrack.Capabilities, MediaStreamStatusEnum.Inactive);
                    videoStream.LocalTrack = inactiveAudioTrack;
                    mediaStream.Add(videoStream);
                }
            }

            return mediaStream;
        }

        /// <summary>
        /// Starts the RTCP session(s) that monitor this RTP session.
        /// </summary>
        public virtual Task Start()
        {
            if (!IsStarted)
            {
                IsStarted = true;


                foreach(var audioStream in AudioStreamList)
                {
                    if(audioStream.HasAudio && audioStream.RtcpSession != null && audioStream.LocalTrack.StreamStatus != MediaStreamStatusEnum.Inactive)
                    {
                        // The local audio track may have been disabled if there were no matching capabilities with
                        // the remote party.
                        audioStream.RtcpSession.Start();
                    }
                }

                foreach (var videoStream in VideoStreamList)
                {
                    if (videoStream.HasVideo && videoStream.RtcpSession != null && videoStream.LocalTrack.StreamStatus != MediaStreamStatusEnum.Inactive)
                    {
                        // The local video track may have been disabled if there were no matching capabilities with
                        // the remote party.
                        videoStream.RtcpSession.Start();
                    }
                }

                OnStarted?.Invoke();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Sends an audio sample to the remote peer. (on the primary one)
        /// </summary>
        /// <param name="durationRtpUnits">The duration in RTP timestamp units of the audio sample. This
        /// value is added to the previous RTP timestamp when building the RTP header.</param>
        /// <param name="sample">The audio sample to set as the RTP packet payload.</param>
        public void SendAudio(uint durationRtpUnits, byte[] sample)
        {
            AudioStream?.SendAudio(durationRtpUnits, sample);
        }

        /// <summary>
        /// Sends a video sample to the remote peer. (on the primary one)
        /// </summary>
        /// <param name="durationRtpUnits">The duration in RTP timestamp units of the video sample. This
        /// value is added to the previous RTP timestamp when building the RTP header.</param>
        /// <param name="sample">The video sample to set as the RTP packet payload.</param>
        public void SendVideo(uint durationRtpUnits, byte[] sample)
        {
            VideoStream?.SendVideo(durationRtpUnits, sample);
        }

        /// <summary>
        /// Sends a DTMF toneas an RTP event to the remote party. (on the primary one)
        /// </summary>
        /// <param name="key">The DTMF tone to send.</param>
        /// <param name="ct">RTP events can span multiple RTP packets. This token can
        /// be used to cancel the send.</param>
        public virtual Task SendDtmf(byte key, CancellationToken ct)
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


                foreach(var audioStream in AudioStreamList)
                {
                    if (audioStream != null)
                    {
                        audioStream.IsClosed = true;
                        CloseRtcpSession(audioStream, reason);

                        if (audioStream.HasRtpChannel())
                        {
                            var rtpChannel = audioStream.GetRTPChannel();
                            rtpChannel.OnRTPDataReceived -= OnReceive;
                            rtpChannel.OnControlDataReceived -= OnReceive;
                            rtpChannel.OnClosed -= OnRTPChannelClosed;
                            rtpChannel.Close(reason);
                        }
                    }
                }

                foreach (var videoStream in VideoStreamList)
                {
                    if (videoStream != null)
                    {
                        videoStream.IsClosed = true;
                        CloseRtcpSession(videoStream, reason);

                        if (videoStream.HasRtpChannel())
                        {
                            var rtpChannel = videoStream.GetRTPChannel();
                            rtpChannel.OnRTPDataReceived -= OnReceive;
                            rtpChannel.OnControlDataReceived -= OnReceive;
                            rtpChannel.OnClosed -= OnRTPChannelClosed;
                            rtpChannel.Close(reason);
                        }
                    }
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
                    if (Enum.IsDefined(typeof(RTCPReportTypesEnum), buffer[1]))
                    {
                        // Only call OnReceiveRTCPPacket for SR and RR packets
                        if (buffer[1] == (byte)RTCPReportTypesEnum.SR || buffer[1] == (byte)RTCPReportTypesEnum.RR)
                        {
                            OnReceiveRTCPPacket(localPort, remoteEndPoint, buffer);
                        }
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
                //logger.LogWarning($"Could not find appropriate remote track for SSRC for RTCP packet - Ssrc:{ssrc}");
            }


            if(ssrc < 190)
            {
                return;
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
                        // We close peer connection only if there is no more local/remote tracks on the primary stream
                        if ((m_primaryStream.RemoteTrack == null) && (m_primaryStream.LocalTrack == null))
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
                        mediaStream.RaiseOnReceiveReportByIndex(remoteEndPoint, rtcpPkt);
                    }
                    else if (rtcpPkt.ReceiverReport?.SSRC == RTCP_RR_NOSTREAM_SSRC)
                    {
                        // Ignore for the time being. Not sure what use an empty RTCP Receiver Report can provide.
                    }
                    else if (AudioStream?.RtcpSession?.PacketsReceivedCount > 0 || VideoStream?.RtcpSession?.PacketsReceivedCount > 0)
                    {
                        // Only give this warning if we've received at least one RTP packet.
                        //logger.LogWarning("Could not match an RTCP packet against any SSRC's in the session.");
                        //logger.LogTrace(rtcpPkt.GetDebugSummary());
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

                MediaStream mediaStream = GetMediaStream(hdr.SyncSource);

                if ( (mediaStream == null) && (AudioStreamList.Count < 2) && (VideoStreamList.Count < 2) )
                {
                    mediaStream = GetMediaStreamFromPayloadType(hdr.PayloadType);
                }

                if (mediaStream == null)
                {
                    logger.LogWarning($"An RTP packet with SSRC {hdr.SyncSource} and payload ID {hdr.PayloadType} was received that could not be matched to an audio or video stream.");
                    return;
                }

                hdr.ReceivedTime = DateTime.Now;
                if (mediaStream.MediaType == SDPMediaTypesEnum.audio)
                {
                    mediaStream.OnReceiveRTPPacket(hdr, localPort, remoteEndPoint, buffer, null);
                }
                else if (mediaStream.MediaType == SDPMediaTypesEnum.video)
                {
                    mediaStream.OnReceiveRTPPacket(hdr, localPort, remoteEndPoint, buffer, mediaStream as VideoStream);
                }
            }
        }

        private MediaStreamTrack GetMediaStreamTrackFromPayloadType(int payloadId)
        {
            foreach(var audioStream in AudioStreamList)
            {
                if (audioStream.RemoteTrack != null && audioStream.RemoteTrack.IsPayloadIDMatch(payloadId))
                {
                    return audioStream.RemoteTrack;
                } 
                else if (audioStream.LocalTrack != null && audioStream.LocalTrack.IsPayloadIDMatch(payloadId))
                {
                    return audioStream.LocalTrack;
                }
            }

            foreach (var videoStream in VideoStreamList)
            {
                if (videoStream.RemoteTrack != null && videoStream.RemoteTrack.IsPayloadIDMatch(payloadId))
                {
                    return videoStream.RemoteTrack;
                }
                else if (videoStream.LocalTrack != null && videoStream.LocalTrack.IsPayloadIDMatch(payloadId))
                {
                    return videoStream.LocalTrack;
                }
            }

            return null;
        }

        private MediaStream GetMediaStreamFromPayloadType(int payloadId)
        {
            foreach (var audioStream in AudioStreamList)
            {
                if (audioStream.RemoteTrack != null && audioStream.RemoteTrack.IsPayloadIDMatch(payloadId))
                {
                    return audioStream;
                }
                else if (audioStream.LocalTrack != null && audioStream.LocalTrack.IsPayloadIDMatch(payloadId))
                {
                    return audioStream;
                }
            }

            foreach (var videoStream in VideoStreamList)
            {
                if (videoStream.RemoteTrack != null && videoStream.RemoteTrack.IsPayloadIDMatch(payloadId))
                {
                    return videoStream;
                }
                else if (videoStream.LocalTrack != null && videoStream.LocalTrack.IsPayloadIDMatch(payloadId))
                {
                    return videoStream;
                }
            }

            return null;
        }

        private MediaStream GetMediaStream(uint ssrc)
        {
            if (HasAudio)
            {
                if (!HasVideo)
                {
                    return AudioStream;
                }
            }
            else 
            {
                if (HasVideo)
                {
                    return VideoStream;
                }
            }

            foreach (var audioStream in AudioStreamList)
            {
                if (audioStream?.RemoteTrack?.IsSsrcMatch(ssrc) == true)
                {
                    return audioStream;
                } 
                else if (audioStream?.LocalTrack?.IsSsrcMatch(ssrc) == true)
                {
                    return audioStream;
                }
            }

            foreach (var videoStream in VideoStreamList)
            {
                if (videoStream?.RemoteTrack?.IsSsrcMatch(ssrc) == true)
                {
                    return videoStream;
                }
                else if (videoStream?.LocalTrack?.IsSsrcMatch(ssrc) == true)
                {
                    return videoStream;
                }
            }

            return GetMediaStreamRemoteSDPSsrcAttributes(ssrc);
        }

        private MediaStream GetMediaStreamRemoteSDPSsrcAttributes(uint ssrc)
        {
            if (ssrc < 200)
            {
                return null;
            }

            bool found = false;
            int index;

            // Loop au audioRemoteSDPSsrcAttributes 
            for (index = 0; index < audioRemoteSDPSsrcAttributes.Count; index++)
            {
                foreach (var ssrcAttributes in audioRemoteSDPSsrcAttributes[index])
                {
                    if (ssrcAttributes.SSRC == ssrc)
                    {
                        found = true;
                        break;
                    }
                }
                if (found)
                {
                    break;
                }
            }

            // Get related AudioStream if found
            if (found && (AudioStreamList.Count > index) )
            {
                var audioStream = AudioStreamList[index];
                //if (audioStream?.RemoteTrack != null)
                //{
                //    audioStream.RemoteTrack.Ssrc = ssrc;
                //}
                return audioStream;
            }

            // Loop au videoRemoteSDPSsrcAttributes 
            found = false;
            for (index = 0; index < videoRemoteSDPSsrcAttributes.Count; index++)
            {
                foreach (var ssrcAttributes in videoRemoteSDPSsrcAttributes[index])
                {
                    if (ssrcAttributes.SSRC == ssrc)
                    {
                        found = true;
                        break;
                    }
                }
                if (found)
                {
                    break;
                }
            }

            // Get related VideoStreamList if found
            if (found && (VideoStreamList.Count > index))
            {
                var videoStream = VideoStreamList[index];
                //if (videoStream?.RemoteTrack != null)
                //{
                //    videoStream.RemoteTrack.Ssrc = ssrc;
                //}
                return videoStream;
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
                    var mediaStream = GetMediaStream(recRep.SSRC);
                    if (mediaStream != null)
                    {
                        return mediaStream;
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
        /// Allows additional control for sending raw RTCP payloads (on the primary one).
        /// </summary>
        /// <param name="mediaType">The media type of the RTCP packet being sent. Must be audio or video.</param>
        /// <param name="payload">The RTCP packet payload.</param>
        public void SendRtcpRaw(SDPMediaTypesEnum mediaType, byte[] payload)
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                AudioStream.SendRtcpRaw(payload);
            }
            else if (mediaType == SDPMediaTypesEnum.video)
            {
                VideoStream?.SendRtcpRaw(payload);
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
                SetGlobalDestination(rtpEndPoint, rtcpEndPoint);
            }
            else
            {
                if (mediaType == SDPMediaTypesEnum.audio)
                {
                    AudioStream.SetDestination(rtpEndPoint, rtcpEndPoint);
                }
                else if (mediaType == SDPMediaTypesEnum.video)
                {
                    VideoStream?.SetDestination(rtpEndPoint, rtcpEndPoint);
                }
            }
        }

        /// <summary>
        /// Allows sending of RTCP feedback reports (on the primary one)
        /// </summary>
        /// <param name="mediaType">The media type of the RTCP report  being sent. Must be audio or video.</param>
        /// <param name="feedback">The feedback report to send.</param>
        public void SendRtcpFeedback(SDPMediaTypesEnum mediaType, RTCPFeedback feedback)
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                AudioStream.SendRtcpFeedback(feedback);
            }
            else if (mediaType == SDPMediaTypesEnum.video)
            {
                VideoStream?.SendRtcpFeedback(feedback);
            }
        }

        /// <summary>
        /// Sends the RTCP report to the remote call party. (on the primary one)
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
                VideoStream?.SendRtcpReport(report);
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
