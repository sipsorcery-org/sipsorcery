// https://platform.openai.com/docs/api-reference/realtime-server-events/input_audio_buffer

using System.Text.Json;
using System.Text.Json.Serialization;

namespace demo;

public class OpenAIAudioBufferAudioCleared : OpenAIServerEventBase
{
    public const string TypeName = "output_audio_buffer.audio_cleared";

    [JsonPropertyName("type")]
    public override string Type => TypeName;

    [JsonPropertyName("response_id")]
    public string? ResponseID { get; set; }

    public override string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions.Default);
    }
}
