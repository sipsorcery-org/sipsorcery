using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Realtime transcription session object configuration.
/// </summary>
public class RealtimeTranscriptionSessionCreateRequest
{
    /// <summary>
    /// The set of modalities the model can respond with. To disable audio, set this to ["text"].
    /// </summary>
    [JsonPropertyName("modalities")]
    public List<RealtimeModalityEnum> Modalities { get; set; } = [RealtimeModalityEnum.text, RealtimeModalityEnum.audio ];

    /// <summary>
    /// The format of input audio. Options are pcm16, g711_ulaw, or g711_alaw.
    /// For pcm16, input audio must be 16-bit PCM at a 24kHz sample rate, single channel (mono), and little-endian byte order.
    /// </summary>
    [JsonPropertyName("input_audio_format")]
    public RealtimeAudioFormatEnum InputAudioFormat { get; set; } = RealtimeAudioFormatEnum.pcm16;

    /// <summary>
    /// Configuration for input audio transcription, including optional language and prompt for guidance.
    /// </summary>
    [JsonPropertyName("input_audio_transcription")]
    public RealtimeInputAudioTranscription? InputAudioTranscription { get; set; }

    /// <summary>
    /// Configuration for turn detection (e.g., server VAD or semantic VAD).
    /// </summary>
    [JsonPropertyName("turn_detection")]
    public RealtimeTurnDetection? TurnDetection { get; set; }

    /// <summary>
    /// Configuration for input audio noise reduction. Can be set to null to turn off.
    /// </summary>
    [JsonPropertyName("input_audio_noise_reduction")]
    public InputAudioNoiseReduction? InputAudioNoiseReduction { get; set; }

    /// <summary>
    /// The set of items to include in the transcription, such as item.input_audio_transcription.logprobs.
    /// </summary>
    [JsonPropertyName("include")]
    public List<string>? Include { get; set; }
}

