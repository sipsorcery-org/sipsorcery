//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A receive only WebRTC client that decodes a browser's H.264
// video with SIPSorcery.H264, the pure C# port of OpenH264, and shows it in a
// window. Open capture.html in Chrome, Edge or Firefox and press Start: the
// page sends its camera, or a test pattern when there is no camera, to this
// program over WebRTC. Modelled on the WebRTCClientVP8Net example.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (D7), from WebRTCClientVP8Net.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.H264;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using WebSocketSharp.Server;

namespace demo
{
    class Program
    {
        private const int WEBSOCKET_PORT = 8081;

        private static Microsoft.Extensions.Logging.ILogger logger = null;

        private static Form _form;
        private static PictureBox _picBox;
        private static Bitmap _displayBmp;
        private static bool _isFormActivated;

        [STAThread]
        static void Main(string[] args)
        {
            Console.WriteLine("WebRTC H.264 Client Test Console (SIPSorcery.H264 decoder)");

            logger = AddConsoleLogger();

            Console.WriteLine($"Starting web socket server on port {WEBSOCKET_PORT}...");
            var webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
            webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/", (peer) => peer.CreatePeerConnection = CreatePeerConnection);
            webSocketServer.Start();
            Console.WriteLine("Open capture.html in a browser and press Start.");

            // A window to display the video from the WebRTC peer.
            _form = new Form();
            _form.Text = "SIPSorcery.H264";
            _form.AutoSize = true;
            _form.BackgroundImageLayout = ImageLayout.Center;
            _picBox = new PictureBox
            {
                Size = new Size(640, 480),
                Location = new Point(0, 0),
                Visible = true
            };
            _form.Controls.Add(_picBox);
            _form.FormClosing += (sender, e) => _isFormActivated = false;
            _form.Activated += (sender, e) => _isFormActivated = true;

            Application.EnableVisualStyles();
            Application.Run(_form);

            webSocketServer.Stop();
        }

        private static Task<RTCPeerConnection> CreatePeerConnection()
        {
            var peerConnection = new RTCPeerConnection();

            // Decodes H.264 Constrained Baseline, the profile browsers send for 42e01f.
            var videoEP = new H264NetVideoEndPoint();

            int frames = 0;
            var rateTimer = Stopwatch.StartNew();
            videoEP.OnVideoSinkDecodedSample += (byte[] bgr, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat) =>
            {
                if (++frames % 100 == 0)
                {
                    logger.LogInformation("Decoded {Frames} frames, {Width}x{Height}, {Fps:0.0} fps.", frames, width, height, 100 / rateTimer.Elapsed.TotalSeconds);
                    rateTimer.Restart();
                }

                if (_isFormActivated && _form.Handle != IntPtr.Zero)
                {
                    _form.BeginInvoke(new Action(() =>
                        _displayBmp = ShowFrame(_displayBmp, _picBox, bgr, (int)width, (int)height, stride)));
                }
            };

            MediaStreamTrack videoTrack = new MediaStreamTrack(videoEP.GetVideoSinkFormats(), MediaStreamStatusEnum.RecvOnly);
            peerConnection.addTrack(videoTrack);

            peerConnection.OnVideoFrameReceived += videoEP.GotVideoFrame;
            peerConnection.OnVideoFormatsNegotiated += (formats) =>
            {
                logger.LogInformation("Video format negotiated: {Format}.", formats.First().ToString());
                videoEP.SetVideoSinkFormat(formats.First());
            };

            // The decoder asks for a key frame when it starts mid-stream or loses data. Browsers
            // send one when the session starts and then only when asked, so the asking matters.
            uint remoteVideoSsrc = 0;
            peerConnection.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
            {
                if (media == SDPMediaTypesEnum.video)
                {
                    Interlocked.CompareExchange(ref remoteVideoSsrc, rtpPkt.Header.SyncSource, 0);
                }
            };
            videoEP.OnKeyFrameRequired += () =>
            {
                uint remoteSsrc = Volatile.Read(ref remoteVideoSsrc);
                // The sender SSRC must be the one our RTCP reports go out under; a recvonly local
                // track has none of its own, and a PLI from SSRC 0 is ignored.
                uint localSsrc = peerConnection.VideoRtcpSession?.Ssrc ?? 0;
                if (remoteSsrc != 0 && peerConnection.connectionState == RTCPeerConnectionState.connected)
                {
                    logger.LogInformation("Requesting a key frame (PLI).");
                    // Off the RTP receive thread, which raised this.
                    _ = Task.Run(() => peerConnection.SendRtcpFeedback(SDPMediaTypesEnum.video,
                        new RTCPFeedback(localSsrc, remoteSsrc, PSFBFeedbackTypesEnum.PLI)));
                }
            };

            peerConnection.OnTimeout += (mediaType) => logger.LogDebug("Timeout on media {Media}.", mediaType);
            peerConnection.oniceconnectionstatechange += (state) => logger.LogDebug("ICE connection state changed to {State}.", state);
            peerConnection.onconnectionstatechange += (state) =>
            {
                logger.LogDebug("Peer connection state changed to {State}.", state);

                if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.disconnected)
                {
                    videoEP.Dispose();
                }
            };

            return Task.FromResult(peerConnection);
        }

        /// <summary>
        /// Copies a decoded BGR frame into a bitmap this application owns and displays it,
        /// returning the bitmap so it can be re-used for the next frame. Runs on the UI thread.
        /// GDI+'s Format24bppRgb is B, G, R in memory despite its name, so BGR copies in as it is.
        /// </summary>
        private static Bitmap ShowFrame(Bitmap displayBmp, PictureBox picBox, byte[] sample, int width, int height, int stride)
        {
            if (displayBmp == null || displayBmp.Width != width || displayBmp.Height != height)
            {
                var previous = displayBmp;
                displayBmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);

                picBox.Width = width;
                picBox.Height = height;
                picBox.Image = displayBmp;
                previous?.Dispose();
            }

            var bmpData = displayBmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            try
            {
                // GDI+ pads each row to a multiple of four bytes, so the rows are copied one by
                // one unless the strides happen to agree.
                if (stride == bmpData.Stride)
                {
                    Marshal.Copy(sample, 0, bmpData.Scan0, stride * height);
                }
                else
                {
                    for (int row = 0; row < height; row++)
                    {
                        Marshal.Copy(sample, row * stride, bmpData.Scan0 + row * bmpData.Stride, width * 3);
                    }
                }
            }
            finally
            {
                displayBmp.UnlockBits(bmpData);
            }

            picBox.Invalidate();

            return displayBmp;
        }

        /// <summary>
        /// Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
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
