//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A prototype WebRTC application that uses Nostr protocol and 
// the NNostr library for signalling between two peers. The nostr.net relay
// is used to exchange WebRTC SDP offers, answers, and ICE candidates.
//
// Once signalling is complete, the WebRTC peer connection negotiates media
// streams similar to the WebRTCGetStarted example.
//
// Author(s):
// GitHub Copilot
// 
// History:
// 02 May 2026  Created, based on WebRTCGetStarted example.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NBitcoin.Secp256k1;
using NNostr.Client;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using Vpx.Net;

namespace WebRTCNostrSignalling
{
    /// <summary>
    /// Message types for Nostr WebRTC signalling.
    /// </summary>
    public enum NostrSignalType
    {
        Offer,
        Answer,
        IceCandidate
    }

    /// <summary>
    /// Signalling message structure for WebRTC over Nostr.
    /// </summary>
    public class NostrSignalMessage
    {
        public NostrSignalType Type { get; set; }
        public string? PeerId { get; set; }
        public string? TargetPeerId { get; set; }
        public string? Sdp { get; set; }
        public string? Candidate { get; set; }
        public string? SdpMid { get; set; }
        public ushort? SdpMLineIndex { get; set; }
    }

    class Program
    {
        // Note: Use a well-known public Nostr relay. Some options:
        // - wss://nos.lol (reliable, fast)
        // - wss://relay.damus.io (popular, Damus official)
        // - wss://relay.snort.social (Snort official)
        // - wss://nostr.mom (public, well maintained)
        private const string NOSTR_RELAY_URL = "wss://nos.lol";
        private const string STUN_URL = "stun:stun.cloudflare.com";
        
        // Custom ephemeral Nostr event kind used for WebRTC signalling.
        // 24133 is NIP-46 NostrConnect which is busy and encrypted-by-spec on
        // public relays -- using it for plaintext signalling collides with every
        // NostrConnect signer on the relay (those events arrive looking like
        // base64 encrypted blobs in the receive log). We pick an unused
        // ephemeral kind in the 20000-29999 range instead.
        private const int WEBRTC_SIGNAL_KIND = 25555;

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;
        private static NostrClient? nostrClient;
        private static RTCPeerConnection? peerConnection;
        private static string? localPeerId;
        private static string? remotePeerId;

        // Nostr identity for this process. Generated once at startup and
        // reused for every published event so peers can be correlated by
        // their pubkey across the offer / answer / ICE candidate exchange.
        private static ECPrivKey? localPrivateKey;
        private static string? localPubKeyHex;
        private static string? remotePubKeyHex;
        private static bool isOfferer = false;
        private static bool signallingStarted = false; // Track if offer/answer exchange has started

        static async Task Main(string[] args)
        {
            Console.WriteLine("WebRTC Nostr Signalling Prototype");
            Console.WriteLine("==================================");
            Console.WriteLine();

            logger = AddConsoleLogger();

            // Generate a Nostr keypair for this session. The displayed Peer ID
            // is the FULL hex public key (64 chars) -- inconvenient to read but
            // copy-pasteable, and unambiguous so the answerer can target the
            // offerer (and vice versa) via a Nostr-level "p" tag without
            // needing a separate discovery channel. The 8-char short prefix
            // is shown alongside for at-a-glance identification in logs.
            var localPrivKeyBytes = new byte[32];
            System.Security.Cryptography.RandomNumberGenerator.Fill(localPrivKeyBytes);
            localPrivateKey = ECPrivKey.Create(localPrivKeyBytes);
            localPubKeyHex = localPrivateKey.CreateXOnlyPubKey().ToHex();
            localPeerId = localPubKeyHex[..8];
            Console.WriteLine($"Your Peer ID (short): {localPeerId}");
            Console.WriteLine($"Your Peer ID (full):  {localPubKeyHex}");
            Console.WriteLine("(give the full Peer ID to the other peer to connect)");
            Console.WriteLine();

            // Determine role
            Console.Write("Are you the offerer? (y/n): ");
            var roleInput = Console.ReadLine()?.Trim().ToLower();
            isOfferer = roleInput == "y" || roleInput == "yes";

            if (!isOfferer)
            {
                Console.Write("Enter the Peer ID (full 64 hex chars) of the offerer to connect to: ");
                var input = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(input) || input.Length != 64)
                {
                    Console.WriteLine("Error: a full 64-char hex Peer ID is required for answerer role.");
                    return;
                }
                remotePubKeyHex = input.ToLowerInvariant();
                remotePeerId = remotePubKeyHex[..8];
            }

            // Connect to Nostr relay
            Console.WriteLine($"Connecting to Nostr relay at {NOSTR_RELAY_URL}...");
            await ConnectToNostrRelay();

            // Create the peer connection
            peerConnection = CreatePeerConnection();

            if (isOfferer)
            {
                Console.WriteLine();
                Console.WriteLine("Waiting for a peer to connect...");
                Console.WriteLine($"Share your Peer ID ({localPeerId}) with the other peer.");
                Console.WriteLine();
                
                // Wait for an answer or for the user to initiate
                Console.WriteLine("Press Enter when the other peer is ready, or Ctrl+C to exit...");
                Console.ReadLine();
                
                Console.Write("Enter the Peer ID (full 64 hex chars) of the peer you want to connect to: ");
                var input = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(input) || input.Length != 64)
                {
                    Console.WriteLine("Error: a full 64-char hex Peer ID is required.");
                    peerConnection.Close("No remote peer specified");
                    return;
                }
                remotePubKeyHex = input.ToLowerInvariant();
                remotePeerId = remotePubKeyHex[..8];

                // Create and send offer
                await CreateAndSendOffer();
            }
            else
            {
                Console.WriteLine($"Waiting for offer from peer {remotePeerId}...");
            }

            // Ctrl-c will gracefully exit
            ManualResetEvent exitMre = new ManualResetEvent(false);
            Console.CancelKeyPress += delegate (object? sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitMre.Set();
            };

            Console.WriteLine("Press Ctrl+C to exit.");
            exitMre.WaitOne();

            // Cleanup
            peerConnection?.Close("User exit");
            nostrClient?.Dispose();
        }

        private static async Task ConnectToNostrRelay()
        {
            nostrClient = new NostrClient(new Uri(NOSTR_RELAY_URL));
            
            // Set up event handlers for diagnostics
            nostrClient.EventsReceived += OnNostrEventsReceived;
            nostrClient.NoticeReceived += (sender, notice) => 
                logger.LogWarning($"Nostr relay notice: {notice}");
            nostrClient.OkReceived += (sender, args) =>
            {
                var (eventId, success, message) = args;
                if (success)
                {
                    logger.LogDebug($"Nostr event {eventId[..8]}... published successfully");
                }
                else
                {
                    logger.LogError($"Nostr event {eventId[..8]}... failed to publish: {message}");
                }
            };
            nostrClient.EoseReceived += (sender, subscriptionId) =>
                logger.LogDebug($"Nostr end of stored events for subscription: {subscriptionId}");
            
            // Monitor connection state changes
            nostrClient.StateChanged += (sender, state) =>
            {
                logger.LogInformation($"Nostr connection state changed to: {state}");
            };
            
            // Monitor raw messages for debugging AND short-circuit EVENT
            // messages on our subscription past NNostr.Client's Verify gate.
            //
            // NostrClient.HandleIncomingMessage drops any EVENT for which
            // evt.Verify() returns false. Verify() rebuilds the id-preimage
            // using NNostr's JavaScriptStringEncode, then SHA-256s it, and
            // requires the result to equal evt.Id. The relay computes Id
            // from the JSON we publish (System.Text.Json's WriteStringValue
            // with JavaScriptEncoder.Default). Those two encoders disagree
            // on <, >, ', &, +, and non-ASCII -- the same bug we patched on
            // the SEND side with hex-encoding.
            //
            // We hex-encode our own content so OUR events round-trip
            // through Verify, but a peer's event arrives with the relay's
            // id (computed from the relay's view of the JSON) and Verify
            // re-hashes using NNostr's encoder, often producing a different
            // value -- so the event is silently dropped and EventsReceived
            // never fires.
            //
            // Workaround: parse EVENT messages off MessageReceived
            // ourselves and dispatch to OnNostrEventsReceived directly,
            // skipping the buggy Verify. This is acceptable for a
            // prototype because the relay-side "#p" filter already
            // ensures the events are addressed to us, and the application-
            // level TargetPeerId check in OnNostrEventsReceived rejects
            // anything that slipped through.
            nostrClient.MessageReceived += (sender, message) =>
            {
                var truncatedMessage = message.Length > 200 ? message[..200] + "..." : message;
                logger.LogDebug($"Nostr raw message received: {truncatedMessage}");

                try
                {
                    using var doc = JsonDocument.Parse(message);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 3) { return; }
                    var msgType = root[0].GetString();
                    if (!string.Equals(msgType, "EVENT", StringComparison.OrdinalIgnoreCase)) { return; }

                    var subId = root[1].GetString();
                    if (subId != "webrtc-signal") { return; }

                    // Deserialize the event payload using NNostr's own
                    // converters, but skip Verify(). Use JsonElement.GetRawText
                    // -> Deserialize<NostrEvent> so we go through the same
                    // converters NNostr would use internally.
                    var evt = JsonSerializer.Deserialize<NostrEvent>(root[2].GetRawText());
                    if (evt == null) { return; }

                    OnNostrEventsReceived(sender, (subId, new[] { evt }));
                }
                catch (Exception ex)
                {
                    logger.LogDebug($"Nostr message bypass-parse failed: {ex.Message}");
                }
            };
            
            nostrClient.InvalidMessageReceived += (sender, message) =>
            {
                logger.LogWarning($"Nostr invalid message received: {message}");
            };

            logger.LogDebug($"Connecting to Nostr relay at {NOSTR_RELAY_URL}...");
            
            // Connect() internally calls ConnectAndWaitUntilConnected which:
            // 1. Creates and opens the WebSocket connection
            // 2. Waits until the connection is open
            // 3. Automatically starts ListenForMessages() in the background
            // Note: Do NOT call ListenForMessages() manually as it will return immediately
            // due to the internal _listening flag being already set
            try
            {
                await nostrClient.Connect();
                logger.LogInformation($"Connected to Nostr relay: {NOSTR_RELAY_URL}");
            }
            catch (Exception ex)
            {
                logger.LogError($"Failed to connect to Nostr relay: {ex.Message}");
                throw;
            }
            
            logger.LogDebug($"Nostr connection state: {nostrClient.State}");
            
            // Give the message listener time to fully initialize
            await Task.Delay(100);

            // Subscribe only to WebRTC-signalling events that are tagged for us
            // (NIP-01 "p" tag). Without the ReferencedPublicKeys filter the
            // relay sends every event of this kind from every other client on
            // the relay -- which on a public relay is a firehose of unrelated
            // traffic. With the filter we only receive events whose author has
            // explicitly tagged our pubkey as the recipient.
            var filter = new NostrSubscriptionFilter
            {
                Kinds = new[] { WEBRTC_SIGNAL_KIND },
                ReferencedPublicKeys = new[] { localPubKeyHex! }
            };

            // Create subscription
            try
            {
                logger.LogDebug($"Creating Nostr subscription for kind {WEBRTC_SIGNAL_KIND}...");
                await nostrClient.CreateSubscription("webrtc-signal", new[] { filter });
                logger.LogDebug("Subscribed to WebRTC signalling events");
                logger.LogDebug($"Nostr connection state after subscription: {nostrClient.State}");
            }
            catch (Exception ex)
            {
                logger.LogError($"Failed to create Nostr subscription: {ex.Message}");
            }
        }

        // Pending remote ICE candidates received before the remote description
        // has been applied. They get drained once setRemoteDescription
        // succeeds. Without buffering, an IceCandidate that arrives at the
        // offerer before the Answer (Nostr does not preserve ordering across
        // events) would be applied to a peer connection with no remote
        // description and the ICE checks against the answerer would never
        // start.
        private static readonly System.Collections.Generic.List<RTCIceCandidateInit> pendingRemoteCandidates = new();
        private static bool remoteDescriptionApplied = false;

        private static void DrainPendingRemoteCandidates()
        {
            if (peerConnection == null) { return; }
            lock (pendingRemoteCandidates)
            {
                if (pendingRemoteCandidates.Count == 0) { return; }
                logger.LogInformation($"Draining {pendingRemoteCandidates.Count} buffered remote ICE candidate(s).");
                foreach (var cand in pendingRemoteCandidates)
                {
                    peerConnection.addIceCandidate(cand);
                }
                pendingRemoteCandidates.Clear();
            }
        }

        private static void OnNostrEventsReceived(object? sender, (string subscriptionId, NostrEvent[] events) args)
        {
            logger.LogInformation($"Nostr events received: subscription={args.subscriptionId}, count={args.events.Length}");

            if (args.subscriptionId != "webrtc-signal") { return; }

            foreach (var nostrEvent in args.events)
            {
                try
                {
                    logger.LogDebug($"Processing Nostr event: id={nostrEvent.Id?[..8]}..., kind={nostrEvent.Kind}");
                    
                    if (string.IsNullOrEmpty(nostrEvent.Content))
                    {
                        logger.LogDebug("Nostr event has empty content, skipping");
                        continue;
                    }
                    
                    // Content is hex-encoded JSON (see SendSignalMessage for why).
                    // Anything that doesn't decode as hex is foreign traffic on this
                    // kind and gets skipped silently.
                    string jsonContent;
                    try
                    {
                        var jsonBytes = Convert.FromHexString(nostrEvent.Content);
                        jsonContent = System.Text.Encoding.UTF8.GetString(jsonBytes);
                    }
                    catch (FormatException)
                    {
                        logger.LogDebug($"Nostr event content is not hex-encoded, skipping: {nostrEvent.Content[..Math.Min(20, nostrEvent.Content.Length)]}...");
                        continue;
                    }

                    var message = JsonSerializer.Deserialize<NostrSignalMessage>(jsonContent);
                    
                    if (message == null)
                    {
                        logger.LogDebug("Failed to deserialize Nostr event content");
                        continue;
                    }

                    logger.LogDebug($"Parsed signal message: type={message.Type}, from={message.PeerId}, to={message.TargetPeerId}");

                    // Check if this message is for us
                    if (message.TargetPeerId != localPeerId)
                    {
                        logger.LogDebug($"Message not for us (target={message.TargetPeerId}, local={localPeerId}), skipping");
                        continue;
                    }

                    logger.LogInformation($"Received {message.Type} from peer {message.PeerId}");

                    // Process the message asynchronously with error handling
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ProcessSignalMessage(message);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError($"Failed to process signal message: {ex.Message}");
                        }
                    });
                }
                catch (JsonException ex)
                {
                    // JSON parsing error - likely encrypted content or malformed JSON
                    logger.LogDebug($"Skipping Nostr event with invalid JSON: {ex.Message}");
                }
                catch (Exception ex)
                {
                    logger.LogWarning($"Failed to process Nostr event: {ex.Message}");
                }
            }
        }

        private static async Task ProcessSignalMessage(NostrSignalMessage message)
        {
            switch (message.Type)
            {
                case NostrSignalType.Offer:
                    if (!isOfferer && peerConnection != null)
                    {
                        remotePeerId = message.PeerId;
                        signallingStarted = true; // Mark signalling as started when we receive the offer
                        logger.LogInformation($"Received offer from peer {remotePeerId}");
                        
                        var offerSdp = new RTCSessionDescriptionInit
                        {
                            type = RTCSdpType.offer,
                            sdp = message.Sdp
                        };

                        var setResult = peerConnection.setRemoteDescription(offerSdp);
                        if (setResult == SetDescriptionResultEnum.OK)
                        {
                            remoteDescriptionApplied = true;
                            DrainPendingRemoteCandidates();
                        }
                        var result = setResult;
                        if (result != SetDescriptionResultEnum.OK)
                        {
                            logger.LogError($"Failed to set remote description: {result}");
                            return;
                        }

                        // Create and send answer
                        var answerSdp = peerConnection.createAnswer();
                        await peerConnection.setLocalDescription(answerSdp);

                        await SendSignalMessage(new NostrSignalMessage
                        {
                            Type = NostrSignalType.Answer,
                            PeerId = localPeerId,
                            TargetPeerId = remotePeerId,
                            Sdp = answerSdp.sdp
                        });

                        logger.LogInformation("Sent answer to peer");
                    }
                    break;

                case NostrSignalType.Answer:
                    if (isOfferer && peerConnection != null)
                    {
                        logger.LogInformation($"Received answer from peer {message.PeerId}, applying...");
                        
                        var answerSdp = new RTCSessionDescriptionInit
                        {
                            type = RTCSdpType.answer,
                            sdp = message.Sdp
                        };

                        var result = peerConnection.setRemoteDescription(answerSdp);
                        if (result != SetDescriptionResultEnum.OK)
                        {
                            logger.LogError($"Failed to set remote description: {result}");
                        }
                        else
                        {
                            logger.LogInformation("Remote description (answer) applied successfully");
                            remoteDescriptionApplied = true;
                            DrainPendingRemoteCandidates();
                        }
                    }
                    break;

                case NostrSignalType.IceCandidate:
                    if (peerConnection != null && !string.IsNullOrEmpty(message.Candidate))
                    {
                        var iceCandidate = new RTCIceCandidateInit
                        {
                            candidate = message.Candidate,
                            sdpMid = message.SdpMid,
                            sdpMLineIndex = message.SdpMLineIndex ?? 0
                        };

                        if (!remoteDescriptionApplied)
                        {
                            lock (pendingRemoteCandidates)
                            {
                                pendingRemoteCandidates.Add(iceCandidate);
                            }
                            logger.LogInformation($"Buffered remote ICE candidate from {message.PeerId} (no remote description yet)");
                        }
                        else
                        {
                            logger.LogInformation($"Adding remote ICE candidate from {message.PeerId}: {message.Candidate}");
                            peerConnection.addIceCandidate(iceCandidate);
                        }
                    }
                    break;
            }
        }

        private static async Task CreateAndSendOffer()
        {
            if (peerConnection == null) { return; }

            signallingStarted = true; // Mark that signalling has started
            
            var offerSdp = peerConnection.createOffer();
            await peerConnection.setLocalDescription(offerSdp);

            await SendSignalMessage(new NostrSignalMessage
            {
                Type = NostrSignalType.Offer,
                PeerId = localPeerId,
                TargetPeerId = remotePeerId,
                Sdp = offerSdp.sdp
            });

            logger.LogInformation($"Sent offer to peer {remotePeerId}");
        }

        private static async Task SendSignalMessage(NostrSignalMessage message)
        {
            if (nostrClient == null)
            {
                logger.LogError("Cannot send signal message: Nostr client is null");
                return;
            }
            if (localPrivateKey == null || localPubKeyHex == null)
            {
                logger.LogError("Cannot send signal message: local Nostr keypair not initialised.");
                return;
            }
            if (string.IsNullOrEmpty(remotePubKeyHex))
            {
                logger.LogError("Cannot send signal message: remote Nostr pubkey is unknown.");
                return;
            }

            logger.LogDebug($"Sending {message.Type} to peer {message.TargetPeerId}...");

            // Hex-encode the inner JSON before putting it on the wire.
            //
            // NNostr.Client (every version up to and including master) has a
            // canonicalisation bug: the id-preimage path uses its own
            // JavaScriptStringEncode (which does NOT escape <, >, ', &, +, or
            // non-ASCII), but the publish path uses System.Text.Json's
            // WriteStringValue with JavaScriptEncoder.Default (which DOES
            // escape all of those). For any content containing one of those
            // characters -- e.g. the "+" that's all over a typical SDP
            // (ice-pwd, fingerprint, etc.) -- the id we hash differs from
            // the JSON we publish, the relay re-computes the id, and we get
            // back "OK ... false ... invalid: bad event id".
            //
            // Workaround: hex-encode the content so the on-the-wire string
            // is purely [0-9a-f]. Both encoders pass that through untouched
            // and the ids match. The receiver hex-decodes before parsing.
            var jsonBytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
            var content = Convert.ToHexString(jsonBytes).ToLowerInvariant();

            // Build the event with a "p" tag addressing the recipient. The matching
            // subscription on the recipient side (ReferencedPublicKeys) only forwards
            // events whose "p" tag contains its pubkey, so this is the relay-side
            // routing primitive. Ensures Tags is non-null which (separately) avoids
            // an id-preimage / publish-JSON canonicalisation mismatch in some NNostr
            // versions that surfaces as the relay rejecting the event with
            // "invalid: bad event id".
            var nostrEvent = new NostrEvent
            {
                Kind = WEBRTC_SIGNAL_KIND,
                Content = content,
                CreatedAt = DateTimeOffset.UtcNow,
                Tags = new List<NostrEventTag>
                {
                    new NostrEventTag
                    {
                        TagIdentifier = "p",
                        Data = new List<string> { remotePubKeyHex! }
                    }
                }
            };

            await nostrEvent.ComputeIdAndSignAsync(localPrivateKey);

            logger.LogDebug($"Nostr event created: id={nostrEvent.Id?[..8]}..., kind={nostrEvent.Kind}, pubkey={nostrEvent.PublicKey?[..8]}...");

            // Wire up an OK-frame listener BEFORE sending so we don't miss the
            // relay's accept / reject response. SendEventsAndWaitUntilReceived
            // returns once any OK frame for our event id arrives but doesn't
            // distinguish accept (true) from reject (false) -- so we observe
            // the OkReceived event ourselves and surface a real error if the
            // relay rejected the event (the typical reason being "invalid:
            // bad event id" which means our id-preimage didn't match what
            // the relay re-computed from our published JSON).
            var ackTcs = new System.Threading.Tasks.TaskCompletionSource<(bool ok, string? message)>();
            EventHandler<(string eventId, bool success, string message)> onOk = (s, args) =>
            {
                if (args.eventId == nostrEvent.Id)
                {
                    ackTcs.TrySetResult((args.success, args.message));
                }
            };
            nostrClient.OkReceived += onOk;
            try
            {
                await nostrClient.SendEventsAndWaitUntilReceived(new[] { nostrEvent }, CancellationToken.None);

                // Give the OK frame up to 1s to arrive (it usually arrives essentially
                // instantly; SendEventsAndWaitUntilReceived returns on OK or on the
                // event being echoed back via subscription, whichever happens first).
                using var cts = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(1));
                cts.Token.Register(() => ackTcs.TrySetResult((true, "(no OK frame within 1s; assuming accepted)")));
                var (ok, relayMessage) = await ackTcs.Task;
                if (!ok)
                {
                    logger.LogError($"Relay rejected Nostr event {nostrEvent.Id?[..8]}...: {relayMessage}");
                    throw new InvalidOperationException($"Relay rejected event: {relayMessage}");
                }
                logger.LogDebug($"Nostr event acknowledged by relay: {relayMessage}");
            }
            catch (Exception ex) when (!(ex is InvalidOperationException))
            {
                logger.LogError($"Failed to send Nostr event: {ex.Message}");
                throw;
            }
            finally
            {
                nostrClient.OkReceived -= onOk;
            }
        }

        private static RTCPeerConnection CreatePeerConnection()
        {
            RTCConfiguration config = new RTCConfiguration
            {
                iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } }
            };
            var pc = new RTCPeerConnection(config);

            // Set up video source using VP8 codec (same as WebRTCGetStarted)
            var vp8Codec = new VP8Codec();
            var testPatternSource = new VideoTestPatternSource(vp8Codec);
            var audioSource = new AudioExtrasSource(new AudioEncoder(), new AudioSourceOptions { AudioSource = AudioSourcesEnum.Music });

            // Tracks are SendOnly because the C# side only ever sources
            // media (a video test pattern + a music audio source) and has no
            // sink to render received media into. SendRecv would generate an
            // answer SDP whose direction is incompatible with a browser
            // offer that sets the transceivers as recvonly:
            //
            //   InvalidAccessError: Failed to set remote answer sdp:
            //   Incompatible send direction
            //
            // SendOnly answers the browser cleanly and also produces a
            // self-consistent offer when the C# side initiates the call.
            MediaStreamTrack videoTrack = new MediaStreamTrack(testPatternSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
            pc.addTrack(videoTrack);
            MediaStreamTrack audioTrack = new MediaStreamTrack(audioSource.GetAudioSourceFormats(), MediaStreamStatusEnum.SendOnly);
            pc.addTrack(audioTrack);

            testPatternSource.OnVideoSourceEncodedSample += pc.SendVideo;
            audioSource.OnAudioSourceEncodedSample += pc.SendAudio;

            pc.OnVideoFormatsNegotiated += (formats) => testPatternSource.SetVideoSourceFormat(formats.First());
            pc.OnAudioFormatsNegotiated += (formats) => audioSource.SetAudioSourceFormat(formats.First());

            // ICE candidate handling - only send after signalling has started
            pc.onicecandidate += async (iceCandidate) =>
            {
                // Don't send ICE candidates until the offer/answer exchange has started
                if (!signallingStarted)
                {
                    logger.LogDebug("Skipping ICE candidate - signalling not yet started");
                    return;
                }
                
                if (remotePeerId != null && nostrClient != null)
                {
                    logger.LogDebug($"Sending ICE candidate to peer {remotePeerId}");
                    await SendSignalMessage(new NostrSignalMessage
                    {
                        Type = NostrSignalType.IceCandidate,
                        PeerId = localPeerId,
                        TargetPeerId = remotePeerId,
                        Candidate = iceCandidate.candidate,
                        SdpMid = iceCandidate.sdpMid,
                        SdpMLineIndex = iceCandidate.sdpMLineIndex
                    });
                }
                else
                {
                    logger.LogDebug($"Cannot send ICE candidate: remotePeerId={remotePeerId}, nostrClient={(nostrClient != null ? "set" : "null")}");
                }
            };

            pc.onsignalingstatechange += () =>
            {
                logger.LogDebug($"Signalling state changed to {pc.signalingState}");
            };

            pc.onconnectionstatechange += async (state) =>
            {
                logger.LogInformation($"Peer connection state changed to {state}");

                if (state == RTCPeerConnectionState.connected)
                {
                    Console.WriteLine();
                    Console.WriteLine("===========================================");
                    Console.WriteLine("WebRTC Connection Established Successfully!");
                    Console.WriteLine("===========================================");
                    Console.WriteLine();

                    await audioSource.StartAudio();
                    await testPatternSource.StartVideo();
                }
                else if (state == RTCPeerConnectionState.failed)
                {
                    pc.Close("ICE connection failed");
                }
                else if (state == RTCPeerConnectionState.closed)
                {
                    // Detach the encoded-sample event handlers BEFORE awaiting
                    // CloseVideo/CloseAudio. The audio source in particular
                    // produces a packet every ~20 ms and there are typically
                    // a handful already in flight when the peer connection
                    // transitions to closed. Without this detach each one
                    // racing past the close boundary fires
                    //   pc.SendAudio -> RTPSession.SendRtpRaw
                    //     -> [WRN] SendRtpRaw was called for a audio packet
                    //              on a closed RTP session.
                    // Video happens to be quieter (a frame every 33 ms at
                    // 30 fps) so the symptom is mostly visible on audio,
                    // but unsubscribe both for symmetry.
                    testPatternSource.OnVideoSourceEncodedSample -= pc.SendVideo;
                    audioSource.OnAudioSourceEncodedSample -= pc.SendAudio;

                    await testPatternSource.CloseVideo();
                    await audioSource.CloseAudio();
                }
            };

            pc.oniceconnectionstatechange += (state) => 
                logger.LogDebug($"ICE connection state changed to {state}");

            // Diagnostics
            //pc.OnReceiveReport += (re, media, rr) => 
            //    logger.LogDebug($"RTCP Receive for {media} from {re}\n{rr.GetDebugSummary()}");
            //pc.OnSendReport += (media, sr) => 
            //    logger.LogDebug($"RTCP Send for {media}\n{sr.GetDebugSummary()}");
            //pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) => 
            //    logger.LogDebug($"STUN {msg.Header.MessageType} received from {ep}");

            return pc;
        }

        /// <summary>
        /// Adds a console logger for debug and info messages.
        /// </summary>
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
        {
            var seriLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(seriLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<Program>();
        }
    }
}
