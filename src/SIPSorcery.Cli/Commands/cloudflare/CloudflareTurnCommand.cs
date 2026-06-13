//-----------------------------------------------------------------------------
// Filename: CloudflareTurnCommand.cs
//
// Description: The "sipsorcery cloudflare turn" verb. Requests short lived TURN
// credentials from the Cloudflare Realtime TURN API and then runs a relay only
// ICE gather against the Cloudflare TURN server to confirm a relay candidate is
// allocated. Answers "are my Cloudflare TURN credentials valid and does relay
// actually work from here".
//
// See: https://developers.cloudflare.com/realtime/turn/
// Credentials: https://developers.cloudflare.com/realtime/turn/generate-credentials/
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

using System.CommandLine;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;

namespace SIPSorcery.Cli.Commands;

public sealed class CloudflareTurnCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 15;

    /// <summary>
    /// The result shape written to stdout with --json. Stable field names; additive changes only.
    /// The credential itself is deliberately not included.
    /// </summary>
    private sealed record TurnResult(
        bool Success,
        string KeyId,
        int Ttl,
        string TurnUrl,
        string? Username,
        int RelayCandidates,
        long GatheringMs,
        string? Error);

    public CloudflareTurnCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var keyIdOption = CloudflareTurn.CreateKeyIdOption();
        var tokenOption = CloudflareTurn.CreateTokenOption();
        var ttlOption = CloudflareTurn.CreateTtlOption();
        var transportOption = CloudflareTurn.CreateTransportOption();

        var command = new Command("turn", "Fetch Cloudflare TURN credentials and verify a relay candidate can be allocated.");
        command.Options.Add(keyIdOption);
        command.Options.Add(tokenOption);
        command.Options.Add(ttlOption);
        command.Options.Add(transportOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
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

    private static async Task<int> RunAsync(string? keyId, string? token, int ttl, string transport,
        int timeoutSeconds, bool asJson, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(CloudflareTurnCommand));

        CloudflareTurn.ResolveCredentials(ref keyId, ref token);

        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(token))
        {
            return WriteResult(asJson,
                new TurnResult(false, keyId ?? string.Empty, ttl, string.Empty, null, 0, 0,
                    "A TURN key ID and API token are required (--key-id/--token or CLOUDFLARE_TURN_KEY_ID/CLOUDFLARE_API_TOKEN)."),
                ExitCodes.InvalidArgument);
        }

        if (!CloudflareTurn.TryResolveTurnUrl(transport, out string turnUrl, out string? urlError))
        {
            return WriteResult(asJson,
                new TurnResult(false, keyId, ttl, string.Empty, null, 0, 0, urlError),
                ExitCodes.InvalidArgument);
        }

        var fetch = await CloudflareTurn.FetchIceServerAsync(keyId, token, ttl, turnUrl, timeoutSeconds, logger, ct).ConfigureAwait(false);

        if (fetch.Error != null)
        {
            return WriteResult(asJson,
                new TurnResult(false, keyId, ttl, turnUrl, null, 0, 0, fetch.Error),
                ExitCodes.Failed);
        }

        string? username = fetch.Username;

        // Relay only ICE gather against the Cloudflare TURN server with the fetched credentials.
        var iceServer = fetch.IceServer!;

        var iceChannel = new RtpIceChannel(
            null,
            RTCIceComponent.rtp,
            new List<RTCIceServer> { iceServer },
            RTCIceTransportPolicy.relay,
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

            iceChannel.OnIceCandidateError += (candidate, error) => logger.LogWarning("ICE candidate error: {Error}", error);

            var stopwatch = Stopwatch.StartNew();
            iceChannel.StartGathering();

            // A relay candidate can appear before gathering formally completes, so also resolve early
            // once at least one relay candidate is present.
            var relaySeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            iceChannel.OnIceCandidate += (candidate) =>
            {
                if (candidate.type == RTCIceCandidateType.relay)
                {
                    relaySeen.TrySetResult(true);
                }
            };

            await Task.WhenAny(
                Task.WhenAny(gatheringComplete.Task, relaySeen.Task),
                Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct)).ConfigureAwait(false);

            stopwatch.Stop();

            int relayCount = iceChannel.Candidates.Count(x => x.type == RTCIceCandidateType.relay);

            string? error = relayCount == 0
                ? "No relay candidate was allocated from the Cloudflare TURN server (credentials valid but relay failed, check egress to the TURN transport)."
                : null;

            return WriteResult(asJson,
                new TurnResult(error == null, keyId, ttl, turnUrl, username, relayCount, stopwatch.ElapsedMilliseconds, error),
                error == null ? ExitCodes.Ok : ExitCodes.Failed);
        }
        catch (Exception excp)
        {
            return WriteResult(asJson,
                new TurnResult(false, keyId, ttl, turnUrl, username, 0, 0, excp.Message),
                ExitCodes.TransportError);
        }
        finally
        {
            iceChannel.Close("turn probe complete");
        }
    }

    private static int WriteResult(bool asJson, TurnResult result, int exitCode)
    {
        if (asJson)
        {
            WriteJson(result);
        }
        else if (result.Success)
        {
            Console.WriteLine($"Cloudflare TURN OK: obtained credentials for key {result.KeyId} and allocated " +
                $"{result.RelayCandidates} relay candidate(s) via {result.TurnUrl} in {result.GatheringMs}ms.");
        }
        else
        {
            Console.Error.WriteLine($"Cloudflare TURN check failed: {result.Error}");
        }

        return exitCode;
    }
}
