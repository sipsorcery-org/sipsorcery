//-----------------------------------------------------------------------------
// Filename: WebRtcSession.cs
//
// Description: This class is the glue that combines the ICE connection establishment with the VP8 encoding and media
// transmission.
//
// History:
// 04 Mar 2016	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2016 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Pty Ltd 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SIPSorceryMedia;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Net.WebRtc
{
    public class WebRtcSession
    {
        private const int RTP_MAX_PAYLOAD = 1400;
        private const int VP8_PAYLOAD_TYPE_ID = 100;
        private const int PCMU_PAYLOAD_TYPE_ID = 0;
        private const int SRTP_AUTH_KEY_LENGTH = 10;

        private static ILog logger = AppState.logger;

        private static ManualResetEvent _dtlsInitMre = new ManualResetEvent(false);

        public WebRtcPeer Peer;
       

        public DtlsManaged DtlsContext;
        public SRTPManaged SrtpContext;
        public SRTPManaged SrtpReceiveContext;  // Used to decrypt packets received from the remote peer.

        private string _dtlsCertFilePath;
        private string _dtlsKeyFilePath;

        public string CallID
        {
            get { return Peer.CallID; }
        }

        public WebRtcSession(string dtlsCertFilePath, string dtlsKeyFilePath, string callID)
        {
            _dtlsCertFilePath = dtlsCertFilePath;
            _dtlsKeyFilePath = dtlsKeyFilePath;

            Peer = new WebRtcPeer() { CallID = callID };
        }

        public void DtlsPacketReceived(IceCandidate iceCandidate, byte[] buffer, IPEndPoint remoteEndPoint)
        {
            logger.Debug("DTLS packet received for media type " + iceCandidate.MediaType.ToString().ToUpper() + " of " + buffer.Length + " bytes from " + remoteEndPoint.ToString() + ".");

            if(!File.Exists(_dtlsCertFilePath))
            {
                throw new ApplicationException($"The DTLS certificate file could not be found at {_dtlsCertFilePath}.");
            }

            if (!File.Exists(_dtlsKeyFilePath))
            {
                throw new ApplicationException($"The DTLS key file could not be found at {_dtlsKeyFilePath}.");
            }

            if (DtlsContext == null)
            {
                lock (_dtlsInitMre)
                {
                    DtlsContext = new DtlsManaged(_dtlsCertFilePath, _dtlsKeyFilePath);
                    //DtlsContext = new DtlsManaged();
                    int res = DtlsContext.Init();
                    logger.Debug("DtlsContext initialisation result=" + res);
                }
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
                    logger.Debug("No bytes read from DTLS context :(.");
                }
                else
                {
                    logger.Debug(bytesRead + " bytes read from DTLS context sending to " + remoteEndPoint.ToString() + ".");
                    iceCandidate.LocalRtpSocket.SendTo(dtlsOutBytes, 0, bytesRead, SocketFlags.None, remoteEndPoint);

                    //if (client.DtlsContext.IsHandshakeComplete())
                    if (DtlsContext.GetState() == 3)
                    {
                        logger.Debug("DTLS negotiation complete for " + remoteEndPoint.ToString() + ".");
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

        // https://tools.ietf.org/html/rfc3551#section-4.5.14: 8 bit PCMU samples must be signed.
        public void SendPcmu(byte[] buffer, uint sampleTimestamp)
        {
            try
            {
                //Peer.LastTimestamp = (Peer.LastTimestamp == 0) ? RTSPSession.DateTimeToNptTimestamp32(DateTime.Now) : Peer.LastTimestamp + sampleDuarion;

                for (int index = 0; index * RTP_MAX_PAYLOAD < buffer.Length; index++)
                {
                    int offset = (index == 0) ? 0 : (index * RTP_MAX_PAYLOAD);
                    int payloadLength = (offset + RTP_MAX_PAYLOAD < buffer.Length) ? RTP_MAX_PAYLOAD : buffer.Length - offset;

                    RTPPacket rtpPacket = new RTPPacket(payloadLength + SRTP_AUTH_KEY_LENGTH);
                    rtpPacket.Header.SyncSource = Peer.AudioSSRC;
                    rtpPacket.Header.SequenceNumber = Peer.AudioSequenceNumber++;
                    rtpPacket.Header.Timestamp = sampleTimestamp; //Peer.LastTimestamp;
                    rtpPacket.Header.MarkerBit = 0;
                    rtpPacket.Header.PayloadType = PCMU_PAYLOAD_TYPE_ID;

                    Buffer.BlockCopy(buffer, offset, rtpPacket.Payload, 0, payloadLength);

                    var rtpBuffer = rtpPacket.GetBytes();

                    //Peer.LocalIceCandidates.Where(y => y.RemoteRtpEndPoint != null).First().LocalRtpSocket.SendTo(rtpBuffer, wiresharkEp);

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

        public void SendVp8(byte[] buffer, uint sampleTimestamp)
        {
            try
            {
                //Peer.LastTimestamp = (Peer.LastTimestamp == 0) ? RTSPSession.DateTimeToNptTimestamp32(DateTime.Now) : Peer.LastTimestamp + TIMESTAMP_SPACING;

                for (int index = 0; index * RTP_MAX_PAYLOAD < buffer.Length; index++)
                {
                    int offset = (index == 0) ? 0 : (index * RTP_MAX_PAYLOAD);
                    int payloadLength = (offset + RTP_MAX_PAYLOAD < buffer.Length) ? RTP_MAX_PAYLOAD : buffer.Length - offset;

                    byte[] vp8HeaderBytes = (index == 0) ? new byte[] { 0x10 } : new byte[] { 0x00 };

                    RTPPacket rtpPacket = new RTPPacket(payloadLength + SRTP_AUTH_KEY_LENGTH + vp8HeaderBytes.Length);
                    rtpPacket.Header.SyncSource = Peer.VideoSSRC;
                    rtpPacket.Header.SequenceNumber = Peer.VideoSequenceNumber++;
                    rtpPacket.Header.Timestamp = sampleTimestamp; // Peer.LastTimestamp;
                    rtpPacket.Header.MarkerBit = ((offset + payloadLength) >= buffer.Length) ? 1 : 0; // Set marker bit for the last packet in the frame.
                    rtpPacket.Header.PayloadType = VP8_PAYLOAD_TYPE_ID;

                    Buffer.BlockCopy(vp8HeaderBytes, 0, rtpPacket.Payload, 0, vp8HeaderBytes.Length);
                    Buffer.BlockCopy(buffer, offset, rtpPacket.Payload, vp8HeaderBytes.Length, payloadLength);

                    var rtpBuffer = rtpPacket.GetBytes();

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

        public void SendRtcpSenderReport(uint senderSyncSource, ulong ntpTimestamp, uint rtpTimestamp, uint senderPacketCount, uint senderOctetCount)
        {
            try
            {
                var rtcpSRPacket = new RTCPPacket(senderSyncSource, ntpTimestamp, rtpTimestamp, senderPacketCount, senderOctetCount);
                var rtcpSRBytes = rtcpSRPacket.GetBytes();

                byte[] sendBuffer = new byte[rtcpSRBytes.Length + SRTP_AUTH_KEY_LENGTH];
                //byte[] sendBuffer = new byte[rtcpHeaderBytes.Length + senderReportBuffer.Length];

                Buffer.BlockCopy(rtcpSRBytes, 0, sendBuffer, 0, rtcpSRBytes.Length);

                var connectedIceCandidate = Peer.LocalIceCandidates.Where(y => y.RemoteRtpEndPoint != null).First();

                int rtperr = SrtpContext.ProtectRTCP(sendBuffer, sendBuffer.Length - SRTP_AUTH_KEY_LENGTH);
                if (rtperr != 0)
                {
                    logger.Warn("SRTP RTCP packet protection failed, result " + rtperr + ".");
                }
                else
                {
                    connectedIceCandidate.LocalRtpSocket.SendTo(sendBuffer, connectedIceCandidate.RemoteRtpEndPoint);
                }
            }
            catch (Exception sendExcp)
            {
                // logger.Error("SendRTP exception sending to " + client.SocketAddress + ". " + sendExcp.Message);
            }
        }
    }
}
