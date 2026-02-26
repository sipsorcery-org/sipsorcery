# OpenAI Realtime WebRTC Get Started Example

This is a minimal WebRTC application demonstrating interaction with OpenAI's [Realtime API](https://platform.openai.com/docs/guides/realtime-webrtc). It sets up a peer connection and streams audio from a Windows audio device. Once connected, it sends a message to start the conversation and prints transcription results from both the user and the assistant.

> ⚠️ **Note**: As of 10 May 2025, this example successfully establishes an audio stream and receives data channel messages. However, echo cancellation is not implemented—use a headset or ensure your audio device supports echo cancellation to avoid feedback loops.

## Features

- Establishes a WebRTC connection with OpenAI's realtime endpoint
- Streams audio directly from Windows devices
- Sends a response prompt to trigger conversation
- Handles and logs transcription deltas and completions for both user and assistant

## Requirements

- Windows OS with audio devices
- [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- OpenAI API key with access to the Realtime API

## Getting Started

1. **Set your OpenAI API key as an environment variable**:

```bash
set OPENAI_API_KEY=your_openai_key
```

2. **Run the application**:

```bash
dotnet run
```

3. **Interact**:

Speak into your microphone and observe the transcription logs for both your voice and the assistant’s responses.

## File Overview

### Program.cs

Contains the core application logic:
- Initializes the OpenAI WebRTC endpoint
- Connects audio from the default Windows input device
- Sends session updates and creates a response to initiate conversation
- Logs transcription updates and completions

## Notes

- Echo cancellation is not handled in this demo. If you're using speakers, OpenAI may end up responding to itself. Use a headset for clean operation.
- Transcription is enabled using the `Whisper1` model.
- This demo is part of the `SIPSorcery.OpenAI.WebRTC` library.

## License

BSD 3-Clause "New" or "Revised" License and the additional BDS BY-NC-SA restriction. See `LICENSE.md` for full details.