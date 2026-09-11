using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Nested audio configuration container introduced by the GA Realtime API.
/// Voice, format, transcription and turn-detection settings used to live as
/// flat fields directly on the session / response.create object in the beta
/// API; the GA spec groups them under <c>audio.input</c> and <c>audio.output</c>.
/// </summary>
public class RealtimeAudio
{
    /// <summary>
    /// Output-side audio configuration (the audio the model produces, e.g. voice).
    /// </summary>
    [JsonPropertyName("output")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RealtimeAudioOutput? Output { get; set; }
}

/// <summary>
/// Output-side fields under <c>audio.output</c>.
/// </summary>
public class RealtimeAudioOutput
{
    /// <summary>
    /// The voice the model uses to respond. Supported built-in voices include
    /// alloy, ash, ballad, coral, echo, sage, shimmer, verse, marin, and cedar.
    /// </summary>
    [JsonPropertyName("voice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RealtimeVoicesEnum? Voice { get; set; }
}
