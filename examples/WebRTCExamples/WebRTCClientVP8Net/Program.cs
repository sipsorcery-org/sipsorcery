//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC client (from a signalling point of view)
// application that is designed to work with the demo server WebRTC applications.
// This program can fulfil the role of the WebRTC enabled Browser for testing.
// This version differs from the WebRTCClient demo in that it uses the .NET
// VP8 ported decoder.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 28 Jan 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using Vpx.Net;
using WebSocketSharp.Server;

namespace demo
{
    class Program
    {
        //private const string REST_SIGNALING_SERVER = "https://localhost:5001/api/webrtcsignal";
        //private const string REST_SIGNALING_MY_USER = "cli";
        //private const string REST_SIGNALING_THEIR_USER = "svr";
        private const int WEBSOCKET_PORT = 8081;

        private static Microsoft.Extensions.Logging.ILogger logger = null;

        private static Form _form;
        private static PictureBox _picBox;
        private static Bitmap _displayBmp;
        private static bool _isFormActivated;

        static async Task Main(string[] args)
        {
            Console.WriteLine("WebRTC Client Test Console");

            logger = AddConsoleLogger();

            //CancellationTokenSource cts = new CancellationTokenSource();

            //var nodeDssWebRTCPeer = new WebRTCRestSignalingPeer(REST_SIGNALING_SERVER, REST_SIGNALING_MY_USER, REST_SIGNALING_THEIR_USER, CreatePeerConnection);
            //await nodeDssWebRTCPeer.Start(cts);

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            var webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
            webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/", (peer) => peer.CreatePeerConnection = CreatePeerConnection);
            webSocketServer.Start();

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
            _form.FormClosing += (sender, e) => _isFormActivated = false;
            _form.Activated += (sender, e) => _isFormActivated = true;

            Application.EnableVisualStyles();
            Application.Run(_form);
        }

        private static Task<RTCPeerConnection> CreatePeerConnection()
        {
            var peerConnection = new RTCPeerConnection();

            //FileStream captureStream = new FileStream("capture.stm", FileMode.Create, FileAccess.ReadWrite);

            var videoEP = new Vp8NetVideoEncoderEndPoint();

            videoEP.OnVideoSinkDecodedSample += (byte[] bmp, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat) =>
            {
                if (_isFormActivated && _form.Handle != IntPtr.Zero)
                {
                    unsafe
                    {
                        fixed (byte* s = bmp)
                        {
                            ShowFrame(s, (int)width, (int)height, (int)(bmp.Length / height));
                        }
                    }
                }
            };

            // Sink (speaker) only audio end point.
            //WindowsAudioEndPoint windowsAudioEP = new WindowsAudioEndPoint(new AudioEncoder(), -1, -1, true, false);

            //MediaStreamTrack audioTrack = new MediaStreamTrack(windowsAudioEP.GetAudioSinkFormats(), MediaStreamStatusEnum.RecvOnly);
            //peerConnection.addTrack(audioTrack);
            MediaStreamTrack videoTrack = new MediaStreamTrack(videoEP.GetVideoSinkFormats(), MediaStreamStatusEnum.RecvOnly);
            peerConnection.addTrack(videoTrack);

            peerConnection.OnVideoFrameReceived += (rep, ts, frame, pixelFmt) =>
            {
                Console.WriteLine($"Video frame received {frame.Length} bytes.");
                //Console.WriteLine(frame.HexStr());
                //captureStream.Write(Encoding.ASCII.GetBytes($"{frame.Length},"));
                //captureStream.Write(frame);
                //captureStream.Flush();
                videoEP.GotVideoFrame(rep, ts, frame, pixelFmt);
            };
            peerConnection.OnVideoFormatsNegotiated += (formats) =>
                videoEP.SetVideoSinkFormat(formats.First());
            //peerConnection.OnAudioFormatsNegotiated += (formats) =>
            //    windowsAudioEP.SetAudioSinkFormat(formats.First());

            peerConnection.OnTimeout += (mediaType) => logger.LogDebug($"Timeout on media {mediaType}.");
            peerConnection.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state changed to {state}.");
            peerConnection.onconnectionstatechange += (state) =>
            {
                logger.LogDebug($"Peer connection connected changed to {state}.");

                if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.disconnected)
                {
                    //captureStream.Close();
                }
            };

            peerConnection.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) =>
            {
                bool hasUseCandidate = msg.Attributes.Any(x => x.AttributeType == STUNAttributeTypesEnum.UseCandidate);
                Console.WriteLine($"STUN {msg.Header.MessageType} received from {ep}, use candidate {hasUseCandidate}.");
            };

            peerConnection.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
            {
                //logger.LogDebug($"RTP {media} pkt received, SSRC {rtpPkt.Header.SyncSource}.");
                //if (media == SDPMediaTypesEnum.audio)
                //{
                //    windowsAudioEP.GotAudioRtp(rep, rtpPkt.Header.SyncSource, rtpPkt.Header.SequenceNumber, rtpPkt.Header.Timestamp, rtpPkt.Header.PayloadType, rtpPkt.Header.MarkerBit == 1, rtpPkt.Payload);
                //}
            };

            return Task.FromResult(peerConnection);
        }

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        /// <summary>
        /// Copies a decoded 24 bits per pixel frame into a bitmap this application owns and displays it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Called on the decoder's thread, not the UI thread, and deliberately so. The sample is only
        /// valid for the duration of the event handler: the decoder converts every frame into the same
        /// buffer, so deferring the copy to the UI thread would read a frame that has already been
        /// overwritten. Only the display of the finished bitmap is marshalled to the form.
        /// </para>
        /// <para>
        /// The bitmap is allocated once and re-used until the frame size changes. Allocating one per
        /// frame costs several times more than the copy itself and, at 1080p and above, produces enough
        /// garbage to stall the application.
        /// </para>
        /// <para>
        /// Note GDI+'s Format24bppRgb is B,G,R in memory despite the name, which is why a Bgr sample
        /// can be copied into it verbatim.
        /// </para>
        /// </remarks>
        private static unsafe void ShowFrame(byte* sample, int width, int height, int stride)
        {
            if (_displayBmp == null || _displayBmp.Width != width || _displayBmp.Height != height)
            {
                logger.LogDebug($"Adjusting video display to {width}x{height}.");

                var previous = _displayBmp;
                _displayBmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);

                _form.BeginInvoke(new Action(() =>
                {
                    _picBox.Width = width;
                    _picBox.Height = height;
                    _picBox.Image = _displayBmp;
                    previous?.Dispose();
                }));
            }

            var bmpData = _displayBmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            try
            {
                // GDI+ rounds its stride up to a multiple of four while the decoder's is exactly
                // width * 3, so the two agree for any width that is a multiple of four. That covers
                // every standard video resolution and lets the frame go in a single copy, which
                // measures around 30% faster than a row at a time. Widths that are not a multiple
                // of four, which cropped display sizes can produce, still need the row loop.
                if (stride == bmpData.Stride)
                {
                    Buffer.MemoryCopy(sample, (byte*)bmpData.Scan0, (long)bmpData.Stride * height, (long)stride * height);
                }
                else
                {
                    for (int row = 0; row < height; row++)
                    {
                        Buffer.MemoryCopy(sample + row * stride, (byte*)bmpData.Scan0 + row * bmpData.Stride, bmpData.Stride, width * 3);
                    }
                }
            }
            finally
            {
                _displayBmp.UnlockBits(bmpData);
            }

            // Control.Invalidate is not documented as thread safe, so marshal it like any other UI call.
            _form.BeginInvoke(new Action(() => _picBox.Invalidate()));
        }

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
