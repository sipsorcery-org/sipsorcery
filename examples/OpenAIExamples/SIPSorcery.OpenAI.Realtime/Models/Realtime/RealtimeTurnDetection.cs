using SIPSorcery.OpenAI.Realtime.Models;
using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime;

/// <summary>
/// Configuration for turn detection: server VAD or semantic VAD.
/// </summary>
public class RealtimeTurnDetection
{
    /// <summary>
    /// Type of turn detection.
    /// </summary>
    [JsonPropertyName("type")]
    public TurnDetectionTypeEnum Type { get; set; } = TurnDetectionTypeEnum.server_vad;

    /// <summary>
    /// Eagerness to respond, used only in semantic VAD mode.
    /// </summary>
    [JsonPropertyName("eagerness")]
    public EagernessLevelEnum Eagerness { get; set; } = EagernessLevelEnum.auto;

    /// <summary>
    /// Activation threshold for VAD (0.0 to 1.0), used only in server VAD mode.
    /// </summary>
    [JsonPropertyName("threshold")]
    public double? Threshold { get; set; }

    /// <summary>
    /// Audio included before detected speech, in milliseconds. Used in server VAD.
    /// </summary>
    [JsonPropertyName("prefix_padding_ms")]
    public int? PrefixPaddingMs { get; set; }

    /// <summary>
    /// Silence duration to detect speech stop, in milliseconds. Used in server VAD.
    /// </summary>
    [JsonPropertyName("silence_duration_ms")]
    public int? SilenceDurationMs { get; set; }

    /// <summary>
    /// Whether to generate a response when a VAD stop event occurs. Not available for transcription sessions.
    /// </summary>
    [JsonPropertyName("create_response")]
    public bool CreateResponse { get; set; } = true;

    /// <summary>
    /// Whether to interrupt an ongoing response when a VAD start event occurs. Not available for transcription sessions.
    /// </summary>
    [JsonPropertyName("interrupt_response")]
    public bool InterruptResponse { get; set; } = true;
}
