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
// 25 Aug 2019	Aaron Clauson	Created, Montreux, Switzerland.
// 12 Nov 2019  Aaron Clauson   Added send event method.
// 07 Dec 2019  Aaron Clauson   Big refactor. Brought in a lot of functions previously
//                              in the RTPChannel class.
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
        private const int RTP_MAX_PAYLOAD = 1400;

        /// <summary>
        /// From libsrtp: SRTP_MAX_TRAILER_LEN is the maximum length of the SRTP trailer
        /// (authentication tag and MKI) supported by libSRTP.This value is
        /// the maximum number of octets that will be added to an RTP packet by
        /// srtp_protect().
        /// 
        /// srtp_protect():
        /// @warning This function assumes that it can write SRTP_MAX_TRAILER_LEN
        /// into the location in memory immediately following the RTP packet.
        /// Callers MUST ensure that this much writable memory is available in
        /// the buffer that holds the RTP packet.
        /// 
        /// srtp_protect_rtcp():
        /// @warning This function assumes that it can write SRTP_MAX_TRAILER_LEN+4
        /// to the location in memory immediately following the RTCP packet.
        /// Callers MUST ensure that this much writable memory is available in
        /// the buffer that holds the RTCP packet.
        /// </summary>
        public const int SRTP_MAX_PREFIX_LENGTH = 148;
        private const int DEFAULT_AUDIO_CLOCK_RATE = 8000;
        public const int RTP_EVENT_DEFAULT_SAMPLE_PERIOD_MS = 50; // Default sample period for an RTP event as specified by RFC2833.
        public const SDPMediaTypesEnum DEFAULT_MEDIA_TYPE = SDPMediaTypesEnum.audio; // If we can't match an RTP payload ID assume it's audio.
        public const int DEFAULT_DTMF_EVENT_PAYLOAD_ID = 101;
        public const string RTP_MEDIA_PROFILE = "RTP/AVP";
        private const int SDP_SESSIONID_LENGTH = 10;             // The length of the pseudo-random string to use for the session ID.
        public const int DTMF_EVENT_DURATION = 1200;            // Default duration for a DTMF event.
        public const int DTMF_EVENT_PAYLOAD_ID = 101;

        private static ILogger logger = Log.Logger;

        private bool m_isMediaMultiplexed = false;      // Indicates whether audio and video are multiplexed on a single RTP channel or not.
        private bool m_isRtcpMultiplexed = false;       // Indicates whether the RTP channel is multiplexing RTP and RTCP packets on the same port.
        private IPAddress m_bindAddress = null;         // If set the address to use for binding the RTP and control sockets.
        private int m_bindPort = 0;                     // If non-zero specifies the port number to attempt to bind the first RTP socket on.
        private bool m_rtpEventInProgress;              // Gets set to true when an RTP event is being sent and the normal stream is interrupted.
        private uint m_lastRtpTimestamp;                // The last timestamp used in an RTP packet.    
        private RtpVideoFramer _rtpVideoFramer;

        private string m_sdpSessionID = null;           // Need to maintain the same SDP session ID for all offers and answers.
        private int m_sdpAnnouncementVersion = 0;       // The SDP version needs to increase whenever the local SDP is modified (see https://tools.ietf.org/html/rfc6337#section-5.2.5).

        internal Dictionary<SDPMediaTypesEnum, RTPChannel> m_rtpChannels = new Dictionary<SDPMediaTypesEnum, RTPChannel>();

        /// <summary>
        /// The local audio stream for this session. Will be null if we are not sending audio.
        /// </summary>
        public virtual MediaStreamTrack AudioLocalTrack { get; private set; }

        /// <summary>
        /// The remote audio track for this session. Will be null if the remote party is not sending audio.
        /// </summary>
        public virtual MediaStreamTrack AudioRemoteTrack { get; private set; }

        /// <summary>
        /// The reporting session for the audio stream. Will be null if only video is being sent.
        /// </summary>
        public RTCPSession AudioRtcpSession { get; private set; }

        /// <summary>
        /// The local video track for this session. Will be null if we are not sending video.
        /// </summary>
        public MediaStreamTrack VideoLocalTrack { get; private set; }

        /// <summary>
        /// The remote video track for this session. Will be null if the remote party is not sending video.
        /// </summary>
        public MediaStreamTrack VideoRemoteTrack { get; private set; }

        /// <summary>
        /// The reporting session for the video stream. Will be null if only audio is being sent.
        /// </summary>
        public RTCPSession VideoRtcpSession { get; private set; }

        /// <summary>
        /// The SDP offered by the remote call party for this session.
        /// </summary>
        public SDP RemoteDescription { get; protected set; }

        /// <summary>
        /// Function pointer to an SRTP context that encrypts an RTP packet.
        /// </summary>
        private ProtectRtpPacket m_srtpProtect;

        /// <summary>
        /// Function pointer to an SRTP context that decrypts an RTP packet.
        /// </summary>
        private ProtectRtpPacket m_srtpUnprotect;

        /// <summary>
        /// Function pointer to an SRTCP context that encrypts an RTP packet.
        /// </summary>
        private ProtectRtpPacket m_srtcpControlProtect;

        /// <summary>
        /// Function pointer to an SRTP context that decrypts an RTP packet.
        /// </summary>
        private ProtectRtpPacket m_srtcpControlUnprotect;

        /// <summary>
        /// Indicates whether this session is using a secure SRTP context to encrypt RTP and
        /// RTCP packets.
        /// </summary>
        public bool IsSecure { get; private set; } = false;

        /// <summary>
        /// If this session is using a secure context this flag MUST be set to indicate
        /// the security delegate (SrtpProtect, SrtpUnprotect etc) have been set.
        /// </summary>
        public bool IsSecureContextReady { get; private set; } = false;

        /// <summary>
        /// The remote RTP end point this session is sending audio to.
        /// </summary>
        public IPEndPoint AudioDestinationEndPoint { get; protected set; }

        /// <summary>
        /// The remote RTP control end point this session is sending to RTCP reports 
        /// for the audio stream to.
        /// </summary>
        public IPEndPoint AudioControlDestinationEndPoint { get; private set; }

        /// <summary>
        /// The remote RTP end point this session is sending video to.
        /// </summary>
        public IPEndPoint VideoDestinationEndPoint { get; private set; }

        /// <summary>
        /// The remote RTP control end point this session is sending to RTCP reports 
        /// for the video stream to.
        /// </summary>
        public IPEndPoint VideoControlDestinationEndPoint { get; private set; }

        /// <summary>
        /// In order to detect RTP events from the remote party this property needs to 
        /// be set to the payload ID they are using.
        /// </summary>
        public int RemoteRtpEventPayloadID { get; set; } = DEFAULT_DTMF_EVENT_PAYLOAD_ID;

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
                return AudioLocalTrack != null && AudioLocalTrack.StreamStatus != MediaStreamStatusEnum.Inactive
                  && AudioRemoteTrack != null && AudioRemoteTrack.StreamStatus != MediaStreamStatusEnum.Inactive;
            }
        }

        /// <summary>
        /// Indicates whether this session is using video.
        /// </summary>
        public bool HasVideo
        {
            get
            {
                return VideoLocalTrack != null && VideoLocalTrack.StreamStatus != MediaStreamStatusEnum.Inactive
                  && VideoRemoteTrack != null && VideoRemoteTrack.StreamStatus != MediaStreamStatusEnum.Inactive;
            }
        }

        /// <summary>
        /// If set to true RTP will be accepted from ANY remote end point. If false
        /// certain rules are used to determine whether RTP should be accepted for 
        /// a particular audio or video stream. It is recommended to leave the
        /// value to false unless a specific need exists.
        /// </summary>
        public bool AcceptRtpFromAny { get; set; } = false;

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
        public RTPSession(
            bool isMediaMultiplexed,
            bool isRtcpMultiplexed,
            bool isSecure,
            IPAddress bindAddress = null,
            int bindPort = 0)
        {
            m_isMediaMultiplexed = isMediaMultiplexed;
            m_isRtcpMultiplexed = isRtcpMultiplexed;
            IsSecure = isSecure;
            m_bindAddress = bindAddress;
            m_bindPort = bindPort;

            m_sdpSessionID = Crypto.GetRandomInt(SDP_SESSIONID_LENGTH).ToString();
        }

        /// <summary>
        /// Used for child classes that require a single RTP channel for all RTP (audio and video)
        /// and RTCP communications.
        /// </summary>
        protected void addSingleTrack()
        {
            // We use audio as the media type when multiplexing.
            CreateRtpChannel(SDPMediaTypesEnum.audio);
            AudioRtcpSession = CreateRtcpSession(SDPMediaTypesEnum.audio);
        }

        /// <summary>
        /// Adds a media track to this session. A media track represents an audio or video
        /// stream and can be a local (which means we're sending) or remote (which means
        /// we're receiving).
        /// </summary>
        /// <param name="track">The media track to add to the session.</param>
        public virtual void addTrack(MediaStreamTrack track)
        {
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
        /// Generates the SDP for an offer that can be made to a remote user agent.
        /// </summary>
        /// <param name="connectionAddress">Optional. If specified this IP address
        /// will be used as the address advertised in the SDP offer. If not provided
        /// the kernel routing table will be used to determine the local IP address used
        /// for Internet access.</param>
        /// <returns>A task that when complete contains the SDP offer.</returns>
        public virtual SDP CreateOffer(IPAddress connectionAddress)
        {
            if (AudioLocalTrack == null && VideoLocalTrack == null)
            {
                logger.LogWarning("No local media tracks available for create offer.");
                return null;
            }
            else
            {
                List<MediaStreamTrack> localTracks = GetLocalTracks();
                var offerSdp = GetSessionDesciption(localTracks, connectionAddress);
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
        /// from the remote offer.</param>
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
                        if (AudioLocalTrack != null)
                        {
                            tracks.Add(AudioLocalTrack);
                        }
                    }
                    else if (announcement.Media == SDPMediaTypesEnum.video)
                    {
                        if (VideoLocalTrack != null)
                        {
                            tracks.Add(VideoLocalTrack);
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

                var answerSdp = GetSessionDesciption(tracks, connectionAddress);

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
                    if (remoteMediaType == SDPMediaTypesEnum.audio && AudioLocalTrack == null)
                    {
                        return SetDescriptionResultEnum.NoMatchingMediaType;
                    }
                    else if (remoteMediaType == SDPMediaTypesEnum.video && VideoLocalTrack == null)
                    {
                        return SetDescriptionResultEnum.NoMatchingMediaType;
                    }
                }

                // Pre-flight checks have passed. Move onto matching up the local and remote media streams.
                IPAddress connectionAddress = null;
                if (sessionDescription.Connection != null && !String.IsNullOrEmpty(sessionDescription.Connection.ConnectionAddress))
                {
                    connectionAddress = IPAddress.Parse(sessionDescription.Connection.ConnectionAddress);
                }

                IPEndPoint remoteAudioRtpEP = null;
                IPEndPoint remoteAudioRtcpEP = null;
                IPEndPoint remoteVideoRtpEP = null;
                IPEndPoint remoteVideoRtcpEP = null;

                foreach (var announcement in sessionDescription.Media.Where(x => x.Media == SDPMediaTypesEnum.audio || x.Media == SDPMediaTypesEnum.video))
                {
                    MediaStreamStatusEnum mediaStreamStatus = announcement.MediaStreamStatus.HasValue ? announcement.MediaStreamStatus.Value : MediaStreamStatusEnum.SendRecv;
                    var remoteTrack = new MediaStreamTrack(announcement.Media, true, announcement.MediaFormats.Values.ToList(), mediaStreamStatus, announcement.SsrcAttributes);
                    addTrack(remoteTrack);

                    if (announcement.Media == SDPMediaTypesEnum.audio)
                    {
                        if (AudioLocalTrack == null)
                        {
                            // We don't have an audio track BUT we must have another track (which has to be video). The choices are
                            // to reject the offer or to set audio stream as inactive and accept the video. We accept the video.
                            var inactiveLocalAudioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, remoteTrack.Capabilities, MediaStreamStatusEnum.Inactive);
                            addTrack(inactiveLocalAudioTrack);
                        }
                        else
                        {
                            AudioLocalTrack.Capabilities = SDPAudioVideoMediaFormat.GetCompatibleFormats(announcement.MediaFormats.Values.ToList(), AudioLocalTrack?.Capabilities);
                            remoteAudioRtpEP = GetAnnouncementRTPDestination(announcement, connectionAddress);

                            // Check whether RTP events can be supported and adjust our parameters to match the remote party if we can.
                            SDPAudioVideoMediaFormat commonEventFormat = SDPAudioVideoMediaFormat.GetCommonRtpEventFormat(announcement.MediaFormats.Values.ToList(), AudioLocalTrack.Capabilities);
                            if (!commonEventFormat.IsEmpty())
                            {
                                RemoteRtpEventPayloadID = commonEventFormat.ID;
                            }

                            SetLocalTrackStreamStatus(AudioLocalTrack, remoteTrack.StreamStatus, remoteAudioRtpEP);
                            if (remoteTrack.StreamStatus != MediaStreamStatusEnum.Inactive && AudioLocalTrack.StreamStatus != MediaStreamStatusEnum.Inactive)
                            {
                                remoteAudioRtcpEP = (m_isRtcpMultiplexed) ? remoteAudioRtpEP : new IPEndPoint(remoteAudioRtpEP.Address, remoteAudioRtpEP.Port + 1);
                            }
                        }
                    }
                    else if (announcement.Media == SDPMediaTypesEnum.video)
                    {
                        if (VideoLocalTrack == null)
                        {
                            // We don't have a video track BUT we must have another track (which has to be audio). The choices are
                            // to reject the offer or to set video stream as inactive and accept the audio. We accept the audio.
                            var inactiveLocalVideoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, remoteTrack.Capabilities, MediaStreamStatusEnum.Inactive);
                            addTrack(inactiveLocalVideoTrack);
                        }
                        else
                        {
                            VideoLocalTrack.Capabilities = SDPAudioVideoMediaFormat.GetCompatibleFormats(announcement.MediaFormats.Values.ToList(), VideoLocalTrack?.Capabilities);
                            remoteVideoRtpEP = GetAnnouncementRTPDestination(announcement, connectionAddress);

                            SetLocalTrackStreamStatus(VideoLocalTrack, remoteTrack.StreamStatus, remoteVideoRtpEP);
                            if (remoteTrack.StreamStatus != MediaStreamStatusEnum.Inactive && VideoLocalTrack.StreamStatus != MediaStreamStatusEnum.Inactive)
                            {
                                remoteVideoRtcpEP = (m_isRtcpMultiplexed) ? remoteVideoRtpEP : new IPEndPoint(remoteVideoRtpEP.Address, remoteVideoRtpEP.Port + 1);
                            }
                        }
                    }
                }

                if (VideoLocalTrack == null && AudioLocalTrack != null
                    && AudioLocalTrack.Capabilities?.Where(x => x.Name().ToLower() != SDP.TELEPHONE_EVENT_ATTRIBUTE).Count() == 0)
                {
                    return SetDescriptionResultEnum.AudioIncompatible;
                }
                else if (AudioLocalTrack == null && VideoLocalTrack != null && VideoLocalTrack.Capabilities?.Count == 0)
                {
                    return SetDescriptionResultEnum.VideoIncompatible;
                }
                else
                {
                    if (AudioLocalTrack != null &&
                        AudioLocalTrack.Capabilities.Where(x => x.Name().ToLower() != SDP.TELEPHONE_EVENT_ATTRIBUTE).Count() > 0)
                    {
                        OnAudioFormatsNegotiated?.Invoke(
                            AudioLocalTrack.Capabilities
                            .Where(x => x.Name().ToLower() != SDP.TELEPHONE_EVENT_ATTRIBUTE)
                            .Select(x => x.ToAudioFormat()).ToList());
                    }

                    if (VideoLocalTrack != null && VideoLocalTrack.Capabilities?.Count() > 0)
                    {
                        OnVideoFormatsNegotiated?.Invoke(
                            VideoLocalTrack.Capabilities
                            .Select(x => x.ToVideoFormat()).ToList());
                    }

                    // If we get to here then the remote description was compatible with the local media tracks.
                    // Set the remote description and end points.
                    RemoteDescription = sessionDescription;
                    AudioDestinationEndPoint = remoteAudioRtpEP ?? AudioDestinationEndPoint;
                    AudioControlDestinationEndPoint = remoteAudioRtcpEP ?? AudioControlDestinationEndPoint;
                    VideoDestinationEndPoint = remoteVideoRtpEP ?? VideoDestinationEndPoint;
                    VideoControlDestinationEndPoint = remoteVideoRtcpEP ?? VideoControlDestinationEndPoint;

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
            if (kind == SDPMediaTypesEnum.audio && AudioLocalTrack != null)
            {
                AudioLocalTrack.StreamStatus = status;
                m_sdpAnnouncementVersion++;
            }
            else if (kind == SDPMediaTypesEnum.video && VideoLocalTrack != null)
            {
                VideoLocalTrack.StreamStatus = status;
                m_sdpAnnouncementVersion++;
            }
        }

        /// <summary>
        /// Gets the RTP end point for an SDP media announcement from the remote peer.
        /// </summary>
        /// <param name="announcement">The media announcement to get the connection address for.</param>
        /// <param name="connectionAddress">The remote SDP session level connection address. Will be null if not available.</param>
        /// <returns>An IP end point for an SDP media announcement from the remote peer.</returns>
        private IPEndPoint GetAnnouncementRTPDestination(
            SDPMediaAnnouncement announcement,
            IPAddress connectionAddress)
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
        /// Adds a local media stream to this session. Local media tracks should be added by the
        /// application to control what session description offers and answers can be made as
        /// well as being used to match up with remote tracks.
        /// </summary>
        /// <param name="track">The local track to add.</param>
        private void AddLocalTrack(MediaStreamTrack track)
        {
            if (track.Kind == SDPMediaTypesEnum.audio && AudioLocalTrack != null)
            {
                throw new ApplicationException("A local audio track has already been set on this session.");
            }
            else if (track.Kind == SDPMediaTypesEnum.video && VideoLocalTrack != null)
            {
                throw new ApplicationException("A local video track has already been set on this session.");
            }

            if (track.StreamStatus == MediaStreamStatusEnum.Inactive)
            {
                // Inactive tracks don't use/require any local resources. Instead they are placeholders
                // so that the session description offers/answers can be balanced with the remote party.
                // For example if the remote party offers audio and video but we only support audio we
                // can reject the call or we can accept the audio and answer with an inactive video
                // announcement.
                if (track.Kind == SDPMediaTypesEnum.audio)
                {
                    AudioLocalTrack = track;
                }
                else if (track.Kind == SDPMediaTypesEnum.video)
                {
                    VideoLocalTrack = track;
                }
            }
            else
            {
                if (m_isMediaMultiplexed && m_rtpChannels.Count == 0)
                {
                    // We use audio as the media type when multiplexing.
                    CreateRtpChannel(SDPMediaTypesEnum.audio);
                    AudioRtcpSession = CreateRtcpSession(SDPMediaTypesEnum.audio);
                }

                if (track.Kind == SDPMediaTypesEnum.audio)
                {
                    if (!m_isMediaMultiplexed && !m_rtpChannels.ContainsKey(SDPMediaTypesEnum.audio))
                    {
                        CreateRtpChannel(SDPMediaTypesEnum.audio);
                    }

                    if (AudioRtcpSession == null)
                    {
                        AudioRtcpSession = CreateRtcpSession(SDPMediaTypesEnum.audio);
                    }

                    // Need to create a sending SSRC and set it on the RTCP session. 
                    AudioRtcpSession.Ssrc = track.Ssrc;
                    AudioLocalTrack = track;

                    if (AudioLocalTrack.Capabilities != null && !AudioLocalTrack.NoDtmfSupport &&
                        !AudioLocalTrack.Capabilities.Any(x => x.ID == DTMF_EVENT_PAYLOAD_ID))
                    {
                        SDPAudioVideoMediaFormat rtpEventFormat = new SDPAudioVideoMediaFormat(
                            SDPMediaTypesEnum.audio,
                            DTMF_EVENT_PAYLOAD_ID,
                            SDP.TELEPHONE_EVENT_ATTRIBUTE,
                            DEFAULT_AUDIO_CLOCK_RATE,
                            SDPAudioVideoMediaFormat.DEFAULT_AUDIO_CHANNEL_COUNT,
                            "0-16");
                        AudioLocalTrack.Capabilities.Add(rtpEventFormat);
                    }
                }
                else if (track.Kind == SDPMediaTypesEnum.video)
                {
                    // Only create the RTP socket, RTCP session etc. if a non-inactive local track is added
                    // to the session.

                    if (!m_isMediaMultiplexed && !m_rtpChannels.ContainsKey(SDPMediaTypesEnum.video))
                    {
                        CreateRtpChannel(SDPMediaTypesEnum.video);
                    }

                    if (VideoRtcpSession == null)
                    {
                        VideoRtcpSession = CreateRtcpSession(SDPMediaTypesEnum.video);
                    }

                    // Need to create a sending SSRC and set it on the RTCP session. 
                    VideoRtcpSession.Ssrc = track.Ssrc;
                    VideoLocalTrack = track;
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
                if (AudioRemoteTrack != null)
                {
                    //throw new ApplicationException("A remote audio track has already been set on this session.");
                    logger.LogDebug($"Replacing existing remote audio track for ssrc {AudioRemoteTrack.Ssrc}.");
                }

                AudioRemoteTrack = track;

                // Even if there's no local audio track an RTCP session can still be required 
                // in case the remote party send reports (presumably in case we decide we do want
                // to send or receive audio on this session at some later stage).
                if (AudioRtcpSession == null)
                {
                    AudioRtcpSession = CreateRtcpSession(SDPMediaTypesEnum.audio);
                }
            }
            else if (track.Kind == SDPMediaTypesEnum.video)
            {
                if (VideoRemoteTrack != null)
                {
                    logger.LogDebug($"Replacing existing remote video track for ssrc {VideoRemoteTrack.Ssrc}.");
                }

                VideoRemoteTrack = track;

                // Even if there's no local video track an RTCP session can still be required 
                // in case the remote party send reports (presumably in case we decide we do want
                // to send or receive video on this session at some later stage).
                if (VideoRtcpSession == null)
                {
                    VideoRtcpSession = CreateRtcpSession(SDPMediaTypesEnum.video);
                }
            }
        }

        /// <summary>
        /// Adjust the stream status of the local media tracks based on the remote tracks.
        /// </summary>
        private void SetLocalTrackStreamStatus(MediaStreamTrack localTrack, MediaStreamStatusEnum remoteTrackStatus, IPEndPoint remoteRTPEndPoint)
        {
            if (localTrack != null)
            {
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
        /// be used.</param>
        /// <returns>A session description payload.</returns>
        private SDP GetSessionDesciption(List<MediaStreamTrack> tracks, IPAddress connectionAddress)
        {
            IPAddress localAddress = connectionAddress;

            if (localAddress == null)
            {
                if (m_bindAddress != null)
                {
                    localAddress = m_bindAddress;
                }
                else if (AudioDestinationEndPoint != null && AudioDestinationEndPoint.Address != null)
                {
                    if (IPAddress.Any.Equals(AudioDestinationEndPoint.Address) || IPAddress.IPv6Any.Equals(AudioDestinationEndPoint.Address))
                    {
                        // If the remote party has set an inactive media stream via the connection address then we do the same.
                        localAddress = AudioDestinationEndPoint.Address;
                    }
                    else
                    {
                        localAddress = NetServices.GetLocalAddressForRemote(AudioDestinationEndPoint.Address);
                    }
                }
                else if (VideoDestinationEndPoint != null && VideoDestinationEndPoint.Address != null)
                {
                    if (IPAddress.Any.Equals(VideoDestinationEndPoint.Address) || IPAddress.IPv6Any.Equals(VideoDestinationEndPoint.Address))
                    {
                        // If the remote party has set an inactive media stream via the connection address then we do the same.
                        localAddress = VideoDestinationEndPoint.Address;
                    }
                    else
                    {
                        localAddress = NetServices.GetLocalAddressForRemote(VideoDestinationEndPoint.Address);
                    }
                }
                else
                {
                    localAddress = NetServices.InternetDefaultAddress;
                }
            }

            SDP sdp = new SDP(IPAddress.Loopback);
            sdp.SessionId = m_sdpSessionID;
            sdp.AnnouncementVersion = m_sdpAnnouncementVersion;

            sdp.Connection = new SDPConnectionInformation(localAddress);

            int mediaIndex = 0;

            foreach (var track in tracks)
            {
                int mindex = RemoteDescription == null ? mediaIndex++ : RemoteDescription.GetIndexForMediaType(track.Kind);

                int rtpPort = 0; // A port of zero means the media type is not supported.
                if (track.Capabilities != null && track.Capabilities.Count() > 0 && track.StreamStatus != MediaStreamStatusEnum.Inactive)
                {
                    rtpPort = (m_isMediaMultiplexed) ? m_rtpChannels.Single().Value.RTPPort : m_rtpChannels[track.Kind].RTPPort;
                }

                SDPMediaAnnouncement announcement = new SDPMediaAnnouncement(
                   track.Kind,
                   rtpPort,
                   track.Capabilities);

                announcement.Transport = RTP_MEDIA_PROFILE;
                announcement.MediaStreamStatus = track.StreamStatus;
                announcement.MLineIndex = mindex;

                if(track.MaximumBandwidth > 0)
                {
                    announcement.TIASBandwidth = track.MaximumBandwidth;
                }

                sdp.Media.Add(announcement);
            }

            return sdp;
        }

        /// <summary>
        /// Gets the RTP channel being used to send and receive the specified media type for this session.
        /// If media multiplexing is being used there will only a single RTP channel.
        /// </summary>
        public RTPChannel GetRtpChannel(SDPMediaTypesEnum mediaType)
        {
            if (m_isMediaMultiplexed)
            {
                return m_rtpChannels.FirstOrDefault().Value;
            }
            else if (m_rtpChannels.ContainsKey(mediaType))
            {
                return m_rtpChannels[mediaType];
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Creates a new RTP channel (which manages the UDP socket sending and receiving RTP
        /// packets) for use with this session.
        /// </summary>
        /// <param name="mediaType">The type of media the RTP channel is for. Must be audio or video.</param>
        /// <returns>A new RTPChannel instance.</returns>
        protected virtual RTPChannel CreateRtpChannel(SDPMediaTypesEnum mediaType)
        {
            // If RTCP is multiplexed we don't need a control socket.
            int bindPort = (m_bindPort == 0) ? 0 : m_bindPort + m_rtpChannels.Count() * 2;
            var rtpChannel = new RTPChannel(!m_isRtcpMultiplexed, m_bindAddress, bindPort);
            m_rtpChannels.Add(mediaType, rtpChannel);

            rtpChannel.OnRTPDataReceived += OnReceive;
            rtpChannel.OnControlDataReceived += OnReceive; // RTCP packets could come on RTP or control socket.
            rtpChannel.OnClosed += OnRTPChannelClosed;

            // Start the RTP, and if required the Control, socket receivers and the RTCP session.
            rtpChannel.Start();

            return rtpChannel;
        }

        /// <summary>
        /// Creates a new RTCP session for a media track belonging to this RTP session.
        /// </summary>
        /// <param name="mediaType">The media type to create the RTP session for. Must be
        /// audio or video.</param>
        /// <returns>A new RTCPSession object. The RTCPSession must have its Start method called
        /// in order to commence sending RTCP reports.</returns>
        private RTCPSession CreateRtcpSession(SDPMediaTypesEnum mediaType)
        {
            var rtcpSession = new RTCPSession(mediaType, 0);
            rtcpSession.OnTimeout += (mt) => OnTimeout?.Invoke(mt);
            rtcpSession.OnReportReadyToSend += SendRtcpReport;

            return rtcpSession;
        }

        /// <summary>
        /// Sets the Secure RTP (SRTP) delegates and marks this session as ready for communications.
        /// </summary>
        /// <param name="protectRtp">SRTP encrypt RTP packet delegate.</param>
        /// <param name="unprotectRtp">SRTP decrypt RTP packet delegate.</param>
        /// <param name="protectRtcp">SRTP encrypt RTCP packet delegate.</param>
        /// <param name="unprotectRtcp">SRTP decrypt RTCP packet delegate.</param>
        public virtual void SetSecurityContext(
            ProtectRtpPacket protectRtp,
            ProtectRtpPacket unprotectRtp,
            ProtectRtpPacket protectRtcp,
            ProtectRtpPacket unprotectRtcp)
        {
            m_srtpProtect = protectRtp;
            m_srtpUnprotect = unprotectRtp;
            m_srtcpControlProtect = protectRtcp;
            m_srtcpControlUnprotect = unprotectRtcp;

            IsSecureContextReady = true;

            logger.LogDebug("Secure context successfully set on RTPSession.");
        }

        /// <summary>
        /// Gets the local tracks available in this session. Will only be audio, video or both.
        /// Local tracks represent an audio or video source that we are sending to the remote party.
        /// </summary>
        /// <returns>A list of the local tracks that have been added to this session.</returns>
        protected List<MediaStreamTrack> GetLocalTracks()
        {
            List<MediaStreamTrack> localTracks = new List<MediaStreamTrack>();

            if (AudioLocalTrack != null)
            {
                localTracks.Add(AudioLocalTrack);
            }

            if (VideoLocalTrack != null)
            {
                localTracks.Add(VideoLocalTrack);
            }

            return localTracks;
        }

        /// <summary>
        /// Sets the remote end points for a media type supported by this RTP session.
        /// </summary>
        /// <param name="mediaType">The media type, must be audio or video, to set the remote end point for.</param>
        /// <param name="rtpEndPoint">The remote end point for RTP packets corresponding to the media type.</param>
        /// <param name="rtcpEndPoint">The remote end point for RTCP packets corresponding to the media type.</param>
        public void SetDestination(SDPMediaTypesEnum mediaType, IPEndPoint rtpEndPoint, IPEndPoint rtcpEndPoint)
        {
            if (m_isMediaMultiplexed)
            {
                AudioDestinationEndPoint = rtpEndPoint;
                VideoDestinationEndPoint = rtpEndPoint;
                AudioControlDestinationEndPoint = rtcpEndPoint;
                VideoControlDestinationEndPoint = rtcpEndPoint;
            }
            else
            {
                if (mediaType == SDPMediaTypesEnum.audio)
                {
                    AudioDestinationEndPoint = rtpEndPoint;
                    AudioControlDestinationEndPoint = rtcpEndPoint;
                }
                else if (mediaType == SDPMediaTypesEnum.video)
                {
                    VideoDestinationEndPoint = rtpEndPoint;
                    VideoControlDestinationEndPoint = rtcpEndPoint;
                }
            }
        }

        /// <summary>
        /// Starts the RTCP session(s) that monitor this RTP session.
        /// </summary>
        public virtual Task Start()
        {
            if (!IsStarted)
            {
                IsStarted = true;

                if (HasAudio && AudioRtcpSession != null && AudioLocalTrack.StreamStatus != MediaStreamStatusEnum.Inactive)
                {
                    // The local audio track may have been disabled if there were no matching capabilities with
                    // the remote party.
                    AudioRtcpSession.Start();
                }

                if (HasVideo && VideoRtcpSession != null && VideoLocalTrack.StreamStatus != MediaStreamStatusEnum.Inactive)
                {
                    // The local video track may have been disabled if there were no matching capabilities with
                    // the remote party.
                    VideoRtcpSession.Start();
                }

                OnStarted?.Invoke();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Attempts to get the highest priority sending format for the remote call party.
        /// </summary>
        /// <param name="mediaType">The media type to get the sending format for.</param>
        /// <returns>The first compatible media format found for the specified media type.</returns>
        public SDPAudioVideoMediaFormat GetSendingFormat(SDPMediaTypesEnum mediaType)
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                if (AudioLocalTrack != null && AudioRemoteTrack != null)
                {
                    var format = SDPAudioVideoMediaFormat.GetCompatibleFormats(AudioLocalTrack.Capabilities, AudioRemoteTrack.Capabilities)
                        .Where(x => x.ID != RemoteRtpEventPayloadID).FirstOrDefault();

                    if (format.IsEmpty())
                    {
                        // It's not expected that this occurs as a compatibility check is done when the remote session description
                        // is set. By this point a compatible codec should be available.
                        throw new ApplicationException($"No compatible sending format could be found for media {mediaType}.");
                    }
                    else
                    {
                        return format;
                    }
                }
                else
                {
                    throw new ApplicationException($"Cannot get the {mediaType} sending format, missing either local or remote {mediaType} track.");
                }
            }
            else if (mediaType == SDPMediaTypesEnum.video)
            {
                if (VideoLocalTrack != null && VideoRemoteTrack != null)
                {
                    return SDPAudioVideoMediaFormat.GetCompatibleFormats(VideoLocalTrack.Capabilities, VideoRemoteTrack.Capabilities).First();
                }
                else
                {
                    throw new ApplicationException($"Cannot get the {mediaType} sending format, missing wither local or remote {mediaType} track.");
                }
            }
            else
            {
                throw new ApplicationException($"Sending of {mediaType} is not supported.");
            }
        }

        /// <summary>
        /// Sends an audio sample to the remote peer.
        /// </summary>
        /// <param name="durationRtpUnits">The duration in RTP timestamp units of the audio sample. This
        /// value is added to the previous RTP timestamp when building the RTP header.</param>
        /// <param name="sample">The audio sample to set as the RTP packet payload.</param>
        public void SendAudio(uint durationRtpUnits, byte[] sample)
        {
            if (AudioDestinationEndPoint != null && (!IsSecure || IsSecureContextReady))
            {
                var audioFormat = GetSendingFormat(SDPMediaTypesEnum.audio);
                SendAudioFrame(durationRtpUnits, audioFormat.ID, sample);
            }
        }

        /// <summary>
        /// Sends a video sample to the remote peer.
        /// </summary>
        /// <param name="durationRtpUnits">The duration in RTP timestamp units of the video sample. This
        /// value is added to the previous RTP timestamp when building the RTP header.</param>
        /// <param name="sample">The video sample to set as the RTP packet payload.</param>
        public void SendVideo(uint durationRtpUnits, byte[] sample)
        {
            if (VideoDestinationEndPoint != null || (m_isMediaMultiplexed && AudioDestinationEndPoint != null) && (!IsSecure || IsSecureContextReady))
            {
                var videoSendingFormat = GetSendingFormat(SDPMediaTypesEnum.video);

                switch (videoSendingFormat.Name())
                {
                    case "VP8":
                        int vp8PayloadID = Convert.ToInt32(VideoLocalTrack.Capabilities.Single(x => x.Name() == "VP8").ID);
                        SendVp8Frame(durationRtpUnits, vp8PayloadID, sample);
                        break;
                    case "H264":
                        int h264PayloadID = Convert.ToInt32(VideoLocalTrack.Capabilities.Single(x => x.Name() == "H264").ID);
                        SendH264Frame(durationRtpUnits, h264PayloadID, sample);
                        break;
                    default:
                        throw new ApplicationException($"Unsupported video format selected {videoSendingFormat.Name()}.");
                }
            }
        }

        /// <summary>
        /// Sends an audio packet to the remote party.
        /// </summary>
        /// <param name="duration">The duration of the audio payload in timestamp units. This value
        /// gets added onto the timestamp being set in the RTP header.</param>
        /// <param name="payloadTypeID">The payload ID to set in the RTP header.</param>
        /// <param name="buffer">The audio payload to send.</param>
        public void SendAudioFrame(uint duration, int payloadTypeID, byte[] buffer)
        {
            if (IsClosed || m_rtpEventInProgress || AudioDestinationEndPoint == null || buffer == null || buffer.Length == 0)
            {
                return;
            }

            try
            {
                var audioTrack = AudioLocalTrack;

                if (audioTrack == null)
                {
                    logger.LogWarning("SendAudio was called on an RTP session without an audio stream.");
                }
                else if (audioTrack.StreamStatus == MediaStreamStatusEnum.Inactive || audioTrack.StreamStatus == MediaStreamStatusEnum.RecvOnly)
                {
                    return;
                }
                else
                {
                    // Basic RTP audio formats (such as G711, G722) do not have a concept of frames. The payload of the RTP packet is
                    // considered a single frame. This results in a problem is the audio frame being sent is larger than the MTU. In 
                    // that case the audio frame must be split across mutliple RTP packets. Unlike video frames theres no way to 
                    // indicate that a series of RTP packets are correlated to the same timestamp. For that reason if an audio buffer
                    // is supplied that's larger than MTU it will be split and the timestamp will be adjusted to best fit each RTP 
                    // paylaod.
                    // See https://github.com/sipsorcery/sipsorcery/issues/394.

                    uint payloadTimestamp = audioTrack.Timestamp;
                    uint payloadDuration = 0;

                    for (int index = 0; index * RTP_MAX_PAYLOAD < buffer.Length; index++)
                    {
                        int offset = (index == 0) ? 0 : (index * RTP_MAX_PAYLOAD);
                        int payloadLength = (offset + RTP_MAX_PAYLOAD < buffer.Length) ? RTP_MAX_PAYLOAD : buffer.Length - offset;
                        payloadTimestamp += payloadDuration;
                        byte[] payload = new byte[payloadLength];

                        Buffer.BlockCopy(buffer, offset, payload, 0, payloadLength);

                        // RFC3551 specifies that for audio the marker bit should always be 0 except for when returning
                        // from silence suppression. For video the marker bit DOES get set to 1 for the last packet
                        // in a frame.
                        int markerBit = 0;

                        var audioRtpChannel = GetRtpChannel(SDPMediaTypesEnum.audio);
                        SendRtpPacket(audioRtpChannel, AudioDestinationEndPoint, payload, payloadTimestamp, markerBit, payloadTypeID, audioTrack.Ssrc, audioTrack.SeqNum, AudioRtcpSession);

                        //logger.LogDebug($"send audio { audioRtpChannel.RTPLocalEndPoint}->{AudioDestinationEndPoint}.");

                        audioTrack.SeqNum = (audioTrack.SeqNum == UInt16.MaxValue) ? (ushort)0 : (ushort)(audioTrack.SeqNum + 1);
                        payloadDuration = (uint)(((decimal)payloadLength / buffer.Length) * duration); // Get the percentage duration of this payload.
                    }

                    audioTrack.Timestamp += duration;
                }
            }
            catch (SocketException sockExcp)
            {
                logger.LogError("SocketException SendAudioFrame. " + sockExcp.Message);
            }
        }

        /// <summary>
        /// Sends a VP8 frame as one or more RTP packets.
        /// </summary>
        /// <param name="timestamp">The timestamp to place in the RTP header. Needs
        /// to be based on a 90Khz clock.</param>
        /// <param name="payloadTypeID">The payload ID to place in the RTP header.</param>
        /// <param name="buffer">The VP8 encoded payload.</param>
        public void SendVp8Frame(uint duration, int payloadTypeID, byte[] buffer)
        {
            var dstEndPoint = m_isMediaMultiplexed ? AudioDestinationEndPoint : VideoDestinationEndPoint;

            if (IsClosed || m_rtpEventInProgress || dstEndPoint == null)
            {
                return;
            }

            try
            {
                var videoTrack = VideoLocalTrack;

                if (videoTrack == null)
                {
                    logger.LogWarning("SendVp8Frame was called on an RTP session without a video stream.");
                }
                else if (videoTrack.StreamStatus == MediaStreamStatusEnum.Inactive || videoTrack.StreamStatus == MediaStreamStatusEnum.RecvOnly)
                {
                    return;
                }
                else
                {
                    for (int index = 0; index * RTP_MAX_PAYLOAD < buffer.Length; index++)
                    {
                        int offset = index * RTP_MAX_PAYLOAD;
                        int payloadLength = (offset + RTP_MAX_PAYLOAD < buffer.Length) ? RTP_MAX_PAYLOAD : buffer.Length - offset;

                        byte[] vp8HeaderBytes = (index == 0) ? new byte[] { 0x10 } : new byte[] { 0x00 };
                        byte[] payload = new byte[payloadLength + vp8HeaderBytes.Length];
                        Buffer.BlockCopy(vp8HeaderBytes, 0, payload, 0, vp8HeaderBytes.Length);
                        Buffer.BlockCopy(buffer, offset, payload, vp8HeaderBytes.Length, payloadLength);

                        int markerBit = ((offset + payloadLength) >= buffer.Length) ? 1 : 0; // Set marker bit for the last packet in the frame.

                        var videoChannel = GetRtpChannel(SDPMediaTypesEnum.video);

                        SendRtpPacket(videoChannel, dstEndPoint, payload, videoTrack.Timestamp, markerBit, payloadTypeID, videoTrack.Ssrc, videoTrack.SeqNum, VideoRtcpSession);
                        //logger.LogDebug($"send VP8 {videoChannel.RTPLocalEndPoint}->{dstEndPoint} timestamp {videoTrack.Timestamp}, sample length {buffer.Length}.");

                        videoTrack.SeqNum = (videoTrack.SeqNum == UInt16.MaxValue) ? (ushort)0 : (ushort)(videoTrack.SeqNum + 1);
                    }

                    videoTrack.Timestamp += duration;
                }
            }
            catch (SocketException sockExcp)
            {
                logger.LogError("SocketException SendVp8Frame. " + sockExcp.Message);
            }
        }

        /// <summary>
        /// Helper method to send a low quality JPEG image over RTP. This method supports a very abbreviated version of RFC 2435 "RTP Payload Format for JPEG-compressed Video".
        /// It's intended as a quick convenient way to send something like a test pattern image over an RTSP connection. More than likely it won't be suitable when a high
        /// quality image is required since the header used in this method does not support quantization tables.
        /// </summary>
        /// <param name="jpegBytes">The raw encoded bytes of the JPEG image to transmit.</param>
        /// <param name="jpegQuality">The encoder quality of the JPEG image.</param>
        /// <param name="jpegWidth">The width of the JPEG image.</param>
        /// <param name="jpegHeight">The height of the JPEG image.</param>
        /// <param name="framesPerSecond">The rate at which the JPEG frames are being transmitted at. used to calculate the timestamp.</param>
        public void SendJpegFrame(uint duration, int payloadTypeID, byte[] jpegBytes, int jpegQuality, int jpegWidth, int jpegHeight)
        {
            var dstEndPoint = m_isMediaMultiplexed ? AudioDestinationEndPoint : VideoDestinationEndPoint;

            if (IsClosed || m_rtpEventInProgress || dstEndPoint == null)
            {
                return;
            }

            try
            {
                var videoTrack = VideoLocalTrack;

                if (videoTrack == null)
                {
                    logger.LogWarning("SendJpegFrame was called on an RTP session without a video stream.");
                }
                else if (videoTrack.StreamStatus == MediaStreamStatusEnum.Inactive || videoTrack.StreamStatus == MediaStreamStatusEnum.RecvOnly)
                {
                    return;
                }
                else
                {
                    for (int index = 0; index * RTP_MAX_PAYLOAD < jpegBytes.Length; index++)
                    {
                        uint offset = Convert.ToUInt32(index * RTP_MAX_PAYLOAD);
                        int payloadLength = ((index + 1) * RTP_MAX_PAYLOAD < jpegBytes.Length) ? RTP_MAX_PAYLOAD : jpegBytes.Length - index * RTP_MAX_PAYLOAD;
                        byte[] jpegHeader = RtpVideoFramer.CreateLowQualityRtpJpegHeader(offset, jpegQuality, jpegWidth, jpegHeight);

                        List<byte> packetPayload = new List<byte>();
                        packetPayload.AddRange(jpegHeader);
                        packetPayload.AddRange(jpegBytes.Skip(index * RTP_MAX_PAYLOAD).Take(payloadLength));

                        int markerBit = ((index + 1) * RTP_MAX_PAYLOAD < jpegBytes.Length) ? 0 : 1;
                        SendRtpPacket(GetRtpChannel(SDPMediaTypesEnum.video), dstEndPoint, packetPayload.ToArray(), videoTrack.Timestamp, markerBit, payloadTypeID, videoTrack.Ssrc, videoTrack.SeqNum, VideoRtcpSession);

                        videoTrack.SeqNum = (videoTrack.SeqNum == UInt16.MaxValue) ? (ushort)0 : (ushort)(videoTrack.SeqNum + 1);
                    }

                    videoTrack.Timestamp += duration;
                }
            }
            catch (SocketException sockExcp)
            {
                logger.LogError("SocketException SendJpegFrame. " + sockExcp.Message);
            }
        }

        /// <summary>
        /// Sends a H264 frame, represented by an Access Unit, to the remote party.
        /// </summary>
        /// <param name="duration">The duration in timestamp units of the payload (e.g. 3000 for 30fps).</param>
        /// <param name="payloadTypeID">The payload type ID  being used for H264 and that will be set on the RTP header.</param>
        /// <param name="accessUnit">The encoded H264 access unit to transmit. An access unit can contain one or more
        /// NAL's.</param>
        /// <remarks>
        /// An Access Unit can contain one or more NAL's. The NAL's have to be parsed in order to be able to package 
        /// in RTP packets.
        /// 
        /// See https://www.itu.int/rec/dologin_pub.asp?lang=e&id=T-REC-H.264-201602-S!!PDF-E&type=items Annex B for byte stream specification.
        /// </remarks>
        public void SendH264Frame(uint duration, int payloadTypeID, byte[] accessUnit)
        {
            var dstEndPoint = m_isMediaMultiplexed ? AudioDestinationEndPoint : VideoDestinationEndPoint;

            if (IsClosed || m_rtpEventInProgress || dstEndPoint == null || accessUnit == null || accessUnit.Length == 0)
            {
                return;
            }

            var videoTrack = VideoLocalTrack;

            if (videoTrack == null)
            {
                logger.LogWarning("SendH264Frame was called on an RTP session without a video stream.");
            }
            else if (videoTrack.StreamStatus == MediaStreamStatusEnum.Inactive || videoTrack.StreamStatus == MediaStreamStatusEnum.RecvOnly)
            {
                return;
            }
            else
            {
                foreach (var nal in H264Packetiser.ParseNals(accessUnit))
                {
                    SendH264Nal(duration, payloadTypeID, nal.NAL, nal.IsLast, dstEndPoint, videoTrack);
                }
            }
        }

        /// <summary>
        /// Sends a single H264 NAL to the remote party.
        /// </summary>
        /// <param name="duration">The duration in timestamp units of the payload (e.g. 3000 for 30fps).</param>
        /// <param name="payloadTypeID">The payload type ID  being used for H264 and that will be set on the RTP header.</param>
        /// <param name="nal">The buffer containing the NAL to send.</param>
        /// <param name="isLastNal">Should be set for the last NAL in the H264 access unit. Determines when the markbit gets set 
        /// and the timestamp incremented.</param>
        /// <param name="dstEndPoint">The destination end point to send to.</param>
        /// <param name="videoTrack">The video track to send on.</param>
        private void SendH264Nal(uint duration, int payloadTypeID, byte[] nal, bool isLastNal, IPEndPoint dstEndPoint, MediaStreamTrack videoTrack)
        {
            //logger.LogDebug($"Send NAL {nal.Length}, is last {isLastNal}, timestamp {videoTrack.Timestamp}.");
            //logger.LogDebug($"nri {nalNri:X2}, type {nalType:X2}.");

            byte nal0 = nal[0];

            if (nal.Length <= RTP_MAX_PAYLOAD)
            {
                // Send as Single-Time Aggregation Packet (STAP-A).
                byte[] payload = new byte[nal.Length];
                int markerBit = isLastNal ? 1 : 0;   // There is only ever one packet in a STAP-A.
                Buffer.BlockCopy(nal, 0, payload, 0, nal.Length);

                var videoChannel = GetRtpChannel(SDPMediaTypesEnum.video);

                SendRtpPacket(videoChannel, dstEndPoint, payload, videoTrack.Timestamp, markerBit, payloadTypeID, videoTrack.Ssrc, videoTrack.SeqNum, VideoRtcpSession);
                //logger.LogDebug($"send H264 {videoChannel.RTPLocalEndPoint}->{dstEndPoint} timestamp {videoTrack.Timestamp}, payload length {payload.Length}, seqnum {videoTrack.SeqNum}, marker {markerBit}.");
                //logger.LogDebug($"send H264 {videoChannel.RTPLocalEndPoint}->{dstEndPoint} timestamp {videoTrack.Timestamp}, STAP-A {h264RtpHdr.HexStr()}, payload length {payload.Length}, seqnum {videoTrack.SeqNum}, marker {markerBit}.");

                videoTrack.SeqNum = (videoTrack.SeqNum == UInt16.MaxValue) ? (ushort)0 : (ushort)(videoTrack.SeqNum + 1);
            }
            else
            {
                nal = nal.Skip(1).ToArray();

                // Send as Fragmentation Unit A (FU-A):
                for (int index = 0; index * RTP_MAX_PAYLOAD < nal.Length; index++)
                {
                    int offset = index * RTP_MAX_PAYLOAD;
                    int payloadLength = ((index + 1) * RTP_MAX_PAYLOAD < nal.Length) ? RTP_MAX_PAYLOAD : nal.Length - index * RTP_MAX_PAYLOAD;

                    bool isFirstPacket = index == 0;
                    bool isFinalPacket = (index + 1) * RTP_MAX_PAYLOAD >= nal.Length;
                    int markerBit = (isLastNal && isFinalPacket) ? 1 : 0;

                    byte[] h264RtpHdr = H264Packetiser.GetH264RtpHeader(nal0, isFirstPacket, isFinalPacket);

                    byte[] payload = new byte[payloadLength + h264RtpHdr.Length];
                    Buffer.BlockCopy(h264RtpHdr, 0, payload, 0, h264RtpHdr.Length);
                    Buffer.BlockCopy(nal, offset, payload, h264RtpHdr.Length, payloadLength);

                    var videoChannel = GetRtpChannel(SDPMediaTypesEnum.video);

                    SendRtpPacket(videoChannel, dstEndPoint, payload, videoTrack.Timestamp, markerBit, payloadTypeID, videoTrack.Ssrc, videoTrack.SeqNum, VideoRtcpSession);
                    //logger.LogDebug($"send H264 {videoChannel.RTPLocalEndPoint}->{dstEndPoint} timestamp {videoTrack.Timestamp}, FU-A {h264RtpHdr.HexStr()}, payload length {payloadLength}, seqnum {videoTrack.SeqNum}, marker {markerBit}.");

                    videoTrack.SeqNum = (videoTrack.SeqNum == UInt16.MaxValue) ? (ushort)0 : (ushort)(videoTrack.SeqNum + 1);
                }
            }

            if (isLastNal)
            {
                videoTrack.Timestamp += duration;
            }
        }

        /// <summary>
        /// Sends a DTMF tone as an RTP event to the remote party.
        /// </summary>
        /// <param name="key">The DTMF tone to send.</param>
        /// <param name="ct">RTP events can span multiple RTP packets. This token can
        /// be used to cancel the send.</param>
        public virtual Task SendDtmf(byte key, CancellationToken ct)
        {
            var dtmfEvent = new RTPEvent(key, false, RTPEvent.DEFAULT_VOLUME, DTMF_EVENT_DURATION, DTMF_EVENT_PAYLOAD_ID);
            return SendDtmfEvent(dtmfEvent, ct);
        }

        /// <summary>
        /// Sends an RTP event for a DTMF tone as per RFC2833. Sending the event requires multiple packets to be sent.
        /// This method will hold onto the socket until all the packets required for the event have been sent. The send
        /// can be cancelled using the cancellation token.
        /// </summary>
        /// <param name="rtpEvent">The RTP event to send.</param>
        /// <param name="cancellationToken">CancellationToken to allow the operation to be cancelled prematurely.</param>
        /// <param name="clockRate">To send an RTP event the clock rate of the underlying stream needs to be known.</param>
        /// <param name="streamID">For multiplexed sessions the ID of the stream to send the event on. Defaults to 0
        /// for single stream sessions.</param>
        public async Task SendDtmfEvent(
            RTPEvent rtpEvent,
            CancellationToken cancellationToken,
            int clockRate = DEFAULT_AUDIO_CLOCK_RATE)
        {
            var dstEndPoint = AudioDestinationEndPoint;

            if (IsClosed || m_rtpEventInProgress == true || dstEndPoint == null)
            {
                logger.LogWarning("SendDtmfEvent request ignored as an RTP event is already in progress.");
            }

            try
            {
                var audioTrack = AudioLocalTrack;

                if (audioTrack == null)
                {
                    logger.LogWarning("SendDtmfEvent was called on an RTP session without an audio stream.");
                }
                else if (audioTrack.StreamStatus == MediaStreamStatusEnum.Inactive || audioTrack.StreamStatus == MediaStreamStatusEnum.RecvOnly)
                {
                    return;
                }
                else
                {
                    m_rtpEventInProgress = true;
                    uint startTimestamp = m_lastRtpTimestamp;

                    // The sample period in milliseconds being used for the media stream that the event 
                    // is being inserted into. Should be set to 50ms if main media stream is dynamic or 
                    // sample period is unknown.
                    int samplePeriod = RTP_EVENT_DEFAULT_SAMPLE_PERIOD_MS;

                    // The RTP timestamp step corresponding to the sampling period. This can change depending
                    // on the codec being used. For example using PCMU with a sampling frequency of 8000Hz and a sample period of 50ms
                    // the timestamp step is 400 (8000 / (1000 / 50)). For a sample period of 20ms it's 160 (8000 / (1000 / 20)).
                    ushort rtpTimestampStep = (ushort)(clockRate * samplePeriod / 1000);

                    // If only the minimum number of packets are being sent then they are both the start and end of the event.
                    rtpEvent.EndOfEvent = (rtpEvent.TotalDuration <= rtpTimestampStep);
                    // The DTMF tone is generally multiple RTP events. Each event has a duration of the RTP timestamp step.
                    rtpEvent.Duration = rtpTimestampStep;

                    // Send the start of event packets.
                    for (int i = 0; i < RTPEvent.DUPLICATE_COUNT && !cancellationToken.IsCancellationRequested; i++)
                    {
                        byte[] buffer = rtpEvent.GetEventPayload();

                        int markerBit = (i == 0) ? 1 : 0;  // Set marker bit for the first packet in the event.
                        SendRtpPacket(GetRtpChannel(SDPMediaTypesEnum.audio), dstEndPoint, buffer, startTimestamp, markerBit, rtpEvent.PayloadTypeID, audioTrack.Ssrc, audioTrack.SeqNum, AudioRtcpSession);

                        audioTrack.SeqNum = (audioTrack.SeqNum == UInt16.MaxValue) ? (ushort)0 : (ushort)(audioTrack.SeqNum + 1);
                    }

                    await Task.Delay(samplePeriod, cancellationToken).ConfigureAwait(false);

                    if (!rtpEvent.EndOfEvent)
                    {
                        // Send the progressive event packets 
                        while ((rtpEvent.Duration + rtpTimestampStep) < rtpEvent.TotalDuration && !cancellationToken.IsCancellationRequested)
                        {
                            rtpEvent.Duration += rtpTimestampStep;
                            byte[] buffer = rtpEvent.GetEventPayload();

                            SendRtpPacket(GetRtpChannel(SDPMediaTypesEnum.audio), dstEndPoint, buffer, startTimestamp, 0, rtpEvent.PayloadTypeID, audioTrack.Ssrc, audioTrack.SeqNum, AudioRtcpSession);

                            audioTrack.SeqNum = (audioTrack.SeqNum == UInt16.MaxValue) ? (ushort)0 : (ushort)(audioTrack.SeqNum + 1);

                            await Task.Delay(samplePeriod, cancellationToken).ConfigureAwait(false);
                        }

                        // Send the end of event packets.
                        for (int j = 0; j < RTPEvent.DUPLICATE_COUNT && !cancellationToken.IsCancellationRequested; j++)
                        {
                            rtpEvent.EndOfEvent = true;
                            rtpEvent.Duration = rtpEvent.TotalDuration;
                            byte[] buffer = rtpEvent.GetEventPayload();

                            SendRtpPacket(GetRtpChannel(SDPMediaTypesEnum.audio), dstEndPoint, buffer, startTimestamp, 0, rtpEvent.PayloadTypeID, audioTrack.Ssrc, audioTrack.SeqNum, AudioRtcpSession);

                            audioTrack.SeqNum = (audioTrack.SeqNum == UInt16.MaxValue) ? (ushort)0 : (ushort)(audioTrack.SeqNum + 1);
                        }
                    }
                }
            }
            catch (SocketException sockExcp)
            {
                logger.LogError("SocketException SendDtmfEvent. " + sockExcp.Message);
            }
            catch (TaskCanceledException)
            {
                logger.LogWarning("SendDtmfEvent was cancelled by caller.");
            }
            finally
            {
                m_rtpEventInProgress = false;
            }
        }

        /// <summary>
        /// Allows additional control for sending raw RTP payloads. No framing or other processing is carried out.
        /// </summary>
        /// <param name="mediaType">The media type of the RTP packet being sent. Must be audio or video.</param>
        /// <param name="payload">The RTP packet payload.</param>
        /// <param name="timestamp">The timestamp to set on the RTP header.</param>
        /// <param name="markerBit">The value to set on the RTP header marker bit, should be 0 or 1.</param>
        /// <param name="payloadTypeID">The payload ID to set in the RTP header.</param>
        public void SendRtpRaw(SDPMediaTypesEnum mediaType, byte[] payload, uint timestamp, int markerBit, int payloadTypeID)
        {
            if (mediaType == SDPMediaTypesEnum.audio && AudioLocalTrack == null)
            {
                logger.LogWarning("SendRtpRaw was called for an audio packet on an RTP session without a local audio stream.");
            }
            else if (mediaType == SDPMediaTypesEnum.video && VideoLocalTrack == null)
            {
                logger.LogWarning("SendRtpRaw was called for a video packet on an RTP session without a local video stream.");
            }
            else
            {
                var rtpChannel = GetRtpChannel(mediaType);
                RTCPSession rtcpSession = (mediaType == SDPMediaTypesEnum.video) ? VideoRtcpSession : AudioRtcpSession;
                IPEndPoint dstEndPoint = (mediaType == SDPMediaTypesEnum.audio || m_isMediaMultiplexed) ? AudioDestinationEndPoint : VideoDestinationEndPoint;
                MediaStreamTrack track = (mediaType == SDPMediaTypesEnum.video) ? VideoLocalTrack : AudioLocalTrack;

                if (dstEndPoint != null)
                {
                    SendRtpPacket(rtpChannel, dstEndPoint, payload, timestamp, markerBit, payloadTypeID, track.Ssrc, track.SeqNum, rtcpSession);

                    track.SeqNum = (track.SeqNum == UInt16.MaxValue) ? (ushort)0 : (ushort)(track.SeqNum + 1);
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
            var reportBytes = feedback.GetBytes();
            SendRtcpReport(mediaType, reportBytes);
        }

        /// <summary>
        /// Close the session and RTP channel.
        /// </summary>
        public virtual void Close(string reason)
        {
            if (!IsClosed)
            {
                IsClosed = true;

                AudioRtcpSession?.Close(reason);
                VideoRtcpSession?.Close(reason);

                foreach (var rtpChannel in m_rtpChannels.Values)
                {
                    rtpChannel.OnRTPDataReceived -= OnReceive;
                    rtpChannel.OnControlDataReceived -= OnReceive;
                    rtpChannel.OnClosed -= OnRTPChannelClosed;
                    rtpChannel.Close(reason);
                }

                OnRtpClosed?.Invoke(reason);

                OnClosed?.Invoke();
            }
        }

        /// <summary>
        /// Event handler for receiving data on the RTP and Control channels. For multiplexed
        /// sessions both RTP and RTCP packets will be received on the RTP channel.
        /// </summary>
        /// <param name="localPort">The local port the data was received on.</param>
        /// <param name="remoteEndPoint">The remote end point the data was received from.</param>
        /// <param name="buffer">The data received.</param>
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
                if (IsSecure && !IsSecureContextReady)
                {
                    logger.LogWarning("RTP or RTCP packet received before secure context ready.");
                }
                else if (buffer[1] == 0xC8 /* RTCP SR */ || buffer[1] == 0xC9 /* RTCP RR */)
                {
                    //logger.LogDebug($"RTCP packet received from {remoteEndPoint} {buffer.HexStr()}");

                    #region RTCP packet.

                    if (m_srtcpControlUnprotect != null)
                    {
                        int outBufLen = 0;
                        int res = m_srtcpControlUnprotect(buffer, buffer.Length, out outBufLen);

                        if (res != 0)
                        {
                            logger.LogWarning($"SRTCP unprotect failed, result {res}.");
                            return;
                        }
                        else
                        {
                            buffer = buffer.Take(outBufLen).ToArray();
                        }
                    }

                    var rtcpPkt = new RTCPCompoundPacket(buffer);

                    if (rtcpPkt != null)
                    {
                        if (rtcpPkt.Bye != null)
                        {
                            logger.LogDebug($"RTCP BYE received for SSRC {rtcpPkt.Bye.SSRC}, reason {rtcpPkt.Bye.Reason}.");

                            OnRtcpBye?.Invoke(rtcpPkt.Bye.Reason);

                            // In some cases, such as a SIP re-INVITE, it's possible the RTP session
                            // will keep going with a new remote SSRC. 
                            if (AudioRemoteTrack != null && rtcpPkt.Bye.SSRC == AudioRemoteTrack.Ssrc)
                            {
                                AudioRtcpSession?.RemoveReceptionReport(rtcpPkt.Bye.SSRC);
                                //AudioDestinationEndPoint = null;
                                //AudioControlDestinationEndPoint = null;
                                AudioRemoteTrack.Ssrc = 0;
                            }
                            else if (VideoRemoteTrack != null && rtcpPkt.Bye.SSRC == VideoRemoteTrack.Ssrc)
                            {
                                VideoRtcpSession?.RemoveReceptionReport(rtcpPkt.Bye.SSRC);
                                //VideoDestinationEndPoint = null;
                                //VideoControlDestinationEndPoint = null;
                                VideoRemoteTrack.Ssrc = 0;
                            }
                        }
                        else if (!IsClosed)
                        {
                            var rtcpSession = GetRtcpSession(rtcpPkt);
                            if (rtcpSession != null)
                            {
                                if (rtcpSession.LastActivityAt == DateTime.MinValue)
                                {
                                    // On the first received RTCP report for a session check whether the remote end point matches the
                                    // expected remote end point. If not it's "likely" that a private IP address was specified in the SDP.
                                    // Take the risk and switch the remote control end point to the one we are receiving from.

                                    if (rtcpSession == AudioRtcpSession &&
                                        (AudioControlDestinationEndPoint == null ||
                                        !AudioControlDestinationEndPoint.Address.Equals(remoteEndPoint.Address) ||
                                        AudioControlDestinationEndPoint.Port != remoteEndPoint.Port))
                                    {
                                        logger.LogDebug($"Audio control end point switched from {AudioControlDestinationEndPoint} to {remoteEndPoint}.");
                                        AudioControlDestinationEndPoint = remoteEndPoint;
                                    }
                                    else if (rtcpSession == VideoRtcpSession &&
                                        (VideoControlDestinationEndPoint == null ||
                                        !VideoControlDestinationEndPoint.Address.Equals(remoteEndPoint.Address) ||
                                        VideoControlDestinationEndPoint.Port != remoteEndPoint.Port))
                                    {
                                        logger.LogDebug($"Video control end point switched from {VideoControlDestinationEndPoint} to {remoteEndPoint}.");
                                        VideoControlDestinationEndPoint = remoteEndPoint;
                                    }
                                }

                                rtcpSession.ReportReceived(remoteEndPoint, rtcpPkt);
                                OnReceiveReport?.Invoke(remoteEndPoint, rtcpSession.MediaType, rtcpPkt);
                            }
                            else if (AudioRtcpSession?.PacketsReceivedCount > 0 || VideoRtcpSession?.PacketsReceivedCount > 0)
                            {
                                // Only give this warning if we've received at least one RTP packet.
                                logger.LogWarning("Could not match an RTCP packet against any SSRC's in the session.");
                            }
                        }
                    }
                    else
                    {
                        logger.LogWarning("Failed to parse RTCP compound report.");
                    }

                    #endregion
                }
                else
                {
                    #region RTP packet.

                    if (!IsClosed)
                    {
                        if (m_srtpUnprotect != null)
                        {
                            int res = m_srtpUnprotect(buffer, buffer.Length, out int outBufLen);

                            if (res != 0)
                            {
                                logger.LogWarning($"SRTP unprotect failed, result {res}.");
                                return;
                            }
                            else
                            {
                                buffer = buffer.Take(outBufLen).ToArray();
                            }
                        }

                        var rtpPacket = new RTPPacket(buffer);

                        var hdr = rtpPacket.Header;
                        //logger.LogDebug($"rtp recv, seqnum {hdr.SequenceNumber}, ts {hdr.Timestamp}, marker {hdr.MarkerBit}, payload {rtpPacket.Payload.Length}.");

                        //SDPMediaTypesEnum? rtpMediaType = null;

                        // Check whether this is an RTP event.
                        if (RemoteRtpEventPayloadID != 0 && rtpPacket.Header.PayloadType == RemoteRtpEventPayloadID)
                        {
                            RTPEvent rtpEvent = new RTPEvent(rtpPacket.Payload);
                            OnRtpEvent?.Invoke(remoteEndPoint, rtpEvent, rtpPacket.Header);
                        }
                        else
                        {
                            // Attempt to determine the media type for the RTP packet.
                            //rtpMediaType = GetMediaTypeForRtpPacket(rtpPacket.Header);
                            //if (rtpMediaType == null)
                            //{
                            //    if (AudioLocalTrack != null && VideoLocalTrack == null)
                            //    {
                            //        rtpMediaType = SDPMediaTypesEnum.audio;
                            //    }
                            //    else if (AudioLocalTrack == null && VideoLocalTrack != null)
                            //    {
                            //        rtpMediaType = SDPMediaTypesEnum.video;
                            //    }
                            //    else
                            //    {
                            //        rtpMediaType = GetMediaTypeForLocalPort(localPort);
                            //    }
                            //}

                            var avFormat = GetFormatForRtpPacket(rtpPacket.Header);

                            if (avFormat != null)
                            {
                                // Set the remote track SSRC so that RTCP reports can match the media type.
                                if (avFormat.Value.Kind == SDPMediaTypesEnum.audio && AudioRemoteTrack != null && AudioRemoteTrack.Ssrc == 0 && AudioDestinationEndPoint != null)
                                {
                                    bool isValidSource = AdjustRemoteEndPoint(SDPMediaTypesEnum.audio, rtpPacket.Header.SyncSource, remoteEndPoint);

                                    if (isValidSource)
                                    {
                                        logger.LogDebug($"Set remote audio track SSRC to {rtpPacket.Header.SyncSource}.");
                                        AudioRemoteTrack.Ssrc = rtpPacket.Header.SyncSource;
                                    }
                                }
                                else if (avFormat.Value.Kind == SDPMediaTypesEnum.video && VideoRemoteTrack != null && VideoRemoteTrack.Ssrc == 0 && (m_isMediaMultiplexed || VideoDestinationEndPoint != null))
                                {
                                    bool isValidSource = AdjustRemoteEndPoint(SDPMediaTypesEnum.video, rtpPacket.Header.SyncSource, remoteEndPoint);

                                    if (isValidSource)
                                    {
                                        logger.LogDebug($"Set remote video track SSRC to {rtpPacket.Header.SyncSource}.");
                                        VideoRemoteTrack.Ssrc = rtpPacket.Header.SyncSource;
                                    }
                                }

                                // Note AC 24 Dec 2020: The probelm with waiting until the remote description is set is that the remote peer often starts sending
                                // RTP packets at the same time it signals its SDP offer or answer. Generally this is not a problem for audio but for video streams
                                // the first RTP packet(s) are the key frame and if they are ignored the video stream will take addtional time or manual 
                                // intervention to synchronise.
                                //if (RemoteDescription != null)
                                //{

                                // Don't hand RTP packets to the application until the remote description has been set. Without it
                                // things like the common codec, DTMF support etc. are not known.

                                //SDPMediaTypesEnum mediaType = (rtpMediaType.HasValue) ? rtpMediaType.Value : DEFAULT_MEDIA_TYPE;

                                // For video RTP packets an attempt will be made to collate into frames. It's up to the application
                                // whether it wants to subscribe to frames of RTP packets.
                                if (avFormat.Value.Kind == SDPMediaTypesEnum.video)
                                {
                                    if (VideoRemoteTrack != null)
                                    {
                                        if (VideoRemoteTrack.LastRemoteSeqNum != 0 &&
                                           rtpPacket.Header.SequenceNumber != (VideoRemoteTrack.LastRemoteSeqNum + 1) &&
                                          !(rtpPacket.Header.SequenceNumber == 0 && VideoRemoteTrack.LastRemoteSeqNum == UInt16.MaxValue))
                                        {
                                            logger.LogWarning($"Video stream sequence number jumped from {VideoRemoteTrack.LastRemoteSeqNum} to {rtpPacket.Header.SequenceNumber}.");
                                        }

                                        VideoRemoteTrack.LastRemoteSeqNum = rtpPacket.Header.SequenceNumber;
                                    }

                                    if (_rtpVideoFramer != null)
                                    {
                                        var frame = _rtpVideoFramer.GotRtpPacket(rtpPacket);
                                        if (frame != null)
                                        {
                                            OnVideoFrameReceived?.Invoke(remoteEndPoint, rtpPacket.Header.Timestamp, frame, avFormat.Value.ToVideoFormat());
                                        }
                                    }
                                    else
                                    {
                                        var videoFormat = avFormat.Value; //GetSendingFormat(SDPMediaTypesEnum.video);

                                        if (videoFormat.ToVideoFormat().Codec == VideoCodecsEnum.VP8 ||
                                            videoFormat.ToVideoFormat().Codec == VideoCodecsEnum.H264)
                                        {
                                            logger.LogDebug($"Video depacketisation codec set to {videoFormat.ToVideoFormat().Codec} for SSRC {rtpPacket.Header.SyncSource}.");

                                            _rtpVideoFramer = new RtpVideoFramer(videoFormat.ToVideoFormat().Codec);

                                            var frame = _rtpVideoFramer.GotRtpPacket(rtpPacket);
                                            if (frame != null)
                                            {
                                                OnVideoFrameReceived?.Invoke(remoteEndPoint, rtpPacket.Header.Timestamp, frame, avFormat.Value.ToVideoFormat());
                                            }
                                        }
                                        else
                                        {
                                            logger.LogWarning($"Video depacketisation logic for codec {videoFormat.Name()} has not been implemented, PR's welcome!");
                                        }
                                    }
                                }
                                else if (avFormat.Value.Kind == SDPMediaTypesEnum.audio && AudioRemoteTrack != null)
                                {
                                    if (AudioRemoteTrack.LastRemoteSeqNum != 0 &&
                                        rtpPacket.Header.SequenceNumber != (AudioRemoteTrack.LastRemoteSeqNum + 1) &&
                                       !(rtpPacket.Header.SequenceNumber == 0 && AudioRemoteTrack.LastRemoteSeqNum == UInt16.MaxValue))
                                    {
                                        logger.LogWarning($"Audio stream sequence number jumped from {AudioRemoteTrack.LastRemoteSeqNum} to {rtpPacket.Header.SequenceNumber}.");
                                    }

                                    AudioRemoteTrack.LastRemoteSeqNum = rtpPacket.Header.SequenceNumber;
                                }

                                OnRtpPacketReceived?.Invoke(remoteEndPoint, avFormat.Value.Kind, rtpPacket);
                                //}

                                // Used for reporting purposes.
                                if (avFormat.Value.Kind == SDPMediaTypesEnum.audio && AudioRtcpSession != null)
                                {
                                    AudioRtcpSession.RecordRtpPacketReceived(rtpPacket);
                                }
                                else if (avFormat.Value.Kind == SDPMediaTypesEnum.video && VideoRtcpSession != null)
                                {
                                    VideoRtcpSession.RecordRtpPacketReceived(rtpPacket);
                                }
                            }
                        }
                    }

                    #endregion
                }
            }
        }

        /// <summary>
        /// Adjusts the expected remote end point for a particular media type.
        /// </summary>
        /// <param name="mediaType">The media type of the RTP packet received.</param>
        /// <param name="ssrc">The SSRC from the RTP packet header.</param>
        /// <param name="receivedOnEndPoint">The actual remote end point that the RTP packet came from.</param>
        /// <returns>True if remote end point for this media type was th expected one or it was adjusted. False if
        /// the remote end point was deemed to be invalid for this media type.</returns>
        private bool AdjustRemoteEndPoint(SDPMediaTypesEnum mediaType, uint ssrc, IPEndPoint receivedOnEndPoint)
        {
            bool isValidSource = false;
            IPEndPoint expectedEndPoint = (mediaType == SDPMediaTypesEnum.audio || m_isMediaMultiplexed) ? AudioDestinationEndPoint : VideoDestinationEndPoint;

            if (expectedEndPoint.Address.Equals(receivedOnEndPoint.Address) && expectedEndPoint.Port == receivedOnEndPoint.Port)
            {
                // Exact match on actual and expected destination.
                isValidSource = true;
            }
            else if (AcceptRtpFromAny || (expectedEndPoint.Address.IsPrivate() && !receivedOnEndPoint.Address.IsPrivate())
               //|| (IPAddress.Loopback.Equals(receivedOnEndPoint.Address) || IPAddress.IPv6Loopback.Equals(receivedOnEndPoint.Address
               )
            {
                // The end point doesn't match BUT we were supplied a private address in the SDP and the remote source is a public address
                // so high probability there's a NAT on the network path. Switch to the remote end point (note this can only happen once
                // and only if the SSRV is 0, i.e. this is the first RTP packet.
                // If the remote end point is a loopback address then it's likely that this is a test/development 
                // scenario and the source can be trusted.
                // AC 12 Jul 2020: Commented out the expression that allows the end point to be change just because it's a loopback address.
                // A breaking case is doing an attended transfer test where two different agents are using loopback addresses. 
                // The expression allows an older session to override the destination set by a newer remote SDP.
                // AC 18 Aug 2020: Despite the carefully crafted rules below and https://github.com/sipsorcery/sipsorcery/issues/197
                // there are still cases that were a problem in one scenario but acceptable in another. To accommodate a new property
                // was added to allow the application to decide whether the RTP end point switches should be liberal or not.
                logger.LogDebug($"{mediaType} end point switched for RTP ssrc {ssrc} from {expectedEndPoint} to {receivedOnEndPoint}.");

                if (mediaType == SDPMediaTypesEnum.audio)
                {
                    AudioDestinationEndPoint = receivedOnEndPoint;
                    if (m_isRtcpMultiplexed)
                    {
                        AudioControlDestinationEndPoint = AudioDestinationEndPoint;
                    }
                    else
                    {
                        AudioControlDestinationEndPoint = new IPEndPoint(AudioDestinationEndPoint.Address, AudioDestinationEndPoint.Port + 1);
                    }
                }
                else
                {
                    VideoDestinationEndPoint = receivedOnEndPoint;
                    if (m_isRtcpMultiplexed)
                    {
                        VideoControlDestinationEndPoint = VideoDestinationEndPoint;
                    }
                    else
                    {
                        VideoControlDestinationEndPoint = new IPEndPoint(VideoControlDestinationEndPoint.Address, VideoControlDestinationEndPoint.Port + 1);
                    }
                }

                isValidSource = true;
            }
            else
            {
                logger.LogWarning($"RTP packet with SSRC {ssrc} received from unrecognised end point {receivedOnEndPoint}.");
            }

            return isValidSource;
        }

        /// <summary>
        /// Attempts to determine which media stream a received RTP packet is for based on the RTP socket
        /// it was received on. This is for cases where media multiplexing is not in use (i.e. legacy RTP).
        /// </summary>
        /// <param name="localPort">The local port the RTP packet was received on.</param>
        /// <returns>The media type for the received packet or null if it could not be determined.</returns>
        private SDPMediaTypesEnum? GetMediaTypeForLocalPort(int localPort)
        {
            if (m_rtpChannels.ContainsKey(SDPMediaTypesEnum.audio) && m_rtpChannels[SDPMediaTypesEnum.audio].RTPPort == localPort)
            {
                return SDPMediaTypesEnum.audio;
            }
            else if (m_rtpChannels.ContainsKey(SDPMediaTypesEnum.video) && m_rtpChannels[SDPMediaTypesEnum.video].RTPPort == localPort)
            {
                return SDPMediaTypesEnum.video;
            }
            else
            {
                return null;
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

            if (AudioRemoteTrack != null && AudioRemoteTrack.IsSsrcMatch(header.SyncSource))
            {
                matchingTrack = AudioRemoteTrack;
            }
            else if (VideoRemoteTrack != null && VideoRemoteTrack.IsSsrcMatch(header.SyncSource))
            {
                matchingTrack = VideoRemoteTrack;
            }
            else if (AudioRemoteTrack != null && AudioRemoteTrack.IsPayloadIDMatch(header.PayloadType))
            {
                matchingTrack = AudioRemoteTrack;
            }
            else if (VideoRemoteTrack != null && VideoRemoteTrack.IsPayloadIDMatch(header.PayloadType))
            {
                matchingTrack = VideoRemoteTrack;
            }
            else if (AudioLocalTrack != null && AudioLocalTrack.IsPayloadIDMatch(header.PayloadType))
            {
                matchingTrack = AudioLocalTrack;
            }
            else if (VideoLocalTrack != null && VideoLocalTrack.IsPayloadIDMatch(header.PayloadType))
            {
                matchingTrack = VideoLocalTrack;
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
                    logger.LogWarning($"An RTP packet with SSRC {header.SyncSource} matched the {matchingTrack.Kind} track but no capabiltity exists for payload ID {header.PayloadType}.");
                    return null;
                }
            }
            else
            {
                logger.LogWarning($"An RTP packet with SSRC {header.SyncSource} and payload ID {header.PayloadType} was received that could not be matched to an audio or video stream.");
                return null;
            }
        }

        /// <summary>
        /// Attempts to get the RTCP session that matches a received RTCP report.
        /// </summary>
        /// <param name="rtcpPkt">The RTCP compound packet received from the remote party.</param>
        /// <returns>If a match could be found an SSRC the RTCP session otherwise null.</returns>
        private RTCPSession GetRtcpSession(RTCPCompoundPacket rtcpPkt)
        {
            if (rtcpPkt.SenderReport != null)
            {
                if (AudioRemoteTrack != null && AudioRemoteTrack.IsSsrcMatch(rtcpPkt.SenderReport.SSRC))
                {
                    return AudioRtcpSession;
                }
                else if (VideoRemoteTrack != null && VideoRemoteTrack.IsSsrcMatch(rtcpPkt.SenderReport.SSRC))
                {
                    return VideoRtcpSession;
                }
            }
            else if (rtcpPkt.ReceiverReport != null)
            {
                if (AudioRemoteTrack != null && AudioRemoteTrack.IsSsrcMatch(rtcpPkt.ReceiverReport.SSRC))
                {
                    return AudioRtcpSession;
                }
                else if (VideoRemoteTrack != null && VideoRemoteTrack.IsSsrcMatch(rtcpPkt.ReceiverReport.SSRC))
                {
                    return VideoRtcpSession;
                }
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
                    if (AudioLocalTrack != null && recRep.SSRC == AudioLocalTrack.Ssrc)
                    {
                        return AudioRtcpSession;
                    }
                    else if (VideoLocalTrack != null && recRep.SSRC == VideoLocalTrack.Ssrc)
                    {
                        return VideoRtcpSession;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Does the actual sending of an RTP packet using the specified data and header values.
        /// </summary>
        /// <param name="rtpChannel">The RTP channel to send from.</param>
        /// <param name="dstRtpSocket">Destination to send to.</param>
        /// <param name="data">The RTP packet payload.</param>
        /// <param name="timestamp">The RTP header timestamp.</param>
        /// <param name="markerBit">The RTP header marker bit.</param>
        /// <param name="payloadType">The RTP header payload type.</param>
        private void SendRtpPacket(RTPChannel rtpChannel, IPEndPoint dstRtpSocket, byte[] data, uint timestamp, int markerBit, int payloadType, uint ssrc, ushort seqNum, RTCPSession rtcpSession)
        {
            if (IsSecure && !IsSecureContextReady)
            {
                logger.LogWarning("SendRtpPacket cannot be called on a secure session before calling SetSecurityContext.");
            }
            else
            {
                int srtpProtectionLength = (m_srtpProtect != null) ? SRTP_MAX_PREFIX_LENGTH : 0;

                RTPPacket rtpPacket = new RTPPacket(data.Length + srtpProtectionLength);
                rtpPacket.Header.SyncSource = ssrc;
                rtpPacket.Header.SequenceNumber = seqNum;
                rtpPacket.Header.Timestamp = timestamp;
                rtpPacket.Header.MarkerBit = markerBit;
                rtpPacket.Header.PayloadType = payloadType;

                Buffer.BlockCopy(data, 0, rtpPacket.Payload, 0, data.Length);

                var rtpBuffer = rtpPacket.GetBytes();

                if (m_srtpProtect == null)
                {
                    rtpChannel.Send(RTPChannelSocketsEnum.RTP, dstRtpSocket, rtpBuffer);
                }
                else
                {
                    int outBufLen = 0;
                    int rtperr = m_srtpProtect(rtpBuffer, rtpBuffer.Length - srtpProtectionLength, out outBufLen);
                    if (rtperr != 0)
                    {
                        logger.LogError("SendRTPPacket protection failed, result " + rtperr + ".");
                    }
                    else
                    {
                        rtpChannel.Send(RTPChannelSocketsEnum.RTP, dstRtpSocket, rtpBuffer.Take(outBufLen).ToArray());
                    }
                }
                m_lastRtpTimestamp = timestamp;

                rtcpSession?.RecordRtpPacketSend(rtpPacket);
            }
        }

        /// <summary>
        /// Sends the RTCP report to the remote call party.
        /// </summary>
        /// <param name="report">RTCP report to send.</param>
        private void SendRtcpReport(SDPMediaTypesEnum mediaType, RTCPCompoundPacket report)
        {
            if (IsSecure && !IsSecureContextReady && report.Bye != null)
            {
                // Do nothing. The RTCP BYE gets generated when an RTP session is closed.
                // If that occurs before the connection was able to set up the secure context
                // there's no point trying to send it.
            }
            else
            {
                var reportBytes = report.GetBytes();
                SendRtcpReport(mediaType, reportBytes);
                OnSendReport?.Invoke(mediaType, report);
            }
        }

        /// <summary>
        /// Sends the RTCP report to the remote call party.
        /// </summary>
        /// <param name="report">The serialised RTCP report to send.</param>
        private void SendRtcpReport(SDPMediaTypesEnum mediaType, byte[] reportBuffer)
        {
            IPEndPoint controlDstEndPoint = null;
            if (m_isMediaMultiplexed || mediaType == SDPMediaTypesEnum.audio)
            {
                controlDstEndPoint = AudioControlDestinationEndPoint;
            }
            else if (mediaType == SDPMediaTypesEnum.video)
            {
                controlDstEndPoint = VideoControlDestinationEndPoint;
            }

            if (IsSecure && !IsSecureContextReady)
            {
                logger.LogWarning("SendRtcpReport cannot be called on a secure session before calling SetSecurityContext.");
            }
            else if (controlDstEndPoint != null)
            {
                //logger.LogDebug($"SendRtcpReport: {reportBytes.HexStr()}");

                var sendOnSocket = (m_isRtcpMultiplexed) ? RTPChannelSocketsEnum.RTP : RTPChannelSocketsEnum.Control;

                var rtpChannel = GetRtpChannel(mediaType);

                if (m_srtcpControlProtect == null)
                {
                    rtpChannel.Send(sendOnSocket, controlDstEndPoint, reportBuffer);
                }
                else
                {
                    byte[] sendBuffer = new byte[reportBuffer.Length + SRTP_MAX_PREFIX_LENGTH];
                    Buffer.BlockCopy(reportBuffer, 0, sendBuffer, 0, reportBuffer.Length);

                    int outBufLen = 0;
                    int rtperr = m_srtcpControlProtect(sendBuffer, sendBuffer.Length - SRTP_MAX_PREFIX_LENGTH, out outBufLen);
                    if (rtperr != 0)
                    {
                        logger.LogWarning("SRTP RTCP packet protection failed, result " + rtperr + ".");
                    }
                    else
                    {
                        rtpChannel.Send(sendOnSocket, controlDstEndPoint, sendBuffer.Take(outBufLen).ToArray());
                    }
                }
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
