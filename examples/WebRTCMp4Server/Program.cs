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
        private static MFVideoSampler _mfSampler;
        private static bool _isSampling = false;
        private static VpxEncoder _vpxEncoder;
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
            //_mfSampleGrabber = new SIPSorceryMedia.MFSampleGrabber();
            //_mfSampleGrabber.OnVideoResolutionChangedEvent += OnVideoResolutionChangedEvent;
            //unsafe
            //{
            //    _mfSampleGrabber.OnProcessSampleEvent += OnProcessSampleEvent;
            //}
            //Task.Run(() => _mfSampleGrabber.Run(MP4_FILE_PATH, true));

            _mfSampler = new MFVideoSampler();
            //_mfSampler.InitFromFile(MP4_FILE_PATH);
            _mfSampler.Init(0, VideoSubTypesEnum.I420, 640, 480);
            //StartMedia();

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            _webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT, true);
            _webSocketServer.SslConfiguration.ServerCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(WEBSOCKET_CERTIFICATE_PATH);
            _webSocketServer.SslConfiguration.CheckCertificateRevocation = false;
            _webSocketServer.Log.Level = WebSocketSharp.LogLevel.Debug;
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

            //_mfSampleGrabber.StopAndExit();
            _mfSampler.Stop();
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

                if (!_isSampling)
                {
                    Task.Run(StartMedia);
                }
            }

            return res;
        }

        private static void OnVideoResolutionChanged(uint width, uint height, uint stride)
        {
            try
            {
                if (_vpxEncoder == null ||
                    (_vpxEncoder.GetWidth() != width || _vpxEncoder.GetHeight() != height || _vpxEncoder.GetStride() != stride))
                {
                    //_vpxEncoderReady = false;

                    if (_vpxEncoder != null)
                    {
                        _vpxEncoder.Dispose();
                    }

                    _vpxEncoder = new VpxEncoder();
                    _vpxEncoder.InitEncoder(width, height, stride);

                    logger.LogInformation($"VPX encoder initialised with width {width}, height {height} and stride {stride}.");

                    //_vpxEncoderReady = true;
                }
            }
            catch (Exception excp)
            {
                logger.LogWarning("Exception OnVideoResolutionChangedEvent. " + excp.Message);
            }
        }

        unsafe private static void StartMedia()
        {
            try
            {
                while (true)
                {
                    if (OnMediaSampleReady == null)
                    {
                        logger.LogDebug("No active clients, media sampling paused.");
                        break;
                    }
                    else
                    {
                        byte[] sampleBuffer = null;
                        //var sample = _mfSampler.GetNextSample(ref sampleBuffer);
                        var sample = _mfSampler.GetSample(ref sampleBuffer);

                        if (sample != null && sample.HasVideoSample)
                        {
                            if (_vpxEncoder == null ||
                                (_vpxEncoder.GetWidth() != sample.Width || _vpxEncoder.GetHeight() != sample.Height || _vpxEncoder.GetStride() != sample.Stride))
                            {
                                OnVideoResolutionChanged((uint)sample.Width, (uint)sample.Height, (uint)sample.Stride);
                            }

                            byte[] vpxEncodedBuffer = null;

                            unsafe
                            {
                                fixed (byte* p = sampleBuffer)
                                {
                                    int encodeResult = _vpxEncoder.Encode(p, sampleBuffer.Length, 1, ref vpxEncodedBuffer);

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
                        else if (sample != null && sample.HasAudioSample)
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
            }
            catch (Exception excp)
            {
                logger.LogWarning("Exception OnProcessSampleEvent. " + excp.Message);
            }
        }

        //private static void SampleFile(MFVideoSampler mfSampler)
        //{
        //    try
        //    {
        //        var vpxEncoder = new VpxEncoder();
        //        // TODO: The last parameter passed to the vpx encoder init needs to be the frame stride not the width.
        //        //vpxEncoder.InitEncoder(Convert.ToUInt32(videoMode.Width), Convert.ToUInt32(videoMode.Height), Convert.ToUInt32(videoMode.Width));

        //        // var videoSampler = new MFVideoSampler();
        //        //videoSampler.Init(videoMode.DeviceIndex, videoMode.Width, videoMode.Height);
        //        // videoSampler.InitFromFile();

        //        while (true)
        //        {
        //            byte[] mediaSample = null;
        //            var sample = mfSampler.GetNextSample((int)MediaFoundationStreamsEnum.MF_SOURCE_READER_ANY_STREAM, ref mediaSample);

        //            //if (result == NAudio.MediaFoundation.MediaFoundationErrors.MF_E_HW_MFT_FAILED_START_STREAMING)
        //            //{
        //            //    logger.Warn("A sample could not be acquired from the local webcam. Check that it is not already in use.");
        //            //    OnLocalVideoError("A sample could not be acquired from the local webcam. Check that it is not already in use.");
        //            //    break;
        //            //}
        //            //else if (result != 0)
        //            //{
        //            //    logger.Warn("A sample could not be acquired from the local webcam. Check that it is not already in use. Error code: " + result);
        //            //    OnLocalVideoError("A sample could not be acquired from the local webcam. Check that it is not already in use. Error code: " + result);
        //            //    break;
        //            //}
        //            //else 
        //            if (sample?.HasVideoSample == true)
        //            {
        //                Console.WriteLine($"Video sample ready {sample.Width}x{sample.Height} timestamp {sample.Timestamp}.");

        //                // This event sends the raw bitmap to the WPF UI.
        //                //OnLocalVideoSampleReady?.Invoke(videoSample, videoSampler.Width, videoSampler.Height);

        //                //// This event encodes the sample and forwards it to the RTP manager for network transmission.
        //                //if (OnLocalVideoEncodedSampleReady != null)
        //                //{
        //                //    IntPtr rawSamplePtr = Marshal.AllocHGlobal(videoSample.Length);
        //                //    Marshal.Copy(videoSample, 0, rawSamplePtr, videoSample.Length);

        //                //    byte[] yuv = null;

        //                //    unsafe
        //                //    {
        //                //        // TODO: using width instead of stride.
        //                //        _imageConverter.ConvertRGBtoYUV((byte*)rawSamplePtr, VideoSubTypesEnum.RGB24, Convert.ToInt32(videoMode.Width), Convert.ToInt32(videoMode.Height), Convert.ToInt32(videoMode.Width), VideoSubTypesEnum.I420, ref yuv);
        //                //        //_imageConverter.ConvertToI420((byte*)rawSamplePtr, VideoSubTypesEnum.RGB24, Convert.ToInt32(videoMode.Width), Convert.ToInt32(videoMode.Height), ref yuv);
        //                //    }

        //                //    Marshal.FreeHGlobal(rawSamplePtr);

        //                //    IntPtr yuvPtr = Marshal.AllocHGlobal(yuv.Length);
        //                //    Marshal.Copy(yuv, 0, yuvPtr, yuv.Length);

        //                //    byte[] encodedBuffer = null;

        //                //    unsafe
        //                //    {
        //                //        vpxEncoder.Encode((byte*)yuvPtr, yuv.Length, _encodingSample++, ref encodedBuffer);
        //                //    }

        //                //    Marshal.FreeHGlobal(yuvPtr);

        //                //    OnLocalVideoEncodedSampleReady(encodedBuffer);
        //                //}
        //            }
        //            else if (sample?.HasAudioSample == true)
        //            {
        //                Console.WriteLine($"Audio sample ready timestamp {sample.Timestamp}.");
        //            }
        //            else if (sample?.EndOfStream == true)
        //            {
        //                break;
        //            }

        //        }

        //        mfSampler.Stop();
        //        vpxEncoder.Dispose();
        //    }
        //    catch (Exception excp)
        //    {
        //        logger.LogError($"Exception SampleWebCam. {excp.Message}");
        //    }
        //}

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
