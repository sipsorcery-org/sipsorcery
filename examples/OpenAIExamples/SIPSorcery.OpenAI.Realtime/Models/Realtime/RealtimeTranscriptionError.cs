using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Details of a transcription failure, including type, code, message, and related parameter.
/// </summary>
public class RealtimeTranscriptionError
{
    /// <summary>
    /// The type of error.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// Optional error code (e.g., "audio_unintelligible").
    /// </summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>
    /// Human-readable message explaining the error.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Parameter related to the error, if applicable.
    /// </summary>
    [JsonPropertyName("param")]
    public string? Param { get; set; }
}