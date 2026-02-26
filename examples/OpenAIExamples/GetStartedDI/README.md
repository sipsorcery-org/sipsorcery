# OpenAI WebRTC Get Started Example using Dependency Injection

This example demonstrates how to set up and run a basic WebRTC application that interacts with OpenAI's [Real-time WebRTC API](https://platform.openai.com/docs/guides/realtime-webrtc). This version is demonstrates using a Dependency Injection (DI) environment, using `HttpClientFactory` and DI-compliant service wiring.

> ⚠️ **Note:** This demo does not include echo cancellation. If your Windows audio device doesn't provide echo cancellation, ChatGPT may end up talking to itself. To avoid this, use a headset.

## Features

- Initializes WebRTC connections to OpenAI’s Realtime endpoint.
- Sends and receives audio using Windows audio devices.
- Uses `Microsoft.Extensions.DependencyInjection` for DI and `Serilog` for logging.
- Sends a "Say Hi!" message to trigger conversation once connection is established.
- Shows how to listen for final transcript messages from the assistant.

## Usage

Set your OpenAI API key in the environment and run:

```bash
set OPENAI_API_KEY=<your_openai_key>
dotnet run
```

### Requirements

- Windows machine with audio devices
- [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download)
- Valid [OpenAI API Key](https://platform.openai.com/account/api-keys)

## Program Structure

- Uses `IServiceCollection` to register dependencies.
- Adds `AddOpenAIRealtimeWebRTC(openAiKey)` to DI container.
- Resolves `IWebRTCEndPoint` from `IServiceProvider`.
- Sends audio from local device and handles real-time responses via DataChannel.
- Clean shutdown with Ctrl+C.

## Code Highlights

### Dependency Injection Setup

```csharp
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddSerilog(dispose: true));
services.AddOpenAIRealtimeWebRTC(openAiKey);
using var provider = services.BuildServiceProvider();
var webrtcEndPoint = provider.GetRequiredService<IWebRTCEndPoint>();
```

### Trigger Assistant Response

```csharp
webrtcEndPoint.DataChannelMessenger.SendResponseCreate(RealtimeVoicesEnum.shimmer, "Say Hi!");
```

### Handle Final Transcripts

```csharp
webrtcEndPoint.OnDataChannelMessage += (dc, message) =>
{
    if (message is RealtimeServerEventResponseAudioTranscriptDone done)
    {
        Log.Information($"Transcript done: {done.Transcript}");
    }
};
```

## License

BSD 3-Clause "New" or "Revised" License and the additional BDS BY-NC-SA restriction. See `LICENSE.md`.
