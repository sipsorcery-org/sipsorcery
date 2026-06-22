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
//
// The graph repacketises, it does not transcode: frames travel encoded from source
// to sink. Transport sinks (whip/sip/livekit) and transcode transforms are later
// versions; the factory in Edges.cs is where they slot in.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 18 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
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

    /// <summary>The result shape written with --json. Stable field names; additive changes only.</summary>
    private sealed record RouteResult(
        bool Success,
        string From,
        string[] To,
        string Stopped,
        long FramesRouted,
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
            Description = "The source edge to pull a stream from: testpattern (a generated VP8 pattern) or whep:<url> (a live WebRTC stream).",
            Required = true
        };

        var toOption = new Option<string[]>("--to", "-o")
        {
            Description = "A sink edge to push the stream to: a file path (VP8->IVF, H264/H265->Annex B), \"play\" (ffplay), " +
                          "\"null\" (discard) or \"-\" (bitstream on stdout). Repeat --to to fan out to several sinks at once.",
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
            Description = "Frame rate for a generated source (testpattern).",
            DefaultValueFactory = _ => DEFAULT_FPS
        };

        var tokenOption = new Option<string?>("--token")
        {
            Description = "Optional bearer token for a transport source (e.g. a whep stream key)."
        };

        var command = new Command("route",
            "Route a media stream from a --from source edge to one or more --to sink edges (a v0.1 stream graph).");
        command.Options.Add(fromOption);
        command.Options.Add(toOption);
        command.Options.Add(durationOption);
        command.Options.Add(fpsOption);
        command.Options.Add(tokenOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(fromOption)!,
            parseResult.GetValue(toOption)!,
            parseResult.GetValue(durationOption),
            parseResult.GetValue(fpsOption),
            parseResult.GetValue(tokenOption),
            parseResult.GetValue(TimeoutOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(string from, string[] to, int durationSeconds, int fps, string? token,
        int timeoutSeconds, bool asJson, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(RouteCommand));

        // When a sink claims stdout (--to -), the result is commentary and moves to stderr so the
        // bitstream on stdout stays a single clean payload.
        bool stdoutClaimed = to.Contains("-");

        var edgeOptions = new EdgeOptions(fps, token, timeoutSeconds);

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
                new RouteResult(false, from, to, "invalid source", 0, null, 0, Array.Empty<SinkReport>(), ex.Message),
                ExitCodes.InvalidArgument);
        }

        try
        {
            foreach (var spec in to)
            {
                sinks.Add(EdgeFactory.CreateSink(spec, logger));
            }
        }
        catch (EdgeException ex)
        {
            await DisposeAllAsync(source, sinks).ConfigureAwait(false);
            return WriteResult(asJson, stdoutClaimed,
                new RouteResult(false, from, to, "invalid sink", 0, null, 0, Array.Empty<SinkReport>(), ex.Message),
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
            new RouteResult(success, from, to, stopped, framesRouted, connectTimeMs, (int)stopwatch.ElapsedMilliseconds,
                sinkReports.ToArray(), error),
            exitCode);
    }

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
            output.WriteLine($"Routed {result.From} -> [{string.Join(", ", result.To)}] ({connect}{result.Stopped} after {result.RunMs}ms). " +
                $"{result.FramesRouted} frames routed. {sinkList}.");
        }
        else
        {
            Console.Error.WriteLine($"Route {result.From} -> [{string.Join(", ", result.To)}] failed ({result.Stopped}): {result.Error}");
        }

        return exitCode;
    }
}
