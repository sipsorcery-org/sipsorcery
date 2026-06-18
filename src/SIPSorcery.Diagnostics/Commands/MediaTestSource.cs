//-----------------------------------------------------------------------------
// Filename: MediaTestSource.cs
//
// Description: A reusable publisher media source for the SFU/room verbs: a VP8
// video test pattern and an OPUS (or G711) music audio source, wired onto a
// peer connection. Centralises the track creation, encoded sample plumbing and
// format negotiation so the "cloudflare sfu" and "livekit room" verbs share one
// implementation.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 13 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using Vpx.Net;

namespace SIPSorcery.Diagnostics.Commands;

public sealed class MediaTestSource : IDisposable
{
    private readonly VideoTestPatternSource _videoSource;
    private readonly AudioExtrasSource _audioSource;
    private readonly ILogger _logger;
    private bool _started;

    /// <summary>
    /// Creates the test pattern (VP8) and music (OPUS) sources.
    /// </summary>
    /// <param name="opusOnly">When true the audio is pinned to OPUS. LiveKit's room pipeline and
    /// the SIP bridge require OPUS; without the pin the track negotiates G711, which browsers play
    /// but the SIP bridge relays as silence.</param>
    public MediaTestSource(bool opusOnly, ILogger logger)
    {
        _logger = logger;

        _videoSource = new VideoTestPatternSource(new VP8Codec());
        _videoSource.RestrictFormats(format => format.Codec == VideoCodecsEnum.VP8);

        _audioSource = new AudioExtrasSource(new AudioEncoder(includeOpus: true),
            new AudioSourceOptions { AudioSource = AudioSourcesEnum.Music });

        if (opusOnly)
        {
            _audioSource.RestrictFormats(format => format.Codec == AudioCodecsEnum.OPUS);
        }
    }

    /// <summary>
    /// Adds the audio and video tracks to the peer connection and wires the encoded sample
    /// plumbing and format negotiation. The media itself is not started until <see cref="StartAsync"/>.
    /// </summary>
    public void AddTracks(RTCPeerConnection pc, MediaStreamStatusEnum direction)
    {
        var videoTrack = new MediaStreamTrack(_videoSource.GetVideoSourceFormats(), direction);
        pc.addTrack(videoTrack);

        var audioTrack = new MediaStreamTrack(_audioSource.GetAudioSourceFormats(), direction);
        pc.addTrack(audioTrack);

        _videoSource.OnVideoSourceEncodedSample += pc.SendVideo;
        _audioSource.OnAudioSourceEncodedSample += pc.SendAudio;

        pc.OnVideoFormatsNegotiated += (formats) => _videoSource.SetVideoSourceFormat(formats.First());
        pc.OnAudioFormatsNegotiated += (formats) => _audioSource.SetAudioSourceFormat(formats.First());
    }

    /// <summary>
    /// Starts the media flowing. Idempotent: safe to call from a connection state handler that may
    /// fire more than once.
    /// </summary>
    public async Task StartAsync()
    {
        if (_started)
        {
            return;
        }
        _started = true;

        _logger.LogDebug("Starting test pattern video and music audio sources.");
        await _audioSource.StartAudio().ConfigureAwait(false);
        await _videoSource.StartVideo().ConfigureAwait(false);
    }

    public void Dispose()
    {
        try
        {
            _videoSource.CloseVideo().Wait(TimeSpan.FromSeconds(2));
            _audioSource.CloseAudio().Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception excp)
        {
            _logger.LogDebug("Error closing media test source: {Error}", excp.Message);
        }

        // VideoTestPatternSource is IDisposable; AudioExtrasSource is torn down via CloseAudio.
        _videoSource.Dispose();
    }
}
