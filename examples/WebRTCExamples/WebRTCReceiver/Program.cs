//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Displays a VP8 video stream received from a WebRTC peer.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 05 Feb 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Net;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions.V1;
using WebSocketSharp.Server;

namespace demo
{
    class Program
    {
        private const string STUN_URL = "stun:stun.sipsorcery.com";
        private const int WEBSOCKET_PORT = 8081;

        private static Form _form;
        private static PictureBox _picBox;

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        //[STAThread]
        static void Main()
        {
            Console.WriteLine("WebRTC Receive Demo");

            logger = AddConsoleLogger();

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            var webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
            webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/", (peer) => peer.CreatePeerConnection = CreatePeerConnection);
            webSocketServer.Start();

            Console.WriteLine($"Waiting for web socket connections on {webSocketServer.Address}:{webSocketServer.Port}...");

            // Open a Window to display the video feed from the WebRTC peer.
            _form = new Form();
            _form.AutoSize = true;
            _form.BackgroundImageLayout = ImageLayout.Center;
            _picBox = new PictureBox
            {
                Size = new Size(640, 480),
                Location = new Point(0, 0),
                Visible = true
            };
            _form.Controls.Add(_picBox);

            Application.EnableVisualStyles();
            Application.Run(_form);
        }

        private static RTCPeerConnection CreatePeerConnection()
        {
            //var videoEP = new SIPSorceryMedia.Windows.WindowsVideoEndPoint();
            var videoEP = new SIPSorceryMedia.FFmpeg.FFmpegVideoEndPoint();
            videoEP.RestrictCodecs(new List<VideoCodecsEnum> { VideoCodecsEnum.VP8 });

            videoEP.OnVideoSinkDecodedSample += (byte[] bmp, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat) =>
            {
                _form.BeginInvoke(new Action(() =>
                {
                    unsafe
                    {
                        fixed (byte* s = bmp)
                        {
                            Bitmap bmpImage = new Bitmap((int)width, (int)height, (int)(bmp.Length / height), PixelFormat.Format24bppRgb, (IntPtr)s);
                            _picBox.Image = bmpImage;
                        }
                    }
                }));
            };

            RTCConfiguration config = new RTCConfiguration
            {
                iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } }
            };
            var pc = new RTCPeerConnection(config);

            // Add local receive only tracks. This ensures that the SDP answer includes only the codecs we support.
            MediaStreamTrack audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) }, MediaStreamStatusEnum.RecvOnly);
            pc.addTrack(audioTrack);
            MediaStreamTrack videoTrack = new MediaStreamTrack(videoEP.GetVideoSinkFormats(), MediaStreamStatusEnum.RecvOnly);
            pc.addTrack(videoTrack);

            pc.OnVideoFrameReceived += videoEP.GotVideoFrame;
            pc.OnVideoFormatsNegotiated += (sdpFormat) => videoEP.SetVideoSinkFormat(SDPMediaFormatInfo.GetVideoCodecForSdpFormat(sdpFormat.First().FormatCodec));

            pc.onconnectionstatechange += async (state) =>
            {
                logger.LogDebug($"Peer connection state change to {state}.");

                if (state == RTCPeerConnectionState.failed)
                {
                    pc.Close("ice disconnection");
                }
                else if (state == RTCPeerConnectionState.closed)
                {
                    await videoEP.CloseVideo();
                }
            };

            // Diagnostics.
            //pc.OnReceiveReport += (re, media, rr) => logger.LogDebug($"RTCP Receive for {media} from {re}\n{rr.GetDebugSummary()}");
            //pc.OnSendReport += (media, sr) => logger.LogDebug($"RTCP Send for {media}\n{sr.GetDebugSummary()}");
            //pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) => logger.LogDebug($"STUN {msg.Header.MessageType} received from {ep}.");
            pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change to {state}.");

            return pc;
        }

        /// <summary>
        /// Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
        {
            var serilogLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(serilogLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<Program>();
        }
    }
}
