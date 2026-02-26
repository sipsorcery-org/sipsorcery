# OpenAI WebRTC Demo: Payment Request via Function Calling

This example demonstrates how to use [OpenAI's real-time WebRTC API](https://platform.openai.com/docs/guides/realtime-webrtc)
in conjunction with **function calling** to simulate a sales assistant that creates payment requests.

## Features

- WebRTC audio stream using local Windows audio devices.
- Realtime voice-to-text and assistant responses.
- Uses OpenAI's function calling to trigger a local function for generating payment requests.
- JSON messages are exchanged over WebRTC data channels.

## Requirements

- .NET 8
- A Windows environment (uses `WindowsAudioEndPoint`).
- An OpenAI API key with access to the real-time WebRTC feature.

## Usage

1. Clone the repo or copy the example.
2. Set your OpenAI API key as an environment variable:

```bash
set OPENAI_API_KEY=your_openai_key
dotnet run
```

## What It Does

- Connects to OpenAI's WebRTC endpoint.
- Sends and receives audio using your Windows microphone and speaker.
- Initiates a conversation with the assistant.
- Configures a local tool named `create_payment_request` that the assistant can call.
- When the assistant calls the tool, a simulated payment request is generated locally.
- The tool's response is sent back to the assistant to continue the conversation.

## Function Calling Example

```json
{
  "name": "create_payment_request",
  "arguments": {
    "amount": 49.95,
    "currency": "USD"
  }
}
```

This will trigger the local C# method `CreatePaymentRequest`, which generates a mock payment request and responds with a result like:

```
New payment request order ID is X1234
```

## Relevant Code Snippets

### Registering the Tool

```csharp
new RealtimeTool
{
    Name = "create_payment_request",
    Description = "Creates a payment request.",
    Parameters = new RealtimeToolParameters
    {
        Properties = new Dictionary<string, RealtimeToolProperty>
        {
            { "amount", new RealtimeToolProperty { Type = "number" } },
            { "currency", new RealtimeToolProperty { Type = "string" } }
        },
        Required = new List<string> { "amount", "currency" }
    }
}
```

### Function Execution

```csharp
private static RealtimeClientEventConversationItemCreate CreatePaymentRequest(...)
{
    string orderID = "X1234";
    return new RealtimeClientEventConversationItemCreate
    {
        Output = $"New payment request order ID is {orderID}",
        ...
    };
}
```

## Notes

- This is a demonstration app. The payment request is mocked and not tied to any payment system.
- You can adapt the function to integrate with a real API or database.

## License

BSD 3-Clause + BY-NC-SA restriction — see LICENSE.md.
