using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Configuration for input audio transcription.
/// </summary>
public class RealtimeInputAudioTranscription
{
    /// <summary>
    /// The model to use for transcription: gpt-4o-transcribe, gpt-4o-mini-transcribe, or whisper-1.
    /// </summary>
    [JsonPropertyName("model")]
    public TranscriptionModelEnum? Model { get; set; }

    /// <summary>
    /// The language of the input audio in ISO-639-1 format (e.g. "en").
    /// </summary>
    [JsonPropertyName("language")]
    public string? Language { get; set; }

    /// <summary>
    /// Optional prompt to guide the model's style or continue a previous audio segment.
    /// </summary>
    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }
}
