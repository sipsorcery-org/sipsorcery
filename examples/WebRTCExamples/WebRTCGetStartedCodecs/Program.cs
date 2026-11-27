//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Sends a test pattern and music to a browser over WebRTC, with the
// video codec, and the library that encodes it, chosen on the command line:
//
//   dotnet run                                    VP8 from vp8.net
//   dotnet run -- --library h264.net              H264 from h264.net
//   dotnet run -- --codec av1                     AV1 from av1.net
//   dotnet run -- --library ffmpeg --codec h265   H265 from FFmpeg
//
// ASP.NET serves the page and the web socket used for signalling, so once it's
// running open http://localhost:8080 and press Start. dotnet run -- --help lists
// the libraries and options. WebRTCGetStarted is the simpler example, with just
// vp8.net.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 12 Sep 2020  Aaron Clauson   Created WebRTCGetStarted, later WebRTCGetStartedLibvpx, Dublin, Ireland.
// 06 Apr 2025  Aaron Clauson   Created WebRTCGetStartedFFmpeg, Dublin, Ireland.
// 27 Nov 2026  Claude          Combined WebRTCGetStartedFFmpeg, WebRTCGetStartedLibvpx and
//                              WebRTCGetStartedH264Net into WebRTCGetStartedCodecs.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;

namespace demo;

class Program
{
    private const string DEFAULT_URL = "http://localhost:8080";

    // The test pattern runs at 30 frames a second, so this is a key frame every two seconds.
    // A browser that loses one also asks for one (see CreatePeerConnection).
    private const int FRAME_RATE = 30;
    private const int KEY_FRAME_INTERVAL = 60;

    // For the libraries that take a bitrate: h264.net, av1.net and ffmpeg.
    private const int BITRATE_KBPS = 1000;

    /// <summary>
    /// A library that can encode the test pattern, and the codecs it encodes. The first codec
    /// is the one it sends when --codec isn't given.
    /// </summary>
    private sealed record Library(string Name, string Description, VideoCodecsEnum[] Codecs, Func<IVideoEncoder> CreateEncoder);

    // Without --library a codec is sent with the first library here that encodes it.
    private static readonly Library[] _libraries =
    [
        new("vp8.net", "SIPSorcery.VP8, managed, no native libraries.",
            [VideoCodecsEnum.VP8],
            () => new Vpx.Net.VP8Codec()),
        new("h264.net", "SIPSorcery.H264, a managed port of OpenH264.",
            [VideoCodecsEnum.H264],
            () => new SIPSorcery.H264.H264Codec
            {
                FrameRate = FRAME_RATE,
                TargetBitrateKbps = BITRATE_KBPS,
                KeyframeIntervalFrames = KEY_FRAME_INTERVAL,
            }),
        new("av1.net", "SIPSorcery.AV1, a managed port of libaom's realtime encoder.",
            [VideoCodecsEnum.AV1],
            () => new SIPSorcery.AV1.AV1VideoEncoder(new Av1rt.Av1EncoderOptions
            {
                FrameRate = FRAME_RATE,
                TargetBitrateKbps = BITRATE_KBPS,
                KeyFrameInterval = KEY_FRAME_INTERVAL,
            })),
        new("ffmpeg", "SIPSorceryMedia.FFmpeg, needs the FFmpeg shared libraries.",
            [VideoCodecsEnum.H264, VideoCodecsEnum.H265, VideoCodecsEnum.VP8, VideoCodecsEnum.VP9, VideoCodecsEnum.AV1],
            () =>
            {
                var encoder = new FFmpegVideoEncoder();
                encoder.SetBitrate(BITRATE_KBPS * 1000, null, null, null);
                return encoder;
            }),
    ];

    private static Microsoft.Extensions.Logging.ILogger _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("WebRTC Get Started Codecs");

        if (args.Any(a => a is "-h" or "--help" or "-?" or "/?"))
        {
            Console.WriteLine(Usage());
            return 0;
        }

        // ASP.NET's configuration reads the options from the command line (--library h264.net or
        // --library=h264.net), and also from environment variables and appsettings.json.
        var builder = WebApplication.CreateBuilder(args);
        var config = builder.Configuration;

        if (!TryChooseLibrary(config["library"], config["codec"], out var library, out var codec, out string error))
        {
            Console.Error.WriteLine(error);
            Console.WriteLine();
            Console.WriteLine(Usage());
            return 1;
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();
        builder.Host.UseSerilog();

        var factory = new SerilogLoggerFactory(Log.Logger);
        SIPSorcery.LogFactory.Set(factory);
        _logger = factory.CreateLogger<Program>();

        if (library.Name == "ffmpeg")
        {
            try
            {
                FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_WARNING, config["ffmpeg-path"], _logger);
            }
            catch (Exception excp)
            {
                _logger.LogError("Could not load FFmpeg: {Error} Install its shared libraries or pass --ffmpeg-path.", excp.Message);
                return 1;
            }
        }

        var iceServers = new List<RTCIceServer>();
        if (!string.IsNullOrWhiteSpace(config["stun"]))
        {
            iceServers.Add(ParseIceServer(config["stun"]!));
        }
        if (!string.IsNullOrWhiteSpace(config["turn"]))
        {
            iceServers.Add(ParseIceServer(config["turn"]!));
        }
        int bindPort = config.GetValue("bind-port", 0);

        // --urls, or ASPNETCORE_URLS (which dotnet run sets from Properties/launchSettings.json),
        // picks where the web server listens.
        if (string.IsNullOrWhiteSpace(config["urls"]))
        {
            builder.WebHost.UseUrls(DEFAULT_URL);
        }

        var app = builder.Build();

        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseWebSockets();

        // The page shows what it's being sent.
        app.MapGet("/config", () => new { library = library.Name, codec = codec.ToString() });

        Task<RTCPeerConnection> createPeerConnection(RTCConfiguration pcConfig) =>
            CreatePeerConnection(pcConfig, iceServers, bindPort, library, codec);

        app.Map("/ws", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var webSocket = await context.WebSockets.AcceptWebSocketAsync();

            var webSocketPeer = new WebRTCWebSocketPeerAspNet(
                webSocket,
                createPeerConnection,
                new RTCConfiguration { X_ICEIncludeAllInterfaceAddresses = true },
                RTCSdpType.offer);

            await webSocketPeer.Run();

            _logger.LogDebug("Web socket closed with the peer connection {State}.", webSocketPeer.RTCPeerConnection?.connectionState);
        });

        app.Lifetime.ApplicationStarted.Register(() =>
            _logger.LogInformation("Sending {Codec} from {Library}. Open {Urls} in a browser and press Start.",
                codec, library.Name, string.Join(" or ", app.Urls)));

        await app.RunAsync();
        return 0;
    }

    private static Task<RTCPeerConnection> CreatePeerConnection(RTCConfiguration config, List<RTCIceServer> iceServers,
        int bindPort, Library library, VideoCodecsEnum codec)
    {
        config.iceServers = iceServers;
        var pc = new RTCPeerConnection(config, bindPort);

        // The test pattern source passes each frame to the encoder and raises the encoded frames.
        var encoder = library.CreateEncoder();
        var testPattern = new VideoTestPatternSource(encoder);
        var audioSource = new AudioExtrasSource(new AudioEncoder(), new AudioSourceOptions { AudioSource = AudioSourcesEnum.Music });

        // Offer just the chosen codec, with the encoder's own format parameters (the H264
        // profile-level-id, for example), so the browser can't pick one the encoder doesn't send.
        var videoFormats = encoder.SupportedFormats.Where(f => f.Codec == codec).ToList();
        pc.addTrack(new MediaStreamTrack(videoFormats, MediaStreamStatusEnum.SendOnly));
        pc.addTrack(new MediaStreamTrack(audioSource.GetAudioSourceFormats(), MediaStreamStatusEnum.SendOnly));

        testPattern.OnVideoSourceEncodedSample += pc.SendVideo;
        audioSource.OnAudioSourceEncodedSample += pc.SendAudio;

        bool videoNegotiated = false;
        pc.OnVideoFormatsNegotiated += (formats) =>
        {
            var format = formats.First();
            videoNegotiated = true;
            _logger.LogInformation("Video format negotiated: {Codec}, payload type {FormatID} {Parameters}.", format.Codec, format.FormatID, format.Parameters);
            testPattern.SetVideoSourceFormat(format);
        };
        pc.OnAudioFormatsNegotiated += (formats) => audioSource.SetAudioSourceFormat(formats.First());

        // A browser that joins late or loses data asks for a key frame with an RTCP PLI (or FIR).
        pc.OnReceiveReport += (ep, media, report) =>
        {
            if (report.FeedbackMessages.Any(f => f.Header.PacketType == RTCPReportTypesEnum.PSFB &&
                    (f.Header.PayloadFeedbackMessageType == PSFBFeedbackTypesEnum.PLI ||
                     f.Header.PayloadFeedbackMessageType == PSFBFeedbackTypesEnum.FIR)))
            {
                _logger.LogDebug("The browser asked for a key frame.");
                testPattern.ForceKeyFrame();
            }
        };

        pc.onsignalingstatechange += () =>
        {
            if (pc.signalingState == RTCSignalingState.have_local_offer)
            {
                _logger.LogDebug("Local SDP offer:\n{Sdp}", pc.localDescription.sdp);
            }
            else if (pc.signalingState == RTCSignalingState.stable)
            {
                _logger.LogDebug("Remote SDP answer:\n{Sdp}", pc.remoteDescription.sdp);
            }
        };

        pc.oniceconnectionstatechange += (state) => _logger.LogDebug("ICE connection state changed to {State}.", state);

        pc.onconnectionstatechange += async (state) =>
        {
            _logger.LogDebug("Peer connection state changed to {State}.", state);

            if (state == RTCPeerConnectionState.connected)
            {
                if (!videoNegotiated)
                {
                    _logger.LogWarning("The browser didn't accept {Codec}, so only audio is sent. The page's codec list shows what it can receive.", codec);
                }

                await audioSource.StartAudio();
                await testPattern.StartVideo();
            }
            else if (state == RTCPeerConnectionState.failed)
            {
                pc.Close("ice disconnection");
            }
            else if (state == RTCPeerConnectionState.closed)
            {
                await testPattern.CloseVideo();
                await audioSource.CloseAudio();
                // Disposes the encoder too.
                testPattern.Dispose();
            }
        };

        return Task.FromResult(pc);
    }

    /// <summary>
    /// Picks the library and codec from --library and --codec. Either may be left out: a library
    /// sends its first codec, and a codec is sent by the first library that encodes it.
    /// </summary>
    private static bool TryChooseLibrary(string? libraryArg, string? codecArg, out Library library, out VideoCodecsEnum codec, out string error)
    {
        library = _libraries[0];
        codec = library.Codecs[0];
        error = string.Empty;

        var allCodecs = _libraries.SelectMany(l => l.Codecs).Distinct().ToList();
        VideoCodecsEnum? wantCodec = null;

        if (!string.IsNullOrWhiteSpace(codecArg))
        {
            wantCodec = allCodecs.Cast<VideoCodecsEnum?>().FirstOrDefault(c => c.ToString()!.Equals(codecArg, StringComparison.OrdinalIgnoreCase));
            if (wantCodec == null)
            {
                error = $"Unknown --codec {codecArg}. Expected {string.Join(", ", allCodecs.Select(c => c.ToString().ToLowerInvariant()))}.";
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(libraryArg))
        {
            var named = _libraries.FirstOrDefault(l => l.Name.Equals(libraryArg, StringComparison.OrdinalIgnoreCase));
            if (named == null)
            {
                error = $"Unknown --library {libraryArg}. Expected {string.Join(", ", _libraries.Select(l => l.Name))}.";
                return false;
            }
            if (wantCodec != null && !named.Codecs.Contains(wantCodec.Value))
            {
                error = $"{named.Name} doesn't encode {wantCodec}. It encodes {string.Join(", ", named.Codecs)}.";
                return false;
            }
            library = named;
        }
        else if (wantCodec != null)
        {
            library = _libraries.First(l => l.Codecs.Contains(wantCodec.Value));
        }

        codec = wantCodec ?? library.Codecs[0];
        return true;
    }

    private static string Usage()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Usage: dotnet run -- [--library <name>] [--codec <name>] [options]");
        sb.AppendLine();
        sb.AppendLine("  --library <name>    The library that encodes the video. The default is vp8.net, or with");
        sb.AppendLine("                      --codec the first one below that encodes it:");
        foreach (var library in _libraries)
        {
            sb.AppendLine($"                        {library.Name,-9} {string.Join(", ", library.Codecs),-26} {library.Description}");
        }
        sb.AppendLine("  --codec <name>      The video codec. The default is the library's first one above.");
        sb.AppendLine($"  --urls <url>        Where the web server listens. The default is {DEFAULT_URL}.");
        sb.AppendLine("  --stun <url>        A STUN server for the ICE candidates, e.g. stun:stun.cloudflare.com.");
        sb.AppendLine("  --turn <url>        A TURN server, as url;username;password.");
        sb.AppendLine("  --bind-port <port>  The UDP port for the media. The default lets the OS choose.");
        sb.AppendLine("  --ffmpeg-path <dir> Where the FFmpeg shared libraries are, if they aren't found on their own.");
        return sb.ToString();
    }

    private static RTCIceServer ParseIceServer(string server)
    {
        var fields = server.Split(';');

        return new RTCIceServer
        {
            urls = fields[0],
            username = fields.Length > 1 ? fields[1] : null,
            credential = fields.Length > 2 ? fields[2] : null,
            credentialType = RTCIceCredentialType.password
        };
    }
}
