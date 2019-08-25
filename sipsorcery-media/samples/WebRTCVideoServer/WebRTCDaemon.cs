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
// Create a self signed localhost certificate for use with the web socket server and that is accepted by Chrome
// NOTE: The localhost.pfx needs to be added to the Local Computer/Trusted Root Certification Authorities to make Chrome happy
// Also need to set these two Chorme flags (enter the strings below in the address bar):
// chrome://flags/#allow-insecure-localhost
// chrome://flags/#enable-webrtc-hide-local-ips-with-mdns
//
// openssl req -config req.conf -x509 -newkey rsa:4096 -keyout private/localhost.pem -out localhost.pem -nodes -days 3650
// openssl pkcs12 -export -in localhost.pem -inkey private/localhost.pem -out localhost.pfx -nodes
//
// cat req.conf
//[ req ]
//default_bits = 2048
//default_md = sha256
//prompt = no
//encrypt_key = no
//distinguished_name = dn
//x509_extensions = x509_ext
//string_mask = utf8only
//[dn]
//CN = localhost
//[x509_ext]
//subjectAltName = localhost, IP:127.0.0.1, IP:::1 
//keyUsage = Digital Signature, Key Encipherment, Data Encipherment
//extendedKeyUsage = TLS Web Server Authentication
//
// Get thumbrpint for certificate used for DTLS:
// openssl x509 -fingerprint -sha256 -in localhost.pem
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// To use with encryption disabled:
// "C:\Users\aaron\AppData\Local\Google\Chrome SxS\Application\chrome.exe" -disable-webrtc-encryption
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// ffmpeg command for an audio and video container that "should" work well with this sample is:
// ToDo: Determine good output codec/format parameters.
// ffmpeg -i max.mp4 -ss 00:00:06 max_even_better.mp4
// ffmpeg -i max4.mp4 -ss 00:00:06 -vf scale=320x240 max4small.mp4
//
// To receive raw RTP samples set the RawRtpBaseEndPoint so the port matches the audio port in
// SDP below and then use ffplay as below:
//
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
using SIPSorcery.Sys;
using log4net;
using NAudio.Codecs;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace SIPSorcery.Net.WebRtc
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

    public enum MediaSourceEnum
    {
        Max = 0,
        TestPattern = 1
    }

    public class SDPExchange : WebSocketBehavior
    {
        public MediaSourceEnum MediaSource { get; private set; }
        public bool IsEncryptionDisabled { get; private set; }

        public event Action<WebSocketSharp.Net.WebSockets.WebSocketContext, string, IPAddress, bool, MediaSourceEnum> WebSocketOpened;
        public event Action<string, string> SDPAnswerReceived;

        public SDPExchange(MediaSourceEnum mediaSource, bool isEncryptionDisabled)
        {
            MediaSource = mediaSource;
            IsEncryptionDisabled = isEncryptionDisabled;
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            SDPAnswerReceived(this.ID, e.Data);
        }

        protected override void OnOpen()
        {
            base.OnOpen();
            WebSocketOpened(this.Context, this.ID, this.Context.ServerEndPoint.Address, IsEncryptionDisabled, MediaSource);
        }
    }

    public class WebRTCDaemon
    {
        private static ILog logger = AppState.logger;

        private const float TEXT_SIZE_PERCENTAGE = 0.035f;       // height of text as a percentage of the total image height
        private const float TEXT_OUTLINE_REL_THICKNESS = 0.02f; // Black text outline thickness is set as a percentage of text height in pixels
        private const int TEXT_MARGIN_PIXELS = 5;
        private const int POINTS_PER_INCH = 72;

        private const int VP8_TIMESTAMP_SPACING = 3000;
        private const int VP8_PAYLOAD_TYPE_ID = 100;
        private const int RTCP_SR_PERIOD_SECONDS = 3;
        private const int RAW_RTP_START_PORT_RANGE = 48000;
        private const int RAW_RTP_END_PORT_RANGE = 48200;

        // This needs to match the cerificate used for DTLS comunications: openssl x509 -fingerprint -sha256 -in localhost.pem
        // TODO: Extract this programatically from the DTLS Certificate.
        private const string DTLS_CERTIFICATE_THUMBRPINT = "C6:ED:8C:9D:06:50:77:23:0A:4A:D8:42:68:29:D0:70:2F:BB:C7:72:EC:98:5C:62:07:1B:0C:5D:CB:CE:BE:CD";

        // Application configuration settings.
        private string _webSocketCertificatePath = AppState.GetConfigSetting("WebSocketCertificatePath");
        private string _webSocketCertificatePassword = AppState.GetConfigSetting("WebSocketCertificatePassword");
        private string _dtlsCertificatePath = AppState.GetConfigSetting("DtlsCertificatePath");
        private string _dtlsKeyPath = AppState.GetConfigSetting("DtlsKeyPath");
        private string _rawRtpBaseEndPoint = AppState.GetConfigSetting("RawRtpBaseEndPoint");
        private string _mediaFilePath = AppState.GetConfigSetting("MediaFilePath");

        private bool _exit = false;
        private DateTime _lastRtcpSenderReportSentAt = DateTime.MinValue;
        private ConcurrentDictionary<string, WebRtcSession> _webRtcSessions = new ConcurrentDictionary<string, WebRtcSession>();

        SIPSorceryMedia.VPXEncoder _vpxEncoder;
        private uint _vp8Timestamp;
        private uint _mulawTimestamp;

        private delegate void MediaSampleReadyDelegate(MediaSampleTypeEnum sampleType, uint timestamp, byte[] sample);
        private event MediaSampleReadyDelegate OnMediaSampleReady;

        public void Start()
        {
            try
            {
                Console.WriteLine("This application includes software developed by the OpenSSL Project and cryptographic software written by Eric Young (eay@cryptsoft.com).");

                logger.Debug("WebRTCDaemon starting.");

                var wssCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(_webSocketCertificatePath, _webSocketCertificatePassword);
                logger.Debug("Web Socket Server Certificate: " + wssCertificate.Subject + ", have key " + wssCertificate.HasPrivateKey + ", Expires " + wssCertificate.GetExpirationDateString() + ".");

                if (!File.Exists(_mediaFilePath))
                {
                    throw new ApplicationException($"The media file at does not exist at {_mediaFilePath}.");
                }

                // Configure the web socket and the differetn end point handlers.
                var wss = new WebSocketServer(8081, true);
                //wss.Log.Level = LogLevel.Debug;
                wss.SslConfiguration = new WebSocketSharp.Net.ServerSslConfiguration(wssCertificate, false, System.Security.Authentication.SslProtocols.Default, false);

                // Standard encrypted WebRtc stream.
                wss.AddWebSocketService<SDPExchange>("/max", () =>
                {
                    SDPExchange sdpReceiver = new SDPExchange(MediaSourceEnum.Max, false) { IgnoreExtensions = true };
                    sdpReceiver.WebSocketOpened += WebRtcStartCall;
                    sdpReceiver.SDPAnswerReceived += WebRtcAnswerReceived;
                    return sdpReceiver;
                });

                // Decrypted WebRtc stream for diagnostics (browsers do not support this without specific flags being enabled).
                wss.AddWebSocketService<SDPExchange>("/maxnocry", () =>
                {
                    SDPExchange sdpReceiver = new SDPExchange(MediaSourceEnum.Max, true) { IgnoreExtensions = true };
                    sdpReceiver.WebSocketOpened += WebRtcStartCall;
                    sdpReceiver.SDPAnswerReceived += WebRtcAnswerReceived;
                    return sdpReceiver;
                });

                wss.AddWebSocketService<SDPExchange>("/testpattern", () =>
                {
                    SDPExchange sdpReceiver = new SDPExchange(MediaSourceEnum.TestPattern, false) { IgnoreExtensions = true };
                    sdpReceiver.WebSocketOpened += WebRtcStartCall;
                    sdpReceiver.SDPAnswerReceived += WebRtcAnswerReceived;
                    return sdpReceiver;
                });

                wss.Start();

                // Initialise the Media Foundation library that will pull the samples from the mp4 file.
                SIPSorceryMedia.MFSampleGrabber mfSampleGrabber = new SIPSorceryMedia.MFSampleGrabber();
                mfSampleGrabber.OnClockStartEvent += OnClockStartEvent;
                mfSampleGrabber.OnVideoResolutionChangedEvent += OnVideoResolutionChangedEvent;
                unsafe
                {
                    mfSampleGrabber.OnProcessSampleEvent += OnProcessSampleEvent;
                }

                // Hook up event handlers to send the media samples to the network.
                InitMediaToWebRtcClients();

                // Start test pattern.
                Task.Run(SendTestPattern);

                if (!String.IsNullOrEmpty(_rawRtpBaseEndPoint))
                {
                    try
                    {
                        var rawRtpBaseEndPoint = SIPSorcery.Sys.IPSocket.GetIPEndPoint(_rawRtpBaseEndPoint);

                        if (rawRtpBaseEndPoint != null)
                        {
                            logger.Info($"Raw RTP send starting, base end point {rawRtpBaseEndPoint}.");
                            SendSamplesAsRtp(rawRtpBaseEndPoint);
                        }
                    }
                    catch (Exception rawRtpExcp)
                    {
                        logger.Warn("Exception attempting to start raw RTP. " + rawRtpExcp.Message);
                    }
                }

                // Start sampling the media file.
                mfSampleGrabber.Run(_mediaFilePath, true);
            }
            catch (Exception excp)
            {
                logger.Error("Exception WebRTCDaemon.Start. " + excp);
            }
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

        private void OnVideoResolutionChangedEvent(uint width, uint height, uint stride)
        {
            try
            {
                if (_vpxEncoder == null ||
                    (_vpxEncoder.GetWidth() != width || _vpxEncoder.GetHeight() != height || _vpxEncoder.GetStride() != stride))
                {
                    if (_vpxEncoder != null)
                    {
                        _vpxEncoder.Dispose();
                    }

                    logger.Info($"Initialising VPXEncoder with width {width}, height {height} and stride {stride}.");

                    _vpxEncoder = new VPXEncoder();
                    _vpxEncoder.InitEncoder(width, height, stride);
                }
            }
            catch (Exception excp)
            {
                logger.Warn("Exception MfSampleGrabber_OnVideoResolutionChangedEvent. " + excp.Message);
            }
        }

        unsafe private void OnProcessSampleEvent(int mediaTypeID, uint dwSampleFlags, long llSampleTime, long llSampleDuration, uint dwSampleSize, ref byte[] sampleBuffer)
        {
            try
            {
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
            catch (Exception excp)
            {
                logger.Warn("Exception MfSampleGrabber_OnProcessSampleEvent. " + excp.Message);
            }
        }

        public void OnClockStartEvent(long hnsSystemTime, long llClockStartOffset)
        {
            //Console.WriteLine($"C# OnClockStart {hnsSystemTime}, {llClockStartOffset}.");
        }

        private void WebRtcStartCall(WebSocketSharp.Net.WebSockets.WebSocketContext context, string webSocketID, IPAddress defaultIPAddress, bool isEncryptionDisabled, MediaSourceEnum mediaSource)
        {
            logger.Debug($"New WebRTC client added for web socket connection {webSocketID} and local IP address {defaultIPAddress}, encryption disabled {isEncryptionDisabled}.");

            var mediaTypes = new List<RtpMediaTypesEnum> { RtpMediaTypesEnum.Video, RtpMediaTypesEnum.Audio };

            lock (_webRtcSessions)
            {
                if (!_webRtcSessions.Any(x => x.Key == webSocketID))
                {
                    var webRtcSession = new WebRtcSession(_dtlsCertificatePath, _dtlsKeyPath, webSocketID, isEncryptionDisabled, mediaSource);

                    context.WebSocket.OnClose += (sender, e) =>
                    {
                        Console.WriteLine($"Web socket {webSocketID} closed, closing WebRtc peer.");
                        webRtcSession.Peer.Close();
                    };

                    string dtlsThumbrpint = (isEncryptionDisabled == false) ? DTLS_CERTIFICATE_THUMBRPINT : null;

                    if (_webRtcSessions.TryAdd(webSocketID, webRtcSession))
                    {
                        webRtcSession.Peer.OnSdpOfferReady += (sdp) => { logger.Debug("Offer SDP: " + sdp); context.WebSocket.Send(sdp); };
                        webRtcSession.Peer.OnMediaPacket += webRtcSession.MediaPacketReceived;
                        webRtcSession.Peer.Initialise(dtlsThumbrpint, null, mediaTypes, defaultIPAddress, isEncryptionDisabled);
                        webRtcSession.Peer.OnClose += () => { PeerClosed(webSocketID); };

                        if (isEncryptionDisabled == false)
                        {
                            webRtcSession.Peer.OnDtlsPacket += webRtcSession.DtlsPacketReceived;
                        }
                        else
                        {
                            webRtcSession.Peer.OnIceConnected += webRtcSession.InitEncryptionDisabledSession;
                        }
                    }
                    else
                    {
                        logger.Error("Failed to add new WebRTC client.");
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

        /// <summary>
        /// This method sets up the streaming of the first audio and video stream from an mp4 file to a WebRtc browser 
        /// (tested predominantly with Chrome). This method deals only with the media, the signalling and WebRtc session
        /// needs to have already been set up by the respective WebRtc classes.
        /// </summary>
        private void InitMediaToWebRtcClients()
        {
            OnMediaSampleReady += (mediaType, timestamp, sample) =>
            {
                lock (_webRtcSessions)
                {
                    foreach (var session in _webRtcSessions.Where(x => (x.Value.Peer.IsDtlsNegotiationComplete == true || x.Value.IsEncryptionDisabled == true) &&
                       x.Value.Peer.LocalIceCandidates.Any(y => y.RemoteRtpEndPoint != null) && x.Value.MediaSource == MediaSourceEnum.Max))
                    {
                        session.Value.SendMedia(mediaType, timestamp, sample);
                    }
                }

                // Deliver periodic RTCP sender reports. This helps the receiver to sync the audio and video stream timestamps.
                // If there are gaps in the media, silence supression etc. then the sender repors shouldn't be triggered from the media samples.
                // In this case the samples are from an mp4 file which provides a constant uninterrupted stream.
                if (DateTime.Now.Subtract(_lastRtcpSenderReportSentAt).TotalSeconds >= RTCP_SR_PERIOD_SECONDS)
                {
                    foreach (var session in _webRtcSessions.Where(x => (x.Value.Peer.IsDtlsNegotiationComplete == true || x.Value.IsEncryptionDisabled == true) &&
                      x.Value.Peer.LocalIceCandidates.Any(y => y.RemoteRtpEndPoint != null) && x.Value.MediaSource == MediaSourceEnum.Max))
                    {
                        session.Value.SendRtcpSenderReports(_mulawTimestamp, _vp8Timestamp);
                    }

                    _lastRtcpSenderReportSentAt = DateTime.Now;
                }
            };
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
        private void SendSamplesAsRtp(IPEndPoint dstBaseEndPoint)
        {
            try
            {
                Socket videoSrcRtpSocket = null;
                Socket videoSrcControlSocket = null;
                Socket audioSrcRtpSocket = null;
                Socket audioSrcControlSocket = null;

                // WebRtc multiplexes all the RTP and RTCP sessions onto a single UDP connection.
                // The approach needed for ffplay is the original way where each media type has it's own UDP connection and the RTCP 
                // also require a separate UDP connection on RTP port + 1.
                IPAddress localIPAddress = IPAddress.Any;
                IPEndPoint audioRtpEP = dstBaseEndPoint;
                IPEndPoint audioRtcpEP = new IPEndPoint(dstBaseEndPoint.Address, dstBaseEndPoint.Port + 1);
                IPEndPoint videoRtpEP = new IPEndPoint(dstBaseEndPoint.Address, dstBaseEndPoint.Port + 2);
                IPEndPoint videoRtcpEP = new IPEndPoint(dstBaseEndPoint.Address, dstBaseEndPoint.Port + 3);

                RTPSession audioRtpSession = new RTPSession((int)RTPPayloadTypesEnum.PCMU, null, null);
                RTPSession videoRtpSession = new RTPSession(VP8_PAYLOAD_TYPE_ID, null, null);

                DateTime lastRtcpSenderReportSentAt = DateTime.Now;

                NetServices.CreateRtpSocket(localIPAddress, RAW_RTP_START_PORT_RANGE, RAW_RTP_END_PORT_RANGE, true, out audioSrcRtpSocket, out audioSrcControlSocket);
                NetServices.CreateRtpSocket(localIPAddress, ((IPEndPoint)audioSrcRtpSocket.LocalEndPoint).Port, RAW_RTP_END_PORT_RANGE, true, out videoSrcRtpSocket, out videoSrcControlSocket);

                OnMediaSampleReady += (mediaType, timestamp, sample) =>
                {
                    if (mediaType == MediaSampleTypeEnum.VP8)
                    {
                        videoRtpSession.SendVp8Frame(videoSrcRtpSocket, videoRtpEP, timestamp, sample);
                    }
                    else
                    {
                        audioRtpSession.SendAudioFrame(audioSrcRtpSocket, audioRtpEP, timestamp, sample);
                    }

                    // Deliver periodic RTCP sender reports. This helps the receiver to sync the audio and video stream timestamps.
                    // If there are gaps in the media, silence supression etc. then the sender repors shouldn't be triggered from the media samples.
                    // In this case the samples are from an mp4 file which provides a constant uninterrupted stream.
                    if (DateTime.Now.Subtract(lastRtcpSenderReportSentAt).TotalSeconds >= RTCP_SR_PERIOD_SECONDS)
                    {
                        videoRtpSession.SendRtcpSenderReport(videoSrcControlSocket, videoRtcpEP, _vp8Timestamp);
                        audioRtpSession.SendRtcpSenderReport(audioSrcControlSocket, audioRtcpEP, _mulawTimestamp);

                        lastRtcpSenderReportSentAt = DateTime.Now;
                    }
                };
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendSamplesAsRtp. " + excp);
            }
        }

        private void SendTestPattern()
        {
            try
            {
                unsafe
                {
                    Bitmap testPattern = new Bitmap("wizard.jpeg");

                    SIPSorceryMedia.VPXEncoder vpxEncoder = new VPXEncoder();
                    vpxEncoder.InitEncoder(Convert.ToUInt32(testPattern.Width), Convert.ToUInt32(testPattern.Height), 2160);

                    SIPSorceryMedia.ImageConvert colorConverter = new ImageConvert();

                    byte[] sampleBuffer = null;
                    byte[] encodedBuffer = new byte[50000];
                    int sampleCount = 0;
                    uint rtpTimestamp = 0;

                    while (!_exit)
                    {
                        if (_webRtcSessions.Any(x => (x.Value.Peer.IsDtlsNegotiationComplete == true || x.Value.IsEncryptionDisabled == true) &&
                             x.Value.Peer.LocalIceCandidates.Any(y => y.RemoteRtpEndPoint != null && x.Value.MediaSource == MediaSourceEnum.TestPattern &&
                             x.Value.Peer.IsClosed == false)))
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
                                foreach (var session in _webRtcSessions.Where(x => (x.Value.Peer.IsDtlsNegotiationComplete == true || x.Value.IsEncryptionDisabled == true) &&
                                        x.Value.Peer.LocalIceCandidates.Any(y => y.RemoteRtpEndPoint != null) && x.Value.MediaSource == MediaSourceEnum.TestPattern))
                                {
                                    //session.Value.SendVp8(encodedBuffer, 0);
                                    session.Value.SendMedia(MediaSampleTypeEnum.VP8, rtpTimestamp, encodedBuffer);
                                }
                            }

                            encodedBuffer = null;

                            sampleCount++;
                            rtpTimestamp += VP8_TIMESTAMP_SPACING;
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
