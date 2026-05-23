using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Realtime session object configuration.
/// </summary>
public class RealtimeSession
{
    /// <summary>
    /// Required session discriminator in the GA Realtime API. Always
    /// "realtime" for the WebRTC/WebSocket Realtime session. The GA server
    /// uses this to validate the session.update payload — without it the
    /// session is effectively unconfigured and the model will not respond.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type => "realtime";

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
    /// Nested audio configuration (GA shape). Populated automatically when
    /// <see cref="Voice"/> is assigned; callers can also build it directly
    /// for finer-grained control.
    /// </summary>
    [JsonPropertyName("audio")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RealtimeAudio? Audio { get; set; }

    /// <summary>
    /// The voice used to respond, once audio output is engaged. Public API
    /// retained for backward compatibility: writes are routed into
    /// <see cref="Audio"/>.<see cref="RealtimeAudio.Output"/>.<see cref="RealtimeAudioOutput.Voice"/>
    /// (the GA location) and JSON serialisation skips this flat property.
    /// </summary>
    [JsonIgnore]
    public RealtimeVoicesEnum? Voice
    {
        get => Audio?.Output?.Voice;
        set
        {
            if (value == null)
            {
                if (Audio?.Output != null)
                {
                    Audio.Output.Voice = null;
                }
                return;
            }
            Audio ??= new RealtimeAudio();
            Audio.Output ??= new RealtimeAudioOutput();
            Audio.Output.Voice = value;
        }
    }

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

