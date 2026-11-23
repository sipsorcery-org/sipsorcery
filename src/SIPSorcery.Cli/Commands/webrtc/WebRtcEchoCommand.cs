//-----------------------------------------------------------------------------
// Filename: WebRtcEchoCommand.cs
//
// Description: The "sipsorcery webrtc echo" verb. Acts as a WebRTC echo test
// client per the webrtc-echoes interoperability specification: it builds a peer
// connection with an audio track and a data channel, POSTs the SDP offer as JSON
// to an echo server's /offer endpoint, applies the answer, and verifies the data
// channel round trips a message back. The equivalent of the webrtccmdline
// "--echoclient" option.
//
// See: https://github.com/sipsorcery/webrtc-echoes
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
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Commands;

public sealed class WebRtcEchoCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 15;
    private const int VP8_PAYLOAD_ID = 96;
    private const string DATA_CHANNEL_LABEL = "dcx";

    /// <summary>
    /// The result shape written to stdout with --json. Stable field names; additive changes only.
    /// </summary>
    private sealed record EchoResult(
        bool Success,
        string Url,
        string ConnectionState,
        long? ConnectTimeMs,
        bool DataChannelEcho,
        int AudioPacketsEchoed,
        int VideoPacketsEchoed,
        string? Error);

    public WebRtcEchoCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var urlArg = new Argument<string>("url")
        {
            Description = "The echo server's offer endpoint, e.g. http://localhost:8080/offer."
        };

        var stunOption = new Option<string?>("--stun")
        {
            Description = "A STUN or TURN server for the peer connection, format \"(stun|turn):host[:port][;username;password]\"."
        };

        var relayOnlyOption = new Option<bool>("--relay-only")
        {
            Description = "Only use relay (TURN) candidates (sets the ICE transport policy to relay)."
        };

        var noAudioOption = new Option<bool>("--no-audio")
        {
            Description = "Do not include an audio track in the offer."
        };

        var noDataOption = new Option<bool>("--no-data")
        {
            Description = "Do not include a data channel (then the connection state is the only success signal)."
        };

        // Cloudflare TURN options (same as the "cloudflare turn" verb). When a key ID and token are
        // supplied, short lived TURN credentials are fetched and added as an ICE server before the
        // connection is offered.
        var keyIdOption = CloudflareTurn.CreateKeyIdOption();
        var tokenOption = CloudflareTurn.CreateTokenOption();
        var ttlOption = CloudflareTurn.CreateTtlOption();
        var transportOption = CloudflareTurn.CreateTransportOption();

        var command = new Command("echo", "Run a WebRTC echo test against an echo server and verify the data channel round trips (webrtccmdline --echoclient).");
        command.Arguments.Add(urlArg);
        command.Options.Add(stunOption);
        command.Options.Add(relayOnlyOption);
        command.Options.Add(noAudioOption);
        command.Options.Add(noDataOption);
        command.Options.Add(keyIdOption);
        command.Options.Add(tokenOption);
        command.Options.Add(ttlOption);
        command.Options.Add(transportOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(urlArg)!,
            parseResult.GetValue(stunOption),
            parseResult.GetValue(relayOnlyOption),
            parseResult.GetValue(noAudioOption),
            parseResult.GetValue(noDataOption),
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

    private static async Task<int> RunAsync(string url, string? stun, bool relayOnly, bool noAudio, bool noData,
        string? turnKeyId, string? turnToken, int turnTtl, string turnTransport,
        int timeoutSeconds, bool asJson, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(WebRtcEchoCommand));

        if (!Uri.TryCreate(url, UriKind.Absolute, out var endpointUri) ||
            (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
        {
            return WriteResult(asJson,
                new EchoResult(false, url, "new", null, false, 0, 0, $"Could not parse \"{url}\" as an HTTP or HTTPS URL."),
                ExitCodes.InvalidArgument);
        }

        var config = new RTCConfiguration { iceServers = new List<RTCIceServer>() };
        if (!string.IsNullOrWhiteSpace(stun))
        {
            string[] fields = stun.Split(';');
            config.iceServers.Add(new RTCIceServer
            {
                urls = fields[0],
                username = fields.Length > 1 ? fields[1] : null,
                credential = fields.Length > 2 ? fields[2] : null,
                credentialType = RTCIceCredentialType.password
            });
        }

        // If Cloudflare TURN is requested, fetch credentials first and add the TURN server to the
        // peer connection's ICE servers.
        CloudflareTurn.ResolveCredentials(ref turnKeyId, ref turnToken);
        bool turnRequested = !string.IsNullOrWhiteSpace(turnKeyId) || !string.IsNullOrWhiteSpace(turnToken);
        if (turnRequested)
        {
            if (string.IsNullOrWhiteSpace(turnKeyId) || string.IsNullOrWhiteSpace(turnToken))
            {
                return WriteResult(asJson,
                    new EchoResult(false, url, "new", null, false, 0, 0,
                        "Both a Cloudflare TURN key ID and token are required (--key-id/--token or CLOUDFLARE_TURN_KEY_ID/CLOUDFLARE_API_TOKEN)."),
                    ExitCodes.InvalidArgument);
            }

            if (!CloudflareTurn.TryResolveTurnUrl(turnTransport, out string turnUrl, out string? urlError))
            {
                return WriteResult(asJson,
                    new EchoResult(false, url, "new", null, false, 0, 0, urlError),
                    ExitCodes.InvalidArgument);
            }

            var fetch = await CloudflareTurn.FetchIceServerAsync(turnKeyId, turnToken, turnTtl, turnUrl, timeoutSeconds, logger, ct).ConfigureAwait(false);
            if (fetch.Error != null)
            {
                return WriteResult(asJson,
                    new EchoResult(false, url, "new", null, false, 0, 0, $"Could not obtain Cloudflare TURN credentials: {fetch.Error}"),
                    ExitCodes.Failed);
            }

            logger.LogDebug("Added Cloudflare TURN server {TurnUrl};{TurnUsername};{TurnCredential} to the peer connection.", turnUrl, fetch.IceServer?.username, fetch.IceServer?.credential);
            config.iceServers.Add(fetch.IceServer!);
        }

        if (relayOnly)
        {
            config.iceTransportPolicy = RTCIceTransportPolicy.relay;
        }

        var pc = new RTCPeerConnection(config);
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };

        int audioEchoed = 0;
        int videoEchoed = 0;
        var dataChannelEcho = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            if (!noAudio)
            {
                pc.addTrack(new MediaStreamTrack(SDPWellKnownMediaFormatsEnum.PCMU));
            }

            // Count any media the echo server reflects back (only non-zero if media is being sent,
            // which this verb does not do; kept for symmetry and future media echo support).
            pc.OnRtpPacketReceived += (remoteEndPoint, mediaType, rtpPacket) =>
            {
                if (mediaType == SDPMediaTypesEnum.audio) { Interlocked.Increment(ref audioEchoed); }
                else if (mediaType == SDPMediaTypesEnum.video) { Interlocked.Increment(ref videoEchoed); }
            };

            string pseudo = Guid.NewGuid().ToString("N")[..8];

            if (!noData)
            {
                var dc = await pc.createDataChannel(DATA_CHANNEL_LABEL).ConfigureAwait(false);
                dc.onopen += () =>
                {
                    logger.LogDebug("Data channel open, sending echo probe \"{Probe}\".", pseudo);
                    dc.send(pseudo);
                };
                dc.onmessage += (rdc, proto, data) =>
                {
                    string echoed = Encoding.UTF8.GetString(data);
                    logger.LogDebug("Data channel received \"{Echoed}\".", echoed);
                    dataChannelEcho.TrySetResult(echoed == pseudo);
                };
            }

            var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pc.onconnectionstatechange += (state) =>
            {
                logger.LogDebug("Peer connection state changed to {State}.", state);
                if (state == RTCPeerConnectionState.connected) { connected.TrySetResult(true); }
                else if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.closed) { connected.TrySetResult(false); }
            };

            var offer = pc.createOffer(new RTCOfferOptions { X_WaitForIceGatheringToComplete = true });
            await pc.setLocalDescription(offer).ConfigureAwait(false);

            // Short circuit here if no ICE candidates were gathered. This is only likely to happen if a relay only policy was requested and
            // the TURN server was not valid or the credentials failed.
            if(pc.GetRtpChannel().Candidates.Count == 0)
            {
                logger.LogWarning("No ICE candidates were gathered. Check the STUN/TURN server configuration.");

                return WriteResult(asJson,
                    new EchoResult(false, url, pc.connectionState.ToString(), null, false, 0, 0,
                        "No ICE candidates were gathered. Check the STUN/TURN server configuration."),
                    ExitCodes.Failed);
            }

            var stopwatch = Stopwatch.StartNew();

            using var content = new StringContent(offer.toJSON(), Encoding.UTF8, "application/json");
            using var response = await httpClient.PostAsync(endpointUri, content, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return WriteResult(asJson,
                    new EchoResult(false, url, pc.connectionState.ToString(), null, false, 0, 0,
                        $"The echo server returned HTTP {(int)response.StatusCode}."),
                    ExitCodes.Failed);
            }

            string answerStr = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!RTCSessionDescriptionInit.TryParse(answerStr, out var answerInit))
            {
                return WriteResult(asJson,
                    new EchoResult(false, url, pc.connectionState.ToString(), null, false, 0, 0,
                        "The echo server's answer could not be parsed as an SDP answer."),
                    ExitCodes.Failed);
            }

            var setAnswerResult = pc.setRemoteDescription(answerInit);
            if (setAnswerResult != SetDescriptionResultEnum.OK)
            {
                return WriteResult(asJson,
                    new EchoResult(false, url, pc.connectionState.ToString(), null, false, 0, 0,
                        $"The SDP answer could not be applied: {setAnswerResult}."),
                    ExitCodes.Failed);
            }

            // With --relay-only every candidate pair routes through the (public) TURN relay. If the
            // remote peer only offered private/loopback candidates the relay cannot create a
            // permission for them and the connection will fail, so warn up front rather than after a
            // timeout.
            if (relayOnly)
            {
                WarnIfRemoteCandidatesAllPrivate(answerInit.sdp, logger);
            }

            var connectCompleted = await Task.WhenAny(connected.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct)).ConfigureAwait(false);

            if (connectCompleted != connected.Task || !await connected.Task.ConfigureAwait(false))
            {
                return WriteResult(asJson,
                    new EchoResult(false, url, pc.connectionState.ToString(), stopwatch.ElapsedMilliseconds, false, audioEchoed, videoEchoed,
                        connectCompleted == connected.Task
                            ? $"The peer connection failed (state {pc.connectionState})."
                            : $"The peer connection did not reach connected within {timeoutSeconds}s."),
                    ExitCodes.Timeout);
            }

            long connectTimeMs = stopwatch.ElapsedMilliseconds;

            bool echoOk = noData;
            if (!noData)
            {
                var echoCompleted = await Task.WhenAny(dataChannelEcho.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct)).ConfigureAwait(false);
                echoOk = echoCompleted == dataChannelEcho.Task && await dataChannelEcho.Task.ConfigureAwait(false);
            }

            return WriteResult(asJson,
                new EchoResult(echoOk, url, pc.connectionState.ToString(), connectTimeMs, !noData && echoOk, audioEchoed, videoEchoed,
                    echoOk ? null : "The connection succeeded but the data channel did not echo the probe message."),
                echoOk ? ExitCodes.Ok : ExitCodes.Failed);
        }
        catch (OperationCanceledException)
        {
            return WriteResult(asJson,
                new EchoResult(false, url, pc.connectionState.ToString(), null, false, audioEchoed, videoEchoed, "Cancelled or the request timed out."),
                ExitCodes.Timeout);
        }
        catch (Exception excp)
        {
            return WriteResult(asJson,
                new EchoResult(false, url, pc.connectionState.ToString(), null, false, audioEchoed, videoEchoed, excp.Message),
                ExitCodes.TransportError);
        }
        finally
        {
            pc.Close("echo test complete");
        }
    }

    /// <summary>
    /// Parses the candidate addresses out of the remote answer SDP and, if every one is a
    /// private/loopback address, warns that a public TURN relay cannot reach them under --relay-only.
    /// mDNS (.local) candidates are not IP literals so they are ignored.
    /// </summary>
    private static void WarnIfRemoteCandidatesAllPrivate(string? answerSdp, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(answerSdp))
        {
            return;
        }

        var addresses = new List<IPAddress>();
        foreach (var line in answerSdp.Split('\n'))
        {
            // a=candidate:<foundation> <component> <transport> <priority> <address> <port> typ <type> ...
            int idx = line.IndexOf("candidate:", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                continue;
            }

            var parts = line[idx..].Trim().Split(' ');
            if (parts.Length >= 5 && IPAddress.TryParse(parts[4], out var addr))
            {
                addresses.Add(addr);
            }
        }

        if (addresses.Count > 0 && addresses.TrueForAll(IsPrivateAddress))
        {
            logger.LogWarning("--relay-only is set but the remote peer's {Count} candidate(s) are all private/loopback addresses ({Addresses}). " +
                "A public TURN relay (e.g. Cloudflare) cannot create permissions for private addresses, so the connection will likely fail. " +
                "Use a publicly reachable peer, or drop --relay-only.",
                addresses.Count, string.Join(", ", addresses));
        }
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] b = address.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254);   // link-local
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // Link-local (fe80::/10), site-local (deprecated) or unique-local (fc00::/7).
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
        }

        return false;
    }

    private static int WriteResult(bool asJson, EchoResult result, int exitCode)
    {
        if (asJson)
        {
            WriteJson(result);
        }
        else if (result.Success)
        {
            string echo = result.DataChannelEcho ? "data channel echo OK" : "connected (no data channel)";
            Console.WriteLine($"WebRTC echo test to {result.Url} succeeded in {result.ConnectTimeMs}ms: {echo}.");
        }
        else
        {
            Console.Error.WriteLine($"WebRTC echo test to {result.Url} failed (state {result.ConnectionState}): {result.Error}");
        }

        return exitCode;
    }
}
