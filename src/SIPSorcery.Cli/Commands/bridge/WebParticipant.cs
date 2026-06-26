//-----------------------------------------------------------------------------
// Filename: WebParticipant.cs
//
// Description: The "web" bridge endpoint: a browser, as a microphone + speaker,
// over one send/recv OPUS WebRTC peer connection. A duplex participant for the
// bridge graph - it produces the browser's microphone frames (OnFrame) and consumes
// frames to play on the browser's speaker (Write).
//
// It composes the existing BrowserAudioBridge (the self-hosted page + signalling
// already used by "openai chat"), bridging its EncodedAudioFrame microphone events
// and its SendAudio to the route graph's MediaFrame, so there is no duplicated HTTP
// or page code.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 26 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Cli.Commands.Route;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Commands.Bridge;

public sealed class WebParticipant : IBridgeParticipant
{
    private readonly BrowserAudioBridge _bridge;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _framesToBrowser;
    private long _bytesToBrowser;

    public event Action<MediaFrame>? OnFrame;

    /// <summary>Raised once when the browser peer first connects (the command uses it to cue a greeting).</summary>
    public event Action? Connected;

    public long? ConnectTimeMs => null;

    public Task Completion => _completion.Task;

    public WebParticipant(int port, bool openBrowser, bool enableVideo, ILogger logger)
    {
        _bridge = new BrowserAudioBridge(port, openBrowser, logger, enableVideo);

        // The browser microphone (encoded OPUS) becomes an audio MediaFrame. The Azure agent only
        // decodes the payload (duration unused), but the openai endpoint re-sends it on its own track,
        // so carry the real frame duration (its RTP clock units) for that direction.
        _bridge.OnMicFrameReceived += frame =>
            OnFrame?.Invoke(MediaFrame.ForAudio(frame.EncodedAudio, 0, ToRtpUnits(frame), AudioCommonlyUsedFormats.OpusWebRTC));

        _bridge.OnBrowserConnected += () => Connected?.Invoke();
        _bridge.OnBrowserDisconnected += () => _completion.TrySetResult();
    }

    public Task StartAsync(CancellationToken ct) => _bridge.StartAsync(ct);

    public void Write(MediaFrame frame)
    {
        if (frame.Payload.Length == 0)
        {
            return;
        }

        // Audio always; video only reaches the browser when the page was built with a video track
        // (the agent avatar). SendVideo is a no-op if no video track was offered.
        if (frame.Kind == MediaKind.Audio)
        {
            _bridge.SendAudio(frame.DurationRtpUnits, frame.Payload);
        }
        else
        {
            _bridge.SendVideo(frame.DurationRtpUnits, frame.Payload);
        }

        Interlocked.Increment(ref _framesToBrowser);
        Interlocked.Add(ref _bytesToBrowser, frame.Payload.Length);
    }

    /// <summary>Converts a mic frame's millisecond duration to its OPUS RTP clock units.</summary>
    private static uint ToRtpUnits(EncodedAudioFrame frame) =>
        (uint)((long)frame.DurationMilliSeconds * frame.AudioFormat.RtpClockRate / 1000);

    public SinkStats GetStats() => new(_framesToBrowser, Interlocked.Read(ref _bytesToBrowser), 0);

    public string Describe() => $"web ({_bridge.Url})";

    public ValueTask DisposeAsync() => _bridge.DisposeAsync();
}
