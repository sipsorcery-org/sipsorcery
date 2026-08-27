//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A command line application that can make or receive a video
// call. The application is designed to be used as a pair where one instance
// places the call and the other instance answers.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 23 Dec 2020	Aaron Clauson	Created, Dublin, Ireland.
// 30 Sep 2024  Aaron Clauson   Broken :(. Needs investigation.
// 15 Oct 2024  Aaron Clauson   Fixed. Was just missing FFmpeg initialise call.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Usage - with testpatterns:
// Listening application:
// dotnet run -- --tp
//
// Calling application:
// dotnet run --dst=127.0.0.1:5080 --tp
//
// Usage - with two webcams:
// Listening application:
// dotnet run
//
// Calling application:
// dotnet run --dst=127.0.0.1:5080 --cam=1
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CommandLine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;
using SIPSorceryMedia.Windows;

namespace demo
{
    public class Options
    {
        [Option("dst", Required = false,
            HelpText = "The SIP URI to call. Format \"--dst=sip:3333@sip2sip.info\".")]
        public string CallDestination { get; set; }

        [Option("cam", Required = false,
           HelpText = "If set specifies the index of the wecam to use. Only useful for systems with multiple webcams. Format \"--cam=1\".")]
        public int? WebcamIndex { get; set; }

        [Option("listcams", Required = false,
           HelpText = "If set will list the available webcams and exit. Format \"--listcams\".")]
        public bool ListCameras { get; set; }

        [Option("listformats", Required = false, Default = null,
           HelpText = "If set will list the available video formats for a webcam and exit. Format \"--listformats=0\".")]
        public int? ListFormats { get; set; }

        [Option("tp", Required = false, Default = true,
           HelpText = "If set will use a test pattern source instead of a webcam feed. Format \"--tp\".")]
        public bool TestPattern { get; set; }
        
        [Option("noaudio", Required = false, Default =false,
            HelpText = "If set will exclude the audio stream from the call. Format \"--noaudio\".")]
        public bool NoAudio { get; set; }
    }

    public class DecoderVideoSink : IVideoSink
    {
        public static readonly List<VideoFormat> SupportedFormats = new List<VideoFormat>();
        private IVideoEncoder _videoDecoder;
        private MediaFormatManager<VideoFormat> _formatManager;

        public event VideoSinkSampleDecodedDelegate OnVideoSinkDecodedSample;
#pragma warning disable CS0067
        public event VideoSinkSampleDecodedFasterDelegate OnVideoSinkDecodedSampleFaster;
#pragma warning restore CS0067

        public DecoderVideoSink(IVideoEncoder videoDecoder)
        {
            _videoDecoder = videoDecoder;
            _formatManager = new MediaFormatManager<VideoFormat>(videoDecoder.SupportedFormats);
        }

        public Task CloseVideoSink() => Task.CompletedTask;
        public Task StartVideoSink() => Task.CompletedTask;
        public Task PauseVideoSink() => Task.CompletedTask;
        public Task ResumeVideoSink() => Task.CompletedTask;

        public void RestrictFormats(Func<VideoFormat, bool> filter) => _formatManager.RestrictFormats(filter);
        public List<VideoFormat> GetVideoSinkFormats() => _formatManager.GetSourceFormats();
        public void SetVideoSinkFormat(VideoFormat videoFormat) => _formatManager.SetSelectedFormat(videoFormat);
        public void GotVideoRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, bool marker, byte[] payload) =>
             throw new ApplicationException("This Video End Point requires full video frames rather than individual RTP packets.");

        public void GotVideoFrame(IPEndPoint remoteEndPoint, uint timestamp, byte[] frame, VideoFormat format)
        {
            if (OnVideoSinkDecodedSample != null)
            {
                try
                {
                    foreach (var decoded in _videoDecoder.DecodeVideo(frame, VideoPixelFormatsEnum.Bgr, format.Codec))
                    {
                        OnVideoSinkDecodedSample(decoded.Sample, decoded.Width, decoded.Height, (int)(decoded.Width * 3), VideoPixelFormatsEnum.Bgr);
                    }
                }
                catch(Exception excp)
                {
                    Console.WriteLine($"Exception decoding video. {excp.Message}");
                }
            }
        }
    }

    class Program
    {
        private static int SIP_PORT_DEFAULT = 5080;
        private static int CALL_TIMEOUT_SECONDS = 20;
        private static int VIDEO_FRAME_WIDTH = 640;
        private static int VIDEO_FRAME_HEIGHT = 480;
        private const uint MAXIMUM_VIDEO_BANDWIDTH = 5000000; // 5Mbps.
        private const VideoCodecsEnum VIDEO_CODEC = VideoCodecsEnum.VP8; // Supported options are H264 or VP8.

        private static Microsoft.Extensions.Logging.ILogger Log = NullLogger.Instance;

        private static SIPTransport _sipTransport;
        private static Form _form;
        private static bool _isFormActivated;
        private static PictureBox _remoteVideoPicBox;
        private static PictureBox _localVideoPicBox;
        private static Bitmap _remoteVideoBmp;
        private static Bitmap _localVideoBmp;
        private static Options _options;

        static async Task Main(string[] args)
        {
            Console.WriteLine("SIPSorcery Video Phone Command Line Demo");
            Console.WriteLine("Press ctrl-c to exit.");

            Log = AddConsoleLogger();
            ManualResetEvent exitMRE = new ManualResetEvent(false);
            ManualResetEvent waitForCallMre = new ManualResetEvent(false);

            SIPSorceryMedia.FFmpeg.FFmpegInit.Initialise(SIPSorceryMedia.FFmpeg.FfmpegLogLevelEnum.AV_LOG_VERBOSE, null, Log);

            var parseResult = Parser.Default.ParseArguments<Options>(args);
            _options = (parseResult as Parsed<Options>)?.Value;

            if (parseResult.Tag != ParserResultType.NotParsed)
            {
                if (_options.ListCameras)
                {
                    #region List webcams.

                    var webcams = await WindowsVideoEndPoint.GetVideoCatpureDevices();
                    if (webcams == null || webcams.Count == 0)
                    {
                        Console.WriteLine("No webcams were found.");
                    }
                    else
                    {
                        var index = 0;
                        foreach (var webcam in webcams)
                        {
                            Console.WriteLine($"{index}: \"{webcam.Name}\", use --cam={index}.");
                            index++;
                        }
                    }

                    #endregion
                }
                else if (_options.ListFormats != null)
                {
                    #region List webcam formats.

                    var webcams = await WindowsVideoEndPoint.GetVideoCatpureDevices();
                    if (webcams == null || webcams.Count == 0)
                    {
                        Console.WriteLine("No webcams were found.");
                    }
                    else if (_options.ListFormats >= webcams.Count)
                    {
                        Console.WriteLine($"No webcam available for index {_options.ListFormats}.");
                    }
                    else
                    {
                        string webcamName = webcams[_options.ListFormats.Value].Name;
                        var formats = await WindowsVideoEndPoint.GetDeviceFrameFormats(webcamName);

                        Console.WriteLine($"Video frame formats for {webcamName}.");
                        foreach (var vidFmt in formats)
                        {
                            float vidFps = vidFmt.MediaFrameFormat.FrameRate.Numerator / vidFmt.MediaFrameFormat.FrameRate.Denominator;
                            string pixFmt = vidFmt.MediaFrameFormat.Subtype == WindowsVideoEndPoint.MF_I420_PIXEL_FORMAT ? "I420" : vidFmt.MediaFrameFormat.Subtype;
                            Console.WriteLine($"{vidFmt.Width}x{vidFmt.Height} {vidFps:0.##}fps {pixFmt}");
                        }
                    }

                    #endregion
                }
                else
                {
                    string webcamName = null;

                    if (_options.WebcamIndex != null)
                    {
                        var webcams = await WindowsVideoEndPoint.GetVideoCatpureDevices();
                        if (webcams == null || webcams.Count == 0)
                        {
                            Console.WriteLine("No webcams were found.");
                            Application.Exit();
                        }
                        else if (webcams.Count < _options.WebcamIndex)
                        {
                            Console.WriteLine($"No webcam available for index {_options.WebcamIndex}.");
                            Application.Exit();
                        }
                        else
                        {
                            webcamName = webcams[_options.WebcamIndex.Value].Name;
                            Console.WriteLine($"Using webcam {webcamName}.");
                        }
                    }

                    _sipTransport = new SIPTransport();

                    if (string.IsNullOrEmpty(_options.CallDestination))
                    {
                        // We haven't been asked to place a call so we're listening.
                        IPAddress listenAddress = (System.Net.Sockets.Socket.OSSupportsIPv6) ? IPAddress.IPv6Any : IPAddress.Any;
                        var listenEndPoint = new IPEndPoint(listenAddress, SIP_PORT_DEFAULT);

                        try
                        {
                            SIPUDPChannel udpChannel = new SIPUDPChannel(listenEndPoint, true);
                            _sipTransport.AddSIPChannel(udpChannel);
                        }
                        catch (ApplicationException appExcp)
                        {
                            Console.WriteLine($"Failed to create UDP SIP channel on {listenEndPoint}, error {appExcp.Message}.");
                            SIPUDPChannel udpChannel = new SIPUDPChannel(new IPEndPoint(listenAddress, 0), true);
                            _sipTransport.AddSIPChannel(udpChannel);
                        }

                        var listeningEP = _sipTransport.GetSIPChannels().First().ListeningSIPEndPoint;
                        Console.WriteLine($"Listening for incoming call on {listeningEP}.");
                    }

                    EnableTraceLogs(_sipTransport);

                    // Open a window to display the video feed from the remote SIP party.
                    _form = new Form();
                    _form.Text = string.IsNullOrEmpty(_options.CallDestination) ? "Listener" : "Caller";
                    _form.AutoSize = true;
                    _form.BackgroundImageLayout = ImageLayout.Center;
                    _localVideoPicBox = new PictureBox
                    {
                        Size = new Size(VIDEO_FRAME_WIDTH, VIDEO_FRAME_HEIGHT),
                        Location = new Point(0, 0),
                        Visible = true
                    };
                    _remoteVideoPicBox = new PictureBox
                    {
                        Size = new Size(VIDEO_FRAME_WIDTH, VIDEO_FRAME_HEIGHT),
                        Location = new Point(0, VIDEO_FRAME_HEIGHT),
                        Visible = true
                    };
                    _form.Controls.Add(_localVideoPicBox);
                    _form.Controls.Add(_remoteVideoPicBox);

                    var userAgent = new SIPUserAgent(_sipTransport, null, true);
                    userAgent.OnCallHungup += (dialog) => exitMRE.Set();

                    WindowsAudioEndPoint windowsAudioEndPoint = null;
                    if (!_options.NoAudio)
                    {
                        windowsAudioEndPoint = new WindowsAudioEndPoint(new AudioEncoder());
                        windowsAudioEndPoint.RestrictFormats(x => x.Codec == AudioCodecsEnum.PCMU || x.Codec == AudioCodecsEnum.PCMA);
                    }

                    MediaEndPoints mediaEndPoints = null;

                    if (_options.TestPattern && _options.WebcamIndex == null)
                    {
                        var testPattern = new VideoTestPatternSource(new FFmpegVideoEncoder());
                        var decoderSink = new DecoderVideoSink(new FFmpegVideoEncoder());
                        //var decoderSink = new DecoderVideoSink(new VpxVideoEncoder());

                        testPattern.RestrictFormats(format => format.Codec == VIDEO_CODEC);
                        decoderSink.RestrictFormats(format => format.Codec == VIDEO_CODEC);

                        mediaEndPoints = new MediaEndPoints
                        {
                            AudioSink = windowsAudioEndPoint,
                            AudioSource = windowsAudioEndPoint,
                            VideoSink = decoderSink,
                            VideoSource = testPattern,
                        };
                    }
                    else
                    {
                        WindowsVideoEndPoint windowsVideoEndPoint = webcamName switch
                        {
                            null => new WindowsVideoEndPoint(new FFmpegVideoEncoder()),
                            _ => new WindowsVideoEndPoint(new FFmpegVideoEncoder(), webcamName),
                        };
                        windowsVideoEndPoint.RestrictFormats(format => format.Codec == VIDEO_CODEC);

                        mediaEndPoints = new MediaEndPoints
                        {
                            AudioSink = windowsAudioEndPoint,
                            AudioSource = windowsAudioEndPoint,
                            VideoSink = windowsVideoEndPoint,
                            VideoSource = windowsVideoEndPoint,
                        };
                    }

                    mediaEndPoints.VideoSource.OnVideoSourceRawSample += (uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat) =>
                    {
                        if (_isFormActivated && _form.Handle != IntPtr.Zero)
                        {
                            int stride = width * 3;
                            if (pixelFormat == VideoPixelFormatsEnum.I420)
                            {
                                sample = PixelConverter.I420toBGR(sample, width, height, out stride);
                            }

                            _form.BeginInvoke(new Action(() =>
                                _localVideoBmp = ShowFrame(_localVideoBmp, _localVideoPicBox, sample, width, height, stride)));
                        }
                    };

                    Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
                    {
                        e.Cancel = true;
                        Log.LogInformation("Exiting...");
                        waitForCallMre.Set();
                        exitMRE.Set();
                    };

                    if (string.IsNullOrEmpty(_options.CallDestination))
                    {
                        ActivateForm();

                        userAgent.OnIncomingCall += async (ua, req) =>
                        {
                            var voipMediaSession = new VoIPMediaSession(mediaEndPoints);
                            voipMediaSession.AcceptRtpFromAny = true;
                            if (voipMediaSession.VideoLocalTrack != null)
                            {
                                voipMediaSession.VideoLocalTrack.MaximumBandwidth = MAXIMUM_VIDEO_BANDWIDTH;
                            }

                            var uas = userAgent.AcceptCall(req);
                            await userAgent.Answer(uas, voipMediaSession);

                            Console.WriteLine("Starting local video source...");
                            await mediaEndPoints.VideoSource.StartVideo().ConfigureAwait(false);

                            waitForCallMre.Set();
                        };

                        Console.WriteLine("Waiting for incoming call...");
                        waitForCallMre.WaitOne();
                    }
                    else
                    {
                        var voipMediaSession = new VoIPMediaSession(mediaEndPoints);
                        voipMediaSession.AcceptRtpFromAny = true;
                        if (voipMediaSession.VideoLocalTrack != null)
                        {
                            voipMediaSession.VideoLocalTrack.MaximumBandwidth = MAXIMUM_VIDEO_BANDWIDTH;
                        }

                        ActivateForm();

                        Console.WriteLine("Starting local video source...");
                        await mediaEndPoints.VideoSource.StartVideo().ConfigureAwait(false);

                        // Place the call and wait for the result.
                        Task<bool> callTask = userAgent.Call(_options.CallDestination, null, null, voipMediaSession);
                        callTask.Wait(CALL_TIMEOUT_SECONDS * 1000);
                    }

                    if (userAgent.IsCallActive)
                    {
                        Log.LogInformation("Call attempt successful.");
                        mediaEndPoints.VideoSink.OnVideoSinkDecodedSample += (byte[] bmp, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat) =>
                        {
                            if (_isFormActivated && _form.Handle != IntPtr.Zero)
                            {
                                _form.BeginInvoke(new Action(() =>
                                    _remoteVideoBmp = ShowFrame(_remoteVideoBmp, _remoteVideoPicBox, bmp, (int)width, (int)height, stride)));
                            }
                        };
                    }
                    else
                    {
                        Log.LogWarning("Call attempt failed.");
                        Console.WriteLine("Press ctrl-c to exit.");
                    }

                    exitMRE.WaitOne();

                    if (userAgent.IsCallActive)
                    {
                        Log.LogInformation("Hanging up.");
                        userAgent.Hangup();
                    }

                    Task.Delay(1000).Wait();

                    // Clean up.
                    if (_form.Handle != IntPtr.Zero)
                    {
                        _form.BeginInvoke(new Action(() => _form.Close()));
                    }
                    _sipTransport.Shutdown();
                }
            }
        }

        private static void ActivateForm()
        {
            Application.EnableVisualStyles();
            ThreadPool.QueueUserWorkItem(delegate { Application.Run(_form); });
            _form.Activated += (object sender, EventArgs e) => _isFormActivated = true;
        }

        /// <summary>
        /// Enable detailed SIP log messages.
        /// </summary>
        private static void EnableTraceLogs(SIPTransport sipTransport)
        {
            sipTransport.SIPRequestInTraceEvent += (localEP, remoteEP, req) =>
            {
                Log.LogDebug($"Request received: {localEP}<-{remoteEP}");
                Log.LogDebug(req.ToString());
            };

            sipTransport.SIPRequestOutTraceEvent += (localEP, remoteEP, req) =>
            {
                Log.LogDebug($"Request sent: {localEP}->{remoteEP}");
                Log.LogDebug(req.ToString());
            };

            sipTransport.SIPResponseInTraceEvent += (localEP, remoteEP, resp) =>
            {
                Log.LogDebug($"Response received: {localEP}<-{remoteEP}");
                Log.LogDebug(resp.ToString());
            };

            sipTransport.SIPResponseOutTraceEvent += (localEP, remoteEP, resp) =>
            {
                Log.LogDebug($"Response sent: {localEP}->{remoteEP}");
                Log.LogDebug(resp.ToString());
            };

            sipTransport.SIPRequestRetransmitTraceEvent += (tx, req, count) =>
            {
                Log.LogDebug($"Request retransmit {count} for request {req.StatusLine}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds.ToString("0.###")}s ago.");
            };

            sipTransport.SIPResponseRetransmitTraceEvent += (tx, resp, count) =>
            {
                Log.LogDebug($"Response retransmit {count} for response {resp.ShortDescription}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds.ToString("0.###")}s ago.");
            };
        }

        /// <summary>
        /// Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
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
