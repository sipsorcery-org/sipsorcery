using System.Runtime.Serialization;

namespace SIPSorcery.OpenAI.Realtime;

public enum RealtimeModelsEnum
{
    [EnumMember(Value = "gpt-4o-realtime-preview")]
    Gpt4oRealtimePreview,

    [EnumMember(Value = "gpt-4o-realtime-preview-2024-10-01")]
    Gpt4oRealtimePreview_2024_10_01,

    [EnumMember(Value = "gpt-4o-realtime-preview-2024-12-17")]
    Gpt4oRealtimePreview_2024_12_17,

    [EnumMember(Value = "gpt-4o-mini-realtime-preview")]
    Gpt4oMiniRealtimePreview,

    [EnumMember(Value = "gpt-4o-mini-realtime-preview-2024-12-17")]
    Gpt4oMiniRealtimePreview_2024_12_17,

    [EnumMember(Value = "gpt-4o-realtime-preview-2025-06-03")] 
    Gpt4oRealtimePreview_2025_06_03,

    // Added 23 Feb 2026 and replaces the previous preview models.
    [EnumMember(Value = "gpt-realtime")]
    GptRealtime
}
