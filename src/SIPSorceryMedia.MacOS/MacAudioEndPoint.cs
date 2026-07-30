//-----------------------------------------------------------------------------
// Filename: MacAudioEndPoint.cs
//
// Description: RTP audio endpoint that uses AVFoundation for audio capture
// and rendering on macOS.
//
// Architecture overview
// =====================
// Capture path:
//   The AVFoundation tap is installed requesting the target codec PCM16 format
//   so that AVFoundation performs sample-rate and channel conversion internally
//   (via its own hidden AVAudioConverter).  The tap callback (real-time audio
//   thread) does only three things:
//     1. One Marshal.Copy from the AVAudioPcmBuffer into a preallocated scratch
//        array (no per-frame heap allocation in steady state).
//     2. One ring-buffer write (brief lock, no allocation).
//     3. One SemaphoreSlim.Release() to wake the encoding worker.
//   A dedicated background thread ("MacAudioCapture") reads 20 ms frames from
//   the ring buffer, calls IAudioEncoder.EncodeAudio, and raises the events.
//
// Playback path:
//   A small pool of PLAYBACK_POOL_SIZE preallocated AVAudioPcmBuffers eliminates
//   per-frame allocation.  The completion callback that fires when AVFoundation
//   finishes playing a buffer simply returns it to the pool via ConcurrentStack
//   and decrements the queue counter via Interlocked — no lock is taken.
//   Latency is bounded: frames arriving when the queued audio would exceed
//   MAX_QUEUED_PLAYBACK_MS are dropped (newest-dropped policy).
//
// Locking discipline:
//   _sourceLock / _sinkLock are held ONLY inside lifecycle methods
//   (Start / Pause / Resume / Close / InitDevice / SetFormat).
//   They are never held inside the AVFoundation tap callback or the playback
//   completion callback.
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
using System.Threading;
using System.Threading.Tasks;
using AVFoundation;
using Foundation;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;

namespace SIPSorceryMedia.MacOS
{
    /// <summary>
    /// Real-time audio endpoint for the default macOS microphone and output device.
    /// See file header for architecture details.
    /// </summary>
    /// <remarks>
    /// The containing application must provide an NSMicrophoneUsageDescription
    /// entry in its Info.plist before microphone capture is enabled.
    /// </remarks>
    public class MacAudioEndPoint : IAudioEndPoint, IDisposable
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const int AUDIO_FRAME_MILLISECONDS = 20;

        /// <summary>
        /// Capacity of the capture PCM ring buffer in milliseconds.
        /// Sized to absorb up to 2 s of jitter between the AVFoundation thread
        /// and the encoding worker.
        /// </summary>
        private const int CAPTURE_RING_BUFFER_MS = 2000;

        /// <summary>
        /// Number of AVAudioPcmBuffers preallocated in the playback pool.
        /// </summary>
        private const int PLAYBACK_POOL_SIZE = 8;

        /// <summary>
        /// Preallocated frame capacity of each playback pool buffer.
        /// 2048 frames covers ≈42 ms at 48 kHz — sufficient for any supported codec.
        /// </summary>
        private const int PLAYBACK_POOL_FRAME_CAPACITY = 2048;

        /// <summary>
        /// Maximum queued playback duration.  New frames that would push the
        /// queue beyond this limit are dropped (newest-dropped policy).
        /// </summary>
        private const int MAX_QUEUED_PLAYBACK_MS = 300;

        // ── Public defaults ───────────────────────────────────────────────────

        /// <summary>Default codec-side source rate used before SDP negotiation.</summary>
        public static readonly AudioSamplingRatesEnum DefaultAudioSourceSamplingRate =
            AudioSamplingRatesEnum.Rate8KHz;

        /// <summary>Default codec-side playback rate used before SDP negotiation.</summary>
        public static readonly AudioSamplingRatesEnum DefaultAudioPlaybackRate =
            AudioSamplingRatesEnum.Rate8KHz;

        // ── Logging ───────────────────────────────────────────────────────────

        private static ILogger logger =
            SIPSorcery.LogFactory.CreateLogger<MacAudioEndPoint>();

        // ── Format / encoder ──────────────────────────────────────────────────

        private readonly IAudioEncoder _audioEncoder;
        private readonly MediaFormatManager<AudioFormat> _audioFormatManager;
        private readonly bool _disableSource;
        private readonly bool _disableSink;

        // ── Lifecycle locks (lifecycle ops only — never held in callbacks) ────

        private readonly object _sourceLock = new object();
        private readonly object _sinkLock = new object();

        // ── Playback state ────────────────────────────────────────────────────

        private AVAudioEngine _playbackEngine;
        private AVAudioPlayerNode _playerNode;
        private AVAudioFormat _playbackFormat;
        private int _playbackSampleRate;
        private int _playbackChannels;

        /// <summary>
        /// Total PCM samples (all channels) currently scheduled on the player node.
        /// Updated via Interlocked; never protected by _sinkLock.
        /// </summary>
        private long _queuedPlaybackSamples;

        /// <summary>
        /// Pool of reusable AVAudioPcmBuffers.  ConcurrentStack is used so that
        /// the most-recently-returned buffer (likely warm in cache) is chosen first.
        /// Rebuilt when the codec format changes.
        /// </summary>
        private readonly System.Collections.Concurrent.ConcurrentStack<AVAudioPcmBuffer>
            _playbackPool = new System.Collections.Concurrent.ConcurrentStack<AVAudioPcmBuffer>();

        // ── Capture state ─────────────────────────────────────────────────────

        private AVAudioEngine _captureEngine;
        private AVAudioInputNode _inputNode;
        private AVAudioFormat _captureNativeFormat; // stored for diagnostic logging only
        private int _tapRate;     // rate at which the tap is currently installed
        private int _tapChannels; // channels at which the tap is currently installed

        /// <summary>
        /// Fixed-capacity SPSC ring buffer.
        /// Producer: AVFoundation tap callback.
        /// Consumer: _captureWorkerThread.
        /// Overflow policy: oldest samples are overwritten.
        /// </summary>
        private PcmRingBuffer _captureRingBuffer;

        /// <summary>
        /// Preallocated scratch array used inside the tap callback for the single
        /// Marshal.Copy.  Only ever accessed from the AVFoundation callback thread;
        /// no synchronisation needed.  Grown lazily; never shrunk.
        /// </summary>
        private short[] _captureScratch;

        private Thread _captureWorkerThread;
        private CancellationTokenSource _captureWorkerCts;
        private SemaphoreSlim _captureWorkerSignal; // producer: callback, consumer: worker

        // ── Lifecycle flags ───────────────────────────────────────────────────

        protected bool _isAudioSourceStarted;
        protected bool _isAudioSinkStarted;
        protected bool _isAudioSourcePaused;
        protected bool _isAudioSinkPaused;
        protected bool _isAudioSourceClosed;
        protected bool _isAudioSinkClosed;
        private bool _disposed;

        // ── Performance counters (Interlocked; logged at Close) ───────────────

        private long _capturedSamples;
        private long _encodedFrames;
        private long _playedFrames;
        private long _droppedCaptureBuffers;
        private long _droppedPlaybackFrames;

        // ── Events ────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public event EncodedSampleDelegate OnAudioSourceEncodedSample;

        /// <inheritdoc/>
        public event Action<EncodedAudioFrame> OnAudioSourceEncodedFrameReady;

        /// <summary>Not raised — this endpoint is encoded-only.</summary>
        [Obsolete("The audio source only generates encoded samples.")]
        public event RawAudioSampleDelegate OnAudioSourceRawSample
        {
            add { }
            remove { }
        }

        /// <inheritdoc/>
        public event SourceErrorDelegate OnAudioSourceError;

        /// <inheritdoc/>
        public event SourceErrorDelegate OnAudioSinkError;

        // ── Constructor ───────────────────────────────────────────────────────

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
                throw new ArgumentNullException(nameof(audioEncoder));

            logger = SIPSorcery.LogFactory.CreateLogger<MacAudioEndPoint>();
            _audioEncoder = audioEncoder;
            _audioFormatManager = new MediaFormatManager<AudioFormat>(audioEncoder.SupportedFormats);
            _disableSource = disableSource;
            _disableSink = disableSink;

            if (!_disableSink)
            {
                InitPlaybackDevice((int)DefaultAudioPlaybackRate, 1);

                if (audioEncoder.SupportedFormats?.Count == 1)
                    SetAudioSinkFormat(audioEncoder.SupportedFormats[0]);
            }

            if (!_disableSource)
            {
                InitCaptureDevice();

                if (audioEncoder.SupportedFormats?.Count == 1)
                    SetAudioSourceFormat(audioEncoder.SupportedFormats[0]);
            }
        }

        // ── IAudioEndPoint helpers ────────────────────────────────────────────

        public void RestrictFormats(Func<AudioFormat, bool> filter)
            => _audioFormatManager.RestrictFormats(filter);

        public List<AudioFormat> GetAudioSourceFormats()
            => _audioFormatManager.GetSourceFormats();

        public List<AudioFormat> GetAudioSinkFormats()
            => _audioFormatManager.GetSourceFormats();

        public bool HasEncodedAudioSubscribers()
            => OnAudioSourceEncodedSample != null || OnAudioSourceEncodedFrameReady != null;

        public bool IsAudioSourcePaused() => _isAudioSourcePaused;
        public bool IsAudioSinkPaused()   => _isAudioSinkPaused;

        public void ExternalAudioSourceRawSample(
            AudioSamplingRatesEnum samplingRate,
            uint durationMilliseconds,
            short[] sample)
            => throw new NotImplementedException();

        /// <summary>
        /// Called during SDP negotiation to select the active codec format.
        /// If the rate or channel count changes the capture tap is reinstalled
        /// with the new PCM16 format so that AVFoundation's internal converter
        /// is configured for the new parameters.  Safe to call while capture is
        /// running.
        /// </summary>
        public void SetAudioSourceFormat(AudioFormat audioFormat)
        {
            _audioFormatManager.SetSelectedFormat(audioFormat);

            lock (_sourceLock)
            {
                int rate     = audioFormat.ClockRate;
                int channels = Math.Max(1, audioFormat.ChannelCount);

                if (_tapRate != rate || _tapChannels != channels)
                {
                    RebuildCaptureBuffer(rate, channels);
                    if (_inputNode != null)
                        ReinstallCaptureTap(rate, channels);
                }
                else
                {
                    // Same format: just discard stale samples.
                    _captureRingBuffer?.Clear();
                }
            }

            logger.LogDebug(
                "macOS audio endpoint capture format: {Codec} {Rate} Hz {Channels} ch.",
                audioFormat.ToString(), audioFormat.ClockRate, audioFormat.ChannelCount);
        }

        public void SetAudioSinkFormat(AudioFormat audioFormat)
        {
            _audioFormatManager.SetSelectedFormat(audioFormat);

            if (_disableSink) return;

            int channels   = Math.Max(1, audioFormat.ChannelCount);
            int sampleRate = audioFormat.ClockRate;

            if (_playbackFormat == null ||
                _playbackSampleRate != sampleRate ||
                _playbackChannels   != channels)
            {
                logger.LogDebug(
                    "macOS audio endpoint playback format: {Rate} Hz {Channels} ch.",
                    sampleRate, channels);

                bool restart = _isAudioSinkStarted && !_isAudioSinkPaused;
                InitPlaybackDevice(sampleRate, channels);
                if (restart) StartPlaybackDevice();
            }
        }

        public MediaEndPoints ToMediaEndPoints() => new MediaEndPoints
        {
            AudioSource = _disableSource ? null : this,
            AudioSink   = _disableSink   ? null : this,
        };

        // ── IAudioEndPoint lifecycle ──────────────────────────────────────────

        /// <summary>Starts capture and playback.</summary>
        public Task Start()
        {
            if (!_disableSource && !_isAudioSourceStarted) StartAudio();
            if (!_disableSink   && !_isAudioSinkStarted)   StartAudioSink();
            return Task.CompletedTask;
        }

        /// <summary>Closes capture and playback.</summary>
        public Task Close()
        {
            if (!_disableSource && !_isAudioSourceClosed) CloseAudio();
            if (!_disableSink   && !_isAudioSinkClosed)   CloseAudioSink();
            return Task.CompletedTask;
        }

        public Task Pause()
        {
            if (!_disableSource && !_isAudioSourcePaused) PauseAudio();
            if (!_disableSink   && !_isAudioSinkPaused)   PauseAudioSink();
            return Task.CompletedTask;
        }

        public Task Resume()
        {
            if (!_disableSource && _isAudioSourcePaused) ResumeAudio();
            if (!_disableSink   && _isAudioSinkPaused)   ResumeAudioSink();
            return Task.CompletedTask;
        }

        // ── Capture device ────────────────────────────────────────────────────

        private void InitCaptureDevice()
        {
            lock (_sourceLock)
            {
                try
                {
                    DisposeCaptureDevice();

                    _captureEngine = new AVAudioEngine();
                    _inputNode     = _captureEngine.InputNode;
                    _captureNativeFormat = _inputNode.GetBusOutputFormat(0);

                    if (_captureNativeFormat == null ||
                        _captureNativeFormat.SampleRate  <= 0 ||
                        _captureNativeFormat.ChannelCount == 0)
                    {
                        throw new InvalidOperationException(
                            "The default macOS microphone returned an invalid audio format.");
                    }

                    // Install tap at the default codec format.  SetAudioSourceFormat will
                    // reinstall transparently if the negotiated format differs.
                    int defaultRate = (int)DefaultAudioSourceSamplingRate;
                    RebuildCaptureBuffer(defaultRate, 1);
                    ReinstallCaptureTap(defaultRate, 1);

                    _captureEngine.Prepare();

                    logger.LogDebug(
                        "macOS microphone native format: {Rate} Hz, {Channels} ch, {Fmt}.",
                        _captureNativeFormat.SampleRate,
                        _captureNativeFormat.ChannelCount,
                        _captureNativeFormat.CommonFormat);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(0, ex, "MacAudioEndPoint failed to initialise capture.");
                    OnAudioSourceError?.Invoke(
                        "MacAudioEndPoint failed to initialise capture. " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Allocates a new ring buffer sized for <paramref name="rate"/> × <paramref name="channels"/>
        /// × <see cref="CAPTURE_RING_BUFFER_MS"/> milliseconds.
        /// Must be called while holding <see cref="_sourceLock"/>.
        /// </summary>
        private void RebuildCaptureBuffer(int rate, int channels)
        {
            int capacity = Math.Max(1, rate * channels * CAPTURE_RING_BUFFER_MS / 1000);
            _captureRingBuffer = new PcmRingBuffer(capacity);
        }

        /// <summary>
        /// Removes the existing tap and installs a new PCMInt16 tap at
        /// <paramref name="rate"/> Hz / <paramref name="channels"/> channels.
        /// AVFoundation creates an internal AVAudioConverter from the native
        /// microphone format to this target format automatically.
        /// Safe to call while the engine is running.
        /// Must be called while holding <see cref="_sourceLock"/>.
        /// </summary>
        private void ReinstallCaptureTap(int rate, int channels)
        {
            _inputNode.RemoveTapOnBus(0);

            _tapRate     = rate;
            _tapChannels = channels;

            // Hint for ~40 ms batches; actual batch size is determined by AVFoundation.
            uint tapFrameHint = (uint)Math.Max(1, rate * 40 / 1000);

            // Non-interleaved PCMInt16 — consistent with how AVFoundation delivers
            // mono audio and easy to read with a single Marshal.Copy.
            var tapFormat = new AVAudioFormat(
                AVAudioCommonFormat.PCMInt16,
                rate,
                (uint)channels,
                false /* non-interleaved */);

            _inputNode.InstallTapOnBus(0, tapFrameHint, tapFormat, LocalAudioSampleAvailable);
        }

        /// <summary>
        /// AVFoundation tap callback — runs on the AVFoundation real-time audio thread.
        ///
        /// This method is intentionally minimal:
        ///   1. One Marshal.Copy into a preallocated scratch array (no heap allocation).
        ///   2. One ring-buffer write (brief spinlock, no allocation).
        ///   3. One SemaphoreSlim.Release() to wake the encoding worker.
        ///
        /// No encoding, no logging, no locks from the lifecycle path.
        /// </summary>
        private void LocalAudioSampleAvailable(AVAudioPcmBuffer buffer, AVAudioTime when)
        {
            // Fast early-out without acquiring any lock.
            if (_isAudioSourceClosed || _isAudioSourcePaused || !_isAudioSourceStarted)
                return;

            if (buffer == null || buffer.FrameLength == 0)
                return;

            int frames   = (int)buffer.FrameLength;
            int channels = (int)buffer.Format.ChannelCount;

            // Tap is installed as PCMInt16 non-interleaved.
            IntPtr channelDataArray = buffer.Int16ChannelData;
            if (channelDataArray == IntPtr.Zero)
                return;

            if (channels == 1)
            {
                // Mono fast path: one channel pointer, one Marshal.Copy.
                IntPtr ch0 = Marshal.ReadIntPtr(channelDataArray, 0);
                if (ch0 == IntPtr.Zero) return;

                // Grow scratch only on the first call or a rare tap size change.
                if (_captureScratch == null || _captureScratch.Length < frames)
                    _captureScratch = new short[frames + 64];

                Marshal.Copy(ch0, _captureScratch, 0, frames);
            }
            else
            {
                // Multi-channel: mix down to mono.
                // Rare — the tap is installed as mono — but guard for robustness.
                if (_captureScratch == null || _captureScratch.Length < frames)
                    _captureScratch = new short[frames + 64];

                // Accumulate channels into _captureScratch using an int[] to avoid
                // per-frame overflow. Allocating a small int[] here is acceptable
                // because multi-channel taps are not expected in production.
                int[] accum = new int[frames];
                for (int ch = 0; ch < channels; ch++)
                {
                    IntPtr chPtr = Marshal.ReadIntPtr(channelDataArray, ch * IntPtr.Size);
                    if (chPtr == IntPtr.Zero) continue;
                    short[] chanData = new short[frames];
                    Marshal.Copy(chPtr, chanData, 0, frames);
                    for (int f = 0; f < frames; f++)
                        accum[f] += chanData[f];
                }
                for (int f = 0; f < frames; f++)
                    _captureScratch[f] = ClampToInt16(accum[f] / channels);
            }

            PcmRingBuffer ring = _captureRingBuffer;
            if (ring == null) return;

            if (!ring.TryWrite(_captureScratch, 0, frames))
            {
                Interlocked.Increment(ref _droppedCaptureBuffers);
                return;
            }

            Interlocked.Add(ref _capturedSamples, frames);

            // Signal the worker.  SemaphoreFullException means the worker is
            // already scheduled to drain; the data is safely in the ring buffer.
            try   { _captureWorkerSignal?.Release(); }
            catch (SemaphoreFullException)  { /* worker already signalled */ }
            catch (ObjectDisposedException) { /* shutting down */ }
        }

        /// <summary>
        /// Encoding worker thread ("MacAudioCapture").
        /// Reads complete 20 ms frames from the ring buffer, encodes them, and
        /// raises <see cref="OnAudioSourceEncodedSample"/> /
        /// <see cref="OnAudioSourceEncodedFrameReady"/>.
        /// All encoding, format negotiation, and event dispatch happen here —
        /// never in the AVFoundation callback.
        /// </summary>
        private void CaptureWorkerLoop(CancellationToken ct)
        {
            // Per-worker preallocated frame buffer.  Reallocated only when the
            // negotiated format changes (rare).
            short[] frameBuffer = Array.Empty<short>();

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    _captureWorkerSignal.Wait(ct);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException)    { break; }

                if (_isAudioSourceClosed) break;

                AudioFormat audioFormat = _audioFormatManager.SelectedFormat;
                if (audioFormat.IsEmpty()) continue;

                int sampleRate    = audioFormat.ClockRate;
                int channels      = Math.Max(1, audioFormat.ChannelCount);
                int samplesPerFrame = Math.Max(1,
                    sampleRate * channels * AUDIO_FRAME_MILLISECONDS / 1000);

                // Resize only when frame shape changes.
                if (frameBuffer.Length != samplesPerFrame)
                    frameBuffer = new short[samplesPerFrame];

                PcmRingBuffer ring = _captureRingBuffer;
                if (ring == null) continue;

                // Drain all complete frames available in the ring buffer.
                while (!ct.IsCancellationRequested &&
                       ring.TryRead(frameBuffer, 0, samplesPerFrame))
                {
                    try
                    {
                        byte[] encoded = _audioEncoder.EncodeAudio(frameBuffer, audioFormat);
                        if (encoded == null)
                        {
                            continue;
                        }

                        uint durationRtpUnits = (uint)((long)audioFormat.RtpClockRate * AUDIO_FRAME_MILLISECONDS / 1000);
                        OnAudioSourceEncodedSample?.Invoke(durationRtpUnits, encoded);

                        OnAudioSourceEncodedFrameReady?.Invoke(new EncodedAudioFrame(
                            0, audioFormat, AUDIO_FRAME_MILLISECONDS, encoded));

                        Interlocked.Increment(ref _encodedFrames);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(0, ex, "MacAudioEndPoint: encoding failed.");
                    }
                }
            }
        }

        private void StartCaptureDevice()
        {
            if (_captureEngine == null) return;

            // Start encoding worker if not already running.
            if (_captureWorkerThread == null || !_captureWorkerThread.IsAlive)
            {
                _captureWorkerCts?.Dispose();
                _captureWorkerCts = new CancellationTokenSource();

                _captureWorkerSignal?.Dispose();
                _captureWorkerSignal = new SemaphoreSlim(0);

                _captureWorkerThread = new Thread(
                    () => CaptureWorkerLoop(_captureWorkerCts.Token))
                {
                    Name         = "MacAudioCapture",
                    IsBackground = true,
                };
                _captureWorkerThread.Start();
            }

            if (!_captureEngine.Running)
            {
                if (!_captureEngine.StartAndReturnError(out NSError error))
                {
                    string msg = error?.LocalizedDescription
                        ?? "Unknown AVAudioEngine capture error.";

                    OnAudioSourceError?.Invoke(
                        "MacAudioEndPoint failed to start capture. " + msg);
                }
            }
        }

        /// <summary>
        /// Cancels and joins the encoding worker thread.
        /// Must be called before disposing AVFoundation resources.
        /// </summary>
        private void StopCaptureWorker()
        {
            _captureWorkerCts?.Cancel();

            // Unblock a worker that is waiting on the semaphore.
            try   { _captureWorkerSignal?.Release(); }
            catch (ObjectDisposedException) { }

            _captureWorkerThread?.Join(millisecondsTimeout: 2000);
            _captureWorkerThread = null;
        }

        public Task PauseAudio()
        {
            lock (_sourceLock)
            {
                _isAudioSourcePaused = true;
                _captureEngine?.Pause();
            }
            return Task.CompletedTask;
        }

        public Task ResumeAudio()
        {
            lock (_sourceLock)
            {
                _isAudioSourcePaused = false;
                if (!_isAudioSourceClosed) StartCaptureDevice();
            }
            return Task.CompletedTask;
        }

        public Task StartAudio()
        {
            lock (_sourceLock)
            {
                if (!_isAudioSourceStarted && !_isAudioSourceClosed)
                {
                    _isAudioSourceStarted = true;
                    _isAudioSourcePaused  = false;
                    StartCaptureDevice();
                }
            }
            return Task.CompletedTask;
        }

        public Task CloseAudio()
        {
            lock (_sourceLock)
            {
                if (!_isAudioSourceClosed)
                {
                    _isAudioSourceClosed  = true;
                    _isAudioSourceStarted = false;
                    _isAudioSourcePaused  = false;
                    _captureRingBuffer?.Clear();
                    DisposeCaptureDevice();
                }
            }

            logger.LogDebug(
                "MacAudioEndPoint capture closed — " +
                "captured={Captured} encoded={Encoded} droppedCapture={Dropped}.",
                Interlocked.Read(ref _capturedSamples),
                Interlocked.Read(ref _encodedFrames),
                Interlocked.Read(ref _droppedCaptureBuffers));

            return Task.CompletedTask;
        }

        private void DisposeCaptureDevice()
        {
            // Worker must stop first so it cannot touch AVFoundation objects after disposal.
            StopCaptureWorker();

            try
            {
                _inputNode?.RemoveTapOnBus(0);
                _captureEngine?.Stop();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "MacAudioEndPoint: error while closing capture.");
            }
            finally
            {
                _captureNativeFormat?.Dispose();
                _captureNativeFormat = null;

                // InputNode is owned by AVAudioEngine — do not dispose independently.
                _inputNode = null;

                _captureEngine?.Dispose();
                _captureEngine = null;

                _captureWorkerCts?.Dispose();
                _captureWorkerCts = null;

                _captureWorkerSignal?.Dispose();
                _captureWorkerSignal = null;
            }
        }

        // ── Playback device ───────────────────────────────────────────────────

        private void InitPlaybackDevice(int sampleRate, int channels)
        {
            lock (_sinkLock)
            {
                try
                {
                    DisposePlaybackDevice();

                    _playbackSampleRate = sampleRate;
                    _playbackChannels   = Math.Max(1, channels);
                    Interlocked.Exchange(ref _queuedPlaybackSamples, 0);

                    _playbackEngine = new AVAudioEngine();
                    _playerNode     = new AVAudioPlayerNode();

                    // Interleaved PCMInt16: simplifies the single Marshal.Copy in QueuePlayback.
                    _playbackFormat = new AVAudioFormat(
                        AVAudioCommonFormat.PCMInt16,
                        sampleRate,
                        (uint)_playbackChannels,
                        true /* interleaved */);

                    _playbackEngine.AttachNode(_playerNode);

                    // AVAudioEngine converts the codec PCM format to the output-device format.
                    _playbackEngine.Connect(
                        _playerNode,
                        _playbackEngine.MainMixerNode,
                        _playbackFormat);

                    _playbackEngine.Prepare();

                    RebuildPlaybackPool();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(0, ex, "MacAudioEndPoint failed to initialise playback.");
                    OnAudioSinkError?.Invoke(
                        "MacAudioEndPoint failed to initialise playback. " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Drains and disposes the current pool then allocates
        /// <see cref="PLAYBACK_POOL_SIZE"/> fresh buffers for the active format.
        /// Must be called while holding <see cref="_sinkLock"/>.
        /// </summary>
        private void RebuildPlaybackPool()
        {
            while (_playbackPool.TryPop(out AVAudioPcmBuffer old))
                old.Dispose();

            if (_playbackFormat == null) return;

            for (int i = 0; i < PLAYBACK_POOL_SIZE; i++)
            {
                _playbackPool.Push(
                    new AVAudioPcmBuffer(_playbackFormat, PLAYBACK_POOL_FRAME_CAPACITY));
            }
        }

        /// <summary>
        /// Receives already-decoded little-endian PCM16 bytes.
        /// </summary>
        public void GotAudioSample(byte[] pcmSample)
        {
            if (pcmSample == null || pcmSample.Length < sizeof(short)) return;

            short[] samples = new short[pcmSample.Length / sizeof(short)];
            Buffer.BlockCopy(pcmSample, 0, samples, 0, samples.Length * sizeof(short));
            QueuePlayback(samples);
        }

        /// <summary>Obsolete RTP payload handler.</summary>
        [Obsolete("Use GotEncodedMediaFrame instead.")]
        public void GotAudioRtp(
            IPEndPoint remoteEndPoint,
            uint ssrc, uint seqnum, uint timestamp,
            int payloadID, bool marker, byte[] payload)
        {
            if (_disableSink || payload == null || _audioEncoder == null ||
                _audioFormatManager.SelectedFormat.IsEmpty())
                return;

            QueuePlayback(_audioEncoder.DecodeAudio(payload, _audioFormatManager.SelectedFormat));
        }

        /// <summary>Receives an encoded frame from the remote call party.</summary>
        public void GotEncodedMediaFrame(EncodedAudioFrame encodedMediaFrame)
        {
            if (_disableSink || encodedMediaFrame == null || _audioEncoder == null)
                return;

            AudioFormat audioFormat = encodedMediaFrame.AudioFormat;
            if (audioFormat.IsEmpty()) return;

            if (_playbackFormat == null ||
                _playbackSampleRate != audioFormat.ClockRate ||
                _playbackChannels   != Math.Max(1, audioFormat.ChannelCount))
            {
                SetAudioSinkFormat(audioFormat);
            }

            QueuePlayback(_audioEncoder.DecodeAudio(encodedMediaFrame.EncodedAudio, audioFormat));
        }

        /// <summary>
        /// Schedules decoded PCM16 for playback using a buffer from the preallocated pool.
        ///
        /// Overflow policy: if scheduling this frame would push queued audio beyond
        /// <see cref="MAX_QUEUED_PLAYBACK_MS"/> the frame is dropped (newest-dropped).
        ///
        /// The AVFoundation completion callback runs on an AVFoundation internal thread.
        /// It only decrements the queue counter (Interlocked) and returns the buffer to
        /// the pool (ConcurrentStack.Push) — no lock is acquired.
        /// </summary>
        private void QueuePlayback(short[] pcm)
        {
            if (pcm == null || pcm.Length == 0 ||
                _isAudioSinkClosed || _isAudioSinkPaused)
                return;

            lock (_sinkLock)
            {
                if (_playerNode == null || _playbackFormat == null) return;

                int  channels   = Math.Max(1, _playbackChannels);
                int  frameCount = pcm.Length / channels;
                if (frameCount <= 0) return;

                // Bound latency: drop newest frame if queue is saturated.
                long maxSamples =
                    (long)_playbackSampleRate * MAX_QUEUED_PLAYBACK_MS / 1000 * channels;

                if (Interlocked.Read(ref _queuedPlaybackSamples) + pcm.Length > maxSamples)
                {
                    Interlocked.Increment(ref _droppedPlaybackFrames);
                    return;
                }

                // Try to obtain a buffer from the pool.
                if (!_playbackPool.TryPop(out AVAudioPcmBuffer buffer))
                {
                    // All pool buffers are currently in flight — drop incoming frame.
                    Interlocked.Increment(ref _droppedPlaybackFrames);
                    return;
                }

                // If the frame is larger than the pool buffer capacity (rare), fall back
                // to a fresh allocation rather than silently truncating audio.
                if ((int)buffer.FrameCapacity < frameCount)
                {
                    buffer.Dispose();
                    buffer = new AVAudioPcmBuffer(_playbackFormat, (uint)frameCount);
                }

                buffer.FrameLength = (uint)frameCount;

                // Interleaved PCMInt16: the channel-data array holds one pointer
                // to all interleaved samples.
                IntPtr channelArray = buffer.Int16ChannelData;
                if (channelArray == IntPtr.Zero)
                {
                    _playbackPool.Push(buffer);
                    return;
                }

                IntPtr samplesPtr = Marshal.ReadIntPtr(channelArray, 0);
                if (samplesPtr == IntPtr.Zero)
                {
                    _playbackPool.Push(buffer);
                    return;
                }

                // One Marshal.Copy for the entire frame.
                Marshal.Copy(pcm, 0, samplesPtr, pcm.Length);

                Interlocked.Add(ref _queuedPlaybackSamples, pcm.Length);

                // Capture pcm.Length in a local so the closure captures the right value.
                int samplesScheduled = pcm.Length;

                _playerNode.ScheduleBuffer(buffer, () =>
                {
                    // AVFoundation completion callback — runs on AVFoundation internal thread.
                    // No lock; only lock-free operations.
                    Interlocked.Add(ref _queuedPlaybackSamples, -samplesScheduled);
                    _playbackPool.Push(buffer); // return buffer to pool
                    Interlocked.Increment(ref _playedFrames);
                });

                if (_isAudioSinkStarted && !_isAudioSinkPaused && !_playerNode.Playing)
                    _playerNode.Play();
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
                if (!_isAudioSinkClosed) StartPlaybackDevice();
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
                    _isAudioSinkPaused  = false;
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
                    _isAudioSinkClosed  = true;
                    _isAudioSinkStarted = false;
                    _isAudioSinkPaused  = false;
                    DisposePlaybackDevice();
                }
            }

            logger.LogDebug(
                "MacAudioEndPoint playback closed — " +
                "played={Played} droppedPlayback={Dropped}.",
                Interlocked.Read(ref _playedFrames),
                Interlocked.Read(ref _droppedPlaybackFrames));

            return Task.CompletedTask;
        }

        private void StartPlaybackDevice()
        {
            if (_playbackEngine == null || _playerNode == null) return;

            if (!_playbackEngine.Running)
            {
                if (!_playbackEngine.StartAndReturnError(out NSError error))
                {
                    string msg = error?.LocalizedDescription
                        ?? "Unknown AVAudioEngine playback error.";

                    OnAudioSinkError?.Invoke(
                        "MacAudioEndPoint failed to start playback. " + msg);
                    return;
                }
            }

            if (!_playerNode.Playing)
                _playerNode.Play();
        }

        private void DisposePlaybackDevice()
        {
            try
            {
                // Stop the player node first.  Per AVFoundation docs, Stop() immediately
                // cancels pending buffers and fires their completion callbacks synchronously,
                // so in-flight pool buffers are returned before we drain the pool below.
                _playerNode?.Stop();
                _playbackEngine?.Stop();

                if (_playbackEngine != null && _playerNode != null)
                {
                    _playbackEngine.DisconnectNodeOutput(_playerNode);
                    _playbackEngine.DetachNode(_playerNode);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "MacAudioEndPoint: error while closing playback.");
            }
            finally
            {
                // Drain the pool after stopping so that buffers returned by completion
                // callbacks (fired during Stop above) are also disposed.
                while (_playbackPool.TryPop(out AVAudioPcmBuffer poolBuf))
                    poolBuf.Dispose();

                Interlocked.Exchange(ref _queuedPlaybackSamples, 0);

                _playbackFormat?.Dispose();
                _playbackFormat = null;

                _playerNode?.Dispose();
                _playerNode = null;

                _playbackEngine?.Dispose();
                _playbackEngine = null;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static short ClampToInt16(long value)
        {
            if (value >  short.MaxValue) return  short.MaxValue;
            if (value <  short.MinValue) return  short.MinValue;
            return (short)value;
        }

        // ── Dispose ───────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            CloseAudio();
            CloseAudioSink();

            GC.SuppressFinalize(this);
        }

        // ── PcmRingBuffer ─────────────────────────────────────────────────────

        /// <summary>
        /// Fixed-capacity PCM16 ring buffer.
        ///
        /// Designed for a single-producer / single-consumer scenario:
        ///   Producer: the AVFoundation tap callback (writes via <see cref="TryWrite"/>).
        ///   Consumer: the encoding worker thread (reads via <see cref="TryRead"/>).
        ///   Manager:  lifecycle methods (clears via <see cref="Clear"/>; must not run
        ///             concurrently with producer or consumer).
        ///
        /// A short lock is used for all operations so that <see cref="Clear"/> can be
        /// called safely from the lifecycle thread without data-racing with the callback.
        /// The lock is held only for the duration of one <see cref="Array.Copy"/> plus
        /// two integer updates — negligible latency.
        ///
        /// Overflow policy: <see cref="TryWrite"/> returns <c>false</c> if there is
        /// insufficient space.  The caller increments the drop counter and skips the write.
        /// </summary>
        private sealed class PcmRingBuffer
        {
            private readonly short[] _data;
            private readonly int     _capacity;
            private int _readPos;  // next read index
            private int _writePos; // next write index
            private int _count;    // available samples
            private readonly object _lock = new object();

            public PcmRingBuffer(int capacity)
            {
                if (capacity <= 0)
                    throw new ArgumentOutOfRangeException(nameof(capacity));

                _capacity = capacity;
                _data     = new short[capacity];
            }

            public int Count    { get { lock (_lock) return _count;    } }
            public int Capacity => _capacity;

            /// <summary>
            /// Writes <paramref name="count"/> samples from
            /// <paramref name="src"/>[<paramref name="offset"/>..].
            /// Returns <c>false</c> (drop) if the buffer does not have enough free space.
            /// </summary>
            public bool TryWrite(short[] src, int offset, int count)
            {
                if (count <= 0) return true;

                lock (_lock)
                {
                    int free = _capacity - _count;
                    if (count > free) return false;

                    int firstPart = Math.Min(count, _capacity - _writePos);
                    Array.Copy(src, offset, _data, _writePos, firstPart);
                    if (firstPart < count)
                        Array.Copy(src, offset + firstPart, _data, 0, count - firstPart);

                    _writePos = (_writePos + count) % _capacity;
                    _count   += count;
                }

                return true;
            }

            /// <summary>
            /// Reads exactly <paramref name="count"/> samples into
            /// <paramref name="dest"/>[<paramref name="offset"/>..].
            /// Returns <c>false</c> if insufficient data is available.
            /// </summary>
            public bool TryRead(short[] dest, int offset, int count)
            {
                if (count <= 0) return true;

                lock (_lock)
                {
                    if (_count < count) return false;

                    int firstPart = Math.Min(count, _capacity - _readPos);
                    Array.Copy(_data, _readPos, dest, offset, firstPart);
                    if (firstPart < count)
                        Array.Copy(_data, 0, dest, offset + firstPart, count - firstPart);

                    _readPos = (_readPos + count) % _capacity;
                    _count  -= count;
                }

                return true;
            }

            /// <summary>
            /// Resets the buffer without reallocating.
            /// Must not be called concurrently with <see cref="TryWrite"/> or
            /// <see cref="TryRead"/>.
            /// </summary>
            public void Clear()
            {
                lock (_lock)
                {
                    _readPos  = 0;
                    _writePos = 0;
                    _count    = 0;
                }
            }
        }
    }
}
