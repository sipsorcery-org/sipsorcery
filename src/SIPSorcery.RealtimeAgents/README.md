# SIPSorcery.RealtimeAgents

**Status: design draft.** Interfaces only — the seams and the host-facing surface for
realtime voice (and optionally avatar) agents on SIPSorcery transports, distilled from the
`WebRTCMaxHeadroom` example and the `WebRTCGodotAvatar` prototype, and checked against a
third consumer, an AI receptionist in the softphone. `AgentSession`, the implementation, is
next; nothing here is stable yet.

It's an **agent** library, not an avatar library: a face is one optional output. A
receptionist on a SIP call is an agent with no face, and needs no avatar code.

## The shape

```
                        ┌──────────────────────── IAgentSession ─────────────────────────┐
                        │  state · transcript · tool activity  ◀── host UI               │
                        │  Say · Interrupt · Pause/Resume · Detach  ◀── host controls     │
                        │                                                                │
 IAgentMediaPort        │  cascade:  ISpeechRecognizer ─▶ IChatClient ─▶ ISpeechSynthesizer│
 (a call's media) ─────▶│             (or)  ISpeechToSpeechEngine  (one realtime model)   │──▶ speech ─▶ port
  audio · DTMF · text   │                                                                │
  · video track         │  tool calls ─▶ IAgentToolPolicy ─▶ the host's AIFunctions      │──▶ IAvatarMouth
                        └────────────────────────────────────────────────────────────────┘     (face, optional)
```

Everything inside the box — playout pacing, sentence streaming, resampling, mouth-driving
and its lip-sync lead, barge-in, the transcript, tool gating — is the session's job and
lives in this package exactly once. The examples each carry their own copy today, and the
copies have drifted.

## The seams

| Interface | Contract | Proven by |
|---|---|---|
| `IAgentMediaPort` | One call's media: decoded caller audio in, agent speech out; optional DTMF, text, video track. Adapters bind it to a transport. | (new) MaxHeadroom's peer connection; the softphone's relay |
| `IAgentSession` (+ `AgentSessionOptions`) | The host's view: state, transcript, tool activity; Say, Interrupt, Pause/Resume, Detach. | (new) the receptionist's needs |
| `ISpeechRecognizer` | Caller PCM in; utterance, partial and speech-onset (barge-in) events out. | sherpa-onnx, ElevenLabs batch + realtime |
| `IChatClient` (Microsoft.Extensions.AI) | The model, for the cascade: history in, streamed reply out, tools. | LLamaSharp, any OpenAI-compatible endpoint |
| `ISpeechSynthesizer` (+ `IStreamingSpeechSynthesizer`) | Text in, `AudioSegment` stream out. Engine only. | sherpa-onnx, ElevenLabs batch + websocket |
| `ISpeechToSpeechEngine` | A realtime model: caller audio in, agent audio, transcripts and tool calls out. Replaces the cascade. | (new) engine packages |
| `IAgentToolPolicy` | Reviews every tool call before it runs; a refusal goes back to the model with its reason. | (new) the receptionist's transfer limits |
| `IAvatarMouth` | Speech PCM in, face animation out. The one seam every avatar host implements. | Both examples |
| `IAvatarRenderer` | `IAvatarMouth` + `IVideoSource`: an in-process avatar whose video the session puts on the port. | MaxHeadroom (cartoon, Wav2Lip) |
| `IWarmable` | Optional on any engine: pay first-use costs before the first call. | MaxHeadroom's warm-up |

## Design decisions

- **Audio first, avatar optional.** No `IAvatarMouth` means a voice agent, attachable to a
  plain SIP call as well as WebRTC. That's the differentiator against LiveKit Agents and
  Pipecat: .NET, and SIP without a media server.
- **One media port, not one attach method per transport.** The session never sees an
  `RTCPeerConnection` or a SIP call, only `IAgentMediaPort`. Adapters cover a peer
  connection, a SIP RTP session, an app's own media relay (the softphone attaches the
  agent to the relay it already has, so a person can take the call over), and local
  devices or WAV files for tests. The port decodes whatever the call negotiated and says
  the rate it wants back; the session resamples (8 kHz G.711, 48 kHz Opus, 16 kHz models).
- **Two pipeline shapes, one session.** The cascade (recogniser, chat client, synthesiser:
  the local Max stack) and a speech-to-speech engine (lower latency, the likely choice for
  a receptionist) sit behind the same `IAgentSession`. The mouth gets PCM either way.
- **Tools in v1, gated.** A receptionist is mostly its tools. The tools are the host's, as
  Microsoft.Extensions.AI `AIFunction`s; the library guarantees every call passes the
  host's `IAgentToolPolicy`. That's the defence against a caller saying "transfer me to…":
  the host limits targets, refuses outbound calls, or asks the user first. The same
  `AIFunction`s can later be exposed over MCP.
- **Microsoft.Extensions.AI, not our own chat seam.** The draft's `IReplyGenerator`
  mirrored `IChatClient`; it now *is* `IChatClient`. The abstractions package is small and
  Microsoft-maintained, and brings every model provider (OpenAI, Azure, Ollama, ...) and
  the tool types for free. So the dependencies are now logging plus
  `Microsoft.Extensions.AI.Abstractions`.
- **The host stays in charge of the call.** `Pause` for hold, `Say` for the host's own
  lines, `Interrupt`, and `Detach`: the agent leaves and the call carries on, which is how
  a person takes over. `OnFailed` detaches too, so an engine dying hands the call back
  rather than dropping it.
- **The mouth is not the video source.** Engine-hosted avatars (Godot, Unity) own their own
  capture, encode and send; only `IAvatarMouth` crosses into them. In-process renderers
  implement `IAvatarRenderer`, and the session puts their video on the port.
- **Cancellation on every async surface.** Barge-in — stopping generation, synthesis,
  playout and the face the instant the caller speaks — can't be retrofitted.
- **Engines loaded once, sessions per call.** Chat clients and synthesisers are stateless
  and shared across sessions; recognisers, speech-to-speech engines and avatars are per
  call. A softphone with three calls needs that.
- **No engines and no personas in this package.** sherpa-onnx, LLamaSharp, ElevenLabs and
  speech-to-speech providers bring heavy or native dependencies and ship as companion
  packages. A receptionist — its persona, rules, tools and AI-disclosure greeting — is
  application code.

## Packages

| Package | Holds |
|---|---|
| `SIPSorcery.RealtimeAgents` | These contracts, `AgentSession`, and media-port adapters for SIPSorcery types. |
| `SIPSorcery.RealtimeAgents.Local` | sherpa-onnx speech in and out, LLamaSharp (native dependencies). |
| Cloud engine packages | ElevenLabs; a speech-to-speech provider. |
| Avatars | The cartoon and Wav2Lip, as packages or kept as examples. Check Wav2Lip's licence before packaging: its original weights are, as far as we know, for non-commercial use. |
| Unity, Godot | Implement `IAvatarMouth` in their own repos. |

Native engines crash in ways managed code doesn't (the DirectML crash in Max would have
taken a host app down). The port is kept small and plain so engines could later run in a
separate process without changing the session's surface.

## How we'll know it's general

Three consumers, no special cases between them:

1. **WebRTCMaxHeadroom**: WebRTC, an in-process avatar, the local cascade. Ported first,
   since it's a working baseline and any regression shows at once.
2. **The softphone receptionist**: SIP, audio only, tools behind a policy, Take over
   (`Detach`), hold (`Pause`), probably a cloud speech-to-speech engine.
3. **WebRTCGodotAvatar**: an engine-hosted avatar, the mouth only.

## Not in this cut

- **`AgentSession`** itself, and the media-port adapters.
- **A turn-taking policy** beyond barge-in (pause lengths, interruption thresholds); it can
  be layered on `OnSpeechStarted` without breaking these contracts.
- **DTMF to the model.** The port carries DTMF and the host can act on it; whether the
  session also tells the model about key presses is open.
- **An MCP surface** over a session's tools.
