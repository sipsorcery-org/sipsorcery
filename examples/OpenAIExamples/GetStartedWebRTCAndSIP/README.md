# OpenAI WebRTC SIP Gateway Example

This example demonstrates how to create a SIP-to-OpenAI WebRTC gateway that receives incoming SIP calls and bridges the audio to OpenAI's real-time API. The caller can have a voice conversation with OpenAI through their SIP client or phone.

## Features

- **SIP Server**: Listens for incoming SIP calls on UDP port 5060
- **Audio Bridging**: Routes audio from SIP caller to OpenAI and responses back to caller
- **Real-time Conversation**: Enables natural voice conversations between SIP callers and OpenAI
- **Call Management**: Handles call setup, teardown, and proper resource cleanup
- **Transcription Logging**: Displays conversation transcripts in real-time

## How it Works

1. The application starts a SIP server listening on UDP port 5060
2. When a SIP call is received, it's automatically answered
3. A WebRTC connection is established with OpenAI's real-time endpoint
4. Audio is bridged bidirectionally:
   - Caller's voice → OpenAI (for processing and response generation)
   - OpenAI's response → Caller (through the SIP call)
5. Conversation transcripts are logged to the console
6. When the caller hangs up, connections are properly cleaned up

## Requirements

- Windows OS (due to media dependencies)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- OpenAI API key with access to the Realtime API
- SIP client or softphone for testing
- Network access on UDP port 5060

## Usage

1. **Set your OpenAI API key**:
```bash
set OPENAI_API_KEY=your_openai_key
```

2. **Run the application**:
```bash
dotnet run
```

3. **Test with a SIP call**:
   - Use any SIP client (like [microSIP](https://www.microsip.org/) or a hardware SIP phone)
   - Call: `sip:test@{your_computer_ip}:5060`
   - Start speaking once the call connects

## Example Session

```
[14:30:15 INF] SIP user agent listening on UDP:*:5060...
Waiting for incoming SIP calls...
To test, call sip:test@192.168.1.100:5060

[14:30:45 INF] Incoming SIP call from sip:user@192.168.1.50:5060
[14:30:46 INF] SIP call answered, connecting to OpenAI...
[14:30:47 INF] OpenAI WebRTC peer connection established.
[14:30:48 INF] Audio bridge established between SIP call and OpenAI.
[14:30:49 INF] AI: Hello! How can I help you today?
[14:30:52 INF] CALLER: Hi, can you tell me about the weather?
[14:30:54 INF] AI: I'd be happy to help with weather information, but I don't have access to current weather data...
```

## Configuration

The demo uses these default settings:
- **SIP Port**: UDP 5060
- **OpenAI Voice**: shimmer
- **Instructions**: "You are speaking with someone via a phone call. Keep responses brief and conversational."
- **Transcription**: Enabled with Whisper-1 model

## Network Requirements

- Ensure UDP port 5060 is accessible for incoming SIP calls
- If testing from external networks, configure firewall/router appropriately
- For RTP audio, ensure UDP ports in the range 10000-20000 are accessible (default SIPSorcery range)

## Testing

### Local Testing
1. Install a softphone like [microSIP](https://www.microsip.org/)
2. Configure it to call `sip:test@127.0.0.1:5060`
3. Make the call and start speaking

### Network Testing
1. Find your computer's IP address: `ipconfig` (Windows) or `ifconfig` (Linux/Mac)
2. Call `sip:test@{your_ip}:5060` from any SIP client on the network
3. Ensure firewall allows UDP 5060 and RTP ports

## Limitations

- The only supported audio codec is Opus.
- No authentication or call routing (accepts all incoming calls)
- Designed for demonstration purposes

## License

BSD 3-Clause "New" or "Revised" License and the additional BDS BY-NC-SA restriction. See `LICENSE.md` for details.