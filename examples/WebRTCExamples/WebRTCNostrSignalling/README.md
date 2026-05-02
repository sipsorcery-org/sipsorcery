# WebRTC Nostr Signalling

A prototype WebRTC application that uses the [Nostr](https://nostr.com/) protocol and the [NNostr](https://github.com/Kukks/NNostr) library for signalling between peers. This example demonstrates how to use a decentralized relay network for WebRTC offer/answer and ICE candidate exchange.

## Overview

Instead of using a traditional WebSocket server for WebRTC signalling, this application uses the Nostr protocol to relay signalling messages between peers. This provides several benefits:

- **Decentralized**: No need for a centralized signalling server
- **Privacy**: Messages can be encrypted with recipient's public key
- **Resilience**: Multiple relays can be used for redundancy

## How It Works

1. Each peer connects to a Nostr relay (default: `wss://nostr.net`)
2. Each peer generates a unique Peer ID for the session
3. Peers exchange the Peer IDs out-of-band (e.g., chat, email)
4. The offerer creates an SDP offer and publishes it to the relay as a Nostr event
5. The answerer receives the offer, creates an answer, and publishes it back
6. ICE candidates are exchanged via Nostr events
7. Once signalling is complete, WebRTC media flows directly peer-to-peer

## Message Format

Signalling messages use a custom Nostr event kind (`24133`) with JSON content:

```json
{
  "type": "Offer|Answer|IceCandidate",
  "peerId": "sender_peer_id",
  "targetPeerId": "recipient_peer_id",
  "sdp": "...",           // For Offer/Answer
  "candidate": "...",     // For IceCandidate
  "sdpMid": "...",
  "sdpMLineIndex": 0
}
```

## Running the Example

### Console Application (Offerer)

```bash
cd examples/WebRTCExamples/WebRTCNostrSignalling
dotnet run
```

1. Note your Peer ID displayed on startup
2. Choose 'y' when asked if you're the offerer
3. Share your Peer ID with the other peer
4. Wait for them to be ready, then enter their Peer ID

### Console Application (Answerer)

```bash
cd examples/WebRTCExamples/WebRTCNostrSignalling
dotnet run
```

1. Note your Peer ID displayed on startup
2. Choose 'n' when asked if you're the offerer
3. Enter the offerer's Peer ID
4. Wait for the offer to arrive

### Browser Peer

Open `nostr-webrtc.html` in a browser to use a browser-based peer:

1. Click "Connect to Relay" to connect to the Nostr relay
2. Enter the remote Peer ID
3. Click "Create Offer" to initiate a call, or wait for an incoming offer

## Dependencies

- [NNostr.Client](https://www.nuget.org/packages/NNostr.Client/) - Nostr client library
- [SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) - WebRTC implementation
- [SIPSorcery.VP8](https://github.com/sipsorcery-org/sipsorcery) - VP8 video codec

## Notes

- This is a prototype/demonstration application
- In production, use proper Nostr key management instead of randomly generated keys
- The browser HTML file uses a simplified signing approach that may not work with all relays
- Consider encrypting signalling messages for privacy (NIP-04 or NIP-44)

## Related NIPs

While there's no official NIP specifically for WebRTC signalling, this example follows community conventions for signalling over Nostr. Consider reviewing:

- [NIP-46](https://github.com/nostr-protocol/nips/blob/master/46.md) - Nostr Connect (for signing patterns)
- [NIP-04](https://github.com/nostr-protocol/nips/blob/master/04.md) - Encrypted Direct Messages
- [NIP-44](https://github.com/nostr-protocol/nips/blob/master/44.md) - Versioned Encryption
