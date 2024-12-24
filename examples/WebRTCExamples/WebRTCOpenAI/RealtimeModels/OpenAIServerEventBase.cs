using System.Text.Json;
using System.Text.Json.Serialization;

namespace demo;

public class OpenAIServerEventBase
{
    [JsonPropertyName("event_id")]
    public string EventID { get; set; }

    public string Type { get; set; }

    public virtual string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions.Default);
    }
}
