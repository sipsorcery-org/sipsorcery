
//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC server application that streams the contents 
// of a media file, such as an mp4, to a WebRTC enabled browser.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// Christophe Irles (christophe.irles@al-enterprise.com)
// 
// History:
// 17 Sep 2020	Aaron Clauson	Created, Dublin, Ireland.
// 27 Nov 2021 Christophe Irles Split Audio/Video, Add Camera support
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using WebSocketSharp.Server;

namespace FFmpegFileAndDevicesTest
{
    class Program
    {
        private const bool USE_AUDIO = true;
        private const bool USE_VIDEO = true;
        
        private const bool REPEAT_AUDIO = true;
        private const bool REPEAT_VIDEO = true;

        private const bool USE_WEBCAM = true;
        private const string WEBCAM_DEVICE = "video=HD Pro Webcam C920";

        private const string LIB_PATH = @"C:\ffmpeg-4.3-sipsorcery";
        //private const string LIB_PATH = @"C:\ffmpeg-4.4.1-full_build-shared\bin";

        private const string AUDIO_FILE_PATH = @"C:\media\simplest_ffmpeg_audio_decoder_skycity1.mp3";
        private const string VIDEO_FILE_PATH = @"C:\media\big_buck_bunny.mp4";

        private const VideoCodecsEnum VIDEO_CODEC = VideoCodecsEnum.H264; // VideoCodecsEnum.VP8
        private const AudioCodecsEnum AUDIO_CODEC = AudioCodecsEnum.PCMU;

        private const int WEBSOCKET_PORT = 8081;
        private const string STUN_URL = "stun:stun.sipsorcery.com";
        
        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        static void Main()
        {
            Console.WriteLine("WebRTC MP4 Source Demo");

            logger = AddConsoleLogger();

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            var webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
            webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/", (peer) => peer.CreatePeerConnection = CreatePeerConnection);
            webSocketServer.Start();

            Console.WriteLine($"Waiting for web socket connections on {webSocketServer.Address}:{webSocketServer.Port}...");
            Console.WriteLine("Press ctrl-c to exit.");

            // Ctrl-c will gracefully exit the call at any point.
            ManualResetEvent exitMre = new ManualResetEvent(false);
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitMre.Set();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitMre.WaitOne();
        }

        private static Task<RTCPeerConnection> CreatePeerConnection()
        {
            IVideoSource videoSource = null;
            IAudioSource audioSource = null;

            RTCConfiguration config = new RTCConfiguration
            {
                iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } }
            };
            var pc = new RTCPeerConnection(config);

            SIPSorceryMedia.FFmpeg.FFmpegInit.Initialise(SIPSorceryMedia.FFmpeg.FfmpegLogLevelEnum.AV_LOG_DEBUG, LIB_PATH);

            if (USE_AUDIO)
            {
                audioSource = new SIPSorceryMedia.FFmpeg.FFmpegFileSource(AUDIO_FILE_PATH, REPEAT_AUDIO, new AudioEncoder(), false);

                audioSource.RestrictFormats(x => x.Codec == AUDIO_CODEC);

                MediaStreamTrack audioTrack = new MediaStreamTrack(audioSource.GetAudioSourceFormats(), MediaStreamStatusEnum.SendRecv);
                pc.addTrack(audioTrack);

                audioSource.OnAudioSourceEncodedSample += pc.SendAudio;
                pc.OnAudioFormatsNegotiated += (audioFormats) => audioSource.SetAudioSourceFormat(audioFormats.First());
            }


            if (USE_VIDEO)
            {
                if (USE_WEBCAM)
                {
                    videoSource = new SIPSorceryMedia.FFmpeg.FFmpegCameraSource(WEBCAM_DEVICE);
                }
                else
                {
                    videoSource = new SIPSorceryMedia.FFmpeg.FFmpegFileSource(VIDEO_FILE_PATH, REPEAT_VIDEO, null, USE_VIDEO);
                }

                videoSource.RestrictFormats(x => x.Codec == VIDEO_CODEC);

                MediaStreamTrack videoTrack = new MediaStreamTrack(videoSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendRecv);
                pc.addTrack(videoTrack);

                videoSource.OnVideoSourceEncodedSample += pc.SendVideo;
                pc.OnVideoFormatsNegotiated += (videoFormats) => videoSource.SetVideoSourceFormat(videoFormats.First());
            }

            //mediaFileSource.OnEndOfFile += () => pc.Close("source eof");

            pc.onconnectionstatechange += async (state) =>
            {
                logger.LogDebug($"Peer connection state change to {state}.");

                if (state == RTCPeerConnectionState.failed)
                {
                    pc.Close("ice disconnection");
                }
                else if (state == RTCPeerConnectionState.closed)
                {
                    if(videoSource != null)
                        await videoSource.CloseVideo();

                    if (audioSource != null)
                        await audioSource.CloseAudio();
                }
                else if (state == RTCPeerConnectionState.connected)
                {
                    if (videoSource != null)
                        await videoSource.StartVideo();

                    if (audioSource != null)
                        await audioSource.StartAudio();
                }
            };

            // Diagnostics.
            //pc.OnReceiveReport += (re, media, rr) => logger.LogDebug($"RTCP Receive for {media} from {re}\n{rr.GetDebugSummary()}");
            //pc.OnSendReport += (media, sr) => logger.LogDebug($"RTCP Send for {media}\n{sr.GetDebugSummary()}");
            //pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) => logger.LogDebug($"STUN {msg.Header.MessageType} received from {ep}.");
            pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change to {state}.");

            return Task.FromResult(pc);
        }

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
        {
            var seriLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(seriLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<Program>();
        }
    }
}

