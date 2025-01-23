// https://platform.openai.com/docs/api-reference/realtime-server-events/response/done

using System.Text.Json;
using System.Text.Json.Serialization;

namespace demo;

public class OpenAIResponse
{
    [JsonPropertyName("id")]
    public required string ID { get; set; }

    [JsonPropertyName("object")]
    public required string @Object { get; set; }

    [JsonPropertyName("status")]
    public OpenAIResponseStatusEnum Status { get; set; }

    // TODO.

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions.Default);
    }
}
