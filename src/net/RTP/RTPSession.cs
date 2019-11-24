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
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
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

        public const int RTP_EVENT_DEFAULT_SAMPLE_PERIOD_MS = 50; // Default sample period for an RTP event as specified by RFC2833.

        private static ILogger logger = Log.Logger;

        /// <summary>
        /// The payload type for the RTP packet header.
        /// </summary>
        public int PayloadType { get; private set; }
        public uint Ssrc { get; private set; }
        public ushort SeqNum { get; private set; }

        public uint PacketsSent { get; private set; }
        public uint OctetsSent { get; private set; }

        /// <summary>
        /// Function pointer to an SRTP context that encrypts an RTP packet.
        /// </summary>
        public ProtectRtpPacket SrtpProtect { get; private set; }

        /// <summary>
        /// Function pointer to an SRTCP context that encrypts an RTCP packet.
        /// </summary>
        public ProtectRtpPacket SrtcpProtect { get; private set; }

        /// <summary>
        /// Creates a new RTP session. The synchronisation source and sequence number are initialised to
        /// pseudo random values.
        /// </summary>
        /// <param name="payloadType">The payload type for the media attached to the sync source.</param>
        /// <param name="srtpProtect">Optional secure DTLS context for encrypting RTP packets.</param>
        /// <param name="srtcpProtect">Optional secure DTLS context for encrypting RTCP packets.</param>
        public RTPSession(int payloadType, ProtectRtpPacket srtpProtect, ProtectRtpPacket srtcpProtect)
        {
            PayloadType = payloadType;
            SrtpProtect = srtpProtect;
            SrtcpProtect = srtcpProtect;
            Ssrc = Convert.ToUInt32(Crypto.GetRandomInt(0, Int32.MaxValue));
            SeqNum = Convert.ToUInt16(Crypto.GetRandomInt(0, UInt16.MaxValue));
        }

        /// <summary>
        /// Packages and sends a single audio frame over one or more RTP packets.
        /// </summary>
        public void SendAudioFrame(Socket srcRtpSocket, IPEndPoint dstRtpSocket, uint timestamp, byte[] buffer)
        {
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
                    rtpPacket.Header.PayloadType = (int)PayloadType;

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
                }
            }
            catch (System.Net.Sockets.SocketException sockExcp)
            {
                logger.LogError("SocketException SendAudioFrame. " + sockExcp.Message);
            }
        }

        public void SendVp8Frame(Socket srcRtpSocket, IPEndPoint dstRtpSocket, uint timestamp, byte[] buffer)
        {
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
                    rtpPacket.Header.PayloadType = (int)PayloadType;

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
                }
            }
            catch (System.Net.Sockets.SocketException sockExcp)
            {
                logger.LogError("SocketException SendVp8Frame. " + sockExcp.Message);
            }
        }

        public void SendRtcpSenderReport(Socket srcRtpSocket, IPEndPoint dstRtpSocket, uint timestamp)
        {
            try
            {
                var ntp = RTSPSession.DateTimeToNptTimestamp(DateTime.Now);
                var rtcpSRPacket = new RTCPPacket(Ssrc, ntp, timestamp, PacketsSent, OctetsSent);

                if (SrtcpProtect == null)
                {
                    srcRtpSocket.SendTo(rtcpSRPacket.GetBytes(), dstRtpSocket);
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
                        srcRtpSocket.SendTo(sendBuffer, dstRtpSocket);
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
        /// <param name="srcRtpSocket">The local RTP socket to send the event from.</param>
        /// <param name="dstRtpSocket">The remote RTP socket to send the event to.</param>
        /// <param name="rtpEvent">The RTP event to send.</param>
        /// <param name="startTimestamp">The RTP timestamp at the start of the event.</param>
        /// <param name="samplePeriod">The sample period in milliseconds being used for the media stream that the event 
        /// is being inserted into. Should be set to 50ms if main media stream is dynamic or sample period is unknown.</param>
        /// <param name="timestampStep">The RTP timestamp step corresponding to the sampling period. This can change depending
        /// on the codec being used. For example using PCMU with a sampling frequency of 8000Hz the timestamp step
        /// for a sample period of 50ms is 400 (8000 / (1000 / 50)). For a sample period of 20ms it's 160 (8000 / (1000 / 20)).</param>
        /// <param name="cts">Token source to allow the operation to be cancelled prematurely.</param>
        public async Task SendDtmfEvent(Socket srcRtpSocket,
            IPEndPoint dstRtpSocket,
            RTPEvent rtpEvent,
            uint startTimestamp,
            ushort samplePeriod,
            ushort timestampStep,
            CancellationTokenSource cts)
        {
            try
            {
                // If only the minimum number of packets are being sent then they are both the start and end of the event.
                rtpEvent.EndOfEvent = (rtpEvent.TotalDuration <= timestampStep);
                rtpEvent.Duration = timestampStep;

                // Send the start of event packets.
                for (int i = 0; i < RTPEvent.DUPLICATE_COUNT && !cts.IsCancellationRequested; i++)
                {
                    byte[] buffer = rtpEvent.GetEventPayload();

                    int markerBit = (i == 0) ? 1 : 0;  // Set marker bit for the first packet in the event.
                    SendRtpPacket(srcRtpSocket, dstRtpSocket, buffer, startTimestamp, markerBit, rtpEvent.PayloadTypeID);

                    SeqNum++;
                    PacketsSent++;
                }

                await Task.Delay(samplePeriod, cts.Token);

                if (!rtpEvent.EndOfEvent)
                {
                    // Send the progressive event packets 
                    while ((rtpEvent.Duration + timestampStep) < rtpEvent.TotalDuration && !cts.IsCancellationRequested)
                    {
                        rtpEvent.Duration += timestampStep;
                        byte[] buffer = rtpEvent.GetEventPayload();

                        SendRtpPacket(srcRtpSocket, dstRtpSocket, buffer, startTimestamp, 0, rtpEvent.PayloadTypeID);

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

                        SendRtpPacket(srcRtpSocket, dstRtpSocket, buffer, startTimestamp, 0, rtpEvent.PayloadTypeID);

                        SeqNum++;
                        PacketsSent++;
                    }
                }
            }
            catch (System.Net.Sockets.SocketException sockExcp)
            {
                logger.LogError("SocketException SendDtmfEvent. " + sockExcp.Message);
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                logger.LogWarning("SendDtmfEvent was cancelled by caller.");
            }
        }

        /// <summary>
        /// Does the actual sending of an RTP packet using the specified data nad header values.
        /// </summary>
        /// <param name="srcRtpSocket">Socket to send from.</param>
        /// <param name="dstRtpSocket">Destination to send to.</param>
        /// <param name="data">The RTP packet payload.</param>
        /// <param name="timestamp">The RTP header timestamp.</param>
        /// <param name="markerBit">The RTP header marker bit.</param>
        /// <param name="payloadType">The RTP header payload type.</param>
        private void SendRtpPacket(Socket srcRtpSocket, IPEndPoint dstRtpSocket, byte[] data, uint timestamp, int markerBit, int payloadType)
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
                srcRtpSocket.SendTo(rtpBuffer, dstRtpSocket);
            }
        }
    }
}
