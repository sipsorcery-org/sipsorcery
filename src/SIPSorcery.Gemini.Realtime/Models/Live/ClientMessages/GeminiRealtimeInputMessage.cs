using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// Wire shape: {"realtimeInput": {...}}. Used to stream live microphone audio (and optionally
/// video/text/activity markers) while a session is open.
/// </summary>
public class GeminiRealtimeInputMessage : GeminiClientMessage
{
    [JsonPropertyName("realtimeInput")]
    public GeminiRealtimeInput RealtimeInput { get; set; } = new();
}

public class GeminiRealtimeInput
{
    /// <summary>
    /// Raw PCM16 audio chunk, mimeType "audio/pcm;rate=16000".
    /// </summary>
    [JsonPropertyName("audio")]
    public GeminiBlob? Audio { get; set; }

    [JsonPropertyName("video")]
    public GeminiBlob? Video { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>
    /// Marks the start of a user activity turn. Only meaningful when automatic activity
    /// detection is disabled (<see cref="GeminiAutomaticActivityDetection.Disabled"/>).
    /// </summary>
    [JsonPropertyName("activityStart")]
    public object? ActivityStart { get; set; }

    /// <summary>
    /// Marks the end of a user activity turn. Only meaningful when automatic activity
    /// detection is disabled.
    /// </summary>
    [JsonPropertyName("activityEnd")]
    public object? ActivityEnd { get; set; }

    /// <summary>
    /// Signals that no more audio will be sent for the current input stream.
    /// </summary>
    [JsonPropertyName("audioStreamEnd")]
    public bool? AudioStreamEnd { get; set; }
}
