//-----------------------------------------------------------------------------
// Filename: AudioScopeVideoSource.cs
//
// Description: Presents the audio scope as an IVideoSource so it can be plugged
// into a media pipeline anywhere a camera or test pattern would go. It is the
// audio scope equivalent of VideoTestPatternSource: a frame clock, an optional
// encoder and the three sample events, with the actual drawing delegated to
// AudioScopeRenderer.
//
// Unlike a camera this source is driven by audio, so the application pushes
// encoded audio in with PushAudio and the scope renders whatever arrived most
// recently. That is the one method beyond the IVideoSource contract.
//
// Which event a consumer subscribes to decides what happens to the frames, and
// both can be live at once:
//  - OnVideoSourceRawSample        -> a byte[] frame, for drawing locally.
//  - OnVideoSourceRawSampleFaster  -> a RawImage, for handing to an encoder.
//  - OnVideoSourceEncodedSample    -> encoded frames, when an encoder was supplied.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 20 Sep 2026  Aaron Clauson   Created, Dublin, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;

namespace AudioScope
{
    /// <summary>
    /// An <see cref="IVideoSource"/> that renders the audio scope. Audio is pushed in with
    /// <see cref="PushAudio"/> and frames come out on the usual video source events.
    /// </summary>
    public class AudioScopeVideoSource : IVideoSource, IDisposable
    {
        private const int VIDEO_SAMPLING_RATE = 90000;
        private const int VP8_SUGGESTED_FORMAT_ID = 96;
        private const int H264_SUGGESTED_FORMAT_ID = 100;

        // Frames are rendered on this clock rather than inside the audio callback. Encoding a
        // 640x480 frame takes several milliseconds, and doing that synchronously on the audio pacing
        // thread pushes audio packets past their 20ms budget, which makes the received audio choppy.
        // 33ms is ~30fps.
        public const int DEFAULT_FRAME_INTERVAL_MS = 33;

        public static readonly List<VideoFormat> SupportedFormats = new List<VideoFormat>
        {
            new VideoFormat(VideoCodecsEnum.VP8, VP8_SUGGESTED_FORMAT_ID, VIDEO_SAMPLING_RATE),
            new VideoFormat(VideoCodecsEnum.H264, H264_SUGGESTED_FORMAT_ID, VIDEO_SAMPLING_RATE, "packetization-mode=1")
        };

        private readonly AudioScopeRenderer _renderer = new AudioScopeRenderer();
        private readonly IAudioEncoder _audioDecoder = new AudioEncoder(includeOpus: true);
        private readonly IVideoEncoder _videoEncoder;
        private readonly MediaFormatManager<VideoFormat> _formatManager;
        private readonly int _frameSpacing;

        private readonly object _pcmLock = new object();
        private short[] _latestPcm;

        // ProcessAudioSample hands back the renderer's own buffer, which the next tick overwrites.
        // The byte[] event is the one a UI subscribes to, and a UI typically blits on its dispatcher
        // after the handler has returned, so the frame is copied out first. Two buffers are
        // alternated rather than one allocated per frame, because a 900KB array every 33ms lands on
        // the large object heap and churns the GC for no reason.
        private readonly byte[][] _displayBuffers = new byte[2][];
        private int _displayBufferIndex;

        private Timer _frameTimer;
        private int _renderInProgress;
        private bool _isStarted;
        private bool _isPaused;
        private bool _isClosed;
        private bool _decodeErrorReported;

        /// <summary>
        /// Fired for each rendered frame. The buffer stays valid for two frames, so a handler that
        /// defers its work, such as one marshalling to a UI thread, has time to consume it.
        /// </summary>
        public event RawVideoSampleDelegate OnVideoSourceRawSample;

        /// <summary>
        /// As for <see cref="OnVideoSourceRawSample"/> but without a byte[] per frame. The
        /// <see cref="RawImage.Sample"/> pointer is only valid for the duration of the handler.
        /// </summary>
        public event RawVideoSampleFasterDelegate OnVideoSourceRawSampleFaster;

        /// <summary>
        /// Fired for each encoded frame, when an encoder was supplied to the constructor and a
        /// format has been negotiated.
        /// </summary>
        public event EncodedSampleDelegate OnVideoSourceEncodedSample;

        public event SourceErrorDelegate OnVideoSourceError;

        /// <summary>
        /// Creates an audio scope video source.
        /// </summary>
        /// <param name="encoder">Optional. Supply one to have the source raise
        /// <see cref="OnVideoSourceEncodedSample"/> itself. Leave it null to take raw frames and do
        /// the encoding elsewhere, for example by wiring
        /// <see cref="OnVideoSourceRawSampleFaster"/> to another end point's
        /// ExternalVideoSourceRawSampleFaster.</param>
        /// <param name="frameIntervalMilliseconds">The frame clock period.</param>
        public AudioScopeVideoSource(IVideoEncoder encoder = null, int frameIntervalMilliseconds = DEFAULT_FRAME_INTERVAL_MS)
        {
            if (frameIntervalMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frameIntervalMilliseconds), "The frame interval must be greater than zero.");
            }

            _videoEncoder = encoder;
            _formatManager = new MediaFormatManager<VideoFormat>(SupportedFormats);
            _frameSpacing = frameIntervalMilliseconds;

            _displayBuffers[0] = new byte[AudioScopeRenderer.FRAME_SIZE];
            _displayBuffers[1] = new byte[AudioScopeRenderer.FRAME_SIZE];

            _frameTimer = new Timer(GenerateFrame, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Supplies the audio the scope visualises. Decodes the sample and keeps it for the frame
        /// clock to pick up, so it stays cheap enough to call from an audio pacing or receive thread.
        /// </summary>
        /// <param name="encodedSample">The encoded audio.</param>
        /// <param name="format">The format the audio is encoded in.</param>
        public void PushAudio(byte[] encodedSample, AudioFormat? format)
        {
            if (_isClosed || encodedSample == null || format == null)
            {
                return;
            }

            try
            {
                var decoded = _audioDecoder.DecodeAudio(encodedSample, format.Value);

                lock (_pcmLock)
                {
                    _latestPcm = decoded;
                }
            }
            catch (Exception excp)
            {
                // This would repeat for every audio packet, so report it once rather than dozens of
                // times a second.
                if (!_decodeErrorReported)
                {
                    _decodeErrorReported = true;
                    OnVideoSourceError?.Invoke($"Audio scope could not decode an audio sample. {excp.Message}");
                }
            }
        }

        /// <summary>
        /// Fires on the frame clock and renders the scope from the most recently pushed audio. If the
        /// previous render is still running the tick is dropped rather than queued, so a slow encode
        /// can never build a backlog.
        /// </summary>
        private void GenerateFrame(object state)
        {
            if (_isClosed || _isPaused)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _renderInProgress, 1, 0) != 0)
            {
                return;
            }

            try
            {
                short[] pcm;
                lock (_pcmLock)
                {
                    pcm = _latestPcm;
                }

                if (pcm == null)
                {
                    // No audio has arrived yet, so there is nothing to visualise.
                    return;
                }

                bool wantsRaw = OnVideoSourceRawSample != null;
                bool wantsFaster = OnVideoSourceRawSampleFaster != null;
                bool wantsEncoded = _videoEncoder != null && OnVideoSourceEncodedSample != null && !_formatManager.SelectedFormat.IsEmpty();

                if (!wantsRaw && !wantsFaster && !wantsEncoded)
                {
                    return;
                }

                var frame = _renderer.ProcessAudioSample(pcm.Select(s => new Complex(s / 32768f, 0f)).ToArray());

                // Re-checked after the render because a tick already in flight when PauseVideo or
                // CloseVideo is called would otherwise emit one more frame. Stopping the timer only
                // prevents the next callback, not the one running.
                if (_isClosed || _isPaused)
                {
                    return;
                }

                if (wantsRaw)
                {
                    RaiseRawSample(frame);
                }

                if (wantsFaster)
                {
                    RaiseRawSampleFaster(frame);
                }

                if (wantsEncoded)
                {
                    RaiseEncodedSample(frame);
                }
            }
            catch (Exception excp)
            {
                OnVideoSourceError?.Invoke($"Audio scope render failed. {excp.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _renderInProgress, 0);
            }
        }

        /// <summary>
        /// Copies the frame out of the renderer's buffer and into one of the two display buffers, so
        /// a handler that defers its work is not reading a frame the next tick has overwritten.
        /// </summary>
        private void RaiseRawSample(byte[] frame)
        {
            _displayBufferIndex ^= 1;
            var display = _displayBuffers[_displayBufferIndex];

            Buffer.BlockCopy(frame, 0, display, 0, frame.Length);

            OnVideoSourceRawSample?.Invoke((uint)_frameSpacing,
                AudioScopeRenderer.Width,
                AudioScopeRenderer.Height,
                display,
                VideoPixelFormatsEnum.Bgr);
        }

        /// <summary>
        /// Points a <see cref="RawImage"/> at the frame for the duration of the event. The buffer is
        /// pinned only for that call, which is exactly as long as <see cref="RawImage.Sample"/> is
        /// documented to be valid.
        /// </summary>
        private void RaiseRawSampleFaster(byte[] frame)
        {
            var pinned = GCHandle.Alloc(frame, GCHandleType.Pinned);

            try
            {
                OnVideoSourceRawSampleFaster?.Invoke((uint)_frameSpacing, new RawImage
                {
                    Width = AudioScopeRenderer.Width,
                    Height = AudioScopeRenderer.Height,
                    Stride = AudioScopeRenderer.ROW_STRIDE,
                    Sample = pinned.AddrOfPinnedObject(),
                    PixelFormat = VideoPixelFormatsEnum.Bgr
                });
            }
            finally
            {
                pinned.Free();
            }
        }

        private void RaiseEncodedSample(byte[] frame)
        {
            var encodedBuffer = _videoEncoder.EncodeVideo(
                AudioScopeRenderer.Width,
                AudioScopeRenderer.Height,
                frame,
                VideoPixelFormatsEnum.Bgr,
                _formatManager.SelectedFormat.Codec);

            if (encodedBuffer != null)
            {
                uint clockRate = (uint)_formatManager.SelectedFormat.ClockRate;
                uint durationRtpTS = (uint)((ulong)clockRate * (uint)_frameSpacing / 1000);

                OnVideoSourceEncodedSample?.Invoke(durationRtpTS, encodedBuffer);
            }
        }

        public List<VideoFormat> GetVideoSourceFormats() => _formatManager.GetSourceFormats();
        public void SetVideoSourceFormat(VideoFormat videoFormat) => _formatManager.SetSelectedFormat(videoFormat);
        public void RestrictFormats(Func<VideoFormat, bool> filter) => _formatManager.RestrictFormats(filter);

        public void ForceKeyFrame() => _videoEncoder?.ForceKeyFrame();
        public bool HasEncodedVideoSubscribers() => OnVideoSourceEncodedSample != null;
        public bool IsVideoSourcePaused() => _isPaused;

        public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat) =>
            throw new NotImplementedException("The audio scope video source generates its own frames and does not encode external ones.");

        public void ExternalVideoSourceRawSampleFaster(uint durationMilliseconds, RawImage rawImage) =>
            throw new NotImplementedException("The audio scope video source generates its own frames and does not encode external ones.");

        public Task StartVideo()
        {
            if (!_isStarted && !_isClosed)
            {
                _isStarted = true;
                _frameTimer.Change(0, _frameSpacing);
            }

            return Task.CompletedTask;
        }

        public Task PauseVideo()
        {
            _isPaused = true;
            _frameTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            return Task.CompletedTask;
        }

        public Task ResumeVideo()
        {
            _isPaused = false;

            if (_isStarted && !_isClosed)
            {
                _frameTimer?.Change(0, _frameSpacing);
            }

            return Task.CompletedTask;
        }

        public Task CloseVideo()
        {
            if (!_isClosed)
            {
                _isClosed = true;
                _isStarted = false;

                _frameTimer?.Dispose();
                _frameTimer = null;

                lock (_pcmLock)
                {
                    _latestPcm = null;
                }
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            CloseVideo();
        }
    }
}
