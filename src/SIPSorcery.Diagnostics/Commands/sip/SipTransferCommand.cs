//-----------------------------------------------------------------------------
// Filename: SipTransferCommand.cs
//
// Description: The "sipsorcery sip transfer" verb. Runs the three parties a
// call transfer needs - Transferor, Transferee and Target - in a single
// process, registers all three with an external SIP server and drives a
// transfer between them through that server.
//
// A transfer cannot be tested with two user agents. The roles are:
//
//   Transferor (A)  in the call, sends the REFER.
//   Transferee (B)  receives the REFER and places the new call.
//   Target     (C)  the transfer destination.
//
// With the Target external the leg that matters - did the Transferee really
// end up talking to the Target - cannot be observed. Hosting all three keeps
// every leg under measurement while all the signalling still goes through the
// server under test.
//
// This is the automated, assert-on-outcome equivalent of the three interactive
// console applications in examples/SIPScenarios/BlindTransferScenario, which
// talk to each other directly on fixed loopback ports with no server, no
// registration and no pass/fail.
//
// PHASE 1: registration of the three roles and the Transferor -> Transferee
// call, with per end point media measurement. The REFER itself is the next
// step, see the TODO below.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 25 Aug 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.CommandLine;
using System.Diagnostics;
using System.Net.Security;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.SIP;

namespace SIPSorcery.Diagnostics.Commands;

public sealed class SipTransferCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 30;
    private const int DEFAULT_CALL_DURATION_SECONDS = 5;
    private const int REGISTRATION_EXPIRY_SECONDS = 300;

    /// <summary>One step of the scenario, with the offset from the start of the run.</summary>
    private sealed record TimelineEvent(string Event, long AtMs, int? StatusCode = null, string? Detail = null);

    private sealed record RoleResult(string Name, string Aor, int Port, bool Registered, string? Error);

    /// <summary>What one party heard, and from where.</summary>
    private sealed record MediaLeg(
        string? RemoteEndPoint,
        int Packets,
        long Lost,
        int OutOfOrder,
        int Duplicates,
        string? Codec);

    /// <summary>
    /// The result shape written to stdout with --json. Stable field names; additive changes only.
    /// </summary>
    private sealed record TransferResult(
        bool Success,
        string Mode,
        string Transport,
        string Server,
        string Domain,
        IReadOnlyList<RoleResult> Roles,
        bool Answered,
        long? ConnectTimeMs,
        long? CallDurationMs,
        MediaLeg? TransferorMedia,
        MediaLeg? TransfereeMedia,
        IReadOnlyList<TimelineEvent> Timeline,
        string? Error);

    /// <summary>Collects the scenario steps with a millisecond offset from the start of the run.</summary>
    private sealed class Timeline
    {
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private readonly List<TimelineEvent> _events = new();
        private readonly object _sync = new();

        public void Add(string name, int? statusCode = null, string? detail = null)
        {
            lock (_sync)
            {
                _events.Add(new TimelineEvent(name, _elapsed.ElapsedMilliseconds, statusCode, detail));
            }
        }

        public IReadOnlyList<TimelineEvent> Events
        {
            get { lock (_sync) { return _events.ToArray(); } }
        }
    }

    public SipTransferCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var serverArg = new Argument<string>("server")
        {
            Description = "The SIP server all three roles register with and route through, in the form " +
                          "[sip:|sips:|udp:|tcp:|tls:]host[:port], e.g. sip.example.com, tcp:192.168.0.10:5060."
        };

        var transferorOption = new Option<string>("--transferor")
        {
            Description = "Credentials for the party that places the call and sends the REFER, as user:password.",
            Required = true
        };

        var transfereeOption = new Option<string>("--transferee")
        {
            Description = "Credentials for the party that is transferred, as user:password.",
            Required = true
        };

        var targetOption = new Option<string>("--target")
        {
            Description = "Credentials for the transfer destination, as user:password.",
            Required = true
        };

        var transportOption = new Option<string?>("--transport")
        {
            Description = "The SIP transport for all three roles: tcp, udp or tls. Defaults to the transport on " +
                          "the server argument, or tcp. With tls and no explicit port, port 5061 is used."
        };

        var insecureOption = new Option<bool>("--insecure")
        {
            Description = "TLS only. Accept any server certificate, for test servers with a self signed certificate."
        };

        var domainOption = new Option<string?>("--domain")
        {
            Description = "The domain part of each account's address of record. Defaults to the server host, and is " +
                          "only needed when accounts live in a named domain but the server is reached by address."
        };

        var basePortOption = new Option<int>("--base-port")
        {
            Description = "The local port for the transferor; the transferee and target take the next two. " +
                          "0 gives each role an ephemeral port.",
            DefaultValueFactory = _ => 0
        };

        var callDurationOption = new Option<int>("--call-duration")
        {
            Description = "Seconds to hold the first call up before transferring, so there is a stable media baseline.",
            DefaultValueFactory = _ => DEFAULT_CALL_DURATION_SECONDS
        };

        var hepOption = HepCapture.CreateOption();

        var command = new Command("transfer",
            "Run the transferor, transferee and target of a call transfer against an external SIP server and verify the outcome.");
        command.Arguments.Add(serverArg);
        command.Options.Add(transferorOption);
        command.Options.Add(transfereeOption);
        command.Options.Add(targetOption);
        command.Options.Add(transportOption);
        command.Options.Add(insecureOption);
        command.Options.Add(domainOption);
        command.Options.Add(basePortOption);
        command.Options.Add(callDurationOption);
        command.Options.Add(hepOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(serverArg)!,
            parseResult.GetValue(transferorOption)!,
            parseResult.GetValue(transfereeOption)!,
            parseResult.GetValue(targetOption)!,
            parseResult.GetValue(transportOption),
            parseResult.GetValue(insecureOption),
            parseResult.GetValue(domainOption),
            parseResult.GetValue(basePortOption),
            parseResult.GetValue(callDurationOption),
            parseResult.GetValue(hepOption),
            parseResult.GetValue(TimeoutOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        string server, string transferor, string transferee, string target, string? transport, bool insecure,
        string? domain, int basePort, int callDurationSeconds, string? hep, int timeoutSeconds, bool asJson,
        bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(SipTransferCommand));
        var timeline = new Timeline();

        if (!SipDestination.TryParse(server, out var serverUri, out var parseError))
        {
            return Fail(asJson, server, parseError!, timeline, ExitCodes.InvalidArgument);
        }

        if (!TryResolveProtocol(transport, serverUri, out var protocol, out string? transportError))
        {
            return Fail(asJson, serverUri.ToString(), transportError!, timeline, ExitCodes.InvalidArgument);
        }

        if (insecure && protocol != SIPProtocolsEnum.tls)
        {
            return Fail(asJson, serverUri.ToString(),
                "Option '--insecure' only applies to '--transport tls'.", timeline, ExitCodes.InvalidArgument);
        }

        // SIPDns picks the default port from the URI SCHEME, so a "sip:" URI carrying
        // transport=tls resolves to 5060 rather than 5061. Pin the TLS port here instead of
        // relying on that, and before the URI is used for either resolution or the registrar.
        if (protocol == SIPProtocolsEnum.tls && serverUri.HostPort == null)
        {
            serverUri.Host = $"{serverUri.HostAddress}:{SIPConstants.DEFAULT_SIP_TLS_PORT}";
            logger.LogDebug("No port on the server argument; using the default TLS port {Port}.",
                SIPConstants.DEFAULT_SIP_TLS_PORT);
        }

        serverUri.Protocol = protocol;

        var credentials = new List<(string Role, string Spec)>
        {
            ("transferor", transferor), ("transferee", transferee), ("target", target)
        };

        var parsed = new List<(string Role, string Username, string Password)>();

        foreach (var (role, spec) in credentials)
        {
            if (!TryParseCredentials(spec, out string username, out string password))
            {
                return Fail(asJson, serverUri.ToString(),
                    $"Option '--{role}' must be in the form user:password.", timeline, ExitCodes.InvalidArgument);
            }

            parsed.Add((role, username, password));
        }

        domain ??= serverUri.HostAddress;

        using var hepCapture = HepCapture.Create(hep, logger, out string? hepError);

        if (hepError != null)
        {
            return Fail(asJson, serverUri.ToString(), hepError, timeline, ExitCodes.InvalidArgument);
        }

        var outboundProxy = await SIPDns.ResolveAsync(serverUri, false, ct).ConfigureAwait(false);

        if (outboundProxy == null)
        {
            return Fail(asJson, serverUri.ToString(),
                $"Could not resolve the SIP server \"{serverUri}\".", timeline, ExitCodes.TransportError);
        }

        logger.LogDebug("SIP server {Server} resolved to {EndPoint}.", serverUri, outboundProxy);

        RemoteCertificateValidationCallback? certificateValidation = insecure
            ? (_, _, _, _) => true
            : null;

        var roles = new List<TransferRole>();

        try
        {
            for (int i = 0; i < parsed.Count; i++)
            {
                var (role, username, password) = parsed[i];

                roles.Add(new TransferRole(
                    new TransferRoleOptions(
                        role,
                        username,
                        password,
                        domain,
                        serverUri,
                        outboundProxy,
                        protocol,
                        basePort == 0 ? 0 : basePort + i,
                        REGISTRATION_EXPIRY_SECONDS,
                        new AudioSourceOptions { AudioSource = AudioSourcesEnum.Music },
                        certificateValidation),
                    hepCapture,
                    verbose,
                    logger));
            }

            var (transferorRole, transfereeRole, targetRole) = (roles[0], roles[1], roles[2]);

            Console.Error.WriteLine(
                $"Registering 3 roles with {serverUri} over {protocol} " +
                $"(ports {string.Join(", ", roles.Select(x => x.Port))}) ...");

            var registrationTimeout = TimeSpan.FromSeconds(timeoutSeconds);
            await Task.WhenAll(roles.Select(x => x.RegisterAsync(registrationTimeout, ct))).ConfigureAwait(false);

            var roleResults = roles
                .Select(x => new RoleResult(x.Name, x.Aor.ToString(), x.Port, x.Registered, x.RegistrationError))
                .ToArray();

            if (roles.Any(x => !x.Registered))
            {
                // A transfer scenario against an unregistered party proves nothing, so stop here
                // rather than producing a failure that looks like a transfer problem.
                string failed = string.Join(", ", roles.Where(x => !x.Registered).Select(x => x.Name));

                return WriteResult(asJson,
                    new TransferResult(false, "blind", protocol.ToString(), serverUri.ToString(), domain,
                        roleResults, false, null, null, null, null, timeline.Events,
                        $"Registration failed for: {failed}."),
                    ExitCodes.Failed);
            }

            timeline.Add("registered");

            // The transferee answers the transferor's call. The target is armed now as well: after
            // the REFER the transferee calls it without any further prompting from here.
            transfereeRole.AutoAnswer();
            targetRole.AutoAnswer();

            var transferorMedia = transferorRole.CreateMedia();
            var callTimer = Stopwatch.StartNew();
            SIPResponse? failureResponse = null;
            var remoteHungup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            transferorRole.UserAgent.ClientCallFailed += (_, error, resp) =>
            {
                failureResponse = resp;
                Console.Error.WriteLine($"Call failed: {error}.");
            };
            transferorRole.UserAgent.OnCallHungup += _ => remoteHungup.TrySetResult(true);

            Console.Error.WriteLine($"Transferor calling transferee at {transfereeRole.Aor} ...");

            bool answered = await transferorRole.UserAgent.Call(
                transfereeRole.Aor.ToString(),
                transferorRole.Aor.User,
                parsed[0].Password,
                transferorMedia.Session,
                timeoutSeconds).ConfigureAwait(false);

            long connectTimeMs = callTimer.ElapsedMilliseconds;

            if (!answered)
            {
                // A ring timeout sends a CANCEL, and the final response to the INVITE (a 487, or
                // whatever the server actually does) arrives after Call has already returned.
                // Tearing the transports down straight away loses it, which is exactly the part of
                // the exchange worth seeing when a call rings and never connects.
                await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false);

                int? statusCode = failureResponse != null ? (int)failureResponse.Status : null;
                timeline.Add("callFailed", statusCode);

                return WriteResult(asJson,
                    new TransferResult(false, "blind", protocol.ToString(), serverUri.ToString(), domain,
                        roleResults, false, connectTimeMs, null, null, null, timeline.Events,
                        statusCode != null
                            ? $"The transferee did not answer: {statusCode} {failureResponse!.ReasonPhrase}."
                            : $"The transferee did not answer within {timeoutSeconds}s."),
                    statusCode != null ? ExitCodes.Failed : ExitCodes.Timeout);
            }

            timeline.Add("callAnswered", (int)SIPResponseStatusCodesEnum.Ok);
            Console.Error.WriteLine($"Answered in {connectTimeMs}ms. Holding the call for {callDurationSeconds}s.");

            var callWindow = Stopwatch.StartNew();
            await Task.WhenAny(
                Task.Delay(TimeSpan.FromSeconds(callDurationSeconds), ct), remoteHungup.Task).ConfigureAwait(false);
            callWindow.Stop();

            // TODO (phase 2): fire the blind REFER here with transferorRole.UserAgent.BlindTransfer,
            // record the NOTIFY sipfrag progression, then assert on the media switching to a new
            // remote end point on the transferee and the original leg being torn down.

            var transferorLeg = Describe(transferorRole.Media);
            var transfereeLeg = Describe(transfereeRole.Media);

            if (transferorRole.UserAgent.IsCallActive)
            {
                transferorRole.UserAgent.Hangup();
                await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);
            }

            timeline.Add("callEnded");

            await Task.WhenAll(roles.Select(x => x.UnregisterAsync())).ConfigureAwait(false);

            bool bothHeardMedia = transferorLeg is { Packets: > 0 } && transfereeLeg is { Packets: > 0 };

            return WriteResult(asJson,
                new TransferResult(bothHeardMedia, "blind", protocol.ToString(), serverUri.ToString(), domain,
                    roleResults, true, connectTimeMs, callWindow.ElapsedMilliseconds,
                    transferorLeg, transfereeLeg, timeline.Events,
                    bothHeardMedia ? null : "The call was answered but audio did not flow in both directions."),
                bothHeardMedia ? ExitCodes.Ok : ExitCodes.Failed);
        }
        catch (OperationCanceledException)
        {
            return Fail(asJson, serverUri.ToString(), "Cancelled.", timeline, ExitCodes.Timeout);
        }
        catch (Exception excp)
        {
            logger.LogDebug(excp, "Unhandled exception running the transfer scenario.");
            return Fail(asJson, serverUri.ToString(), excp.Message, timeline, ExitCodes.Failed);
        }
        finally
        {
            foreach (var role in roles)
            {
                role.Dispose();
            }
        }
    }

    /// <summary>
    /// Resolves the transport to use: an explicit --transport wins, otherwise a transport carried on
    /// the server argument, otherwise TCP. TCP rather than UDP because a NAT binding has to survive
    /// from the REGISTER until the transfer completes, which can be minutes, and because a stream
    /// connection lets in-dialog requests return over the flow the role opened.
    /// </summary>
    private static bool TryResolveProtocol(
        string? transport, SIPURI serverUri, out SIPProtocolsEnum protocol, out string? error)
    {
        error = null;

        if (!string.IsNullOrWhiteSpace(transport))
        {
            if (!Enum.TryParse(transport, true, out protocol) ||
                protocol is not (SIPProtocolsEnum.udp or SIPProtocolsEnum.tcp or SIPProtocolsEnum.tls))
            {
                error = $"Unsupported transport \"{transport}\". Use tcp, udp or tls.";
                return false;
            }

            return true;
        }

        bool serverStatedTransport =
            serverUri.Scheme == SIPSchemesEnum.sips || serverUri.Parameters.Has(SIPHeaderAncillary.SIP_HEADERANC_TRANSPORT);

        protocol = serverStatedTransport ? serverUri.Protocol : SIPProtocolsEnum.tcp;
        return true;
    }

    /// <summary>Splits "user:password" on the first colon, so a password may itself contain one.</summary>
    private static bool TryParseCredentials(string spec, out string username, out string password)
    {
        username = string.Empty;
        password = string.Empty;

        int separator = spec.IndexOf(':');

        if (separator <= 0 || separator == spec.Length - 1)
        {
            return false;
        }

        username = spec[..separator];
        password = spec[(separator + 1)..];
        return true;
    }

    private static MediaLeg? Describe(RoleMedia? media)
    {
        if (media == null)
        {
            return null;
        }

        var dominant = media.DominantAudioRemote;

        if (dominant == null)
        {
            return new MediaLeg(null, 0, 0, 0, 0, null);
        }

        var stats = dominant.Value.Value;

        return new MediaLeg(
            dominant.Value.Key,
            stats.Packets,
            stats.Lost,
            stats.OutOfOrder,
            stats.Duplicates,
            media.NegotiatedFormat.IsEmpty() ? null : $"{media.NegotiatedFormat.Codec}/{media.NegotiatedFormat.ClockRate}");
    }

    private static int Fail(bool asJson, string server, string error, Timeline timeline, int exitCode) =>
        WriteResult(asJson,
            new TransferResult(false, "blind", string.Empty, server, string.Empty, Array.Empty<RoleResult>(),
                false, null, null, null, null, timeline.Events, error),
            exitCode);

    private static int WriteResult(bool asJson, TransferResult result, int exitCode)
    {
        if (asJson)
        {
            WriteJson(result);
        }
        else
        {
            Console.WriteLine(result.Success ? "Transfer scenario: OK" : "Transfer scenario: FAILED");

            foreach (var role in result.Roles)
            {
                Console.WriteLine($"  {role.Name,-11} {role.Aor} port {role.Port} " +
                                  $"{(role.Registered ? "registered" : $"NOT registered ({role.Error})")}");
            }

            if (result.Answered)
            {
                Console.WriteLine($"  call answered in {result.ConnectTimeMs}ms, held {result.CallDurationMs}ms");
                WriteLeg("transferor heard", result.TransferorMedia);
                WriteLeg("transferee heard", result.TransfereeMedia);
            }

            if (result.Error != null)
            {
                Console.WriteLine($"  error: {result.Error}");
            }
        }

        return exitCode;
    }

    private static void WriteLeg(string label, MediaLeg? leg)
    {
        if (leg == null)
        {
            return;
        }

        Console.WriteLine($"  {label,-17} {leg.Packets} packets from {leg.RemoteEndPoint ?? "nobody"}" +
                          $"{(leg.Codec != null ? $" ({leg.Codec})" : string.Empty)}" +
                          $"{(leg.Lost > 0 ? $", {leg.Lost} lost" : string.Empty)}");
    }
}
