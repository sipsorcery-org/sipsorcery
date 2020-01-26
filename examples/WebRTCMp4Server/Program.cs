//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC server application that serves a media stream
// sourced from an MP4 file to a WebRTC enabled browser.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 20 Jan 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NAudio.Codecs;
using Serilog;
using SIPSorcery.Net;
using SIPSorceryMedia;
using WebSocketSharp;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;

namespace WebRTCServer
{
    public class SDPExchange : WebSocketBehavior
    {
        public WebRtcSession WebRtcSession;

        public event Func<WebSocketContext, Task<WebRtcSession>> WebSocketOpened;
        public event Action<WebRtcSession, string> SDPAnswerReceived;

        public SDPExchange()
        { }

        protected override void OnMessage(MessageEventArgs e)
        {
            SDPAnswerReceived(WebRtcSession, e.Data);
            this.Close();
        }

        protected override async void OnOpen()
        {
            base.OnOpen();
            WebRtcSession = await WebSocketOpened(this.Context);
        }
    }

    class Program
    {
        private static string MP4_FILE_PATH = "media/big_buck_bunny.mp4";
        private const int VP8_TIMESTAMP_SPACING = 3000;
        private const int VP8_PAYLOAD_TYPE_ID = 100;
        private const string WEBSOCKET_CERTIFICATE_PATH = "certs/localhost.pfx";
        private const string DTLS_CERTIFICATE_PATH = "certs/localhost.pem";
        private const string DTLS_KEY_PATH = "certs/localhost_key.pem";
        private const string DTLS_CERTIFICATE_FINGERPRINT = "sha-256 C6:ED:8C:9D:06:50:77:23:0A:4A:D8:42:68:29:D0:70:2F:BB:C7:72:EC:98:5C:62:07:1B:0C:5D:CB:CE:BE:CD";
        private const int WEBSOCKET_PORT = 8081;

        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;

        public static readonly List<SDPMediaFormat> _supportedAudioFormats = new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) };
        public static readonly List<SDPMediaFormat> _supportedVideoFormats = new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) };

        private static WebSocketServer _webSocketServer;
        private static MFSampleGrabber _mfSampleGrabber;
        private static VpxEncoder _vpxEncoder;
        private static bool _vpxEncoderReady = false;
        private static uint _vp8Timestamp;
        private static uint _mulawTimestamp;

        private delegate void MediaSampleReadyDelegate(SDPMediaTypesEnum mediaType, uint timestamp, byte[] sample);
        private static event MediaSampleReadyDelegate OnMediaSampleReady;

        static void Main()
        {
            Console.WriteLine("WebRTC Server Sample Program");
            Console.WriteLine("Press ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.
            ManualResetEvent exitMre = new ManualResetEvent(false);

            AddConsoleLogger();

            if (!File.Exists(MP4_FILE_PATH))
            {
                throw new ApplicationException($"The media file at does not exist at {MP4_FILE_PATH}.");
            }

            // Initialise OpenSSL & libsrtp, saves a couple of seconds for the first client connection.
            Console.WriteLine("Initialising OpenSSL and libsrtp...");
            Dtls.InitialiseOpenSSL();
            Srtp.InitialiseLibSrtp();

            // Initialise the Media Foundation library that will pull the samples from the mp4 file.
            _mfSampleGrabber = new SIPSorceryMedia.MFSampleGrabber();
            _mfSampleGrabber.OnVideoResolutionChangedEvent += OnVideoResolutionChangedEvent;
            unsafe
            {
                _mfSampleGrabber.OnProcessSampleEvent += OnProcessSampleEvent;
            }
            Task.Run(() => _mfSampleGrabber.Run(MP4_FILE_PATH, true));

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            _webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT, true);
            _webSocketServer.SslConfiguration.ServerCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(WEBSOCKET_CERTIFICATE_PATH);
            _webSocketServer.SslConfiguration.CheckCertificateRevocation = false;
            //_webSocketServer.Log.Level = WebSocketSharp.LogLevel.Debug;
            _webSocketServer.AddWebSocketService<SDPExchange>("/", (sdpExchanger) =>
            {
                sdpExchanger.WebSocketOpened += SendSDPOffer;
                sdpExchanger.SDPAnswerReceived += SDPAnswerReceived;
            });
            _webSocketServer.Start();

            Console.WriteLine($"Waiting for browser web socket connection to {_webSocketServer.Address}:{_webSocketServer.Port}...");

            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitMre.Set();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitMre.WaitOne();

            _mfSampleGrabber.StopAndExit();
            _webSocketServer.Stop();
        }

        private static async Task<WebRtcSession> SendSDPOffer(WebSocketContext context)
        {
            logger.LogDebug($"Web socket client connection from {context.UserEndPoint}.");

            var webRtcSession = new WebRtcSession(
                DTLS_CERTIFICATE_FINGERPRINT,
                _supportedAudioFormats,
                _supportedVideoFormats,
                null);

            webRtcSession.AudioStreamStatus = MediaStreamStatusEnum.SendOnly;
            webRtcSession.VideoStreamStatus = MediaStreamStatusEnum.SendOnly;
            webRtcSession.RtpSession.OnReceiveReport += RtpSession_OnReceiveReport;
            webRtcSession.RtpSession.OnSendReport += RtpSession_OnSendReport;

            logger.LogDebug($"Sending SDP offer to client {context.UserEndPoint}.");

            webRtcSession.OnClose += (reason) =>
            {
                logger.LogDebug($"WebRtcSession was closed with reason {reason}");
                OnMediaSampleReady -= webRtcSession.SendMedia;
            };

            await webRtcSession.Initialise(DoDtlsHandshake, null);

            context.WebSocket.Send(webRtcSession.SDP.ToString());

            return webRtcSession;
        }

        private static void SDPAnswerReceived(WebRtcSession webRtcSession, string sdpAnswer)
        {
            try
            {
                logger.LogDebug("Answer SDP: " + sdpAnswer);

                var answerSDP = SDP.ParseSDPDescription(sdpAnswer);

                webRtcSession.OnSdpAnswer(answerSDP);

                OnMediaSampleReady += webRtcSession.SendMedia;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SDPAnswerReceived. " + excp.Message);
            }
        }

        /// <summary>
        /// Hands the socket handle to the DTLS context and waits for the handshake to complete.
        /// </summary>
        /// <param name="webRtcSession">The WebRTC session to perform the DTLS handshake on.</param>
        private static int DoDtlsHandshake(WebRtcSession webRtcSession)
        {
            logger.LogDebug("DoDtlsHandshake started.");

            if (!File.Exists(DTLS_CERTIFICATE_PATH))
            {
                throw new ApplicationException($"The DTLS certificate file could not be found at {DTLS_CERTIFICATE_PATH}.");
            }
            else if (!File.Exists(DTLS_KEY_PATH))
            {
                throw new ApplicationException($"The DTLS key file could not be found at {DTLS_KEY_PATH}.");
            }

            var dtls = new Dtls(DTLS_CERTIFICATE_PATH, DTLS_KEY_PATH);
            webRtcSession.OnClose += (reason) => dtls.Shutdown();

            int res = dtls.DoHandshake((ulong)webRtcSession.RtpSession.RtpChannel.RtpSocket.Handle);

            logger.LogDebug("DtlsContext initialisation result=" + res);

            if (dtls.GetState() == (int)DtlsState.OK)
            {
                logger.LogDebug("DTLS negotiation complete.");

                var srtpSendContext = new Srtp(dtls, false);
                var srtpReceiveContext = new Srtp(dtls, true);

                webRtcSession.RtpSession.SetSecurityContext(
                    srtpSendContext.ProtectRTP,
                    srtpReceiveContext.UnprotectRTP,
                    srtpSendContext.ProtectRTCP,
                    srtpReceiveContext.UnprotectRTCP);

                if (_mfSampleGrabber.Paused)
                {
                    _mfSampleGrabber.Start();
                }
            }

            return res;
        }

        private static void OnVideoResolutionChangedEvent(uint width, uint height, uint stride)
        {
            try
            {
                if (_vpxEncoder == null ||
                    (_vpxEncoder.GetWidth() != width || _vpxEncoder.GetHeight() != height || _vpxEncoder.GetStride() != stride))
                {
                    _vpxEncoderReady = false;

                    if (_vpxEncoder != null)
                    {
                        _vpxEncoder.Dispose();
                    }

                    _vpxEncoder = new VpxEncoder();
                    _vpxEncoder.InitEncoder(width, height, stride);

                    logger.LogInformation($"VPX encoder initialised with width {width}, height {height} and stride {stride}.");

                    _vpxEncoderReady = true;
                }
            }
            catch (Exception excp)
            {
                logger.LogWarning("Exception OnVideoResolutionChangedEvent. " + excp.Message);
            }
        }

        unsafe private static void OnProcessSampleEvent(int mediaTypeID, uint dwSampleFlags, long llSampleTime, long llSampleDuration, uint dwSampleSize, ref byte[] sampleBuffer)
        {
            try
            {
                if (OnMediaSampleReady == null)
                {
                    if (!_mfSampleGrabber.Paused)
                    {
                        _mfSampleGrabber.Pause();
                        logger.LogDebug("No active clients, media sampling paused.");
                    }
                }
                else
                {
                    if (mediaTypeID == 0)
                    {
                        if (!_vpxEncoderReady)
                        {
                            logger.LogWarning("Video sample cannot be processed as the VPX encoder is not in a ready state.");
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
                                        logger.LogWarning("VPX encode of video sample failed.");
                                    }
                                }
                            }

                            OnMediaSampleReady?.Invoke(SDPMediaTypesEnum.video, _vp8Timestamp, vpxEncodedBuffer);

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

                        OnMediaSampleReady?.Invoke(SDPMediaTypesEnum.audio, _mulawTimestamp, mulawSample);

                        //Console.WriteLine($"Audio SeqNum {audioSeqNum}, timestamp {audioTimestamp}, buffer length {mulawSample.Length}.");

                        _mulawTimestamp += sampleDuration;
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogWarning("Exception OnProcessSampleEvent. " + excp.Message);
            }
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP sender reports.
        /// </summary>
        private static void RtpSession_OnSendReport(SDPMediaTypesEnum mediaType, RTCPCompoundPacket sentRtcpReport)
        {
            var sr = sentRtcpReport.SenderReport;
            logger.LogDebug($"RTCP {mediaType} Sender Report: SSRC {sr.SSRC}, pkts {sr.PacketCount}, bytes {sr.OctetCount}.");
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP reports from the remote WebRTC peer.
        /// </summary>
        private static void RtpSession_OnReceiveReport(SDPMediaTypesEnum mediaType, RTCPCompoundPacket recvRtcpReport)
        {
            var rr = recvRtcpReport.ReceiverReport.ReceptionReports.FirstOrDefault();
            if (rr != null)
            {
                logger.LogDebug($"RTCP {mediaType} Receiver Report: SSRC {rr.SSRC}, pkts lost {rr.PacketsLost}, delay since SR {rr.DelaySinceLastSenderReport}.");
            }
            else
            {
                logger.LogDebug($"RTCP {mediaType} Receiver Report: empty.");
            }
        }

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static void AddConsoleLogger()
        {
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            var loggerConfig = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            loggerFactory.AddSerilog(loggerConfig);
            SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;
        }
    }
}
