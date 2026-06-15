//-----------------------------------------------------------------------------
// Filename: IceProbeCommand.cs
//
// Description: The "sipsorcery ice probe" verb. Runs ICE candidate gathering,
// optionally against STUN and TURN servers, and reports the candidates
// obtained. Answers "what connectivity paths does this machine have" and
// doubles as a STUN/TURN server health check: if a requested server type
// yields no candidate the probe fails.
//
// Note this verb deliberately stops at the ICE gathering stage. No SDP is
// exchanged and no DTLS handshake is attempted, which is why it lives under
// the "ice" noun rather than "webrtc".
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
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;

namespace SIPSorcery.Cli.Commands;

public sealed class IceProbeCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 10;

    private sealed record CandidateResult(
        string Type,
        string Protocol,
        string Address,
        ushort Port,
        string? RelatedAddress,
        ushort? RelatedPort);

    /// <summary>
    /// The result shape written to stdout with --json. Stable field names; additive changes only.
    /// </summary>
    private sealed record ProbeResult(
        bool Success,
        string GatheringState,
        long DurationMs,
        IReadOnlyList<CandidateResult> Candidates,
        string? Error);

    public IceProbeCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var stunOption = new Option<string[]>("--stun")
        {
            Description = "A STUN server to gather server reflexive candidates from, format stun:host[:port]. May be specified multiple times.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var turnOption = new Option<string[]>("--turn")
        {
            Description = "A TURN server to gather relay candidates from, format \"turn:host[:port];username;password\". May be specified multiple times.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var relayOnlyOption = new Option<bool>("--relay-only")
        {
            Description = "Only gather relay candidates (sets the ICE transport policy to relay)."
        };

        // Cloudflare TURN options (same as the "cloudflare turn" and "webrtc echo" verbs). When a key ID
        // and token are supplied, short lived TURN credentials are fetched and added as an ICE server
        // before gathering, so the probe doubles as a Cloudflare TURN health check.
        var keyIdOption = CloudflareTurn.CreateKeyIdOption();
        var tokenOption = CloudflareTurn.CreateTokenOption();
        var ttlOption = CloudflareTurn.CreateTtlOption();
        var transportOption = CloudflareTurn.CreateTransportOption();

        var command = new Command("probe", "Gather ICE candidates and report them. Fails if a requested STUN/TURN server produces no candidate.");
        command.Options.Add(stunOption);
        command.Options.Add(turnOption);
        command.Options.Add(relayOnlyOption);
        command.Options.Add(keyIdOption);
        command.Options.Add(tokenOption);
        command.Options.Add(ttlOption);
        command.Options.Add(transportOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(stunOption) ?? [],
            parseResult.GetValue(turnOption) ?? [],
            parseResult.GetValue(relayOnlyOption),
            parseResult.GetValue(keyIdOption),
            parseResult.GetValue(tokenOption),
            parseResult.GetValue(ttlOption),
            parseResult.GetValue(transportOption)!,
            parseResult.GetValue(TimeoutOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(string[] stunServers, string[] turnServers, bool relayOnly,
        string? turnKeyId, string? turnToken, int turnTtl, string turnTransport,
        int timeoutSeconds, bool asJson, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(IceProbeCommand));

        var iceServers = new List<RTCIceServer>();

        foreach (var stun in stunServers)
        {
            iceServers.Add(new RTCIceServer { urls = stun });
        }

        foreach (var turn in turnServers)
        {
            // Format matches the webrtccmdline example: url;username;password.
            string[] fields = turn.Split(';');
            if (fields.Length != 3 || string.IsNullOrWhiteSpace(fields[0]))
            {
                return WriteResult(asJson,
                    new ProbeResult(false, RTCIceGatheringState.@new.ToString(), 0, [],
                        $"Could not parse TURN server \"{turn}\". Expected format \"turn:host[:port];username;password\"."),
                    ExitCodes.InvalidArgument);
            }

            iceServers.Add(new RTCIceServer
            {
                urls = fields[0],
                username = fields[1],
                credential = fields[2],
                credentialType = RTCIceCredentialType.password
            });
        }

        // If Cloudflare TURN is requested, fetch credentials and add the TURN server to the ICE servers.
        CloudflareTurn.ResolveCredentials(ref turnKeyId, ref turnToken);
        bool cloudflareTurnRequested = !string.IsNullOrWhiteSpace(turnKeyId) || !string.IsNullOrWhiteSpace(turnToken);
        if (cloudflareTurnRequested)
        {
            if (string.IsNullOrWhiteSpace(turnKeyId) || string.IsNullOrWhiteSpace(turnToken))
            {
                return WriteResult(asJson,
                    new ProbeResult(false, RTCIceGatheringState.@new.ToString(), 0, [],
                        "Both a Cloudflare TURN key ID and token are required (--key-id/--token or CLOUDFLARE_TURN_KEY_ID/CLOUDFLARE_API_TOKEN)."),
                    ExitCodes.InvalidArgument);
            }

            if (!CloudflareTurn.TryResolveTurnUrl(turnTransport, out string turnUrl, out string? urlError))
            {
                return WriteResult(asJson,
                    new ProbeResult(false, RTCIceGatheringState.@new.ToString(), 0, [], urlError),
                    ExitCodes.InvalidArgument);
            }

            var fetch = await CloudflareTurn.FetchIceServerAsync(turnKeyId, turnToken, turnTtl, turnUrl, timeoutSeconds, logger, ct).ConfigureAwait(false);
            if (fetch.Error != null)
            {
                return WriteResult(asJson,
                    new ProbeResult(false, RTCIceGatheringState.@new.ToString(), 0, [], $"Could not obtain Cloudflare TURN credentials: {fetch.Error}"),
                    ExitCodes.Failed);
            }

            logger.LogDebug("Added Cloudflare TURN server {TurnUrl};{TurnUsername};{TurnCredential} to the ICE channel.", turnUrl, fetch.IceServer?.username, fetch.IceServer?.credential);
            iceServers.Add(fetch.IceServer!);
        }

        // A relay candidate is expected if either an explicit --turn server or Cloudflare TURN was requested.
        bool turnRequested = turnServers.Length > 0 || cloudflareTurnRequested;

        if (relayOnly && !turnRequested)
        {
            return WriteResult(asJson,
                new ProbeResult(false, RTCIceGatheringState.@new.ToString(), 0, [],
                    "--relay-only requires at least one --turn server or Cloudflare TURN (--key-id/--token)."),
                ExitCodes.InvalidArgument);
        }

        var iceChannel = new RtpIceChannel(
            null,
            RTCIceComponent.rtp,
            iceServers.Count > 0 ? iceServers : null,
            relayOnly ? RTCIceTransportPolicy.relay : RTCIceTransportPolicy.all,
            includeAllInterfaceAddresses: true);

        try
        {
            var gatheringComplete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            iceChannel.OnIceGatheringStateChange += (state) =>
            {
                if (state == RTCIceGatheringState.complete)
                {
                    gatheringComplete.TrySetResult(true);
                }
            };

            iceChannel.OnIceCandidateError += (candidate, error) =>
                loggerFactory.CreateLogger(nameof(IceProbeCommand)).LogWarning("ICE candidate error: {Error}", error);

            var stopwatch = Stopwatch.StartNew();

            iceChannel.StartGathering();

            var completed = await Task.WhenAny(gatheringComplete.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct)).ConfigureAwait(false);

            stopwatch.Stop();

            var candidates = iceChannel.Candidates
                .Select(x => new CandidateResult(
                    x.type.ToString(),
                    x.protocol.ToString(),
                    x.address,
                    x.port,
                    string.IsNullOrWhiteSpace(x.relatedAddress) ? null : x.relatedAddress,
                    x.relatedPort > 0 ? x.relatedPort : null))
                .ToList();

            if (completed != gatheringComplete.Task)
            {
                return WriteResult(asJson,
                    new ProbeResult(false, iceChannel.IceGatheringState.ToString(), stopwatch.ElapsedMilliseconds, candidates,
                        ct.IsCancellationRequested ? "Cancelled." : $"Gathering did not complete within {timeoutSeconds}s."),
                    ExitCodes.Timeout);
            }

            // The probe contract: each requested capability must have produced a candidate.
            string? error = null;
            if (stunServers.Length > 0 && !candidates.Any(x => x.Type == RTCIceCandidateType.srflx.ToString()))
            {
                error = "No server reflexive candidate was obtained from the STUN server(s).";
            }
            else if (turnRequested && !candidates.Any(x => x.Type == RTCIceCandidateType.relay.ToString()))
            {
                error = "No relay candidate was obtained from the TURN server(s).";
            }
            else if (candidates.Count == 0)
            {
                error = "No candidates were gathered.";
            }

            return WriteResult(asJson,
                new ProbeResult(error == null, iceChannel.IceGatheringState.ToString(), stopwatch.ElapsedMilliseconds, candidates, error),
                error == null ? ExitCodes.Ok : ExitCodes.Failed);
        }
        catch (Exception excp)
        {
            return WriteResult(asJson,
                new ProbeResult(false, iceChannel.IceGatheringState.ToString(), 0, [], excp.Message),
                ExitCodes.TransportError);
        }
        finally
        {
            iceChannel.Close("probe complete");
        }
    }

    private static int WriteResult(bool asJson, ProbeResult result, int exitCode)
    {
        if (asJson)
        {
            WriteJson(result);
        }
        else
        {
            foreach (var candidate in result.Candidates)
            {
                string related = candidate.RelatedAddress != null ? $" (related {candidate.RelatedAddress}:{candidate.RelatedPort})" : string.Empty;
                Console.WriteLine($"{candidate.Type,-6} {candidate.Protocol} {candidate.Address}:{candidate.Port}{related}");
            }

            int host = result.Candidates.Count(x => x.Type == "host");
            int srflx = result.Candidates.Count(x => x.Type == "srflx");
            int relay = result.Candidates.Count(x => x.Type == "relay");

            if (result.Success)
            {
                Console.WriteLine($"Gathering complete in {result.DurationMs}ms: {result.Candidates.Count} candidates ({host} host, {srflx} srflx, {relay} relay).");
            }
            else
            {
                Console.Error.WriteLine($"ICE probe failed after {result.DurationMs}ms ({host} host, {srflx} srflx, {relay} relay): {result.Error}");
            }
        }

        return exitCode;
    }
}
