# WebRTC Nostr Signalling

A demonstration of running the WebRTC offer/answer/ICE-candidate exchange over
the [Nostr](https://nostr.com/) protocol — using a public Nostr relay as a
federated signalling channel instead of a bespoke WebSocket server.

The example ships two peers that talk to each other through any public
relay:

- A C# console peer (`Program.cs`) using [NNostr.Client](https://github.com/Kukks/NNostr)
- A browser peer (`nostr-webrtc.html`) using [nostr-tools](https://github.com/nbd-wtf/nostr-tools)

Once signalling completes the WebRTC media flows directly peer-to-peer; the
relay only ever sees signalling.

## Why this is interesting

Almost every WebRTC tutorial uses a custom WebSocket service to carry the
offer / answer / ICE candidates. That service is a hard dependency — if it
goes down, calls can't be set up; whoever runs it sees every signalling
payload; and it's another thing for the developer to host.

Nostr can do that job without any custom infrastructure. The pattern is:

| Layer                    | Carried by                             |
| ------------------------ | -------------------------------------- |
| Identity                 | secp256k1 keypair (Nostr pubkey)       |
| Per-peer addressing      | `p` tag on the Nostr event             |
| Discovery                | Relay subscription with `#p` filter    |
| Confidentiality          | NIP-44 v2 encryption of `event.content`|
| Wire transport           | Any public Nostr relay (default `wss://nos.lol`) |
| Media                    | Direct WebRTC peer-to-peer (RTP / SRTP) |

The relay sees encrypted ciphertext plus the routing `p` tag (= the
recipient's pubkey, deliberately cleartext so the relay can filter and
forward). Everything substantive — SDP, ICE candidates — is opaque to it.

## How the signalling flow works

```
                                ┌────────────────────────────┐
                                │   Public Nostr relay       │
                                │   (wss://nos.lol default)  │
                                └──────────┬─────────────────┘
                                           │
            kind=25555                     │            kind=25555
            p=<C# pubkey>                  │            p=<browser pubkey>
            content = NIP-44(SDP/ICE)      │            content = NIP-44(SDP/ICE)
       ┌───────────────────────────────────┼───────────────────────────────────┐
       │                                   │                                   │
       ▼                                   ▼                                   ▼
┌─────────────┐                      Subscription with               ┌─────────────┐
│  Browser    │ ◀───── kind 25555 ◀─── "#p":[my pubkey] ────▶ 25555 ─────▶│  C# app  │
│  peer       │                                                       │  (console)  │
└─────────────┘                                                       └─────────────┘
       │                                                                     ▲
       └──────────────── direct WebRTC media (RTP / SRTP) ────────────────────┘
                          (no relay involvement after ICE pair)
```

1. Both peers connect to the same Nostr relay over WebSocket
2. Each subscribes with a `#p` filter for events tagged with its own pubkey
3. The browser peer creates the WebRTC offer and publishes it as a Nostr
   event of kind `25555` with a `p` tag pointing at the C# peer
4. The relay sees the `p` tag, forwards to the matching subscription
5. The C# peer decrypts, generates an answer, publishes it back the same
   way (tagged for the browser pubkey)
6. ICE candidates trickle the same way as additional kind-25555 events
7. Once the WebRTC peer connection completes its ICE pair, media flows
   directly peer-to-peer — the relay is no longer involved

## Wire format

Each signalling event is shaped as:

```json
{
  "kind": 25555,
  "tags": [["p", "<recipient pubkey hex>"]],
  "content": "<NIP-44 v2 base64 ciphertext>",
  "pubkey": "<sender pubkey hex>",
  "created_at": 1700000000,
  "id": "<event id>",
  "sig": "<BIP-340 schnorr signature>"
}
```

`event.content` decrypts (via NIP-44 v2 with the conversation key derived
from sender's secret + recipient's pubkey) to a small JSON payload:

```json
{
  "Type": 0,                           // 0=Offer, 1=Answer, 2=IceCandidate
  "PeerId": "64f89d15",                // first 8 hex chars of sender pubkey
  "TargetPeerId": "7bce0c79",          // first 8 hex chars of recipient pubkey
  "Sdp": "v=0\r\n...",                 // present on Offer / Answer
  "Candidate": "candidate:1 ...",      // present on IceCandidate
  "SdpMid": "0",
  "SdpMLineIndex": 0
}
```

PascalCase keys + integer `Type` discriminator make the wire format what
`System.Text.Json` defaults to on the C# side; the browser uses the same
casing explicitly so the two sides round-trip bit-exact.

### Why a custom event kind

`25555` is in the ephemeral kind range (20000–29999) — relays are not
expected to persist these events, which is the right semantics for
signalling. The number was chosen deliberately to avoid colliding with
[NIP-46 NostrConnect (kind 24133)](https://github.com/nostr-protocol/nips/blob/master/46.md);
there is no standard NIP for WebRTC signalling so the example claims a
free number.

## Demo keys

Both peers ship with **hard-coded keypairs** so the demo runs end-to-end
with no per-run copy/paste:

| Peer    | Private key (hex)                                                  | Pubkey (hex)                                                       |
| ------- | ------------------------------------------------------------------ | ------------------------------------------------------------------ |
| C# app  | `3856fd69b3ce60f003e01449bbae71f5a3fdfd8b4ed483910ab6fd16e2dcf8d0` | `64f89d1551ec0d0f541575a8d39db408d18b19208d3f2951972dc3d738c79c96` |
| Browser | `d459231a949d367b79b45b3f5c1670c2aaa30e696b9f4337fa8e9b7900b8e7e3` | `7bce0c795c5a9465b6c4c62517dab36a194fab389b8fba0efe09269ba18c5710` |

The browser HTML pre-fills the C# app's pubkey in the "Remote Peer ID"
input, and the C# app has the browser's pubkey pinned in `Program.cs`, so
neither side needs to be told who the other is at runtime.

> ⚠ **DEMO ONLY.** Both private keys are visible in source / page view.
> Do not reuse them for anything that matters. To pin a different pair,
> change `BROWSER_PRIVATE_KEY_HEX` + the `value=` on `#remotePeerId` in
> `nostr-webrtc.html`, and `localPrivateKey` + `remotePubKeyHex` in
> `Program.cs`. Both sides have to be updated in lockstep — a mismatch
> manifests as "NIP-44 decrypt failed" log lines on the receiver.

## Running the demo

### 1. Start the C# console peer

```bash
cd examples/WebRTCExamples/WebRTCNostrSignalling
dotnet run
```

The console prints the local pubkey and connects to `wss://nos.lol`. It
then subscribes for events tagged with its pubkey and waits.

### 2. Open the browser peer

Open `nostr-webrtc.html` in any modern browser. The page:

- Loads the hard-coded browser keypair
- Pre-fills the Remote Peer ID input with the C# app's pubkey

Click **Connect to Relay**, then **Create Offer (Start Call)**. The two
peers exchange signalling over the relay, ICE pairs, and media starts
flowing directly.

## Confidentiality model

- Anyone watching `wss://nos.lol` (or whichever relay is used) sees:
  - Both peers' Nostr pubkeys (sender pubkey + the `p` tag)
  - Event kind, timestamp, length of the ciphertext
  - Frequency / pattern of signalling events
- They do **not** see:
  - The SDP, ICE candidates, or any other payload contents
  - The actual media — that's direct peer-to-peer over RTP/SRTP, never
    touches the relay

To strengthen this further you would:

- Rotate keypairs per session (defeats long-term pubkey tracking)
- Publish to multiple relays simultaneously (defeats a single-relay
  observer)
- Use a `kind:1059` gift-wrap envelope (NIP-59) to hide the sender's
  pubkey from the relay entirely

None of those are done here — the demo is deliberately minimal.

## Dependencies

- [NNostr.Client](https://www.nuget.org/packages/NNostr.Client/) — Nostr
  client library; the `Protocols.NIP44` namespace provides the NIP-44 v2
  helpers used on the C# side
- [SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) — WebRTC
  implementation
- [SIPSorcery.VP8](https://www.nuget.org/packages/SIPSorcery.VP8/) — VP8
  video codec
- [nostr-tools 2.7.2](https://github.com/nbd-wtf/nostr-tools) — browser-side
  BIP-340 Schnorr signing and NIP-44 v2 encryption, loaded from
  `esm.sh`

## Related NIPs

- [NIP-01](https://github.com/nostr-protocol/nips/blob/master/01.md) — base
  protocol (event shape, relay messages, `p`-tag routing, `#p`
  subscription filter)
- [NIP-44](https://github.com/nostr-protocol/nips/blob/master/44.md) —
  versioned encryption (v2 is what this demo uses)
- [NIP-46](https://github.com/nostr-protocol/nips/blob/master/46.md) — Nostr
  Connect (kind 24133, deliberately avoided so signalling and remote
  signing don't collide)
- [NIP-59](https://github.com/nostr-protocol/nips/blob/master/59.md) —
  gift-wrap (sender-pubkey hiding; not used here but a natural next
  hardening step)
