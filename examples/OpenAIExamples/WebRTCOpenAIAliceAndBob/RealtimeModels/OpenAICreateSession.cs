// https://platform.openai.com/docs/api-reference/realtime-sessions/create

using System.Text.Json;
using System.Text.Json.Serialization;

namespace demo;

public class OpenAICreateSession
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("voice")]
    public string? voice { get; set; }

    //[JsonPropertyName("turn_detection")]
    //public object TurnDetection { get; set; }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions.Default);
    }
}
