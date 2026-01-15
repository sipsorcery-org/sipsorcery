//-----------------------------------------------------------------------------
// Filename: WindowsAudioSession.cs
//
// Description: Example of an RTP session that uses NAUdio for audio
// capture and rendering on Windows.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 17 Apr 2020  Aaron Clauson	Created, Dublin, Ireland.
// 01 Jun 2020  Aaron Clauson   Refactored to use RtpAudioSession base class.
// 15 Aug 2020  Aaron Clauson   Moved from examples into SIPSorceryMedia.Windows
//                              assembly.
// 21 Jan 2021  Aaron Clauson   Adjust playback rate dependent on selected audio format.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NAudio.Wave;
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SIPSorceryMedia.Windows
{
    public class WindowsAudioEndPoint : IAudioEndPoint, IDisposable
    {
        private const int DEVICE_BITS_PER_SAMPLE = 16;
        private const int DEFAULT_DEVICE_CHANNELS = 1;
        private const int INPUT_BUFFERS = 2;          // See https://github.com/sipsorcery/sipsorcery/pull/148.
        private const int DEFAULT_PLAYBACK_BUFFER_MILLISECONDS = 20;
        private const int AUDIO_INPUTDEVICE_INDEX = -1;
        private const int AUDIO_OUTPUTDEVICE_INDEX = -1;

        // Audio buffer sizing constants for optimal memory allocation
        private const int DEFAULT_AUDIO_BUFFER_SIZE = 4096;        // 4KB - optimal for typical audio frames
        private const int LARGE_AUDIO_BUFFER_SIZE = 8192;          // 8KB - for larger frames
        private const int MAX_AUDIO_BUFFER_SIZE = 16384;           // 16KB - maximum size cap

        // Adaptive sizing tracking - learns from observed buffer usage patterns
        private static int _maxObservedBufferSize = DEFAULT_AUDIO_BUFFER_SIZE;

        /// <summary>
        /// Microphone input is sampled at 8KHz.
        /// </summary>
        public readonly static AudioSamplingRatesEnum DefaultAudioSourceSamplingRate = AudioSamplingRatesEnum.Rate8KHz;

        public readonly static AudioSamplingRatesEnum DefaultAudioPlaybackRate = AudioSamplingRatesEnum.Rate8KHz;

        private ILogger logger = SIPSorcery.LogFactory.CreateLogger<WindowsAudioEndPoint>();

        private WaveFormat? _waveSinkFormat;
        private WaveFormat? _waveSourceFormat;

        private bool _isDisposed;

        /// <summary>
        /// Audio render device.
        /// </summary>
        private WaveOutEvent? _waveOutEvent;

        /// <summary>
        /// Buffer for audio samples to be rendered.
        /// </summary>
        private Sys.BufferedWaveProvider? _waveProvider;

        /// <summary>
        /// Audio capture device.
        /// </summary>
        private WaveInEvent? _waveInEvent;

        private IAudioEncoder _audioEncoder;
        private MediaFormatManager<AudioFormat> _audioFormatManager;

        private bool _disableSink;
        private int _audioOutDeviceIndex;
        private int _audioInDeviceIndex;
        private bool _disableSource;

        protected int _isAudioSourceStarted;
        protected int _isAudioSinkStarted;
        protected int _isAudioSourcePaused;
        protected int _isAudioSinkPaused;
        protected int _isAudioSourceClosed;
        protected int _isAudioSinkClosed;

        /// <summary>
        /// Obsolete. Use the <see cref="OnAudioSourceEncodedFrameReady"/> event instead.
        /// </summary>
        [Obsolete("Use OnAudioSourceEncodedFrameReady instead.")]
        public event Action<uint, ReadOnlyMemory<byte>>? OnAudioSourceEncodedSample;

        /// <summary>
        /// Event handler for when an encoded audio frame is ready to be sent to the RTP transport layer.
        /// The sample contained in this event is already encoded with the chosen audio format (codec) and ready for transmission.
        /// </summary>
        public event Action<EncodedAudioFrame>? OnAudioSourceEncodedFrameReady;

        /// <summary>
        /// This audio source DOES NOT generate raw samples. Subscribe to the encoded samples event
        /// to get samples ready for passing to the RTP transport layer.
        /// </summary>
        [Obsolete("The audio source only generates encoded samples.")]
        public event Action<AudioSamplingRatesEnum, uint, short[]> OnAudioSourceRawSample { add { } remove { } }

        public event SourceErrorDelegate? OnAudioSourceError;

        public event SourceErrorDelegate? OnAudioSinkError;

        /// <summary>
        /// Creates a new basic RTP session that captures and renders audio to/from the default system devices.
        /// </summary>
        /// <param name="audioEncoder">An audio encoder that can be used to encode and decode
        /// specific audio codecs.</param>
        /// <param name="disableSource">Set to true to disable the use of the audio source functionality, i.e.
        /// don't capture input from the microphone.</param>
        /// <param name="disableSink">Set to true to disable the use of the audio sink functionality, i.e.
        /// don't playback audio to the speaker.</param>
        public WindowsAudioEndPoint(IAudioEncoder audioEncoder,
            int audioOutDeviceIndex = AUDIO_OUTPUTDEVICE_INDEX,
            int audioInDeviceIndex = AUDIO_INPUTDEVICE_INDEX,
            bool disableSource = false,
            bool disableSink = false)
        {
            logger = SIPSorcery.LogFactory.CreateLogger<WindowsAudioEndPoint>();

            _audioFormatManager = new MediaFormatManager<AudioFormat>(audioEncoder.SupportedFormats);
            _audioEncoder = audioEncoder;

            _audioOutDeviceIndex = audioOutDeviceIndex;
            _audioInDeviceIndex = audioInDeviceIndex;
            _disableSource = disableSource;
            _disableSink = disableSink;

            if (!_disableSink)
            {
                InitPlaybackDevice(_audioOutDeviceIndex, (int)DefaultAudioPlaybackRate, DEFAULT_DEVICE_CHANNELS);

                if (audioEncoder.SupportedFormats?.Count == 1)
                {
                    SetAudioSinkFormat(audioEncoder.SupportedFormats[0]);
                }
            }

            if (!_disableSource)
            {
                InitCaptureDevice(_audioInDeviceIndex, (int)DefaultAudioSourceSamplingRate, DEFAULT_DEVICE_CHANNELS);

                if (audioEncoder.SupportedFormats?.Count == 1)
                {
                    SetAudioSourceFormat(audioEncoder.SupportedFormats[0]);
                }
            }
        }

        public void RestrictFormats(Func<AudioFormat, bool> filter)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            _audioFormatManager.RestrictFormats(filter);
        }

        public List<AudioFormat> GetAudioSourceFormats()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            return _audioFormatManager.GetSourceFormats();
        }

        public List<AudioFormat> GetAudioSinkFormats()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            return _audioFormatManager.GetSourceFormats();
        }

        public bool HasEncodedAudioSubscribers()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            return OnAudioSourceEncodedSample is { };
        }

        public bool IsAudioSourcePaused()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            return Interlocked.CompareExchange(ref _isAudioSourcePaused, 0, 0) == 1;
        }

        public bool IsAudioSinkPaused()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            return Interlocked.CompareExchange(ref _isAudioSinkPaused, 0, 0) == 1;
        }

        public void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            throw new NotImplementedException();
        }

        public void SetAudioSourceFormat(AudioFormat audioFormat)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            _audioFormatManager.SetSelectedFormat(audioFormat);

            if (!_disableSource)
            {
                var selectedFormat = _audioFormatManager.SelectedFormat;
                Debug.Assert(_waveSourceFormat is { });
                Debug.Assert(selectedFormat is { });
                if (_waveSourceFormat.SampleRate != selectedFormat.ClockRate)
                {
                    // Reinitialise the audio capture device.
                    logger.LogAudioCaptureRateAdjusted(_waveSourceFormat.SampleRate, selectedFormat.ClockRate);

                    InitCaptureDevice(_audioInDeviceIndex, selectedFormat.ClockRate, selectedFormat.ChannelCount);
                }
            }
        }

        public void SetAudioSinkFormat(AudioFormat audioFormat)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            _audioFormatManager.SetSelectedFormat(audioFormat);

            if (!_disableSink)
            {
                var selectedFormat = _audioFormatManager.SelectedFormat;
                Debug.Assert(_waveSinkFormat is { });
                Debug.Assert(selectedFormat is { });
                if (_waveSinkFormat.SampleRate != selectedFormat.ClockRate)
                {
                    // Reinitialise the audio output device.
                    logger.LogAudioPlaybackRateAdjusted(_waveSinkFormat.SampleRate, selectedFormat.ClockRate);

                    InitPlaybackDevice(_audioOutDeviceIndex, selectedFormat.ClockRate, selectedFormat.ChannelCount);
                }
            }
        }

        public MediaEndPoints ToMediaEndPoints()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            return new MediaEndPoints
            {
                AudioSource = _disableSource ? null : this,
                AudioSink = _disableSink ? null : this,
            };
        }

        /// <summary>
        /// Starts the audio capturing/source device and the audio sink device.
        /// </summary>
        public Task Start()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (Interlocked.CompareExchange(ref _isAudioSourceStarted, 0, 0) == 0 && _waveInEvent is { })
            {
                StartAudio();
            }

            if (Interlocked.CompareExchange(ref _isAudioSinkStarted, 0, 0) == 0 && _waveOutEvent is { })
            {
                StartAudioSink();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Closes the audio devices.
        /// </summary>
        public Task Close()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (Interlocked.CompareExchange(ref _isAudioSourceClosed, 0, 0) == 0 && _waveInEvent is { })
            {
                CloseAudio();
            }

            if (Interlocked.CompareExchange(ref _isAudioSinkClosed, 0, 0) == 0 && _waveOutEvent is { })
            {
                CloseAudioSink();
            }

            return Task.CompletedTask;
        }

        public Task Pause()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (Interlocked.CompareExchange(ref _isAudioSourcePaused, 0, 0) == 0 && _waveInEvent is { })
            {
                PauseAudio();
            }

            if (Interlocked.CompareExchange(ref _isAudioSinkPaused, 0, 0) == 0 && _waveOutEvent is { }
)
            {
                PauseAudioSink();
            }

            return Task.CompletedTask;
        }

        public Task Resume()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (Interlocked.CompareExchange(ref _isAudioSourcePaused, 0, 0) == 1 && _waveInEvent is { })
            {
                ResumeAudio();
            }

            if (Interlocked.CompareExchange(ref _isAudioSinkPaused, 0, 0) == 1 && _waveOutEvent is { })
            {
                ResumeAudioSink();
            }

            return Task.CompletedTask;
        }

        private void InitPlaybackDevice(int audioOutDeviceIndex, int audioSinkSampleRate, int channels)
        {
            try
            {
                // Dispose existing playback device if present
                if (_waveOutEvent is { } waveOutEvent)
                {
                    _waveOutEvent = null;
                    waveOutEvent.Stop();
                    waveOutEvent.Dispose();
                }

                _waveSinkFormat = new WaveFormat(
                    audioSinkSampleRate,
                    DEVICE_BITS_PER_SAMPLE,
                    channels);

                // Playback device.
                // For playback devices, we'll rely on WaveOutEvent's internal validation
                // since the static DeviceCount property may not be available in this NAudio version
                _waveOutEvent = new WaveOutEvent();
                _waveOutEvent.DeviceNumber = audioOutDeviceIndex;
                _waveProvider = new Sys.BufferedWaveProvider(_waveSinkFormat);
                _waveProvider.DiscardOnBufferOverflow = true;
                _waveOutEvent.Init(_waveProvider);
            }
            catch (Exception excp)
            {
                logger.LogPlaybackDeviceInitFailed(excp);
                OnAudioSinkError?.Invoke($"WindowsAudioEndPoint failed to initialise playback device. {excp.Message}");
            }
        }

        private void InitCaptureDevice(int audioInDeviceIndex, int audioSourceSampleRate, int audioSourceChannels)
        {
            if (WaveInEvent.DeviceCount > 0)
            {
                // Handle default device (-1) and validate range properly
                if (audioInDeviceIndex >= -1 && audioInDeviceIndex < WaveInEvent.DeviceCount)
                {
                    try
                    {
                        if (_waveInEvent is { } waveInEvent)
                        {
                            _waveInEvent = null;
                            waveInEvent.DataAvailable -= LocalAudioSampleAvailable;
                            waveInEvent.StopRecording();
                            waveInEvent.Dispose();
                        }

                        _waveSourceFormat = new WaveFormat(
                               audioSourceSampleRate,
                               DEVICE_BITS_PER_SAMPLE,
                               audioSourceChannels);

                        _waveInEvent = new WaveInEvent();

                        // Note NAudio recommends a buffer size of 100ms but codecs like Opus can only handle 20ms buffers.
                        _waveInEvent.BufferMilliseconds = DEFAULT_PLAYBACK_BUFFER_MILLISECONDS;

                        _waveInEvent.NumberOfBuffers = INPUT_BUFFERS;
                        _waveInEvent.DeviceNumber = audioInDeviceIndex;
                        _waveInEvent.WaveFormat = _waveSourceFormat;
                        _waveInEvent.DataAvailable += LocalAudioSampleAvailable;
                    }
                    catch (Exception ex)
                    {
                        logger.LogAudioCaptureDeviceInitFailed(audioInDeviceIndex, ex);
                        OnAudioSourceError?.Invoke($"Failed to initialize audio capture device: {ex.Message}");
                    }
                }
                else
                {
                    logger.LogAudioInputDeviceIndexExceeded(audioInDeviceIndex, WaveInEvent.DeviceCount - 1);
                    OnAudioSourceError?.Invoke($"The requested audio input device index {audioInDeviceIndex} exceeds the maximum index of {WaveInEvent.DeviceCount - 1}. Use -1 for default device.");
                }
            }
            else
            {
                logger.LogNoAudioCaptureDevices();
                OnAudioSourceError?.Invoke("No audio capture devices are available.");
            }
        }

        /// <summary>
        /// Event handler for audio sample being supplied by local capture device.
        /// </summary>
        private void LocalAudioSampleAvailable(object? sender, WaveInEventArgs args)
        {
            // Note NAudio.Wave.WaveBuffer.ShortBuffer does not take into account little endian.
            // https://github.com/naudio/NAudio/blob/master/NAudio/Wave/WaveOutputs/WaveBuffer.cs

            var pcm = MemoryMarshal.Cast<byte, short>(args.Buffer.AsSpan(0, args.BytesRecorded));

            // Use adaptive buffer sizing for optimal memory allocation
            using var buffer = new PooledSegmentedBuffer<byte>(GetOptimalBufferSize(pcm.Length));
            _audioEncoder.EncodeAudio(pcm, _audioFormatManager.SelectedFormat, buffer);

            var sequence = buffer.GetReadOnlySequence();

            var onAudioSourceEncodedSample = OnAudioSourceEncodedSample;
            var onAudioSourceEncodedFrameReady = OnAudioSourceEncodedFrameReady;

            if (!sequence.IsEmpty && (onAudioSourceEncodedSample is { } || onAudioSourceEncodedFrameReady is { }))
            {
                if (sequence.IsSingleSegment)
                {
                    var encodedMemory = sequence.First;
                    FireAudioEvents(pcm.Length, encodedMemory, _audioFormatManager.SelectedFormat, onAudioSourceEncodedSample, onAudioSourceEncodedFrameReady);
                }
                else
                {
                    // For multi-segment, consolidate into a single memory block and fire events before returning to pool
                    var totalLength = (int)sequence.Length;
                    var temp = ArrayPool<byte>.Shared.Rent(totalLength);
                    try
                    {
                        sequence.CopyTo(temp);
                        var encodedMemory = temp.AsMemory(0, totalLength);
                        FireAudioEvents(pcm.Length, encodedMemory, _audioFormatManager.SelectedFormat, onAudioSourceEncodedSample, onAudioSourceEncodedFrameReady);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(temp);
                    }
                }

                static void FireAudioEvents(
                    int pcmLength,
                    ReadOnlyMemory<byte> encodedMemory,
                    AudioFormat audioFormat,
                    Action<uint, ReadOnlyMemory<byte>>? onAudioSourceEncodedSample,
                    Action<EncodedAudioFrame>? onAudioSourceEncodedFrameReady)
                {
                    onAudioSourceEncodedSample?.Invoke((uint)encodedMemory.Length, encodedMemory);

                    if (onAudioSourceEncodedFrameReady is { })
                    {
                        var numChannels = audioFormat.ChannelCount;
                        var sampleRate = audioFormat.ClockRate;
                        var frames = pcmLength / numChannels;
                        var durationMsD = sampleRate > 0 ? (frames / (double)sampleRate) * 1000.0 : 0;
                        var durationMs = (uint)Math.Round(durationMsD);
                        var encodedFrame = new EncodedAudioFrame(0, audioFormat, durationMs, encodedMemory);
                        onAudioSourceEncodedFrameReady(encodedFrame);
                    }
                }
            }
        }

        /// <summary>
        /// Event handler for playing audio samples received from the remote call party.
        /// </summary>
        /// <param name="pcmSample">Raw PCM sample from remote party.</param>
        public void GotAudioSample(byte[] pcmSample)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            _waveProvider?.AddSamples(pcmSample.AsSpan());
        }

        /// <summary>
        /// Obsolete. Use the <cref="GotEncodedMediaFrame"/> method instead.
        /// </summary>
        [Obsolete("Use GotEncodedMediaFrame instead.")]
        public void GotAudioRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, bool marker, byte[] payload)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            ProcessDecodedAudio(payload.AsMemory(), _audioFormatManager.SelectedFormat);
        }

        /// <summary>
        /// Handler for receiving an encoded audio frame from the remote party.
        ///</summary>
        /// <param name="encodedMediaFrame">Encoded audio frame received from the remote party.</param>
        public void GotEncodedMediaFrame(EncodedAudioFrame encodedMediaFrame)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            ProcessDecodedAudio(encodedMediaFrame.EncodedAudio, encodedMediaFrame.AudioFormat);
        }

        /// <summary>
        /// Common helper method to process decoded PCM audio samples and add them to the wave provider.
        /// </summary>
        /// <param name="encodedAudio">The encoded audio data to decode.</param>
        /// <param name="audioFormat">The audio format for decoding.</param>
        private void ProcessDecodedAudio(ReadOnlyMemory<byte> encodedAudio, AudioFormat audioFormat)
        {
            if (_waveProvider is { } && _audioEncoder is { } && !audioFormat.IsEmpty())
            {
                // Use optimal buffer sizing for PCM shorts buffer (decode typically expands data 3-4x)
                var estimatedPcmShorts = encodedAudio.Length * 4; // Conservative estimate for decoded size
                using var pcmShortsBuffer = new PooledSegmentedBuffer<short>(GetOptimalBufferSize(estimatedPcmShorts));
                _audioEncoder.DecodeAudio(encodedAudio.Span, audioFormat, pcmShortsBuffer);

                var pcmSequence = pcmShortsBuffer.GetReadOnlySequence();
                if (pcmSequence.IsEmpty)
                {
                    return;
                }

                // Handle single vs multiple segments efficiently
                if (pcmSequence.IsSingleSegment)
                {
                    // Fast path: single segment - direct conversion
                    var segmentBytes = MemoryMarshal.Cast<short, byte>(pcmSequence.FirstSpan);
                    _waveProvider.AddSamples(segmentBytes);
                }
                else
                {
                    // Consolidate multiple segments to prevent audio stuttering and improve performance.
                    // Multiple small writes to the audio buffer can cause playback artifacts, so we
                    // consolidate into a single buffer for smoother audio delivery.
                    var totalShorts = (int)pcmSequence.Length;
                    var consolidatedShorts = ArrayPool<short>.Shared.Rent(totalShorts);
                    try
                    {
                        pcmSequence.CopyTo(consolidatedShorts);
                        var consolidatedBytes = MemoryMarshal.Cast<short, byte>(consolidatedShorts.AsSpan(0, totalShorts));
                        _waveProvider.AddSamples(consolidatedBytes);
                    }
                    finally
                    {
                        ArrayPool<short>.Shared.Return(consolidatedShorts);
                    }
                }
            }
        }

        public Task PauseAudioSink()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            Interlocked.Exchange(ref _isAudioSinkPaused, 1);
            _waveOutEvent?.Pause();
            return Task.CompletedTask;
        }

        public Task ResumeAudioSink()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            Interlocked.Exchange(ref _isAudioSinkPaused, 0);
            _waveOutEvent?.Play();
            return Task.CompletedTask;
        }

        public Task StartAudioSink()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (Interlocked.CompareExchange(ref _isAudioSinkStarted, 1, 0) == 0)
            {
                _waveOutEvent?.Play();
            }
            return Task.CompletedTask;
        }

        public Task CloseAudioSink()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (Interlocked.CompareExchange(ref _isAudioSinkClosed, 1, 0) == 0)
            {
                _waveOutEvent?.Stop();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Pauses the audio source. Use the <cref="Pause"/> method to pause both the audio source and sink.
        /// </summary>
        public Task PauseAudio()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            Interlocked.Exchange(ref _isAudioSourcePaused, 1);
            _waveInEvent?.StopRecording();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Resumes the audio source. Use the <cref="Resume"/> method to resume both the audio source and sink.
        /// </summary>
        public Task ResumeAudio()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            Interlocked.Exchange(ref _isAudioSourcePaused, 0);
            _waveInEvent?.StartRecording();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Starts the audio source. Use the <cref="Start"/> method to start both the audio source and sink.
        /// </summary>
        public Task StartAudio()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (Interlocked.CompareExchange(ref _isAudioSourceStarted, 1, 0) == 0)
            {
                _waveInEvent?.StartRecording();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Closes (stops) the audio source. Use the <cref="Stop"/> method to stop both the audio source and sink.
        /// </summary>
        public Task CloseAudio()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (Interlocked.CompareExchange(ref _isAudioSourceClosed, 1, 0) == 0)
            {
                if (_waveInEvent is { })
                {
                    _waveInEvent.DataAvailable -= LocalAudioSampleAvailable;
                    _waveInEvent.StopRecording();
                }
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed && disposing)
            {
                _isDisposed = true;

                if (_waveInEvent is { } waveInEvent)
                {
                    _waveInEvent = null;
                    waveInEvent.DataAvailable -= LocalAudioSampleAvailable;
                    waveInEvent.Dispose();
                }

                if (_waveOutEvent is { } waveOutEvent)
                {
                    _waveOutEvent = null;
                    waveOutEvent.Dispose();
                }
            }
        }

        /// <summary>
        /// Calculates optimal buffer size for audio processing based on PCM sample count.
        /// Uses adaptive sizing to learn from usage patterns and reduce buffer fragmentation.
        /// </summary>
        /// <param name="pcmSampleCount">Number of PCM samples to be encoded</param>
        /// <returns>Optimal buffer size for PooledSegmentedBuffer allocation</returns>
        private static int GetOptimalBufferSize(int pcmSampleCount)
        {
            // Estimate encoded size (typically 25-50% of PCM for modern codecs like Opus)
            var estimatedEncodedSize = pcmSampleCount * sizeof(short) / 3; // Conservative estimate
            
            var optimalSize = estimatedEncodedSize switch
            {
                <= 2048 => DEFAULT_AUDIO_BUFFER_SIZE,    // 4KB for normal frames
                <= 4096 => LARGE_AUDIO_BUFFER_SIZE,      // 8KB for large frames  
                _ => MAX_AUDIO_BUFFER_SIZE                // 16KB for very large frames
            };

            // Adaptive sizing: learn from observed patterns to reduce future fragmentation
            if (estimatedEncodedSize > _maxObservedBufferSize && estimatedEncodedSize <= MAX_AUDIO_BUFFER_SIZE)
            {
                _maxObservedBufferSize = estimatedEncodedSize;
            }

            // Use the larger of calculated optimal size or previously observed maximum
            return Math.Max(optimalSize, _maxObservedBufferSize);
        }
    }
}
