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
        
        // Use a custom Nostr event kind for WebRTC signalling
        // This is not an official NIP, but follows community conventions
        private const int WEBRTC_SIGNAL_KIND = 24133;

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;
        private static NostrClient? nostrClient;
        private static RTCPeerConnection? peerConnection;
        private static string? localPeerId;
        private static string? remotePeerId;
        private static bool isOfferer = false;
        private static bool signallingStarted = false; // Track if offer/answer exchange has started

        static async Task Main(string[] args)
        {
            Console.WriteLine("WebRTC Nostr Signalling Prototype");
            Console.WriteLine("==================================");
            Console.WriteLine();

            logger = AddConsoleLogger();

            // Generate a unique peer ID for this session
            localPeerId = Guid.NewGuid().ToString("N")[..8];
            Console.WriteLine($"Your Peer ID: {localPeerId}");
            Console.WriteLine();

            // Determine role
            Console.Write("Are you the offerer? (y/n): ");
            var roleInput = Console.ReadLine()?.Trim().ToLower();
            isOfferer = roleInput == "y" || roleInput == "yes";

            if (!isOfferer)
            {
                Console.Write("Enter the Peer ID of the offerer to connect to: ");
                remotePeerId = Console.ReadLine()?.Trim();
                
                if (string.IsNullOrEmpty(remotePeerId))
                {
                    Console.WriteLine("Error: Remote Peer ID is required for answerer role.");
                    return;
                }
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
                
                Console.Write("Enter the Peer ID of the peer you want to connect to: ");
                remotePeerId = Console.ReadLine()?.Trim();
                
                if (string.IsNullOrEmpty(remotePeerId))
                {
                    Console.WriteLine("Error: Remote Peer ID is required.");
                    peerConnection.Close("No remote peer specified");
                    return;
                }

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
            
            // Monitor raw messages for debugging
            nostrClient.MessageReceived += (sender, message) =>
            {
                // Truncate long messages for logging
                var truncatedMessage = message.Length > 200 ? message[..200] + "..." : message;
                logger.LogDebug($"Nostr raw message received: {truncatedMessage}");
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

            // Subscribe to WebRTC signalling events
            // We're interested in events tagged with our peer ID
            var filter = new NostrSubscriptionFilter
            {
                Kinds = new[] { WEBRTC_SIGNAL_KIND }
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

        private static void OnNostrEventsReceived(object? sender, (string subscriptionId, NostrEvent[] events) args)
        {
            logger.LogDebug($"Nostr events received: subscription={args.subscriptionId}, count={args.events.Length}");
            
            if (args.subscriptionId != "webrtc-signal") return;

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
                    
                    var message = JsonSerializer.Deserialize<NostrSignalMessage>(nostrEvent.Content);
                    
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
                catch (Exception ex)
                {
                    logger.LogWarning($"Failed to parse Nostr event: {ex.Message}");
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

                        var result = peerConnection.setRemoteDescription(offerSdp);
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
                        logger.LogInformation($"Received answer from peer {message.PeerId}");
                        
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
                    }
                    break;

                case NostrSignalType.IceCandidate:
                    if (peerConnection != null && !string.IsNullOrEmpty(message.Candidate))
                    {
                        logger.LogDebug($"Received ICE candidate from peer {message.PeerId}");
                        
                        var iceCandidate = new RTCIceCandidateInit
                        {
                            candidate = message.Candidate,
                            sdpMid = message.SdpMid,
                            sdpMLineIndex = message.SdpMLineIndex ?? 0
                        };

                        peerConnection.addIceCandidate(iceCandidate);
                    }
                    break;
            }
        }

        private static async Task CreateAndSendOffer()
        {
            if (peerConnection == null) return;

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

            logger.LogDebug($"Sending {message.Type} to peer {message.TargetPeerId}...");

            var content = JsonSerializer.Serialize(message);

            // Create a new Nostr event
            // Note: In production, you would sign this with a proper private key
            var nostrEvent = new NostrEvent
            {
                Kind = WEBRTC_SIGNAL_KIND,
                Content = content,
                CreatedAt = DateTimeOffset.UtcNow
            };

            // Generate a random private key for signing (in production, use persistent keys)
            var privateKeyBytes = new byte[32];
            System.Security.Cryptography.RandomNumberGenerator.Fill(privateKeyBytes);
            var privateKey = ECPrivKey.Create(privateKeyBytes);
            
            await nostrEvent.ComputeIdAndSignAsync(privateKey);

            logger.LogDebug($"Nostr event created: id={nostrEvent.Id?[..8]}..., kind={nostrEvent.Kind}");

            try
            {
                await nostrClient.SendEventsAndWaitUntilReceived(new[] { nostrEvent }, CancellationToken.None);
                logger.LogDebug($"Nostr event sent and acknowledged");
            }
            catch (Exception ex)
            {
                logger.LogError($"Failed to send Nostr event: {ex.Message}");
                throw;
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

            MediaStreamTrack videoTrack = new MediaStreamTrack(testPatternSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendRecv);
            pc.addTrack(videoTrack);
            MediaStreamTrack audioTrack = new MediaStreamTrack(audioSource.GetAudioSourceFormats(), MediaStreamStatusEnum.SendRecv);
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
                    await testPatternSource.CloseVideo();
                    await audioSource.CloseAudio();
                }
            };

            pc.oniceconnectionstatechange += (state) => 
                logger.LogDebug($"ICE connection state changed to {state}");

            // Diagnostics
            pc.OnReceiveReport += (re, media, rr) => 
                logger.LogDebug($"RTCP Receive for {media} from {re}\n{rr.GetDebugSummary()}");
            pc.OnSendReport += (media, sr) => 
                logger.LogDebug($"RTCP Send for {media}\n{sr.GetDebugSummary()}");
            pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) => 
                logger.LogDebug($"STUN {msg.Header.MessageType} received from {ep}");

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
