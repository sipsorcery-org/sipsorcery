# WebRTC OpenAI Bridge Demo

## Overview

This is a sample ASP.NET Core application that acts as a bridge between a browser-based WebRTC peer and OpenAI's real-time WebRTC API. It enables you to leverage OpenAI's voice and transcription capabilities while retaining full control and extending functionality within your own server, such as integrating with local databases or custom business logic.

Key features:

- Proxy WebRTC signaling and media between the browser and OpenAI
- Bi-directional audio forwarding for real-time voice interactions
- Data channel messaging for custom instructions and events
- Optional STUN server configuration for ICE connectivity
- Session affinity and ICE gathering options

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download)
- An OpenAI API key with access to the real-time WebRTC endpoints
- (Optional) A STUN server URL for improved NAT traversal

## Configuration

Set the following environment variables before running the application:

| Variable                                     | Description                                                                                      |
|----------------------------------------------|--------------------------------------------------------------------------------------------------|
| `OPENAIKEY`                                  | **Required.** Your OpenAI API key for generating ephemeral session secrets.                      |
| `STUN_URL`                                   | **Optional.** A STUN server URL (e.g., `stun:stun.l.google.com:19302`) to help establish ICE.    |
| `WAIT_FOR_ICE_GATHERING_TO_SEND_OFFER`       | **Optional.** `true` or `false`. If `true`, the server will wait for ICE gathering to complete before sending the SDP offer to the client. Default: `false`.

Example (Windows PowerShell):

```powershell
$Env:OPENAIKEY = "sk-..."
$Env:STUN_URL = "stun:stun.l.google.com:19302"
$Env:WAIT_FOR_ICE_GATHERING_TO_SEND_OFFER = "true"
```

## Building and Running

1. Clone this repository:

```bash
git clone https://github.com/your-org/demo-webrtc-openai.git
cd demo-webrtc-openai
```

2. Build and run the application:

```bash
export OPENAIKEY="<your_openai_key>"
export STUN_URL="stun:stun.l.google.com:19302"
dotnet run
```

3. By default, the app will start on `http://localhost:5000` (port may vary depending on your ASP.NET Core configuration).

## Usage

1. Open your browser and navigate to `http://localhost:5000`. The demo static files include a simple HTML/JavaScript client that negotiates a WebRTC connection with this bridge.

2. The client will:
   - Create a WebRTC `RTCPeerConnection`
   - Exchange SDP offers/answers via the `/ws` WebSocket endpoint
   - Send audio and receive audio via SIPSorcery’s media APIs
   - Use the OpenAI data channel to send instructions (e.g., text prompts) and receive transcripts or spoken responses

3. All signaling messages pass through the ASP.NET Core WebSocket endpoint at `/ws`, which then interacts with OpenAI’s REST and WebRTC APIs internally.

## Architecture

- **Program.cs**: Hosts an ASP.NET Core minimal API that:
  - Serves static files (HTML/JS client)
  - Accepts WebSocket connections for signaling
  - Manages creation of SIPSorcery `RTCPeerConnection` instances for both browser and OpenAI
  - Orchestrates ephemeral key retrieval, SDP offers/answers, and data channel messaging

- **InitPcContext / CreatedPcContext**: Record types to carry context between steps
- **OpenAIRealtimeRestClient**: Helper for REST calls to create ephemeral sessions and exchange SDP
- **SIPSorcery**: Provides WebRTC primitives (`RTCPeerConnection`, media tracks, data channels)


## Extending the Demo

- Integrate your own database or backend logic within the `OnRTCPeerConnectionConnected` handler.
- Customize audio processing by adding local sources or sinks in SIPSorcery.
- Add custom data channel events to handle new instructions or streaming data.

## Troubleshooting

- **Missing API key**: Ensure `OPENAIKEY` is set; the server will fail to start otherwise.
- **ICE failures**: Provide a reliable STUN/TURN server via `STUN_URL`.
- **Health and logging**: This demo uses Serilog for console logging; adjust log levels in `Program.cs` as needed.

## References

- OpenAI Real-Time WebRTC Guide: https://platform.openai.com/docs/guides/realtime-webrtc
- SIPSorcery .NET WebRTC: https://github.com/sipsorcery/sipsorcery

