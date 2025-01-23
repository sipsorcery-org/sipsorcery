// https://platform.openai.com/docs/api-reference/realtime-server-events/input_audio_buffer/speech_stopped

using System.Text.Json;
using System.Text.Json.Serialization;

namespace demo;

public class OpenAIInputAudioBufferSpeechStopped : OpenAIServerEventBase
{
    public const string TypeName = "input_audio_buffer.speech_stopped";

    [JsonPropertyName("type")]
    public override string Type => TypeName;

    [JsonPropertyName("audio_end_ms")]
    public int AUdioEndMilliseconds { get; set; }

    [JsonPropertyName("item_id")]
    public string? ItemID { get; set; }

    public override string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions.Default);
    }
}
