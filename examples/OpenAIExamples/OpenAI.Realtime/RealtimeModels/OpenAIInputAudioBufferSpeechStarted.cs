// https://platform.openai.com/docs/api-reference/realtime-server-events/input_audio_buffer/speech_started

using System.Text.Json;
using System.Text.Json.Serialization;

namespace demo;

public class OpenAIInputAudioBufferSpeechStarted : OpenAIServerEventBase
{
    public const string TypeName = "input_audio_buffer.speech_started";

    [JsonPropertyName("type")]
    public override string Type => TypeName;

    [JsonPropertyName("audio_start_ms")]
    public int AudioStartMilliseconds { get; set; }

    [JsonPropertyName("item_id")]
    public string? ItemID { get; set; }

    public override string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions.Default);
    }
}
