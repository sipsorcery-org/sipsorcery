using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Realtime session object configuration.
/// </summary>
public class RealtimeSession
{
    /// <summary>
    /// Unique identifier for the session (e.g., sess_1234567890abcdef).
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// The set of modalities the model can respond with. To disable audio, set this to ["text"].
    /// </summary>
    [JsonPropertyName("modalities")]
    public List<RealtimeModalityEnum> Modalities { get; set; } = new() { RealtimeModalityEnum.text, RealtimeModalityEnum.audio };

    /// <summary>
    /// The default system instructions to guide model behavior.
    /// </summary>
    [JsonPropertyName("instructions")]
    public string? Instructions { get; set; }

    /// <summary>
    /// The Realtime model used for this session.
    /// </summary>
    [JsonPropertyName("model")]
    public RealtimeModelsEnum? Model { get; set; }

    /// <summary>
    /// The voice used to respond, once audio output is engaged.
    /// </summary>
    [JsonPropertyName("voice")]
    public RealtimeVoicesEnum? Voice { get; set; }

    /// <summary>
    /// Input audio format.
    /// </summary>
    [JsonPropertyName("input_audio_format")]
    public RealtimeAudioFormatEnum InputAudioFormat { get; set; } = RealtimeAudioFormatEnum.pcm16;

    /// <summary>
    /// Output audio format.
    /// </summary>
    [JsonPropertyName("output_audio_format")]
    public RealtimeAudioFormatEnum OutputAudioFormat { get; set; } = RealtimeAudioFormatEnum.pcm16;

    /// <summary>
    /// Configuration for input audio transcription.
    /// </summary>
    [JsonPropertyName("input_audio_transcription")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RealtimeInputAudioTranscription? InputAudioTranscription { get; set; }

    /// <summary>
    /// Configuration for server or semantic turn detection.
    /// </summary>
    [JsonPropertyName("turn_detection")]
    public RealtimeTurnDetection? TurnDetection { get; set; }

    /// <summary>
    /// Configuration for input audio noise reduction.
    /// </summary>
    [JsonPropertyName("input_audio_noise_reduction")]
    public NoiseReductionTypeEnum? InputAudioNoiseReduction { get; set; }

    /// <summary>
    /// Tools (functions) available to the model.
    /// </summary>
    [JsonPropertyName("tools")]
    public List<RealtimeTool>? Tools { get; set; }

    /// <summary>
    /// How the model chooses tools.
    /// </summary>
    [JsonPropertyName("tool_choice")]
    public RealtimeToolChoiceEnum ToolChoice { get; set; } = RealtimeToolChoiceEnum.auto;

    /// <summary>
    /// Sampling temperature for the model.
    /// </summary>
    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.8;

    /// <summary>
    /// Maximum number of tokens for an assistant response.
    /// </summary>
    [JsonPropertyName("max_response_output_tokens")]
    public object? MaxResponseOutputTokens { get; set; } = "inf";
}

