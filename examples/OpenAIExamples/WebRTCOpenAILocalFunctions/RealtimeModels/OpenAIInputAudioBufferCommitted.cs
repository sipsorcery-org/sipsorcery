// https://platform.openai.com/docs/api-reference/realtime-server-events/input_audio_buffer

using System.Text.Json;
using System.Text.Json.Serialization;

namespace demo;

public class OpenAIInputAudioBufferCommitted : OpenAIServerEventBase
{
    public const string TypeName = "input_audio_buffer.committed";

    [JsonPropertyName("type")]
    public override string Type => TypeName;

    [JsonPropertyName("item_id")]
    public string? ItemID { get; set; }

    [JsonPropertyName("previous_item_id")]
    public string? PreviousItemID { get; set; }

    public override string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions.Default);
    }
}
