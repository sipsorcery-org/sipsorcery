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
    public delegate int ProtectRtpPacket(byte[] payload, int length);

    public class RTPSessionStream
    {
        public int ID { get; private set; }
        public uint Ssrc { get; internal set; }
        public ushort SeqNum { get; internal set; }

        /// <summary>
        /// The selected format (codec) ID from the type available in the media announcement.
        /// </summary>
        public int PayloadTypeID { get; private set; }

        public RTPSessionStream(int id, int payloadTypeID)
        {
            ID = id;
            PayloadTypeID = payloadTypeID;
            Ssrc = Convert.ToUInt32(Crypto.GetRandomInt(0, Int32.MaxValue));
            SeqNum = Convert.ToUInt16(Crypto.GetRandomInt(0, UInt16.MaxValue));
        }
    }

    public class RTPSession : IDisposable
    {
        private const int RTP_MAX_PAYLOAD = 1400;
        public const int SRTP_AUTH_KEY_LENGTH = 10;
        private const int DEFAULT_AUDIO_CLOCK_RATE = 8000;
        public const int H264_RTP_HEADER_LENGTH = 2;
        public const int RTP_EVENT_DEFAULT_SAMPLE_PERIOD_MS = 50; // Default sample period for an RTP event as specified by RFC2833.

        private static ILogger logger = Log.Logger;

        private IPEndPoint m_lastReceiveFromEndPoint;
        private bool m_rtpEventInProgress;               // Gets set to true when an RTP event is being sent and the normal stream is interrupted.
        private uint m_lastRtpTimestamp;                 // The last timestamp used in an RTP packet.    
        private bool m_isClosed;
        private List<RTPSessionStream> m_sessionStreams = new List<RTPSessionStream>();

        /// <summary>
        /// The RTP communications channel this session is sending and receiving on.
        /// </summary>
        public RTPChannel RtpChannel { get; private set; }

        /// <summary>
        /// The RTCP session to manage RTCP reporting requirements for this session.
        /// </summary>
        public RTCPSession RtcpSession { get; private set; }

        /// <summary>
        /// The remote RTP end point this session is sending to.
        /// </summary>
        public IPEndPoint DestinationEndPoint;

        /// <summary>
        /// In order to detect RTP events from the remote party this property needs to 
        /// be set to tge payload ID they are using.
        /// </summary>
        public int RemoteRtpEventPayloadID { get; set; }

        /// <summary>
        /// Function pointer to an SRTP context that encrypts an RTP packet.
        /// </summary>
        internal ProtectRtpPacket SrtpProtect;

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
        public event Action<IPEndPoint, IPEndPoint> OnReceiveFromEndPointChanged;

        /// <summary>
        /// Gets fired when an RTP packet is received, has been identified and is ready for processing.
        /// </summary>
        public event Action<byte[]> OnReceivedSampleReady;

        /// <summary>
        /// Gets fired when an RTP event is detected on the remote call party's RTP stream.
        /// </summary>
        public event Action<RTPEvent> OnRtpEvent;

        /// <summary>
        /// Gets fired when the RTP session and underlying channel are closed.
        /// </summary>
        public event Action<string> OnRtpClosed;

        /// <summary>
        /// Used to notify the RTCP session of an RTP packet receive.
        /// </summary>
        internal event Action<RTPPacket> OnRtpPacketReceived;

        /// <summary>
        /// Used to notify the RTCP session of an RTP packet send.
        /// </summary
        internal event Action<RTPPacket> OnRtpPacketSent;

        /// <summary>
        /// Creates a new RTP session. The synchronisation source and sequence number are initialised to
        /// pseudo random values.
        /// </summary>
        /// <param name="payloadTypeID">The payload type ID for this RTP stream. It's what gets set in the payload 
        /// type ID field in the RTP header.</param>
        /// <param name="addrFamily">Determines whether the RTP channel will use an IPv4 or IPv6 socket.</param>
        /// <param name="isRtcpMultiplexed">If true RTCP reports will be multiplexed with RTP on a single channel.
        /// If false (standard mode) then a separate socket is used to send and receive RTCP reports.</param>
        public RTPSession(
            int payloadTypeID,
            AddressFamily addrFamily,
            bool isRtcpMultiplexed)
        {
            var sessionStream = new RTPSessionStream(0, payloadTypeID);
            m_sessionStreams.Add(sessionStream);
            InitialiseRtpChannel(addrFamily, isRtcpMultiplexed);
        }

        /// <summary>
        /// Initialises the RTP session state and starts the RTP channel UDP sockets.
        /// </summary>
        /// <param name="addrFamily">Whether the RTP channel should use IPv4 or IPv6.</param>
        /// <param name="isRtcpMultiplexed">If true RTCP reports will be multiplexed with RTP on a single channel.
        /// If false (standard mode) then a separate socket is used to send and receive RTCP reports.</param>
        private void InitialiseRtpChannel(AddressFamily addrFamily, bool isRtcpMultiplexed)
        {
            var channelAddress = (addrFamily == AddressFamily.InterNetworkV6) ? IPAddress.IPv6Any : IPAddress.Any;
            RtpChannel = new RTPChannel(channelAddress, !isRtcpMultiplexed);

            RtpChannel.OnRTPDataReceived += RtpPacketReceived;
            RtpChannel.OnClosed += OnRTPChannelClosed;

            RtcpSession = new RTCPSession(m_sessionStreams.First().Ssrc, RtpChannel, isRtcpMultiplexed);
            OnRtpPacketReceived += RtcpSession.RtpPacketReceived;
            RtpChannel.OnControlDataReceived += RtcpSession.ControlDataReceived;
            OnRtpPacketSent += RtcpSession.RtpPacketSent;

            // Start the RTP and Control socket receivers and the RTCP session.
            RtpChannel.Start();
            RtcpSession.Start();
        }

        /// <summary>
        /// Adds an additional RTP stream to this session. The effect of this is to multiplex
        /// two or more RTP sessions on a single socket. Multiplexing is used by WebRTC.
        /// </summary>
        /// <param name="payloadTypeID">The payload type ID for this RTP stream. It's what gets set in the payload 
        /// type ID field in the RTP header.</param>
        /// <returns>The ID of the stream of this media type. It must be supplied when
        /// doing a send for this stream.</returns>
        public int AddStream(int payloadTypeID, SDPMediaAnnouncement mediaAnnouncement)
        {
            int nextID = m_sessionStreams.OrderByDescending(x => x.ID).First().ID + 1;
            var sessionStream = new RTPSessionStream(nextID, payloadTypeID);
            m_sessionStreams.Add(sessionStream);
            return sessionStream.ID;
        }

        public void SendAudioFrame(uint timestamp, byte[] buffer, int streamID = 0)
        {
            if (m_isClosed || m_rtpEventInProgress || DestinationEndPoint == null)
            {
                return;
            }

            try
            {
                RTPSessionStream sessionStream = m_sessionStreams.Where(x => x.ID == streamID).Single();

                for (int index = 0; index * RTP_MAX_PAYLOAD < buffer.Length; index++)
                {
                    sessionStream.SeqNum = (ushort)(sessionStream.SeqNum % UInt16.MaxValue);

                    int offset = (index == 0) ? 0 : (index * RTP_MAX_PAYLOAD);
                    int payloadLength = (offset + RTP_MAX_PAYLOAD < buffer.Length) ? RTP_MAX_PAYLOAD : buffer.Length - offset;
                    int srtpProtectionLength = (SrtpProtect != null) ? SRTP_AUTH_KEY_LENGTH : 0;

                    RTPPacket rtpPacket = new RTPPacket(payloadLength + srtpProtectionLength);
                    rtpPacket.Header.SyncSource = sessionStream.Ssrc;
                    rtpPacket.Header.SequenceNumber = sessionStream.SeqNum++;
                    rtpPacket.Header.Timestamp = timestamp;
                    // RFC3551 specifies that for audio the marker bit should always be 0 except for when returning
                    // from silence suppression. For video the marker bit DOES get set to 1 for the last packet
                    // in a frame.
                    rtpPacket.Header.MarkerBit = 0;
                    rtpPacket.Header.PayloadType = sessionStream.PayloadTypeID;

                    Buffer.BlockCopy(buffer, offset, rtpPacket.Payload, 0, payloadLength);

                    OnRtpPacketSent?.Invoke(rtpPacket);

                    var rtpBuffer = rtpPacket.GetBytes();

                    int rtperr = SrtpProtect == null ? 0 : SrtpProtect(rtpBuffer, rtpBuffer.Length - srtpProtectionLength);
                    if (rtperr != 0)
                    {
                        logger.LogError("SendAudioFrame SRTP packet protection failed, result " + rtperr + ".");
                    }
                    else
                    {
                        RtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, DestinationEndPoint, rtpBuffer);
                    }

                    m_lastRtpTimestamp = timestamp;
                }
            }
            catch (SocketException sockExcp)
            {
                logger.LogError("SocketException SendAudioFrame. " + sockExcp.Message);
            }
        }

        public void SendVp8Frame(uint timestamp, byte[] buffer, int streamID = 0)
        {
            if (m_isClosed || m_rtpEventInProgress || DestinationEndPoint == null)
            {
                return;
            }

            try
            {
                RTPSessionStream sessionStream = m_sessionStreams.Where(x => x.ID == streamID).Single();

                for (int index = 0; index * RTP_MAX_PAYLOAD < buffer.Length; index++)
                {
                    sessionStream.SeqNum = (ushort)(sessionStream.SeqNum % UInt16.MaxValue);

                    int offset = (index == 0) ? 0 : (index * RTP_MAX_PAYLOAD);
                    int payloadLength = (offset + RTP_MAX_PAYLOAD < buffer.Length) ? RTP_MAX_PAYLOAD : buffer.Length - offset;
                    int srtpProtectionLength = (SrtpProtect != null) ? SRTP_AUTH_KEY_LENGTH : 0;

                    byte[] vp8HeaderBytes = (index == 0) ? new byte[] { 0x10 } : new byte[] { 0x00 };

                    RTPPacket rtpPacket = new RTPPacket(payloadLength + vp8HeaderBytes.Length + srtpProtectionLength);
                    rtpPacket.Header.SyncSource = sessionStream.Ssrc;
                    rtpPacket.Header.SequenceNumber = sessionStream.SeqNum++;
                    rtpPacket.Header.Timestamp = timestamp;
                    rtpPacket.Header.MarkerBit = ((offset + payloadLength) >= buffer.Length) ? 1 : 0; // Set marker bit for the last packet in the frame.
                    rtpPacket.Header.PayloadType = sessionStream.PayloadTypeID;

                    Buffer.BlockCopy(vp8HeaderBytes, 0, rtpPacket.Payload, 0, vp8HeaderBytes.Length);
                    Buffer.BlockCopy(buffer, offset, rtpPacket.Payload, vp8HeaderBytes.Length, payloadLength);

                    OnRtpPacketSent?.Invoke(rtpPacket);

                    var rtpBuffer = rtpPacket.GetBytes();

                    int rtperr = SrtpProtect == null ? 0 : SrtpProtect(rtpBuffer, rtpBuffer.Length - srtpProtectionLength);
                    if (rtperr != 0)
                    {
                        logger.LogError("SendVp8Frame SRTP packet protection failed, result " + rtperr + ".");
                    }
                    else
                    {
                        RtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, DestinationEndPoint, rtpBuffer);
                    }

                    m_lastRtpTimestamp = timestamp;
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
        public void SendJpegFrame(uint timestamp, byte[] jpegBytes, int jpegQuality, int jpegWidth, int jpegHeight, int streamID = 0)
        {
            if (m_isClosed || m_rtpEventInProgress || DestinationEndPoint == null)
            {
                return;
            }

            try
            {
                //System.Diagnostics.Debug.WriteLine("Sending " + jpegBytes.Length + " encoded bytes to client, timestamp " + _timestamp + ", starting sequence number " + _sequenceNumber + ", image dimensions " + jpegWidth + " x " + jpegHeight + ".");

                RTPSessionStream sessionStream = m_sessionStreams.Where(x => x.ID == streamID).Single();

                for (int index = 0; index * RTP_MAX_PAYLOAD < jpegBytes.Length; index++)
                {
                    uint offset = Convert.ToUInt32(index * RTP_MAX_PAYLOAD);
                    int payloadLength = ((index + 1) * RTP_MAX_PAYLOAD < jpegBytes.Length) ? RTP_MAX_PAYLOAD : jpegBytes.Length - index * RTP_MAX_PAYLOAD;

                    byte[] jpegHeader = CreateLowQualityRtpJpegHeader(offset, jpegQuality, jpegWidth, jpegHeight);

                    List<byte> packetPayload = new List<byte>();
                    packetPayload.AddRange(jpegHeader);
                    packetPayload.AddRange(jpegBytes.Skip(index * RTP_MAX_PAYLOAD).Take(payloadLength));

                    RTPPacket rtpPacket = new RTPPacket(packetPayload.Count);
                    rtpPacket.Header.SyncSource = sessionStream.Ssrc;
                    rtpPacket.Header.SequenceNumber = sessionStream.SeqNum++;
                    rtpPacket.Header.Timestamp = timestamp;
                    rtpPacket.Header.MarkerBit = ((index + 1) * RTP_MAX_PAYLOAD < jpegBytes.Length) ? 0 : 1;
                    rtpPacket.Header.PayloadType = (int)SDPMediaFormatsEnum.JPEG;
                    rtpPacket.Payload = packetPayload.ToArray();

                    OnRtpPacketSent?.Invoke(rtpPacket);

                    byte[] rtpBytes = rtpPacket.GetBytes();

                    RtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, DestinationEndPoint, rtpBytes);
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
        public void SendH264Frame(uint timestamp, byte[] frame, uint frameSpacing, int payloadType, int streamID = 0)
        {
            if (m_isClosed || m_rtpEventInProgress || DestinationEndPoint == null)
            {
                return;
            }

            try
            {
                //System.Diagnostics.Debug.WriteLine("Sending " + frame.Length + " H264 encoded bytes to client, timestamp " + _timestamp + ", starting sequence number " + _sequenceNumber + ".");

                RTPSessionStream sessionStream = m_sessionStreams.Where(x => x.ID == streamID).Single();

                for (int index = 0; index * RTP_MAX_PAYLOAD < frame.Length; index++)
                {
                    uint offset = Convert.ToUInt32(index * RTP_MAX_PAYLOAD);
                    int payloadLength = ((index + 1) * RTP_MAX_PAYLOAD < frame.Length) ? RTP_MAX_PAYLOAD : frame.Length - index * RTP_MAX_PAYLOAD;

                    RTPPacket rtpPacket = new RTPPacket(payloadLength + H264_RTP_HEADER_LENGTH);
                    rtpPacket.Header.SyncSource = sessionStream.Ssrc;
                    rtpPacket.Header.SequenceNumber = sessionStream.SeqNum++;
                    rtpPacket.Header.Timestamp = timestamp;
                    rtpPacket.Header.MarkerBit = 0;
                    rtpPacket.Header.PayloadType = payloadType;

                    // Start RTP packet in frame 0x1c 0x89
                    // Middle RTP packet in frame 0x1c 0x09
                    // Last RTP packet in frame 0x1c 0x49

                    byte[] h264Header = new byte[] { 0x1c, 0x09 };

                    if (index == 0 && frame.Length < RTP_MAX_PAYLOAD)
                    {
                        // First and last RTP packet in the frame.
                        h264Header = new byte[] { 0x1c, 0x49 };
                        rtpPacket.Header.MarkerBit = 1;
                    }
                    else if (index == 0)
                    {
                        h264Header = new byte[] { 0x1c, 0x89 };
                    }
                    else if ((index + 1) * RTP_MAX_PAYLOAD > frame.Length)
                    {
                        h264Header = new byte[] { 0x1c, 0x49 };
                        rtpPacket.Header.MarkerBit = 1;
                    }

                    var h264Stream = frame.Skip(index * RTP_MAX_PAYLOAD).Take(payloadLength).ToList();
                    h264Stream.InsertRange(0, h264Header);
                    rtpPacket.Payload = h264Stream.ToArray();

                    OnRtpPacketSent?.Invoke(rtpPacket);

                    byte[] rtpBytes = rtpPacket.GetBytes();

                    RtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, DestinationEndPoint, rtpBytes);
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
            int clockRate = DEFAULT_AUDIO_CLOCK_RATE,
            int streamID = 0)
        {
            if (m_isClosed || m_rtpEventInProgress == true || DestinationEndPoint == null)
            {
                logger.LogWarning("SendDtmfEvent request ignored as an RTP event is already in progress.");
            }

            try
            {
                RTPSessionStream sessionStream = m_sessionStreams.Where(x => x.ID == streamID).Single();

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
                    SendRtpPacket(RtpChannel, DestinationEndPoint, buffer, startTimestamp, markerBit, rtpEvent.PayloadTypeID, sessionStream.Ssrc, sessionStream.SeqNum);

                    sessionStream.SeqNum++;
                }

                await Task.Delay(samplePeriod, cancellationToken);

                if (!rtpEvent.EndOfEvent)
                {
                    // Send the progressive event packets 
                    while ((rtpEvent.Duration + rtpTimestampStep) < rtpEvent.TotalDuration && !cancellationToken.IsCancellationRequested)
                    {
                        rtpEvent.Duration += rtpTimestampStep;
                        byte[] buffer = rtpEvent.GetEventPayload();

                        SendRtpPacket(RtpChannel, DestinationEndPoint, buffer, startTimestamp, 0, rtpEvent.PayloadTypeID, sessionStream.Ssrc, sessionStream.SeqNum);

                        sessionStream.SeqNum++;

                        await Task.Delay(samplePeriod, cancellationToken);
                    }

                    // Send the end of event packets.
                    for (int j = 0; j < RTPEvent.DUPLICATE_COUNT && !cancellationToken.IsCancellationRequested; j++)
                    {
                        rtpEvent.EndOfEvent = true;
                        rtpEvent.Duration = rtpEvent.TotalDuration;
                        byte[] buffer = rtpEvent.GetEventPayload();

                        SendRtpPacket(RtpChannel, DestinationEndPoint, buffer, startTimestamp, 0, rtpEvent.PayloadTypeID, sessionStream.Ssrc, sessionStream.SeqNum);

                        sessionStream.SeqNum++;
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

                OnRtpPacketReceived -= RtcpSession.RtpPacketReceived;
                OnRtpPacketSent -= RtcpSession.RtpPacketSent;
                RtcpSession.Close(reason);

                if (RtpChannel != null)
                {
                    RtpChannel.OnRTPDataReceived -= RtpPacketReceived;
                    RtpChannel.OnClosed -= OnRTPChannelClosed;
                    RtpChannel.OnControlDataReceived -= RtcpSession.ControlDataReceived;
                    RtpChannel.Close(reason);
                }

                OnRtpClosed?.Invoke(reason);
            }
        }

        /// <summary>
        /// Event handler for receiving data on the RTP channel.
        /// </summary>
        /// <param name="remoteEndPoint">The remote end point the data was received from.</param>
        /// <param name="buffer">The data received.</param>
        private void RtpPacketReceived(IPEndPoint remoteEndPoint, byte[] buffer)
        {
            if (m_lastReceiveFromEndPoint == null || !m_lastReceiveFromEndPoint.Equals(remoteEndPoint))
            {
                OnReceiveFromEndPointChanged?.Invoke(m_lastReceiveFromEndPoint, remoteEndPoint);
                m_lastReceiveFromEndPoint = remoteEndPoint;
            }

            // Quick sanity check on whether this is not an RTP or RTCP packet.
            if (buffer?.Length > RTPHeader.MIN_HEADER_LEN && buffer[0] >= 128 && buffer[0] <= 191)
            {
                var rtpPacket = new RTPPacket(buffer);

                OnRtpPacketReceived?.Invoke(rtpPacket);

                if (RemoteRtpEventPayloadID != 0 && rtpPacket.Header.PayloadType == RemoteRtpEventPayloadID)
                {
                    RTPEvent rtpEvent = new RTPEvent(rtpPacket.Payload);
                    OnRtpEvent?.Invoke(rtpEvent);
                }
                else
                {
                    OnReceivedSampleReady?.Invoke(rtpPacket.Payload);
                }
            }
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
        private void SendRtpPacket(RTPChannel rtpChannel, IPEndPoint dstRtpSocket, byte[] data, uint timestamp, int markerBit, int payloadType, uint ssrc, ushort seqNum)
        {
            int srtpProtectionLength = (SrtpProtect != null) ? SRTP_AUTH_KEY_LENGTH : 0;

            RTPPacket rtpPacket = new RTPPacket(data.Length + srtpProtectionLength);
            rtpPacket.Header.SyncSource = ssrc;
            rtpPacket.Header.SequenceNumber = seqNum;
            rtpPacket.Header.Timestamp = timestamp;
            rtpPacket.Header.MarkerBit = markerBit;
            rtpPacket.Header.PayloadType = payloadType;

            Buffer.BlockCopy(data, 0, rtpPacket.Payload, 0, data.Length);

            OnRtpPacketSent?.Invoke(rtpPacket);

            var rtpBuffer = rtpPacket.GetBytes();

            int rtperr = SrtpProtect == null ? 0 : SrtpProtect(rtpBuffer, rtpBuffer.Length - srtpProtectionLength);
            if (rtperr != 0)
            {
                logger.LogError("SendDtmfEvent SRTP packet protection failed, result " + rtperr + ".");
            }
            else
            {
                rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, dstRtpSocket, rtpBuffer);
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
