using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Emitted by the server in `server_vad` mode when it detects the end of speech in the audio buffer.
/// 
/// This marks the conclusion of voice input, after which a user message will be created
/// and emitted via a corresponding `conversation.item.created` event.
/// </summary>
public class RealtimeServerEventInputAudioBufferSpeechStopped : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "input_audio_buffer.speech_stopped".
    /// </summary>
    public const string TypeName = "input_audio_buffer.speech_stopped";

    /// <summary>
    /// Overrides the base type property with the constant event type name.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// Milliseconds from the start of session audio when speech ended.
    /// Includes configured `min_silence_duration_ms`.
    /// </summary>
    [JsonPropertyName("audio_end_ms")]
    public required int AudioEndMs { get; set; }

    /// <summary>
    /// The ID of the user message item that will be created based on this audio.
    /// </summary>
    [JsonPropertyName("item_id")]
    public required string ItemID { get; set; }
}
