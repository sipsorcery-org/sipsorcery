//-----------------------------------------------------------------------------
// Filename: VideoReceiver.cs
//
// Description: One browser's video. Decodes the frames the peer connection
// assembles, shows the pictures in the window, and asks the browser for a key
// frame when the decoder needs one.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created, from WebRTCClientVP8Net and WebRTCClientH264Net.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.H264;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace demo;

/// <summary>
/// Decodes one peer connection's video into the window.
/// </summary>
/// <remarks>
/// A browser sends a key frame when the video starts and then only when it's asked for one,
/// with an RTCP PLI. This asks when:
/// - the first video packet arrives, in case the start of the first key frame was lost while
///   the connection settled;
/// - RTP packets are lost, since the frames after a gap refer to a picture the decoder never got;
/// - a frame decodes to no picture, as when the decoder is waiting for a key frame;
/// - h264.net says it needs one, which it does when it has concealed damage.
/// It asks at most once every <see cref="KEY_FRAME_REQUEST_INTERVAL_MS"/>.
///
/// A decoder that throws is replaced with a new one, since it may not decode anything again:
/// vp8.net, given a damaged key frame, throws on every frame after it, key frames included.
/// </remarks>
internal sealed class VideoReceiver : IDisposable
{
    private const int KEY_FRAME_REQUEST_INTERVAL_MS = 1000;

    private const int STATUS_INTERVAL_MS = 1000;

    private readonly RTCPeerConnection _pc;
    private readonly Func<IVideoSink> _createDecoder;
    private readonly VideoWindow _window;
    private readonly Microsoft.Extensions.Logging.ILogger _logger;

    // The decoder is used on the RTP receive thread, and replaced or disposed on that thread or
    // another, so the lock keeps those apart.
    private readonly object _decodeLock = new();
    private IVideoSink _decoder;
    private VideoFormat? _format;
    private bool _disposed;

    // Pictures raised by the frame being decoded.
    private int _pictures;

    private uint _remoteSsrc;
    private ushort _lastSeq;
    private bool _haveSeq;
    private long _lastKeyFrameRequest = long.MinValue / 2;
    private int _keyFrameRequests;

    private long _framesDecoded;
    private uint _width, _height;
    private readonly Stopwatch _statusTimer = new();
    private long _framesAtLastStatus;

    /// <summary>What's received and what decodes it, as "H264 decoded by h264.net".</summary>
    public string Description { get; }

    public VideoReceiver(RTCPeerConnection pc, Func<IVideoSink> createDecoder, string description, VideoWindow window,
        Microsoft.Extensions.Logging.ILogger logger)
    {
        _pc = pc;
        _createDecoder = createDecoder;
        _window = window;
        _logger = logger;
        Description = description;

        _decoder = CreateDecoder();

        _pc.OnVideoFrameReceived += OnFrame;
        _pc.OnRtpPacketReceived += OnRtpPacket;
    }

    /// <summary>The formats the decoder takes, for the offer.</summary>
    public List<VideoFormat> VideoFormats
    {
        get
        {
            lock (_decodeLock)
            {
                return _decoder.GetVideoSinkFormats();
            }
        }
    }

    /// <summary>Sets the format the browser and this end agreed, on this decoder and any that replaces it.</summary>
    public void SetVideoFormat(VideoFormat format)
    {
        lock (_decodeLock)
        {
            _format = format;
            _decoder.SetVideoSinkFormat(format);
        }
    }

    /// <summary>Closes the peer connection, which disposes this once it's closed.</summary>
    public void Close(string reason) => _pc.Close(reason);

    private IVideoSink CreateDecoder()
    {
        var decoder = _createDecoder();

        // All three decoders raise BGR, in an array that's the handler's to keep.
        decoder.OnVideoSinkDecodedSample += OnPicture;

        if (decoder is H264NetVideoEndPoint h264)
        {
            h264.OnKeyFrameRequired += () => RequestKeyFrame("h264.net concealed damage or missed the start");
        }

        if (_format is { } format)
        {
            decoder.SetVideoSinkFormat(format);
        }

        return decoder;
    }

    private void OnFrame(IPEndPoint remoteEndPoint, uint timestamp, byte[] frame, VideoFormat format)
    {
        lock (_decodeLock)
        {
            if (_disposed)
            {
                return;
            }

            _pictures = 0;
            try
            {
                _decoder.GotVideoFrame(remoteEndPoint, timestamp, frame, format);
            }
            catch (Exception excp)
            {
                // The key frame asked for below starts the new decoder off.
                _logger.LogWarning("The decoder threw {Type}: {Message} Replacing it.", excp.GetType().Name, excp.Message);
                (_decoder as IDisposable)?.Dispose();
                _decoder = CreateDecoder();
            }

            if (_pictures == 0)
            {
                RequestKeyFrame("a frame decoded to no picture");
            }
        }
    }

    private void OnPicture(byte[] bgr, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat)
    {
        _pictures++;
        _framesDecoded++;

        if (width != _width || height != _height)
        {
            _logger.LogInformation("{Description}: {Width}x{Height}.", Description, width, height);
            (_width, _height) = (width, height);
        }

        _window.ShowFrame(bgr, (int)width, (int)height, stride);

        if (!_statusTimer.IsRunning || _statusTimer.ElapsedMilliseconds >= STATUS_INTERVAL_MS)
        {
            double fps = _statusTimer.IsRunning ? (_framesDecoded - _framesAtLastStatus) * 1000.0 / _statusTimer.ElapsedMilliseconds : 0;
            int requests = Volatile.Read(ref _keyFrameRequests);
            _window.ShowStatus($"{Description}: {width}x{height}, {fps:0} fps, {_framesDecoded} pictures, " +
                $"{requests} key frame {(requests == 1 ? "request" : "requests")}.");
            _framesAtLastStatus = _framesDecoded;
            _statusTimer.Restart();
        }
    }

    private void OnRtpPacket(IPEndPoint remoteEndPoint, SDPMediaTypesEnum media, RTPPacket packet)
    {
        if (media != SDPMediaTypesEnum.video)
        {
            return;
        }

        ushort seq = packet.Header.SequenceNumber;

        if (Interlocked.CompareExchange(ref _remoteSsrc, packet.Header.SyncSource, 0) == 0)
        {
            RequestKeyFrame("the video started");
        }
        else if (_haveSeq)
        {
            ushort step = (ushort)(seq - _lastSeq);
            if (step == 0 || step >= 0x8000)
            {
                // A duplicate, or a late packet from before the newest.
                return;
            }
            if (step > 1)
            {
                RequestKeyFrame($"{step - 1} RTP packets were lost");
            }
        }

        _lastSeq = seq;
        _haveSeq = true;
    }

    private void RequestKeyFrame(string reason)
    {
        uint remoteSsrc = Volatile.Read(ref _remoteSsrc);
        long now = Environment.TickCount64;
        long last = Interlocked.Read(ref _lastKeyFrameRequest);

        if (remoteSsrc == 0 || now - last < KEY_FRAME_REQUEST_INTERVAL_MS ||
            Interlocked.CompareExchange(ref _lastKeyFrameRequest, now, last) != last)
        {
            return;
        }

        Interlocked.Increment(ref _keyFrameRequests);
        _logger.LogDebug("Asking the browser for a key frame: {Reason}.", reason);

        // The sender SSRC has to be the one this end's RTCP reports go out under. A receive only
        // track has no SSRC of its own, and a browser ignores a PLI from SSRC 0.
        uint localSsrc = _pc.VideoRtcpSession?.Ssrc ?? 0;

        // Off the RTP receive thread, which this is called on.
        _ = Task.Run(() =>
        {
            try
            {
                _pc.SendRtcpFeedback(SDPMediaTypesEnum.video, new RTCPFeedback(localSsrc, remoteSsrc, PSFBFeedbackTypesEnum.PLI));
            }
            catch (Exception excp)
            {
                _logger.LogDebug("Couldn't send a key frame request: {Message}", excp.Message);
            }
        });
    }

    public void Dispose()
    {
        _pc.OnVideoFrameReceived -= OnFrame;
        _pc.OnRtpPacketReceived -= OnRtpPacket;

        lock (_decodeLock)
        {
            if (!_disposed)
            {
                _disposed = true;
                (_decoder as IDisposable)?.Dispose();
            }
        }
    }
}
