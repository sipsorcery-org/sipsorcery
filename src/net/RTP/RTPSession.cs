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
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public delegate int ProtectRtpPacket(byte[] payload, int length, out int outputBufferLength);

    public class MediaStreamTrack
    {
        public SDPMediaTypesEnum Kind { get; private set; }
        public uint Ssrc { get; internal set; }
        public ushort SeqNum { get; internal set; }
        public uint RemoteSsrc { get; internal set; }

        /// <summary>
        /// Indicates whether this track was source by a remote connection.
        /// </summary>
        public bool IsRemote { get; set; }

        /// <summary>
        /// The media capabilities supported by this track.
        /// </summary>
        public List<SDPMediaFormat> Capabilties { get; private set; }

        /// <summary>
        /// Holds the SDP and flow state of the track.
        /// </summary>
        public RTCRtpTransceiver Transceiver { get; private set; }

        /// <summary>
        /// Creates a lightweight class to track a media stream track within an RTP session 
        /// When supporting RFC3550 (the standard RTP specification) the relationship between
        /// an RTP stream and session is 1:1. For WebRTC and RFC8101 there can be multiple
        /// streams per session.
        /// </summary>
        /// <param name="mid">The media ID for this track. Must match the value set in the SDP.</param>
        /// <param name="kind">The type of media for this stream. There can only be one
        /// stream per media type.</param>
        /// <param name="payloadIDs">The list of payload ID's that are being used in RTP packets 
        /// for this media type. Must be mutually exclusive between the audio and video tracks.</param>
        public MediaStreamTrack(string mid, SDPMediaTypesEnum kind, bool isRemote, List<SDPMediaFormat> capabilties)
        {
            Transceiver = new RTCRtpTransceiver(mid);
            Kind = kind;
            IsRemote = isRemote;
            Capabilties = capabilties;

            if (!isRemote)
            {
                InitForSending();
            }
        }

        public void InitForSending()
        {
            Ssrc = Convert.ToUInt32(Crypto.GetRandomInt(0, Int32.MaxValue));
            SeqNum = Convert.ToUInt16(Crypto.GetRandomInt(0, UInt16.MaxValue));
        }

        /// <summary>
        /// Checks whether the payload ID in an RTP packet received from the remote call party
        /// is in this track's list.
        /// </summary>
        /// <param name="payloadID">The payload ID to check against.</param>
        /// <returns>True if the payload ID matches one of the codecs for this stream. False if not.</returns>
        public bool IsPayloadIDMatch(int payloadID)
        {
            return Capabilties.Any(x => x.FormatID == payloadID.ToString());
        }
    }

    /// <summary>
    /// Describes a pairing of an RTP sender and receiver and their shared state. The state
    /// is set by and relevant for the SDP that is controlling the RTP.
    /// </summary>
    public class RTCRtpTransceiver
    {
        /// <summary>
        /// The media ID of the SDP m-line associated with this transceiver.
        /// </summary>
        public string MID { get; private set; }

        /// <summary>
        /// The current state of the RTP flow between us and the remote party.
        /// </summary>
        public MediaStreamStatusEnum Direction { get; private set; } = MediaStreamStatusEnum.SendRecv;

        public RTCRtpTransceiver(string mid)
        {
            MID = mid;
        }

        public void SetStreamStatus(MediaStreamStatusEnum direction)
        {
            Direction = direction;
        }
    }

    public class RTPSession : IDisposable
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
        public const int H264_RTP_HEADER_LENGTH = 2;
        public const int RTP_EVENT_DEFAULT_SAMPLE_PERIOD_MS = 50; // Default sample period for an RTP event as specified by RFC2833.
        public const SDPMediaTypesEnum DEFAULT_MEDIA_TYPE = SDPMediaTypesEnum.audio; // If we can't match an RTP payload ID assume it's audio.

        private static ILogger logger = Log.Logger;

        private int m_mediaID = 0;

        private bool m_isRtcpMultiplexed = false;       // Indicates whether the RTP channel is multiplexing RTP and RTCP packets on the same port.
        //private IPEndPoint m_lastReceiveFromEndPoint;
        private bool m_rtpEventInProgress;               // Gets set to true when an RTP event is being sent and the normal stream is interrupted.
        private uint m_lastRtpTimestamp;                 // The last timestamp used in an RTP packet.    
        private bool m_isClosed;

        internal List<MediaStreamTrack> m_tracks = new List<MediaStreamTrack>();

        /// <summary>
        /// The audio stream for this session. Will be null if only video is being sent.
        /// </summary>
        internal MediaStreamTrack AudioTrack
        {
            get { return m_tracks.SingleOrDefault(x => x.Kind == SDPMediaTypesEnum.audio); }
        }

        /// <summary>
        /// The reporting session for the audio stream. Will be null if only video is being sent.
        /// </summary>
        private RTCPSession m_audioRtcpSession;

        /// <summary>
        /// The video stream for this session. Will be null if only audio is being sent.
        /// </summary>
        internal MediaStreamTrack VideoTrack
        {
            get { return m_tracks.SingleOrDefault(x => x.Kind == SDPMediaTypesEnum.video); }
        }

        /// <summary>
        /// The reporting session for the video stream. Will be null if only audio is being sent.
        /// </summary>
        private RTCPSession m_videoRtcpSession;

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
        /// The RTP communications channel this session is sending and receiving on.
        /// </summary>
        public RTPChannel RtpChannel { get; private set; }

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
        /// The remote RTP end point this session is sending to.
        /// </summary>
        public IPEndPoint DestinationEndPoint { get; private set; }

        /// <summary>
        /// The remote RTP control end point this session is sending to RTCP reports to.
        /// </summary>
        public IPEndPoint ControlDestinationEndPoint { get; private set; }

        /// <summary>
        /// In order to detect RTP events from the remote party this property needs to 
        /// be set to the payload ID they are using.
        /// </summary>
        public int RemoteRtpEventPayloadID { get; set; }

        /// <summary>
        /// Gets fired when the session detects that the remote end point 
        /// has changed. This is useful because the RTP socket advertised in an SDP
        /// payload will often be different to the one the packets arrive from due
        /// to NAT.
        /// 
        /// The parameters for the event are:
        ///  - Original remote end point,
        ///  - Most recent remote end point.
        /// </summary>
        //public event Action<IPEndPoint, IPEndPoint> OnReceiveFromEndPointChanged;

        /// <summary>
        /// Gets fired when an RTP packet is received from a remote party.
        /// </summary>
        public event Action<SDPMediaTypesEnum, RTPPacket> OnRtpPacketReceived;

        /// <summary>
        /// Gets fired when an RTP event is detected on the remote call party's RTP stream.
        /// </summary>
        public event Action<RTPEvent> OnRtpEvent;

        /// <summary>
        /// Gets fired when the RTP session and underlying channel are closed.
        /// </summary>
        public event Action<string> OnRtpClosed;

        /// <summary>
        /// Gets fired when an RTCP report is received. This event is for diagnostics only.
        /// </summary>
        public event Action<SDPMediaTypesEnum, RTCPCompoundPacket> OnReceiveReport;

        /// <summary>
        /// Gets fired when an RTCP report is sent. This event is for diagnostics only.
        /// </summary>
        public event Action<SDPMediaTypesEnum, RTCPCompoundPacket> OnSendReport;

        /// <summary>
        /// Creates a new RTP session. The synchronisation source and sequence number are initialised to
        /// pseudo random values.
        /// </summary>
        /// <param name="addrFamily">Determines whether the RTP channel will use an IPv4 or IPv6 socket.</param>
        /// <param name="isRtcpMultiplexed">If true RTCP reports will be multiplexed with RTP on a single channel.
        /// If false (standard mode) then a separate socket is used to send and receive RTCP reports.</param>
        /// <param name="isSecure">If true indicated this session is using SRTP to encrypt and authorise
        /// RTP and RTCP packets. No communications or reporting will commence until the 
        /// is explicitly set as complete.</param>
        public RTPSession(
            AddressFamily addrFamily,
            bool isRtcpMultiplexed,
            bool isSecure)
        {
            m_isRtcpMultiplexed = isRtcpMultiplexed;
            IsSecure = isSecure;

            var channelAddress = (addrFamily == AddressFamily.InterNetworkV6) ? IPAddress.IPv6Any : IPAddress.Any;
            RtpChannel = new RTPChannel(channelAddress, !isRtcpMultiplexed);

            RtpChannel.OnRTPDataReceived += OnReceive;
            RtpChannel.OnControlDataReceived += OnReceive; // RTCP packets could come on RTP or control socket.
            RtpChannel.OnClosed += OnRTPChannelClosed;

            // Start the RTP, and if required the Control, socket receivers and the RTCP session.
            RtpChannel.Start();
        }

        /// <summary>
        /// Creates a new RTP session. The synchronisation source and sequence number are initialised to
        /// pseudo random values. This constructor also adds a default stream to the session. Typically this
        /// is suitable for sessions that only have a single stream per session such as a standard SIP call.
        /// </summary>
        /// <param name="mediaType">The default media type for this RTP session. If media multiplexing
        /// is being used additional streams can be added by calling AddStream.</param>
        /// <param name="payloadTypeID">The payload type ID for this RTP stream. It's what gets set in the payload 
        /// type ID field in the RTP header.</param>
        /// <param name="addrFamily">Determines whether the RTP channel will use an IPv4 or IPv6 socket.</param>
        /// <param name="isRtcpMultiplexed">If true RTCP reports will be multiplexed with RTP on a single channel.
        /// If false (standard mode) then a separate socket is used to send and receive RTCP reports.</param>
        /// <param name="isSecure">If true indicated this session is using SRTP to encrypt and authorise
        /// RTP and RTCP packets. No communications or reporting will commence until the 
        /// is explicitly set as complete.</param>
        public RTPSession(
            SDPMediaTypesEnum mediaType,
            SDPMediaFormat mediaFormat,
            AddressFamily addrFamily,
            bool isRtcpMultiplexed,
            bool isSecure) : this(addrFamily, isRtcpMultiplexed, isSecure)
        {
            AddTrack(m_mediaID.ToString(), mediaType, false, new List<SDPMediaFormat> { mediaFormat });
            m_mediaID++;
        }

        public string GetNextMediaID()
        {
            string mid = m_mediaID.ToString();
            m_mediaID++;
            return mid;
        }

        /// <summary>
        /// Adds a media track to this session. If both an audio and video track are added
        /// to a session they will be multiplexed on the same socket pair. Multiplexing is used by WebRTC.
        /// This class needs to know the payload IDs for audio and video media types but does not need to 
        /// know what they mean. It's up to consuming classes to extract the audio and video payloads.
        /// </summary>
        /// <param name="mid">The media ID that will be sued in the SDP media announcement for this track.</param>
        /// <param name="mediaType">The type of media for this stream. When multiplexing streams on an
        /// RTP session there can be only one stream per media type.</param>
        /// <param name="isRemote">Set to true if the stream being added has been initiated from a remote
        /// party. Set to false to indicate this is a stream originating from us.</param>
        /// <param name="capabilties">The capabilities for the track being added. Where the same media
        /// type is supported locally and remotely only the mutual capabilities can be used. This will
        /// occur if we receive an SDP offer (add track initiated by the remote party) and we need
        /// to remove capabilities we don't support.</param>
        public MediaStreamTrack AddTrack(string mid, SDPMediaTypesEnum mediaType, bool isRemote, List<SDPMediaFormat> capabilties)
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                return AddTrack(AudioTrack, mid, mediaType, isRemote, capabilties);
            }
            else if (mediaType == SDPMediaTypesEnum.video)
            {
                return AddTrack(VideoTrack, mid, mediaType, isRemote, capabilties);
            }
            else
            {
                throw new ApplicationException($"The RTPSession does not know how to transmit media type {mediaType}.");
            }
        }

        private MediaStreamTrack AddTrack(MediaStreamTrack existingTrack, string mid, SDPMediaTypesEnum mediaType, bool isRemote, List<SDPMediaFormat> capabilties)
        {
            var track = existingTrack;
            if (track == null)
            {
                track = new MediaStreamTrack(mid, mediaType, isRemote, capabilties);

                if (mediaType == SDPMediaTypesEnum.audio && m_audioRtcpSession == null)
                {
                    m_audioRtcpSession = new RTCPSession(mediaType, (!isRemote) ? track.Ssrc : 0);
                }
                else if (mediaType == SDPMediaTypesEnum.video && m_videoRtcpSession == null)
                {
                    m_videoRtcpSession = new RTCPSession(mediaType, (!isRemote) ? track.Ssrc : 0);
                }

                RTCPSession rtcpSession = (mediaType == SDPMediaTypesEnum.audio) ? m_audioRtcpSession : m_videoRtcpSession;

                rtcpSession.OnReportReadyToSend += SendRtcpReport;
                if (!IsSecure)
                {
                    rtcpSession.Start();
                }

                if (isRemote)
                {
                    track.Transceiver.SetStreamStatus(MediaStreamStatusEnum.RecvOnly);
                }

                m_tracks.Add(track);
            }
            else
            {
                // Adding local configuration to an audio track that was initially added by the remote party.
                if (!isRemote && track.IsRemote)
                {
                    // We're adding a local track to the existing remote track which means we'll be sending.
                    track.Transceiver.SetStreamStatus(MediaStreamStatusEnum.SendRecv);

                    // We need to filter the capabilities to the list of what we both support.
                    for (int i = 0; i < track.Capabilties.Count; i++)
                    {
                        if (!capabilties.Any(x => x.FormatCodec == track.Capabilties[i].FormatCodec))
                        {
                            track.Capabilties.RemoveAt(i);
                            i--;
                        }
                    }

                    if (track.Capabilties.Count == 0)
                    {
                        throw new ApplicationException($"There were no matching capabilities for local and remote {mediaType} media stream.");
                    }

                    // Need to create a sending SSRC and set it on the RTCP session. 
                    track.InitForSending();

                    RTCPSession rtcpSession = (mediaType == SDPMediaTypesEnum.audio) ? m_audioRtcpSession : m_videoRtcpSession;
                    rtcpSession.Ssrc = track.Ssrc;
                }

                // TODO: If the local track was added first then theoretically the remote part should
                // have only sent us capabilities we support. Add logic to verify this.
            }

            return track;
        }

        /// <summary>
        /// Sets the Secure RTP (SRTP) delegates and marks this session as ready for communications.
        /// </summary>
        /// <param name="protectRtp">SRTP encrypt RTP packet delegate.</param>
        /// <param name="unprotectRtp">SRTP decrypt RTP packet delegate.</param>
        /// <param name="protectRtcp">SRTP encrypt RTCP packet delegate.</param>
        /// <param name="unprotectRtcp">SRTP decrypt RTCP packet delegate.</param>
        public void SetSecurityContext(
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

            // Start the reporting sessions.
            m_audioRtcpSession?.Start();
            m_videoRtcpSession?.Start();

            logger.LogDebug("Secure context successfully set on RTPSession.");
        }

        public void SetDestination(IPEndPoint rtpEndPoint, IPEndPoint rtcpEndPoint)
        {
            DestinationEndPoint = rtpEndPoint;
            ControlDestinationEndPoint = rtcpEndPoint;
        }

        public void SendAudioFrame(uint timestamp, int payloadTypeID, byte[] buffer)
        {
            if (m_isClosed || m_rtpEventInProgress || DestinationEndPoint == null)
            {
                return;
            }

            try
            {
                var audioTrack = m_tracks.Where(x => x.Kind == SDPMediaTypesEnum.audio && !x.IsRemote).FirstOrDefault();

                if (audioTrack == null)
                {
                    logger.LogWarning("SendAudio was called on an RTP session without an audio stream.");
                }
                else
                {
                    for (int index = 0; index * RTP_MAX_PAYLOAD < buffer.Length; index++)
                    {
                        audioTrack.SeqNum = (ushort)(audioTrack.SeqNum % UInt16.MaxValue);

                        int offset = (index == 0) ? 0 : (index * RTP_MAX_PAYLOAD);
                        int payloadLength = (offset + RTP_MAX_PAYLOAD < buffer.Length) ? RTP_MAX_PAYLOAD : buffer.Length - offset;
                        byte[] payload = new byte[payloadLength];

                        Buffer.BlockCopy(buffer, offset, payload, 0, payloadLength);

                        // RFC3551 specifies that for audio the marker bit should always be 0 except for when returning
                        // from silence suppression. For video the marker bit DOES get set to 1 for the last packet
                        // in a frame.
                        int markerBit = 0;

                        SendRtpPacket(RtpChannel, DestinationEndPoint, payload, timestamp, markerBit, payloadTypeID, audioTrack.Ssrc, audioTrack.SeqNum++, m_audioRtcpSession);
                    }
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
        public void SendVp8Frame(uint timestamp, int payloadTypeID, byte[] buffer)
        {
            if (m_isClosed || m_rtpEventInProgress || DestinationEndPoint == null)
            {
                return;
            }

            try
            {
                var videoTrack = m_tracks.Where(x => x.Kind == SDPMediaTypesEnum.video && !x.IsRemote).FirstOrDefault();

                if (videoTrack == null)
                {
                    logger.LogWarning("SendVp8Frame was called on an RTP session without a video stream.");
                }
                else
                {
                    for (int index = 0; index * RTP_MAX_PAYLOAD < buffer.Length; index++)
                    {
                        videoTrack.SeqNum = (ushort)(videoTrack.SeqNum % UInt16.MaxValue);

                        int offset = index * RTP_MAX_PAYLOAD;
                        int payloadLength = (offset + RTP_MAX_PAYLOAD < buffer.Length) ? RTP_MAX_PAYLOAD : buffer.Length - offset;

                        byte[] vp8HeaderBytes = (index == 0) ? new byte[] { 0x10 } : new byte[] { 0x00 };
                        byte[] payload = new byte[payloadLength + vp8HeaderBytes.Length];
                        Buffer.BlockCopy(vp8HeaderBytes, 0, payload, 0, vp8HeaderBytes.Length);
                        Buffer.BlockCopy(buffer, offset, payload, vp8HeaderBytes.Length, payloadLength);

                        int markerBit = ((offset + payloadLength) >= buffer.Length) ? 1 : 0; // Set marker bit for the last packet in the frame.

                        SendRtpPacket(RtpChannel, DestinationEndPoint, payload, timestamp, markerBit, payloadTypeID, videoTrack.Ssrc, videoTrack.SeqNum++, m_videoRtcpSession);
                    }
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
        public void SendJpegFrame(uint timestamp, int payloadTypeID, byte[] jpegBytes, int jpegQuality, int jpegWidth, int jpegHeight)
        {
            if (m_isClosed || m_rtpEventInProgress || DestinationEndPoint == null)
            {
                return;
            }

            try
            {
                var videoTrack = m_tracks.Where(x => x.Kind == SDPMediaTypesEnum.video && !x.IsRemote).FirstOrDefault();

                if (videoTrack == null)
                {
                    logger.LogWarning("SendJpegFrame was called on an RTP session without a video stream.");
                }
                else
                {
                    for (int index = 0; index * RTP_MAX_PAYLOAD < jpegBytes.Length; index++)
                    {
                        uint offset = Convert.ToUInt32(index * RTP_MAX_PAYLOAD);
                        int payloadLength = ((index + 1) * RTP_MAX_PAYLOAD < jpegBytes.Length) ? RTP_MAX_PAYLOAD : jpegBytes.Length - index * RTP_MAX_PAYLOAD;
                        byte[] jpegHeader = CreateLowQualityRtpJpegHeader(offset, jpegQuality, jpegWidth, jpegHeight);

                        List<byte> packetPayload = new List<byte>();
                        packetPayload.AddRange(jpegHeader);
                        packetPayload.AddRange(jpegBytes.Skip(index * RTP_MAX_PAYLOAD).Take(payloadLength));

                        int markerBit = ((index + 1) * RTP_MAX_PAYLOAD < jpegBytes.Length) ? 0 : 1; ;
                        SendRtpPacket(RtpChannel, DestinationEndPoint, packetPayload.ToArray(), timestamp, markerBit, payloadTypeID, videoTrack.Ssrc, videoTrack.SeqNum++, m_videoRtcpSession);
                    }
                }
            }
            catch (SocketException sockExcp)
            {
                logger.LogError("SocketException SendJpegFrame. " + sockExcp.Message);
            }
        }

        /// <summary>
        /// H264 frames need a two byte header when transmitted over RTP.
        /// </summary>
        /// <param name="frame">The H264 encoded frame to transmit.</param>
        /// <param name="frameSpacing">The increment to add to the RTP timestamp for each new frame.</param>
        /// <param name="payloadType">The payload type to set on the RTP packet.</param>
        public void SendH264Frame(uint timestamp, int payloadTypeID, byte[] frame)
        {
            if (m_isClosed || m_rtpEventInProgress || DestinationEndPoint == null)
            {
                return;
            }

            try
            {
                var videoTrack = m_tracks.Where(x => x.Kind == SDPMediaTypesEnum.video && !x.IsRemote).FirstOrDefault();

                if (videoTrack == null)
                {
                    logger.LogWarning("SendH264Frame was called on an RTP session without a video stream.");
                }
                else
                {
                    for (int index = 0; index * RTP_MAX_PAYLOAD < frame.Length; index++)
                    {
                        int offset = index * RTP_MAX_PAYLOAD;
                        int payloadLength = ((index + 1) * RTP_MAX_PAYLOAD < frame.Length) ? RTP_MAX_PAYLOAD : frame.Length - index * RTP_MAX_PAYLOAD;
                        byte[] payload = new byte[payloadLength + H264_RTP_HEADER_LENGTH];

                        // Start RTP packet in frame 0x1c 0x89
                        // Middle RTP packet in frame 0x1c 0x09
                        // Last RTP packet in frame 0x1c 0x49

                        int markerBit = 0;
                        byte[] h264Header = new byte[] { 0x1c, 0x09 };

                        if (index == 0 && frame.Length < RTP_MAX_PAYLOAD)
                        {
                            // First and last RTP packet in the frame.
                            h264Header = new byte[] { 0x1c, 0x49 };
                            markerBit = 1;
                        }
                        else if (index == 0)
                        {
                            h264Header = new byte[] { 0x1c, 0x89 };
                        }
                        else if ((index + 1) * RTP_MAX_PAYLOAD > frame.Length)
                        {
                            h264Header = new byte[] { 0x1c, 0x49 };
                            markerBit = 1;
                        }

                        var h264Stream = frame.Skip(index * RTP_MAX_PAYLOAD).Take(payloadLength).ToList();
                        h264Stream.InsertRange(0, h264Header);

                        Buffer.BlockCopy(h264Header, 0, payload, 0, H264_RTP_HEADER_LENGTH);
                        Buffer.BlockCopy(frame, offset, payload, H264_RTP_HEADER_LENGTH, payloadLength);

                        SendRtpPacket(RtpChannel, DestinationEndPoint, payload, timestamp, markerBit, payloadTypeID, videoTrack.Ssrc, videoTrack.SeqNum++, m_videoRtcpSession);
                    }
                }
            }
            catch (SocketException sockExcp)
            {
                logger.LogError("SocketException SendH264Frame. " + sockExcp.Message);
            }
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
            if (m_isClosed || m_rtpEventInProgress == true || DestinationEndPoint == null)
            {
                logger.LogWarning("SendDtmfEvent request ignored as an RTP event is already in progress.");
            }

            try
            {
                var audioTrack = m_tracks.Where(x => x.Kind == SDPMediaTypesEnum.audio && !x.IsRemote).FirstOrDefault();

                if (audioTrack == null)
                {
                    logger.LogWarning("SendDtmfEvent was called on an RTP session without an audio stream.");
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
                        SendRtpPacket(RtpChannel, DestinationEndPoint, buffer, startTimestamp, markerBit, rtpEvent.PayloadTypeID, audioTrack.Ssrc, audioTrack.SeqNum, m_audioRtcpSession);

                        audioTrack.SeqNum++;
                    }

                    await Task.Delay(samplePeriod, cancellationToken);

                    if (!rtpEvent.EndOfEvent)
                    {
                        // Send the progressive event packets 
                        while ((rtpEvent.Duration + rtpTimestampStep) < rtpEvent.TotalDuration && !cancellationToken.IsCancellationRequested)
                        {
                            rtpEvent.Duration += rtpTimestampStep;
                            byte[] buffer = rtpEvent.GetEventPayload();

                            SendRtpPacket(RtpChannel, DestinationEndPoint, buffer, startTimestamp, 0, rtpEvent.PayloadTypeID, audioTrack.Ssrc, audioTrack.SeqNum, m_audioRtcpSession);

                            audioTrack.SeqNum++;

                            await Task.Delay(samplePeriod, cancellationToken);
                        }

                        // Send the end of event packets.
                        for (int j = 0; j < RTPEvent.DUPLICATE_COUNT && !cancellationToken.IsCancellationRequested; j++)
                        {
                            rtpEvent.EndOfEvent = true;
                            rtpEvent.Duration = rtpEvent.TotalDuration;
                            byte[] buffer = rtpEvent.GetEventPayload();

                            SendRtpPacket(RtpChannel, DestinationEndPoint, buffer, startTimestamp, 0, rtpEvent.PayloadTypeID, audioTrack.Ssrc, audioTrack.SeqNum, m_audioRtcpSession);

                            audioTrack.SeqNum++;
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
        /// Close the session and RTP channel.
        /// </summary>
        public void CloseSession(string reason)
        {
            if (!m_isClosed)
            {
                m_isClosed = true;

                m_audioRtcpSession?.Close(reason);
                m_videoRtcpSession?.Close(reason);

                if (RtpChannel != null)
                {
                    RtpChannel.OnRTPDataReceived -= OnReceive;
                    RtpChannel.OnControlDataReceived -= OnReceive;
                    RtpChannel.OnClosed -= OnRTPChannelClosed;
                    RtpChannel.Close(reason);
                }

                OnRtpClosed?.Invoke(reason);
            }
        }

        /// <summary>
        /// Event handler for receiving data on the RTP and Control channels. For multiplexed
        /// sessions both RTP and RTCP packets will be received on the RTP channel.
        /// </summary>
        /// <param name="remoteEndPoint">The remote end point the data was received from.</param>
        /// <param name="buffer">The data received.</param>
        private void OnReceive(IPEndPoint remoteEndPoint, byte[] buffer)
        {
            //if (m_lastReceiveFromEndPoint == null || !m_lastReceiveFromEndPoint.Equals(remoteEndPoint))
            //{
            //    OnReceiveFromEndPointChanged?.Invoke(m_lastReceiveFromEndPoint, remoteEndPoint);
            //    m_lastReceiveFromEndPoint = remoteEndPoint;
            //}

            // Quick sanity check on whether this is not an RTP or RTCP packet.
            if (buffer?.Length > RTPHeader.MIN_HEADER_LEN && buffer[0] >= 128 && buffer[0] <= 191)
            {
                if (IsSecure && !IsSecureContextReady)
                {
                    logger.LogWarning("RTP or RTCP packet received before secure context ready.");
                }
                else if (buffer[1] == 0xC8 /* RTCP SR */ || buffer[1] == 0xC9 /* RTCP RR */)
                {
                    //logger.LogDebug($"RTCP packet received from {remoteEndPoint} before: {buffer.HexStr()}");

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
                            logger.LogDebug($"RTCP BYE received for SSRC {rtcpPkt.Bye.SSRC}.");
                            CloseSession("rtcp bye received.");
                        }
                        else if (!m_isClosed)
                        {
                            var rtcpSession = GetRtcpSession(rtcpPkt);
                            if (rtcpSession != null)
                            {
                                rtcpSession.ReportReceived(remoteEndPoint, rtcpPkt);
                                OnReceiveReport?.Invoke(rtcpSession.MediaType, rtcpPkt);
                            }
                            else
                            {
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

                    if (!m_isClosed)
                    {
                        if (m_srtpUnprotect != null)
                        {
                            int outBufLen = 0;
                            int res = m_srtpUnprotect(buffer, buffer.Length, out outBufLen);

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

                        SDPMediaTypesEnum? rtpMediaType = GetMediaTypeForRtpPacket(rtpPacket.Header);

                        if (RemoteRtpEventPayloadID != 0 && rtpPacket.Header.PayloadType == RemoteRtpEventPayloadID)
                        {
                            RTPEvent rtpEvent = new RTPEvent(rtpPacket.Payload);
                            OnRtpEvent?.Invoke(rtpEvent);
                        }
                        else
                        {
                            SDPMediaTypesEnum mediaType = (rtpMediaType.HasValue) ? rtpMediaType.Value : DEFAULT_MEDIA_TYPE;
                            OnRtpPacketReceived?.Invoke(mediaType, rtpPacket);
                        }

                        // Used for reporting purposes.
                        if (rtpMediaType == SDPMediaTypesEnum.audio && m_audioRtcpSession != null)
                        {
                            m_audioRtcpSession.RecordRtpPacketReceived(rtpPacket);
                        }
                        else if (rtpMediaType == SDPMediaTypesEnum.video && m_videoRtcpSession != null)
                        {
                            m_videoRtcpSession.RecordRtpPacketReceived(rtpPacket);
                        }
                    }

                    #endregion
                }
            }
        }

        /// <summary>
        /// Attempts to determine which media stream a received RTP packet is for.
        /// </summary>
        /// <param name="header">The header of the received RTP packet.</param>
        /// <returns>The media type for the received packet or null if it could not be determined.</returns>
        private SDPMediaTypesEnum? GetMediaTypeForRtpPacket(RTPHeader header)
        {
            if (AudioTrack != null && AudioTrack.RemoteSsrc == header.SyncSource)
            {
                return SDPMediaTypesEnum.audio;
            }
            else if (VideoTrack != null && VideoTrack.RemoteSsrc == header.SyncSource)
            {
                return SDPMediaTypesEnum.video;
            }
            else if (AudioTrack != null && AudioTrack.IsPayloadIDMatch(header.PayloadType))
            {
                if (AudioTrack.RemoteSsrc == 0)
                {
                    logger.LogDebug($"Set remote audio track SSRC to {header.SyncSource}.");
                    AudioTrack.RemoteSsrc = header.SyncSource;
                    return SDPMediaTypesEnum.audio;
                }
            }
            else if (VideoTrack != null && VideoTrack.IsPayloadIDMatch(header.PayloadType))
            {
                if (VideoTrack.RemoteSsrc == 0)
                {
                    logger.LogDebug($"Set remote video track SSRC to {header.SyncSource}.");
                    VideoTrack.RemoteSsrc = header.SyncSource;
                    return SDPMediaTypesEnum.video;
                }
            }

            logger.LogWarning($"An RTP packet with payload ID {header.PayloadType} was received that could not be matched to an audio or video stream.");
            return null;
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
                if (AudioTrack != null && rtcpPkt.SenderReport.SSRC == AudioTrack.RemoteSsrc)
                {
                    return m_audioRtcpSession;
                }
                else if (VideoTrack != null && rtcpPkt.SenderReport.SSRC == VideoTrack.RemoteSsrc)
                {
                    return m_videoRtcpSession;
                }
            }
            else if (rtcpPkt.ReceiverReport != null)
            {
                if (AudioTrack != null && rtcpPkt.ReceiverReport.SSRC == AudioTrack.Ssrc)
                {
                    return m_audioRtcpSession;
                }
                else if (VideoTrack != null && rtcpPkt.ReceiverReport.SSRC == VideoTrack.Ssrc)
                {
                    return m_videoRtcpSession;
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
                    if (AudioTrack != null && recRep.SSRC == AudioTrack.Ssrc)
                    {
                        return m_audioRtcpSession;
                    }
                    else if (VideoTrack != null && recRep.SSRC == VideoTrack.Ssrc)
                    {
                        return m_videoRtcpSession;
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
                    rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, dstRtpSocket, rtpBuffer);
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
                        rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, dstRtpSocket, rtpBuffer.Take(outBufLen).ToArray());
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
            if (IsSecure && !IsSecureContextReady)
            {
                logger.LogWarning("SendRtcpReport cannot be called on a secure session before calling SetSecurityContext.");
            }
            else if (ControlDestinationEndPoint != null)
            {
                var reportBytes = report.GetBytes();

                //logger.LogDebug($"SendRtcpReport: {reportBytes.HexStr()}");

                var sendOnSocket = (m_isRtcpMultiplexed) ? RTPChannelSocketsEnum.RTP : RTPChannelSocketsEnum.Control;

                if (m_srtcpControlProtect == null)
                {
                    RtpChannel.SendAsync(sendOnSocket, ControlDestinationEndPoint, reportBytes);
                }
                else
                {
                    byte[] sendBuffer = new byte[reportBytes.Length + SRTP_MAX_PREFIX_LENGTH];
                    Buffer.BlockCopy(reportBytes, 0, sendBuffer, 0, reportBytes.Length);

                    int outBufLen = 0;
                    int rtperr = m_srtcpControlProtect(sendBuffer, sendBuffer.Length - SRTP_MAX_PREFIX_LENGTH, out outBufLen);
                    if (rtperr != 0)
                    {
                        logger.LogWarning("SRTP RTCP packet protection failed, result " + rtperr + ".");
                    }
                    else
                    {
                        RtpChannel.SendAsync(sendOnSocket, ControlDestinationEndPoint, sendBuffer.Take(outBufLen).ToArray());
                    }
                }

                OnSendReport?.Invoke(mediaType, report);
            }
        }

        /// <summary>
        /// Utility function to create RtpJpegHeader either for initial packet or template for further packets
        /// 
        /// <code>
        /// 0                   1                   2                   3
        /// 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// | Type-specific |              Fragment Offset                  |
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// |      Type     |       Q       |     Width     |     Height    |
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// </code>
        /// </summary>
        /// <param name="fragmentOffset"></param>
        /// <param name="quality"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        private static byte[] CreateLowQualityRtpJpegHeader(uint fragmentOffset, int quality, int width, int height)
        {
            byte[] rtpJpegHeader = new byte[8] { 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00 };

            // Byte 0: Type specific
            //http://tools.ietf.org/search/rfc2435#section-3.1.1

            // Bytes 1 to 3: Three byte fragment offset
            //http://tools.ietf.org/search/rfc2435#section-3.1.2

            if (BitConverter.IsLittleEndian)
            {
                fragmentOffset = NetConvert.DoReverseEndian(fragmentOffset);
            }

            byte[] offsetBytes = BitConverter.GetBytes(fragmentOffset);
            rtpJpegHeader[1] = offsetBytes[2];
            rtpJpegHeader[2] = offsetBytes[1];
            rtpJpegHeader[3] = offsetBytes[0];

            // Byte 4: JPEG Type.
            //http://tools.ietf.org/search/rfc2435#section-3.1.3

            //Byte 5: http://tools.ietf.org/search/rfc2435#section-3.1.4 (Q)
            rtpJpegHeader[5] = (byte)quality;

            // Byte 6: http://tools.ietf.org/search/rfc2435#section-3.1.5 (Width)
            rtpJpegHeader[6] = (byte)(width / 8);

            // Byte 7: http://tools.ietf.org/search/rfc2435#section-3.1.6 (Height)
            rtpJpegHeader[7] = (byte)(height / 8);

            return rtpJpegHeader;
        }

        /// <summary>
        /// Event handler for the RTP channel closure.
        /// </summary>
        private void OnRTPChannelClosed(string reason)
        {
            CloseSession(reason);
        }

        protected virtual void Dispose(bool disposing)
        {
            CloseSession(null);
        }

        public void Dispose()
        {
            CloseSession(null);
        }
    }
}
