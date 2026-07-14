//-----------------------------------------------------------------------------
// Filename: ILlmClient.cs
//
// Description: The reply-generation contract the rest of the app drives. Two
// implementations: LocalLlmClient (HTTP to an OpenAI-compatible endpoint - Ollama,
// LM Studio, or a hosted gateway) and LlamaSharpLlmClient (llama.cpp IN-PROCESS via
// LLamaSharp - no external server to orchestrate). Both speak the same surface: a
// one-shot reply, and a sentence-by-sentence stream so the avatar can start talking
// before the full completion has been generated.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading.Tasks;

namespace demo;

public interface ILlmClient
{
    /// <summary>False when no model/endpoint is configured - callers speak the prompt verbatim.</summary>
    bool IsConfigured { get; }

    /// <summary>Human-readable engine summary for startup logging.</summary>
    string Description { get; }

    /// <summary>Returns the whole in-character reply in one shot (falls back to the prompt on failure).</summary>
    Task<string> GenerateReplyAsync(string prompt);

    /// <summary>
    /// Pays any one-time first-inference costs up front (weights page-in, context allocation)
    /// so the first real reply isn't ~10s slower than the rest. No-op for HTTP clients - the
    /// remote server owns its own warm state.
    /// </summary>
    Task WarmUpAsync() => Task.CompletedTask;

    /// <summary>Streams the reply one sentence at a time as tokens arrive.</summary>
    IAsyncEnumerable<string> StreamReplyAsync(string prompt);
}

/// <summary>Bits shared by the LLM clients: the persona prompt and sentence chunking.</summary>
public static class LlmShared
{
    public const string SystemPrompt =
        "You are Max Headroom, the stuttering, wisecracking 1980s computer-generated TV host. " +
        "Reply in one or two short, punchy, slightly sarcastic sentences. Keep it light and witty. " +
        "Plain text only, no stage directions or emojis.";

    /// <summary>
    /// Removes and returns the first complete sentence (up to and including a '.', '!',
    /// '?' or newline) from <paramref name="buffer"/>. Returns null when there is no
    /// sentence terminator yet, or an empty string when the removed span was blank.
    /// </summary>
    public static string TakeSentence(System.Text.StringBuilder buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            char c = buffer[i];
            if (c == '.' || c == '!' || c == '?' || c == '\n')
            {
                int len = i + 1;
                var sentence = buffer.ToString(0, len).Trim();
                buffer.Remove(0, len);
                return sentence;
            }
        }

        return null;
    }
}
