// https://platform.openai.com/docs/api-reference/realtime-server-events/response/content_part/done

using System.Text.Json;
using System.Text.Json.Serialization;

namespace demo;

public class OpenAIResponseContentPartDone : OpenAIServerEventBase
{
    public const string TypeName = "response.content_part.done";

    [JsonPropertyName("type")]
    public override string Type => TypeName;

    [JsonPropertyName("response_id")]
    public required string ResponseID { get; set; }

    [JsonPropertyName("item_id")]
    public required string ItemID { get; set; }

    [JsonPropertyName("output_index")]
    public int OutputIndex { get; set; }

    [JsonPropertyName("content_index")]
    public int ContentIndex { get; set; }

    // TODO add part.

    public override string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions.Default);
    }
}
