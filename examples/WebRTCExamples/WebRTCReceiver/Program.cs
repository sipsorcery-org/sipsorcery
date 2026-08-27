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
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows.Forms;
using CommandLine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Encoders;
using SIPSorceryMedia.FFmpeg;
using WebSocketSharp.Server;

namespace demo
{
    public class Options
    {
        [Option("cert", Required = false,
            HelpText = "Path to a `.pfx` certificate archive for the web socket server listener. Format \"--cert=mycertificate.pfx.")]
        public string WSSCertificate { get; set; }

        [Option("ipv6", Required = false,
            HelpText = "If set the web socket server will listen on IPv6 instead of IPv4.")]
        public bool UseIPv6 { get; set; }

        [Option("noaudio", Required = false,
           HelpText = "If set the an audio track will not be included in the SDP offer.")]
        public bool NoAudio { get; set; }
    }

    class Program
    {
        private const string ffmpegLibFullPath = @"C:\ffmpeg-4.4.1-full_build-shared\bin"; //  /!\ A valid path to FFmpeg library

        private const string STUN_URL = "stun:stun.sipsorcery.com";
        private const int WEBSOCKET_PORT = 8081;
        private const int VIDEO_INITIAL_WIDTH = 640;
        private const int VIDEO_INITIAL_HEIGHT = 480;

        private static Form _form;
        private static PictureBox _picBox;
        private static Bitmap _displayBmp;
        private static Options _options;

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        static void Main(string[] args)
        {
            Console.WriteLine("WebRTC Receive Demo");

            logger = AddConsoleLogger();

            SIPSorceryMedia.FFmpeg.FFmpegInit.Initialise(SIPSorceryMedia.FFmpeg.FfmpegLogLevelEnum.AV_LOG_VERBOSE, null, logger);

            var parseResult = Parser.Default.ParseArguments<Options>(args);
            _options = (parseResult as Parsed<Options>)?.Value;
            X509Certificate2 wssCertificate = (_options.WSSCertificate != null) ? LoadCertificate(_options.WSSCertificate) : null;

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            var webSocketServer = new WebSocketServer((_options.UseIPv6) ? IPAddress.IPv6Any : IPAddress.Any, WEBSOCKET_PORT, wssCertificate != null);
            if (webSocketServer.IsSecure)
            {
                webSocketServer.SslConfiguration.ServerCertificate = wssCertificate;
                webSocketServer.SslConfiguration.CheckCertificateRevocation = false;
                webSocketServer.SslConfiguration.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12;
            }
            webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/", (peer) => peer.CreatePeerConnection = CreatePeerConnection);
            webSocketServer.Start();

            Console.WriteLine($"Waiting for web socket connections on {(webSocketServer.IsSecure ? "wss" : "ws")}://{webSocketServer.Address}:{webSocketServer.Port}...");

            // Open a Window to display the video feed from the WebRTC peer.
            _form = new Form();
            _form.AutoSize = true;
            _form.BackgroundImageLayout = ImageLayout.Center;
            _picBox = new PictureBox
            {
                Size = new Size(VIDEO_INITIAL_WIDTH, VIDEO_INITIAL_HEIGHT),
                Location = new Point(0, 0),
                Visible = true
            };
            _form.Controls.Add(_picBox);

            Application.EnableVisualStyles();
            Application.Run(_form);
        }

        private static Task<RTCPeerConnection> CreatePeerConnection()
        {
            //var videoEP = new SIPSorceryMedia.Windows.WindowsVideoEndPoint(new VpxVideoEncoder());
            //videoEP.RestrictFormats(format => format.Codec == VideoCodecsEnum.VP8);
            //var videoEP = new SIPSorceryMedia.Windows.WindowsVideoEndPoint(new FFmpegVideoEncoder());
            //videoEP.RestrictFormats(format => format.Codec == VideoCodecsEnum.H264);

            var videoEP = new FFmpegVideoEndPoint();
            videoEP.RestrictFormats(format => format.Codec == VideoCodecsEnum.H264);

            videoEP.OnVideoSinkDecodedSampleFaster += (RawImage rawImage) =>
            {
                if (rawImage.PixelFormat != VideoPixelFormatsEnum.Bgr)
                {
                    logger.LogError($"Cannot display decoded video sample, expected pixel format Bgr but got {rawImage.PixelFormat}.");
                    return;
                }

                unsafe
                {
                    ShowFrame((byte*)rawImage.Sample, rawImage.Width, rawImage.Height, rawImage.Stride);
                }
            };

            videoEP.OnVideoSinkDecodedSample += (byte[] bmp, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat) =>
            {
                if (pixelFormat != VideoPixelFormatsEnum.Bgr)
                {
                    logger.LogError($"Cannot display decoded video sample, expected pixel format Bgr but got {pixelFormat}.");
                    return;
                }

                unsafe
                {
                    fixed (byte* s = bmp)
                    {
                        ShowFrame(s, (int)width, (int)height, (int)(bmp.Length / height));
                    }
                }
            };

            RTCConfiguration config = new RTCConfiguration
            {
                //iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } }
                 X_UseRtpFeedbackProfile = true
            };
            var pc = new RTCPeerConnection(config);

            // Add local receive only tracks. This ensures that the SDP answer includes only the codecs we support.
            if (!_options.NoAudio)
            {
                MediaStreamTrack audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false,
                    new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) }, MediaStreamStatusEnum.RecvOnly);
                pc.addTrack(audioTrack);
            }
            MediaStreamTrack videoTrack = new MediaStreamTrack(videoEP.GetVideoSinkFormats(), MediaStreamStatusEnum.RecvOnly);
            //MediaStreamTrack videoTrack = new MediaStreamTrack(new VideoFormat(96, "VP8", 90000, "x-google-max-bitrate=5000000"), MediaStreamStatusEnum.RecvOnly);
            pc.addTrack(videoTrack);

            pc.OnVideoFrameReceived += videoEP.GotVideoFrame;
            pc.OnVideoFormatsNegotiated += (formats) => videoEP.SetVideoSinkFormat(formats.First());

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
            pc.OnSendReport += (media, sr) => logger.LogDebug($"RTCP Send for {media}\n{sr.GetDebugSummary()}");
            //pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) => logger.LogDebug($"RECV STUN {msg.Header.MessageType} (txid: {msg.Header.TransactionId.HexStr()}) from {ep}.");
            //pc.GetRtpChannel().OnStunMessageSent += (msg, ep, isRelay) => logger.LogDebug($"SEND STUN {msg.Header.MessageType} (txid: {msg.Header.TransactionId.HexStr()}) to {ep}.");
            pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change to {state}.");

            return Task.FromResult(pc);
        }

        /// <summary>
        /// Copies a decoded 24 bits per pixel frame into a bitmap this application owns and displays it.
        /// </summary>
        /// <remarks>
        /// Called on the decoder's thread, not the UI thread, and deliberately so. The sample is only
        /// valid for the duration of the event handler: the decoder converts every frame into the same
        /// buffer, so deferring the copy to the UI thread would read a frame that has already been
        /// overwritten. Only the display of the finished bitmap is marshalled to the form.
        /// 
        /// The bitmap is allocated once and re-used until the frame size changes. Allocating one per
        /// frame costs several times more than the copy itself and, at 1080p and above, produces enough
        /// garbage to stall the application.
        /// 
        /// Note GDI+'s Format24bppRgb is B,G,R in memory despite the name, which is why a Bgr sample
        /// can be copied into it verbatim.
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

        private static X509Certificate2 LoadCertificate(string path)
        {
            if (!File.Exists(path))
            {
                logger.LogWarning($"No certificate file could be found at {path}.");
                return null;
            }
            else
            {
                X509Certificate2 cert = new X509Certificate2(path, "", X509KeyStorageFlags.Exportable);
                if (cert == null)
                {
                    logger.LogWarning($"Failed to load X509 certificate from file {path}.");
                }
                else
                {
                    logger.LogInformation($"Certificate file successfully loaded {cert.Subject}, thumbprint {cert.Thumbprint}, has private key {cert.HasPrivateKey}.");
                }
                return cert;
            }
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
