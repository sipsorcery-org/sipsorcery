//-----------------------------------------------------------------------------
// Filename: H264NetVideoEndPoint.cs
//
// Description: A SIPSorcery video sink for the pure C# H.264 decoder: give it
// the access units an RTP session assembles and it raises the decoded
// pictures. A video source follows once the encoder is ported.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (D7).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.H264;

/// <summary>
/// A video sink that decodes H.264 with <see cref="H264Codec"/>. Wire
/// <c>RTCPeerConnection.OnVideoFrameReceived</c> to <see cref="GotVideoFrame"/> and handle
/// <see cref="OnVideoSinkDecodedSample"/> or <see cref="OnVideoSinkDecodedSampleFaster"/>, which
/// carry BGR pictures. Handle <see cref="OnKeyFrameRequired"/> by sending the sender an RTCP PLI.
/// </summary>
public sealed class H264NetVideoEndPoint : IVideoSink, IDisposable
{
    /// <summary>The shortest time between two <see cref="OnKeyFrameRequired"/> events.</summary>
    public const int KEY_FRAME_REQUEST_INTERVAL_MS = 1000;

    /// <summary>The formats the sink decodes: H.264 Constrained Baseline, packetization mode 1.</summary>
    public static readonly List<VideoFormat> SupportedFormats = new()
    {
        new VideoFormat(VideoCodecsEnum.H264, H264Codec.H264_SUGGESTED_FORMAT_ID, VideoFormat.DEFAULT_CLOCK_RATE, H264Codec.H264_FORMAT_PARAMETERS)
    };

    private readonly MediaFormatManager<VideoFormat> _formatManager;
    private readonly H264Codec _codec = new();
    // So the first request is never held back (Environment.TickCount64 starts at 0).
    private long _lastKeyFrameRequest = -KEY_FRAME_REQUEST_INTERVAL_MS;
    private bool _isClosed;

    /// <summary>
    /// Raised for each decoded picture, as BGR with rows packed (the stride is three times the
    /// width), in an array the handler may keep.
    /// </summary>
    public event VideoSinkSampleDecodedDelegate OnVideoSinkDecodedSample;

    /// <summary>
    /// Raised for each decoded picture, as BGR in a buffer the sink re-uses, so nothing frame
    /// sized is allocated per picture. The <see cref="RawImage.Sample"/> pointer is only valid
    /// during the handler.
    /// </summary>
    public event VideoSinkSampleDecodedFasterDelegate OnVideoSinkDecodedSampleFaster;

    /// <summary>
    /// Raised when the decoder needs a key frame to recover: the stream was joined after its key
    /// frame, or data was lost or damaged and the pictures are concealed until the next one. A
    /// WebRTC receiver answers it with an RTCP PLI to the sender. Raised at most once every
    /// <see cref="KEY_FRAME_REQUEST_INTERVAL_MS"/> while the need lasts.
    /// </summary>
    public event Action OnKeyFrameRequired;

    /// <summary>Creates a sink.</summary>
    public H264NetVideoEndPoint()
    {
        _formatManager = new MediaFormatManager<VideoFormat>(SupportedFormats);
    }

    /// <summary>True when the last access unit did not decode cleanly, see <see cref="H264Codec.KeyFrameRequired"/>.</summary>
    public bool KeyFrameRequired => _codec.KeyFrameRequired;

    /// <inheritdoc/>
    public List<VideoFormat> GetVideoSinkFormats() => _formatManager.GetSourceFormats();

    /// <inheritdoc/>
    public void SetVideoSinkFormat(VideoFormat videoFormat) => _formatManager.SetSelectedFormat(videoFormat);

    /// <inheritdoc/>
    public void RestrictFormats(Func<VideoFormat, bool> filter) => _formatManager.RestrictFormats(filter);

    /// <summary>Not supported: the sink needs whole access units, which the RTP session assembles. Use <see cref="GotVideoFrame"/>.</summary>
    public void GotVideoRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, bool marker, byte[] payload) =>
        throw new NotSupportedException("The H264 video end point needs whole access units rather than RTP packets; use GotVideoFrame.");

    /// <summary>
    /// Decodes one access unit, in Annex B format, and raises the picture it decodes to.
    /// </summary>
    public void GotVideoFrame(IPEndPoint remoteEndPoint, uint timestamp, byte[] frame, VideoFormat format)
    {
        if (_isClosed || frame == null || frame.Length == 0)
        {
            return;
        }

        var arrayHandler = OnVideoSinkDecodedSample;
        var rawHandler = OnVideoSinkDecodedSampleFaster;

        if (rawHandler != null || arrayHandler == null)
        {
            // Decoded even with no subscriber, so a handler added later starts from current
            // reference pictures instead of waiting for a key frame.
            foreach (var image in _codec.DecodeVideoFaster(frame, VideoPixelFormatsEnum.Bgr, VideoCodecsEnum.H264))
            {
                rawHandler?.Invoke(image);
                arrayHandler?.Invoke(image.GetBuffer(), (uint)image.Width, (uint)image.Height, image.Stride, image.PixelFormat);
            }
        }
        else
        {
            foreach (var sample in _codec.DecodeVideo(frame, VideoPixelFormatsEnum.Bgr, VideoCodecsEnum.H264))
            {
                arrayHandler(sample.Sample, sample.Width, sample.Height, (int)sample.Width * 3, sample.PixelFormat);
            }
        }

        if (_codec.KeyFrameRequired)
        {
            long now = Environment.TickCount64;
            if (now - _lastKeyFrameRequest >= KEY_FRAME_REQUEST_INTERVAL_MS)
            {
                _lastKeyFrameRequest = now;
                OnKeyFrameRequired?.Invoke();
            }
        }
    }

    /// <inheritdoc/>
    public Task PauseVideoSink() => Task.CompletedTask;

    /// <inheritdoc/>
    public Task ResumeVideoSink() => Task.CompletedTask;

    /// <inheritdoc/>
    public Task StartVideoSink() => Task.CompletedTask;

    /// <summary>Stops decoding and releases the decoder.</summary>
    public Task CloseVideoSink()
    {
        if (!_isClosed)
        {
            _isClosed = true;
            Dispose();
        }
        return Task.CompletedTask;
    }

    /// <summary>A <see cref="MediaEndPoints"/> with this sink as its video sink.</summary>
    public MediaEndPoints ToMediaEndPoints() => new MediaEndPoints { VideoSink = this };

    /// <inheritdoc/>
    public void Dispose()
    {
        _isClosed = true;
        _codec.Dispose();
    }
}
