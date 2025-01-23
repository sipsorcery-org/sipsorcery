// https://platform.openai.com/docs/api-reference/realtime-server-events/response/audio_transcript/done

using System.Text.Json;
using System.Text.Json.Serialization;

namespace demo;

public class OpenAIResponseAudioTranscriptDone : OpenAIServerEventBase
{
    public const string TypeName = "response.audio_transcript.done";

    [JsonPropertyName("type")]
    public override string Type => TypeName;

    [JsonPropertyName("response_id")]
    public string? ResponseID { get; set; }

    [JsonPropertyName("item_id")]
    public string? ItemID { get; set; }

    [JsonPropertyName("output_index")]
    public int OutputIndex { get; set; }

    [JsonPropertyName("content_index")]
    public int ContentIndex { get; set; }

    public string? Transcript{ get; set; }

    public override string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions.Default);
    }
}
