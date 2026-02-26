using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Emitted by the server in `server_vad` mode when speech is detected in the audio buffer.
/// 
/// This signals the start of speech and allows the client to provide real-time feedback,
/// such as stopping playback or showing voice activity. The `item_id` will be associated
/// with the future user message created when speech ends.
/// </summary>
public class RealtimeServerEventInputAudioBufferSpeechStarted : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "input_audio_buffer.speech_started".
    /// </summary>
    public const string TypeName = "input_audio_buffer.speech_started";

    /// <summary>
    /// Overrides the base type property with the constant value.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// Milliseconds from the beginning of the session's audio buffer where speech started.
    /// Includes any `prefix_padding_ms` as configured in the session.
    /// </summary>
    [JsonPropertyName("audio_start_ms")]
    public required int AudioStartMs { get; set; }

    /// <summary>
    /// The ID of the user message item that will eventually be created when speech ends.
    /// </summary>
    [JsonPropertyName("item_id")]
    public required string ItemID { get; set; }
}
