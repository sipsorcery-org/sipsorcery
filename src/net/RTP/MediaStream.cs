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
                        var protectRtpPacket = SecureContext?.ProtectRtpPacket;
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
        public async Task SendDtmfEvent(RTPEvent rtpEvent, CancellationToken cancellationToken, int clockRate = RTPSession.DEFAULT_AUDIO_CLOCK_RATE, int samplePeriod = RTPSession.RTP_EVENT_DEFAULT_SAMPLE_PERIOD_MS)
        {
            if (IsClosed || rtpEventInProgress == true || DestinationEndPoint == null)
            {
                logger.LogWarning("SendDtmfEvent request ignored as an RTP event is already in progress.");
            }

            try
            {
                if (LocalTrack == null)
                {
                    logger.LogWarning("SendDtmfEvent was called on an RTP session without an audio stream.");
                }
                else if (!HasAudio || LocalTrack.StreamStatus == MediaStreamStatusEnum.RecvOnly)
                {
                    return;
                }
                else
                {
                    rtpEventInProgress = true;
                    uint startTimestamp = LocalTrack.Timestamp;

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
                        var protectRtpPacket = SecureContext?.ProtectRtpPacket;
                        SendRtpPacket(rtpChannel, DestinationEndPoint, buffer, startTimestamp, markerBit, rtpEvent.PayloadTypeID, LocalTrack.Ssrc, LocalTrack.GetNextSeqNum(), RtcpSession, protectRtpPacket);
                    }

                    await Task.Delay(samplePeriod, cancellationToken).ConfigureAwait(false);

                    if (!rtpEvent.EndOfEvent)
                    {
                        // Send the progressive event packets 
                        while ((rtpEvent.Duration + rtpTimestampStep) < rtpEvent.TotalDuration && !cancellationToken.IsCancellationRequested)
                        {
                            rtpEvent.Duration += rtpTimestampStep;
                            byte[] buffer = rtpEvent.GetEventPayload();

                            var protectRtpPacket = SecureContext?.ProtectRtpPacket;
                            SendRtpPacket(rtpChannel, DestinationEndPoint, buffer, startTimestamp, 0, rtpEvent.PayloadTypeID, LocalTrack.Ssrc, LocalTrack.GetNextSeqNum(), RtcpSession, protectRtpPacket);

                            await Task.Delay(samplePeriod, cancellationToken).ConfigureAwait(false);
                        }

                        // Send the end of event packets.
                        for (int j = 0; j < RTPEvent.DUPLICATE_COUNT && !cancellationToken.IsCancellationRequested; j++)
                        {
                            rtpEvent.EndOfEvent = true;
                            rtpEvent.Duration = rtpEvent.TotalDuration;
                            byte[] buffer = rtpEvent.GetEventPayload();

                            var protectRtpPacket = SecureContext?.ProtectRtpPacket;
                            SendRtpPacket(rtpChannel, DestinationEndPoint, buffer, startTimestamp, 0, rtpEvent.PayloadTypeID, LocalTrack.Ssrc, LocalTrack.GetNextSeqNum(), RtcpSession, protectRtpPacket);
                        }
                    }
                    LocalTrack.Timestamp += rtpEvent.TotalDuration;
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

        public void OnReceiveRTPPacket(RTPHeader hdr, SDPAudioVideoMediaFormat format, int localPort, IPEndPoint remoteEndPoint, byte[] buffer)
        {
            base.OnReceiveRTPPacket(hdr, format, localPort, remoteEndPoint, buffer, null);
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
            if (IsClosed || DestinationEndPoint == null)
            {
                return;
            }
            try
            {
                var videoTrack = LocalTrack;

                if (videoTrack == null)
                {
                    logger.LogWarning("SendJpegFrame was called on an RTP session without a video stream.");
                }
                else if (!HasVideo || videoTrack.StreamStatus == MediaStreamStatusEnum.RecvOnly)
                {
                    return;
                }
                else
                {
                    for (int index = 0; index * RTPSession.RTP_MAX_PAYLOAD < jpegBytes.Length; index++)
                    {
                        uint offset = Convert.ToUInt32(index * RTPSession.RTP_MAX_PAYLOAD);
                        int payloadLength = ((index + 1) * RTPSession.RTP_MAX_PAYLOAD < jpegBytes.Length) ? RTPSession.RTP_MAX_PAYLOAD : jpegBytes.Length - index * RTPSession.RTP_MAX_PAYLOAD;
                        byte[] jpegHeader = RtpVideoFramer.CreateLowQualityRtpJpegHeader(offset, jpegQuality, jpegWidth, jpegHeight);

                        List<byte> packetPayload = new List<byte>();
                        packetPayload.AddRange(jpegHeader);
                        packetPayload.AddRange(jpegBytes.Skip(index * RTPSession.RTP_MAX_PAYLOAD).Take(payloadLength));

                        int markerBit = ((index + 1) * RTPSession.RTP_MAX_PAYLOAD < jpegBytes.Length) ? 0 : 1;

                        var protectRtpPacket = SecureContext?.ProtectRtpPacket;
                        SendRtpPacket(rtpChannel, DestinationEndPoint, packetPayload.ToArray(), videoTrack.Timestamp, markerBit, payloadTypeID, videoTrack.Ssrc, videoTrack.GetNextSeqNum(), RtcpSession, protectRtpPacket);
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
            var dstEndPoint = DestinationEndPoint;

            if (IsClosed || dstEndPoint == null || accessUnit == null || accessUnit.Length == 0)
            {
                return;
            }

            if (LocalTrack == null)
            {
                logger.LogWarning("SendH264Frame was called on an RTP session without a video stream.");
            }
            else if (!HasVideo || LocalTrack.StreamStatus == MediaStreamStatusEnum.RecvOnly)
            {
                return;
            }
            else
            {
                foreach (var nal in H264Packetiser.ParseNals(accessUnit))
                {
                    SendH264Nal(duration, payloadTypeID, nal.NAL, nal.IsLast, dstEndPoint, LocalTrack);
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

            if (nal.Length <= RTPSession.RTP_MAX_PAYLOAD)
            {
                // Send as Single-Time Aggregation Packet (STAP-A).
                byte[] payload = new byte[nal.Length];
                int markerBit = isLastNal ? 1 : 0;   // There is only ever one packet in a STAP-A.
                Buffer.BlockCopy(nal, 0, payload, 0, nal.Length);

                var protectRtpPacket = SecureContext?.ProtectRtpPacket;
                SendRtpPacket(rtpChannel, dstEndPoint, payload, videoTrack.Timestamp, markerBit, payloadTypeID, videoTrack.Ssrc, videoTrack.GetNextSeqNum(), RtcpSession, protectRtpPacket);
                //logger.LogDebug($"send H264 {videoChannel.RTPLocalEndPoint}->{dstEndPoint} timestamp {videoTrack.Timestamp}, payload length {payload.Length}, seqnum {videoTrack.SeqNum}, marker {markerBit}.");
                //logger.LogDebug($"send H264 {videoChannel.RTPLocalEndPoint}->{dstEndPoint} timestamp {videoTrack.Timestamp}, STAP-A {h264RtpHdr.HexStr()}, payload length {payload.Length}, seqnum {videoTrack.SeqNum}, marker {markerBit}.");
            }
            else
            {
                nal = nal.Skip(1).ToArray();

                // Send as Fragmentation Unit A (FU-A):
                for (int index = 0; index * RTPSession.RTP_MAX_PAYLOAD < nal.Length; index++)
                {
                    int offset = index * RTPSession.RTP_MAX_PAYLOAD;
                    int payloadLength = ((index + 1) * RTPSession.RTP_MAX_PAYLOAD < nal.Length) ? RTPSession.RTP_MAX_PAYLOAD : nal.Length - index * RTPSession.RTP_MAX_PAYLOAD;

                    bool isFirstPacket = index == 0;
                    bool isFinalPacket = (index + 1) * RTPSession.RTP_MAX_PAYLOAD >= nal.Length;
                    int markerBit = (isLastNal && isFinalPacket) ? 1 : 0;

                    byte[] h264RtpHdr = H264Packetiser.GetH264RtpHeader(nal0, isFirstPacket, isFinalPacket);

                    byte[] payload = new byte[payloadLength + h264RtpHdr.Length];
                    Buffer.BlockCopy(h264RtpHdr, 0, payload, 0, h264RtpHdr.Length);
                    Buffer.BlockCopy(nal, offset, payload, h264RtpHdr.Length, payloadLength);


                    var protectRtpPacket = SecureContext?.ProtectRtpPacket;
                    SendRtpPacket(rtpChannel, dstEndPoint, payload, videoTrack.Timestamp, markerBit, payloadTypeID, videoTrack.Ssrc, videoTrack.GetNextSeqNum(), RtcpSession, protectRtpPacket);
                    //logger.LogDebug($"send H264 {videoChannel.RTPLocalEndPoint}->{dstEndPoint} timestamp {videoTrack.Timestamp}, FU-A {h264RtpHdr.HexStr()}, payload length {payloadLength}, seqnum {videoTrack.SeqNum}, marker {markerBit}.");
                }
            }

            if (isLastNal)
            {
                videoTrack.Timestamp += duration;
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
            if (IsClosed || DestinationEndPoint == null)
            {
                return;
            }

            try
            {
                if (LocalTrack == null)
                {
                    logger.LogWarning("SendVp8Frame was called on an RTP session without a video stream.");
                }
                else if (!HasVideo || LocalTrack.StreamStatus == MediaStreamStatusEnum.RecvOnly)
                {
                    return;
                }
                else
                {
                    for (int index = 0; index * RTPSession.RTP_MAX_PAYLOAD < buffer.Length; index++)
                    {
                        int offset = index * RTPSession.RTP_MAX_PAYLOAD;
                        int payloadLength = (offset + RTPSession.RTP_MAX_PAYLOAD < buffer.Length) ? RTPSession.RTP_MAX_PAYLOAD : buffer.Length - offset;

                        byte[] vp8HeaderBytes = (index == 0) ? new byte[] { 0x10 } : new byte[] { 0x00 };
                        byte[] payload = new byte[payloadLength + vp8HeaderBytes.Length];
                        Buffer.BlockCopy(vp8HeaderBytes, 0, payload, 0, vp8HeaderBytes.Length);
                        Buffer.BlockCopy(buffer, offset, payload, vp8HeaderBytes.Length, payloadLength);

                        int markerBit = ((offset + payloadLength) >= buffer.Length) ? 1 : 0; // Set marker bit for the last packet in the frame.

                        var protectRtpPacket = SecureContext?.ProtectRtpPacket;
                        SendRtpPacket(rtpChannel, DestinationEndPoint, payload, LocalTrack.Timestamp, markerBit, payloadTypeID, LocalTrack.Ssrc, LocalTrack.GetNextSeqNum(), RtcpSession, protectRtpPacket);
                        //logger.LogDebug($"send VP8 {videoChannel.RTPLocalEndPoint}->{dstEndPoint} timestamp {videoTrack.Timestamp}, sample length {buffer.Length}.");
                    }

                    LocalTrack.Timestamp += duration;
                }
            }
            catch (SocketException sockExcp)
            {
                logger.LogError("SocketException SendVp8Frame. " + sockExcp.Message);
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
            if (DestinationEndPoint != null || (DestinationEndPoint != null) &&
                ((!IsSecure && !UseSdpCryptoNegotiation) || IsSecurityContextReady()))
            {
                var videoSendingFormat = GetSendingFormat();

                switch (videoSendingFormat.Name())
                {
                    case "VP8":
                        int vp8PayloadID = Convert.ToInt32(LocalTrack.Capabilities.First(x => x.Name() == "VP8").ID);
                        SendVp8Frame(durationRtpUnits, vp8PayloadID, sample);
                        break;
                    case "H264":
                        int h264PayloadID = Convert.ToInt32(LocalTrack.Capabilities.First(x => x.Name() == "H264").ID);
                        SendH264Frame(durationRtpUnits, h264PayloadID, sample);
                        break;
                    default:
                        throw new ApplicationException($"Unsupported video format selected {videoSendingFormat.Name()}.");
                }
            }
        }

        public void OnReceiveRTPPacket(RTPHeader hdr, SDPAudioVideoMediaFormat format, int localPort, IPEndPoint remoteEndPoint, byte[] buffer)
        {
            base.OnReceiveRTPPacket(hdr, format, localPort, remoteEndPoint, buffer, this);
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

        protected SecureContext SecureContext;
        protected SrtpHandler SrtpHandler;

        private RTPReorderBuffer RTPReorderBuffer = null;

        public Boolean AcceptRtpFromAny { get; set; } = false;

    #region EVENTS

        /// <summary>
        /// Fires when the connection for a media type is classified as timed out due to not
        /// receiving any RTP or RTCP packets within the given period.
        /// </summary>
        public event Action<SDPMediaTypesEnum> OnTimeout;

        /// <summary>
        /// Gets fired when an RTCP report is sent. This event is for diagnostics only.
        /// </summary>
        public event Action<SDPMediaTypesEnum, RTCPCompoundPacket> OnSendReport;


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
        /// Gets fired when an RTCP report is received. This event is for diagnostics only.
        /// </summary>
        public event Action<IPEndPoint, SDPMediaTypesEnum, RTCPCompoundPacket> OnReceiveReport;

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

        public void SetSecurityContext( ProtectRtpPacket protectRtp, ProtectRtpPacket unprotectRtp, ProtectRtpPacket protectRtcp, ProtectRtpPacket unprotectRtcp)
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
            if (SecureContext != null)
            {
                int res = SecureContext.UnprotectRtpPacket(buffer, buffer.Length, out int outBufLen);

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

        public SrtpHandler GetOrCreateSrtpHandler()
        {
            if(SrtpHandler == null)
            {
                SrtpHandler = new SrtpHandler();
            }
            return SrtpHandler;
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

        /// <summary>
        /// Allows additional control for sending raw RTP payloads. No framing or other processing is carried out.
        /// </summary>
        /// <param name="mediaType">The media type of the RTP packet being sent. Must be audio or video.</param>
        /// <param name="payload">The RTP packet payload.</param>
        /// <param name="timestamp">The timestamp to set on the RTP header.</param>
        /// <param name="markerBit">The value to set on the RTP header marker bit, should be 0 or 1.</param>
        /// <param name="payloadTypeID">The payload ID to set in the RTP header.</param>
        public void SendRtpRaw(byte[] payload, uint timestamp, int markerBit, int payloadTypeID)
        {
            if (LocalTrack == null)
            {
                logger.LogWarning($"SendRtpRaw was called for an {MediaType} packet on an RTP session without a local audio stream.");
            }
            else
            {
                RTCPSession rtcpSession = RtcpSession;
                IPEndPoint dstEndPoint = DestinationEndPoint;
                MediaStreamTrack track = LocalTrack;

                if (dstEndPoint != null)
                {
                    var protectRtpPacket = SecureContext?.ProtectRtpPacket;
                    SendRtpPacket(rtpChannel, dstEndPoint, payload, timestamp, markerBit, payloadTypeID, track.Ssrc, track.GetNextSeqNum(), rtcpSession, protectRtpPacket);
                }
            }
        }

        /// <summary>
        /// Sends the RTCP report to the remote call party.
        /// </summary>
        /// <param name="report">The serialised RTCP report to send.</param>
        private void SendRtcpReport(byte[] reportBuffer)
        {
            if ((IsSecure || UseSdpCryptoNegotiation) && !IsSecurityContextReady())
            {
                logger.LogWarning("SendRtcpReport cannot be called on a secure session before calling SetSecurityContext.");
            }
            else if (ControlDestinationEndPoint != null)
            {
                //logger.LogDebug($"SendRtcpReport: {reportBytes.HexStr()}");

                var sendOnSocket = RtpSessionConfig.IsRtcpMultiplexed ? RTPChannelSocketsEnum.RTP : RTPChannelSocketsEnum.Control;

                var protectRtcpPacket = SecureContext?.ProtectRtcpPacket;

                if (protectRtcpPacket == null)
                {
                    rtpChannel.Send(sendOnSocket, ControlDestinationEndPoint, reportBuffer);
                }
                else
                {
                    byte[] sendBuffer = new byte[reportBuffer.Length + RTPSession.SRTP_MAX_PREFIX_LENGTH];
                    Buffer.BlockCopy(reportBuffer, 0, sendBuffer, 0, reportBuffer.Length);

                    int outBufLen = 0;
                    int rtperr = protectRtcpPacket(sendBuffer, sendBuffer.Length - RTPSession.SRTP_MAX_PREFIX_LENGTH, out outBufLen);
                    if (rtperr != 0)
                    {
                        logger.LogWarning("SRTP RTCP packet protection failed, result " + rtperr + ".");
                    }
                    else
                    {
                        rtpChannel.Send(sendOnSocket, ControlDestinationEndPoint, sendBuffer.Take(outBufLen).ToArray());
                    }
                }
            }
        }

        /// <summary>
        /// Sends the RTCP report to the remote call party.
        /// </summary>
        /// <param name="report">RTCP report to send.</param>
        public void SendRtcpReport(RTCPCompoundPacket report)
        {
            if ((IsSecure || UseSdpCryptoNegotiation) && !IsSecurityContextReady() && report.Bye != null)
            {
                // Do nothing. The RTCP BYE gets generated when an RTP session is closed.
                // If that occurs before the connection was able to set up the secure context
                // there's no point trying to send it.
            }
            else
            {
                var reportBytes = report.GetBytes();
                SendRtcpReport(reportBytes);
                OnSendReport?.Invoke(MediaType, report);
            }
        }

        /// <summary>
        /// Allows sending of RTCP feedback reports.
        /// </summary>
        /// <param name="mediaType">The media type of the RTCP report  being sent. Must be audio or video.</param>
        /// <param name="feedback">The feedback report to send.</param>
        public void SendRtcpFeedback(RTCPFeedback feedback)
        {
            var reportBytes = feedback.GetBytes();
            SendRtcpReport(reportBytes);
        }

    #endregion SEND PACKET

        protected void OnReceiveRTPPacket(RTPHeader hdr, SDPAudioVideoMediaFormat format, int localPort, IPEndPoint remoteEndPoint, byte[] buffer, VideoStream videoStream = null)
        {
            RTPPacket rtpPacket = null;
            if (RemoteRtpEventPayloadID != 0 && hdr.PayloadType == RemoteRtpEventPayloadID)
            {
                if (!EnsureBufferUnprotected(buffer, hdr, out rtpPacket))
                {
                    return;
                }

                RtpEvent(remoteEndPoint, new RTPEvent(rtpPacket.Payload), rtpPacket.Header);
                return;
            }

            // Set the remote track SSRC so that RTCP reports can match the media type.
            if (RemoteTrack != null && RemoteTrack.Ssrc == 0 && DestinationEndPoint != null)
            {
                bool isValidSource = AdjustRemoteEndPoint(hdr.SyncSource, remoteEndPoint);

                if (isValidSource)
                {
                    logger.LogDebug($"Set remote {MediaType} track SSRC to {hdr.SyncSource}.");
                    RemoteTrack.Ssrc = hdr.SyncSource;
                }
            }


            // Note AC 24 Dec 2020: The problem with waiting until the remote description is set is that the remote peer often starts sending
            // RTP packets at the same time it signals its SDP offer or answer. Generally this is not a problem for audio but for video streams
            // the first RTP packet(s) are the key frame and if they are ignored the video stream will take additional time or manual 
            // intervention to synchronise.
            //if (RemoteDescription != null)
            //{

            // Don't hand RTP packets to the application until the remote description has been set. Without it
            // things like the common codec, DTMF support etc. are not known.

            //SDPMediaTypesEnum mediaType = (rtpMediaType.HasValue) ? rtpMediaType.Value : DEFAULT_MEDIA_TYPE;

            // For video RTP packets an attempt will be made to collate into frames. It's up to the application
            // whether it wants to subscribe to frames of RTP packets.

            rtpPacket = null;
            if (RemoteTrack != null)
            {
                LogIfWrongSeqNumber($"{MediaType}", hdr, RemoteTrack);
                ProcessHeaderExtensions(hdr);
            }
            if (!EnsureBufferUnprotected(buffer, hdr, out rtpPacket))
            {
                return;
            }

            if (rtpPacket != null)
            {
                if (UseBuffer())
                {
                    var reorderBuffer = GetBuffer();
                    reorderBuffer.Add(rtpPacket);
                    while (reorderBuffer.Get(out var bufferedPacket))
                    {
                        if (RemoteTrack != null)
                        {
                            LogIfWrongSeqNumber($"{MediaType}", bufferedPacket.Header, RemoteTrack);
                            RemoteTrack.LastRemoteSeqNum = bufferedPacket.Header.SequenceNumber;
                        }
                        videoStream?.ProcessVideoRtpFrame(remoteEndPoint, bufferedPacket, format);
                        RtpPacketReceived(remoteEndPoint, bufferedPacket);
                    }
                }
                else
                {
                    videoStream?.ProcessVideoRtpFrame(remoteEndPoint, rtpPacket, format);
                    RtpPacketReceived(remoteEndPoint, rtpPacket);
                }

                RtcpSession?.RecordRtpPacketReceived(rtpPacket);
            }
        }

        public void ReceiveReport(IPEndPoint ipEndPoint, RTCPCompoundPacket rtcpPCompoundPacket)
        {
            OnReceiveReport?.Invoke(ipEndPoint, MediaType, rtcpPCompoundPacket);
        }

        protected void RtpEvent(IPEndPoint ipEndPoint, RTPEvent rtpEvent, RTPHeader rtpHeader)
        {
            OnRtpEvent?.Invoke(ipEndPoint, rtpEvent, rtpHeader);
        }

        protected void RtpPacketReceived(IPEndPoint ipEndPoint, RTPPacket rtpPacket)
        {
            OnRtpPacketReceived?.Invoke(ipEndPoint, MediaType, rtpPacket);
        }

        protected void LogIfWrongSeqNumber(string trackType, RTPHeader header, MediaStreamTrack track)
        {
            if (track.LastRemoteSeqNum != 0 &&
                header.SequenceNumber != (track.LastRemoteSeqNum + 1) &&
                !(header.SequenceNumber == 0 && track.LastRemoteSeqNum == ushort.MaxValue))
            {
                logger.LogWarning($"{trackType} stream sequence number jumped from {track.LastRemoteSeqNum} to {header.SequenceNumber}.");
            }
        }

        /// <summary>
        /// Adjusts the expected remote end point for a particular media type.
        /// </summary>
        /// <param name="mediaType">The media type of the RTP packet received.</param>
        /// <param name="ssrc">The SSRC from the RTP packet header.</param>
        /// <param name="receivedOnEndPoint">The actual remote end point that the RTP packet came from.</param>
        /// <returns>True if remote end point for this media type was the expected one or it was adjusted. False if
        /// the remote end point was deemed to be invalid for this media type.</returns>
        protected bool AdjustRemoteEndPoint(uint ssrc, IPEndPoint receivedOnEndPoint)
        {
            bool isValidSource = false;
            IPEndPoint expectedEndPoint = DestinationEndPoint;

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
                logger.LogDebug($"{MediaType} end point switched for RTP ssrc {ssrc} from {expectedEndPoint} to {receivedOnEndPoint}.");

                DestinationEndPoint = receivedOnEndPoint;
                if (RtpSessionConfig.IsRtcpMultiplexed)
                {
                    ControlDestinationEndPoint = DestinationEndPoint;
                }
                else
                {
                    ControlDestinationEndPoint = new IPEndPoint(DestinationEndPoint.Address, DestinationEndPoint.Port + 1);
                }

                isValidSource = true;
            }
            else
            {
                logger.LogWarning($"RTP packet with SSRC {ssrc} received from unrecognised end point {receivedOnEndPoint}.");
            }

            return isValidSource;
        }

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
                RtcpSession.OnTimeout += OnTimeout;
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
