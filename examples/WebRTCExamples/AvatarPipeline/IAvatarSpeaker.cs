//-----------------------------------------------------------------------------
// Filename: IAvatarSpeaker.cs
//
// Description: The speaking surface the rest of the app drives. IAvatarSpeaker is the
// common contract (speak a complete piece of text); the batch TTS engines implement it
// via LipSyncTtsSpeaker. IStreamingAvatarSpeaker adds the ability to consume text
// incrementally as it is produced (e.g. an LLM token stream), so synthesis can overlap
// generation and the avatar starts talking sooner - implemented by
// ElevenLabsStreamingTtsSpeaker.
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

public interface IAvatarSpeaker
{
    /// <summary>Synthesises and speaks a complete piece of text, with lip-sync.</summary>
    Task SpeakAsync(string text);
}

public interface IStreamingAvatarSpeaker : IAvatarSpeaker
{
    /// <summary>Speaks a reply as its text arrives in chunks (e.g. an LLM token/sentence stream).</summary>
    Task SpeakStreamAsync(IAsyncEnumerable<string> textChunks);
}
