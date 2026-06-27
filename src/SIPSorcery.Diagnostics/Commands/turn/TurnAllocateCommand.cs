//-----------------------------------------------------------------------------
// Filename: TurnAllocateCommand.cs
//
// Description: The "sipsorcery-diags turn allocate" verb. Checks that a single
// TURN server can allocate a relay socket: it builds a relay-only ICE channel
// pointed at just that server, runs gathering, and succeeds only if a relay
// candidate (the allocated relay transport address) comes back. Answers "can I
// get a relay off this TURN server with these credentials" and reports the
// allocated relay address and the server-reflexive (mapped) address it saw.
//
// The server is either an explicit "turn:" / "turns:" URL with --username and
// --password, or, like "ice gather", short lived Cloudflare TURN credentials
// fetched from the Realtime API when --key-id/--token (or the CLOUDFLARE_TURN_KEY_ID
// / CLOUDFLARE_API_TOKEN environment variables) are supplied.
//
// Unlike "ice gather --turn", this is deliberately single server, relay only and
// reports the allocation itself, so it is a focused TURN health/credential check.
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

using System.CommandLine;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;

namespace SIPSorcery.Diagnostics.Commands;

public sealed class TurnAllocateCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 10;

    /// <summary>
    /// The result shape written to stdout with --json. Stable field names; additive changes only.
    /// The relay address/port is the allocated relay transport address on the TURN server; the mapped
    /// address/port is the server-reflexive address the server observed for this client (the relay
    /// candidate's related address).
    /// </summary>
    private sealed record TurnAllocateResult(
        bool Success,
        string Server,
        string GatheringState,
        long DurationMs,
        string? RelayProtocol,
        string? RelayAddress,
        int? RelayPort,
        string? MappedAddress,
        int? MappedPort,
        string? Error);

    public TurnAllocateCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var urlArg = new Argument<string?>("url")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "The TURN server URL, e.g. turn:turn.example.com:3478, turns:turn.example.com:5349 or " +
                          "turn:host:3478?transport=tcp. Optional when Cloudflare credentials are supplied."
        };

        var usernameOption = new Option<string?>("--username", "-u")
        {
            Description = "The TURN username (long term credential). Not needed with Cloudflare credentials."
        };

        var passwordOption = new Option<string?>("--password", "-p")
        {
            Description = "The TURN password/credential. Not needed with Cloudflare credentials."
        };

        // Cloudflare TURN options (same as "ice gather"). When a key ID and token are supplied (or via
        // the CLOUDFLARE_TURN_KEY_ID / CLOUDFLARE_API_TOKEN environment variables) short lived
        // credentials are fetched and that relay is checked, so no explicit url/-u/-p is needed.
        var keyIdOption = CloudflareTurn.CreateKeyIdOption();
        var tokenOption = CloudflareTurn.CreateTokenOption();
        var ttlOption = CloudflareTurn.CreateTtlOption();
        var transportOption = CloudflareTurn.CreateTransportOption();

        var command = new Command("allocate",
            "Allocate a relay socket on a single TURN server and report it. Fails if no relay candidate is obtained.");
        command.Arguments.Add(urlArg);
        command.Options.Add(usernameOption);
        command.Options.Add(passwordOption);
        command.Options.Add(keyIdOption);
        command.Options.Add(tokenOption);
        command.Options.Add(ttlOption);
        command.Options.Add(transportOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(urlArg),
            parseResult.GetValue(usernameOption),
            parseResult.GetValue(passwordOption),
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

    private static async Task<int> RunAsync(string? url, string? username, string? password,
        string? turnKeyId, string? turnToken, int turnTtl, string turnTransport,
        int timeoutSeconds, bool asJson, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(TurnAllocateCommand));

        // Resolve the single TURN server to check: an explicit url (with -u/-p), or Cloudflare
        // credentials fetched from the Realtime API (--key-id/--token or the CLOUDFLARE_* env vars).
        RTCIceServer iceServer;
        string server;

        if (!string.IsNullOrWhiteSpace(url))
        {
            if (!url.StartsWith("turn:", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("turns:", StringComparison.OrdinalIgnoreCase))
            {
                return WriteResult(asJson,
                    new TurnAllocateResult(false, url, RTCIceGatheringState.@new.ToString(), 0, null, null, null, null, null,
                        $"Expected a TURN URL starting with \"turn:\" or \"turns:\". Got \"{url}\"."),
                    ExitCodes.InvalidArgument);
            }

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                // Not fatal (some deployments authenticate differently), but the common case needs both.
                logger.LogWarning("No TURN username and/or password supplied (-u/-p); most servers reject the allocation without long term credentials.");
            }

            iceServer = new RTCIceServer
            {
                urls = url,
                username = username,
                credential = password,
                credentialType = RTCIceCredentialType.password
            };
            server = url;
        }
        else
        {
            CloudflareTurn.ResolveCredentials(ref turnKeyId, ref turnToken);

            if (string.IsNullOrWhiteSpace(turnKeyId) && string.IsNullOrWhiteSpace(turnToken))
            {
                return WriteResult(asJson,
                    new TurnAllocateResult(false, string.Empty, RTCIceGatheringState.@new.ToString(), 0, null, null, null, null, null,
                        "Provide a TURN url (with -u/-p) or Cloudflare credentials (--key-id/--token or CLOUDFLARE_TURN_KEY_ID/CLOUDFLARE_API_TOKEN)."),
                    ExitCodes.InvalidArgument);
            }

            if (string.IsNullOrWhiteSpace(turnKeyId) || string.IsNullOrWhiteSpace(turnToken))
            {
                return WriteResult(asJson,
                    new TurnAllocateResult(false, string.Empty, RTCIceGatheringState.@new.ToString(), 0, null, null, null, null, null,
                        "Both a Cloudflare TURN key ID and token are required (--key-id/--token or CLOUDFLARE_TURN_KEY_ID/CLOUDFLARE_API_TOKEN)."),
                    ExitCodes.InvalidArgument);
            }

            if (!CloudflareTurn.TryResolveTurnUrl(turnTransport, out string turnUrl, out string? urlError))
            {
                return WriteResult(asJson,
                    new TurnAllocateResult(false, string.Empty, RTCIceGatheringState.@new.ToString(), 0, null, null, null, null, null, urlError),
                    ExitCodes.InvalidArgument);
            }

            var fetch = await CloudflareTurn.FetchIceServerAsync(turnKeyId, turnToken, turnTtl, turnUrl, timeoutSeconds, logger, ct).ConfigureAwait(false);
            if (fetch.Error != null)
            {
                return WriteResult(asJson,
                    new TurnAllocateResult(false, turnUrl, RTCIceGatheringState.@new.ToString(), 0, null, null, null, null, null,
                        $"Could not obtain Cloudflare TURN credentials: {fetch.Error}"),
                    ExitCodes.Failed);
            }

            iceServer = fetch.IceServer!;
            server = turnUrl;
        }

        // Relay only so the only candidate that can appear is the relay allocated on this TURN server.
        var iceChannel = new RtpIceChannel(
            null,
            RTCIceComponent.rtp,
            new List<RTCIceServer> { iceServer },
            RTCIceTransportPolicy.relay,
            includeAllInterfaceAddresses: true);

        try
        {
            // Complete as soon as a relay candidate is gathered, or when gathering finishes, whichever
            // comes first; the relay candidate is the allocation we are checking for.
            var allocated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            iceChannel.OnIceCandidate += (candidate) =>
            {
                if (candidate.type == RTCIceCandidateType.relay)
                {
                    allocated.TrySetResult(true);
                }
            };
            iceChannel.OnIceGatheringStateChange += (state) =>
            {
                if (state == RTCIceGatheringState.complete)
                {
                    // Gathering finished; resolve with whatever was (or was not) allocated.
                    allocated.TrySetResult(iceChannel.Candidates.Any(x => x.type == RTCIceCandidateType.relay));
                }
            };
            iceChannel.OnIceCandidateError += (candidate, error) =>
                logger.LogWarning("ICE candidate error: {Error}", error);

            var stopwatch = Stopwatch.StartNew();

            iceChannel.StartGathering();

            var completed = await Task.WhenAny(allocated.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct)).ConfigureAwait(false);

            stopwatch.Stop();

            var relay = iceChannel.Candidates.FirstOrDefault(x => x.type == RTCIceCandidateType.relay);

            if (completed != allocated.Task || relay == null)
            {
                return WriteResult(asJson,
                    new TurnAllocateResult(false, server, iceChannel.IceGatheringState.ToString(), stopwatch.ElapsedMilliseconds,
                        null, null, null, null, null,
                        ct.IsCancellationRequested ? "Cancelled."
                            : completed != allocated.Task ? $"No relay was allocated within {timeoutSeconds}s (the server did not respond, or is unreachable on the given transport)."
                            : "Gathering completed but the TURN server did not allocate a relay candidate. Check the credentials and that the server permits relay allocations."),
                    ExitCodes.Failed);
            }

            return WriteResult(asJson,
                new TurnAllocateResult(true, server, iceChannel.IceGatheringState.ToString(), stopwatch.ElapsedMilliseconds,
                    relay.protocol.ToString(), relay.address, relay.port,
                    string.IsNullOrWhiteSpace(relay.relatedAddress) ? null : relay.relatedAddress,
                    relay.relatedPort > 0 ? relay.relatedPort : null,
                    null),
                ExitCodes.Ok);
        }
        catch (Exception excp)
        {
            return WriteResult(asJson,
                new TurnAllocateResult(false, server, iceChannel.IceGatheringState.ToString(), 0, null, null, null, null, null, excp.Message),
                ExitCodes.TransportError);
        }
        finally
        {
            iceChannel.Close("turn allocate complete");
        }
    }

    private static int WriteResult(bool asJson, TurnAllocateResult result, int exitCode)
    {
        if (asJson)
        {
            WriteJson(result);
        }
        else if (result.Success)
        {
            string mapped = result.MappedAddress != null ? $" (mapped {result.MappedAddress}:{result.MappedPort})" : string.Empty;
            Console.WriteLine($"Allocated a relay socket on {result.Server} in {result.DurationMs}ms: " +
                $"relay {result.RelayProtocol} {result.RelayAddress}:{result.RelayPort}{mapped}.");
        }
        else
        {
            Console.Error.WriteLine($"TURN allocate on {result.Server} failed after {result.DurationMs}ms (state {result.GatheringState}): {result.Error}");
        }

        return exitCode;
    }
}
