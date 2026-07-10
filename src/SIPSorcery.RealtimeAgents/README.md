# SIPSorcery.RealtimeAgents

**Status: design draft.** Interfaces only — the seams for building realtime voice/avatar
agents on SIPSorcery transports, distilled from the `WebRTCMaxHeadroom` example and the
`WebRTCGodotAvatar` prototype. The orchestration (`AgentSession`) and transport binding
come next; nothing here is stable yet.

## The pipeline these seams compose into

```
 caller audio ──▶ ISpeechRecognizer ──▶ IReplyGenerator ──▶ ISpeechSynthesizer ─┬─▶ audio track
 (WebRTC/SIP)         │ OnUtterance         (history-aware,      (text→PCM)     │
                      │ OnSpeechStarted ────── cancels ──────── everything ─────┤
                      ▼      (barge-in)                                         ▼
                 transcript                                               IAvatarMouth
                                                                        (PCM drives face)
```

Everything between the seams — playback pacing, sentence streaming, mouth-driving,
barge-in cancellation, the transcript, transport wiring — is the pipeline's job and will
live in this package exactly once. The examples currently each carry their own copy of
that logic, and the copies have already drifted.

## The seams

| Interface | Contract | Proven by |
|---|---|---|
| `IAvatarMouth` | Speech PCM in, face animation out. The one seam every avatar host implements. | Both examples |
| `IAvatarRenderer` | `IAvatarMouth` + `IVideoSource`: an in-process avatar whose video track the pipeline owns. | MaxHeadroom (cartoon, Wav2Lip) |
| `ISpeechSynthesizer` (+ `IStreamingSpeechSynthesizer`) | Text in, `AudioSegment` stream out. Engine only — no playback/lip-sync coupling. | sherpa-onnx, ElevenLabs batch + websocket |
| `ISpeechRecognizer` | Caller PCM in; utterance, partial and speech-onset (barge-in) events out. | sherpa-onnx, ElevenLabs batch + realtime |
| `IReplyGenerator` | Conversation history in, streamed reply out. Mirrors M.E.AI `IChatClient` so an adapter is trivial. | LLamaSharp, OpenAI-compatible HTTP |

## Design decisions baked into this cut

- **Audio-first, avatar-optional.** An agent with no `IAvatarMouth` is a phone-call voice
  agent — attachable to a plain SIP call as well as WebRTC. That's the differentiator.
- **The mouth is not the video source.** Engine-hosted avatars (Godot, Unity) own their own
  capture→encode→send path; only `IAvatarMouth` crosses into them. In-process renderers
  implement the composed `IAvatarRenderer`. (Learned from the Godot prototype, where the
  renderer seam had to be split.)
- **Cancellation on every async surface.** Barge-in — cancelling generation, synthesis,
  playback and the face the instant the caller speaks — is near-impossible to retrofit,
  so the contracts carry it from day one (`AbortSpeech`, `OnSpeechStarted`, tokens
  throughout).
- **History-aware replies.** `IReplyGenerator` takes the transcript, not a lone prompt;
  the persona is the `System` turn. The examples' stateless `ILlmClient` forgot every
  previous exchange.
- **No engines in this package.** sherpa-onnx, LLamaSharp, ElevenLabs et al. drag heavy or
  native dependencies and belong in companion packages (e.g.
  `SIPSorcery.RealtimeAgents.Local`) or application code. This package stays contracts +
  orchestration with no dependency beyond logging abstractions.

## Not in this cut (deliberately)

- **`AgentSession` / transport binding** (`Attach(RTCPeerConnection)` / `Attach(SIP)`) —
  the next step once the seams settle.
- **A turn-taking/VAD seam.** `OnSpeechStarted` covers barge-in; a fuller endpointing
  policy (pause lengths, interruption thresholds) can be layered later without breaking
  these contracts.
- **Speech-to-speech models** (OpenAI Realtime et al.). They replace the middle three
  seams with a single audio-in/audio-out engine; `IAvatarMouth` and the transport binding
  are unaffected. Needs its own seam once the pipeline exists.
- **Tool/function calling, transcript/caption events** — v2 territory.
