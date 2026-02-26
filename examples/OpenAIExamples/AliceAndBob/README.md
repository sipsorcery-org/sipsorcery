# AliceAndBob - OpenAI Realtime WebRTC Demo

**AliceAndBob** is a demonstration project that connects two [OpenAI Realtime API](https://platform.openai.com/docs/guides/realtime) WebRTC sessions—nicknamed *Alice* and *Bob*—and pipes their audio between one another. This simulates a live, bi-directional conversation between two AI agents. A Windows-based OpenGL audio visualisation displays which side is currently speaking.

---

## 🎯 Features

- Initiates and maintains two OpenAI WebRTC sessions.
- Forwards audio from Alice to Bob and vice versa.
- Uses [SIPSorcery](https://github.com/sipsorcery/sipsorcery) WebRTC libraries and [NAudio](https://github.com/naudio/NAudio) for media handling.
- Visualises audio signal strength in real-time using a WinForms OpenGL scope.
- Shows how to work with:
  - WebRTC connections
  - OpenAI Realtime API
  - Audio frame decoding and manipulation
  - Session updates and message prompting via data channels

---

## 🛠 Requirements

- **Operating System**: Windows
- **.NET Version**: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Audio**: A working input/output device (e.g., microphone, speakers)
- **API Access**: OpenAI API key with access to Realtime features

---

## 🚀 Getting Started

### 1. Clone the Repository

```bash
git clone https://github.com/sipsorcery-org/SIPSorcery.OpenAI.WebRTC.git
cd SIPSorcery.OpenAI.WebRTC/examples/AliceAndBob
```

### 2. Set Your OpenAI API Key

Set the `OPENAI_API_KEY` environment variable in your terminal:

```bash
set OPENAI_API_KEY="<your-openai-api-key>"
```

### 3. Run the Application

```bash
dotnet run
```

You should see a WinForms window showing two audio scopes—one for Alice and one for Bob—while the agents begin a real-time audio exchange.

---

## 📷 Application Preview

The `Program.cs` does the following:

- Creates a WinForms UI thread for audio scope visualization.
- Starts two OpenAI WebRTC sessions (`Alice` and `Bob`).
- Establishes WebRTC peer connections.
- Routes audio received from Alice to Bob, and vice versa.
- Uses OpenAI’s `DataChannelMessenger` to:
  - Send a `response.create` event from Alice saying “Hi!”
  - Change Bob’s voice using a `session.update` event.
- Decodes and visualizes incoming audio frames.

---

## 📦 Key Technologies

| Component          | Role                                            |
|-------------------|--------------------------------------------------|
| `SIPSorcery`       | WebRTC signaling, media transport, SDP         |
| `NAudio`           | Windows audio capture/playback (used internally) |
| `OpenAI Realtime`  | API endpoints for streaming AI audio responses |
| `WinForms`         | UI for displaying real-time audio signal       |
| `OpenGL`           | Audio waveform visualization                   |
| `Serilog`          | Structured logging to console                  |

---

## 🧩 Files of Interest

| File                  | Description                                      |
|-----------------------|--------------------------------------------------|
| `Program.cs`          | Main entry point. Starts sessions and handles logic. |
| `FormAudioScope.cs`   | Audio visualisation using WinForms and OpenGL.  |
| `WebRTCEndPoint.cs`   | WebRTC peer connection wrapper for OpenAI.      |

---

## 📝 License

This project is licensed under the **BSD 3-Clause License** with an additional **BY-NC-SA** restriction. See [`LICENSE.md`](./LICENSE.md) for full terms.
