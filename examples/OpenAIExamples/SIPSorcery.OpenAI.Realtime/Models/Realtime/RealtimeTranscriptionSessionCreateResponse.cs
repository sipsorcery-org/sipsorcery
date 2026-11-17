using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// A new Realtime transcription session configuration. When a session is created via REST API,
/// it contains an ephemeral client secret. This is not present on WebSocket updates.
/// </summary>
public class RealtimeTranscriptionSessionCreateResponse : RealtimeEventBase
{
    public const string TypeValue = "transcription_session.updated";

    [JsonPropertyName("type")]
    public override string? Type => TypeValue;

    /// <summary>
    /// Ephemeral key used for client authentication. Only returned on REST-created sessions.
    /// </summary>
    [JsonPropertyName("client_secret")]
    public RealtimeClientSecret? ClientSecret { get; set; }

    /// <summary>
    /// Modalities enabled for the session. Typically "audio" and/or "text".
    /// </summary>
    [JsonPropertyName("modalities")]
    public List<RealtimeModalityEnum>? Modalities { get; set; }

    /// <summary>
    /// Format of incoming audio (e.g., pcm16, g711_ulaw, g711_alaw).
    /// </summary>
    [JsonPropertyName("input_audio_format")]
    public RealtimeAudioFormatEnum? InputAudioFormat { get; set; }

    /// <summary>
    /// Configuration for transcription model and options.
    /// </summary>
    [JsonPropertyName("input_audio_transcription")]
    public RealtimeInputAudioTranscription? InputAudioTranscription { get; set; }

    /// <summary>
    /// Configuration for turn detection (e.g., VAD).
    /// </summary>
    [JsonPropertyName("turn_detection")]
    public RealtimeTurnDetection? TurnDetection { get; set; }
}