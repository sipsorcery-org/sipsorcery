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
// NOTE: See section below about various flags that may need to be set for different browsers including trusting self 
// signed certificate importing.
//
// openssl req -config req.conf -x509 -newkey rsa:4096 -keyout private/localhost.pem -out localhost.pem -nodes -days 3650
// openssl pkcs12 -export -in localhost.pem -inkey private/localhost.pem -out localhost.pfx -nodes
//
// openssl req -config req.conf -x509 -newkey rsa:2048 -keyout winsvr19-test-key.pem -out winsvr19-test.pem -nodes -days 3650
// openssl pkcs12 -export -in winsvr19-test.pem -inkey winsvr19-test-key.pem -out winsvr19-test.pfx -nodes
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
//
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

//-----------------------------------------------------------------------------
// Browser flags for webrtc testing and certificate management:
//
// Chrome: prevent hostnames using the <addr>.local format being set in ICE candidates (TODO: handle the .local hostnames)
// chrome://flags/#enable-webrtc-hide-local-ips-with-mdns
//
// Chrome: Allow a non-trusted localhost certificate for the web socket connection (this didn't seem to work)
// chrome://flags/#allow-insecure-localhost
//
// Chrome: To trust a self signed certificate:
// Add certificate to the appropriate Windows store to be trusted for web socket connection:
// Note that the steps below correspond to importing into the Windows Current User\Personal\Certificates store.
// This store can be managed directly by typing "certmgr" in the Windows search bar.
// 1. chrome://settings/?search=cert,
// 2. Click the manage certificated popup icon (next to "Manage certificates"),
// 3. Browse to the localhost.pem file and import.
//
// Chrome Canary: allow WebRtc with DTLS encryption disabled (so RTP packets can be captured and checked):
// "C:\Users\aaron\AppData\Local\Google\Chrome SxS\Application\chrome.exe" -disable-webrtc-encryption
//
// Firefox: To trust a self signed certificate:
// 1. about:preferences#privacy
// 2. Scroll down to certificates and click "View Certificates" to bring up the Certificate Manager,
// 3. Click Servers->Add Exception and in the Location type https://localhost:8081 or the address of the web socket server,
// 4. Click Get Certificate, verify the certificate using View and if happy then check the "Permanently store this exception" and
//    click the "Confirm Security Exception" button.
// 
// Firefox: to allow secure web socket (wss) connection to localhost, enter about:config in address bar.
// Search for network.stricttransportsecurity.preloadlist and set to false. (TODO: this is insecure keep looking for a better way)
// Open https://localhost:8081/ and accept risk which seems to add an exception
//
// Edge: NOTE as of 8 Sep 2019 Edge is not working with this program due to the OpenSSL/DTLS issue below.
//
// Edge: allow web socket connections with localhost (**see note above aout Edge not working with openssl)
// C:\WINDOWS\system32>CheckNetIsolation LoopbackExempt -a -n=Microsoft.MicrosoftEdge_8wekyb3d8bbwe
//
// Edge:
// Does not support OpenSSL's RSA (2048 or 4096) bit certificates for DTLS which is required for the WebRTC connection,
// https://developer.microsoft.com/en-us/microsoft-edge/platform/issues/14561214/
//
// Edge: To trust a self signed certificate:
// The only approach found was to add the certificate to the Current User\Trusted Root Certificate Authorities.
// This is not ideal and is incorrect because it's a self signed certificate not a certificate authority.
// This certificate store can be access by Windows Search bar->certmgr select "Trusted Root Certificate Authority".
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// To install as a Windows Service:
// c:\Apps\WebRtcDaemon> WebRTCDaemon.exe -i
// or
// Note: leave the space after "binpath=" and "start="
// c:\Apps\WebRtcDaemon>sc create "SIPSorcery WebRTC Daemon" binpath= "C:\Apps\WebRTCDaemon\WebRTCDaemon.exe" start= auto
//
// To uninstall Windows Service:
// c:\Apps\WebRtcDaemon> WebRTCDaemon.exe -u
// or
// c:\Apps\WebRtcDaemon>sc delete "SIPSorcery WebRTC Daemon" 
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
        public IPAddress RtpIPAddress { get; private set; }

        public event Action<WebSocketSharp.Net.WebSockets.WebSocketContext, string, IPAddress, bool, MediaSourceEnum> WebSocketOpened;
        public event Action<string, string> SDPAnswerReceived;

        public SDPExchange(MediaSourceEnum mediaSource, bool isEncryptionDisabled, IPAddress rtpIPAddress)
        {
            MediaSource = mediaSource;
            IsEncryptionDisabled = isEncryptionDisabled;
            RtpIPAddress = rtpIPAddress;
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            SDPAnswerReceived(this.ID, e.Data);
        }

        protected override void OnOpen()
        {
            base.OnOpen();
            IPAddress rtpIpAddress = RtpIPAddress ?? this.Context.ServerEndPoint.Address;
            WebSocketOpened(this.Context, this.ID, rtpIpAddress, IsEncryptionDisabled, MediaSource);
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
        private const int DEFAULT_WEB_SOCKET_PORT = 8081;

        // Application configuration settings.
        private string _webSocketCertificatePath = AppState.GetConfigSetting("WebSocketCertificatePath");
        private string _webSocketCertificatePassword = AppState.GetConfigSetting("WebSocketCertificatePassword");
        private string _dtlsCertificatePath = AppState.GetConfigSetting("DtlsCertificatePath");
        private string _dtlsKeyPath = AppState.GetConfigSetting("DtlsKeyPath");
        private string _dtlsCertificateThumbprint = AppState.GetConfigSetting("DtlsCertificateThumbprint"); // TODO: Extract this programatically from the DTLS Certificate.
        private string _rawRtpBaseEndPoint = AppState.GetConfigSetting("RawRtpBaseEndPoint");
        private string _mediaFilePath = AppState.GetConfigSetting("MediaFilePath");
        private string _testPattermImagePath = AppState.GetConfigSetting("TestPatternFilePath");
        private string _localRtpIPAddress = AppState.GetConfigSetting("LocalRtpIPAddress");
        private string _webSocketPort = AppState.GetConfigSetting("WebSocketPort");

        private bool _exit = false;
        private WebSocketServer _webSocketServer;
        private SIPSorceryMedia.MFSampleGrabber _mfSampleGrabber;
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
                logger.Debug($"DTLS certificate thumbprint {_dtlsCertificateThumbprint}.");
                logger.Debug($"Web socket port {_webSocketPort}.");

                if (!File.Exists(_mediaFilePath))
                {
                    throw new ApplicationException($"The media file at does not exist at {_mediaFilePath}.");
                }

                IPAddress rtpIPAddress = null;
                if (String.IsNullOrEmpty(_localRtpIPAddress) == false)
                {
                    rtpIPAddress = IPAddress.Parse(_localRtpIPAddress);
                }

                // Configure the web socket and the different end point handlers.
                int webSocketPort = (!String.IsNullOrEmpty(_webSocketPort)) ? Int32.Parse(_webSocketPort) : DEFAULT_WEB_SOCKET_PORT;
                _webSocketServer = new WebSocketServer(webSocketPort, true);
                _webSocketServer.SslConfiguration = new WebSocketSharp.Net.ServerSslConfiguration(wssCertificate, false, System.Security.Authentication.SslProtocols.Default, false);
                _webSocketServer.Log.Level = LogLevel.Debug;

                // Standard encrypted WebRtc stream.
                _webSocketServer.AddWebSocketService<SDPExchange>("/max", () =>
                {
                    SDPExchange sdpReceiver = new SDPExchange(MediaSourceEnum.Max, false, rtpIPAddress) { IgnoreExtensions = true };
                    sdpReceiver.WebSocketOpened += WebRtcStartCall;
                    sdpReceiver.SDPAnswerReceived += WebRtcAnswerReceived;
                    return sdpReceiver;
                });

                // Decrypted WebRtc stream for diagnostics (browsers do not support this without specific flags being enabled).
                _webSocketServer.AddWebSocketService<SDPExchange>("/maxnocry", () =>
                {
                    SDPExchange sdpReceiver = new SDPExchange(MediaSourceEnum.Max, true, rtpIPAddress) { IgnoreExtensions = true };
                    sdpReceiver.WebSocketOpened += WebRtcStartCall;
                    sdpReceiver.SDPAnswerReceived += WebRtcAnswerReceived;
                    return sdpReceiver;
                });

                if (!String.IsNullOrEmpty(_testPattermImagePath) && File.Exists(_testPattermImagePath))
                {
                    _webSocketServer.AddWebSocketService<SDPExchange>("/testpattern", () =>
                    {
                        SDPExchange sdpReceiver = new SDPExchange(MediaSourceEnum.TestPattern, false, rtpIPAddress) { IgnoreExtensions = true };
                        sdpReceiver.WebSocketOpened += WebRtcStartCall;
                        sdpReceiver.SDPAnswerReceived += WebRtcAnswerReceived;
                        return sdpReceiver;
                    });
                }

                _webSocketServer.Start();

                // Initialise the Media Foundation library that will pull the samples from the mp4 file.
                _mfSampleGrabber = new SIPSorceryMedia.MFSampleGrabber();
                _mfSampleGrabber.OnClockStartEvent += OnClockStartEvent;
                _mfSampleGrabber.OnVideoResolutionChangedEvent += OnVideoResolutionChangedEvent;
                unsafe
                {
                    _mfSampleGrabber.OnProcessSampleEvent += OnProcessSampleEvent;
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
                Task.Run(() => _mfSampleGrabber.Run(_mediaFilePath, true));
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

                _mfSampleGrabber.StopAndExit();
                _webSocketServer.Stop();

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

            _mfSampleGrabber.Start();  // Does nothing if media session is not paused.

            lock (_webRtcSessions)
            {
                if (!_webRtcSessions.Any(x => x.Key == webSocketID))
                {
                    var webRtcSession = new WebRtcSession(_dtlsCertificatePath, _dtlsKeyPath, webSocketID, isEncryptionDisabled, mediaSource);

                    string dtlsThumbrpint = (isEncryptionDisabled == false) ? _dtlsCertificateThumbprint : null;

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

                    if (answerSDP.IceCandidates == null)
                    {
                        logger.Warn("SDP answer did not contain any ICE candidates.");
                    }
                    else
                    {
                        foreach (var iceCandidate in answerSDP.IceCandidates)
                        {
                            peer.AppendRemoteIceCandidate(iceCandidate);
                        }
                    }
                }

                // TODO: The web socket has now completed it's job and can be closed.
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
                if (String.IsNullOrEmpty(_rawRtpBaseEndPoint) && _webRtcSessions.Count() == 0)
                {
                    if (_mfSampleGrabber.Paused == false)
                    {
                        logger.Info("No active clients, pausing media sampling.");
                        _mfSampleGrabber.Pause();
                    }
                }
                else
                {
                    lock (_webRtcSessions)
                    {
                        foreach (var session in _webRtcSessions.Where(x => (x.Value.Peer.IsDtlsNegotiationComplete == true || x.Value.IsEncryptionDisabled == true) &&
                           x.Value.Peer.LocalIceCandidates.Any(y => y.RemoteRtpEndPoint != null) && x.Value.MediaSource == MediaSourceEnum.Max &&
                           x.Value.Peer.IsClosed == false))
                        {
                            try
                            {
                                session.Value.SendMedia(mediaType, timestamp, sample);
                            }
                            catch (Exception sendExcp)
                            {
                                logger.Warn("Exception OnMediaSampleReady.SendMedia. " + sendExcp.Message);
                                session.Value.Peer.Close();
                            }
                        }
                    }

                    // Deliver periodic RTCP sender reports. This helps the receiver to sync the audio and video stream timestamps.
                    // If there are gaps in the media, silence supression etc. then the sender repors shouldn't be triggered from the media samples.
                    // In this case the samples are from an mp4 file which provides a constant uninterrupted stream.
                    if (DateTime.Now.Subtract(_lastRtcpSenderReportSentAt).TotalSeconds >= RTCP_SR_PERIOD_SECONDS)
                    {
                        foreach (var session in _webRtcSessions.Where(x => (x.Value.Peer.IsDtlsNegotiationComplete == true || x.Value.IsEncryptionDisabled == true) &&
                          x.Value.Peer.LocalIceCandidates.Any(y => y.RemoteRtpEndPoint != null) && x.Value.MediaSource == MediaSourceEnum.Max &&
                          x.Value.Peer.IsClosed == false))
                        {
                            try
                            {
                                session.Value.SendRtcpSenderReports(_mulawTimestamp, _vp8Timestamp);
                            }
                            catch (Exception sendExcp)
                            {
                                logger.Warn("Exception OnMediaSampleReady.SendRtcpSenderReports. " + sendExcp.Message);
                                session.Value.Peer.Close();
                            }
                        }

                        _lastRtcpSenderReportSentAt = DateTime.Now;
                    }
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
                    Bitmap testPattern = new Bitmap(_testPattermImagePath);

                    // Get the stride.
                    Rectangle rect = new Rectangle(0, 0, testPattern.Width, testPattern.Height);
                    System.Drawing.Imaging.BitmapData bmpData =
                        testPattern.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite,
                        testPattern.PixelFormat);

                    // Get the address of the first line.
                    int stride = bmpData.Stride;

                    testPattern.UnlockBits(bmpData);

                    // Initialise the video codec and color converter.
                    SIPSorceryMedia.VPXEncoder vpxEncoder = new VPXEncoder();
                    vpxEncoder.InitEncoder((uint)testPattern.Width, (uint)testPattern.Height, (uint)stride);

                    SIPSorceryMedia.ImageConvert colorConverter = new ImageConvert();

                    byte[] sampleBuffer = null;
                    byte[] encodedBuffer = null;
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
                                colorConverter.ConvertRGBtoYUV(p, VideoSubTypesEnum.BGR24, testPattern.Width, testPattern.Height, stride, VideoSubTypesEnum.I420, ref convertedFrame);

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
                                    try
                                    {
                                        session.Value.SendMedia(MediaSampleTypeEnum.VP8, rtpTimestamp, encodedBuffer);
                                    }
                                    catch (Exception sendExcp)
                                    {
                                        logger.Warn("Exception SendTestPattern.SendMedia. " + sendExcp.Message);
                                        session.Value.Peer.Close();
                                    }
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
