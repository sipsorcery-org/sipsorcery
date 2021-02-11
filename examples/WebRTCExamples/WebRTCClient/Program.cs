//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC client (from a signalling point of view)
// application that is designed to work with the demo server WebRTC applications.
// This program can fulfill the role of the WebRTC enabled Browser for testing.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 29 Sep 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Net;
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

namespace demo
{
    class Program
    {
        private const string REST_SIGNALING_SERVER = "https://sipsorcery.cloud/api/webrtcsignal";
        private const string REST_SIGNALING_MY_USER = "con";
        private const string REST_SIGNALING_THEIR_USER = "bro";

        private static Microsoft.Extensions.Logging.ILogger logger = null;

        private static Form _form;
        private static PictureBox _picBox;

        static async Task Main(string[] args)
        {
            Console.WriteLine("WebRTC Client Test Console");

            logger = AddConsoleLogger();

            CancellationTokenSource cts = new CancellationTokenSource();

            var restPeer = new WebRTCRestSignalingPeer(REST_SIGNALING_SERVER, REST_SIGNALING_MY_USER, REST_SIGNALING_THEIR_USER, CreatePeerConnection);
            await restPeer.Start(cts);

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

            var videoEP = new SIPSorceryMedia.Encoders.VideoEncoderEndPoint();
            //var videoEP = new SIPSorceryMedia.Windows.WindowsEncoderEndPoint();
            //var videoEP = new FFmpegVideoEndPoint();
            videoEP.RestrictFormats(format => format.Codec == VideoCodecsEnum.VP8);

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
                    await windowsAudioEP.StartAudio();
                }
                else if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.failed)
                {
                    await windowsAudioEP.CloseAudio();
                }
            };

            //peerConnection.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) =>
            //{
            //    bool hasUseCandidate = msg.Attributes.Any(x => x.AttributeType == STUNAttributeTypesEnum.UseCandidate);
            //    Console.WriteLine($"STUN {msg.Header.MessageType} received from {ep}, use candidate {hasUseCandidate}.");
            //};

            peerConnection.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
            {
                //logger.LogDebug($"RTP {media} pkt received, SSRC {rtpPkt.Header.SyncSource}.");
                if (media == SDPMediaTypesEnum.audio)
                {
                    windowsAudioEP.GotAudioRtp(rep, rtpPkt.Header.SyncSource, rtpPkt.Header.SequenceNumber, rtpPkt.Header.Timestamp, rtpPkt.Header.PayloadType, rtpPkt.Header.MarkerBit == 1, rtpPkt.Payload);
                }
            };

            return Task.FromResult(peerConnection);
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
