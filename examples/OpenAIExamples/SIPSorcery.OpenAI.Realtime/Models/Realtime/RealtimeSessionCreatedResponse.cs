using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;


/// <summary>
/// A new Realtime session configuration, with an ephemeral client key.
/// Default TTL for keys is one minute.
/// </summary>
public class RealtimeSessionCreateResponse
{
    /// <summary>
    /// Ephemeral key returned by the API for client-side authentication.
    /// </summary>
    [JsonPropertyName("client_secret")]
    public RealtimeClientSecret ClientSecret { get; set; } = default!;

    /// <summary>
    /// The set of modalities the model can respond with.
    /// </summary>
    [JsonPropertyName("modalities")]
    public List<RealtimeModalityEnum>? Modalities { get; set; }

    /// <summary>
    /// The default system instructions to guide model behavior.
    /// </summary>
    [JsonPropertyName("instructions")]
    public string? Instructions { get; set; }

    /// <summary>
    /// The model used for this Realtime session.
    /// </summary>
    [JsonPropertyName("model")]
    public RealtimeModelsEnum? Model { get; set; }

    /// <summary>
    /// The voice the model will use to respond (if audio is enabled).
    /// </summary>
    [JsonPropertyName("voice")]
    public RealtimeVoicesEnum? Voice { get; set; }

    /// <summary>
    /// Format of input audio (e.g., pcm16, g711_ulaw).
    /// </summary>
    [JsonPropertyName("input_audio_format")]
    public RealtimeAudioFormatEnum? InputAudioFormat { get; set; }

    /// <summary>
    /// Format of output audio (e.g., pcm16, g711_ulaw).
    /// </summary>
    [JsonPropertyName("output_audio_format")]
    public RealtimeAudioFormatEnum? OutputAudioFormat { get; set; }

    /// <summary>
    /// Configuration for input audio transcription.
    /// </summary>
    [JsonPropertyName("input_audio_transcription")]
    public RealtimeInputAudioTranscription? InputAudioTranscription { get; set; }

    /// <summary>
    /// Configuration for turn detection.
    /// </summary>
    [JsonPropertyName("turn_detection")]
    public RealtimeTurnDetection? TurnDetection { get; set; }

    /// <summary>
    /// Functions (tools) available to the model.
    /// </summary>
    [JsonPropertyName("tools")]
    public List<RealtimeTool>? Tools { get; set; }

    /// <summary>
    /// Strategy for choosing tools (auto, none, required, or specific).
    /// </summary>
    [JsonPropertyName("tool_choice")]
    public RealtimeToolChoiceEnum? ToolChoice { get; set; }

    /// <summary>
    /// Sampling temperature (between 0.6 and 1.2).
    /// </summary>
    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    /// <summary>
    /// Max tokens for a single assistant response (int or "inf").
    /// </summary>
    [JsonPropertyName("max_response_output_tokens")]
    public object? MaxResponseOutputTokens { get; set; }
}
