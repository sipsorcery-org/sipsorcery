//-----------------------------------------------------------------------------
// Filename: WebRTCDaemon.cs
//
// Description: This class manages both the web socket and WebRTC connections from external peers.
//
// History:
// 04 Mar 2016	Aaron Clauson	Created.
// 11 Aug 2019	Aaron Clauson	New attempt to get to work with audio and video rather than static test pattern.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2016-2019 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com)
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

//-----------------------------------------------------------------------------
// ffmpeg command for an audio and video container that "should" work well with this sample is:
// ToDo: Determine good output codec/format parameters.
// ffmpeg -i max.mp4 -ss 00:00:06 max_even_better.mp4
// ffmpeg -i max4.mp4 -ss 00:00:06 -vf scale=320x240 max4small.mp4

// To stream samples to ffplay use SendSamplesAsRtp with: 
// ffplay -i ffplay_av.sdp -protocol_whitelist "file,rtp,udp"
//
// cat ffplay_av.sdp
//v=0
//o=- 1129870806 2 IN IP4 127.0.0.1
//s=-
//c=IN IP4 192.168.11.50
//t=0 0
//m=audio 4040 RTP/AVP 0
//a=rtpmap:0 PCMU/8000
//m=video 4042 RTP/AVP 100
//a=rtpmap:100 VP8/90000
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SIPSorceryMedia;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using log4net;
using NAudio.Codecs;
using NAudio.Wave;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace WebRTCVideoServer
{
    public enum MFReaderSampleType
    {
        ANY_STREAM = -2,
        FIRST_AUDIO_STREAM = -3,
        FIRST_VIDEO_STREAM = -4
    }

    public enum MediaSampleTypeEnum
    {
        Unknown = 0,
        Mulaw = 1,
        VP8 = 2,
    }

    public class SDPExchangeReceiver : WebSocketBehavior
    {
        public event Action<WebSocketSharp.Net.WebSockets.WebSocketContext, string> WebSocketOpened;
        public event Action<string, string> SDPAnswerReceived;

        protected override void OnMessage(MessageEventArgs e)
        {
            SDPAnswerReceived(this.ID, e.Data);
        }

        protected override void OnOpen()
        {
            base.OnOpen();
            WebSocketOpened(this.Context, this.ID);
        }
    }

    public class WebRTCDaemon
    {
        private const float TEXT_SIZE_PERCENTAGE = 0.035f;       // height of text as a percentage of the total image height
        private const float TEXT_OUTLINE_REL_THICKNESS = 0.02f; // Black text outline thickness is set as a percentage of text height in pixels
        private const int TEXT_MARGIN_PIXELS = 5;
        private const int POINTS_PER_INCH = 72;
        private const string LOCAL_IP_ADDRESS = "192.168.11.50";

        private const string MEDIA_FILE = "max_intro.mp4";
        //private const string MEDIA_FILE = "max4.1.mp4";
        //private const string MEDIA_FILE = "max4.mp4";
        //private const string MEDIA_FILE = @"c:\tools\ffmpeg\max4small.mp4";
        private const int CACHE_SAMPLE_SIZE = 1000;
        private const int VP8_TIMESTAMP_SPACING = 3000;
        private const int RTP_MAX_PAYLOAD = 1400;
        private const int VP8_PAYLOAD_TYPE_ID = 100;
        private const int RTCP_SR_PERIOD_SECONDS = 3;
        private const int VIDEO_FRAMES_EXPECTED_PER_SECOND = 30;
        private const int PER_FRAME_DELAY_MINIMUM_THRESHOLD = 5;
        private const int AUDIO_SAMPLE_PERIOD_MILLISECONDS = 8;

        private const int SEND_RTP_AUDIO_DEST_PORT = 4040;
        private const int SEND_RTP_VIDEO_DEST_PORT = 4042;

        private const string DTLS_CERTIFICATE_THUMBRPINT = "25:5A:A9:32:1F:35:04:8D:5F:8A:5B:27:0B:9F:A2:90:1A:0E:B9:E9:02:A2:24:95:64:E5:7C:4C:10:11:F7:36";

        private static ILog logger = AppState.logger;

        private string _webSocketCertificatePath = AppState.GetConfigSetting("WebSocketCertificatePath");
        private string _webSocketCertificatePassword = AppState.GetConfigSetting("WebSocketCertificatePassword");

        private bool _exit = false;
        private WebSocketServer _receiverWSS;
        private ConcurrentDictionary<string, WebRtcSession> _webRtcSessions = new ConcurrentDictionary<string, WebRtcSession>();
        private ConcurrentDictionary<string, WebRtcSessionUnencrypted> _webRtcSessionsUnencrypted = new ConcurrentDictionary<string, WebRtcSessionUnencrypted>();

        SIPSorceryMedia.VPXEncoder _vpxEncoder;
        private uint _vp8Timestamp;
        private uint _mulawTimestamp;

        private delegate void MediaSampleReadyDelegate(MediaSampleTypeEnum sampleType, uint timestamp, byte[] sample);

        private event MediaSampleReadyDelegate OnMediaSampleReady;

        public void Start()
        {
            try
            {
                logger.Debug("WebRTCDaemon starting.");

                //var wssCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(_webSocketCertificatePath, _webSocketCertificatePassword);
                //logger.Debug("Web Socket Server Certificate CN: " + wssCertificate.Subject + ", have key " + wssCertificate.HasPrivateKey + ", Expires " + wssCertificate.GetExpirationDateString() + ".");

                _receiverWSS = new WebSocketServer("ws://192.168.11.50:8081");
                //_receiverWSS.Certificate = wssCertificate;
                //_receiverWSS.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12;
                //_receiverWSS.Start(socket =>
                //{
                //    socket.OnOpen = () => Console.WriteLine("Open!");
                //    socket.OnClose = () => Console.WriteLine("Close!");
                //    socket.OnMessage = message => socket.Send(message);
                //});

                //_receiverWSS = new WebSocketServer(8081, true);
                _receiverWSS.Log.Level = LogLevel.Debug;
                //_receiverWSS.SslConfiguration = new WebSocketSharp.Net.ServerSslConfiguration(wssCertificate, false,
                //     System.Security.Authentication.SslProtocols.Tls,
                //    false);

                // Standard encrypted WebRtc stream.
                SDPExchangeReceiver sdpReceiver = new SDPExchangeReceiver { IgnoreExtensions = true };
                sdpReceiver.WebSocketOpened += WebRtcStartCall;
                sdpReceiver.SDPAnswerReceived += WebRtcAnswerReceived;

                _receiverWSS.AddWebSocketService<SDPExchangeReceiver>("/stream", () => sdpReceiver);
                _receiverWSS.Start();

                // Decrypted WebRtc stream for diagnostics (browsers do not support this without specific flags being enabled).
                SDPExchangeReceiver noEncryptionSdpReceiver = new SDPExchangeReceiver { IgnoreExtensions = true };
                noEncryptionSdpReceiver.WebSocketOpened += WebRtcStartCallUnencrypted;
                noEncryptionSdpReceiver.SDPAnswerReceived += WebRtcAnswerReceivedUnencrypted;

                _receiverWSS.AddWebSocketService<SDPExchangeReceiver>("/nocrypt", () => noEncryptionSdpReceiver);
                _receiverWSS.Start();

                SIPSorceryMedia.MFSampleGrabber mfSampleGrabber = new SIPSorceryMedia.MFSampleGrabber();
                mfSampleGrabber.OnClockStartEvent += MfSampleGrabber_OnClockStartEvent;
                mfSampleGrabber.OnVideoResolutionChangedEvent += MfSampleGrabber_OnVideoResolutionChangedEvent;
                unsafe
                {
                    mfSampleGrabber.OnProcessSampleEvent += MfSampleGrabber_OnProcessSampleEvent;
                }

                SendSamplesAsRtp();
                SendToWebRtcClients();

                mfSampleGrabber.Run(MEDIA_FILE, true);

                // Streaming methods
                //Task.Run(StreamMp4);              // Send to WebRtc enabled browser.
                //Task.Run(StreamMp4Unecrypted);    // Send to Chrome Canary with webrtc encryption disabled.
                //Task.Run(SendSamplesAsRtp);       // Send to ffplay.
                //Task.Run(SendTestPattern);      // Streams a static test pattern image overlayed with text of teh current time.
            }
            catch (Exception excp)
            {
                logger.Error("Exception WebRTCDaemon.Start. " + excp);
            }
        }

        private void MfSampleGrabber_OnVideoResolutionChangedEvent(uint width, uint height, uint stride)
        {
            //if (_vpxEncoder == null ||
            //    (_vpxEncoder.GetWidth() != width || _vpxEncoder.GetHeight() != height || _vpxEncoder.GetStride() != stride))
            //{
                if (_vpxEncoder != null)
                {
                    _vpxEncoder.Dispose();
                }

                logger.Info($"Initialising VPXEncoder with width {width}, height {height} and stride {stride}.");

                _vpxEncoder = new VPXEncoder();
                _vpxEncoder.InitEncoder(width, height, stride);
            //}
        }

        unsafe private void MfSampleGrabber_OnProcessSampleEvent(int mediaTypeID, uint dwSampleFlags, long llSampleTime, long llSampleDuration, uint dwSampleSize, ref byte[] sampleBuffer)
        {
            //Console.WriteLine($"C# OnProcessSample {dwSampleSize}.");

            if (mediaTypeID == 0)
            {
                if (_vpxEncoder == null)
                {
                    logger.Warn("Video sample cannot be processed as the VPX encoder has not yet received the frame size.");
                }
                else
                {
                    byte[] vpxEncodedBuffer = null;

                    unsafe
                    {
                        fixed (byte* p = sampleBuffer)
                        {
                            int encodeResult = _vpxEncoder.Encode(p, (int)dwSampleSize, 1, ref vpxEncodedBuffer);

                            if (encodeResult != 0)
                            {
                                logger.Warn("VPX encode of video sample failed.");
                            }
                        }
                    }

                    OnMediaSampleReady?.Invoke(MediaSampleTypeEnum.VP8, _vp8Timestamp, vpxEncodedBuffer);
                    //SendVp8(videoSrcRtpSocket, videoRtpSocket, videoTimestamp, videoSsrc, ref videoSeqNum, vpxEncodedBuffer);

                    //Console.WriteLine($"Video SeqNum {videoSeqNum}, timestamp {videoTimestamp}, buffer length {vpxEncodedBuffer.Length}, frame count {sampleProps.FrameCount}.");

                    _vp8Timestamp += VP8_TIMESTAMP_SPACING;
                }
            }
            else
            {
                uint sampleDuration = (uint)(sampleBuffer.Length / 2);

                byte[] mulawSample = new byte[sampleDuration];
                int sampleIndex = 0;

                // ToDo: Find a way to wire up the Media foundation WAVE_FORMAT_MULAW codec so the encoding below is not necessary.
                for (int index = 0; index < sampleBuffer.Length; index += 2)
                {
                    var ulawByte = MuLawEncoder.LinearToMuLawSample(BitConverter.ToInt16(sampleBuffer, index));
                    mulawSample[sampleIndex++] = ulawByte;
                }

                OnMediaSampleReady?.Invoke(MediaSampleTypeEnum.Mulaw, _mulawTimestamp, mulawSample);

                //Console.WriteLine($"Audio SeqNum {audioSeqNum}, timestamp {audioTimestamp}, buffer length {mulawSample.Length}.");

                _mulawTimestamp += sampleDuration;
            }
        }

        public void MfSampleGrabber_OnClockStartEvent(long hnsSystemTime, long llClockStartOffset)
        {
            //Console.WriteLine($"C# OnClockStart {hnsSystemTime}, {llClockStartOffset}.");
        }

        public void Stop()
        {
            try
            {
                logger.Debug("Stopping WebRTCDaemon.");

                _exit = true;

                foreach (var session in _webRtcSessions.Values)
                {
                    session.Peer.Close();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception WebRTCDaemon.Stop. " + excp);
            }
        }

        private void WebRtcStartCall(WebSocketSharp.Net.WebSockets.WebSocketContext context, string webSocketID)
        {
            logger.Debug("New WebRTC client added for web socket connection " + webSocketID + ".");

            var mediaTypes = new List<RtpMediaTypesEnum> { RtpMediaTypesEnum.Video, RtpMediaTypesEnum.Audio };

            lock (_webRtcSessions)
            {
                if (!_webRtcSessions.Any(x => x.Key == webSocketID))
                {
                    var webRtcSession = new WebRtcSession(webSocketID);

                    if (_webRtcSessions.TryAdd(webSocketID, webRtcSession))
                    {
                        webRtcSession.Peer.OnSdpOfferReady += (sdp) => { logger.Debug("Offer SDP: " + sdp); context.WebSocket.Send(sdp); };
                        webRtcSession.Peer.OnDtlsPacket += webRtcSession.DtlsPacketReceived;
                        webRtcSession.Peer.OnMediaPacket += webRtcSession.MediaPacketReceived;
                        webRtcSession.Peer.Initialise(DTLS_CERTIFICATE_THUMBRPINT, null, mediaTypes, IPAddress.Parse(LOCAL_IP_ADDRESS));
                        webRtcSession.Peer.OnClose += () => { PeerClosed(webSocketID); };
                    }
                    else
                    {
                        logger.Error("Failed to add new WebRTC client to sessions dictionary.");
                    }
                }
            }
        }

        private void WebRtcStartCallUnencrypted(WebSocketSharp.Net.WebSockets.WebSocketContext context, string webSocketID)
        {
            logger.Debug("New WebRTC client added for web socket connection " + webSocketID + ".");

            var mediaTypes = new List<RtpMediaTypesEnum> { RtpMediaTypesEnum.Video, RtpMediaTypesEnum.Audio };

            lock (_webRtcSessions)
            {
                if (!_webRtcSessions.Any(x => x.Key == webSocketID))
                {
                    var webRtcSessionUnencrypted = new WebRtcSessionUnencrypted(webSocketID);

                    if (_webRtcSessionsUnencrypted.TryAdd(webSocketID, webRtcSessionUnencrypted))
                    {
                        webRtcSessionUnencrypted.Peer.OnSdpOfferReady += (sdp) => { logger.Debug("Offer SDP: " + sdp); context.WebSocket.Send(sdp); };
                        webRtcSessionUnencrypted.Peer.OnMediaPacket += webRtcSessionUnencrypted.MediaPacketReceived;
                        webRtcSessionUnencrypted.Peer.Initialise(null, mediaTypes, IPAddress.Parse(LOCAL_IP_ADDRESS));
                        webRtcSessionUnencrypted.Peer.OnClose += () => { PeerClosed(webSocketID); };
                    }
                    else
                    {
                        logger.Error("Failed to add new WebRTC client to sessions dictionary.");
                    }
                }
            }
        }

        private void PeerClosed(string callID)
        {
            try
            {
                logger.Debug("WebRTC session for closed for call ID " + callID + ".");

                WebRtcSession closedSession = null;

                if (!_webRtcSessions.TryRemove(callID, out closedSession))
                {
                    logger.Error("Failed to remove closed WebRTC session from dictionary.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception WebRTCDaemon.PeerClosed. " + excp);
            }
        }

        private void WebRtcAnswerReceived(string webSocketID, string sdpAnswer)
        {
            try
            {
                logger.Debug("Answer SDP: " + sdpAnswer);

                var answerSDP = SDP.ParseSDPDescription(sdpAnswer);

                var peer = _webRtcSessions.Where(x => x.Key == webSocketID).Select(x => x.Value.Peer).SingleOrDefault();

                if (peer == null)
                {
                    logger.Warn("No WebRTC client entry exists for web socket ID " + webSocketID + ", ignoring.");
                }
                else
                {
                    logger.Debug("New WebRTC client SDP answer for web socket ID " + webSocketID + ".");

                    peer.SdpSessionID = answerSDP.SessionId;
                    peer.RemoteIceUser = answerSDP.IceUfrag;
                    peer.RemoteIcePassword = answerSDP.IcePwd;

                    foreach (var iceCandidate in answerSDP.IceCandidates)
                    {
                        peer.AppendRemoteIceCandidate(iceCandidate);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SDPExchangeReceiver_SDPAnswerReceived. " + excp.Message);
            }
        }

        private void WebRtcAnswerReceivedUnencrypted(string webSocketID, string sdpAnswer)
        {
            try
            {
                logger.Debug("Answer SDP: " + sdpAnswer);

                var answerSDP = SDP.ParseSDPDescription(sdpAnswer);

                var peer = _webRtcSessionsUnencrypted.Where(x => x.Key == webSocketID).Select(x => x.Value.Peer).SingleOrDefault();

                if (peer == null)
                {
                    logger.Warn("No WebRTC client entry exists for web socket ID " + webSocketID + ", ignoring.");
                }
                else
                {
                    logger.Debug("New WebRTC client SDP answer for web socket ID " + webSocketID + ".");

                    peer.SdpSessionID = answerSDP.SessionId;
                    peer.RemoteIceUser = answerSDP.IceUfrag;
                    peer.RemoteIcePassword = answerSDP.IcePwd;

                    foreach (var iceCandidate in answerSDP.IceCandidates)
                    {
                        peer.AppendRemoteIceCandidate(iceCandidate);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SDPExchangeReceiver_SDPAnswerReceived. " + excp.Message);
            }
        }

        /// <summary>
        /// This is the most important of the "Send" and "Stream" methods. It streams the first audio and video stream from
        /// an mp4 file to a WebRtc browser (tested predominantly with Chrome). This method deals only with the media, the 
        /// signalling and WebRtc session needs to have already been set up by the respective WebRtc classes.
        /// </summary>
        private void SendToWebRtcClients()
        {
            OnMediaSampleReady += (mediaType, timestamp, sample) =>
            {
                if (mediaType == MediaSampleTypeEnum.VP8)
                {
                    lock (_webRtcSessions)
                    {
                        foreach (var session in _webRtcSessions.Where(x => x.Value.Peer.IsDtlsNegotiationComplete == true &&
                           x.Value.Peer.LocalIceCandidates.Any(y => y.RemoteRtpEndPoint != null)))
                        {
                            session.Value.SendVp8(sample, _vp8Timestamp);
                        }

                        lock (_webRtcSessionsUnencrypted)
                        {
                            foreach (var session in _webRtcSessionsUnencrypted.Where(x => x.Value.Peer.LocalIceCandidates.Any(y => y.RemoteRtpEndPoint != null)))
                            {
                                session.Value.SendVp8Unencrypted(sample, _vp8Timestamp);
                            }
                        }
                    }
                }
                else
                {
                    lock (_webRtcSessions)
                    {
                        foreach (var session in _webRtcSessions.Where(x => x.Value.Peer.IsDtlsNegotiationComplete == true &&
                            x.Value.Peer.LocalIceCandidates.Any(y => y.RemoteRtpEndPoint != null)))
                        {
                            session.Value.SendPcmu(sample, _mulawTimestamp);
                        }
                    }

                    lock (_webRtcSessionsUnencrypted)
                    {
                        foreach (var session in _webRtcSessionsUnencrypted.Where(x => x.Value.Peer.LocalIceCandidates.Any(y => y.RemoteRtpEndPoint != null)))
                        {
                            session.Value.SendPcmuUnencrypted(sample, _mulawTimestamp);
                        }
                    }
                }
            };

            //lock (_webRtcSessions)
            //{
            //    foreach (var session in _webRtcSessions.Where(x => x.Value.Peer.IsDtlsNegotiationComplete == true &&
            //        x.Value.Peer.LocalIceCandidates.Any(y => y.RemoteRtpEndPoint != null)))
            //    {
            //        var webRtcSession = session.Value;

            //        webRtcSession.SendRtcpSenderReport(webRtcSession.Peer.AudioSSRC, ntp, audioTimestamp, audioSampleCount, audioOctetsCount);
            //        webRtcSession.SendRtcpSenderReport(webRtcSession.Peer.VideoSSRC, ntp, videoTimestamp, videoSampleCount, videoOctetsCount);

            //        //if (videoSamplesSinceLastRtcp > VIDEO_FRAMES_EXPECTED_PER_SECOND * RTCP_SR_PERIOD_SECONDS)
            //        //{
            //        //    // ToDo: Determine if the Media Foundation Presentation clock can be AND makes sense to be used instead of this hack.
            //        //    // The stream is running too fast. Use this crude mechanism to insert a delay.
            //        //    int extraFramesPerPeriod = (int)(videoSamplesSinceLastRtcp - VIDEO_FRAMES_EXPECTED_PER_SECOND * RTCP_SR_PERIOD_SECONDS);
            //        //    int streamAheadMilliseconds = 1000 / VIDEO_FRAMES_EXPECTED_PER_SECOND * extraFramesPerPeriod;

            //        //    perFrameDelayMilliseconds = (int)(streamAheadMilliseconds / (videoSamplesSinceLastRtcp + audioSamplesSinceLastRtcp));

            //        //    Console.WriteLine($"Adjusting per frame delay to {perFrameDelayMilliseconds}ms.");
            //        //}

            //        videoSamplesSinceLastRtcp = 0;
            //        audioSamplesSinceLastRtcp = 0;
            //    }
            //}
        }

        /// <summary>
        /// Sends two separate RTP streams to an application like ffplay. 
        /// 
        /// ffplay -i ffplay_av.sdp -protocol_whitelist "file,rtp,udp" -loglevel debug
        /// 
        /// The SDP that describes the streams is:
        /// 
        /// v=0
        /// o=- 1129870806 2 IN IP4 127.0.0.1
        /// s=-
        /// c=IN IP4 192.168.11.50
        /// t=0 0
        /// m=audio 4040 RTP/AVP 0
        /// a=rtpmap:0 PCMU/8000
        /// m=video 4042 RTP/AVP 100
        /// a=rtpmap:100 VP8/90000
        /// </summary>
        private void SendSamplesAsRtp()
        {
            try
            {
                Socket videoSrcRtpSocket = null;
                Socket videoSrcControlSocket = null;
                Socket audioSrcRtpSocket = null;
                Socket audioSrcControlSocket = null; 

                IPAddress localAddress = IPAddress.Parse(LOCAL_IP_ADDRESS);
                IPEndPoint audioRtpSocket = new IPEndPoint(localAddress, SEND_RTP_AUDIO_DEST_PORT);
                IPEndPoint videoRtpSocket = new IPEndPoint(localAddress, SEND_RTP_VIDEO_DEST_PORT);
                uint videoSsrc = Convert.ToUInt32(Crypto.GetRandomInt(6));
                uint audioSsrc = Convert.ToUInt32(Crypto.GetRandomInt(6));
                ushort videoSeqNum = 0;
                ushort audioSeqNum = 0;

                NetServices.CreateRtpSocket(localAddress, 5000, 5010, false, out audioSrcRtpSocket, out audioSrcControlSocket);
                NetServices.CreateRtpSocket(localAddress, 5011, 5020, false, out videoSrcRtpSocket, out videoSrcControlSocket);

                OnMediaSampleReady += (mediaType, timestamp, sample) =>
                {
                    if (mediaType == MediaSampleTypeEnum.VP8)
                    {
                        SendVp8(videoSrcRtpSocket, videoRtpSocket, _vp8Timestamp, videoSsrc, ref videoSeqNum, sample);
                    }
                    else
                    {
                        SendPcmu(audioSrcRtpSocket, audioRtpSocket, _mulawTimestamp, audioSsrc, audioSeqNum++, sample);
                    }
                };
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendSamplesAsRtp. " + excp);
            }
        }      

        /// <summary>
        /// Packages and sends a single audio PCMU packet over RTP.
        /// </summary>
        public void SendPcmu(Socket srcRtpSocket, IPEndPoint wiresharkEp, uint timestamp, uint ssrc, ushort seqNum, byte[] buffer)
        {
            try
            {
                int payloadLength = buffer.Length;

                RTPPacket rtpPacket = new RTPPacket(payloadLength);
                rtpPacket.Header.SyncSource = ssrc;
                rtpPacket.Header.SequenceNumber = seqNum;
                rtpPacket.Header.Timestamp = timestamp;
                rtpPacket.Header.MarkerBit = 0;
                rtpPacket.Header.PayloadType = 0; // PCMU_PAYLOAD_TYPE_ID;

                Buffer.BlockCopy(buffer, 0, rtpPacket.Payload, 0, payloadLength);

                var rtpBuffer = rtpPacket.GetBytes();

                srcRtpSocket.SendTo(rtpBuffer, wiresharkEp);
            }
            catch (Exception sendExcp)
            {
                // logger.Error("SendRTP exception sending to " + client.SocketAddress + ". " + sendExcp.Message);
            }
        }

        /// <summary>
        /// Packages and sends a single video VP8 frame over RTP.
        /// </summary>
        public void SendVp8(Socket srcRtpSocket, IPEndPoint destRtpSocket, uint timestamp, uint ssrc, ref ushort seqNum, byte[] buffer)
        {
            try
            {
                for (int index = 0; index * RTP_MAX_PAYLOAD < buffer.Length; index++)
                {
                    int offset = (index == 0) ? 0 : (index * RTP_MAX_PAYLOAD);
                    int payloadLength = (offset + RTP_MAX_PAYLOAD < buffer.Length) ? RTP_MAX_PAYLOAD : buffer.Length - offset;

                    byte[] vp8HeaderBytes = (index == 0) ? new byte[] { 0x10 } : new byte[] { 0x00 };

                    RTPPacket rtpPacket = new RTPPacket(payloadLength + vp8HeaderBytes.Length);
                    rtpPacket.Header.SyncSource = ssrc;
                    rtpPacket.Header.SequenceNumber = seqNum++;
                    rtpPacket.Header.Timestamp = timestamp;
                    rtpPacket.Header.MarkerBit = ((offset + payloadLength) >= buffer.Length) ? 1 : 0; // Set marker bit for the last packet in the frame.
                    rtpPacket.Header.PayloadType = VP8_PAYLOAD_TYPE_ID;

                    Buffer.BlockCopy(vp8HeaderBytes, 0, rtpPacket.Payload, 0, vp8HeaderBytes.Length);
                    Buffer.BlockCopy(buffer, offset, rtpPacket.Payload, vp8HeaderBytes.Length, payloadLength);

                    var rtpBuffer = rtpPacket.GetBytes();

                    srcRtpSocket.SendTo(rtpBuffer, destRtpSocket);
                }
            }
            catch (Exception sendExcp)
            {
                // logger.Error("SendRTP exception sending to " + client.SocketAddress + ". " + sendExcp.Message);
            }
        }

        private void SendTestPattern()
        {
            try
            {
                unsafe
                {
                    Bitmap testPattern = new Bitmap("wizard.jpeg");
                    //Bitmap testPattern = new Bitmap(@"..\..\max\max257.jpg");

                    SIPSorceryMedia.VPXEncoder vpxEncoder = new VPXEncoder();
                    vpxEncoder.InitEncoder(Convert.ToUInt32(testPattern.Width), Convert.ToUInt32(testPattern.Height), 2160);

                    SIPSorceryMedia.ImageConvert colorConverter = new ImageConvert();

                    byte[] sampleBuffer = null;
                    byte[] encodedBuffer = new byte[5000000];
                    int sampleCount = 0;

                    while (!_exit && sampleCount < 10)
                    {
                        if (_webRtcSessions.Any(x => x.Value.Peer.IsDtlsNegotiationComplete == true && x.Value.Peer.IsClosed == false))
                        {
                            var stampedTestPattern = testPattern.Clone() as System.Drawing.Image;
                            AddTimeStampAndLocation(stampedTestPattern, DateTime.UtcNow.ToString("dd MMM yyyy HH:mm:ss:fff"), "Test Pattern");
                            sampleBuffer = BitmapToRGB24(stampedTestPattern as System.Drawing.Bitmap);

                            fixed (byte* p = sampleBuffer)
                            {
                                byte[] convertedFrame = null;
                                //colorConverter.ConvertRGBtoYUV(p, VideoSubTypesEnum.RGB24, testPattern.Width, testPattern.Height, VideoSubTypesEnum.I420, ref convertedFrame);
                                colorConverter.ConvertRGBtoYUV(p, VideoSubTypesEnum.BGR24, testPattern.Width, testPattern.Height, testPattern.Width * 3, VideoSubTypesEnum.I420, ref convertedFrame);

                                fixed (byte* q = convertedFrame)
                                {
                                    int encodeResult = vpxEncoder.Encode(q, convertedFrame.Length, 1, ref encodedBuffer);

                                    if (encodeResult != 0)
                                    {
                                        logger.Warn("VPX encode of video sample failed.");
                                        continue;
                                    }
                                }
                            }

                            stampedTestPattern.Dispose();
                            stampedTestPattern = null;

                            lock (_webRtcSessions)
                            {
                                foreach (var session in _webRtcSessions.Where(x => x.Value.Peer.IsDtlsNegotiationComplete == true && x.Value.Peer.LocalIceCandidates.Any(y => y.RemoteRtpEndPoint != null)))
                                {
                                    session.Value.SendVp8(encodedBuffer, 0);
                                }
                            }

                            encodedBuffer = null;

                            sampleCount++;
                        }

                        Thread.Sleep(30);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendTestPattern. " + excp);
            }
        }

        private static byte[] BitmapToRGB24(Bitmap bitmap)
        {
            try
            {
                BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                var length = bitmapData.Stride * bitmapData.Height;

                byte[] bytes = new byte[length];

                // Copy bitmap to byte[]
                Marshal.Copy(bitmapData.Scan0, bytes, 0, length);
                bitmap.UnlockBits(bitmapData);

                return bytes;
            }
            catch (Exception)
            {
                return new byte[0];
            }
        }

        private static void AddTimeStampAndLocation(System.Drawing.Image image, string timeStamp, string locationText)
        {
            int pixelHeight = (int)(image.Height * TEXT_SIZE_PERCENTAGE);

            Graphics g = Graphics.FromImage(image);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (StringFormat format = new StringFormat())
            {
                format.LineAlignment = StringAlignment.Center;
                format.Alignment = StringAlignment.Center;

                using (Font f = new Font("Tahoma", pixelHeight, GraphicsUnit.Pixel))
                {
                    using (var gPath = new GraphicsPath())
                    {
                        float emSize = g.DpiY * f.Size / POINTS_PER_INCH;
                        if (locationText != null)
                        {
                            gPath.AddString(locationText, f.FontFamily, (int)FontStyle.Bold, emSize, new Rectangle(0, TEXT_MARGIN_PIXELS, image.Width, pixelHeight), format);
                        }

                        gPath.AddString(timeStamp /* + " -- " + fps.ToString("0.00") + " fps" */, f.FontFamily, (int)FontStyle.Bold, emSize, new Rectangle(0, image.Height - (pixelHeight + TEXT_MARGIN_PIXELS), image.Width, pixelHeight), format);
                        g.FillPath(Brushes.White, gPath);
                        g.DrawPath(new Pen(Brushes.Black, pixelHeight * TEXT_OUTLINE_REL_THICKNESS), gPath);
                    }
                }
            }
        }
    }
}
