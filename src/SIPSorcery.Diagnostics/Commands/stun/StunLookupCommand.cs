//-----------------------------------------------------------------------------
// Filename: StunLookupCommand.cs
//
// Description: The "sipsorcery stun lookup" verb. Sends a STUN binding request
// to a server and reports the mapped (public) address and port, i.e. answers
// "what does the internet see when this machine sends UDP?".
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 12 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.CommandLine;
using System.Diagnostics;
using System.Net;
using SIPSorcery.Net;

namespace SIPSorcery.Diagnostics.Commands;

public sealed class StunLookupCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 5;

    /// <summary>
    /// The result shape written to stdout with --json. Stable field names; additive changes only.
    /// </summary>
    private sealed record LookupResult(
        bool Success,
        string Server,
        string? MappedAddress,
        int? MappedPort,
        long? DurationMs,
        string? Error);

    public StunLookupCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var serverArg = new Argument<string>("server")
        {
            Description = "The STUN server in the form [stun:]host[:port], e.g. stun.cloudflare.com, " +
                          "stun:stun.l.google.com:19302. The port defaults to 3478."
        };

        var command = new Command("lookup", "Send a STUN binding request and report this machine's public IP address and port.");
        command.Arguments.Add(serverArg);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(serverArg)!,
            parseResult.GetValue(TimeoutOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(string server, int timeoutSeconds, bool asJson, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);

        if (!STUNUri.TryParse(server, out var stunUri) || string.IsNullOrWhiteSpace(stunUri.Host) || stunUri.Host.Contains(' '))
        {
            return WriteResult(asJson,
                new LookupResult(false, server, null, null, null, $"Could not parse \"{server}\" as a STUN server."),
                ExitCodes.InvalidArgument);
        }

        string serverDescription = $"{stunUri.Host}:{stunUri.Port}";

        try
        {
            var stopwatch = Stopwatch.StartNew();

            // GetPublicIPEndPoint is synchronous with its own internal response timeout, so run it
            // on the thread pool and race it against this command's timeout.
            var lookupTask = Task.Run(() => STUNClient.GetPublicIPEndPoint(stunUri.Host, stunUri.Port), ct);
            var completed = await Task.WhenAny(lookupTask, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct)).ConfigureAwait(false);

            if (completed != lookupTask)
            {
                return WriteResult(asJson,
                    new LookupResult(false, serverDescription, null, null, stopwatch.ElapsedMilliseconds,
                        ct.IsCancellationRequested ? "Cancelled." : $"No response received within {timeoutSeconds}s."),
                    ExitCodes.Timeout);
            }

            stopwatch.Stop();

            IPEndPoint? mappedEndPoint = await lookupTask.ConfigureAwait(false);

            if (mappedEndPoint == null)
            {
                return WriteResult(asJson,
                    new LookupResult(false, serverDescription, null, null, stopwatch.ElapsedMilliseconds,
                        "No binding response was received from the server."),
                    ExitCodes.Failed);
            }

            return WriteResult(asJson,
                new LookupResult(true, serverDescription, mappedEndPoint.Address.ToString(), mappedEndPoint.Port,
                    stopwatch.ElapsedMilliseconds, null),
                ExitCodes.Ok);
        }
        catch (Exception excp)
        {
            return WriteResult(asJson,
                new LookupResult(false, serverDescription, null, null, null, excp.Message),
                ExitCodes.TransportError);
        }
    }

    private static int WriteResult(bool asJson, LookupResult result, int exitCode)
    {
        if (asJson)
        {
            WriteJson(result);
        }
        else if (result.Success)
        {
            Console.WriteLine($"Public end point {result.MappedAddress}:{result.MappedPort} (via {result.Server} in {result.DurationMs}ms).");
        }
        else
        {
            Console.Error.WriteLine($"STUN lookup via {result.Server} failed: {result.Error}");
        }

        return exitCode;
    }
}
