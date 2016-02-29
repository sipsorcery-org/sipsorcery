using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using SIPSorceryMedia;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using log4net;

namespace WebRTCVideoServer
{
    public class WebRtcSession
    {
        private const int RTP_MAX_PAYLOAD = 1400;
        private const int TIMESTAMP_SPACING = 3000;
        private const int PAYLOAD_TYPE_ID = 100;
        private const int SRTP_AUTH_KEY_LENGTH = 10;

        private static ILog logger = AppState.logger;

        public WebRtcPeer Peer;
        public DtlsManaged DtlsContext;
        public SRTPManaged SrtpContext;
        public SRTPManaged SrtpReceiveContext;  // Used to decrypt packets received from the remote peer.

        public string CallID
        {
            get { return Peer.CallID; }
        }

        public WebRtcSession(string callID)
        {
            Peer = new WebRtcPeer() { CallID = callID };
        }

        public void DtlsPacketReceived(IceCandidate iceCandidate, byte[] buffer, IPEndPoint remoteEndPoint)
        {
            logger.Debug("DTLS packet received " + buffer.Length + " bytes from " + remoteEndPoint.ToString() + ".");

            if (DtlsContext == null)
            {
                DtlsContext = new DtlsManaged();
                int res = DtlsContext.Init();
                Console.WriteLine("DtlsContext initialisation result=" + res);
            }

            int bytesWritten = DtlsContext.Write(buffer, buffer.Length);

            if (bytesWritten != buffer.Length)
            {
                logger.Warn("The required number of bytes were not successfully written to the DTLS context.");
            }
            else
            {
                byte[] dtlsOutBytes = new byte[2048];

                int bytesRead = DtlsContext.Read(dtlsOutBytes, dtlsOutBytes.Length);

                if (bytesRead == 0)
                {
                    Console.WriteLine("No bytes read from DTLS context :(.");
                }
                else
                {
                    Console.WriteLine(bytesRead + " bytes read from DTLS context sending to " + remoteEndPoint.ToString() + ".");
                    iceCandidate.LocalRtpSocket.SendTo(dtlsOutBytes, 0, bytesRead, SocketFlags.None, remoteEndPoint);

                    //if (client.DtlsContext.IsHandshakeComplete())
                    if (DtlsContext.GetState() == 3)
                    {
                        Console.WriteLine("DTLS negotiation complete for " + remoteEndPoint.ToString() + ".");
                        SrtpContext = new SRTPManaged(DtlsContext, false);
                        SrtpReceiveContext = new SRTPManaged(DtlsContext, true);
                        Peer.IsDtlsNegotiationComplete = true;
                        iceCandidate.RemoteRtpEndPoint = remoteEndPoint;
                    }
                }
            }
        }

        public void MediaPacketReceived(IceCandidate iceCandidate, byte[] buffer, IPEndPoint remoteEndPoint)
        {
            if ((buffer[0] >= 128) && (buffer[0] <= 191))
            {
                //logger.Debug("A non-STUN packet was received Receiver Client.");

                if (buffer[1] == 0xC8 /* RTCP SR */ || buffer[1] == 0xC9 /* RTCP RR */)
                {
                    // RTCP packet.
                    //webRtcClient.LastSTUNReceiveAt = DateTime.Now;
                }
                else
                {
                    // RTP packet.
                    //int res = peer.SrtpReceiveContext.UnprotectRTP(buffer, buffer.Length);

                    //if (res != 0)
                    //{
                    //    logger.Warn("SRTP unprotect failed, result " + res + ".");
                    //}
                }
            }
            else
            {
                logger.Debug("An unrecognised packet was received on the WebRTC media socket.");
            }
        }

        public void Send(byte[] buffer)
        {
            try
            {
                //if (client.LastRtcpSenderReportSentAt == DateTime.MinValue)
                //{
                //    logger.Debug("Sending RTCP report to " + client.SocketAddress + ".");

                //    // Send RTCP report.
                //    RTCPPacket rtcp = new RTCPPacket(client.SSRC, 0, 0, 0, 0);
                //    byte[] rtcpBuffer = rtcp.GetBytes();
                //    _webRTCReceiverClient.BeginSend(rtcpBuffer, rtcpBuffer.Length, client.SocketAddress, null, null);
                //    //int rtperr = client.SrtpContext.ProtectRTP(rtcpBuffer, rtcpBuffer.Length - SRTP_AUTH_KEY_LENGTH);
                //}

                //Console.WriteLine("Sending VP8 frame of " + encodedBuffer.Length + " bytes to " + client.SocketAddress + ".");

                Peer.LastTimestamp = (Peer.LastTimestamp == 0) ? RTSPSession.DateTimeToNptTimestamp32(DateTime.Now) : Peer.LastTimestamp + TIMESTAMP_SPACING;

                for (int index = 0; index * RTP_MAX_PAYLOAD < buffer.Length; index++)
                {
                    int offset = (index == 0) ? 0 : (index * RTP_MAX_PAYLOAD);
                    int payloadLength = (offset + RTP_MAX_PAYLOAD < buffer.Length) ? RTP_MAX_PAYLOAD : buffer.Length - offset;

                    byte[] vp8HeaderBytes = (index == 0) ? new byte[] { 0x10 } : new byte[] { 0x00 };

                    RTPPacket rtpPacket = new RTPPacket(payloadLength + SRTP_AUTH_KEY_LENGTH + vp8HeaderBytes.Length);
                    rtpPacket.Header.SyncSource = Peer.SSRC;
                    rtpPacket.Header.SequenceNumber = Peer.SequenceNumber++;
                    rtpPacket.Header.Timestamp = Peer.LastTimestamp;
                    rtpPacket.Header.MarkerBit = ((offset + payloadLength) >= buffer.Length) ? 1 : 0; // Set marker bit for the last packet in the frame.
                    rtpPacket.Header.PayloadType = PAYLOAD_TYPE_ID;

                    Buffer.BlockCopy(vp8HeaderBytes, 0, rtpPacket.Payload, 0, vp8HeaderBytes.Length);
                    Buffer.BlockCopy(buffer, offset, rtpPacket.Payload, vp8HeaderBytes.Length, payloadLength);

                    var rtpBuffer = rtpPacket.GetBytes();

                    //_webRTCReceiverClient.Send(rtpBuffer, rtpBuffer.Length, _wiresharpEP);

                    int rtperr = SrtpContext.ProtectRTP(rtpBuffer, rtpBuffer.Length - SRTP_AUTH_KEY_LENGTH);
                    if (rtperr != 0)
                    {
                        logger.Warn("SRTP packet protection failed, result " + rtperr + ".");
                    }
                    else
                    {
                        var connectedIceCandidate = Peer.LocalIceCandidates.Where(y => y.RemoteRtpEndPoint != null).First();
                        connectedIceCandidate.LocalRtpSocket.SendTo(rtpBuffer, connectedIceCandidate.RemoteRtpEndPoint);
                    }
                }
            }
            catch (Exception sendExcp)
            {
                // logger.Error("SendRTP exception sending to " + client.SocketAddress + ". " + sendExcp.Message);
            }
        }
    }
}
