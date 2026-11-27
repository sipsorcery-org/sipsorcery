//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Receives a browser's camera, or a test pattern, over WebRTC and
// shows it in a window, with the video codec, and the library that decodes it,
// picked on the page before it connects. vp8.net and h264.net are managed, so
// they always work. FFmpeg, when its shared libraries are installed, adds VP8,
// VP9, H264, H265 and AV1.
//
// ASP.NET serves the page and the web socket used for signalling. Run it with
// dotnet run, open http://localhost:8081, pick a codec and press Start.
// dotnet run -- --help lists the options. WebRTCGetStartedCodecs goes the
// other way, sending video to the browser.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 28 Jan 2021  Aaron Clauson   Created WebRTCClientVP8Net, Dublin, Ireland.
// 25 Nov 2026  Claude          Created WebRTCClientH264Net, from WebRTCClientVP8Net.
// 27 Nov 2026  Claude          Combined WebRTCClientVP8Net and WebRTCClientH264Net into
//                              WebRTCClientCodecs, with the codec and library picked on the page.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using SIPSorcery.H264;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;
using Vpx.Net;

namespace demo;

class Program
{
    private const string DEFAULT_URL = "http://localhost:8081";

    private const string USAGE =
        """
        Usage: dotnet run -- [options]

        The page picks the video codec and the library that decodes it.

          --urls <url>        Where the web server listens. The default is http://localhost:8081.
          --stun <url>        A STUN server for the ICE candidates, e.g. stun:stun.cloudflare.com.
          --turn <url>        A TURN server, as url;username;password.
          --bind-port <port>  The UDP port for the media. The default lets the OS choose.
          --ffmpeg-path <dir> Where the FFmpeg shared libraries are, if they aren't found on their own.
        """;

    /// <summary>
    /// A library that can decode the browser's video, and the codecs it decodes. Each decoder is
    /// an <see cref="IVideoSink"/> that raises BGR pictures. Unavailable says why the library
    /// can't be used on this machine, or is null if it can.
    /// </summary>
    private sealed record Library(string Name, string Description, VideoCodecsEnum[] Codecs, Func<IVideoSink> CreateDecoder)
    {
        public string? Unavailable { get; set; }
    }

    // The codecs the page offers, in its order.
    private static readonly VideoCodecsEnum[] _codecs =
        [VideoCodecsEnum.VP8, VideoCodecsEnum.VP9, VideoCodecsEnum.H264, VideoCodecsEnum.H265, VideoCodecsEnum.AV1];

    // The page offers a codec's libraries in this order, the first available one picked.
    // SIPSorcery.AV1 isn't here: it only encodes.
    private static readonly Library[] _libraries =
    [
        new("vp8.net", "SIPSorcery.VP8, managed, no native libraries.",
            [VideoCodecsEnum.VP8],
            () => new Vp8NetVideoEncoderEndPoint()),
        new("h264.net", "SIPSorcery.H264, a managed port of OpenH264, for educational use only: H.264 is covered by patents. It decodes Constrained Baseline, which is what browsers send when it's the profile offered.",
            [VideoCodecsEnum.H264],
            () => new H264NetVideoEndPoint()),
        new("ffmpeg", "SIPSorceryMedia.FFmpeg, with the FFmpeg shared libraries.",
            [VideoCodecsEnum.VP8, VideoCodecsEnum.VP9, VideoCodecsEnum.H264, VideoCodecsEnum.H265, VideoCodecsEnum.AV1],
            () => new FFmpegVideoEndPoint()),
    ];

    private static Microsoft.Extensions.Logging.ILogger _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    private static VideoWindow _window = null!;
    private static string _waiting = string.Empty;

    // The connection whose video the window shows. A browser that connects replaces it.
    private static VideoReceiver? _current;

    [STAThread]
    static int Main(string[] args)
    {
        Console.WriteLine("WebRTC Client Codecs");

        if (args.Any(a => a is "-h" or "--help" or "-?" or "/?"))
        {
            Console.WriteLine(USAGE);
            return 0;
        }

        // ASP.NET's configuration reads the options from the command line (--stun x or
        // --stun=x), and also from environment variables and appsettings.json.
        var builder = WebApplication.CreateBuilder(args);
        var config = builder.Configuration;

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

        // Without FFmpeg the page still offers the managed libraries.
        var ffmpeg = _libraries.Single(l => l.Name == "ffmpeg");
        try
        {
            FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_WARNING, config["ffmpeg-path"], _logger);
        }
        catch (Exception excp)
        {
            ffmpeg.Unavailable = $"FFmpeg's shared libraries couldn't be loaded: {excp.Message.Split('\n')[0]}";
            _logger.LogWarning("{Unavailable} Install them, or pass --ffmpeg-path, to use the ffmpeg decoders.", ffmpeg.Unavailable);
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

        // What the page offers.
        app.MapGet("/libraries", () => new
        {
            codecs = _codecs.Select(c => c.ToString()),
            libraries = _libraries.Select(l => new
            {
                name = l.Name,
                description = l.Description,
                codecs = l.Codecs.Select(c => c.ToString()),
                unavailable = l.Unavailable,
            }),
        });

        app.Map("/ws", context => HandleWebSocket(context, iceServers, bindPort));

        // The window needs this thread, so the web server runs on the thread pool. Task.Run keeps
        // its start and stop off this thread, which is the window's once it exists.
        Task.Run(() => app.StartAsync()).GetAwaiter().GetResult();

        _waiting = $"Open {string.Join(" or ", app.Urls)} in a browser, pick a codec and press Start.";
        _logger.LogInformation("{Waiting}", _waiting);

        Application.EnableVisualStyles();
        _window = new VideoWindow(_waiting);

        // Ctrl+C in the console stops the web server, and the window closes with it.
        app.Lifetime.ApplicationStopping.Register(_window.CloseFromAnyThread);

        Application.Run(_window);

        _current?.Close("the window closed");
        Task.Run(() => app.StopAsync()).GetAwaiter().GetResult();
        return 0;
    }

    /// <summary>
    /// Runs the signalling for one browser. The page puts its choice in the query string,
    /// /ws?codec=H264&amp;library=h264.net.
    /// </summary>
    private static async Task HandleWebSocket(HttpContext context, List<RTCIceServer> iceServers, int bindPort)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (!TryChooseLibrary(context.Request.Query["library"], context.Request.Query["codec"], out var library, out var codec, out string error))
        {
            _logger.LogWarning("Web socket refused: {Error}", error);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync(error);
            return;
        }

        _logger.LogInformation("Receiving {Codec}, decoded by {Library}.", codec, library.Name);

        var webSocket = await context.WebSockets.AcceptWebSocketAsync();

        var webSocketPeer = new WebRTCWebSocketPeerAspNet(
            webSocket,
            pcConfig => CreatePeerConnection(pcConfig, iceServers, bindPort, library, codec),
            new RTCConfiguration { X_ICEIncludeAllInterfaceAddresses = true },
            RTCSdpType.offer);

        await webSocketPeer.Run();

        _logger.LogDebug("Web socket closed with the peer connection {State}.", webSocketPeer.RTCPeerConnection?.connectionState);
    }

    private static Task<RTCPeerConnection> CreatePeerConnection(RTCConfiguration config, List<RTCIceServer> iceServers,
        int bindPort, Library library, VideoCodecsEnum codec)
    {
        config.iceServers = iceServers;
        var pc = new RTCPeerConnection(config, bindPort);

        // Decodes the frames, shows them in the window, and asks for a key frame when needed.
        var receiver = new VideoReceiver(pc, library.CreateDecoder, $"{codec} decoded by {library.Name}", _window, _logger);

        // Offer just the chosen codec, receive only, with the decoder's own format parameters (the
        // H264 profile-level-id, for example), so the browser can't send one the decoder doesn't take.
        var videoFormats = receiver.VideoFormats.Where(f => f.Codec == codec).ToList();
        pc.addTrack(new MediaStreamTrack(videoFormats, MediaStreamStatusEnum.RecvOnly));

        pc.OnVideoFormatsNegotiated += (formats) =>
        {
            var format = formats.First();
            _logger.LogInformation("Video format negotiated: {Codec}, payload type {FormatID} {Parameters}.", format.Codec, format.FormatID, format.Parameters);
            receiver.SetVideoFormat(format);
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

        pc.onconnectionstatechange += (state) =>
        {
            _logger.LogDebug("Peer connection state changed to {State}.", state);

            if (state == RTCPeerConnectionState.connected)
            {
                // The window shows one browser's video, so the newest connection replaces the last.
                var previous = Interlocked.Exchange(ref _current, receiver);
                previous?.Close("another browser connected");
                _window.ShowStatus($"Connected, {receiver.Description}. Waiting for the first picture.");
            }
            else if (state == RTCPeerConnectionState.failed)
            {
                pc.Close("ice disconnection");
            }
            else if (state == RTCPeerConnectionState.closed)
            {
                receiver.Dispose();
                if (Interlocked.CompareExchange(ref _current, null, receiver) == receiver)
                {
                    _window.ShowStatus($"The connection closed. {_waiting}");
                }
            }
        };

        return Task.FromResult(pc);
    }

    /// <summary>
    /// Picks the library and codec the page asked for. Either may be left out: a library decodes
    /// its first codec, and a codec is decoded by the first available library that decodes it.
    /// </summary>
    private static bool TryChooseLibrary(string? libraryArg, string? codecArg, out Library library, out VideoCodecsEnum codec, out string error)
    {
        library = _libraries[0];
        codec = library.Codecs[0];
        error = string.Empty;

        VideoCodecsEnum? wantCodec = null;

        if (!string.IsNullOrWhiteSpace(codecArg))
        {
            wantCodec = _codecs.Cast<VideoCodecsEnum?>().FirstOrDefault(c => c.ToString()!.Equals(codecArg, StringComparison.OrdinalIgnoreCase));
            if (wantCodec == null)
            {
                error = $"Unknown codec {codecArg}. Expected {string.Join(", ", _codecs)}.";
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(libraryArg))
        {
            var named = _libraries.FirstOrDefault(l => l.Name.Equals(libraryArg, StringComparison.OrdinalIgnoreCase));
            if (named == null)
            {
                error = $"Unknown library {libraryArg}. Expected {string.Join(", ", _libraries.Select(l => l.Name))}.";
                return false;
            }
            if (wantCodec != null && !named.Codecs.Contains(wantCodec.Value))
            {
                error = $"{named.Name} doesn't decode {wantCodec}. It decodes {string.Join(", ", named.Codecs)}.";
                return false;
            }
            library = named;
        }
        else if (wantCodec != null)
        {
            library = _libraries.FirstOrDefault(l => l.Unavailable == null && l.Codecs.Contains(wantCodec.Value))
                ?? _libraries.First(l => l.Codecs.Contains(wantCodec.Value));
        }

        if (library.Unavailable != null)
        {
            error = $"{library.Name} isn't available. {library.Unavailable}";
            return false;
        }

        codec = wantCodec ?? library.Codecs[0];
        return true;
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
