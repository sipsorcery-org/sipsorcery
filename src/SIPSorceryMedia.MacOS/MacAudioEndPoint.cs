//-----------------------------------------------------------------------------
// Filename: MacAudioEndPoint.cs
//
// Description: RTP audio endpoint that uses AVFoundation for audio capture
// and rendering on macOS.
//
// This implementation is intentionally close to WindowsAudioEndPoint:
// - AVAudioEngine/AVAudioInputNode replace WaveInEvent.
// - AVAudioEngine/AVAudioPlayerNode replace WaveOutEvent.
// - Captured hardware PCM is converted to mono PCM16, resampled in managed
//   code and emitted in fixed 20 ms codec frames.
// - Decoded remote PCM16 is scheduled on AVAudioPlayerNode.
//
// License:
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

#nullable disable

using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AVFoundation;
using Foundation;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;

namespace SIPSorceryMedia.MacOS
{
    /// <summary>
    /// Audio endpoint for the default macOS microphone and output device.
    /// </summary>
    /// <remarks>
    /// The containing application must provide an NSMicrophoneUsageDescription
    /// entry in its Info.plist before microphone capture is enabled.
    ///
    /// This first implementation uses a lightweight managed linear resampler
    /// for microphone capture. It is suitable for initial testing but can later
    /// be replaced by AVAudioConverter for production-grade resampling.
    /// </remarks>
    public sealed class MacAudioEndPoint : IAudioEndPoint, IDisposable
    {
        private const int DEFAULT_DEVICE_CHANNELS = 1;
        private const int AUDIO_FRAME_MILLISECONDS = 20;
        private const int DEFAULT_CAPTURE_TAP_FRAMES = 960;
        private const int MAX_QUEUED_PLAYBACK_MILLISECONDS = 500;

        /// <summary>
        /// Default codec-side source sample rate used before SDP negotiation.
        /// The physical microphone remains at its native macOS rate.
        /// </summary>
        public static readonly AudioSamplingRatesEnum DefaultAudioSourceSamplingRate =
            AudioSamplingRatesEnum.Rate8KHz;

        /// <summary>
        /// Default codec-side playback rate used before SDP negotiation.
        /// AVAudioEngine converts it to the physical output device rate.
        /// </summary>
        public static readonly AudioSamplingRatesEnum DefaultAudioPlaybackRate =
            AudioSamplingRatesEnum.Rate8KHz;

        private static ILogger logger =
            SIPSorcery.LogFactory.CreateLogger<MacAudioEndPoint>();

        private readonly object _sourceLock = new object();
        private readonly object _sinkLock = new object();

        private readonly IAudioEncoder _audioEncoder;
        private readonly MediaFormatManager<AudioFormat> _audioFormatManager;
        private readonly bool _disableSource;
        private readonly bool _disableSink;

        // Playback.
        private AVAudioEngine _playbackEngine;
        private AVAudioPlayerNode _playerNode;
        private AVAudioFormat _playbackFormat;
        private int _playbackSampleRate;
        private int _playbackChannels;
        private long _queuedPlaybackFrames;

        // Capture.
        private AVAudioEngine _captureEngine;
        private AVAudioInputNode _inputNode;
        private AVAudioFormat _captureNativeFormat;
        private readonly List<short> _captureFrameBuffer = new List<short>();

        protected bool _isAudioSourceStarted;
        protected bool _isAudioSinkStarted;
        protected bool _isAudioSourcePaused;
        protected bool _isAudioSinkPaused;
        protected bool _isAudioSourceClosed;
        protected bool _isAudioSinkClosed;

        private bool _disposed;

        /// <summary>
        /// Obsolete. Use <see cref="OnAudioSourceEncodedFrameReady"/> instead.
        /// </summary>
        public event EncodedSampleDelegate OnAudioSourceEncodedSample;

        /// <summary>
        /// Raised whenever an encoded audio frame is ready for RTP transport.
        /// </summary>
        public event Action<EncodedAudioFrame> OnAudioSourceEncodedFrameReady;

        /// <summary>
        /// This endpoint emits encoded samples only.
        /// </summary>
        [Obsolete("The audio source only generates encoded samples.")]
        public event RawAudioSampleDelegate OnAudioSourceRawSample
        {
            add { }
            remove { }
        }

        public event SourceErrorDelegate OnAudioSourceError;
        public event SourceErrorDelegate OnAudioSinkError;

        /// <summary>
        /// Creates a macOS endpoint using the default input and output devices.
        /// </summary>
        /// <param name="audioEncoder">Encoder used for negotiated SIP codecs.</param>
        /// <param name="disableSource">Disable microphone capture.</param>
        /// <param name="disableSink">Disable speaker playback.</param>
        public MacAudioEndPoint(
            IAudioEncoder audioEncoder,
            bool disableSource = false,
            bool disableSink = false)
        {
            if (audioEncoder == null)
            {
                throw new ArgumentNullException(nameof(audioEncoder));
            }

            logger = SIPSorcery.LogFactory.CreateLogger<MacAudioEndPoint>();

            _audioEncoder = audioEncoder;
            _audioFormatManager =
                new MediaFormatManager<AudioFormat>(audioEncoder.SupportedFormats);

            _disableSource = disableSource;
            _disableSink = disableSink;

            if (!_disableSink)
            {
                InitPlaybackDevice(
                    (int)DefaultAudioPlaybackRate,
                    DEFAULT_DEVICE_CHANNELS);

                if (audioEncoder.SupportedFormats != null &&
                    audioEncoder.SupportedFormats.Count == 1)
                {
                    SetAudioSinkFormat(audioEncoder.SupportedFormats[0]);
                }
            }

            if (!_disableSource)
            {
                InitCaptureDevice();

                if (audioEncoder.SupportedFormats != null &&
                    audioEncoder.SupportedFormats.Count == 1)
                {
                    SetAudioSourceFormat(audioEncoder.SupportedFormats[0]);
                }
            }
        }

        public void RestrictFormats(Func<AudioFormat, bool> filter)
        {
            _audioFormatManager.RestrictFormats(filter);
        }

        public List<AudioFormat> GetAudioSourceFormats()
        {
            return _audioFormatManager.GetSourceFormats();
        }

        public List<AudioFormat> GetAudioSinkFormats()
        {
            return _audioFormatManager.GetSourceFormats();
        }

        public bool HasEncodedAudioSubscribers()
        {
            return OnAudioSourceEncodedSample != null ||
                   OnAudioSourceEncodedFrameReady != null;
        }

        public bool IsAudioSourcePaused()
        {
            return _isAudioSourcePaused;
        }

        public bool IsAudioSinkPaused()
        {
            return _isAudioSinkPaused;
        }

        public void ExternalAudioSourceRawSample(
            AudioSamplingRatesEnum samplingRate,
            uint durationMilliseconds,
            short[] sample)
        {
            throw new NotImplementedException();
        }

        public void SetAudioSourceFormat(AudioFormat audioFormat)
        {
            _audioFormatManager.SetSelectedFormat(audioFormat);

            lock (_sourceLock)
            {
                // A new negotiated format can have a different rate/channel count.
                // Do not mix samples collected for the previous codec frame shape.
                _captureFrameBuffer.Clear();
            }

            logger.LogDebug(
                "macOS audio endpoint selected capture format {Codec} at {Rate} Hz with {Channels} channel(s).",
                audioFormat.ToString(),
                audioFormat.ClockRate,
                audioFormat.ChannelCount);
        }

        public void SetAudioSinkFormat(AudioFormat audioFormat)
        {
            _audioFormatManager.SetSelectedFormat(audioFormat);

            if (_disableSink)
            {
                return;
            }

            int channels = Math.Max(1, audioFormat.ChannelCount);
            int sampleRate = audioFormat.ClockRate;

            if (_playbackFormat == null ||
                _playbackSampleRate != sampleRate ||
                _playbackChannels != channels)
            {
                logger.LogDebug(
                    "macOS audio endpoint adjusting playback format to {Rate} Hz with {Channels} channel(s).",
                    sampleRate,
                    channels);

                bool restart = _isAudioSinkStarted && !_isAudioSinkPaused;
                InitPlaybackDevice(sampleRate, channels);

                if (restart)
                {
                    StartPlaybackDevice();
                }
            }
        }

        public MediaEndPoints ToMediaEndPoints()
        {
            return new MediaEndPoints
            {
                AudioSource = _disableSource ? null : this,
                AudioSink = _disableSink ? null : this,
            };
        }

        /// <summary>
        /// Starts capture and playback.
        /// </summary>
        public Task Start()
        {
            if (!_disableSource && !_isAudioSourceStarted)
            {
                StartAudio();
            }

            if (!_disableSink && !_isAudioSinkStarted)
            {
                StartAudioSink();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Closes capture and playback.
        /// </summary>
        public Task Close()
        {
            if (!_disableSource && !_isAudioSourceClosed)
            {
                CloseAudio();
            }

            if (!_disableSink && !_isAudioSinkClosed)
            {
                CloseAudioSink();
            }

            return Task.CompletedTask;
        }

        public Task Pause()
        {
            if (!_disableSource && !_isAudioSourcePaused)
            {
                PauseAudio();
            }

            if (!_disableSink && !_isAudioSinkPaused)
            {
                PauseAudioSink();
            }

            return Task.CompletedTask;
        }

        public Task Resume()
        {
            if (!_disableSource && _isAudioSourcePaused)
            {
                ResumeAudio();
            }

            if (!_disableSink && _isAudioSinkPaused)
            {
                ResumeAudioSink();
            }

            return Task.CompletedTask;
        }

        private void InitPlaybackDevice(int sampleRate, int channels)
        {
            lock (_sinkLock)
            {
                try
                {
                    DisposePlaybackDevice();

                    _playbackSampleRate = sampleRate;
                    _playbackChannels = Math.Max(1, channels);
                    _queuedPlaybackFrames = 0;

                    _playbackEngine = new AVAudioEngine();
                    _playerNode = new AVAudioPlayerNode();
                    _playbackFormat = new AVAudioFormat(
                        AVAudioCommonFormat.PCMInt16,
                        sampleRate,
                        (uint)_playbackChannels,
                        true);

                    _playbackEngine.AttachNode(_playerNode);

                    // The player consumes the codec PCM format. AVAudioEngine
                    // performs conversion to the physical output device format.
                    _playbackEngine.Connect(
                        _playerNode,
                        _playbackEngine.MainMixerNode,
                        _playbackFormat);

                    _playbackEngine.Prepare();
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        0,
                        exception,
                        "MacAudioEndPoint failed to initialise the playback device.");

                    OnAudioSinkError?.Invoke(
                        "MacAudioEndPoint failed to initialise the playback device. " +
                        exception.Message);
                }
            }
        }

        private void InitCaptureDevice()
        {
            lock (_sourceLock)
            {
                try
                {
                    DisposeCaptureDevice();

                    _captureEngine = new AVAudioEngine();

                    // Accessing InputNode requires NSMicrophoneUsageDescription
                    // in the containing application's Info.plist.
                    _inputNode = _captureEngine.InputNode;
                    _captureNativeFormat = _inputNode.GetBusOutputFormat(0);

                    if (_captureNativeFormat == null ||
                        _captureNativeFormat.SampleRate <= 0 ||
                        _captureNativeFormat.ChannelCount == 0)
                    {
                        throw new InvalidOperationException(
                            "The default macOS microphone returned an invalid audio format.");
                    }

                    _inputNode.InstallTapOnBus(
                        0,
                        DEFAULT_CAPTURE_TAP_FRAMES,
                        _captureNativeFormat,
                        LocalAudioSampleAvailable);

                    _captureEngine.Prepare();

                    logger.LogDebug(
                        "macOS microphone initialised at {Rate} Hz, {Channels} channel(s), {Format}, interleaved={Interleaved}.",
                        _captureNativeFormat.SampleRate,
                        _captureNativeFormat.ChannelCount,
                        _captureNativeFormat.CommonFormat,
                        _captureNativeFormat.Interleaved);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        0,
                        exception,
                        "MacAudioEndPoint failed to initialise the capture device.");

                    OnAudioSourceError?.Invoke(
                        "MacAudioEndPoint failed to initialise the capture device. " +
                        exception.Message);
                }
            }
        }

        /// <summary>
        /// Callback invoked by AVAudioEngine with microphone PCM.
        /// </summary>
        private void LocalAudioSampleAvailable(
            AVAudioPcmBuffer buffer,
            AVAudioTime when)
        {
            try
            {
                if (_isAudioSourceClosed ||
                    _isAudioSourcePaused ||
                    !_isAudioSourceStarted ||
                    buffer == null ||
                    buffer.FrameLength == 0)
                {
                    return;
                }

                AudioFormat selectedFormat = _audioFormatManager.SelectedFormat;

                if (selectedFormat.IsEmpty())
                {
                    return;
                }

                short[] nativeMono = ReadBufferAsMonoPcm16(buffer);

                if (nativeMono.Length == 0)
                {
                    return;
                }

                int sourceRate = (int)Math.Round(buffer.Format.SampleRate);
                int targetRate = selectedFormat.ClockRate;
                int targetChannels = Math.Max(1, selectedFormat.ChannelCount);

                short[] resampledMono =
                    ResampleMonoLinear(nativeMono, sourceRate, targetRate);

                short[] codecPcm =
                    InterleaveMono(resampledMono, targetChannels);

                AppendAndEmitCodecFrames(
                    codecPcm,
                    targetRate,
                    targetChannels,
                    selectedFormat);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    0,
                    exception,
                    "MacAudioEndPoint failed to process a microphone buffer.");

                OnAudioSourceError?.Invoke(
                    "MacAudioEndPoint failed to process a microphone buffer. " +
                    exception.Message);
            }
        }

        private void AppendAndEmitCodecFrames(
            short[] pcm,
            int sampleRate,
            int channels,
            AudioFormat audioFormat)
        {
            int samplesPerFrame =
                Math.Max(1, sampleRate * channels * AUDIO_FRAME_MILLISECONDS / 1000);

            lock (_sourceLock)
            {
                _captureFrameBuffer.AddRange(pcm);

                while (_captureFrameBuffer.Count >= samplesPerFrame)
                {
                    short[] frame = _captureFrameBuffer.GetRange(0, samplesPerFrame).ToArray();
                    _captureFrameBuffer.RemoveRange(0, samplesPerFrame);

                    byte[] encodedSample =
                        _audioEncoder.EncodeAudio(frame, audioFormat);

                    OnAudioSourceEncodedSample?.Invoke(
                        (uint)encodedSample.Length,
                        encodedSample);

                    OnAudioSourceEncodedFrameReady?.Invoke(
                        new EncodedAudioFrame(
                            0,
                            audioFormat,
                            AUDIO_FRAME_MILLISECONDS,
                            encodedSample));
                }
            }
        }

        /// <summary>
        /// Receives already-decoded little-endian PCM16 bytes.
        /// </summary>
        public void GotAudioSample(byte[] pcmSample)
        {
            if (pcmSample == null || pcmSample.Length < sizeof(short))
            {
                return;
            }

            short[] samples = new short[pcmSample.Length / sizeof(short)];
            Buffer.BlockCopy(
                pcmSample,
                0,
                samples,
                0,
                samples.Length * sizeof(short));

            QueuePlayback(samples);
        }

        /// <summary>
        /// Obsolete RTP payload handler.
        /// </summary>
        [Obsolete("Use GotEncodedMediaFrame instead.")]
        public void GotAudioRtp(
            IPEndPoint remoteEndPoint,
            uint ssrc,
            uint seqnum,
            uint timestamp,
            int payloadID,
            bool marker,
            byte[] payload)
        {
            if (_disableSink ||
                payload == null ||
                _audioEncoder == null ||
                _audioFormatManager.SelectedFormat.IsEmpty())
            {
                return;
            }

            short[] pcm =
                _audioEncoder.DecodeAudio(
                    payload,
                    _audioFormatManager.SelectedFormat);

            QueuePlayback(pcm);
        }

        /// <summary>
        /// Receives an encoded frame from the remote call party.
        /// </summary>
        public void GotEncodedMediaFrame(EncodedAudioFrame encodedMediaFrame)
        {
            if (_disableSink ||
                encodedMediaFrame == null ||
                _audioEncoder == null)
            {
                return;
            }

            AudioFormat audioFormat = encodedMediaFrame.AudioFormat;

            if (audioFormat.IsEmpty())
            {
                return;
            }

            if (_playbackFormat == null ||
                _playbackSampleRate != audioFormat.ClockRate ||
                _playbackChannels != Math.Max(1, audioFormat.ChannelCount))
            {
                SetAudioSinkFormat(audioFormat);
            }

            short[] pcm =
                _audioEncoder.DecodeAudio(
                    encodedMediaFrame.EncodedAudio,
                    audioFormat);

            QueuePlayback(pcm);
        }

        private void QueuePlayback(short[] pcm)
        {
            if (pcm == null ||
                pcm.Length == 0 ||
                _isAudioSinkClosed ||
                _isAudioSinkPaused)
            {
                return;
            }

            lock (_sinkLock)
            {
                if (_playerNode == null || _playbackFormat == null)
                {
                    return;
                }

                int channels = Math.Max(1, _playbackChannels);
                int frameCount = pcm.Length / channels;

                if (frameCount <= 0)
                {
                    return;
                }

                long maxFrames =
                    (long)_playbackSampleRate *
                    MAX_QUEUED_PLAYBACK_MILLISECONDS /
                    1000;

                // Drop new audio rather than letting latency grow without bound.
                if (_queuedPlaybackFrames + frameCount > maxFrames)
                {
                    logger.LogDebug(
                        "MacAudioEndPoint dropped a playback frame because the queue exceeded {MaximumMilliseconds} ms.",
                        MAX_QUEUED_PLAYBACK_MILLISECONDS);
                    return;
                }

                AVAudioPcmBuffer buffer =
                    new AVAudioPcmBuffer(
                        _playbackFormat,
                        (uint)frameCount);

                buffer.FrameLength = (uint)frameCount;

                IntPtr channelPointerArray = buffer.Int16ChannelData;
                if (channelPointerArray == IntPtr.Zero)
                {
                    buffer.Dispose();
                    throw new InvalidOperationException(
                        "AVAudioPcmBuffer did not expose PCMInt16 channel data.");
                }

                // For an interleaved AVAudioPcmBuffer the channel-data array
                // contains one pointer to all interleaved samples.
                IntPtr samplesPointer =
                    Marshal.ReadIntPtr(channelPointerArray);

                if (samplesPointer == IntPtr.Zero)
                {
                    buffer.Dispose();
                    throw new InvalidOperationException(
                        "AVAudioPcmBuffer returned an empty PCM sample pointer.");
                }

                Marshal.Copy(
                    pcm,
                    0,
                    samplesPointer,
                    frameCount * channels);

                _queuedPlaybackFrames += frameCount;

                _playerNode.ScheduleBuffer(
                    buffer,
                    () =>
                    {
                        lock (_sinkLock)
                        {
                            _queuedPlaybackFrames =
                                Math.Max(0, _queuedPlaybackFrames - frameCount);
                        }

                        buffer.Dispose();
                    });

                if (_isAudioSinkStarted &&
                    !_isAudioSinkPaused &&
                    !_playerNode.Playing)
                {
                    _playerNode.Play();
                }
            }
        }

        public Task PauseAudioSink()
        {
            lock (_sinkLock)
            {
                _isAudioSinkPaused = true;
                _playerNode?.Pause();
            }

            return Task.CompletedTask;
        }

        public Task ResumeAudioSink()
        {
            lock (_sinkLock)
            {
                _isAudioSinkPaused = false;

                if (!_isAudioSinkClosed)
                {
                    StartPlaybackDevice();
                }
            }

            return Task.CompletedTask;
        }

        public Task StartAudioSink()
        {
            lock (_sinkLock)
            {
                if (!_isAudioSinkStarted && !_isAudioSinkClosed)
                {
                    _isAudioSinkStarted = true;
                    _isAudioSinkPaused = false;
                    StartPlaybackDevice();
                }
            }

            return Task.CompletedTask;
        }

        public Task CloseAudioSink()
        {
            lock (_sinkLock)
            {
                if (!_isAudioSinkClosed)
                {
                    _isAudioSinkClosed = true;
                    _isAudioSinkStarted = false;
                    _isAudioSinkPaused = false;
                    DisposePlaybackDevice();
                }
            }

            return Task.CompletedTask;
        }

        private void StartPlaybackDevice()
        {
            if (_playbackEngine == null || _playerNode == null)
            {
                return;
            }

            if (!_playbackEngine.Running)
            {
                if (!_playbackEngine.StartAndReturnError(out NSError error))
                {
                    string message =
                        error?.LocalizedDescription ??
                        "Unknown AVAudioEngine playback error.";

                    OnAudioSinkError?.Invoke(
                        "MacAudioEndPoint failed to start playback. " + message);
                    return;
                }
            }

            if (!_playerNode.Playing)
            {
                _playerNode.Play();
            }
        }

        /// <summary>
        /// Pauses microphone capture.
        /// </summary>
        public Task PauseAudio()
        {
            lock (_sourceLock)
            {
                _isAudioSourcePaused = true;
                _captureEngine?.Pause();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Resumes microphone capture.
        /// </summary>
        public Task ResumeAudio()
        {
            lock (_sourceLock)
            {
                _isAudioSourcePaused = false;

                if (!_isAudioSourceClosed)
                {
                    StartCaptureDevice();
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Starts microphone capture.
        /// </summary>
        public Task StartAudio()
        {
            lock (_sourceLock)
            {
                if (!_isAudioSourceStarted && !_isAudioSourceClosed)
                {
                    _isAudioSourceStarted = true;
                    _isAudioSourcePaused = false;
                    StartCaptureDevice();
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Closes microphone capture.
        /// </summary>
        public Task CloseAudio()
        {
            lock (_sourceLock)
            {
                if (!_isAudioSourceClosed)
                {
                    _isAudioSourceClosed = true;
                    _isAudioSourceStarted = false;
                    _isAudioSourcePaused = false;
                    _captureFrameBuffer.Clear();
                    DisposeCaptureDevice();
                }
            }

            return Task.CompletedTask;
        }

        private void StartCaptureDevice()
        {
            if (_captureEngine == null)
            {
                return;
            }

            if (!_captureEngine.Running)
            {
                if (!_captureEngine.StartAndReturnError(out NSError error))
                {
                    string message =
                        error?.LocalizedDescription ??
                        "Unknown AVAudioEngine capture error.";

                    OnAudioSourceError?.Invoke(
                        "MacAudioEndPoint failed to start capture. " + message);
                }
            }
        }

        private void DisposePlaybackDevice()
        {
            try
            {
                _playerNode?.Stop();
                _playbackEngine?.Stop();

                if (_playbackEngine != null && _playerNode != null)
                {
                    _playbackEngine.DisconnectNodeOutput(_playerNode);
                    _playbackEngine.DetachNode(_playerNode);
                }
            }
            catch (Exception exception)
            {
                logger.LogDebug(
                    exception,
                    "MacAudioEndPoint encountered an error while closing playback.");
            }
            finally
            {
                _queuedPlaybackFrames = 0;

                _playbackFormat?.Dispose();
                _playbackFormat = null;

                _playerNode?.Dispose();
                _playerNode = null;

                _playbackEngine?.Dispose();
                _playbackEngine = null;
            }
        }

        private void DisposeCaptureDevice()
        {
            try
            {
                _inputNode?.RemoveTapOnBus(0);
                _captureEngine?.Stop();
            }
            catch (Exception exception)
            {
                logger.LogDebug(
                    exception,
                    "MacAudioEndPoint encountered an error while closing capture.");
            }
            finally
            {
                _captureNativeFormat?.Dispose();
                _captureNativeFormat = null;

                // InputNode is owned by AVAudioEngine. Do not dispose it
                // independently before disposing its engine.
                _inputNode = null;

                _captureEngine?.Dispose();
                _captureEngine = null;
            }
        }

        private static short[] ReadBufferAsMonoPcm16(AVAudioPcmBuffer buffer)
        {
            AVAudioFormat format = buffer.Format;
            int frames = checked((int)buffer.FrameLength);
            int channels = checked((int)format.ChannelCount);

            if (frames == 0 || channels == 0)
            {
                return Array.Empty<short>();
            }

            switch (format.CommonFormat)
            {
                case AVAudioCommonFormat.PCMFloat32:
                    return ReadFloat32AsMono(buffer, frames, channels, format.Interleaved);

                case AVAudioCommonFormat.PCMFloat64:
                    // AVAudioPcmBuffer does not expose a Float64 channel-data
                    // pointer in the current .NET macOS bindings. The default
                    // macOS input format is normally PCMFloat32.
                    throw new NotSupportedException(
                        "PCMFloat64 microphone input is not supported by this initial endpoint.");

                case AVAudioCommonFormat.PCMInt16:
                    return ReadInt16AsMono(buffer, frames, channels, format.Interleaved);

                case AVAudioCommonFormat.PCMInt32:
                    return ReadInt32AsMono(buffer, frames, channels, format.Interleaved);

                default:
                    throw new NotSupportedException(
                        "Unsupported macOS microphone PCM format: " +
                        format.CommonFormat + ".");
            }
        }

        private static short[] ReadFloat32AsMono(
            AVAudioPcmBuffer buffer,
            int frames,
            int channels,
            bool interleaved)
        {
            IntPtr root = buffer.FloatChannelData;
            if (root == IntPtr.Zero)
            {
                return Array.Empty<short>();
            }

            short[] mono = new short[frames];

            if (interleaved)
            {
                float[] samples = new float[frames * channels];
                Marshal.Copy(Marshal.ReadIntPtr(root), samples, 0, samples.Length);

                for (int frame = 0; frame < frames; frame++)
                {
                    double sum = 0;
                    int offset = frame * channels;

                    for (int channel = 0; channel < channels; channel++)
                    {
                        sum += samples[offset + channel];
                    }

                    mono[frame] = FloatToInt16(sum / channels);
                }
            }
            else
            {
                float[][] channelSamples = new float[channels][];

                for (int channel = 0; channel < channels; channel++)
                {
                    channelSamples[channel] = new float[frames];
                    IntPtr channelPointer =
                        Marshal.ReadIntPtr(root, channel * IntPtr.Size);
                    Marshal.Copy(
                        channelPointer,
                        channelSamples[channel],
                        0,
                        frames);
                }

                for (int frame = 0; frame < frames; frame++)
                {
                    double sum = 0;

                    for (int channel = 0; channel < channels; channel++)
                    {
                        sum += channelSamples[channel][frame];
                    }

                    mono[frame] = FloatToInt16(sum / channels);
                }
            }

            return mono;
        }

        private static short[] ReadInt16AsMono(
            AVAudioPcmBuffer buffer,
            int frames,
            int channels,
            bool interleaved)
        {
            IntPtr root = buffer.Int16ChannelData;
            if (root == IntPtr.Zero)
            {
                return Array.Empty<short>();
            }

            short[] mono = new short[frames];

            if (interleaved)
            {
                short[] samples = new short[frames * channels];
                Marshal.Copy(Marshal.ReadIntPtr(root), samples, 0, samples.Length);

                for (int frame = 0; frame < frames; frame++)
                {
                    long sum = 0;
                    int offset = frame * channels;

                    for (int channel = 0; channel < channels; channel++)
                    {
                        sum += samples[offset + channel];
                    }

                    mono[frame] = (short)(sum / channels);
                }
            }
            else
            {
                short[][] channelSamples = new short[channels][];

                for (int channel = 0; channel < channels; channel++)
                {
                    channelSamples[channel] = new short[frames];
                    IntPtr channelPointer =
                        Marshal.ReadIntPtr(root, channel * IntPtr.Size);
                    Marshal.Copy(
                        channelPointer,
                        channelSamples[channel],
                        0,
                        frames);
                }

                for (int frame = 0; frame < frames; frame++)
                {
                    long sum = 0;

                    for (int channel = 0; channel < channels; channel++)
                    {
                        sum += channelSamples[channel][frame];
                    }

                    mono[frame] = (short)(sum / channels);
                }
            }

            return mono;
        }

        private static short[] ReadInt32AsMono(
            AVAudioPcmBuffer buffer,
            int frames,
            int channels,
            bool interleaved)
        {
            IntPtr root = buffer.Int32ChannelData;
            if (root == IntPtr.Zero)
            {
                return Array.Empty<short>();
            }

            short[] mono = new short[frames];

            if (interleaved)
            {
                int[] samples = new int[frames * channels];
                Marshal.Copy(Marshal.ReadIntPtr(root), samples, 0, samples.Length);

                for (int frame = 0; frame < frames; frame++)
                {
                    long sum = 0;
                    int offset = frame * channels;

                    for (int channel = 0; channel < channels; channel++)
                    {
                        sum += samples[offset + channel] >> 16;
                    }

                    mono[frame] = ClampToInt16(sum / channels);
                }
            }
            else
            {
                int[][] channelSamples = new int[channels][];

                for (int channel = 0; channel < channels; channel++)
                {
                    channelSamples[channel] = new int[frames];
                    IntPtr channelPointer =
                        Marshal.ReadIntPtr(root, channel * IntPtr.Size);
                    Marshal.Copy(
                        channelPointer,
                        channelSamples[channel],
                        0,
                        frames);
                }

                for (int frame = 0; frame < frames; frame++)
                {
                    long sum = 0;

                    for (int channel = 0; channel < channels; channel++)
                    {
                        sum += channelSamples[channel][frame] >> 16;
                    }

                    mono[frame] = ClampToInt16(sum / channels);
                }
            }

            return mono;
        }

        private static short FloatToInt16(double value)
        {
            double clamped = Math.Max(-1.0, Math.Min(1.0, value));
            return ClampToInt16(
                (long)Math.Round(clamped * short.MaxValue));
        }

        private static short ClampToInt16(long value)
        {
            if (value > short.MaxValue)
            {
                return short.MaxValue;
            }

            if (value < short.MinValue)
            {
                return short.MinValue;
            }

            return (short)value;
        }

        private static short[] ResampleMonoLinear(
            short[] source,
            int sourceRate,
            int targetRate)
        {
            if (source.Length == 0)
            {
                return Array.Empty<short>();
            }

            if (sourceRate <= 0 || targetRate <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sourceRate),
                    "Audio sample rates must be positive.");
            }

            if (sourceRate == targetRate)
            {
                return source;
            }

            int targetLength = Math.Max(
                1,
                (int)Math.Round(
                    source.Length * targetRate / (double)sourceRate));

            short[] target = new short[targetLength];
            double sourceStep = sourceRate / (double)targetRate;

            for (int index = 0; index < targetLength; index++)
            {
                double sourcePosition = index * sourceStep;
                int lowerIndex = (int)sourcePosition;
                int upperIndex = Math.Min(lowerIndex + 1, source.Length - 1);
                double fraction = sourcePosition - lowerIndex;

                double sample =
                    source[lowerIndex] +
                    (source[upperIndex] - source[lowerIndex]) * fraction;

                target[index] = ClampToInt16((long)Math.Round(sample));
            }

            return target;
        }

        private static short[] InterleaveMono(short[] mono, int channels)
        {
            if (channels <= 1)
            {
                return mono;
            }

            short[] interleaved = new short[mono.Length * channels];

            for (int frame = 0; frame < mono.Length; frame++)
            {
                int offset = frame * channels;

                for (int channel = 0; channel < channels; channel++)
                {
                    interleaved[offset + channel] = mono[frame];
                }
            }

            return interleaved;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            CloseAudio();
            CloseAudioSink();

            GC.SuppressFinalize(this);
        }
    }
}
