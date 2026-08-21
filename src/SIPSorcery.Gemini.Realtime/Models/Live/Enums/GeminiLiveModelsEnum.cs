using System.Runtime.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// Well-known Gemini Live API model identifiers (as used in the BidiGenerateContentSetup
/// "model" field, format "models/{model-id}").
///
/// NOTE: Google revises and retires Live API model names frequently, and availability varies by
/// account/region. The values below were confirmed against a live account in August 2026 by
/// listing models and filtering for ones whose supportedGenerationMethods include
/// "bidiGenerateContent":
///
///   (Invoke-RestMethod "https://generativelanguage.googleapis.com/v1beta/models?key=$env:GEMINI_API_KEY").models `
///     | Where-Object { $_.supportedGenerationMethods -contains "bidiGenerateContent" } | Select-Object name
///
/// Re-run that check before relying on a specific value in production. Because
/// <see cref="GeminiSetup.Model"/> is a plain string, a model that isn't listed here yet (or is
/// account-specific, e.g. the robotics/translate specialised Live models) can still be used by
/// assigning its "models/{model-id}" string directly.
/// </summary>
public enum GeminiLiveModelsEnum
{
    /// <summary>
    /// Rolling alias for the current default native-audio Live model — the safest choice when you
    /// don't need to pin an exact dated version.
    /// </summary>
    [EnumMember(Value = "models/gemini-2.5-flash-native-audio-latest")]
    Gemini25FlashNativeAudioLatest,

    [EnumMember(Value = "models/gemini-2.5-flash-native-audio-preview-09-2025")]
    Gemini25FlashNativeAudioPreview202509,

    [EnumMember(Value = "models/gemini-2.5-flash-native-audio-preview-12-2025")]
    Gemini25FlashNativeAudioPreview202512,

    [EnumMember(Value = "models/gemini-3.1-flash-live-preview")]
    Gemini31FlashLivePreview
}
