//-----------------------------------------------------------------------------
// Filename: TransferRole.cs
//
// Description: One of the three endpoints in a "sipsorcery sip transfer" run:
// the Transferor, the Transferee or the Target.
//
// Each role owns its own SIPTransport on its own port rather than sharing one
// across the three. That is not incidental:
//
//  - SIPTCPChannel.ConnectClientAsync binds the outbound socket to the
//    channel's listening end point, so a role's Contact port and its TCP
//    source port are the same. Three roles therefore need three ports.
//  - Sharing a transport would give all three an identical Contact host:port
//    differing only in the user part. After a NAT'ing proxy mangles the
//    Contact to the source address they become indistinguishable, which masks
//    any server bug that keys registrations or connections by end point.
//
// Three transports make the roles look like three separate devices, which is
// the case worth testing.
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

using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Diagnostics.Commands;

/// <summary>
/// The settings needed to stand one role up. Grouped into a record because a role needs most of
/// the command's options and a positional constructor for them would be unreadable.
/// </summary>
public sealed record TransferRoleOptions(
    string Name,
    string Username,
    string Password,
    string Domain,
    SIPURI Server,
    SIPEndPoint OutboundProxy,
    SIPProtocolsEnum Protocol,
    int Port,
    int RegistrationExpirySeconds,
    AudioSourceOptions AudioSource,
    RemoteCertificateValidationCallback? CertificateValidation);

/// <summary>
/// A media session plus the RTP that has arrived on it, tallied separately for each end point it
/// came from.
/// </summary>
/// <remarks>
/// The per end point split is what makes a transfer verifiable. SIPUserAgent hands the SAME media
/// session to the new call it places after a REFER (see ProcessTransferRequest), so a single set of
/// counters runs straight through the transfer and never resets. "The transferee received audio
/// after the REFER" is therefore true even when the transferee is still bridged to the transferor.
/// Keying on the source end point instead answers the question that actually matters: is a new
/// party being heard, and has the old one gone quiet.
/// </remarks>
public sealed class RoleMedia : IDisposable
{
    private readonly ConcurrentDictionary<string, RtpStreamStats> _audioByRemote = new();
    private readonly ILogger _logger;
    private readonly string _roleName;

    public VoIPMediaSession Session { get; }

    public AudioFormat NegotiatedFormat { get; private set; } = AudioFormat.Empty;

    public RoleMedia(string roleName, AudioSourceOptions audioSource, ILogger logger)
    {
        _roleName = roleName;
        _logger = logger;

        Session = new VoIPMediaSession
        {
            // Accept RTP from any source. A B2BUA or media relay can deliver from a port other
            // than the one its SDP advertised, and silently dropping that reads as "no media" when
            // the real problem is elsewhere. Nothing is lost by being permissive here because the
            // identity of the sender is recovered from the per end point tallies below rather than
            // from a single expected address.
            AcceptRtpFromAny = true
        };

        Session.AudioExtrasSource.SetSource(audioSource);
        Session.OnAudioFormatsNegotiated += formats => NegotiatedFormat = formats.First();
        Session.OnRtpPacketReceived += OnRtpPacketReceived;
    }

    /// <summary>Every remote end point that has delivered audio, with its packet tally.</summary>
    public IReadOnlyDictionary<string, RtpStreamStats> AudioByRemote => _audioByRemote;

    /// <summary>
    /// Stops and restarts the tone this role is sending.
    /// </summary>
    /// <remarks>
    /// Used across a transfer. Both parties to one put their existing call on hold for a moment
    /// while the new call is set up, which leaves the RTP session refusing to send, and a source
    /// that keeps generating logs a warning for every packet it offers in that window.
    ///
    /// Pausing removes the noise without hiding anything: a send refused outside the window still
    /// warns, and that is the case actually worth seeing.
    /// </remarks>
    public Task PauseAsync()
    {
        if (_paused)
        {
            return Task.CompletedTask;
        }

        _paused = true;
        return Session.AudioExtrasSource.PauseAudio();
    }

    public Task ResumeAsync()
    {
        if (!_paused)
        {
            return Task.CompletedTask;
        }

        _paused = false;
        return Session.AudioExtrasSource.ResumeAudio();
    }

    private bool _paused;

    public int TotalAudioPackets => _audioByRemote.Values.Sum(x => x.Packets);

    /// <summary>
    /// The end point that has delivered the most audio, or null if none has. With one call in
    /// progress this is the party being heard.
    /// </summary>
    public KeyValuePair<string, RtpStreamStats>? DominantAudioRemote =>
        _audioByRemote.IsEmpty ? null : _audioByRemote.MaxBy(x => x.Value.Packets);

    /// <summary>
    /// The packet tally for every remote end point at this instant, for comparison against a later
    /// one.
    /// </summary>
    /// <remarks>
    /// Cumulative totals cannot answer "who is being heard NOW", which is the only question a
    /// transfer turns on. The library reuses this media session for the call it places after a
    /// REFER, so the pre-transfer party keeps every packet it ever sent, and a short post-transfer
    /// window never overtakes a long call: the party that has been dropped stays the largest tally
    /// indefinitely. Differencing two snapshots gives per end point rates, which do answer it.
    /// </remarks>
    public IReadOnlyDictionary<string, int> SnapshotAudioPacketCounts() =>
        _audioByRemote.ToDictionary(x => x.Key, x => x.Value.Packets);

    private void OnRtpPacketReceived(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
    {
        if (mediaType != SDPMediaTypesEnum.audio)
        {
            return;
        }

        var stats = _audioByRemote.GetOrAdd(remoteEndPoint.ToString(), _ => new RtpStreamStats());
        var outcome = stats.Record((ushort)rtpPacket.Header.SequenceNumber);

        if (outcome.Kind != RtpStreamStats.RecordKind.InOrder)
        {
            _logger.LogDebug("{Role} audio packet seq {Seq} from {Remote} arrived {Kind} (highest seen {Highest}, ssrc {Ssrc}).",
                _roleName, rtpPacket.Header.SequenceNumber, remoteEndPoint, outcome.Kind,
                outcome.PreviousHighestSeq, rtpPacket.Header.SyncSource);
        }
    }

    public void Dispose() => Session.Close("disposed");
}

public sealed class TransferRole : IDisposable
{
    private readonly TransferRoleOptions _options;
    private readonly ILogger _logger;
    private readonly SIPRegistrationUserAgent _registration;
    private bool _unregistered;

    /// <summary>How long to let the zero expiry REGISTER get away before the transport closes.</summary>
    private static readonly TimeSpan UNREGISTER_SETTLE = TimeSpan.FromMilliseconds(600);
    private readonly TaskCompletionSource<(bool Success, string? Error)> _registrationOutcome =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The role's name, "transferor", "transferee" or "target".</summary>
    public string Name => _options.Name;

    /// <summary>The address of record the role registers, e.g. sip:alice@example.com.</summary>
    public SIPURI Aor { get; }

    /// <summary>The local port the role's single SIP channel listens on and sends from.</summary>
    public int Port { get; }

    public SIPTransport Transport { get; }

    public SIPUserAgent UserAgent { get; }

    public bool Registered { get; private set; }

    public string? RegistrationError { get; private set; }

    /// <summary>The media session for the role's current call, null before one is set up.</summary>
    public RoleMedia? Media { get; private set; }

    public TransferRole(TransferRoleOptions options, HepCapture? hep, bool verbose, ILogger logger)
    {
        _options = options;
        _logger = logger;

        Aor = new SIPURI(options.Username, options.Domain, null, options.Server.Scheme, options.Protocol);

        Transport = new SIPTransport();
        var channel = CreateChannel(options.Protocol, options.Port, options.CertificateValidation);
        Transport.AddSIPChannel(channel);
        Port = channel.ListeningSIPEndPoint.Port;

        hep?.Attach(Transport);

        if (verbose)
        {
            Transport.EnableTraceLogs();
        }

        // The outbound proxy carries every request to the server while the Request-URI stays the
        // callee's address of record, which is how a real phone with a configured proxy behaves and
        // keeps the server's routing under test rather than bypassed.
        UserAgent = new SIPUserAgent(Transport, options.OutboundProxy);

        // Being held is the commonest reason this role's session stops accepting sends, and it is
        // the far end's decision, so it is watched rather than predicted. Covers the transferor
        // above all: both the transferee and the target hold it during a transfer, and its tone
        // would otherwise warn on every packet for the whole of both windows.
        PauseAudioWhileHeld(UserAgent, () => Media);

        // IPAddress.Any is substituted with the real send-from address by SIPTransport at send
        // time. The transport parameter has to be set here rather than left to that substitution:
        // SIPTransport stamps the protocol onto a Contact for every method EXCEPT REGISTER, where
        // it defers to the caller. Without it the binding is registered as "sip:host:port" with no
        // transport, which a server reads as UDP, and an inbound call is then sent to a UDP port
        // this role is not listening on - the call rings and no INVITE ever arrives.
        var contactUri = new SIPURI(options.Server.Scheme, IPAddress.Any, 0)
        {
            Protocol = options.Protocol
        };

        _registration = new SIPRegistrationUserAgent(
            Transport,
            options.OutboundProxy,
            Aor,
            options.Username,
            options.Password,
            null,                                                   // Realm: taken from the challenge.
            options.Server.ToString(),
            contactUri,
            options.RegistrationExpirySeconds,
            null);

        _registration.RegistrationSuccessful += (_, _) => _registrationOutcome.TrySetResult((true, null));
        _registration.RegistrationFailed += (_, _, error) => _registrationOutcome.TrySetResult((false, error));
        _registration.RegistrationTemporaryFailure += (_, _, error) => _registrationOutcome.TrySetResult((false, error));
    }

    private static SIPChannel CreateChannel(
        SIPProtocolsEnum protocol, int port, RemoteCertificateValidationCallback? certificateValidation) =>
        protocol switch
        {
            SIPProtocolsEnum.udp => new SIPUDPChannel(new IPEndPoint(IPAddress.Any, port)),
            SIPProtocolsEnum.tcp => new SIPTCPChannel(new IPEndPoint(IPAddress.Any, port)),
            // The certificate-less overload is a client only TLS channel: this tool always
            // originates connections and never accepts one, so there is no server certificate.
            SIPProtocolsEnum.tls => new SIPTLSChannel(new IPEndPoint(IPAddress.Any, port), false, certificateValidation),
            _ => throw new ArgumentOutOfRangeException(nameof(protocol), protocol, "Unsupported SIP transport.")
        };

    /// <summary>
    /// Starts the registration and waits for the first outcome. The agent keeps refreshing in the
    /// background afterwards.
    /// </summary>
    public async Task<bool> RegisterAsync(TimeSpan timeout, CancellationToken ct)
    {
        _logger.LogDebug("Registering {Role} as {Aor} from port {Port}.", Name, Aor, Port);

        _registration.Start();

        var completed = await Task.WhenAny(_registrationOutcome.Task, Task.Delay(timeout, ct)).ConfigureAwait(false);

        if (completed != _registrationOutcome.Task)
        {
            RegistrationError = ct.IsCancellationRequested
                ? "Cancelled."
                : $"No registration response within {timeout.TotalSeconds:0}s.";
            return false;
        }

        (Registered, RegistrationError) = await _registrationOutcome.Task.ConfigureAwait(false);

        if (Registered)
        {
            _logger.LogDebug("{Role} registered as {Aor}.", Name, Aor);
        }

        return Registered;
    }

    /// <summary>
    /// Creates the role's media session. Kept separate from the constructor because the transferee
    /// and target only need one once a call arrives, and because the session has to be handed to
    /// SIPUserAgent at call time.
    /// </summary>
    public RoleMedia CreateMedia()
    {
        Media = new RoleMedia(Name, _options.AudioSource, _logger);
        return Media;
    }

    /// <summary>The second call this role is holding, for an attended transfer.</summary>
    public SIPUserAgent? ConsultationAgent { get; private set; }

    public RoleMedia? ConsultationMedia { get; private set; }

    /// <summary>
    /// Stands up a second user agent on this role's transport so it can hold two calls at once.
    /// </summary>
    /// <remarks>
    /// An attended transfer needs the transferor talking to the transferee and to the target at
    /// the same time, and a SIPUserAgent holds exactly one dialogue. A second agent on the same
    /// transport is what a two line phone is: one registration, one Contact, two calls.
    /// </remarks>
    public (SIPUserAgent Agent, RoleMedia Media) CreateConsultation()
    {
        ConsultationAgent = new SIPUserAgent(Transport, _options.OutboundProxy);
        ConsultationMedia = new RoleMedia($"{Name}-consult", _options.AudioSource, _logger);

        PauseAudioWhileHeld(ConsultationAgent, () => ConsultationMedia);

        return (ConsultationAgent, ConsultationMedia);
    }

    /// <summary>
    /// Answers any incoming call with a fresh media session. Both the transferee (called by the
    /// transferor) and the target (called by the transferee after the REFER) need this.
    /// </summary>
    public void AutoAnswer(Action<SIPRequest>? onIncoming = null)
    {
        UserAgent.OnIncomingCall += async (ua, req) =>
        {
            _logger.LogDebug("{Role} incoming call from {From} at {Remote}.",
                Name, req.Header.From?.FriendlyDescription(), req.RemoteSIPEndPoint);

            onIncoming?.Invoke(req);

            var uas = ua.AcceptCall(req);

            if (ua.IsCallActive)
            {
                // A role is only ever in one call at a time in a blind transfer. A second offer
                // means the scenario has gone wrong, and answering it would hide that.
                _logger.LogWarning("{Role} rejected a second incoming call as busy.", Name);
                uas.Reject(SIPResponseStatusCodesEnum.BusyHere, null);
                return;
            }

            await ua.Answer(uas, CreateMedia().Session).ConfigureAwait(false);
        };
    }

    /// <summary>
    /// Pauses a call's audio for as long as the far end has it on hold.
    /// </summary>
    /// <remarks>
    /// A held session refuses to send, and a source that keeps generating logs a warning for every
    /// packet it offers. Tying this to the hold itself rather than to a guessed window means a
    /// send refused for any other reason is still reported, which is the case worth seeing.
    /// </remarks>
    private void PauseAudioWhileHeld(SIPUserAgent agent, Func<RoleMedia?> media)
    {
        agent.RemotePutOnHold += () =>
        {
            _logger.LogDebug("{Role} was put on hold; pausing its audio.", Name);
            _ = media()?.PauseAsync();
        };

        agent.RemoteTookOffHold += () =>
        {
            _logger.LogDebug("{Role} was taken off hold; resuming its audio.", Name);
            _ = media()?.ResumeAsync();
        };
    }

    /// <summary>Removes the registration with a zero expiry re-register and waits briefly for it.</summary>
    public async Task UnregisterAsync()
    {
        // Idempotent because this is called from the scenario's finally block, which also runs
        // after the paths that already unregistered on their way out.
        if (!Registered || _unregistered)
        {
            return;
        }

        _unregistered = true;

        _logger.LogDebug("Removing the registration for {Role} ({Aor}).", Name, Aor);

        _registration.Stop();

        // Long enough for the zero expiry REGISTER to make it out over a WAN round trip before the
        // transport is torn down. Leaving a binding behind is not cosmetic: it points at a port
        // that dies with this process, and the registrar will keep offering it to callers until it
        // expires, so the NEXT run of this command gets its calls routed into a black hole.
        await Task.Delay(UNREGISTER_SETTLE, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Ends this role's call, if it still has one, and waits for the BYE transaction to finish.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Dispose"/>, and awaited, for two reasons that both showed up as
    /// BYEs retransmitting at the end of a run.
    ///
    /// Hangup only queues the request. Shutting the transport down straight afterwards means the
    /// 200 has nowhere to arrive, so the transaction never completes and retransmits until it
    /// times out - the same hazard the ring-timeout path already waits out.
    ///
    /// The wait also settles the other end. After a transfer the surviving call has one of these
    /// roles at each end, and hanging both up at once leaves two BYEs crossing with nobody left to
    /// answer either. Giving the first one time to be relayed means the second role sees its call
    /// already gone and stays quiet.
    /// </remarks>
    public async Task HangupAsync(TimeSpan settle)
    {
        var hungUp = false;

        if (UserAgent.IsCallActive)
        {
            _logger.LogDebug("{Role} hanging up.", Name);
            UserAgent.Hangup();
            hungUp = true;
        }

        // The consultation call of an attended transfer is normally gone by now: the target ends
        // it when it accepts the call replacing it. It is hung up here for the runs where the
        // transfer did not get that far.
        if (ConsultationAgent?.IsCallActive == true)
        {
            _logger.LogDebug("{Role} hanging up its consultation call.", Name);
            ConsultationAgent.Hangup();
            hungUp = true;
        }

        if (hungUp)
        {
            await Task.Delay(settle, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Releases the transport and media. Deliberately sends nothing: a role that still has a call
    /// at this point would be originating a BYE it cannot wait for the answer to.
    /// </summary>
    public void Dispose()
    {
        Media?.Dispose();
        ConsultationMedia?.Dispose();
        Transport.Shutdown();
    }
}
