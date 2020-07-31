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
        public RTCPeerConnection PeerConnection;

        public event Func<WebSocketContext, Task<RTCPeerConnection>> WebSocketOpened;
        public event Action<RTCPeerConnection, string> OnMessageReceived;

        public SDPExchange()
        { }

        protected override void OnMessage(MessageEventArgs e)
        {
            OnMessageReceived(PeerConnection, e.Data);
        }

        protected override async void OnOpen()
        {
            base.OnOpen();
            PeerConnection = await WebSocketOpened(this.Context);
        }

        protected override void OnClose(CloseEventArgs e)
        {
            base.OnClose(e);
            PeerConnection.Close("remote party close");
        }
    }

    class Program
    {
        private static string MP4_FILE_PATH = "media/big_buck_bunny.mp4";
        private const int VP8_TIMESTAMP_SPACING = 3000;
        private const int VP8_PAYLOAD_TYPE_ID = 100;
        private const string LOCALHOST_CERTIFICATE_PATH = "certs/localhost.pfx";
        private const int WEBSOCKET_PORT = 8081;

        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;

        private static WebSocketServer _webSocketServer;
        private static MediaSource _mediaSource;
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
            else if (!File.Exists(LOCALHOST_CERTIFICATE_PATH))
            {
                throw new ApplicationException($"The localhost certificate file for the web socket server could not be found at {LOCALHOST_CERTIFICATE_PATH }.");
            }

            // The MediaSource class is a prototype class that wraps some basic Windows Media Foundation API's.
            // It has not been extensively tested and your mileage may vary with different file sources and
            // webcams.
            _mediaSource = new MediaSource();

            // To use the mp4 file media source uncomment the line below:
            _mediaSource.Init(MP4_FILE_PATH, true);

            // To use a webcam as the media source uncomment the line below and adjust the
            // pixel format and dimensions to a mode supported by your webcam.
            //_mediaSource.Init(0, 0, VideoSubTypesEnum.YUY2, 640, 480);

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            _webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT, true);
            _webSocketServer.SslConfiguration.ServerCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(LOCALHOST_CERTIFICATE_PATH);
            _webSocketServer.SslConfiguration.CheckCertificateRevocation = false;
            //_webSocketServer.Log.Level = WebSocketSharp.LogLevel.Debug;
            _webSocketServer.AddWebSocketService<SDPExchange>("/", (sdpExchanger) =>
            {
                sdpExchanger.WebSocketOpened += SendSDPOffer;
                sdpExchanger.OnMessageReceived += WebSocketMessageReceived;
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

            _mediaSource.Shutdown();
            _webSocketServer.Stop();
        }

        private static async Task<RTCPeerConnection> SendSDPOffer(WebSocketContext context)
        {
            logger.LogDebug($"Web socket client connection from {context.UserEndPoint}.");

            var peerConnection = new RTCPeerConnection(null);

            MediaStreamTrack audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) }, MediaStreamStatusEnum.SendOnly);
            peerConnection.addTrack(audioTrack);
            MediaStreamTrack videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) }, MediaStreamStatusEnum.SendOnly);
            peerConnection.addTrack(videoTrack);

            peerConnection.OnReceiveReport += RtpSession_OnReceiveReport;
            peerConnection.OnSendReport += RtpSession_OnSendReport;
            peerConnection.OnTimeout += (mediaType) => peerConnection.Close("remote timeout");
            peerConnection.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state changed to {state}.");
            peerConnection.onconnectionstatechange += (state) =>
            {
                logger.LogDebug($"Peer connection state changed to {state}.");

                if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.disconnected || state == RTCPeerConnectionState.failed)
                {
                    OnMediaSampleReady -= peerConnection.SendMedia;
                    peerConnection.OnReceiveReport -= RtpSession_OnReceiveReport;
                    peerConnection.OnSendReport -= RtpSession_OnSendReport;
                }
                else if (state == RTCPeerConnectionState.connected)
                {
                    if (!_isSampling)
                    {
                        _isSampling = true;
                        OnMediaSampleReady += peerConnection.SendMedia;
                        _ = Task.Run(StartMedia);
                    }
                }
            };

            var offerInit = peerConnection.createOffer(null);
            await peerConnection.setLocalDescription(offerInit);

            logger.LogDebug($"Sending SDP offer to client {context.UserEndPoint}.");

            context.WebSocket.Send(offerInit.sdp);

            return peerConnection;
        }

        private static void WebSocketMessageReceived(RTCPeerConnection pc, string message)
        {
            try
            {
                if (pc.remoteDescription == null)
                {
                    logger.LogDebug("Answer SDP: " + message);
                    pc.setRemoteDescription(new RTCSessionDescriptionInit { sdp = message, type = RTCSdpType.answer });
                }
                else
                {
                    logger.LogDebug("ICE Candidate: " + message);
                    pc.addIceCandidate(new RTCIceCandidateInit { candidate = message });
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception WebSocketMessageReceived. " + excp.Message);
            }
        }

        /// <summary>
        /// Video resolution changed event handler.
        /// </summary>
        /// <param name="width">The new video frame width.</param>
        /// <param name="height">The new video frame height.</param>
        /// <param name="stride">The new video frame stride.</param>
        private static void OnVideoResolutionChanged(uint width, uint height, uint stride)
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

                    _vpxEncoder = new VpxEncoder();
                    _vpxEncoder.InitEncoder(width, height, stride);

                    logger.LogInformation($"VPX encoder initialised with width {width}, height {height} and stride {stride}.");
                }
            }
            catch (Exception excp)
            {
                logger.LogWarning("Exception OnVideoResolutionChangedEvent. " + excp.Message);
            }
        }

        /// <summary>
        /// Starts the Media Foundation sampling.
        /// </summary>
        unsafe private static void StartMedia()
        {
            try
            {
                logger.LogDebug("Starting media sampling thread.");

                _isSampling = true;

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
                        var sample = _mediaSource.GetSample(ref sampleBuffer);

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
                logger.LogWarning("Exception StartMedia. " + excp.Message);
            }
            finally
            {
                logger.LogDebug("Media sampling thread stopped.");

                _isSampling = false;
            }
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP sender/receiver reports.
        /// </summary>
        private static void RtpSession_OnSendReport(SDPMediaTypesEnum mediaType, RTCPCompoundPacket sentRtcpReport)
        {
            if (sentRtcpReport.SenderReport != null)
            {
                var sr = sentRtcpReport.SenderReport;
                Console.WriteLine($"RTCP sent SR {mediaType}, ssrc {sr.SSRC}, pkts {sr.PacketCount}, bytes {sr.OctetCount}.");
            }
            else
            {
                var rrSample = sentRtcpReport.ReceiverReport.ReceptionReports.First();
                Console.WriteLine($"RTCP sent RR {mediaType}, ssrc {rrSample.SSRC}, seqnum {rrSample.ExtendedHighestSequenceNumber}.");
            }
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP reports from the remote WebRTC peer.
        /// </summary>
        private static void RtpSession_OnReceiveReport(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTCPCompoundPacket recvRtcpReport)
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
