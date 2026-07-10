//-----------------------------------------------------------------------------
// Filename: IReplyGenerator.cs
//
// Description: The reply-generation seam: conversation history in, a streamed
// reply out. Deliberate changes from the examples' ILlmClient:
//
//  * History-aware. The examples were stateless (prompt string in) so the
//    agent forgot every previous turn; here the session owns the transcript
//    and passes it whole. The persona/system prompt is history[0] with
//    ChatRole.System, replacing the examples' systemPrompt constructor
//    parameter.
//  * Streaming only. The one-shot GenerateReplyAsync was just a concatenation
//    of the stream; callers that want it can concatenate.
//  * Cancellation everywhere, so barge-in can abandon a half-generated reply.
//
// NOTE: this seam intentionally mirrors Microsoft.Extensions.AI's IChatClient
// (chat message list in, streamed updates out) so a bridging adapter is a few
// lines, without making every consumer take the M.E.AI dependency. If we later
// decide the dependency is acceptable this interface is the thing IChatClient
// would replace.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 08 Jul 2026  Aaron Clauson   Created, evolved from the WebRTCMaxHeadroom
//                              ILlmClient.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SIPSorcery.RealtimeAgents;

/// <summary>The speaker of a <see cref="ChatTurn"/> in an agent conversation.</summary>
public enum ChatRole
{
    /// <summary>The agent's persona / behavioural instructions (by convention the first turn).</summary>
    System,

    /// <summary>The human caller (recognised speech or typed text).</summary>
    User,

    /// <summary>The agent's own previous replies.</summary>
    Assistant
}

/// <summary>One turn of an agent conversation.</summary>
public readonly record struct ChatTurn(ChatRole Role, string Text);

/// <summary>
/// Generates the agent's replies: full conversation history in (persona
/// first, newest user turn last), reply text streamed out. Implementations
/// range from an in-process llama.cpp model to any OpenAI-compatible HTTP
/// endpoint.
/// </summary>
public interface IReplyGenerator : System.IDisposable
{
    /// <summary>
    /// Streams the reply to the newest user turn in <paramref name="history"/>,
    /// in chunks sized for incremental speech synthesis (sentences or clauses,
    /// not single tokens). Cancelling the token abandons the remainder of the
    /// reply (barge-in); implementations must not raise partial chunks after
    /// cancellation.
    /// </summary>
    IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatTurn> history, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pays any one-time first-inference costs up front (weights page-in,
    /// context allocation) so the first real reply isn't dramatically slower
    /// than the rest. No-op by default - remote endpoints own their own warm
    /// state.
    /// </summary>
    Task WarmUpAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
