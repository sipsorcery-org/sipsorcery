# OpenAI WebRTC ASP.NET Local Function Calling Example

This ASP.NET web application demonstrates how to bridge a browser WebRTC client to OpenAI's real-time API, enabling local function calling and live transcript relay via the browser.

## Features

- **Browser-based UI**: Connect from any modern browser to interact with OpenAI's real-time WebRTC API via an ASP.NET application.
- **Live Audio & Transcription**: Streams audio from your browser to OpenAI and displays live transcriptions.
- **Local Function Calling**: Implements a local function (e.g., `get_weather`) that is invoked by OpenAI based on user speech.
- **Data Channel Relay**: Transcripts and other events are relayed from OpenAI to the browser via a WebRTC data channel.
- **Diagnostics & Status**: Visual panels for diagnostics and data channel status/messages in the browser UI.

## How it Works

1. The browser connects to the ASP.NET app via WebSocket and negotiates a WebRTC session.
2. The ASP.NET app establishes a second WebRTC session with OpenAI's real-time API.
3. Audio is piped between the browser and OpenAI.
4. Transcription and function call events from OpenAI are relayed to the browser via a WebRTC data channel.
5. The browser UI displays live transcriptions and data channel messages.
6. Local functions (like `get_weather`) are handled in the ASP.NET app and results are sent back to OpenAI.

## Usage

```bash
set OPENAI_API_KEY=your_openai_key
# Optionally set STUN_URL and TURN_URL for ICE servers
# set STUN_URL=stun:stun.cloudflare.com
# set TURN_URL=turn:your.turn.server;username;password

dotnet run
```

Then open your browser and navigate to `https://localhost:57790` (or the port shown in the console).

## Browser UI Overview

- **Start/Close**: Begin or end the WebRTC session and microphone access.
- **Data Channel Panel**: Shows live messages and status (green/red icon) for the data channel.
- **Diagnostics Panel**: (Optional) Toggle to view detailed connection and event logs.

## Function Calling Flow

```text
[User Speaks in Browser] ──► [ASP.NET app ] ──► [Transcription by OpenAI] ──► [OpenAI Function Call Request]
                                         ▼
                              [get_weather(location)]
                                         ▼
                              [Local Function Execution in ASP.NET]
                                         ▼
                              [Result Returned to OpenAI]
                                         ▼
                              [AI Continues Response]
```

## Example Local Function

```csharp
private static string GetWeather(RealtimeServerEventResponseFunctionCallArgumentsDone argsDone)
{
    var location = argsDone.Arguments.GetNamedArgumentValue("location") ?? string.Empty;
    return location switch
    {
        string s when s.Contains("Dublin") => "It's raining and 7 degrees.",
        string s when s.Contains("Sydney") => "It's humid and stormy and 30 degrees.",
        _ => "It's sunny and 20 degrees."
    };
}
```

## Project Structure

- **Program.cs**: ASP.NET Core entry point, WebSocket/WebRTC negotiation, function call logic.
- **wwwroot/index.html**: Browser UI for audio, transcript, and diagnostics.
- **/src**: Core library for OpenAI WebRTC integration.

## License

BSD 3-Clause "New" or "Revised" License and an additional BY-NC-SA restriction. See `LICENSE.md` for details.
