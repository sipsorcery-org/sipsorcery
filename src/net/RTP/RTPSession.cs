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

    public class RTPSession
    {
        private const int RTP_MAX_PAYLOAD = 1400;
        private const int SRTP_AUTH_KEY_LENGTH = 10;
        private const int DEFAULT_AUDIO_CLOCK_RATE = 8000;
        public const int H264_RTP_HEADER_LENGTH = 2;
        public const string TELEPHONE_EVENT_ATTRIBUTE = "telephone-event";

        public const int RTP_EVENT_DEFAULT_SAMPLE_PERIOD_MS = 50; // Default sample period for an RTP event as specified by RFC2833.

        public const int DTMF_EVENT_PAYLOAD_ID = 101;

        private static ILogger logger = Log.Logger;

        private IPEndPoint m_lastReceiveFromEndPoint;
        private bool m_rtpEventInProgress;               // Gets set to true when an RTP event is being sent and the normal stream is interrupted.
        private uint m_lastRtpTimestamp;                 // The last timestamp used in an RTP packet.    
        private bool m_rtpEventSupport;                  // True if this session is supporting RTP events.
        private int m_remoteRtpEventPayloadID;           // If the remote party supports RTP events this is the RTP header payload ID they are using.

        public uint Ssrc { get; private set; }
        public ushort SeqNum { get; private set; }

        public uint PacketsSent { get; private set; }
        public uint OctetsSent { get; private set; }

        /// <summary>
        /// The RTP communications channel this session is sending and receiving on.
        /// </summary>
        public RTPChannel RtpChannel { get; private set; }

        /// <summary>
        /// The media announcement from the Session Description Protocol that describes this RTP session.
        /// <code>
        /// // Example:
        /// m=audio 10000 RTP/AVP 0
        /// a=rtpmap:0 PCMU/8000
        /// a=rtpmap:101 telephone-event/8000
        /// a=fmtp:101 0-15
        /// a=sendrecv
        /// </code>
        /// </summary>
        public SDPMediaAnnouncement MediaAnnouncement { get; private set; }

        /// <summary>
        /// The format from within the media announcement that the session is currently using.
        /// </summary>
        public SDPMediaFormat MediaFormat { get; private set; }

        /// <summary>
        /// The selected format (codec) ID from the type available in the media announcment.
        /// </summary>
        public int FormatTypeID { get; private set; }

        /// <summary>
        /// Function pointer to an SRTP context that encrypts an RTP packet.
        /// </summary>
        public ProtectRtpPacket SrtpProtect { get; private set; }

        /// <summary>
        /// Function pointer to an SRTCP context that encrypts an RTCP packet.
        /// </summary>
        public ProtectRtpPacket SrtcpProtect { get; private set; }

        /// <summary>
        /// The remote end point this session is sending to.
        /// </summary>
        public IPEndPoint DestinationEndPoint;

        /// <summary>
        /// The SDP offered by the remote call party for this session.
        /// </summary>
        public SDP RemoteSDP { get; private set; }

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
        /// Gets fired if a network error indicates the remote RTP socket is no longer accepting packets.
        /// </summary>
        public event Action OnRtpDisconnect;

        /// <summary>
        /// Creates a new RTP session. The synchronisation source and sequence number are initialised to
        /// pseudo random values.
        /// </summary>
        /// <param name="formatTypeID">The format type ID for the media. It's what gets set in the payload 
        /// type ID field in the RTP header. A default media announcement will be created.</param>
        /// <param name="srtpProtect">Optional secure DTLS context for encrypting RTP packets.</param>
        /// <param name="srtcpProtect">Optional secure DTLS context for encrypting RTCP packets.</param>
        /// <param name="rtpEventSupport">True if RTP event sending and receiving should be supported.</param>
        /// <param name="addrFamily">Determines whether the RTP channel will use an IPv4 or IPv6 socket.</param>
        public RTPSession(int formatTypeID, ProtectRtpPacket srtpProtect, ProtectRtpPacket srtcpProtect, bool rtpEventSupport, AddressFamily addrFamily)
        {
            MediaFormat = new SDPMediaFormat(formatTypeID);
            MediaAnnouncement = new SDPMediaAnnouncement
            {
                Media = SDPMediaTypesEnum.audio,
                MediaFormats = new List<SDPMediaFormat> { MediaFormat },
                MediaStreamStatus = MediaStreamStatusEnum.SendRecv
            };

            if (rtpEventSupport)
            {
                int clockRate = MediaFormat.GetClockRate();
                SDPMediaFormat rtpEventFormat = new SDPMediaFormat(DTMF_EVENT_PAYLOAD_ID);
                rtpEventFormat.SetFormatAttribute($"{TELEPHONE_EVENT_ATTRIBUTE}/{clockRate}");
                rtpEventFormat.SetFormatParameterAttribute("0-16");
                MediaAnnouncement.MediaFormats.Add(rtpEventFormat);
            }

            m_rtpEventSupport = rtpEventSupport;
            FormatTypeID = formatTypeID;
            SrtpProtect = srtpProtect;
            SrtcpProtect = srtcpProtect;

            Initialise(addrFamily);
        }

        /// <summary>
        /// Initialises the RTP session state and starts the RTP channel UDP sockets.
        /// </summary>
        private void Initialise(AddressFamily addrFamily)
        {
            Ssrc = Convert.ToUInt32(Crypto.GetRandomInt(0, Int32.MaxValue));
            SeqNum = Convert.ToUInt16(Crypto.GetRandomInt(0, UInt16.MaxValue));

            RtpChannel = new RTPChannel((addrFamily == AddressFamily.InterNetworkV6) ? IPAddress.IPv6Any : IPAddress.Any, true);

            MediaAnnouncement.Port = RtpChannel.RTPPort;
            RtpChannel.OnRTPDataReceived += RtpPacketReceived;
            RtpChannel.OnRTPSocketDisconnected += OnRTPSocketDisconnected;

            // Start the RTP and Control socket receivers.
            RtpChannel.Start();
        }

        /// <summary>
        /// Sets the remote SDP offer for this RTP session. It contains required information about payload ID's
        /// for media formats and RTP evetns.
        /// </summary>
        /// <param name="sdp">The SDP from the remote call party.</param>
        public void SetRemoteSDP(SDP sdp)
        {
            RemoteSDP = sdp;

            foreach (var announcement in sdp.Media.Where(x => x.Media == SDPMediaTypesEnum.audio))
            {
                foreach (var mediaFormat in announcement.MediaFormats)
                {
                    if (mediaFormat.FormatAttribute?.StartsWith(TELEPHONE_EVENT_ATTRIBUTE) == true)
                    {
                        if (!int.TryParse(mediaFormat.FormatID, out m_remoteRtpEventPayloadID))
                        {
                            logger.LogWarning("The media format on the telpehone event attribute was not a valid integer.");
                        }
                        break;
                    }
                }
            }
        }

        public void SendAudioFrame(uint timestamp, byte[] buffer)
        {
            if (m_rtpEventInProgress || DestinationEndPoint == null)
            {
                return;
            }

            try
            {
                for (int index = 0; index * RTP_MAX_PAYLOAD < buffer.Length; index++)
                {
                    SeqNum = (ushort)(SeqNum % UInt16.MaxValue);

                    int offset = (index == 0) ? 0 : (index * RTP_MAX_PAYLOAD);
                    int payloadLength = (offset + RTP_MAX_PAYLOAD < buffer.Length) ? RTP_MAX_PAYLOAD : buffer.Length - offset;
                    int srtpProtectionLength = (SrtpProtect != null) ? SRTP_AUTH_KEY_LENGTH : 0;

                    RTPPacket rtpPacket = new RTPPacket(payloadLength + srtpProtectionLength);
                    rtpPacket.Header.SyncSource = Ssrc;
                    rtpPacket.Header.SequenceNumber = SeqNum++;
                    rtpPacket.Header.Timestamp = timestamp;
                    // RFC3551 specifies that for audio the marker bit should always be 0 except for when returning
                    // from silence suppression. For video the marker bit DOES get set to 1 for the last packet
                    // in a frame.
                    rtpPacket.Header.MarkerBit = 0;
                    rtpPacket.Header.PayloadType = FormatTypeID;

                    Buffer.BlockCopy(buffer, offset, rtpPacket.Payload, 0, payloadLength);

                    var rtpBuffer = rtpPacket.GetBytes();

                    int rtperr = SrtpProtect == null ? 0 : SrtpProtect(rtpBuffer, rtpBuffer.Length - srtpProtectionLength);
                    if (rtperr != 0)
                    {
                        logger.LogError("SendAudioFrame SRTP packet protection failed, result " + rtperr + ".");
                    }
                    else
                    {
                        var sendResult = RtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, DestinationEndPoint, rtpBuffer);
                        
                        if(sendResult != SocketError.Success)
                        {
                            //logger.LogWarning($"RTPChannel SendAudioFrame failed with {sendResult}.");
                            OnRtpDisconnect?.Invoke();
                            break;
                        }
                    }

                    PacketsSent++;
                    OctetsSent += (uint)payloadLength;
                    m_lastRtpTimestamp = timestamp;
                }
            }
            catch (SocketException sockExcp)
            {
                logger.LogError("SocketException SendAudioFrame. " + sockExcp.Message);
            }
        }

        public void SendVp8Frame(uint timestamp, byte[] buffer)
        {
            if (m_rtpEventInProgress || DestinationEndPoint == null)
            {
                return;
            }

            try
            {
                for (int index = 0; index * RTP_MAX_PAYLOAD < buffer.Length; index++)
                {
                    SeqNum = (ushort)(SeqNum % UInt16.MaxValue);

                    int offset = (index == 0) ? 0 : (index * RTP_MAX_PAYLOAD);
                    int payloadLength = (offset + RTP_MAX_PAYLOAD < buffer.Length) ? RTP_MAX_PAYLOAD : buffer.Length - offset;
                    int srtpProtectionLength = (SrtpProtect != null) ? SRTP_AUTH_KEY_LENGTH : 0;

                    byte[] vp8HeaderBytes = (index == 0) ? new byte[] { 0x10 } : new byte[] { 0x00 };

                    RTPPacket rtpPacket = new RTPPacket(payloadLength + vp8HeaderBytes.Length + srtpProtectionLength);
                    rtpPacket.Header.SyncSource = Ssrc;
                    rtpPacket.Header.SequenceNumber = SeqNum++;
                    rtpPacket.Header.Timestamp = timestamp;
                    rtpPacket.Header.MarkerBit = ((offset + payloadLength) >= buffer.Length) ? 1 : 0; // Set marker bit for the last packet in the frame.
                    rtpPacket.Header.PayloadType = FormatTypeID;

                    Buffer.BlockCopy(vp8HeaderBytes, 0, rtpPacket.Payload, 0, vp8HeaderBytes.Length);
                    Buffer.BlockCopy(buffer, offset, rtpPacket.Payload, vp8HeaderBytes.Length, payloadLength);

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

                    PacketsSent++;
                    OctetsSent += (uint)payloadLength;
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
        /// <param name="jpegBytes">The raw encoded bytes of teh JPEG image to transmit.</param>
        /// <param name="jpegQuality">The encoder quality of the JPEG image.</param>
        /// <param name="jpegWidth">The width of the JPEG image.</param>
        /// <param name="jpegHeight">The height of the JPEG image.</param>
        /// <param name="framesPerSecond">The rate at which the JPEG frames are being transmitted at. used to calculate the timestamp.</param>
        public void SendJpegFrame(uint timestamp, byte[] jpegBytes, int jpegQuality, int jpegWidth, int jpegHeight, int framesPerSecond)
        {
            if (m_rtpEventInProgress || DestinationEndPoint == null)
            {
                return;
            }

            try
            {
                //_timestamp = (_timestamp == 0) ? DateTimeToNptTimestamp32(DateTime.Now) : (_timestamp + (uint)(RFC_2435_FREQUENCY_BASELINE / framesPerSecond)) % UInt32.MaxValue;

                //System.Diagnostics.Debug.WriteLine("Sending " + jpegBytes.Length + " encoded bytes to client, timestamp " + _timestamp + ", starting sequence number " + _sequenceNumber + ", image dimensions " + jpegWidth + " x " + jpegHeight + ".");

                for (int index = 0; index * RTP_MAX_PAYLOAD < jpegBytes.Length; index++)
                {
                    uint offset = Convert.ToUInt32(index * RTP_MAX_PAYLOAD);
                    int payloadLength = ((index + 1) * RTP_MAX_PAYLOAD < jpegBytes.Length) ? RTP_MAX_PAYLOAD : jpegBytes.Length - index * RTP_MAX_PAYLOAD;

                    byte[] jpegHeader = CreateLowQualityRtpJpegHeader(offset, jpegQuality, jpegWidth, jpegHeight);

                    List<byte> packetPayload = new List<byte>();
                    packetPayload.AddRange(jpegHeader);
                    packetPayload.AddRange(jpegBytes.Skip(index * RTP_MAX_PAYLOAD).Take(payloadLength));

                    RTPPacket rtpPacket = new RTPPacket(packetPayload.Count);
                    rtpPacket.Header.SyncSource = Ssrc;
                    rtpPacket.Header.SequenceNumber = SeqNum++;
                    rtpPacket.Header.Timestamp = timestamp;
                    rtpPacket.Header.MarkerBit = ((index + 1) * RTP_MAX_PAYLOAD < jpegBytes.Length) ? 0 : 1;
                    rtpPacket.Header.PayloadType = (int)SDPMediaFormatsEnum.JPEG;
                    rtpPacket.Payload = packetPayload.ToArray();

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
        public void SendH264Frame(uint timestamp, byte[] frame, uint frameSpacing, int payloadType)
        {
            if (m_rtpEventInProgress || DestinationEndPoint == null)
            {
                return;
            }

            try
            {
                //_timestamp = (_timestamp == 0) ? DateTimeToNptTimestamp32(DateTime.Now) : (_timestamp + frameSpacing) % UInt32.MaxValue;

                //System.Diagnostics.Debug.WriteLine("Sending " + frame.Length + " H264 encoded bytes to client, timestamp " + _timestamp + ", starting sequence number " + _sequenceNumber + ".");

                for (int index = 0; index * RTP_MAX_PAYLOAD < frame.Length; index++)
                {
                    uint offset = Convert.ToUInt32(index * RTP_MAX_PAYLOAD);
                    int payloadLength = ((index + 1) * RTP_MAX_PAYLOAD < frame.Length) ? RTP_MAX_PAYLOAD : frame.Length - index * RTP_MAX_PAYLOAD;

                    RTPPacket rtpPacket = new RTPPacket(payloadLength + H264_RTP_HEADER_LENGTH);
                    rtpPacket.Header.SyncSource = Ssrc;
                    rtpPacket.Header.SequenceNumber = SeqNum++;
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

                    byte[] rtpBytes = rtpPacket.GetBytes();

                    RtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, DestinationEndPoint, rtpBytes);
                }
            }
            catch (SocketException sockExcp)
            {
                logger.LogError("SocketException SendH264Frame. " + sockExcp.Message);
            }
        }

        public void SendRtcpSenderReport(Socket srcControlSocket, IPEndPoint dstRtpSocket, uint timestamp)
        {
            try
            {
                var ntp = RTSPSession.DateTimeToNptTimestamp(DateTime.Now);
                var rtcpSRPacket = new RTCPPacket(Ssrc, ntp, timestamp, PacketsSent, OctetsSent);

                if (SrtcpProtect == null)
                {
                    srcControlSocket.SendTo(rtcpSRPacket.GetBytes(), dstRtpSocket);
                }
                else
                {
                    var rtcpSRBytes = rtcpSRPacket.GetBytes();
                    byte[] sendBuffer = new byte[rtcpSRBytes.Length + SRTP_AUTH_KEY_LENGTH];
                    Buffer.BlockCopy(rtcpSRBytes, 0, sendBuffer, 0, rtcpSRBytes.Length);

                    int rtperr = SrtcpProtect(sendBuffer, sendBuffer.Length - SRTP_AUTH_KEY_LENGTH);
                    if (rtperr != 0)
                    {
                        logger.LogWarning("SRTP RTCP packet protection failed, result " + rtperr + ".");
                    }
                    else
                    {
                        srcControlSocket.SendTo(sendBuffer, dstRtpSocket);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogWarning("Exception SendRtcpSenderReport. " + excp.Message);
            }
        }

        /// <summary>
        /// Sends an RTP event for a DTMF tone as per RFC2833. Sending the event requires multiple packets to be sent.
        /// This method will hold onto the socket until all the packets required for the event have been sent. The send
        /// can be cancelled using the cancellation token.
        /// </summary>
        /// <param name="rtpEvent">The RTP event to send.</param>
        ///  <param name="cancellationToken">CancellationToken to allow the operation to be cancelled prematurely.</param>
        public async Task SendDtmfEvent(
            RTPEvent rtpEvent,
            CancellationToken cancellationToken)
        {
            if (m_rtpEventInProgress == true || DestinationEndPoint == null)
            {
                logger.LogWarning("SendDtmfEvent request ignored as an RTP event is already in progress.");
            }

            try
            {
                m_rtpEventInProgress = true;
                uint startTimestamp = m_lastRtpTimestamp;

                // The sample period in milliseconds being used for the media stream that the event 
                // is being inserted into. Should be set to 50ms if main media stream is dynamic or 
                // sample period is unknown.
                int samplePeriod = RTP_EVENT_DEFAULT_SAMPLE_PERIOD_MS;

                int clockRate = MediaFormat.ClockRate;

                // If the clock rate is unknown or dynamic cross our fingers and use 8KHz.
                if (clockRate == 0)
                {
                    clockRate = DEFAULT_AUDIO_CLOCK_RATE;
                }

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
                    SendRtpPacket(RtpChannel, DestinationEndPoint, buffer, startTimestamp, markerBit, rtpEvent.PayloadTypeID);

                    SeqNum++;
                    PacketsSent++;
                }

                await Task.Delay(samplePeriod, cancellationToken);

                if (!rtpEvent.EndOfEvent)
                {
                    // Send the progressive event packets 
                    while ((rtpEvent.Duration + rtpTimestampStep) < rtpEvent.TotalDuration && !cancellationToken.IsCancellationRequested)
                    {
                        rtpEvent.Duration += rtpTimestampStep;
                        byte[] buffer = rtpEvent.GetEventPayload();

                        SendRtpPacket(RtpChannel, DestinationEndPoint, buffer, startTimestamp, 0, rtpEvent.PayloadTypeID);

                        PacketsSent++;
                        SeqNum++;

                        await Task.Delay(samplePeriod, cancellationToken);
                    }

                    // Send the end of event packets.
                    for (int j = 0; j < RTPEvent.DUPLICATE_COUNT && !cancellationToken.IsCancellationRequested; j++)
                    {
                        rtpEvent.EndOfEvent = true;
                        rtpEvent.Duration = rtpEvent.TotalDuration;
                        byte[] buffer = rtpEvent.GetEventPayload();

                        SendRtpPacket(RtpChannel, DestinationEndPoint, buffer, startTimestamp, 0, rtpEvent.PayloadTypeID);

                        SeqNum++;
                        PacketsSent++;
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
        /// Gets the a basic Session Description Protocol object that describes this RTP session.
        /// </summary>
        /// <param name="localAddress">The RTP socket we will be sending from. Note this can't be IPAddress.Any as
        /// it's getting sent to the callee. An IP address of 0.0.0.0 or [::0] will typically be interpreted as
        /// "don't send me any RTP".</param>
        /// <returns>An Session Description Protocol object that can be sent to a remote callee.</returns>
        public SDP GetSDP(IPAddress localAddress)
        {
            var sdp = new SDP(localAddress)
            {
                SessionId = Crypto.GetRandomInt(5).ToString(),
                SessionName = "sipsorcery",
                Timing = "0 0",
                Connection = new SDPConnectionInformation(localAddress),
            };

            sdp.Media.Add(MediaAnnouncement);

            return sdp;
        }

        /// <summary>
        /// Close the session and RTP channel.
        /// </summary>
        public void Close()
        {
            RtpChannel?.Close();
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

            var rtpPacket = new RTPPacket(buffer);

            if (m_remoteRtpEventPayloadID != 0 && rtpPacket.Header.PayloadType == m_remoteRtpEventPayloadID)
            {
                RTPEvent rtpEvent = new RTPEvent(rtpPacket.Payload);
                OnRtpEvent?.Invoke(rtpEvent);
            }
            else
            {
                OnReceivedSampleReady?.Invoke(rtpPacket.Payload);
            }
        }

        /// <summary>
        /// Does the actual sending of an RTP packet using the specified data nad header values.
        /// </summary>
        /// <param name="rtpChannel">The RTP channel to send from.</param>
        /// <param name="dstRtpSocket">Destination to send to.</param>
        /// <param name="data">The RTP packet payload.</param>
        /// <param name="timestamp">The RTP header timestamp.</param>
        /// <param name="markerBit">The RTP header marker bit.</param>
        /// <param name="payloadType">The RTP header payload type.</param>
        private void SendRtpPacket(RTPChannel rtpChannel, IPEndPoint dstRtpSocket, byte[] data, uint timestamp, int markerBit, int payloadType)
        {
            int srtpProtectionLength = (SrtpProtect != null) ? SRTP_AUTH_KEY_LENGTH : 0;

            RTPPacket rtpPacket = new RTPPacket(data.Length + srtpProtectionLength);
            rtpPacket.Header.SyncSource = Ssrc;
            rtpPacket.Header.SequenceNumber = SeqNum;
            rtpPacket.Header.Timestamp = timestamp;
            rtpPacket.Header.MarkerBit = markerBit;
            rtpPacket.Header.PayloadType = payloadType;

            Buffer.BlockCopy(data, 0, rtpPacket.Payload, 0, data.Length);

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
        /// Event handler for network errors indicating the remote RTP socket is no longer accepting packets.
        /// </summary>
        private void OnRTPSocketDisconnected()
        {
            DestinationEndPoint = null;
            OnRtpDisconnect?.Invoke();
        }
    }
}
