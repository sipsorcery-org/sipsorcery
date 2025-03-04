// https://platform.openai.com/docs/api-reference/realtime-sessions/create

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace demo;

public class OpenAISession
{
    [JsonPropertyName("modalities")]
    public string[] Modalities { get; set; } = { "audio", "text" };

    [JsonPropertyName("instructions")]
    public string? Instructions { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("voice")]
    public OpenAIVoicesEnum? Voice { get; set; }

    [JsonPropertyName("input_audio_format")]
    public OpenAIAudioFormatsEnum InputAudioFormat { get; set; } = OpenAIAudioFormatsEnum.pcm16;

    [JsonPropertyName("output_audio_format")]
    public OpenAIAudioFormatsEnum OutputAudioFormat { get; set; } = OpenAIAudioFormatsEnum.pcm16;

    [JsonPropertyName("turn_detection")]
    public OpenAITurnDetection? TurnDetection { get; set; }

    [JsonPropertyName("tools")]
    public List<OpenAITool>? Tools { get; set; }

    [JsonPropertyName("tool_choice")]
    public OpenAIToolChoiceEnum ToolChoice { get; set; } = OpenAIToolChoiceEnum.auto;

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions.Default);
    }
}
