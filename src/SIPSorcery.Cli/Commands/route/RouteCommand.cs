//-----------------------------------------------------------------------------
// Filename: RouteCommand.cs
//
// Description: The "sipsorcery-diags route" verb: the v0.1 of the stream router.
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
// The graph repacketises, it does not transcode: frames travel encoded from source
// to sink. The scope video is the exception by design - it is rendered and encoded
// by an external ffmpeg process, not a managed node (see AudioScopeTransform).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 18 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
// 23 Jun 2026	Aaron Clauson	Added the sip: source, whip: sink and the --scope audio-scope video.
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
using SIPSorcery.Cli.Commands.Route;

namespace SIPSorcery.Cli.Commands;

public sealed class RouteCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 30;
    private const int DEFAULT_DURATION_SECONDS = 10;
    private const int DEFAULT_FPS = 30;
    private const string DEFAULT_AUDIO_CODEC = "pcmu";
    private const string DEFAULT_SCOPE_MODE = "waves";
    private const string DEFAULT_SCOPE_SIZE = "640x360";

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
        string? Error);

    private sealed record SinkReport(string Edge, int Frames, long Bytes, int Dropped);

    public RouteCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var fromOption = new Option<string>("--from", "-f")
        {
            Description = "The source edge to pull a stream from: testpattern (a generated H264 pattern + PCMU music), " +
                          "whep:<url> (a live WebRTC stream) or sip:<uri> (place a SIP call and forward its audio, e.g. sip:music@iptel.org).",
            Required = true
        };

        var toOption = new Option<string[]>("--to", "-o")
        {
            Description = "A sink edge to push the stream to: a file path (VP8->IVF, H264/H265->Annex B), \"play\" (ffplay), " +
                          "\"null\" (discard), \"-\" (bitstream on stdout) or whip:<url> (publish to a WebRTC endpoint). " +
                          "Repeat --to to fan out to several sinks at once.",
            Required = true,
            AllowMultipleArgumentsPerToken = true
        };

        var durationOption = new Option<int>("--duration", "-d")
        {
            Description = "Seconds to run for. 0 runs until the source ends (a live source hanging up) or ctrl-c.",
            DefaultValueFactory = _ => DEFAULT_DURATION_SECONDS
        };

        var fpsOption = new Option<int>("--fps")
        {
            Description = "Frame rate for a generated source (testpattern) and for the --scope video.",
            DefaultValueFactory = _ => DEFAULT_FPS
        };

        var tokenOption = new Option<string?>("--token")
        {
            Description = "Optional bearer token for a transport edge (a whep stream key, or the whip endpoint Authorization)."
        };

        var scopeOption = new Option<bool>("--scope")
        {
            Description = "For an audio source (sip:), add a generated \"audio scope\" video track that visualises the audio."
        };

        var scopeModeOption = new Option<string>("--scope-mode")
        {
            Description = "Scope visualisation: waves (a moving waveform) or spectrum (a scrolling frequency spectrum).",
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
            Description = "Optional username for authenticating a sip: source call."
        };

        var passwordOption = new Option<string?>("--password")
        {
            Description = "Optional password for authenticating a sip: source call."
        };

        var audioCodecOption = new Option<string>("--audio-codec")
        {
            Description = "Audio codec a whip: sink offers / the source produces: pcmu (default), pcma or opus. " +
                          "Some WebRTC endpoints (e.g. Broadcast Box) only accept opus; a G.711 sip: call is transcoded up to opus when needed.",
            DefaultValueFactory = _ => DEFAULT_AUDIO_CODEC
        };

        var command = new Command("route",
            "Route a media stream from a --from source edge to one or more --to sink edges (a v0.1 stream graph).");
        command.Options.Add(fromOption);
        command.Options.Add(toOption);
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
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(fromOption)!,
            parseResult.GetValue(toOption)!,
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
            parseResult.GetValue(TimeoutOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(string from, string[] to, int durationSeconds, int fps, string? token,
        bool scope, string scopeMode, string scopeSize, string? ffmpegPath, string? username, string? password,
        string audioCodec, int timeoutSeconds, bool asJson, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(RouteCommand));

        // When a sink claims stdout (--to -), the result is commentary and moves to stderr so the
        // bitstream on stdout stays a single clean payload.
        bool stdoutClaimed = to.Contains("-");

        // The scope video derives from audio, so it only applies to a sip: source.
        if (scope && !IsSipSource(from))
        {
            return WriteResult(asJson, stdoutClaimed,
                new RouteResult(false, from, to, "invalid argument", 0, 0, 0, null, 0, Array.Empty<SinkReport>(),
                    "The --scope option needs an audio source, e.g. --from sip:music@iptel.org."),
                ExitCodes.InvalidArgument);
        }

        // Validate the audio codec up front for a clean error before any edge is built.
        if (!RouteAudio.TryResolveCodec(audioCodec, out _, out string? audioCodecError))
        {
            return WriteResult(asJson, stdoutClaimed,
                new RouteResult(false, from, to, "invalid argument", 0, 0, 0, null, 0, Array.Empty<SinkReport>(), audioCodecError),
                ExitCodes.InvalidArgument);
        }

        var edgeOptions = new EdgeOptions(fps, token, timeoutSeconds, scope, scopeMode, scopeSize, ffmpegPath, username, password, audioCodec);

        // Build the edges. A bad spec is an argument error before anything starts.
        ISourceNode source;
        var sinks = new List<ISinkNode>();
        try
        {
            source = EdgeFactory.CreateSource(from, edgeOptions, logger);
        }
        catch (EdgeException ex)
        {
            return WriteResult(asJson, stdoutClaimed,
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
            return WriteResult(asJson, stdoutClaimed,
                new RouteResult(false, from, to, "invalid sink", 0, 0, 0, null, 0, Array.Empty<SinkReport>(), ex.Message),
                ExitCodes.InvalidArgument);
        }

        var graph = new StreamGraph(source, sinks);

        string stopped;
        string? error = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            stopped = await graph.RunAsync(durationSeconds, ct).ConfigureAwait(false);
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

        return WriteResult(asJson, stdoutClaimed,
            new RouteResult(success, from, to, stopped, framesRouted, graph.VideoFramesRouted, graph.AudioFramesRouted,
                connectTimeMs, (int)stopwatch.ElapsedMilliseconds, sinkReports.ToArray(), error),
            exitCode);
    }

    /// <summary>
    /// Whether a --from spec resolves to a SIP (audio) source: an explicit sip:/sips: scheme, or a
    /// bare user@host. Matches the SIP cases in <see cref="EdgeFactory.CreateSource"/>.
    /// </summary>
    private static bool IsSipSource(string from) =>
        from.StartsWith("sip:", StringComparison.OrdinalIgnoreCase) ||
        from.StartsWith("sips:", StringComparison.OrdinalIgnoreCase) ||
        (from.Contains('@') && !from.Contains(':'));

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
            Console.Error.WriteLine($"Route {result.From} -> [{string.Join(", ", result.To)}] failed ({result.Stopped}): {result.Error}");
        }

        return exitCode;
    }
}
