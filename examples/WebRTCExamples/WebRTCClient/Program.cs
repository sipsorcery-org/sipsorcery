//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC client (from a signalling point of view)
// application that is designed to work with the demo WebRTC TestPatternServer
// application. This program can fulfill the role of the WebRTC enabled Browser
// for testing.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 29 Sep 2020	Aaron Clauson	Created, Dublin, Ireland.
// 31 May 2025  Aaron Clauson   Removed REST server signalling and switched to a simple HTTP POST for SDP exchange.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Windows;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;
using System.Net.Http;

namespace demo
{
    class Program
    {
        // Install with: winget install "FFmpeg (Shared)" 
        private const string ffmpegLibFullPath = null; // @"C:\ffmpeg-4.4.1-full_build-shared\bin"; //  /!\ A valid path to FFmpeg library

        private const string TEST_SERVER_URL_SERVER = "https://localhost:5443/offer";

        private static Microsoft.Extensions.Logging.ILogger logger = null;

        private static Form _form;
        private static PictureBox _picBox;
        private static Bitmap _spareBmp;   // Retired bitmap handed back by the UI thread for re-use.

        static async Task Main(string[] args)
        {
            Console.WriteLine("WebRTC Client Test Console");

            logger = AddConsoleLogger();

            SIPSorceryMedia.FFmpeg.FFmpegInit.Initialise(SIPSorceryMedia.FFmpeg.FfmpegLogLevelEnum.AV_LOG_VERBOSE, ffmpegLibFullPath, logger);

            var pc = await CreatePeerConnection();

            var offerSdp = pc.createOffer();
            await pc.setLocalDescription(offerSdp);

            HttpClient httpClient = new HttpClient();
            var response = await httpClient.PostAsync(TEST_SERVER_URL_SERVER, new StringContent(pc.localDescription.sdp.ToString()));

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError($"Failed to successfully negotiate SDP with server. Status code: {response.StatusCode}");
                pc.Close("SDP negotiation failed.");
                return;
            }

            var sdpAnswer = await response.Content.ReadAsStringAsync();

            logger.LogInformation($"Received SDP answer from server:\n{sdpAnswer}");

            var sdp = SDP.ParseSDPDescription(sdpAnswer);

            if (sdp == null)
            {
                logger.LogError("Failed to parse SDP answer from server.");
                pc.Close("SDP parsing failed.");
                return;
            }

            logger.LogDebug("SDP answer:\n{sdp}", sdp);

            var result = pc.SetRemoteDescription(SIPSorcery.SIP.App.SdpType.answer, sdp);

            if(result != SetDescriptionResultEnum.OK)
            {
                logger.LogError($"Failed to set remote description: {result}");
                pc.Close("Failed to set remote description.");
                return;
            }

            logger.LogInformation($"Set remote description result: {result}.");

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

        private static Task<RTCPeerConnection> CreatePeerConnection()
        {
            var peerConnection = new RTCPeerConnection(null);

            var videoEP = new FFmpegVideoEndPoint();

            videoEP.RestrictFormats(format => format.Codec == VideoCodecsEnum.VP8);
            videoEP.OnVideoSinkDecodedSampleFaster += ShowFrame;

            // Sink (speaker) only audio end point.
            WindowsAudioEndPoint windowsAudioEP = new WindowsAudioEndPoint(new AudioEncoder(), -1, -1, true, false);

            MediaStreamTrack audioTrack = new MediaStreamTrack(windowsAudioEP.GetAudioSinkFormats(), MediaStreamStatusEnum.RecvOnly);
            peerConnection.addTrack(audioTrack);
            MediaStreamTrack videoTrack = new MediaStreamTrack(videoEP.GetVideoSinkFormats(), MediaStreamStatusEnum.RecvOnly);
            peerConnection.addTrack(videoTrack);

            peerConnection.OnVideoFrameReceived += videoEP.GotVideoFrame;
            peerConnection.OnVideoFormatsNegotiated += (formats) =>
                videoEP.SetVideoSinkFormat(formats.First());
            peerConnection.OnAudioFormatsNegotiated += (formats) =>
                windowsAudioEP.SetAudioSinkFormat(formats.First());

            peerConnection.OnTimeout += (mediaType) => logger.LogDebug($"Timeout on media {mediaType}.");
            peerConnection.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state changed to {state}.");
            peerConnection.onconnectionstatechange += async (state) =>
            {
                logger.LogDebug($"Peer connection connected changed to {state}.");

                if (state == RTCPeerConnectionState.connected)
                {
                    await windowsAudioEP.Start();
                }
                else if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.failed)
                {
                    await windowsAudioEP.Close();
                }
            };

            peerConnection.OnAudioFrameReceived += windowsAudioEP.GotEncodedMediaFrame;

            return Task.FromResult(peerConnection);
        }

        private static void ShowFrame(RawImage rawImage)
        {
            if (rawImage.PixelFormat != VideoPixelFormatsEnum.Bgr)
            {
                logger.LogError($"Cannot display decoded video sample, expected pixel format Bgr but got {rawImage.PixelFormat}.");
                return;
            }

            var bmp = Interlocked.Exchange(ref _spareBmp, null);

            int width = rawImage.Width;
            int height = rawImage.Height;

            if (bmp == null || bmp.Width != width || bmp.Height != height)
            {
                bmp?.Dispose();
                bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            }

            var bmpData = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            try
            {
                rawImage.CopyTo(bmpData.Scan0, bmpData.Stride);
            }
            finally
            {
                bmp.UnlockBits(bmpData);
            }

            _form.BeginInvoke(new Action(() =>
            {
                if (_picBox.Width != width || _picBox.Height != height)
                {
                    logger.LogDebug($"Adjusting video display to {width}x{height}.");
                    _picBox.Width = width;
                    _picBox.Height = height;
                }

                var previous = _picBox.Image as Bitmap;
                _picBox.Image = bmp;

                // The previous picure box bitmap is no longer being painted so it can be used for the next frame copy.
                Interlocked.Exchange(ref _spareBmp, previous)?.Dispose();
            }));
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
