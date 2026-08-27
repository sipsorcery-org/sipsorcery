//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Example of connecting to the Janus WebRTC Server and creating a
// WebRTC echo connection.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 04 Feb 2021	Aaron Clauson	Created, Dublin, Ireland.
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
using RestSharp;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Encoders;

namespace demo
{
    class Program
    {
        private const string JANUS_BASE_URI = "http://192.168.0.39:8088/janus/";
        //private const string WEBRTC_SIGNALING_JANUS_URL = "https://localhost:5001/api/webrtcsignal/janus";
        private const string WEBRTC_SIGNALING_JANUS_URL = "https://sipsorcery.cloud/api/webrtcsignal/janus";
        private static int VIDEO_FRAME_WIDTH = 640;
        private static int VIDEO_FRAME_HEIGHT = 480;

        private static bool _useJanusRest = false;

        static async Task Main()
        {
            Console.WriteLine("Janus Echo Test Demo");

            AddConsoleLogger();

            CancellationTokenSource cts = new CancellationTokenSource();
            bool isFormActivated = false;

            #region Set up a simple Windows Form with two picture boxes. 

            var form = new Form();
            Bitmap localVideoBmp = null;
            Bitmap remoteVideoBmp = null;
            form.AutoSize = true;
            form.BackgroundImageLayout = ImageLayout.Center;
            var localVideoPicBox = new PictureBox
            {
                Size = new Size(VIDEO_FRAME_WIDTH, VIDEO_FRAME_HEIGHT),
                Location = new Point(0, 0),
                Visible = true
            };
            var remoteVideoPicBox = new PictureBox
            {
                Size = new Size(VIDEO_FRAME_WIDTH, VIDEO_FRAME_HEIGHT),
                Location = new Point(0, VIDEO_FRAME_HEIGHT),
                Visible = true
            };
            form.Controls.Add(localVideoPicBox);
            form.Controls.Add(remoteVideoPicBox);

            Application.EnableVisualStyles();
            ThreadPool.QueueUserWorkItem(delegate { Application.Run(form); });
            form.FormClosing += (sender, e) => isFormActivated = false;
            form.Activated += (sender, e) => isFormActivated = true;

            #endregion

            Console.WriteLine("Creating peer connection.");
            RTCPeerConnection pc = new RTCPeerConnection(null);

            var videoSource = new VideoTestPatternSource(new VpxVideoEncoder());
            var videoSink = new VideoEncoderEndPoint();

            MediaStreamTrack videoTrack = new MediaStreamTrack(videoSink.GetVideoSourceFormats(), MediaStreamStatusEnum.SendRecv);
            pc.addTrack(videoTrack);
            pc.OnVideoFrameReceived += videoSink.GotVideoFrame;
            videoSource.OnVideoSourceEncodedSample += pc.SendVideo;

            pc.OnVideoFormatsNegotiated += (formats) =>
            {
                videoSink.SetVideoSourceFormat(formats.First());
                videoSource.SetVideoSourceFormat(formats.First());
            };
            pc.OnTimeout += (mediaType) => Console.WriteLine($"Peer connection timeout on media {mediaType}.");
            pc.oniceconnectionstatechange += (state) => Console.WriteLine($"ICE connection state changed to {state}.");
            pc.onconnectionstatechange += async (state) =>
            {
                Console.WriteLine($"Peer connection connected changed to {state}.");

                if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.failed)
                {
                    await videoSource.CloseVideo().ConfigureAwait(false);
                    videoSource.Dispose();
                }
            };

            #region Wire up the video source and sink to the picutre boxes.

            videoSource.OnVideoSourceRawSample += (uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat) =>
            {
                if (isFormActivated && form.Handle != IntPtr.Zero)
                {
                    unsafe
                    {
                        fixed (byte* s = sample)
                        {
                            localVideoBmp = ShowFrame(localVideoBmp, form, localVideoPicBox, s, width, height, width * 3);
                        }
                    }
                }
            };

            videoSink.OnVideoSinkDecodedSample += (byte[] bmp, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat) =>
            {
                if (isFormActivated && form.Handle != IntPtr.Zero)
                {
                    unsafe
                    {
                        fixed (byte* s = bmp)
                        {
                            remoteVideoBmp = ShowFrame(remoteVideoBmp, form, remoteVideoPicBox, s, (int)width, (int)height, (int)(bmp.Length / height));
                        }
                    }
                }
            };

            #endregion

            var offer = pc.CreateOffer(null);
            await pc.setLocalDescription(new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = offer.ToString() }).ConfigureAwait(false);
            Console.WriteLine($"SDP Offer: {pc.localDescription.sdp}");

            await videoSource.StartVideo().ConfigureAwait(false);

            if (_useJanusRest)
            {
                JanusRestClient janusClient = new JanusRestClient(
                    JANUS_BASE_URI,
                    SIPSorcery.LogFactory.CreateLogger<JanusRestClient>(),
                    cts.Token);

                //var serverInfo = await janusClient.GetServerInfo().ConfigureAwait(false);
                //Console.WriteLine($"Name={serverInfo.name}.");
                //Console.WriteLine($"Version={serverInfo.version}.");

                janusClient.OnJanusEvent += async (resp) =>
                {
                    if (resp.jsep != null)
                    {
                        Console.WriteLine($"get event jsep={resp.jsep.type}.");

                        Console.WriteLine($"SDP Answer: {resp.jsep.sdp}");
                        var result = pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = resp.jsep.sdp });
                        Console.WriteLine($"SDP Answer: {pc.remoteDescription.sdp}");

                        if (result == SetDescriptionResultEnum.OK)
                        {
                            Console.WriteLine("Starting peer connection.");
                            await pc.Start().ConfigureAwait(false);
                        }
                        else
                        {
                            Console.WriteLine($"Error setting remote SDP description {result}.");
                        }
                    }
                };

                await janusClient.StartSession().ConfigureAwait(false);
                await janusClient.StartEcho(offer.ToString()).ConfigureAwait(false);
            }
            else
            {
                //RestClient signalingClient = new RestClient($"{WEBRTC_SIGNALING_JANUS_URL}?duration=15");
                RestClient signalingClient = new RestClient($"{WEBRTC_SIGNALING_JANUS_URL}");
                var echoTestReq = new RestRequest(string.Empty, Method.Post);
                echoTestReq.AddJsonBody(pc.localDescription.sdp.ToString());
                var echoTestResp = await signalingClient.ExecutePostAsync<string>(echoTestReq).ConfigureAwait(false);

                if (echoTestResp.IsSuccessful)
                {
                    var sdpAnswer = echoTestResp.Data;
                    Console.WriteLine($"SDP Answer: {sdpAnswer}");
                    var result = pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = sdpAnswer });
                    Console.WriteLine($"SDP Answer: {pc.remoteDescription.sdp}");

                    if (result == SetDescriptionResultEnum.OK)
                    {
                        Console.WriteLine("Starting peer connection.");
                        await pc.Start().ConfigureAwait(false);
                    }
                    else
                    {
                        Console.WriteLine($"Error setting remote SDP description {result}.");
                    }
                }
                else
                {
                    Console.WriteLine($"Janus echo test plugin request failed {echoTestResp.ErrorMessage}.");
                }
            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadLine();

            isFormActivated = false;
            cts.Cancel();

            //await janusClient.DestroySession().ConfigureAwait(false);
        }

        /// <summary>
        /// Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        /// <summary>
        /// Copies a decoded 24 bits per pixel frame into a bitmap this application owns and displays it,
        /// returning the bitmap so the caller can hold it for the next frame.
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
        private static unsafe Bitmap ShowFrame(Bitmap displayBmp, Form form, PictureBox picBox, byte* sample, int width, int height, int stride)
        {
            if (displayBmp == null || displayBmp.Width != width || displayBmp.Height != height)
            {
                var previous = displayBmp;
                displayBmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                var created = displayBmp;

                form.BeginInvoke(new Action(() =>
                {
                    picBox.Width = width;
                    picBox.Height = height;
                    picBox.Image = created;
                    previous?.Dispose();
                }));
            }

            var bmpData = displayBmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

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
                displayBmp.UnlockBits(bmpData);
            }

            // Control.Invalidate is not documented as thread safe, so marshal it like any other UI call.
            form.BeginInvoke(new Action(() => picBox.Invalidate()));

            return displayBmp;
        }

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
