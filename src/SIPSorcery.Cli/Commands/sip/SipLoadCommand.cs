//-----------------------------------------------------------------------------
// Filename: SipLoadCommand.cs
//
// Description: The "sipsorcery sip load" verb. Generates SIP OPTIONS load against
// a destination using a pool of concurrent workers and reports aggregate timing
// and success statistics. The equivalent of the sipcmdline load harness
// (-c count, -x concurrent, -p period, -b breakonfail) applied to the OPTIONS
// scenario.
//
// A single SIP transport is shared across all workers; responses are correlated
// back to their request by Call-ID.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 13 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.CommandLine;
using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;

namespace SIPSorcery.Cli.Commands;

public sealed class SipLoadCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 5;

    /// <summary>
    /// The result shape written to stdout with --json. Stable field names; additive changes only.
    /// </summary>
    private sealed record LoadResult(
        bool Success,
        string Destination,
        int Requested,
        int Attempts,
        int Successes,
        int Failures,
        long DurationMs,
        double RequestsPerSecond,
        long? MinMs,
        long? AvgMs,
        long? MaxMs,
        string? Error);

    public SipLoadCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var destinationArg = new Argument<string>("destination")
        {
            Description = "The SIP destination to send OPTIONS requests to, e.g. iptel.org, tcp:sip.example.com:5060."
        };

        var countOption = new Option<int>("--count", "-c")
        {
            Description = "The total number of OPTIONS requests to send.",
            DefaultValueFactory = _ => 1
        };

        var concurrentOption = new Option<int>("--concurrent", "-x")
        {
            Description = "The number of requests to keep in flight concurrently.",
            DefaultValueFactory = _ => 1
        };

        var periodOption = new Option<int>("--period", "-p")
        {
            Description = "Seconds each worker waits between successful requests.",
            DefaultValueFactory = _ => 0
        };

        var breakOption = new Option<bool>("--break-on-fail", "-b")
        {
            Description = "Stop the run as soon as a single request fails."
        };

        var sourcePortOption = new Option<int>("--source-port")
        {
            Description = "The local SIP port to send from. 0 uses an ephemeral port.",
            DefaultValueFactory = _ => 0
        };

        var ipv6Option = new Option<bool>("--ipv6")
        {
            Description = "Prefer IPv6 when resolving the destination."
        };

        var hepOption = HepCapture.CreateOption();

        var command = new Command("load", "Generate concurrent SIP OPTIONS load and report aggregate statistics (sipcmdline load).");
        command.Arguments.Add(destinationArg);
        command.Options.Add(countOption);
        command.Options.Add(concurrentOption);
        command.Options.Add(periodOption);
        command.Options.Add(breakOption);
        command.Options.Add(sourcePortOption);
        command.Options.Add(ipv6Option);
        command.Options.Add(hepOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(destinationArg)!,
            parseResult.GetValue(countOption),
            parseResult.GetValue(concurrentOption),
            parseResult.GetValue(periodOption),
            parseResult.GetValue(breakOption),
            parseResult.GetValue(sourcePortOption),
            parseResult.GetValue(ipv6Option),
            parseResult.GetValue(hepOption),
            parseResult.GetValue(TimeoutOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(string destination, int count, int concurrent, int period, bool breakOnFail,
        int sourcePort, bool preferIPv6, string? hep, int timeoutSeconds, bool asJson, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(SipLoadCommand));

        if (!SipDestination.TryParse(destination, out var dstUri, out var parseError))
        {
            return WriteResult(asJson, new LoadResult(false, destination, count, 0, 0, 0, 0, 0, null, null, null, parseError), ExitCodes.InvalidArgument);
        }

        if (count < 1 || concurrent < 1)
        {
            return WriteResult(asJson, new LoadResult(false, dstUri.ToString(), count, 0, 0, 0, 0, 0, null, null, null,
                "--count and --concurrent must be at least 1."), ExitCodes.InvalidArgument);
        }

        using var hepCapture = HepCapture.Create(hep, logger, out string? hepError);

        if (hepError != null)
        {
            return WriteResult(asJson, new LoadResult(false, dstUri.ToString(), count, 0, 0, 0, 0, 0, null, null, null, hepError), ExitCodes.InvalidArgument);
        }

        var sipTransport = new SIPTransport { PreferIPv6NameResolution = preferIPv6 };
        hepCapture?.Attach(sipTransport);

        if (sourcePort != 0)
        {
            var channel = sipTransport.CreateChannel(dstUri.Protocol,
                preferIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork, sourcePort);
            sipTransport.AddSIPChannel(channel);
        }

        if (verbose)
        {
            sipTransport.EnableTraceLogs();
        }

        // Responses are matched to their request by Call-ID so a single transport can carry many
        // concurrent OPTIONS exchanges.
        var pending = new ConcurrentDictionary<string, TaskCompletionSource<bool>>();
        sipTransport.SIPTransportResponseReceived += (localEP, remoteEP, response) =>
        {
            if (response.Header.CSeqMethod == SIPMethodsEnum.OPTIONS && pending.TryGetValue(response.Header.CallId, out var tcs))
            {
                tcs.TrySetResult(true);
            }
            return Task.CompletedTask;
        };

        int attempts = 0;
        int successes = 0;
        int failures = 0;
        var latencies = new ConcurrentBag<long>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var workers = new List<Task>();
            for (int i = 0; i < concurrent; i++)
            {
                workers.Add(Task.Run(async () =>
                {
                    while (!cts.IsCancellationRequested && Interlocked.Increment(ref attempts) <= count)
                    {
                        bool ok = await SendOptionsAsync(sipTransport, dstUri, pending, timeoutSeconds, latencies, cts.Token).ConfigureAwait(false);

                        if (ok)
                        {
                            Interlocked.Increment(ref successes);
                        }
                        else
                        {
                            Interlocked.Increment(ref failures);
                            if (breakOnFail)
                            {
                                cts.Cancel();
                                break;
                            }
                        }

                        if (period > 0)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(period), cts.Token).ConfigureAwait(false);
                        }
                    }
                }, cts.Token));
            }

            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // break-on-fail cancellation; the result below reports the failure.
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return WriteResult(asJson, BuildResult(dstUri.ToString(), count, attempts, successes, failures, stopwatch, latencies, "Cancelled."), ExitCodes.Timeout);
        }
        finally
        {
            sipTransport.Shutdown();
        }

        stopwatch.Stop();

        // attempts is incremented past count by the workers' loop check; clamp it to the real send count.
        int actualAttempts = successes + failures;
        string? error = failures > 0 ? $"{failures} of {actualAttempts} request(s) failed." : null;

        return WriteResult(asJson,
            BuildResult(dstUri.ToString(), count, actualAttempts, successes, failures, stopwatch, latencies, error),
            failures == 0 ? ExitCodes.Ok : ExitCodes.Failed);
    }

    private static async Task<bool> SendOptionsAsync(SIPTransport sipTransport, SIPURI dstUri,
        ConcurrentDictionary<string, TaskCompletionSource<bool>> pending, int timeoutSeconds, ConcurrentBag<long> latencies, CancellationToken ct)
    {
        var request = SIPRequest.GetRequest(SIPMethodsEnum.OPTIONS, dstUri);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[request.Header.CallId] = tcs;

        var sw = Stopwatch.StartNew();

        try
        {
            var sendResult = await sipTransport.SendRequestAsync(request, true).ConfigureAwait(false);
            if (sendResult != SocketError.Success)
            {
                return false;
            }

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct)).ConfigureAwait(false);
            sw.Stop();

            if (completed == tcs.Task)
            {
                latencies.Add(sw.ElapsedMilliseconds);
                return true;
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            pending.TryRemove(request.Header.CallId, out _);
        }
    }

    private static LoadResult BuildResult(string destination, int requested, int attempts, int successes, int failures,
        Stopwatch stopwatch, ConcurrentBag<long> latencies, string? error)
    {
        double seconds = stopwatch.Elapsed.TotalSeconds;
        double rps = seconds > 0 ? Math.Round(attempts / seconds, 2) : 0;

        long? min = latencies.IsEmpty ? null : latencies.Min();
        long? max = latencies.IsEmpty ? null : latencies.Max();
        long? avg = latencies.IsEmpty ? null : (long)latencies.Average();

        return new LoadResult(failures == 0 && attempts > 0, destination, requested, attempts, successes, failures,
            stopwatch.ElapsedMilliseconds, rps, min, avg, max, error);
    }

    private static int WriteResult(bool asJson, LoadResult result, int exitCode)
    {
        if (asJson)
        {
            WriteJson(result);
        }
        else
        {
            string latency = result.AvgMs != null ? $" latency min/avg/max {result.MinMs}/{result.AvgMs}/{result.MaxMs}ms," : string.Empty;
            string line = $"Load to {result.Destination}: {result.Successes}/{result.Attempts} succeeded,{latency} " +
                $"{result.RequestsPerSecond} req/s over {result.DurationMs}ms.";

            if (result.Success)
            {
                Console.WriteLine(line);
            }
            else
            {
                Console.Error.WriteLine(result.Error != null ? $"{line} {result.Error}" : line);
            }
        }

        return exitCode;
    }
}
