//-----------------------------------------------------------------------------
// Filename: WebRtcSession.cs
//
// Description: This class is the glue that combines the ICE connection establishment with the VP8 encoding and media
// transmission.
//
// History:
// 04 Mar 2016	Aaron Clauson	Created.
// 25 Aug 2019  Aaron Clauson   Updated from video only to audio and video.
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
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Net.WebRtc
{
    public class WebRtcSession
    {
        private const int VP8_PAYLOAD_TYPE_ID = 100;

        private static ILog logger = AppState.logger;

        private static ManualResetEvent _secureChannelInitMre = new ManualResetEvent(false);

        public WebRtcPeer Peer;
        public DtlsManaged DtlsContext;
        public SRTPManaged SrtpContext;
        public SRTPManaged SrtpReceiveContext;  // Used to decrypt packets received from the remote peer.
        public RTPSession _audioRtpSession;
        public RTPSession _videoRtpSession;

        public bool IsEncryptionDisabled { get; private set; }
        public MediaSourceEnum MediaSource { get; private set; }

        private string _dtlsCertFilePath;
        private string _dtlsKeyFilePath;

        public string CallID
        {
            get { return Peer.CallID; }
        }

        public WebRtcSession(string dtlsCertFilePath, string dtlsKeyFilePath, string callID, bool isEncryptionDisabled, MediaSourceEnum mediaSource)
        {
            _dtlsCertFilePath = dtlsCertFilePath;
            _dtlsKeyFilePath = dtlsKeyFilePath;
            IsEncryptionDisabled = isEncryptionDisabled;
            MediaSource = mediaSource;

            Peer = new WebRtcPeer() { CallID = callID };
        }

        public void DtlsPacketReceived(IceCandidate iceCandidate, byte[] buffer, IPEndPoint remoteEndPoint)
        {
            logger.Debug("DTLS packet received for media type " + iceCandidate.MediaType.ToString().ToUpper() + " of " + buffer.Length + " bytes from " + remoteEndPoint.ToString() + ".");

            if (!File.Exists(_dtlsCertFilePath))
            {
                throw new ApplicationException($"The DTLS certificate file could not be found at {_dtlsCertFilePath}.");
            }

            if (!File.Exists(_dtlsKeyFilePath))
            {
                throw new ApplicationException($"The DTLS key file could not be found at {_dtlsKeyFilePath}.");
            }

            if (DtlsContext == null)
            {
                lock (_secureChannelInitMre)
                {
                    DtlsContext = new DtlsManaged(_dtlsCertFilePath, _dtlsKeyFilePath);
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
                byte[] dtlsOutBytes = new byte[4096];

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

                        lock (_secureChannelInitMre)
                        {
                            SrtpContext = new SRTPManaged(DtlsContext, false);
                            SrtpReceiveContext = new SRTPManaged(DtlsContext, true);
                        }

                        Peer.IsDtlsNegotiationComplete = true;
                        iceCandidate.RemoteRtpEndPoint = remoteEndPoint;

                        _audioRtpSession = new RTPSession((int)RTPPayloadTypesEnum.PCMU, SrtpContext.ProtectRTP, SrtpContext.ProtectRTCP);
                        _videoRtpSession = new RTPSession(VP8_PAYLOAD_TYPE_ID, SrtpContext.ProtectRTP, SrtpContext.ProtectRTCP);
                    }
                }
            }
        }

        public void InitEncryptionDisabledSession(IceCandidate iceCandidate, IPEndPoint remoteEndPoint)
        {
            if (_audioRtpSession == null || _videoRtpSession == null)
            {
                logger.Debug($"Initialising non encrypted WebRtc session for remote end point {remoteEndPoint.ToString()}.");

                iceCandidate.RemoteRtpEndPoint = remoteEndPoint;

                _audioRtpSession = new RTPSession((int)RTPPayloadTypesEnum.PCMU, null, null);
                _videoRtpSession = new RTPSession(VP8_PAYLOAD_TYPE_ID, null, null);
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

        public void SendMedia(MediaSampleTypeEnum mediaType, uint sampleTimestamp, byte[] sample)
        {
            var connectedIceCandidate = Peer.LocalIceCandidates.Where(y => y.RemoteRtpEndPoint != null).FirstOrDefault();

            if (connectedIceCandidate != null)
            {
                var srcRtpEndPoint = connectedIceCandidate.LocalRtpSocket;
                var dstRtpEndPoint = connectedIceCandidate.RemoteRtpEndPoint;

                if (mediaType == MediaSampleTypeEnum.VP8)
                {
                    _videoRtpSession.SendVp8Frame(srcRtpEndPoint, dstRtpEndPoint, sampleTimestamp, sample);
                }
                else
                {
                    _audioRtpSession.SendAudioFrame(srcRtpEndPoint, dstRtpEndPoint, sampleTimestamp, sample);
                }
            }
        }

        public void SendRtcpSenderReports(uint audioTimestamp, uint videoTimestamp)
        {
            var connectedIceCandidate = Peer.LocalIceCandidates.Where(y => y.RemoteRtpEndPoint != null).FirstOrDefault();

            if (connectedIceCandidate != null)
            {
                var srcRtpEndPoint = connectedIceCandidate.LocalRtpSocket;
                var dstRtpEndPoint = connectedIceCandidate.RemoteRtpEndPoint;

                _audioRtpSession.SendRtcpSenderReport(srcRtpEndPoint, dstRtpEndPoint, audioTimestamp);
                _videoRtpSession.SendRtcpSenderReport(srcRtpEndPoint, dstRtpEndPoint, videoTimestamp);
            }
        }
    }
}
