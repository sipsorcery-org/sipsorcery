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
using System.Runtime.InteropServices;
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
                    _form.BeginInvoke(new Action(() =>
                        _displayBmp = ShowFrame(_displayBmp, _picBox, bmp, (int)width, (int)height, (int)(bmp.Length / height))));
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
        /// Copies a decoded 24 bits per pixel frame into a bitmap this application owns and displays it,
        /// returning the bitmap so the caller can re-use it for the next frame.
        /// </summary>
        /// <remarks>
        /// Runs on the UI thread. The byte[] overloads hand over a managed array, which unlike
        /// RawImage.Sample stays valid after the callback returns, so the frame can simply be
        /// marshalled across and copied here. Wrapping the array in a Bitmap instead would leave the
        /// picture box pointing at memory the GC is free to move or reclaim.
        ///
        /// The bitmap is allocated once and re-used until the frame size changes. Note GDI+'s
        /// Format24bppRgb is B,G,R in memory despite the name, which is why a Bgr sample copies in
        /// verbatim.
        /// </remarks>
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
                // GDI+ pads each row to a multiple of four bytes, so the strides only agree when the
                // width is a multiple of four. That covers every standard resolution.
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
