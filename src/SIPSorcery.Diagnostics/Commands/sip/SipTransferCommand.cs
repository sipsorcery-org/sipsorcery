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
using SIPSorcery.SIP.App;

namespace SIPSorcery.Diagnostics.Commands;

public sealed class SipTransferCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 30;
    private const string BlindMode = "blind";
    private const string AttendedMode = "attended";

    private const int DEFAULT_CALL_DURATION_SECONDS = 5;

    /// <summary>
    /// How long the transfer is given to complete after the REFER is accepted. The transferee
    /// has to place a whole second call within it, so this is a call setup budget rather than a
    /// settling delay.
    /// </summary>
    private const int DEFAULT_SETTLE_SECONDS = 10;

    /// <summary>
    /// How long a role waits after its BYE before its transport is torn down. Long enough for the
    /// 200 to come back over a WAN round trip and for the far end to see the call end.
    /// </summary>
    private static readonly TimeSpan HANGUP_SETTLE = TimeSpan.FromMilliseconds(600);
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
        MediaLeg? TargetMedia,
        IReadOnlyList<TimelineEvent> Timeline,
        string? Error,
        TransferOutcome? Transfer = null);

    /// <summary>
    /// What the REFER actually achieved, as opposed to what it was answered with.
    /// </summary>
    /// <remarks>
    /// Split out because a 202 says only that the transferee accepted the request. Everything
    /// worth knowing happens after it: whether the transferee really called the target, whether
    /// the target answered, and whether the audio moved. A server can return 202 and do nothing.
    /// </remarks>
    private sealed record TransferOutcome(
        bool Accepted,
        int? ReferStatus,
        bool TargetCalled,
        bool OriginalLegEnded,
        string? MediaBefore,
        string? MediaAfter,
        string? MediaSwitched,
        IReadOnlyList<string> Notifies);

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

        var modeOption = new Option<string>("--mode")
        {
            Description =
                "blind: the transferor refers the transferee straight to the target. " +
                "attended: the transferor calls the target first and refers the transferee to " +
                "that call, which the target then replaces.",
            DefaultValueFactory = _ => BlindMode
        };

        modeOption.AcceptOnlyFromAmong(BlindMode, AttendedMode);

        var settleOption = new Option<int>("--settle")
        {
            Description =
                "Seconds to wait after the REFER for the transferee to reach the target and its audio " +
                "to move, before the outcome is judged.",
            DefaultValueFactory = _ => DEFAULT_SETTLE_SECONDS
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
        command.Options.Add(modeOption);
        command.Options.Add(settleOption);
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
            parseResult.GetValue(modeOption)!,
            parseResult.GetValue(settleOption),
            parseResult.GetValue(hepOption),
            parseResult.GetValue(TimeoutOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        string server, string transferor, string transferee, string target, string? transport, bool insecure,
        string? domain, int basePort, int callDurationSeconds, string mode, int settleSeconds,
        string? hep, int timeoutSeconds, bool asJson, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(SipTransferCommand));
        var timeline = new Timeline();

        if (!SipDestination.TryParse(server, out var serverUri, out var parseError))
        {
            return Fail(asJson, server, mode, parseError!, timeline, ExitCodes.InvalidArgument);
        }

        if (!TryResolveProtocol(transport, serverUri, out var protocol, out string? transportError))
        {
            return Fail(asJson, serverUri.ToString(), mode, transportError!, timeline, ExitCodes.InvalidArgument);
        }

        if (insecure && protocol != SIPProtocolsEnum.tls)
        {
            return Fail(asJson, serverUri.ToString(), mode,
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
                return Fail(asJson, serverUri.ToString(), mode,
                    $"Option '--{role}' must be in the form user:password.", timeline, ExitCodes.InvalidArgument);
            }

            parsed.Add((role, username, password));
        }

        domain ??= serverUri.HostAddress;

        using var hepCapture = HepCapture.Create(hep, logger, out string? hepError);

        if (hepError != null)
        {
            return Fail(asJson, serverUri.ToString(), mode, hepError, timeline, ExitCodes.InvalidArgument);
        }

        var outboundProxy = await SIPDns.ResolveAsync(serverUri, false, ct).ConfigureAwait(false);

        if (outboundProxy == null)
        {
            return Fail(asJson, serverUri.ToString(), mode,
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
                    new TransferResult(false, mode, protocol.ToString(), serverUri.ToString(), domain,
                        roleResults, false, null, null, null, null, null, timeline.Events,
                        $"Registration failed for: {failed}."),
                    ExitCodes.Failed);
            }

            timeline.Add("registered");

            // The transferee answers the transferor's call. The target is armed now as well: after
            // the REFER the transferee calls it without any further prompting from here.
            var notifies = new List<string>();
            var targetCalled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            transfereeRole.AutoAnswer();

            // The target records that it was rung as well as answering. That is the difference
            // between "the transferee accepted the REFER" and "the transfer actually happened":
            // a server can answer 202 and never place the second call.
            targetRole.AutoAnswer(_ =>
            {
                timeline.Add("targetRinging");
                targetCalled.TrySetResult(true);
            });

            // The call the transferee places to the target is built by the library from
            // SIPConstants.SIP_DEFAULT_USERNAME with no credential, so a server that challenges
            // the INVITE would fail the transfer for a reason that has nothing to do with
            // transfers. This is the hook the library provides for exactly that.
            transfereeRole.UserAgent.OnTransferCallDescriptorCreated += (descriptor, _) =>
            {
                descriptor.Username = transfereeRole.Aor.User;
                descriptor.AuthUsername = transfereeRole.Aor.User;
                descriptor.Password = parsed[1].Password;
                descriptor.From = transfereeRole.Aor.ToString();

                Console.Error.WriteLine($"Transferee is calling the target at {descriptor.Uri}.");
                timeline.Add("transfereeCallingTarget", detail: descriptor.Uri);
            };

            // The transferee's side of the transfer, which the library does raise events for.
            transfereeRole.UserAgent.OnTransferRequested += (referTo, referredBy) =>
            {
                Console.Error.WriteLine(
                    $"Transferee was asked to transfer the call to {referTo.URI.ToParameterlessString()}" +
                    (string.IsNullOrWhiteSpace(referredBy) ? "" : $" by {referredBy}") + " - accepting.");

                timeline.Add("transfereeAcceptedRefer", detail: referTo.URI.ToParameterlessString());

                // Silenced for the transfer: the transferee holds the transferor while it calls
                // the target, and a tone playing into a session that will not send warns on every
                // packet.
                _ = transfereeRole.Media?.PauseAsync();

                // Returning true is what accepts it; with no handler at all the library accepts
                // anyway, so this changes nothing beyond making the decision visible.
                return true;
            };

            transfereeRole.UserAgent.OnTransferToTargetSuccessful += referTo =>
            {
                Console.Error.WriteLine(
                    $"Transferee reached {referTo.URI.ToParameterlessString()}; its call to the transferor is done.");
                timeline.Add("transfereeReachedTarget");

                _ = transfereeRole.Media?.ResumeAsync();
            };

            transfereeRole.UserAgent.OnTransferToTargetFailed += referTo =>
            {
                Console.Error.WriteLine(
                    $"Transferee could not reach {referTo.URI.ToParameterlessString()}; the transfer failed.");
                timeline.Add("transfereeFailedToReachTarget");

                _ = transfereeRole.Media?.ResumeAsync();
            };

            // The target's side of it. Between these two the call being replaced is on hold, which
            // is where the RecvOnly warnings from the audio source come from.
            targetRole.UserAgent.OnAttendedTransferRequested += request =>
            {
                var replaced = SIPReplacesParameter.Parse(request.Header.Replaces)?.CallID;

                Console.Error.WriteLine(
                    $"Target is being asked to replace call {replaced} - putting it on hold and " +
                    "answering the call taking it over.");

                timeline.Add("targetReplacingCall", detail: replaced);

                _ = targetRole.Media?.PauseAsync();
            };

            targetRole.UserAgent.OnAttendedTransferAccepted += replaced =>
            {
                Console.Error.WriteLine(
                    $"Target accepted the transfer; its call {replaced.CallId} to the transferor is done.");

                timeline.Add("targetAcceptedTransfer", detail: replaced.CallId);

                _ = targetRole.Media?.ResumeAsync();
            };

            // Recorded rather than required. The transferee's own REFER handling in the library
            // has a TODO where the implicit subscription would be created, so against a
            // SIPUserAgent transferee no NOTIFY arrives at all; a handset would send them.
            transferorRole.UserAgent.OnTransferNotify += sipfrag =>
            {
                var line = sipfrag?.Split('\n').FirstOrDefault()?.Trim();

                if (!string.IsNullOrWhiteSpace(line))
                {
                    lock (notifies)
                    {
                        notifies.Add(line);
                    }

                    timeline.Add("notify", detail: line);
                }
            };

            var transferorMedia = transferorRole.CreateMedia();
            var callTimer = Stopwatch.StartNew();
            SIPResponse? failureResponse = null;
            var remoteHungup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            transferorRole.UserAgent.ClientCallFailed += (_, error, resp) =>
            {
                failureResponse = resp;
                Console.Error.WriteLine($"Call failed: {error}.");
            };
            transferorRole.UserAgent.OnCallHungup += _ =>
            {
                // After a transfer this is the transferee letting the transferor go, which is the
                // half of the outcome the transferor can actually observe.
                Console.Error.WriteLine("Transferor's call to the transferee has been hung up.");
                timeline.Add("originalLegHungup");
                remoteHungup.TrySetResult(true);
            };

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
                    new TransferResult(false, mode, protocol.ToString(), serverUri.ToString(), domain,
                        roleResults, false, connectTimeMs, null, null, null, null, timeline.Events,
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

            // Where the transferee is hearing audio from before the transfer. The comparison after
            // it is the whole point: the library hands the same media session to the new call, so
            // packet counts carry straight through and only the remote end point actually changes.
            var mediaBefore = transfereeRole.Media?.DominantAudioRemote?.Key;
            bool baselineMedia = transferorRole.Media is { TotalAudioPackets: > 0 }
                && transfereeRole.Media is { TotalAudioPackets: > 0 };

            bool attended = mode == AttendedMode;

            // An attended transfer refers the transferee to a call the transferor is already in,
            // so that call has to exist first. It runs on a second user agent because one holds a
            // single dialogue, and the transferor needs to be talking to both parties at once.
            var consultationReplaced =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            SIPUserAgent? consultation = null;

            if (attended)
            {
                var (agent, media) = transferorRole.CreateConsultation();
                consultation = agent;

                // The target ending this call is how an attended transfer reports success: it
                // hangs up the dialogue it was asked to replace once it has accepted the
                // replacement. Nothing else tells the transferor the transfer landed.
                agent.OnCallHungup += _ =>
                {
                    Console.Error.WriteLine(
                        "Transferor's call to the target has been hung up - the target took the replacement.");
                    timeline.Add("consultationReplaced");
                    consultationReplaced.TrySetResult(true);
                };

                Console.Error.WriteLine($"Transferor calling the target at {targetRole.Aor} to consult ...");

                bool consulted = await agent.Call(
                    targetRole.Aor.ToString(),
                    transferorRole.Aor.User,
                    parsed[0].Password,
                    media.Session,
                    timeoutSeconds).ConfigureAwait(false);

                if (!consulted)
                {
                    timeline.Add("consultationFailed");

                    return WriteResult(asJson,
                        new TransferResult(false, mode, protocol.ToString(), serverUri.ToString(), domain,
                            roleResults, true, connectTimeMs, callWindow.ElapsedMilliseconds,
                            Describe(transferorRole.Media), Describe(transfereeRole.Media),
                            Describe(targetRole.Media), timeline.Events,
                            "The consultation call to the target was not answered."),
                        ExitCodes.Failed);
                }

                timeline.Add("consultationAnswered");
                Console.Error.WriteLine("Consultation call answered.");
            }

            Console.Error.WriteLine($"Transferring the transferee to {targetRole.Aor} ...");
            timeline.Add("referSent", detail: targetRole.Aor.ToString());

            bool accepted;

            try
            {
                // The only difference on the wire is whether the Refer-To carries a Replaces. In
                // the attended case it names the consultation dialogue, which is the one the
                // target is holding and the one it is being asked to replace.
                accepted = attended
                    ? await transferorRole.UserAgent.AttendedTransfer(
                        consultation!.Dialogue,
                        TimeSpan.FromSeconds(timeoutSeconds),
                        ct,
                        username: transferorRole.Aor.User,
                        password: parsed[0].Password).ConfigureAwait(false)
                    : await transferorRole.UserAgent.BlindTransfer(
                        targetRole.Aor,
                        TimeSpan.FromSeconds(timeoutSeconds),
                        ct,
                        username: transferorRole.Aor.User,
                        password: parsed[0].Password).ConfigureAwait(false);
            }
            catch (Exception excp)
            {
                accepted = false;
                Console.Error.WriteLine($"The REFER threw: {excp.Message}");
            }

            timeline.Add(accepted ? "referAccepted" : "referRejected",
                accepted ? (int)SIPResponseStatusCodesEnum.Accepted : null);

            Console.Error.WriteLine(accepted
                ? "REFER accepted. Waiting for the transferee to reach the target ..."
                : "REFER was not accepted.");

            // The transferee places the second call on its own once it has accepted, so this waits
            // on the target being rung rather than on a fixed delay.
            if (accepted)
            {
                // What is waited on differs by mode. A blind transfer has the target rung as a new
                // call, so its incoming call handler fires. An attended one replaces a call the
                // target already has, which the library accepts internally without raising that -
                // so the signal is the consultation being hung up by the target instead.
                await Task.WhenAny(
                    attended ? consultationReplaced.Task : targetCalled.Task,
                    Task.Delay(TimeSpan.FromSeconds(settleSeconds), ct)).ConfigureAwait(false);

                // A moment beyond the answer for media to start arriving from the new end point.
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }

            Console.Error.WriteLine(attended
                ? "Both of the transferor's calls should now be gone; the transferee and target are talking."
                : "The transferor's call should now be gone; the transferee and target are talking.");

            bool targetAnswered = attended
                ? consultationReplaced.Task.IsCompletedSuccessfully
                : targetCalled.Task.IsCompletedSuccessfully;

            if (targetAnswered)
            {
                timeline.Add("targetAnswered");
            }

            var mediaAfter = transfereeRole.Media?.DominantAudioRemote?.Key;

            // Three-valued on purpose. "The audio did not move" and "there was never any audio to
            // move" are different findings, and reporting the second as the first would blame the
            // transfer for a media path that never worked - which is what happens when the roles
            // all sit behind one NAT and the router will not hairpin their RTP.
            string mediaSwitched =
                !baselineMedia ? "not-assessed"
                : mediaAfter != null && mediaAfter != mediaBefore ? "yes"
                : "no";

            if (mediaSwitched == "yes")
            {
                timeline.Add("mediaSwitched", detail: mediaAfter);
            }

            // The transferor's leg is finished with once the transfer is away: a blind transfer
            // ends with the transferee hanging it up.
            bool originalLegEnded = remoteHungup.Task.IsCompletedSuccessfully
                || !transferorRole.UserAgent.IsCallActive;

            if (originalLegEnded)
            {
                timeline.Add("originalLegEnded");
            }

            var transferorLeg = Describe(transferorRole.Media);
            var transfereeLeg = Describe(transfereeRole.Media);

            // The surviving call after a transfer is the transferee and the target, so the
            // target's side of it is half the evidence for whether the media actually moved.
            var targetLeg = Describe(targetRole.Media);

            // The consultation is a real leg of an attended transfer and the only place the
            // target's audio can be seen before the transfer, which separates "the target stopped
            // sending when it was taken over" from "the target never sent at all".
            if (attended)
            {
                var consultLeg = Describe(transferorRole.ConsultationMedia);
                Console.Error.WriteLine(
                    $"  consultation: transferor heard {(consultLeg is null or { Packets: 0 } ? "nothing" : $"{consultLeg.Packets} packets from {consultLeg.RemoteEndPoint}")} from the target.");
            }

            await HangUpAllAsync(roles).ConfigureAwait(false);

            timeline.Add("callEnded");

            await Task.WhenAll(roles.Select(x => x.UnregisterAsync())).ConfigureAwait(false);

            string[] notified;

            lock (notifies)
            {
                notified = notifies.ToArray();
            }

            var outcome = new TransferOutcome(
                accepted,
                accepted ? (int)SIPResponseStatusCodesEnum.Accepted : null,
                targetAnswered,
                originalLegEnded,
                mediaBefore,
                mediaAfter,
                mediaSwitched,
                notified);

            // The transfer is judged on what it achieved, and media only counts against it when
            // there was media to begin with. Note what is deliberately absent: the NOTIFY
            // progression is recorded but not required, because a SIPUserAgent transferee never
            // sends one - requiring it would fail every run for a gap in the transferee, not the
            // server under test.
            bool transferred = accepted && targetAnswered && mediaSwitched != "no";

            string? error =
                !accepted ? "The transferee did not accept the REFER."
                : !targetAnswered ? (attended
                    ? "The REFER was accepted but the target never replaced the consultation call."
                    : "The REFER was accepted but the target was never called.")
                : mediaSwitched == "no" ? "The target answered but the transferee's audio did not move to it."
                : null;

            return WriteResult(asJson,
                new TransferResult(transferred, mode, protocol.ToString(), serverUri.ToString(), domain,
                    roleResults, true, connectTimeMs, callWindow.ElapsedMilliseconds,
                    transferorLeg, transfereeLeg, targetLeg, timeline.Events, error, outcome),
                transferred ? ExitCodes.Ok : ExitCodes.Failed);
        }
        catch (OperationCanceledException)
        {
            return Fail(asJson, serverUri.ToString(), mode, "Cancelled.", timeline, ExitCodes.Timeout);
        }
        catch (Exception excp)
        {
            logger.LogDebug(excp, "Unhandled exception running the transfer scenario.");
            return Fail(asJson, serverUri.ToString(), mode, excp.Message, timeline, ExitCodes.Failed);
        }
        finally
        {
            // Ends any call still up before the transports go, so an abandoned run leaves nothing
            // ringing on the server. Idempotent with the tidy path above: a role whose call has
            // already ended sends nothing.
            await HangUpAllAsync(roles).ConfigureAwait(false);

            foreach (var role in roles)
            {
                role.Dispose();
            }
        }
    }

    /// <summary>
    /// Ends every call still up, one role at a time.
    /// </summary>
    /// <remarks>
    /// Sequential, and re-checking before each, is the point. After a successful transfer the
    /// surviving call has one of these roles at each end; hanging both up together leaves two BYEs
    /// crossing with neither end alive to answer the other, and both transactions retransmitting
    /// into a socket that is about to close. Letting the first BYE be relayed means the second role
    /// finds its call already over and stays quiet.
    /// </remarks>
    private static async Task HangUpAllAsync(IEnumerable<TransferRole> roles)
    {
        foreach (var role in roles)
        {
            await role.HangupAsync(HANGUP_SETTLE).ConfigureAwait(false);
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

    private static int Fail(
        bool asJson, string server, string mode, string error, Timeline timeline, int exitCode) =>
        WriteResult(asJson,
            new TransferResult(false, mode, string.Empty, server, string.Empty, Array.Empty<RoleResult>(),
                false, null, null, null, null, null, timeline.Events, error),
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
        WriteLeg("target     heard", result.TargetMedia);
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
