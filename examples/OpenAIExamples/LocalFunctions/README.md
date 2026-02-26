# OpenAI WebRTC Local Function Calling Example

This is a demonstration application that establishes a WebRTC session with OpenAI's real-time API and handles **function calling** locally based on transcription input.

The application showcases how to:
- Initiate a WebRTC session with OpenAI's real-time endpoint.
- Set up local audio input/output via Windows devices.
- Receive transcription and function call prompts.
- Respond to function calls such as `get_weather` with locally computed data.

## Features

- Utilizes OpenAI’s [real-time WebRTC API](https://platform.openai.com/docs/guides/realtime-webrtc)
- Demonstrates [function calling](https://platform.openai.com/docs/guides/function-calling) using a mock weather function.
- Provides a pattern for local LLM agent behavior triggered via live transcription.

## Usage

```bash
set OPENAI_API_KEY=your_openai_key
dotnet run
```

> **Note:** This demo assumes Windows audio devices are available. Echo cancellation is not implemented; use a headset to avoid feedback loops.

## How it Works

1. The app starts a WebRTC connection to OpenAI using an API key.
2. When the session is created, a tool (`get_weather`) is registered as part of the `session.update` event.
3. When OpenAI decides to call the function, a `function_call_arguments_done` message is sent.
4. The app extracts the arguments and computes a result via a mock `GetWeather` method.
5. The result is returned to OpenAI via a `function_call_output` message.
6. A follow-up message is sent to continue the conversation with the computed result.

## Function Calling Flow

```text
[User Speaks] ──► [Transcription] ──► [OpenAI Function Call Request]
                               ▼
                  [get_weather(location)]
                               ▼
                  [Local Function Execution]
                               ▼
                  [Return Result to AI]
                               ▼
                  [AI Continues Response]
```

## Sample Weather Function

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

## License

BSD 3-Clause "New" or "Revised" License and an additional **BY-NC-SA** restriction. See `LICENSE.md` for details.
