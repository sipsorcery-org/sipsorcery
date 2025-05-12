// https://platform.openai.com/docs/api-reference/realtime-sessions/session_object#realtime-sessions/session_object-tools
//
// Example payload:
//{
//    "event_id": "event_123",
//    "type": "session.update",
//    "session": {
//        "modalities": ["text", "audio"],
//        "instructions": "You are a helpful assistant.",
//        "voice": "sage",
//        "input_audio_format": "pcm16",
//        "output_audio_format": "pcm16",
//        "input_audio_transcription": {
//            "model": "whisper-1"
//        },
//        "turn_detection": {
//            "type": "server_vad",
//            "threshold": 0.5,
//            "prefix_padding_ms": 300,
//            "silence_duration_ms": 500,
//            "create_response": true
//        },
//        "tools": [
//            {
//            "type": "function",
//                "name": "get_weather",
//                "description": "Get the current weather...",
//                "parameters": {
//                "type": "object",
//                    "properties": {
//                    "location": { "type": "string" }
//                },
//                    "required": ["location"]
//                }
//        }
//        ],
//        "tool_choice": "auto",
//        "temperature": 0.8,
//        "max_response_output_tokens": "inf"
//    }
//}

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace demo;

public class OpenAIToolParameters
{
    [JsonPropertyName("type")]
    public string Type => "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, OpenAIToolProperty>? Properties { get; set; }

    [JsonPropertyName("required")]
    public List<string>? Required { get; set; }
}

public class OpenAIToolProperty
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }
}
