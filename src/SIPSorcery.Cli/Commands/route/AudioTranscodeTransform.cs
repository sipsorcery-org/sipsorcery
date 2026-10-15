//-----------------------------------------------------------------------------
// Filename: AudioTranscodeTransform.cs
//
// Description: A source decorator that transcodes a stream's audio to a target
// codec, so a G.711 SIP caller can be bridged to an Opus-only WebRTC endpoint
// (e.g. Broadcast Box). It passes video frames through untouched and converts
// audio frames whose codec differs from the target; a frame already in the
// target codec passes through unchanged (the transform is wired only when a
// transcode is actually needed, but the per-frame check keeps it safe).
//
// Audio is light enough to transcode in managed code (8 kHz mono is ~3 orders of
// magnitude less work than video, which is why the "delegate per-sample work to
// ffmpeg" rule is really about video): decode the G.711 to PCM with the library's
// AudioEncoder and re-encode to the target. For an Opus target the PCM is encoded
// as NARROWBAND Opus at the source's 8 kHz rate (no resampling) - per RFC 7587 the
// Opus RTP timestamp clock is always 48 kHz regardless of the encoder's internal
// rate, so the frame duration is reported at 48 kHz while the payload stays 8 kHz.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 23 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Commands.Route;

public sealed class AudioTranscodeTransform : ISourceNode
{
    private const int OPUS_RTP_CLOCK_RATE = 48000;       // The Opus RTP timestamp clock (RFC 7587), always 48 kHz.
    private const int FRAME_MILLISECONDS = 20;           // One Opus frame per 20 ms of audio.

    private readonly ISourceNode _inner;
    private readonly AudioFormat _targetFormat;
    private readonly ILogger _logger;
    private readonly AudioEncoder _codec = new(includeOpus: true);
    private readonly List<short> _pcmBuffer = new();
    private readonly object _lock = new();

    private bool _initialised;
    private bool _failed;
    private int _frameSamples;                            // 20 ms at the source's sample rate.
    private uint _frameDurationRtpUnits;                  // 20 ms at the Opus 48 kHz clock.
    private AudioFormat _encodeFormat;                    // Opus at the source's (narrowband) sample rate.
    private uint _timestamp;

    public event Action<MediaFrame>? OnFrame;

    public Task Completion => _inner.Completion;

    public long? ConnectTimeMs => _inner.ConnectTimeMs;

    public AudioTranscodeTransform(ISourceNode inner, AudioFormat targetFormat, ILogger logger)
    {
        _inner = inner;
        _targetFormat = targetFormat;
        _logger = logger;
    }

    public string Describe() => $"{_inner.Describe()} ->{_targetFormat.Codec.ToString().ToLowerInvariant()}";

    public async Task StartAsync(CancellationToken ct)
    {
        _inner.OnFrame += HandleInnerFrame;
        await _inner.StartAsync(ct).ConfigureAwait(false);
    }

    private void HandleInnerFrame(MediaFrame frame)
    {
        // Video and already-target-codec audio pass straight through.
        if (frame.Kind != MediaKind.Audio || frame.AudioFormat.Codec == _targetFormat.Codec || _failed)
        {
            OnFrame?.Invoke(frame);
            return;
        }

        Transcode(frame);
    }

    private void Transcode(MediaFrame frame)
    {
        short[] pcm;
        try
        {
            pcm = _codec.DecodeAudio(frame.Payload, frame.AudioFormat);
        }
        catch (Exception excp)
        {
            _logger.LogWarning("Audio transcode decode failed, audio will stop: {Error}", excp.Message);
            _failed = true;
            return;
        }

        if (pcm.Length == 0)
        {
            return;
        }

        lock (_lock)
        {
            if (!_initialised)
            {
                Initialise(frame.AudioFormat.ClockRate);
            }

            _pcmBuffer.AddRange(pcm);

            // Emit fixed 20 ms frames so any inbound packetisation (ptime) yields valid Opus frames.
            while (_pcmBuffer.Count >= _frameSamples)
            {
                var slice = _pcmBuffer.GetRange(0, _frameSamples).ToArray();
                _pcmBuffer.RemoveRange(0, _frameSamples);

                byte[] encoded;
                try
                {
                    encoded = _codec.EncodeAudio(slice, _encodeFormat);
                }
                catch (Exception excp)
                {
                    _logger.LogWarning("Audio transcode encode failed, audio will stop: {Error}", excp.Message);
                    _failed = true;
                    return;
                }

                if (encoded.Length == 0)
                {
                    continue;
                }

                _timestamp += _frameDurationRtpUnits;
                OnFrame?.Invoke(MediaFrame.ForAudio(encoded, _timestamp, _frameDurationRtpUnits, _targetFormat));
            }
        }
    }

    private void Initialise(int sourceClockRate)
    {
        _frameSamples = Math.Max(1, sourceClockRate / 1000 * FRAME_MILLISECONDS);

        if (_targetFormat.Codec == AudioCodecsEnum.OPUS)
        {
            // Encode narrowband Opus at the source rate (no resampling); the RTP clock stays 48 kHz.
            _encodeFormat = _targetFormat;
            _encodeFormat.ClockRate = sourceClockRate;
            _frameDurationRtpUnits = OPUS_RTP_CLOCK_RATE / 1000 * FRAME_MILLISECONDS;
        }
        else
        {
            // A same-rate codec change (e.g. PCMU->PCMA): the RTP clock is the codec's own rate.
            _encodeFormat = _targetFormat;
            _frameDurationRtpUnits = (uint)Math.Max(1, _targetFormat.ClockRate / 1000 * FRAME_MILLISECONDS);
        }

        _logger.LogDebug("Audio transcode started: {Source}Hz {SourceCodec} -> {TargetCodec} (20ms frames).",
            sourceClockRate, "g711", _targetFormat.Codec);
        _initialised = true;
    }

    public async ValueTask DisposeAsync()
    {
        _inner.OnFrame -= HandleInnerFrame;
        await _inner.DisposeAsync().ConfigureAwait(false);
    }
}
