namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// Received once, immediately after the client's <see cref="GeminiClientSetupMessage"/> has been
/// accepted. The session is ready for audio/content exchange once this arrives.
/// </summary>
public class GeminiServerEventSetupComplete : GeminiServerMessage
{
    public const string JsonKey = "setupComplete";

    public GeminiServerEventSetupComplete() : base(GeminiServerMessageKind.SetupComplete)
    {
    }
}
