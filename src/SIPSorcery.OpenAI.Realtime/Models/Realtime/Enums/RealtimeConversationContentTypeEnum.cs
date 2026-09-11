namespace SIPSorcery.OpenAI.Realtime.Models;

public enum RealtimeConversationContentTypeEnum
{
    input_audio,

    input_text,

    item_reference,

    // Legacy beta content types — kept for backward compatibility with any
    // session created against pre-GA models.
    text,

    audio,

    // GA Realtime API content types. "output_text" is what the model emits
    // for text content parts; "output_audio" for audio content parts.
    output_text,

    output_audio
}
