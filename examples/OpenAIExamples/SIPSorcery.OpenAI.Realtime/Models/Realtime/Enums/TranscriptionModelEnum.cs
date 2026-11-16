using System.Runtime.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

public enum TranscriptionModelEnum
{
    /// <summary>
    /// GPT-4o transcription model.
    /// </summary>
    [EnumMember(Value = "gpt-4o-transcribe")]
    Gpt4oTranscribe,

    /// <summary>
    /// GPT-4o mini transcription model.
    /// </summary>
    [EnumMember(Value = "gpt-4o-mini-transcribe")]
    Gpt4oMiniTranscribe,

    /// <summary>
    /// Whisper-1 transcription model.
    /// </summary>
    [EnumMember(Value = "whisper-1")]
    Whisper1
}