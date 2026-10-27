//-----------------------------------------------------------------------------
// Filename: RouteCommand.cs
//
// Description: The "sipsorcery route" verb: the v0.1 of the stream router.
// It builds a small stream graph from a --from source edge and one or more --to
// sink edges and pumps media through it, then reports what flowed. This is the
// imperative, one-shot front-end to the graph (the seed of the larger "routing
// policy over streams" idea): a stream is the noun, and --from / --to attach edges
// to it.
//
//   route --from testpattern --to out.ivf            # generate VP8, record to IVF
//   route --from testpattern --to play --to out.ivf  # tee: watch and record
//   route --from whep:https://host/whep --to out.ivf # pull a live WebRTC stream and record it
//   route --from sip:music@iptel.org --to whip:http://host/whip --scope
//                                                    # bridge a SIP call to WebRTC, adding an
//                                                    # audio-scope video generated from the call audio
//
// Routes are one-way by default. A two-way route connects exactly two endpoints
// that both send and receive (web, term, agent, openai, sip:) so each hears the
// other. It is two-way when --duplex is given, or automatically when either end is
// an agent, since a one-way agent is useless:
//
//   route --from term --to openai --avatar | mpv -   # talk to an agent from the terminal
//   route --from web --to sip:alice@example.com --duplex
//                                                    # a browser softphone call
//
// One-way is the default so a microphone (term, web) is only ever opened for a
// route that is clearly a conversation.
//
// The graph repacketises, it does not transcode: frames travel encoded from source
// to sink. The scope video is the exception by design - it is encoded by an
// external ffmpeg process, which also renders the waves/spectrum modes; the hilbert
// mode is the examples' audio scope, rendered in-process (see AudioScopeTransform).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 18 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
// 23 Jun 2026	Aaron Clauson	Added the sip: source, whip: sink and the --scope audio-scope video.
// 24 Oct 2026	Aaron Clauson	Added --scope-mode hilbert.
// 25 Oct 2026	Aaron Clauson	Folded in the bridge verb as two-way routes (--duplex).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Cli.Commands.Bridge;
using SIPSorcery.Cli.Commands.Route;

namespace SIPSorcery.Cli.Commands;

public sealed class RouteCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 30;
    private const int DEFAULT_DURATION_SECONDS = 0;
    private const int DEFAULT_FPS = 30;
    private const string DEFAULT_AUDIO_CODEC = "opus";
    private const string DEFAULT_SCOPE_MODE = "waves";
    private const string DEFAULT_SCOPE_SIZE = "640x360";
    private const int DEFAULT_WEB_PORT = 8080;

    /// <summary>The result shape written with --json. Stable field names; additive changes only.</summary>
    private sealed record RouteResult(
        bool Success,
        string From,
        string[] To,
        string Stopped,
        long FramesRouted,
        long VideoFramesRouted,
        long AudioFramesRouted,
        long? ConnectTimeMs,
        int RunMs,
        SinkReport[] Sinks,
        string? Error,
        bool Duplex = false);

    /// <summary>What a node received. For a two-way route there is one per endpoint (each receives from the other).</summary>
    private sealed record SinkReport(string Edge, int Frames, long Bytes, int Dropped);

    /// <summary>Everything the verb was given, gathered once so the one-way and two-way paths share it.</summary>
    private sealed record RouteArgs(
        string From,
        string[] To,
        bool Duplex,
        int DurationSeconds,
        int Fps,
        string? Token,
        bool Scope,
        string ScopeMode,
        string ScopeSize,
        string? FfmpegPath,
        string? Username,
        string? Password,
        string AudioCodec,
        string? VideoCodec,
        bool NoOpen,
        string? Url,
        string? ApiKey,
        string? ApiSecret,
        string? AppId,
        string? Avatar,
        string? Voice,
        string? Persona,
        string? Greeting,
        string? Llm,
        string? LlmModel,
        string? AzureKey,
        string? AzureRegion,
        string? Mic,
        int TimeoutSeconds,
        bool AsJson,
        bool Verbose);

    public RouteCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var fromOption = new Option<string>("--from", "-f")
        {
            Description = "The source edge to pull a stream from: testpattern (a generated H264 pattern + PCMU music), " +
                          "whep:<url> (a live WebRTC stream), sip:<uri> (place a SIP call and forward its audio, e.g. sip:music@iptel.org), " +
                          "livekit:<room> (subscribe to a LiveKit room; needs --url/--api-key/--api-secret) or " +
                          "cloudflare:<sessionId> (pull a Cloudflare Realtime SFU session published by the cloudflare sink; needs --app-id/--token). " +
                          "In a two-way route, one of: web, term, agent, openai or sip:<uri>.",
            Required = true
        };

        var toOption = new Option<string[]>("--to", "-o")
        {
            Description = "A sink edge to push the stream to: a file path (VP8->IVF, H264/H265->Annex B, or *.mkv for video + audio " +
                          "in Matroska), \"play\" (ffplay), \"null\" (discard), \"-\" (video bitstream on stdout), \"-:mkv\" or \"term\" " +
                          "(video + audio as Matroska on stdout, e.g. | mpv -), whip:<url> (publish to a WebRTC endpoint), " +
                          "web[:port] (self-host a WHEP server + player page on localhost, default port 8080, to watch in a browser), " +
                          "livekit[:room] (publish into a LiveKit room; needs --url/--api-key/--api-secret) or " +
                          "cloudflare (publish to a Cloudflare Realtime SFU session; needs --app-id/--token). " +
                          "Repeat --to to fan out to several sinks at once. In a two-way route, one of: web[:port] (a browser as " +
                          "microphone + speaker), term (the terminal: microphone via ffmpeg in, Matroska on stdout out), agent, " +
                          "openai or sip:<uri>.",
            Required = true,
            AllowMultipleArgumentsPerToken = true
        };

        var duplexOption = new Option<bool>("--duplex")
        {
            Description = "Route both ways: --from and the single --to each send to and receive from the other (e.g. a phone call " +
                          "between term or web and sip:<uri>). Implied when either end is an agent (openai or agent). Without it a " +
                          "route is one-way, so a microphone (term, web) is never opened."
        };

        var durationOption = new Option<int>("--duration", "-d")
        {
            Description = "Seconds to run for. The default 0 runs until the source ends (a live source hanging up, a participant " +
                          "leaving) or ctrl-c; set a positive value to stop after a fixed time.",
            DefaultValueFactory = _ => DEFAULT_DURATION_SECONDS
        };

        var fpsOption = new Option<int>("--fps")
        {
            Description = "Frame rate for a generated source (testpattern) and for the --scope video.",
            DefaultValueFactory = _ => DEFAULT_FPS
        };

        var tokenOption = new Option<string?>("--token")
        {
            Description = "Bearer token for a transport edge: a whep stream key, the whip endpoint Authorization, or the " +
                          "Cloudflare SFU API token (else CLOUDFLARE_API_TOKEN)."
        };

        var scopeOption = new Option<bool>("--scope")
        {
            Description = "For a one-way audio source (sip:), add a generated \"audio scope\" video track that visualises the audio."
        };

        var scopeModeOption = new Option<string>("--scope-mode")
        {
            Description = "Scope visualisation: waves (a moving waveform), spectrum (a scrolling frequency spectrum) " +
                          "or hilbert (the audio scope from the Softphone example: the analytic signal traced as a coloured curve).",
            DefaultValueFactory = _ => DEFAULT_SCOPE_MODE
        };

        var scopeSizeOption = new Option<string>("--scope-size")
        {
            Description = "Scope video size WxH.",
            DefaultValueFactory = _ => DEFAULT_SCOPE_SIZE
        };

        var ffmpegPathOption = new Option<string?>("--ffmpeg-path")
        {
            Description = "Directory containing the ffmpeg executable used for the --scope video. Defaults to ffmpeg on the PATH."
        };

        var usernameOption = new Option<string?>("--username", "-u")
        {
            Description = "Optional username for authenticating a sip: call."
        };

        var passwordOption = new Option<string?>("--password")
        {
            Description = "Optional password for authenticating a sip: call."
        };

        var audioCodecOption = new Option<string>("--audio-codec")
        {
            Description = "Force the audio codec the sinks offer / a generated source produces: exactly one of " +
                          $"{CodecSelection.AudioNamesList}. opus is the default (the universal WebRTC codec); " +
                          "pcmu/pcma are for an endpoint that only accepts G.711. A G.711 sip: call is transcoded " +
                          "up to opus automatically, so opus needs no flag. Two-way routes always use opus.",
            DefaultValueFactory = _ => DEFAULT_AUDIO_CODEC
        };

        var videoCodecOption = new Option<string?>(CodecSelection.VIDEO_OPTION)
        {
            Description = "Force the video codec the route's edges send and receive: exactly one of " +
                          $"{CodecSelection.VideoNamesList}. Without it a sending edge uses h264 and a receiving " +
                          "edge offers every supported codec. Pinned, the codec has to be negotiated or the run " +
                          "fails, which is what makes it useful for testing one codec end to end."
        };

        var noOpenOption = new Option<bool>("--no-open")
        {
            Description = "For a web edge, do not automatically open a browser at its page (it is also never opened with --json)."
        };

        var urlOption = new Option<string?>("--url")
        {
            Description = "For a livekit edge: the LiveKit web socket URL (wss://...). Defaults to the LIVEKIT_WEBSOCKET_URL environment variable."
        };

        var apiKeyOption = new Option<string?>("--api-key")
        {
            Description = "API key for the endpoint that needs one: the LiveKit API key for a livekit edge (else LIVEKIT_API_KEY), " +
                          "the OpenAI key for openai (else OPENAI_API_KEY), or the bearer key for a hosted agent --llm gateway " +
                          "(else LLM_API_KEY)."
        };

        var apiSecretOption = new Option<string?>("--api-secret")
        {
            Description = "For a livekit edge: the LiveKit API secret. Defaults to the LIVEKIT_API_SECRET environment variable."
        };

        var appIdOption = new Option<string?>("--app-id")
        {
            Description = "For a cloudflare sink: the Cloudflare Realtime SFU app ID (its API token comes from --token). " +
                          "Defaults to the CLOUDFLARE_APPID environment variable (token: CLOUDFLARE_API_TOKEN)."
        };

        var avatarOption = new Option<string?>("--avatar")
        {
            Description = "Give an agent a lip-synced video face: a video track to a web or term peer, or a video call to a sip: " +
                          "peer. Optionally name it: wav2lip (the default, the photoreal Max Headroom head; Windows, needs its model " +
                          "and persona via WAV2LIP_ONNX/NEURAL_PERSONA or C:\\tools\\wav2lip, else falls back to max) or max (the " +
                          "SkiaSharp cartoon).",
            Arity = ArgumentArity.ZeroOrOne
        };

        var voiceOption = new Option<string?>("--voice")
        {
            Description = "Voice for an agent. agent: an Azure neural TTS voice (default en-US-GuyNeural; must be " +
                          "neural for visemes). openai: an OpenAI Realtime voice e.g. marin, alloy, verse (default marin)."
        };

        var personaOption = new Option<string?>("--persona", "--system")
        {
            Description = "System prompt giving an agent its character (default: the Max Headroom persona)."
        };

        var greetingOption = new Option<string?>("--greeting")
        {
            Description = "What an agent says when the far side connects (default: a Max Headroom greeting)."
        };

        var llmOption = new Option<string?>("--llm")
        {
            Description = "OpenAI-compatible chat endpoint for the agent endpoint (Ollama/LM Studio/OpenAI/OpenRouter). " +
                          "Without it the agent just repeats what it heard."
        };

        var llmModelOption = new Option<string?>("--llm-model")
        {
            Description = "Model for --llm (default llama3.2)."
        };

        var azureKeyOption = new Option<string?>("--azure-key")
        {
            Description = "Azure Speech resource key for the agent endpoint (else AZURE_SPEECH_KEY)."
        };

        var azureRegionOption = new Option<string?>("--azure-region")
        {
            Description = "Azure Speech resource region for the agent endpoint, e.g. westeurope (else AZURE_SPEECH_REGION)."
        };

        var micOption = new Option<string?>("--mic")
        {
            Description = "For a two-way term endpoint, the microphone to capture via ffmpeg: a DirectShow device name on " +
                          "Windows (default: the first audio device ffmpeg lists), an avfoundation device on macOS or a " +
                          "PulseAudio source on Linux (default: default)."
        };

        var command = new Command("route",
            "Route media from a --from edge to one or more --to edges: one-way by default, or both ways between two " +
            "endpoints with --duplex (implied when either end is an agent).");
        command.Options.Add(fromOption);
        command.Options.Add(toOption);
        command.Options.Add(duplexOption);
        command.Options.Add(durationOption);
        command.Options.Add(fpsOption);
        command.Options.Add(tokenOption);
        command.Options.Add(scopeOption);
        command.Options.Add(scopeModeOption);
        command.Options.Add(scopeSizeOption);
        command.Options.Add(ffmpegPathOption);
        command.Options.Add(usernameOption);
        command.Options.Add(passwordOption);
        command.Options.Add(audioCodecOption);
        command.Options.Add(videoCodecOption);
        command.Options.Add(noOpenOption);
        command.Options.Add(urlOption);
        command.Options.Add(apiKeyOption);
        command.Options.Add(apiSecretOption);
        command.Options.Add(appIdOption);
        command.Options.Add(avatarOption);
        command.Options.Add(voiceOption);
        command.Options.Add(personaOption);
        command.Options.Add(greetingOption);
        command.Options.Add(llmOption);
        command.Options.Add(llmModelOption);
        command.Options.Add(azureKeyOption);
        command.Options.Add(azureRegionOption);
        command.Options.Add(micOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            new RouteArgs(
                parseResult.GetValue(fromOption)!,
                parseResult.GetValue(toOption)!,
                parseResult.GetValue(duplexOption),
                parseResult.GetValue(durationOption),
                parseResult.GetValue(fpsOption),
                parseResult.GetValue(tokenOption),
                parseResult.GetValue(scopeOption),
                parseResult.GetValue(scopeModeOption)!,
                parseResult.GetValue(scopeSizeOption)!,
                parseResult.GetValue(ffmpegPathOption),
                parseResult.GetValue(usernameOption),
                parseResult.GetValue(passwordOption),
                parseResult.GetValue(audioCodecOption)!,
                parseResult.GetValue(videoCodecOption),
                parseResult.GetValue(noOpenOption),
                parseResult.GetValue(urlOption),
                parseResult.GetValue(apiKeyOption),
                parseResult.GetValue(apiSecretOption),
                parseResult.GetValue(appIdOption),
                // --avatar alone selects the default face; absent means no avatar.
                parseResult.GetResult(avatarOption) is null ? null : parseResult.GetValue(avatarOption) ?? AgentAvatar.DEFAULT,
                parseResult.GetValue(voiceOption),
                parseResult.GetValue(personaOption),
                parseResult.GetValue(greetingOption),
                parseResult.GetValue(llmOption),
                parseResult.GetValue(llmModelOption),
                parseResult.GetValue(azureKeyOption),
                parseResult.GetValue(azureRegionOption),
                parseResult.GetValue(micOption),
                parseResult.GetValue(TimeoutOption),
                parseResult.GetValue(JsonOption),
                parseResult.GetValue(VerboseOption)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(RouteArgs args, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(args.Verbose);
        var logger = loggerFactory.CreateLogger(nameof(RouteCommand));

        string from = args.From;
        string[] to = args.To;

        // When an edge claims stdout (--to -, -:mkv or term), the result is commentary and moves to stderr
        // so the stream on stdout stays a single clean payload.
        bool stdoutClaimed = to.Append(from).Any(ClaimsStdout);

        // Two-way when asked for, or when an agent is at either end: an agent that cannot hear is useless.
        bool duplex = args.Duplex || IsAgent(from) || to.Any(IsAgent);

        string? argumentError = ValidateArguments(args, duplex);
        if (argumentError != null)
        {
            return WriteResult(args.AsJson, stdoutClaimed,
                new RouteResult(false, from, to, "invalid argument", 0, 0, 0, null, 0, Array.Empty<SinkReport>(), argumentError, duplex),
                ExitCodes.InvalidArgument);
        }

        return duplex
            ? await RunDuplexAsync(args, stdoutClaimed, loggerFactory, logger, ct).ConfigureAwait(false)
            : await RunOneWayAsync(args, stdoutClaimed, logger, ct).ConfigureAwait(false);
    }

    /// <summary>Checks the option combinations up front, for a clean error before any edge is built.</summary>
    private static string? ValidateArguments(RouteArgs args, bool duplex)
    {
        if (duplex)
        {
            if (args.To.Length != 1)
            {
                return "A two-way route connects exactly two endpoints: give a single --to.";
            }
            if (args.Scope)
            {
                return "The --scope option is for one-way routes from a sip: source; leave out --duplex to use it.";
            }
            if (args.Avatar != null && !IsAgent(args.From) && !IsAgent(args.To[0]))
            {
                return "The --avatar option gives an agent a face, so one end needs to be an agent (openai or agent).";
            }
            if (!string.IsNullOrWhiteSpace(args.VideoCodec))
            {
                // A two-way route runs on the bridge endpoints, whose video (an agent avatar, a browser
                // track) is produced H264 by its own encoder rather than negotiated per edge, so there is
                // nothing here for a forced codec to pin. Say so rather than accept it and ignore it.
                return "The --video-codec option applies to one-way routes; a two-way route's video is always H264.";
            }
            return null;
        }

        if (args.Avatar != null)
        {
            return "The --avatar option gives an agent a face, so one end needs to be an agent (openai or agent).";
        }

        // The scope video derives from audio, so it only applies to a sip: source.
        if (args.Scope && !IsSipSource(args.From))
        {
            return "The --scope option needs an audio source, e.g. --from sip:music@iptel.org.";
        }

        if (args.Scope)
        {
            if (!AudioScopeTransform.Modes.Contains(args.ScopeMode, StringComparer.OrdinalIgnoreCase))
            {
                return $"Unrecognised --scope-mode {args.ScopeMode}, use one of: {string.Join(", ", AudioScopeTransform.Modes)}.";
            }
            if (!AudioScopeTransform.TryParseSize(args.ScopeSize, out _, out _, out string? sizeError))
            {
                return sizeError;
            }
            // The scope video comes out of an external ffmpeg process wired for H264, so it cannot
            // follow a forced codec. Better to say so than to pin a codec the frames will not be in.
            if (!string.IsNullOrWhiteSpace(args.VideoCodec) &&
                !string.Equals(args.VideoCodec!.Trim(), "h264", StringComparison.OrdinalIgnoreCase))
            {
                return "The --scope video is generated as H264, so it cannot be combined with --video-codec " +
                       $"{args.VideoCodec.Trim()}.";
            }
        }

        if (!RouteAudio.TryResolveCodec(args.AudioCodec, out _, out string? audioCodecError))
        {
            return audioCodecError;
        }

        return ResolveVideoCodecError(args);
    }

    /// <summary>
    /// Checks --video-codec, which is optional: left out, each edge keeps its own default (h264 when
    /// sending, every supported codec when receiving). Given, it must name exactly one known codec -
    /// a typo has to be an argument error, because silently falling back would make a run that was
    /// meant to prove one codec works prove nothing.
    /// </summary>
    private static string? ResolveVideoCodecError(RouteArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.VideoCodec))
        {
            return null;
        }

        return RouteVideo.TryResolveCodec(args.VideoCodec, out _, out string? error) ? null : error;
    }

    /// <summary>A one-way route: one source fanned out to the --to sinks.</summary>
    private static async Task<int> RunOneWayAsync(RouteArgs args, bool stdoutClaimed, ILogger logger, CancellationToken ct)
    {
        string from = args.From;
        string[] to = args.To;

        // A web sink auto-opens a browser for interactive use, but never when --json (a scripted run) or
        // --no-open is set.
        bool openBrowser = !args.NoOpen && !args.AsJson;
        var edgeOptions = new EdgeOptions(args.Fps, args.Token, args.TimeoutSeconds, args.Scope, args.ScopeMode, args.ScopeSize,
            args.FfmpegPath, args.Username, args.Password, args.AudioCodec, args.VideoCodec, openBrowser, args.Url,
            args.ApiKey, args.ApiSecret, args.AppId);

        // Build the edges. A bad spec is an argument error before anything starts.
        ISourceNode source;
        var sinks = new List<ISinkNode>();
        try
        {
            source = EdgeFactory.CreateSource(from, edgeOptions, logger);
        }
        catch (EdgeException ex)
        {
            return WriteResult(args.AsJson, stdoutClaimed,
                new RouteResult(false, from, to, "invalid source", 0, 0, 0, null, 0, Array.Empty<SinkReport>(), ex.Message),
                ExitCodes.InvalidArgument);
        }

        try
        {
            foreach (var spec in to)
            {
                sinks.Add(EdgeFactory.CreateSink(spec, edgeOptions, logger));
            }
        }
        catch (EdgeException ex)
        {
            await DisposeAllAsync(source, sinks).ConfigureAwait(false);
            return WriteResult(args.AsJson, stdoutClaimed,
                new RouteResult(false, from, to, "invalid sink", 0, 0, 0, null, 0, Array.Empty<SinkReport>(), ex.Message),
                ExitCodes.InvalidArgument);
        }

        var graph = new StreamGraph(source, sinks);

        string stopped;
        string? error = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            stopped = await graph.RunAsync(args.DurationSeconds, ct).ConfigureAwait(false);
        }
        catch (EdgeException ex)
        {
            // A transport source that could not come up (bad URL, no answer, timeout).
            error = ex.Message;
            stopped = "failed to start";
        }
        catch (OperationCanceledException)
        {
            stopped = "cancelled";
        }
        stopwatch.Stop();

        long? connectTimeMs = source.ConnectTimeMs;

        // Dispose the sinks first so files are finalised and any worker drains before the stats are
        // read, then the source (transport teardown).
        var sinkReports = new List<SinkReport>(sinks.Count);
        foreach (var sink in sinks)
        {
            await sink.DisposeAsync().ConfigureAwait(false);
            var stats = sink.GetStats();
            sinkReports.Add(new SinkReport(sink.Describe(), stats.Frames, stats.Bytes, stats.Dropped));
        }
        await source.DisposeAsync().ConfigureAwait(false);

        long framesRouted = graph.FramesRouted;
        bool success = error == null && framesRouted > 0;
        if (error == null && framesRouted == 0)
        {
            error = "No frames flowed from the source. For a live source, is anything publishing to it?";
        }

        int exitCode = success ? ExitCodes.Ok
            : error != null && error.Contains("did not reach connected", StringComparison.OrdinalIgnoreCase) ? ExitCodes.Timeout
            : ExitCodes.Failed;

        return WriteResult(args.AsJson, stdoutClaimed,
            new RouteResult(success, from, to, stopped, framesRouted, graph.VideoFramesRouted, graph.AudioFramesRouted,
                connectTimeMs, (int)stopwatch.ElapsedMilliseconds, sinkReports.ToArray(), error),
            exitCode);
    }

    /// <summary>A two-way route: the --from and --to endpoints cross-wired so each hears the other.</summary>
    private static async Task<int> RunDuplexAsync(RouteArgs args, bool stdoutClaimed, ILoggerFactory loggerFactory, ILogger logger,
        CancellationToken ct)
    {
        string from = args.From;
        string[] to = args.To;

        // Match the one-way web sink: a web endpoint's page opens automatically unless --no-open or --json.
        bool openBrowser = !args.NoOpen && !args.AsJson && (IsWeb(from) || IsWeb(to[0]));

        var options = new BridgeOptions(
            DEFAULT_WEB_PORT,
            openBrowser,
            args.AzureKey,
            args.AzureRegion,
            args.Voice,
            args.Persona,
            args.Llm,
            args.LlmModel,
            args.ApiKey,
            args.Greeting,
            args.Avatar,
            args.Username,
            args.Password,
            args.TimeoutSeconds,
            args.Mic);

        // Build both endpoints. A bad spec or missing credentials is an argument error before anything starts.
        IBridgeParticipant participantA;
        IBridgeParticipant participantB;
        try
        {
            participantA = BridgeFactory.CreateParticipant(from, options, loggerFactory, logger);
        }
        catch (EdgeException ex)
        {
            return WriteResult(args.AsJson, stdoutClaimed,
                new RouteResult(false, from, to, "invalid source", 0, 0, 0, null, 0, Array.Empty<SinkReport>(), ex.Message, true),
                ExitCodes.InvalidArgument);
        }

        try
        {
            participantB = BridgeFactory.CreateParticipant(to[0], options, loggerFactory, logger);
        }
        catch (EdgeException ex)
        {
            await participantA.DisposeAsync().ConfigureAwait(false);
            return WriteResult(args.AsJson, stdoutClaimed,
                new RouteResult(false, from, to, "invalid sink", 0, 0, 0, null, 0, Array.Empty<SinkReport>(), ex.Message, true),
                ExitCodes.InvalidArgument);
        }

        // Cue the agent's greeting when the other side connects, and relay the agent transcript to a web
        // peer's browser console. Both are side channels the media graph doesn't carry.
        WireGreeting(participantA, participantB);
        WireTranscript(participantA, participantB);

        var graph = new BridgeGraph(participantA, participantB);

        string stopped;
        string? error = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            stopped = await graph.RunAsync(args.DurationSeconds, ct).ConfigureAwait(false);
        }
        catch (EdgeException ex)
        {
            error = ex.Message;
            stopped = "failed to start";
        }
        catch (OperationCanceledException)
        {
            stopped = "cancelled";
        }
        catch (Exception ex)
        {
            // A participant failed to start or run (e.g. Azure unreachable, the web port already in use).
            error = ex.Message;
            stopped = "failed";
        }
        stopwatch.Stop();

        await participantA.DisposeAsync().ConfigureAwait(false);
        await participantB.DisposeAsync().ConfigureAwait(false);

        var reports = new[] { participantA, participantB }.Select(p =>
        {
            var stats = p.GetStats();
            return new SinkReport(p.Describe(), stats.Frames, stats.Bytes, stats.Dropped);
        }).ToArray();

        bool success = error == null;
        return WriteResult(args.AsJson, stdoutClaimed,
            new RouteResult(success, participantA.Describe(), new[] { participantB.Describe() }, stopped,
                graph.AToBFrames + graph.BToAFrames, graph.VideoFrames, graph.AudioFrames, null,
                (int)stopwatch.ElapsedMilliseconds, reports, error, true),
            success ? ExitCodes.Ok : ExitCodes.Failed);
    }

    private static void WireGreeting(IBridgeParticipant a, IBridgeParticipant b)
    {
        // The agent greets when the far side first connects (a browser opening the page, a SIP call
        // answered, the terminal's microphone starting). The connectable is whichever side announces a
        // connection; the greeter is the agent.
        var greeter = a as IGreetable ?? b as IGreetable;
        var connectable = (a is IGreetable ? null : a as IConnectable) ?? (b is IGreetable ? null : b as IConnectable);
        if (greeter != null && connectable != null)
        {
            connectable.Connected += greeter.Greet;
        }
    }

    private static void WireTranscript(IBridgeParticipant a, IBridgeParticipant b)
    {
        // The agent produces the transcript; a web peer shows it in the browser console. Only meaningful
        // when one side is a web peer, so it no-ops otherwise.
        var web = a as WebParticipant ?? b as WebParticipant;
        var source = a as ITranscriptSource ?? b as ITranscriptSource;
        if (web != null && source != null)
        {
            source.OnTranscript += web.SendTranscript;
        }
    }

    /// <summary>
    /// Whether a --from spec resolves to a SIP (audio) source: an explicit sip:/sips: scheme, or a
    /// bare user@host. Matches the SIP cases in <see cref="EdgeFactory.CreateSource"/>.
    /// </summary>
    private static bool IsSipSource(string from) =>
        from.StartsWith("sip:", StringComparison.OrdinalIgnoreCase) ||
        from.StartsWith("sips:", StringComparison.OrdinalIgnoreCase) ||
        (from.Contains('@') && !from.Contains(':'));

    /// <summary>An agent endpoint: a voice agent, which only makes sense two-way.</summary>
    private static bool IsAgent(string spec) =>
        spec.Trim().Equals("openai", StringComparison.OrdinalIgnoreCase) ||
        spec.Trim().Equals("agent", StringComparison.OrdinalIgnoreCase);

    private static bool IsWeb(string spec) =>
        spec.Trim().Equals("web", StringComparison.OrdinalIgnoreCase) ||
        spec.Trim().StartsWith("web:", StringComparison.OrdinalIgnoreCase);

    private static bool ClaimsStdout(string spec) =>
        spec == "-" || spec.StartsWith("-:", StringComparison.Ordinal) ||
        spec.Trim().Equals("term", StringComparison.OrdinalIgnoreCase);

    private static async Task DisposeAllAsync(ISourceNode source, IEnumerable<ISinkNode> sinks)
    {
        foreach (var sink in sinks)
        {
            try { await sink.DisposeAsync().ConfigureAwait(false); } catch { /* best effort */ }
        }
        try { await source.DisposeAsync().ConfigureAwait(false); } catch { /* best effort */ }
    }

    private static int WriteResult(bool asJson, bool stdoutClaimed, RouteResult result, int exitCode)
    {
        var output = stdoutClaimed ? Console.Error : Console.Out;

        if (asJson)
        {
            output.WriteLine(SerializeResult(result));
        }
        else if (result.Success && result.Duplex)
        {
            // Each endpoint's report is what it received from the other side.
            string received = string.Join("; ", result.Sinks.Select(s => $"{s.Edge} received {s.Frames} frames"));
            output.WriteLine($"Routed {result.From} <-> {string.Join(", ", result.To)} (two-way, {result.Stopped} after {result.RunMs}ms). {received}.");
        }
        else if (result.Success)
        {
            string connect = result.ConnectTimeMs != null ? $"connected in {result.ConnectTimeMs}ms, " : string.Empty;
            string sinkList = string.Join("; ", result.Sinks.Select(s =>
            {
                string dropped = s.Dropped > 0 ? $", {s.Dropped} dropped" : string.Empty;
                return $"{s.Edge}: {s.Frames} frames ({s.Bytes} bytes){dropped}";
            }));
            string breakdown = result.AudioFramesRouted > 0
                ? $"{result.FramesRouted} frames routed ({result.VideoFramesRouted} video, {result.AudioFramesRouted} audio)"
                : $"{result.FramesRouted} frames routed";
            output.WriteLine($"Routed {result.From} -> [{string.Join(", ", result.To)}] ({connect}{result.Stopped} after {result.RunMs}ms). " +
                $"{breakdown}. {sinkList}.");
        }
        else
        {
            string arrow = result.Duplex ? "<->" : "->";
            Console.Error.WriteLine($"Route {result.From} {arrow} [{string.Join(", ", result.To)}] failed ({result.Stopped}): {result.Error}");
        }

        return exitCode;
    }
}
