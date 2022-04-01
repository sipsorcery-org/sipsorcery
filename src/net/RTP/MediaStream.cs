using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.net.RTP
{

    public class AudioStream : MediaStream
    {
        private static ILogger logger = Log.Logger;

        /// <summary>
        /// Gets fired when the remote SDP is received and the set of common audio formats is set.
        /// </summary>
        public event Action<List<AudioFormat>> OnAudioFormatsNegotiated;

        public void CheckAudioFormatsNegotiation()
        {
            if(LocalTrack != null &&
                        LocalTrack.Capabilities.Where(x => x.Name().ToLower() != SDP.TELEPHONE_EVENT_ATTRIBUTE).Count() > 0)
            {
                OnAudioFormatsNegotiated?.Invoke(
                            LocalTrack.Capabilities
                            .Where(x => x.Name().ToLower() != SDP.TELEPHONE_EVENT_ATTRIBUTE)
                            .Select(x => x.ToAudioFormat()).ToList());
            }
        }

        public AudioStream(RtpSessionConfig config) : base(config)
        {
            MediaType = SDPMediaTypesEnum.audio;
        }

        private Boolean rtpEventInProgress = false;

        /// <summary>
        /// Sends an audio sample to the remote peer.
        /// </summary>
        /// <param name="durationRtpUnits">The duration in RTP timestamp units of the audio sample. This
        /// value is added to the previous RTP timestamp when building the RTP header.</param>
        /// <param name="sample">The audio sample to set as the RTP packet payload.</param>
        public void SendAudio(uint durationRtpUnits, byte[] sample)
        {
            if (DestinationEndPoint != null && ((!IsSecure && !UseSdpCryptoNegotiation) || IsSecurityContextReady()))
            {
                var audioFormat = GetSendingFormat();
                SendAudioFrame(durationRtpUnits, audioFormat.ID, sample);
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
            if (IsClosed || rtpEventInProgress || DestinationEndPoint == null || buffer == null || buffer.Length == 0)
            {
                return;
            }

            try
            {
                var audioTrack = LocalTrack;

                if (audioTrack == null)
                {
                    logger.LogWarning("SendAudio was called on an RTP session without an audio stream.");
                }
                else if (!HasAudio || audioTrack.StreamStatus == MediaStreamStatusEnum.RecvOnly)
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

                    for (int index = 0; index * RTPSession.RTP_MAX_PAYLOAD < buffer.Length; index++)
                    {
                        int offset = (index == 0) ? 0 : (index * RTPSession.RTP_MAX_PAYLOAD);
                        int payloadLength = (offset + RTPSession.RTP_MAX_PAYLOAD < buffer.Length) ? RTPSession.RTP_MAX_PAYLOAD : buffer.Length - offset;
                        payloadTimestamp += payloadDuration;
                        byte[] payload = new byte[payloadLength];

                        Buffer.BlockCopy(buffer, offset, payload, 0, payloadLength);

                        // RFC3551 specifies that for audio the marker bit should always be 0 except for when returning
                        // from silence suppression. For video the marker bit DOES get set to 1 for the last packet
                        // in a frame.
                        int markerBit = 0;

                        var audioRtpChannel = rtpChannel;
                        var protectRtpPacket = GetSecurityContext()?.ProtectRtpPacket;
                        SendRtpPacket(audioRtpChannel, DestinationEndPoint, payload, payloadTimestamp, markerBit, payloadTypeID, audioTrack.Ssrc, audioTrack.GetNextSeqNum(), RtcpSession, protectRtpPacket);

                        //logger.LogDebug($"send audio { audioRtpChannel.RTPLocalEndPoint}->{AudioDestinationEndPoint}.");

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
        /// Sends an RTP event for a DTMF tone as per RFC2833. Sending the event requires multiple packets to be sent.
        /// This method will hold onto the socket until all the packets required for the event have been sent. The send
        /// can be cancelled using the cancellation token.
        /// </summary>
        /// <param name="rtpEvent">The RTP event to send.</param>
        /// <param name="cancellationToken">CancellationToken to allow the operation to be cancelled prematurely.</param>
        /// <param name="clockRate">To send an RTP event the clock rate of the underlying stream needs to be known.</param>
        /// <param name="samplePeriod">The sample period in milliseconds being used for the media stream that the event 
        /// is being inserted into. Should be set to 50ms if main media stream is dynamic or sample period is unknown.</param>
        public async Task SendDtmfEvent(
            RTPEvent rtpEvent,
            CancellationToken cancellationToken,
            int clockRate = RTPSession.DEFAULT_AUDIO_CLOCK_RATE,
            int samplePeriod = RTPSession.RTP_EVENT_DEFAULT_SAMPLE_PERIOD_MS)
        {
            var dstEndPoint = DestinationEndPoint;

            if (IsClosed || rtpEventInProgress == true || dstEndPoint == null)
            {
                logger.LogWarning("SendDtmfEvent request ignored as an RTP event is already in progress.");
            }

            try
            {
                var audioTrack = LocalTrack;

                if (audioTrack == null)
                {
                    logger.LogWarning("SendDtmfEvent was called on an RTP session without an audio stream.");
                }
                else if (!HasAudio || audioTrack.StreamStatus == MediaStreamStatusEnum.RecvOnly)
                {
                    return;
                }
                else
                {
                    rtpEventInProgress = true;
                    uint startTimestamp = audioTrack.Timestamp;

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
                        var protectRtpPacket = GetSecurityContext()?.ProtectRtpPacket;
                        SendRtpPacket(rtpChannel, dstEndPoint, buffer, startTimestamp, markerBit, rtpEvent.PayloadTypeID, audioTrack.Ssrc, audioTrack.GetNextSeqNum(), RtcpSession, protectRtpPacket);
                    }

                    await Task.Delay(samplePeriod, cancellationToken).ConfigureAwait(false);

                    if (!rtpEvent.EndOfEvent)
                    {
                        // Send the progressive event packets 
                        while ((rtpEvent.Duration + rtpTimestampStep) < rtpEvent.TotalDuration && !cancellationToken.IsCancellationRequested)
                        {
                            rtpEvent.Duration += rtpTimestampStep;
                            byte[] buffer = rtpEvent.GetEventPayload();

                            var protectRtpPacket = GetSecurityContext()?.ProtectRtpPacket;
                            SendRtpPacket(rtpChannel, dstEndPoint, buffer, startTimestamp, 0, rtpEvent.PayloadTypeID, audioTrack.Ssrc, audioTrack.GetNextSeqNum(), RtcpSession, protectRtpPacket);

                            await Task.Delay(samplePeriod, cancellationToken).ConfigureAwait(false);
                        }

                        // Send the end of event packets.
                        for (int j = 0; j < RTPEvent.DUPLICATE_COUNT && !cancellationToken.IsCancellationRequested; j++)
                        {
                            rtpEvent.EndOfEvent = true;
                            rtpEvent.Duration = rtpEvent.TotalDuration;
                            byte[] buffer = rtpEvent.GetEventPayload();

                            var protectRtpPacket = GetSecurityContext()?.ProtectRtpPacket;
                            SendRtpPacket(rtpChannel, dstEndPoint, buffer, startTimestamp, 0, rtpEvent.PayloadTypeID, audioTrack.Ssrc, audioTrack.GetNextSeqNum(), RtcpSession, protectRtpPacket);
                        }
                    }
                    audioTrack.Timestamp += rtpEvent.TotalDuration;
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
                rtpEventInProgress = false;
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
            var dtmfEvent = new RTPEvent(key, false, RTPEvent.DEFAULT_VOLUME, RTPSession.DTMF_EVENT_DURATION, RTPSession.DTMF_EVENT_PAYLOAD_ID);
            return SendDtmfEvent(dtmfEvent, ct);
        }

        /// <summary>
        /// Indicates whether this session is using audio.
        /// </summary>
        public bool HasAudio
        {
            get
            {
                return LocalTrack != null && LocalTrack.StreamStatus != MediaStreamStatusEnum.Inactive
                  && RemoteTrack != null && RemoteTrack.StreamStatus != MediaStreamStatusEnum.Inactive;
            }
        }
    }

    public class VideoStream: MediaStream
    {
        private static ILogger logger = Log.Logger;

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

        public RtpVideoFramer RtpVideoFramer;

        /// <summary>
        /// Indicates whether this session is using video.
        /// </summary>
        public bool HasVideo
        {
            get
            {
                // TODO - CI - need to use dictionnary
                return LocalTrack != null && LocalTrack.StreamStatus != MediaStreamStatusEnum.Inactive
                  && RemoteTrack != null && RemoteTrack.StreamStatus != MediaStreamStatusEnum.Inactive;
            }
        }

        public void CheckVideoFormatsNegotiation()
        {
            if (LocalTrack != null && LocalTrack.Capabilities?.Count() > 0)
            {
                OnVideoFormatsNegotiated?.Invoke(
                            LocalTrack.Capabilities
                            .Select(x => x.ToVideoFormat()).ToList());
            }
        }

        public void ProcessVideoRtpFrame(IPEndPoint endpoint, RTPPacket packet, SDPAudioVideoMediaFormat format)
        {
            if (OnVideoFrameReceived == null)
            {
                return;
            }

            if (RtpVideoFramer != null)
            {
                var frame = RtpVideoFramer.GotRtpPacket(packet);
                if (frame != null)
                {
                    OnVideoFrameReceived?.Invoke(endpoint, packet.Header.Timestamp, frame, format.ToVideoFormat());
                }
            }
            else
            {
                if (format.ToVideoFormat().Codec == VideoCodecsEnum.VP8 ||
                    format.ToVideoFormat().Codec == VideoCodecsEnum.H264)
                {
                    logger.LogDebug($"Video depacketisation codec set to {format.ToVideoFormat().Codec} for SSRC {packet.Header.SyncSource}.");

                    RtpVideoFramer = new RtpVideoFramer(format.ToVideoFormat().Codec);

                    var frame = RtpVideoFramer.GotRtpPacket(packet);
                    if (frame != null)
                    {
                        OnVideoFrameReceived?.Invoke(endpoint, packet.Header.Timestamp, frame, format.ToVideoFormat());
                    }
                }
                else
                {
                    logger.LogWarning($"Video depacketisation logic for codec {format.Name()} has not been implemented, PR's welcome!");
                }
            }
        }

        public VideoStream(RtpSessionConfig config) : base(config)
        {
            MediaType = SDPMediaTypesEnum.video;
        }
    }


    public class MediaStream
    {
        private static ILogger logger = Log.Logger;

        private uint m_lastRtpTimestamp;

        private RtpSessionConfig RtpSessionConfig;
        protected Boolean IsSecure;
        protected Boolean UseSdpCryptoNegotiation;

        private SecureContext SecureContext;
        private RTPReorderBuffer RTPReorderBuffer = null;

    #region EVENTS

        /// <summary>
        /// Fires when the connection for a media type is classified as timed out due to not
        /// receiving any RTP or RTCP packets within the given period.
        /// </summary>
        public event Action<uint, SDPMediaTypesEnum> OnTimeout;

        /// <summary>
        /// Gets fired when an RTP packet is received from a remote party.
        /// Parameters are:
        ///  - Remote endpoint packet was received from,
        ///  - The media type the packet contains, will be audio or video,
        ///  - The full RTP packet.
        /// </summary>
        public event Action<IPEndPoint, RTPPacket> OnRtpPacketReceived;  // TODO - CI - 

        /// <summary>
        /// Gets fired when an RTP event is detected on the remote call party's RTP stream.
        /// </summary>
        public event Action<IPEndPoint, RTPEvent, RTPHeader> OnRtpEvent;  // TODO - CI - 

        /// <summary>
        /// Gets fired when an RTCP report is received. This event is for diagnostics only.
        /// </summary>
        public event Action<IPEndPoint, RTCPCompoundPacket> OnReceiveReport;  // TODO - CI - 

        /// <summary>
        /// Gets fired when an RTCP report is sent. This event is for diagnostics only.
        /// </summary>
        public event Action<RTCPCompoundPacket> OnSendReport;  // TODO - CI - 


        public event Action<int, IPEndPoint, byte[]> OnRTPDataReceived;
        public event Action<int, IPEndPoint, byte[]> OnRTPControlDataReceived;
        /// <summary>
        /// Event handler for the RTP channel closure.
        /// </summary>
        public event Action<string> OnRTPChannelClosed;

    #endregion EVENTS

    #region PROPERTIES

        protected RTPChannel rtpChannel = null;

        /// <summary>
        /// Indicates whether the session has been closed. Once a session is closed it cannot
        /// be restarted.
        /// </summary>
        public bool IsClosed { get; set; } = false;

        /// <summary>
        /// In order to detect RTP events from the remote party this property needs to 
        /// be set to the payload ID they are using.
        /// </summary>
        public int RemoteRtpEventPayloadID { get; set; } = RTPSession.DEFAULT_DTMF_EVENT_PAYLOAD_ID;

        /// <summary>
        /// To type of this media
        /// </summary>
        public SDPMediaTypesEnum MediaType { get; set; }

        /// <summary>
        /// The local track. Will be null if we are not sending this media.
        /// </summary>
        public MediaStreamTrack LocalTrack { get; set; }

        /// <summary>
        /// The remote video track. Will be null if the remote party is not sending this media
        /// </summary>
        public MediaStreamTrack RemoteTrack { get; set; }

        /// <summary>
        /// The reporting session for this media stream.
        /// </summary>
        public RTCPSession RtcpSession { get; set; }

        /// <summary>
        /// The remote RTP end point this stream is sending media to.
        /// </summary>
        public IPEndPoint DestinationEndPoint { get; set; }

        /// <summary>
        /// The remote RTP control end point this stream is sending to RTCP reports for the media stream to.
        /// </summary>
        public IPEndPoint ControlDestinationEndPoint { get; set; }

    #endregion PROPERTIES


    #region REORDER BUFFER

        public void AddBuffer(TimeSpan dropPacketTimeout)
        {
            RTPReorderBuffer = new RTPReorderBuffer(dropPacketTimeout);
        }

        public void RemoveBuffer(TimeSpan dropPacketTimeout)
        {
            RTPReorderBuffer = null;
        }

        public Boolean UseBuffer()
        {
            return RTPReorderBuffer != null;
        }

        public RTPReorderBuffer GetBuffer()
        {
            return RTPReorderBuffer;
        }

    #endregion REORDER BUFFER


    #region SECURITY CONTEXT

        public void SetSecurityContext(
            ProtectRtpPacket protectRtp,
            ProtectRtpPacket unprotectRtp,
            ProtectRtpPacket protectRtcp,
            ProtectRtpPacket unprotectRtcp)
        {
            if (SecureContext != null)
            {
                logger.LogTrace($"Tried adding new SecureContext for media type {MediaType}, but one already existed");
            }

            SecureContext = new SecureContext(protectRtp, unprotectRtp, protectRtcp, unprotectRtcp);
        }

        public SecureContext GetSecurityContext()
        {
            return SecureContext;
        }

        public Boolean IsSecurityContextReady()
        {
            return (SecureContext != null);
        }

        private (bool, byte[]) UnprotectBuffer(byte[] buffer)
        {
            var secureContext = GetSecurityContext();
            if (secureContext != null)
            {
                int res = secureContext.UnprotectRtpPacket(buffer, buffer.Length, out int outBufLen);

                if (res == 0)
                {
                    return (true, buffer.Take(outBufLen).ToArray());
                }
                else
                {
                    logger.LogWarning($"SRTP unprotect failed for {MediaType}, result {res}.");
                }
            }
            return (false, buffer);
        }

        public bool EnsureBufferUnprotected(byte[] buf, RTPHeader header, out RTPPacket packet)
        {
            if (IsSecure || UseSdpCryptoNegotiation)
            {
                var (succeeded, newBuffer) = UnprotectBuffer(buf);
                if (!succeeded)
                {
                    packet = null;
                    return false;
                }
                packet = new RTPPacket(newBuffer);
            }
            else
            {
                packet = new RTPPacket(buf);
            }
            packet.Header.ReceivedTime = header.ReceivedTime;
            return true;
        }

    #endregion SECURITY CONTEXT


    #region RTP CHANNEL

        public void AddRtpChannel(RTPChannel rtpChannel)
        {
            this.rtpChannel = rtpChannel;
        }

        public Boolean HasRtpChannel()
        {
            return rtpChannel != null;
        }

        public RTPChannel GetRTPChannel()
        {
            return rtpChannel;
        }

    #endregion RTP CHANNEL


    #region SEND PACKET

        protected void SendRtpPacket(RTPChannel rtpChannel, IPEndPoint dstRtpSocket, byte[] data, uint timestamp, int markerBit, int payloadType, uint ssrc, ushort seqNum, RTCPSession rtcpSession, ProtectRtpPacket protectRtpPacket)
        {
            if ((IsSecure || UseSdpCryptoNegotiation) && protectRtpPacket == null)
            {
                logger.LogWarning("SendRtpPacket cannot be called on a secure session before calling SetSecurityContext.");
            }
            else
            {
                int srtpProtectionLength = (protectRtpPacket != null) ? RTPSession.SRTP_MAX_PREFIX_LENGTH : 0;

                RTPPacket rtpPacket = new RTPPacket(data.Length + srtpProtectionLength);
                rtpPacket.Header.SyncSource = ssrc;
                rtpPacket.Header.SequenceNumber = seqNum;
                rtpPacket.Header.Timestamp = timestamp;
                rtpPacket.Header.MarkerBit = markerBit;
                rtpPacket.Header.PayloadType = payloadType;

                Buffer.BlockCopy(data, 0, rtpPacket.Payload, 0, data.Length);

                var rtpBuffer = rtpPacket.GetBytes();

                if (protectRtpPacket == null)
                {
                    rtpChannel.Send(RTPChannelSocketsEnum.RTP, dstRtpSocket, rtpBuffer);
                }
                else
                {
                    int outBufLen = 0;
                    int rtperr = protectRtpPacket(rtpBuffer, rtpBuffer.Length - srtpProtectionLength, out outBufLen);
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

    #endregion SEND PACKET




        public MediaStream(RtpSessionConfig config)
        {
            IsSecure = config.RtpSecureMediaOption == RtpSecureMediaOptionEnum.DtlsSrtp;
            UseSdpCryptoNegotiation = config.RtpSecureMediaOption == RtpSecureMediaOptionEnum.SdpCryptoNegotiation;
            RtpSessionConfig = config;
        }

        /// <summary>
        /// Creates a new RTCP session for a media track belonging to this RTP session.
        /// </summary>
        /// <param name="mediaType">The media type to create the RTP session for. Must be
        /// audio or video.</param>
        /// <returns>A new RTCPSession object. The RTCPSession must have its Start method called
        /// in order to commence sending RTCP reports.</returns>
        public Boolean CreateRtcpSession()
        {
            if (RtcpSession == null)
            {
                RtcpSession = new RTCPSession(MediaType, 0);
                RtcpSession.OnTimeout += (ssrc, mt) => OnTimeout?.Invoke(ssrc, mt);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Sets the remote end points for a media type supported by this RTP session.
        /// </summary>
        /// <param name="mediaType">The media type, must be audio or video, to set the remote end point for.</param>
        /// <param name="rtpEndPoint">The remote end point for RTP packets corresponding to the media type.</param>
        /// <param name="rtcpEndPoint">The remote end point for RTCP packets corresponding to the media type.</param>
        public void SetDestination(IPEndPoint rtpEndPoint, IPEndPoint rtcpEndPoint)
        {
            DestinationEndPoint = rtpEndPoint;
            ControlDestinationEndPoint = rtcpEndPoint;
        }

        /// <summary>
        /// Attempts to get the highest priority sending format for the remote call party.
        /// </summary>
        /// <returns>The first compatible media format found for the specified media type.</returns>
        public SDPAudioVideoMediaFormat GetSendingFormat()
        {
            if (LocalTrack != null || RemoteTrack != null)
            {
                if (LocalTrack == null)
                {
                    return RemoteTrack.Capabilities.First();
                }
                else if (RemoteTrack == null)
                {
                    return LocalTrack.Capabilities.First();
                }

                SDPAudioVideoMediaFormat format;
                if (MediaType == SDPMediaTypesEnum.audio)
                {

                    format = SDPAudioVideoMediaFormat.GetCompatibleFormats(LocalTrack.Capabilities, RemoteTrack.Capabilities)
                        .Where(x => x.ID != RemoteRtpEventPayloadID).FirstOrDefault();
                }
                else
                {
                    format = SDPAudioVideoMediaFormat.GetCompatibleFormats(LocalTrack.Capabilities, RemoteTrack.Capabilities).First();
                }

                if (format.IsEmpty())
                {
                    // It's not expected that this occurs as a compatibility check is done when the remote session description
                    // is set. By this point a compatible codec should be available.
                    throw new ApplicationException($"No compatible sending format could be found for media {MediaType}.");
                }
                else
                {
                    return format;
                }
            }
            else
            {
                throw new ApplicationException($"Cannot get the {MediaType} sending format, missing either local or remote {MediaType} track.");
            }
        }

        public void ProcessHeaderExtensions(RTPHeader header)
        {
            header.GetHeaderExtensions().ToList().ForEach(x =>
            {
                var ntpTimestamp = x.GetNtpTimestamp(RemoteTrack.HeaderExtensions);
                if (ntpTimestamp.HasValue)
                {
                    RemoteTrack.LastAbsoluteCaptureTimestamp = new TimestampPair() { NtpTimestamp = ntpTimestamp.Value, RtpTimestamp = header.Timestamp };
                }
            });
        }

    }
}
