namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// Presence-only marker: assigning an (empty) instance to
/// <see cref="GeminiSetup.InputAudioTranscription"/>/<see cref="GeminiSetup.OutputAudioTranscription"/>
/// enables transcription for that stream ("{}" on the wire); leaving the property null omits it
/// and transcription stays disabled.
/// </summary>
public class GeminiAudioTranscriptionConfig
{
}
