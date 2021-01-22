//-----------------------------------------------------------------------------
// Filename: AudioExtrasSource.cs
//
// Description: Implements an audio source that can generate samples from a
// variety of non-live sources. For examples signal generators or reading
// samples from files.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 19 Mar 2020	Aaron Clauson	Created, Dublin, Ireland.
// 21 Apr 2020  Aaron Clauson   Added alaw and mulaw decode classes.
// 31 May 2020  Aaron Clauson   Refactored codecs and signal generator to 
//                              separate class files.
// 19 Aug 2020  Aaron Clauson   Renamed from RtpAudioSession to
//                              AudioExtrasSource.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Media
{
    public enum AudioSourcesEnum
    {
        /// <summary>
        /// Plays music samples from a file. The file will be played in a loop until
        /// another source option is set.
        /// </summary>
        Music = 0,

        /// <summary>
        /// Send an audio stream of silence. Note this option does result
        /// in audio RTP packet getting sent.
        /// </summary>
        Silence = 1,

        /// <summary>
        /// White noise static.
        /// </summary>
        WhiteNoise = 2,

        /// <summary>
        /// A continuous sine wave.
        /// </summary>
        SineWave = 3,

        /// <summary>
        /// Pink noise static.
        /// </summary>
        PinkNoise = 4,

        /// <summary>
        /// Don't generate any audio samples.
        /// </summary>
        None = 5,
    }

    public class AudioSourceOptions
    {
        /// <summary>
        /// The type of audio source to use.
        /// </summary>
        public AudioSourcesEnum AudioSource;

        /// <summary>
        /// The sampling rate used to generate the input or if the source is
        /// being generated the sample rate to generate it at.
        /// </summary>
        public AudioSamplingRatesEnum MusicInputSamplingRate = AudioSamplingRatesEnum.Rate8KHz;

        /// <summary>
        /// If the audio source is set to music this must be the path to a raw PCM 8K sampled file.
        /// If set to null or the file doesn't exist the default embedded resource music file will
        /// be used.
        /// </summary>
        public string MusicFile;
    }

    /// <summary>
    /// An audio source implementation that provides a diverse range of audio source options.
    /// The available options encompass signal generation, playback from file and more.
    /// </summary>
    public class AudioExtrasSource : IAudioSource
    {
        public const int AUDIO_SAMPLE_PERIOD_MILLISECONDS_DEFAULT = 20;
        public const int AUDIO_SAMPLE_PERIOD_MILLISECONDS_MIN = 20;
        public const int AUDIO_SAMPLE_PERIOD_MILLISECONDS_MAX = 500;

        private const string MUSIC_RESOURCE_PATH = "SIPSorcery.media.Macroform_-_Simplicity.raw";
        private static float LINEAR_MAXIMUM = 32767f;

        private static ILogger Log = SIPSorcery.Sys.Log.Logger;

        private MediaFormatManager<AudioFormat> _audioFormatManager;
        private BinaryReader _musicStreamReader;
        private SignalGenerator _signalGenerator;
        private Timer _sendSampleTimer;
        private AudioSourceOptions _audioOpts;
        private bool _isStarted;
        private bool _isPaused;
        private bool _isClosed;
        private AudioEncoder _audioEncoder;

        // Fields for interrupting the main audio source with a different stream. For example playing
        // an announcement over music etc.
        private Timer _streamSourceTimer;
        private BinaryReader _streamSourceReader;
        private bool _streamSendInProgress;             // When a send for stream is in progress it takes precedence over the existing audio source.
        private AudioSamplingRatesEnum _streamSourceRate = AudioSamplingRatesEnum.Rate8KHz;

        /// <summary>
        /// Fires when the current send audio from stream operation completes. Send from
        /// stream operations are intended to be short snippets of audio that get sent 
        /// as interruptions to the primary audio stream.
        /// </summary>
        public event Action OnSendFromAudioStreamComplete;

        public event EncodedSampleDelegate OnAudioSourceEncodedSample;

        /// <summary>
        /// This audio source DOES NOT generate raw samples. Subscribe to the encoded samples event
        /// to get samples ready for passing to the RTP transport layer.
        /// </summary>
        [Obsolete("This audio source only produces encoded samples. Do not subscribe to this event.")]
        public event RawAudioSampleDelegate OnAudioSourceRawSample { add { } remove { } }

#pragma warning disable CS0067
        public event SourceErrorDelegate OnAudioSourceError;
        public event SourceErrorDelegate OnAudioSinkError;
#pragma warning restore CS0067

        public AudioExtrasSource()
        {
            _audioEncoder = new AudioEncoder();
            _audioFormatManager = new MediaFormatManager<AudioFormat>(_audioEncoder.SupportedFormats);
            _audioOpts = new AudioSourceOptions { AudioSource = AudioSourcesEnum.None };
        }

        public int _audioSamplePeriodMilliseconds = AUDIO_SAMPLE_PERIOD_MILLISECONDS_DEFAULT;
        public int AudioSamplePeriodMilliseconds
        {
            get => _audioSamplePeriodMilliseconds;
            set
            {
                if (value < AUDIO_SAMPLE_PERIOD_MILLISECONDS_MIN || value > AUDIO_SAMPLE_PERIOD_MILLISECONDS_MAX)
                {
                    throw new ApplicationException("Invalid value for the audio sample period. Must be between " +
                        $"{AUDIO_SAMPLE_PERIOD_MILLISECONDS_MIN} and {AUDIO_SAMPLE_PERIOD_MILLISECONDS_MAX}ms.");
                }
                else
                {
                    _audioSamplePeriodMilliseconds = value;
                }
            }
        }

        /// <summary>
        /// Instantiates an audio source that can generate output samples from a variety of different
        /// non-live sources.
        /// </summary>
        /// <param name="audioOptions">Optional. The options that determine the type of audio to stream to the remote party. 
        /// Example type of audio sources are music, silence, white noise etc.</param>
        public AudioExtrasSource(
            AudioEncoder audioEncoder,
            AudioSourceOptions audioOptions = null)
        {
            _audioEncoder = audioEncoder;
            _audioFormatManager = new MediaFormatManager<AudioFormat>(_audioEncoder.SupportedFormats);
            _audioOpts = audioOptions ?? new AudioSourceOptions { AudioSource = AudioSourcesEnum.None };
        }

        public void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample) =>
            throw new NotImplementedException();
        public bool HasEncodedAudioSubscribers() => OnAudioSourceEncodedSample != null;
        public bool IsAudioSourcePaused() => _isPaused;
        public void RestrictFormats(Func<AudioFormat, bool> filter) => _audioFormatManager.RestrictFormats(filter);
        public List<AudioFormat> GetAudioSourceFormats() => _audioFormatManager.GetSourceFormats();
        public void SetAudioSourceFormat(AudioFormat audioFormat) => _audioFormatManager.SetSelectedFormat(audioFormat);

        public Task CloseAudio()
        {
            if (!_isClosed)
            {
                _isClosed = true;
                _sendSampleTimer?.Dispose();
                _musicStreamReader?.Close();
                StopSendFromAudioStream();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Initialises the audio source as required.
        /// </summary>
        public Task StartAudio()
        {
            if (_audioFormatManager.SelectedFormat.IsEmpty())
            {
                throw new ApplicationException("The sending format for the Audio Extras Source has not been set. Cannot start source.");
            }
            else
            {
                if (!_isStarted)
                {
                    _isStarted = true;
                    SetSource(_audioOpts);
                }

                return Task.CompletedTask;
            }

        }

        public Task PauseAudio()
        {
            _isPaused = true;
            return Task.CompletedTask;
        }

        public Task ResumeAudio()
        {
            _isPaused = false;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Same as the async method of the same name but returns a task that waits for the 
        /// stream send to complete.
        /// </summary>
        /// <param name="audioStream">The stream containing the 16 bit PCM sampled at either 8 or 16Khz 
        /// to send to the remote party.</param>
        /// <param name="streamSampleRate">The sample rate of the supplied PCM samples. Supported rates are
        /// 8 or 16 KHz.</param>
        /// <returns>A task that completes once the stream has been fully sent.</returns>
        public Task SendAudioFromStream(Stream audioStream, AudioSamplingRatesEnum streamSampleRate)
        {
            if (!_isClosed && audioStream != null && audioStream.Length > 0)
            {
                // Stop any existing send from stream operation.
                StopSendFromAudioStream();

                TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                Action handler = null;
                handler = () =>
                {
                    tcs.TrySetResult(true);
                    OnSendFromAudioStreamComplete -= handler;
                };
                OnSendFromAudioStreamComplete += handler;

                InitialiseSendAudioFromStreamTimer(audioStream, streamSampleRate);

                _streamSourceTimer.Change(_audioSamplePeriodMilliseconds, _audioSamplePeriodMilliseconds);

                return tcs.Task;
            }
            else
            {
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Cancels an in-progress send audio from stream operation.
        /// </summary>
        public void CancelSendAudioFromStream()
        {
            StopSendFromAudioStream();
        }

        /// <summary>
        /// Convenience method for audio sources when only default options are required,
        /// e.g. the default music file rather than a custom one.
        /// </summary>
        /// <param name="audioSource">The audio source to set. The call will fail
        /// if the source requires additional options, e.g. stream from file.</param>
        public void SetSource(AudioSourcesEnum audioSource)
        {
            SetSource(new AudioSourceOptions { AudioSource = audioSource });
        }

        /// <summary>
        /// Sets the source for the session. Overrides any existing source.
        /// </summary>
        /// <param name="sourceOptions">The new audio source.</param>
        public void SetSource(AudioSourceOptions sourceOptions)
        {
            if (!_isStarted)
            {
                // Audio source is being adjusted prior to starting.
                _audioOpts = sourceOptions;
            }
            else
            {
                // If required start the audio source.
                if (sourceOptions != null)
                {
                    _sendSampleTimer?.Dispose();
                    _musicStreamReader?.Close();
                    StopSendFromAudioStream();

                    _audioOpts = sourceOptions;

                    if (_audioOpts.AudioSource == AudioSourcesEnum.None)
                    {
                        // Do nothing, all other sources have already been stopped.
                    }
                    else if (_audioOpts.AudioSource == AudioSourcesEnum.Silence)
                    {
                        _sendSampleTimer = new Timer(SendSilenceSample, null, 0, _audioSamplePeriodMilliseconds);
                    }
                    else if (_audioOpts.AudioSource == AudioSourcesEnum.PinkNoise ||
                         _audioOpts.AudioSource == AudioSourcesEnum.WhiteNoise ||
                         _audioOpts.AudioSource == AudioSourcesEnum.SineWave)
                    {
                        _signalGenerator = new SignalGenerator(_audioFormatManager.SelectedFormat.ClockRate, 1);

                        switch (_audioOpts.AudioSource)
                        {
                            case AudioSourcesEnum.PinkNoise:
                                _signalGenerator.Type = SignalGeneratorType.Pink;
                                break;
                            case AudioSourcesEnum.SineWave:
                                _signalGenerator.Type = SignalGeneratorType.Sin;
                                break;
                            case AudioSourcesEnum.WhiteNoise:
                            default:
                                _signalGenerator.Type = SignalGeneratorType.White;
                                break;
                        }

                        _sendSampleTimer = new Timer(SendSignalGeneratorSample, null, 0, _audioSamplePeriodMilliseconds);
                    }
                    else if (_audioOpts.AudioSource == AudioSourcesEnum.Music)
                    {
                        if (string.IsNullOrWhiteSpace(_audioOpts.MusicFile) || !File.Exists(_audioOpts.MusicFile))
                        {
                            if (!string.IsNullOrWhiteSpace(_audioOpts.MusicFile))
                            {
                                Log.LogWarning($"Music file not set or not found, using default music resource.");
                            }

                            var assem = typeof(VideoTestPatternSource).GetTypeInfo().Assembly;
                            var audioStream = assem.GetManifestResourceStream(MUSIC_RESOURCE_PATH);

                            _musicStreamReader = new BinaryReader(audioStream);
                        }
                        else
                        {
                            _musicStreamReader = new BinaryReader(new FileStream(_audioOpts.MusicFile, FileMode.Open, FileAccess.Read));
                        }

                        _sendSampleTimer = new Timer(SendMusicSample, null, 0, _audioSamplePeriodMilliseconds);
                    }
                }
            }
        }

        /// <summary>
        /// Sends a stream containing 16 bit PCM audio to the remote party. Calling this method
        /// will pause the existing audio source until the stream has been sent.
        /// </summary>
        /// <param name="audioStream">The stream containing the 16 bit PCM, sampled at either 8 or 16 Khz,
        /// to send to the remote party.</param>
        /// <param name="streamSampleRate">The sample rate of the supplied PCM samples. Supported rates are
        /// 8 or 16 KHz.</param>
        private void InitialiseSendAudioFromStreamTimer(Stream audioStream, AudioSamplingRatesEnum streamSampleRate)
        {
            if (!_isClosed && audioStream != null && audioStream.Length > 0)
            {
                Log.LogDebug($"Sending audio stream length {audioStream.Length}.");

                _streamSendInProgress = true;
                _streamSourceRate = streamSampleRate;

                _streamSourceReader = new BinaryReader(audioStream);
                _streamSourceTimer = new Timer(SendStreamSample, null, Timeout.Infinite, Timeout.Infinite);
            }
        }

        /// <summary>
        /// Sends audio samples read from a file.
        /// </summary>
        private void SendMusicSample(object state)
        {
            if (!_isClosed && !_streamSendInProgress)
            {
                lock (_sendSampleTimer)
                {
                    var pcm = GetPcmSampleFromReader(_musicStreamReader, _audioOpts.MusicInputSamplingRate, out int samplesRead);

                    if (samplesRead > 0)
                    {
                        EncodeAndSend(pcm, (int)_audioOpts.MusicInputSamplingRate);
                    }

                    if (samplesRead == 0)
                    {
                        _musicStreamReader.BaseStream.Position = 0;
                    }
                }
            }
        }

        /// <summary>
        /// Sends the sounds of silence.
        /// </summary>
        private void SendSilenceSample(object state)
        {
            if (!_isClosed && !_streamSendInProgress)
            {
                lock (_sendSampleTimer)
                {
                    short[] silencePcm = new short[_audioFormatManager.SelectedFormat.ClockRate / 1000 * _audioSamplePeriodMilliseconds];
                    EncodeAndSend(silencePcm, _audioFormatManager.SelectedFormat.ClockRate);
                }
            }
        }

        /// <summary>
        /// Sends a sample from a signal generator generated waveform.
        /// </summary>
        private void SendSignalGeneratorSample(object state)
        {
            if (!_isClosed && !_streamSendInProgress)
            {
                lock (_sendSampleTimer)
                {
                    // Get the signal generator to generate the samples and then convert from signed linear to PCM.
                    float[] linear = new float[_audioFormatManager.SelectedFormat.ClockRate / 1000 * _audioSamplePeriodMilliseconds];
                    _signalGenerator.Read(linear, 0, linear.Length);
                    short[] pcm = linear.Select(x => (short)(x * LINEAR_MAXIMUM)).ToArray();

                    EncodeAndSend(pcm, _audioFormatManager.SelectedFormat.ClockRate);
                }
            }
        }

        /// <summary>
        /// Sends audio samples read from a file containing 16 bit PCM samples.
        /// </summary>
        private void SendStreamSample(object state)
        {
            if (!_isClosed)
            {
                lock (_streamSourceTimer)
                {
                    if (_streamSourceReader?.BaseStream?.CanRead == true)
                    {
                        var pcm = GetPcmSampleFromReader(_streamSourceReader, _streamSourceRate, out int samplesRead);

                        if (samplesRead > 0)
                        {
                            EncodeAndSend(pcm, (int)_streamSourceRate);

                            if (_streamSourceReader.BaseStream.Position >= _streamSourceReader.BaseStream.Length)
                            {
                                Log.LogDebug("Send audio from stream completed.");
                                StopSendFromAudioStream();
                            }
                        }
                        else
                        {
                            Log.LogWarning("Failed to read from audio stream source.");
                            StopSendFromAudioStream();
                        }
                    }
                    else
                    {
                        Log.LogWarning("Failed to read from audio stream source, stream null or closed.");
                        StopSendFromAudioStream();
                    }
                }
            }
        }

        private short[] GetPcmSampleFromReader(BinaryReader binaryReader, AudioSamplingRatesEnum inputSampleRate, out int samplesRead)
        {
            samplesRead = 0;

            if (binaryReader?.BaseStream?.CanRead == true)
            {
                int sampleRate = (inputSampleRate == AudioSamplingRatesEnum.Rate8KHz) ? 8000 : 16000;
                int sampleSize = sampleRate / 1000 * _audioSamplePeriodMilliseconds;
                short[] pcm = new short[sampleSize];

                for (int i = 0; i < sampleSize && binaryReader.BaseStream.Position < binaryReader.BaseStream.Length; i++)
                {
                    pcm[samplesRead++] = binaryReader.ReadInt16();
                }

                return pcm;
            }

            return null;
        }

        private void EncodeAndSend(short[] pcm, int pcmSampleRate)
        {
            if (pcm.Length > 0)
            {
                if (pcmSampleRate != _audioFormatManager.SelectedFormat.ClockRate)
                {
                    pcm = _audioEncoder.Resample(pcm, pcmSampleRate, _audioFormatManager.SelectedFormat.ClockRate);
                }

                byte[] encodedSample = _audioEncoder.EncodeAudio(pcm, _audioFormatManager.SelectedFormat);

                uint rtpUnits = (uint)(_audioFormatManager.SelectedFormat.RtpClockRate / 1000 * _audioSamplePeriodMilliseconds);

                OnAudioSourceEncodedSample?.Invoke(rtpUnits, encodedSample);
            }
        }

        /// <summary>
        /// Stops a send from audio stream job.
        /// </summary>
        private void StopSendFromAudioStream()
        {
            _streamSourceReader?.Close();
            _streamSourceTimer?.Dispose();
            _streamSendInProgress = false;

            OnSendFromAudioStreamComplete?.Invoke();
        }
    }
}
