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

        public const int RTP_EVENT_DEFAULT_SAMPLE_PERIOD_MS = 50; // Default sample period for an RTP event as specified by RFC2833.

        private static ILogger logger = Log.Logger;

        private IPEndPoint m_lastReceiveFromEndPoint;
        private bool m_rtpEventInProgress;                   // Gets set to true when an RTP event is being sent and the normal stream is interrupted.
        private uint m_lastRtpTimestamp;                     // The last timestamp used in an RTP packet.    

        public uint Ssrc { get; private set; }
        public ushort SeqNum { get; private set; }

        public uint PacketsSent { get; private set; }
        public uint OctetsSent { get; private set; }

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
        /// Gest fired when an RTP packet is received, has been identified and is ready for processing.
        /// </summary>
        public event Action<byte[]> OnReceivedSampleReady;

        /// <summary>
        /// Creates a new RTP session. The synchronisation source and sequence number are initialised to
        /// pseudo random values.
        /// </summary>
        /// <param name="formatTypeID">The format type ID for the media. It's what gets set in the payload 
        /// type ID field in the RTP header. A default media announcement will be created.</param>
        /// <param name="srtpProtect">Optional secure DTLS context for encrypting RTP packets.</param>
        /// <param name="srtcpProtect">Optional secure DTLS context for encrypting RTCP packets.</param>
        public RTPSession(int formatTypeID, ProtectRtpPacket srtpProtect, ProtectRtpPacket srtcpProtect)
        {
            MediaFormat = new SDPMediaFormat(formatTypeID);
            MediaAnnouncement = new SDPMediaAnnouncement
            {
                MediaFormats = new List<SDPMediaFormat> { MediaFormat }
            };
            FormatTypeID = formatTypeID;
            SrtpProtect = srtpProtect;
            SrtcpProtect = srtcpProtect;
            Ssrc = Convert.ToUInt32(Crypto.GetRandomInt(0, Int32.MaxValue));
            SeqNum = Convert.ToUInt16(Crypto.GetRandomInt(0, UInt16.MaxValue));
        }

        /// <summary>
        /// Creates a new RTP session. The synchronisation source and sequence number are initialised to
        /// pseudo random values.
        /// </summary>
        /// <param name="mediaAnnouncement">The media announcement describing this session.</param>
        /// <param name="formatTypeID">The format type ID for the media. It must match one of the formats specified in the 
        /// media announcement. It's what gets set in the payload type ID field in the RTP header.</param>
        /// <param name="srtpProtect">Optional secure DTLS context for encrypting RTP packets.</param>
        /// <param name="srtcpProtect">Optional secure DTLS context for encrypting RTCP packets.</param>
        public RTPSession(SDPMediaAnnouncement mediaAnnouncement, int formatTypeID, ProtectRtpPacket srtpProtect, ProtectRtpPacket srtcpProtect)
        {
            if(mediaAnnouncement == null)
            {
                throw new ArgumentException("The mediaAnnouncement parameter cannot be null", "mediaAnnouncement");
            }
            else if(mediaAnnouncement.MediaFormats.Any(x => x.FormatID == formatTypeID.ToString()) == false)
            {
                throw new ArgumentException("The mediaAnnouncement did not contain a matching entry for the formatTypeID.", "formatTypeID");
            }
            
            MediaAnnouncement = mediaAnnouncement;
            MediaFormat = mediaAnnouncement.MediaFormats.First(x => x.FormatID == formatTypeID.ToString());
            FormatTypeID = formatTypeID;
            SrtpProtect = srtpProtect;
            SrtcpProtect = srtcpProtect;
            Ssrc = Convert.ToUInt32(Crypto.GetRandomInt(0, Int32.MaxValue));
            SeqNum = Convert.ToUInt16(Crypto.GetRandomInt(0, UInt16.MaxValue));
        }

        public void RtpPacketReceived(IPEndPoint remoteEndPoint, byte[] buffer)
        {
            if (m_lastReceiveFromEndPoint == null || !m_lastReceiveFromEndPoint.Equals(remoteEndPoint))
            {
                OnReceiveFromEndPointChanged?.Invoke(m_lastReceiveFromEndPoint, remoteEndPoint);
                m_lastReceiveFromEndPoint = remoteEndPoint;
            }

            var rtpPacket = new RTPPacket(buffer);

            OnReceivedSampleReady?.Invoke(rtpPacket.Payload);
        }

        /// <summary>
        /// Packages and sends a single audio frame over one or more RTP packets.
        /// </summary>
        public void SendAudioFrame(Socket srcRtpSocket, IPEndPoint dstRtpSocket, uint timestamp, byte[] buffer)
        {
            if(m_rtpEventInProgress)
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
                    rtpPacket.Header.MarkerBit = ((offset + payloadLength) >= buffer.Length) ? 1 : 0; // Set marker bit for the last packet in the frame.
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
                        srcRtpSocket.SendTo(rtpBuffer, dstRtpSocket);
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

        public void SendAudioFrame(RTPChannel2 rtpChannel, IPEndPoint dstRtpSocket, uint timestamp, byte[] buffer)
        {
            if (m_rtpEventInProgress)
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
                    rtpPacket.Header.MarkerBit = ((offset + payloadLength) >= buffer.Length) ? 1 : 0; // Set marker bit for the last packet in the frame.
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
                        rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, dstRtpSocket, rtpBuffer);
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

        public void SendVp8Frame(Socket srcRtpSocket, IPEndPoint dstRtpSocket, uint timestamp, byte[] buffer)
        {
            if (m_rtpEventInProgress)
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
                        srcRtpSocket.SendTo(rtpBuffer, dstRtpSocket);
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
        /// <param name="rtpChannel">The RTP channel to send the event from.</param>
        /// <param name="dstRtpSocket">The remote RTP socket to send the event to.</param>
        /// <param name="rtpEvent">The RTP event to send.</param>
        ///  <param name="cts">Token source to allow the operation to be cancelled prematurely.</param>
        public async Task SendDtmfEvent(RTPChannel2 rtpChannel,
            IPEndPoint dstRtpSocket,
            RTPEvent rtpEvent,
            CancellationTokenSource cts)
        {
            if(m_rtpEventInProgress == true)
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
                if(clockRate == 0)
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
                for (int i = 0; i < RTPEvent.DUPLICATE_COUNT && !cts.IsCancellationRequested; i++)
                {
                    byte[] buffer = rtpEvent.GetEventPayload();

                    int markerBit = (i == 0) ? 1 : 0;  // Set marker bit for the first packet in the event.
                    SendRtpPacket(rtpChannel, dstRtpSocket, buffer, startTimestamp, markerBit, rtpEvent.PayloadTypeID);

                    SeqNum++;
                    PacketsSent++;
                }

                await Task.Delay(samplePeriod, cts.Token);

                if (!rtpEvent.EndOfEvent)
                {
                    // Send the progressive event packets 
                    while ((rtpEvent.Duration + rtpTimestampStep) < rtpEvent.TotalDuration && !cts.IsCancellationRequested)
                    {
                        rtpEvent.Duration += rtpTimestampStep;
                        byte[] buffer = rtpEvent.GetEventPayload();

                        SendRtpPacket(rtpChannel, dstRtpSocket, buffer, startTimestamp, 0, rtpEvent.PayloadTypeID);

                        PacketsSent++;
                        SeqNum++;

                        await Task.Delay(samplePeriod, cts.Token);
                    }

                    // Send the end of event packets.
                    for (int j = 0; j < RTPEvent.DUPLICATE_COUNT && !cts.IsCancellationRequested; j++)
                    {
                        rtpEvent.EndOfEvent = true;
                        rtpEvent.Duration = rtpEvent.TotalDuration;
                        byte[] buffer = rtpEvent.GetEventPayload();

                        SendRtpPacket(rtpChannel, dstRtpSocket, buffer, startTimestamp, 0, rtpEvent.PayloadTypeID);

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
        /// Processes received RTP packets.
        /// </summary>
        /// <param name="buffer">The raw data received on the RTP socket.</param>
        /// <param name="offset">Offset in the buffer that the received data starts from.</param>
        /// <param name="count">The number of bytes received.</param>
        /// <param name="remoteEndPoint">The remote end point the receive was from.</param>
        /// <returns>An RTP packet.</returns>
        [Obsolete]
        public RTPPacket RtpReceive(byte[] buffer, int offset, int count, IPEndPoint remoteEndPoint)
        {
            if (m_lastReceiveFromEndPoint == null || !m_lastReceiveFromEndPoint.Equals(remoteEndPoint))
            {
                OnReceiveFromEndPointChanged?.Invoke(m_lastReceiveFromEndPoint, remoteEndPoint);
                m_lastReceiveFromEndPoint = remoteEndPoint;
            }

            return new RTPPacket(buffer.Skip(offset).Take(count).ToArray());
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
        private void SendRtpPacket(RTPChannel2 rtpChannel, IPEndPoint dstRtpSocket, byte[] data, uint timestamp, int markerBit, int payloadType)
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
    }
}
