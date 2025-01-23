// https://platform.openai.com/docs/api-reference/realtime-server-events/response/output_item/done

using System.Text.Json;
using System.Text.Json.Serialization;

namespace demo;

public class OpenAIResponseOutputItemDone : OpenAIServerEventBase
{
    public const string TypeName = "response.output_item.done";

    [JsonPropertyName("type")]
    public override string Type => TypeName;

    [JsonPropertyName("response_id")]
    public required string ResponseID { get; set; }

    [JsonPropertyName("output_index")]
    public int OutputIndex { get; set; }

    // TODO add item..

    public override string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions.Default);
    }
}
