// https://platform.openai.com/docs/api-reference/realtime-sessions/session_object#realtime-sessions/session_object-tools

using System.Text.Json.Serialization;

namespace demo;

public class OpenAITool
{
    [JsonPropertyName("type")]
    public OpenAIToolKindEnum Type => OpenAIToolKindEnum.function;

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("parameters")]
    public OpenAIToolParameters? Parameters { get; set; }
}
