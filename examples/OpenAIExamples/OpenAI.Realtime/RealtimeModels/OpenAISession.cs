// https://platform.openai.com/docs/api-reference/realtime-sessions/create

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace demo;

public class OpenAISession
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("voice")]
    public OpenAIVoicesEnum? Voice { get; set; }

    [JsonPropertyName("turn_detection")]
    public OpenAITurnDetection? TurnDetection { get; set; }

    [JsonPropertyName("tools")]
    public List<OpenAITool>? Tools { get; set; }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions.Default);
    }
}
