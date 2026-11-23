//-----------------------------------------------------------------------------
// Filename: IAgentMediaPort.cs
//
// Description: Where an agent plugs into a call. The agent never sees an
// RTCPeerConnection, a SIP call or a softphone: it sees this port - decoded
// caller audio in, agent speech out, and optionally DTMF, text and a video
// track. Adapters bind it to a transport:
//
//  * an RTCPeerConnection (the WebRTCMaxHeadroom shape);
//  * a SIP call's RTP session (a phone-line voice agent, no video);
//  * an application's own media relay (e.g. the softphone's MediaRelay, so the
//    agent answers a call the user can later take over);
//  * local devices or WAV files, for tests.
//
// Formats are the port's business: it decodes whatever the call negotiated
// (G.711 at 8 kHz, Opus at 48 kHz) and says what rate it wants back, and the
// session resamples to and from its engines.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 23 Nov 2026  Aaron Clauson   Created, replacing the planned
//                              Attach(RTCPeerConnection) / Attach(SIP) pair.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.RealtimeAgents;

/// <summary>What a media port can carry beyond audio.</summary>
[Flags]
public enum AgentMediaCapabilities
{
    /// <summary>Audio only (a plain phone line).</summary>
    AudioOnly = 0,

    /// <summary>Telephone events (RFC 4733 or SIP INFO) in and out.</summary>
    Dtmf = 1,

    /// <summary>Text in and out (RFC 4103 real-time text, a data channel, chat).</summary>
    Text = 2,

    /// <summary>A video track the agent's avatar can be sent on.</summary>
    Video = 4
}

/// <summary>
/// The agent's view of one call's media. One port per call; a session attaches
/// to a port and can detach from it while the call carries on.
/// </summary>
/// <remarks>
/// Pacing: the session sends speech in real time (it owns the playout clock,
/// barge-in and the avatar's lip-sync lead), so <see cref="SendAudio"/> must
/// forward promptly and not buffer ahead.
/// </remarks>
public interface IAgentMediaPort
{
    /// <summary>What this port carries besides audio.</summary>
    AgentMediaCapabilities Capabilities { get; }

    /// <summary>The sample rate the port wants agent speech at (e.g. 8000 for G.711, 48000 for Opus).</summary>
    int OutputSampleRateHz { get; }

    /// <summary>
    /// Raised with each block of decoded caller audio. The segment's memory is
    /// only valid for the duration of the call: copy what you need.
    /// </summary>
    event Action<AudioSegment>? OnAudioReceived;

    /// <summary>Sends a block of agent speech, already at <see cref="OutputSampleRateHz"/>.</summary>
    void SendAudio(AudioSegment segment);

    /// <summary>Raised for each telephone event the caller presses. Never raised without <see cref="AgentMediaCapabilities.Dtmf"/>.</summary>
    event Action<char>? OnDtmfReceived;

    /// <summary>Sends a telephone event. Throws <see cref="NotSupportedException"/> without <see cref="AgentMediaCapabilities.Dtmf"/>.</summary>
    Task SendDtmfAsync(char tone, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised with text from the caller: a finished line, not keystrokes.
    /// Never raised without <see cref="AgentMediaCapabilities.Text"/>.
    /// </summary>
    event Action<string>? OnTextReceived;

    /// <summary>Sends a line of text. Throws <see cref="NotSupportedException"/> without <see cref="AgentMediaCapabilities.Text"/>.</summary>
    Task SendTextAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts an in-process avatar's video on the call, or takes it off with
    /// null. Returns false when the port has no video
    /// (<see cref="AgentMediaCapabilities.Video"/>), in which case the agent
    /// simply has no face on this call. Engine-hosted avatars send their own
    /// video and never use this.
    /// </summary>
    bool SetVideoSource(IVideoSource? source);
}
