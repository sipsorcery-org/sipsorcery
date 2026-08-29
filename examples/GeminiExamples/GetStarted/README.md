# Gemini Live Get Started Example

Minimal console application that connects your microphone and speakers to Google's
[Gemini Live API](https://ai.google.dev/gemini-api/docs/live-api).

## Usage

```bash
set GEMINI_API_KEY=your_gemini_api_key
dotnet run
```

(`GOOGLE_API_KEY` is also accepted if you already have that set.)

Speak into your microphone once you see "Gemini Live session established." in the log — the
model will respond with spoken audio (and, if a headset isn't used, you may hear it echo back
without another mic, since this demo has no echo cancellation).

## What it does

- Captures 16 kHz/16-bit/mono PCM from the default Windows recording device with NAudio and
  streams it to Gemini via `GeminiLiveEndPoint.SendAudio`.
- Plays 24 kHz/16-bit/mono PCM audio received from Gemini back through the default Windows
  playback device.
- Enables input/output transcription and logs it (`ME`/`AI` lines).
- Clears the playback buffer on barge-in (`OnInterrupted`), so the model doesn't keep talking
  over you.

See [src/SIPSorcery.Gemini.Realtime/README.md](../../../src/SIPSorcery.Gemini.Realtime/README.md)
for more on the underlying library.
