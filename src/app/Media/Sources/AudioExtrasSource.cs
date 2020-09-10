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
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions.V1;

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
        private const string MUSIC_RESOURCE_PATH = "media.Macroform_-_Simplicity.raw";
        private const int AUDIO_SAMPLE_PERIOD_MILLISECONDS = 20;
        private const AudioSamplingRatesEnum DEFAULT_AUDIO_SAMPLE_RATE = AudioSamplingRatesEnum.Rate8KHz;
        private const int DEFAULT_RTP_TIMESTAMP_RATE = 8000;
        private const int MUSIC_FILE_SAMPLE_RATE = 8000;

        private static readonly byte PCMU_SILENCE_BYTE_ZERO = 0x7F;
        private static readonly byte PCMU_SILENCE_BYTE_ONE = 0xFF;
        private static readonly byte PCMA_SILENCE_BYTE_ZERO = 0x55;
        private static readonly byte PCMA_SILENCE_BYTE_ONE = 0xD5;
        private static float LINEAR_MAXIMUM = 32767f;
        private static AudioCodecsEnum DEFAULT_SENDING_FORMAT = AudioCodecsEnum.PCMU;

        private static ILogger Log = SIPSorcery.Sys.Log.Logger;

        public static readonly List<AudioCodecsEnum> SupportedCodecs = new List<AudioCodecsEnum>
        {
            AudioCodecsEnum.PCMU,
            AudioCodecsEnum.PCMA,
            AudioCodecsEnum.G722
        };

        private List<AudioCodecsEnum> _supportedCodecs = new List<AudioCodecsEnum>(SupportedCodecs);
        private StreamReader _audioStreamReader;
        private SignalGenerator _signalGenerator;
        private Timer _audioStreamTimer;
        private AudioSourceOptions _audioOpts;
        private AudioCodecsEnum _sendingFormat = DEFAULT_SENDING_FORMAT;        // The codec that was selected to send with during the SDP negotiation.
        private AudioSamplingRatesEnum _sourceAudioSampleRate = DEFAULT_AUDIO_SAMPLE_RATE;
        private int _sendingAudioRtpRate = DEFAULT_RTP_TIMESTAMP_RATE;      // 8Khz for both G711 and G722. Future codecs could have different values.
        private bool _streamSendInProgress;             // When a send for stream is in progress it takes precedence over the existing audio source.
        private byte[] _silenceBuffer;                  // PCMU and PCMA have a standardised silence format. When using these codecs the buffer can be constructed.  
        private BinaryReader _streamSourceReader;
        private Timer _streamSourceTimer;
        private bool _isStarted = false;
        private bool _isClosed = false;
        private AudioEncoder _audioEncoder;

        /// <summary>
        /// The sample rate of the source stream.
        /// </summary>
        private AudioSamplingRatesEnum _streamSourceSampleRate;

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
            _audioOpts = new AudioSourceOptions { AudioSource = AudioSourcesEnum.None };
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
            _audioOpts = audioOptions ?? new AudioSourceOptions { AudioSource = AudioSourcesEnum.None };
        }

        /// <summary>
        /// Requests that the audio sink and source only advertise support for the supplied list of codecs.
        /// Only codecs that are already supported and in the <see cref="SupportedCodecs" /> list can be 
        /// used.
        /// </summary>
        /// <param name="codecs">The list of codecs to restrict advertised support to.</param>
        public void RestrictCodecs(List<AudioCodecsEnum> codecs)
        {
            if (codecs == null || codecs.Count == 0)
            {
                _supportedCodecs = new List<AudioCodecsEnum>(SupportedCodecs);
            }
            else
            {
                _supportedCodecs = new List<AudioCodecsEnum>();
                foreach (var codec in codecs)
                {
                    if (SupportedCodecs.Any(x => x == codec))
                    {
                        _supportedCodecs.Add(codec);
                    }
                    else
                    {
                        Log.LogWarning($"Not including unsupported codec {codec} in filtered list.");
                    }
                }
            }
        }

        public List<AudioCodecsEnum> GetAudioSourceFormats()
        {
            return _supportedCodecs;
        }

        public void SetAudioSourceFormat(AudioCodecsEnum audioFormat)
        {
            _sendingFormat = audioFormat;
        }

        public Task CloseAudio()
        {
            if (!_isClosed)
            {
                _isClosed = true;
                _audioStreamTimer?.Dispose();
                _audioStreamReader?.Close();
                StopSendFromAudioStream();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Initialises the audio source as required.
        /// </summary>
        public Task StartAudio()
        {
            if (!_isStarted)
            {
                _isStarted = true;
                SetSource(_audioOpts);
            }

            return Task.CompletedTask;
        }

        public Task PauseAudio()
        {
            // TODO.

            return Task.CompletedTask;
        }

        public Task ResumeAudio()
        {
            // TODO.

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
        public async Task SendAudioFromStream(Stream audioStream, AudioSamplingRatesEnum streamSampleRate)
        {
            if (audioStream != null && audioStream.Length > 0)
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

                _streamSourceTimer.Change(AUDIO_SAMPLE_PERIOD_MILLISECONDS, AUDIO_SAMPLE_PERIOD_MILLISECONDS);

                await tcs.Task;
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
            // If required start the audio source.
            if (sourceOptions != null)
            {
                _audioStreamTimer?.Dispose();
                _audioStreamReader?.Close();
                StopSendFromAudioStream();

                if (sourceOptions.AudioSource == AudioSourcesEnum.None)
                {
                    // Do nothing, all other sources have already been stopped.
                }
                else if (sourceOptions.AudioSource == AudioSourcesEnum.Silence)
                {
                    _audioStreamTimer = new Timer(SendSilenceSample, null, 0, AUDIO_SAMPLE_PERIOD_MILLISECONDS);
                }
                else if (sourceOptions.AudioSource == AudioSourcesEnum.PinkNoise ||
                     sourceOptions.AudioSource == AudioSourcesEnum.WhiteNoise ||
                    sourceOptions.AudioSource == AudioSourcesEnum.SineWave)
                {
                    int sourceSampleRate = _sourceAudioSampleRate == AudioSamplingRatesEnum.Rate8KHz ? 8000 : 16000;
                    _signalGenerator = new SignalGenerator(sourceSampleRate, 1);

                    switch (sourceOptions.AudioSource)
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

                    _audioStreamTimer = new Timer(SendSignalGeneratorSample, null, 0, AUDIO_SAMPLE_PERIOD_MILLISECONDS);
                }
                else if (sourceOptions.AudioSource == AudioSourcesEnum.Music)
                {
                    if (string.IsNullOrWhiteSpace(sourceOptions.MusicFile) || !File.Exists(sourceOptions.MusicFile))
                    {
                        if (!string.IsNullOrWhiteSpace(sourceOptions.MusicFile))
                        {
                            Log.LogWarning($"Music file not set or not found, using default music resource.");
                        }

                        EmbeddedFileProvider efp = new EmbeddedFileProvider(Assembly.GetExecutingAssembly());
                        var audioStreamFileInfo = efp.GetFileInfo(MUSIC_RESOURCE_PATH);
                        _audioStreamReader = new StreamReader(audioStreamFileInfo.CreateReadStream());
                    }
                    else
                    {
                        _audioStreamReader = new StreamReader(sourceOptions.MusicFile);
                    }

                    _audioStreamTimer = new Timer(SendMusicSample, null, 0, AUDIO_SAMPLE_PERIOD_MILLISECONDS);
                }

                _audioOpts = sourceOptions;
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
            if (audioStream != null && audioStream.Length > 0)
            {
                Log.LogDebug($"Sending audio stream length {audioStream.Length}.");

                _streamSendInProgress = true;

                _streamSourceSampleRate = streamSampleRate;
                _streamSourceReader = new BinaryReader(audioStream);
                _streamSourceTimer = new Timer(SendStreamSample, null, Timeout.Infinite, Timeout.Infinite);
            }
        }

        /// <summary>
        /// Sends audio samples read from a file.
        /// </summary>
        private void SendMusicSample(object state)
        {
            if (!_streamSendInProgress)
            {
                lock (_audioStreamTimer)
                {
                    int sampleRate = MUSIC_FILE_SAMPLE_RATE;
                    uint sampleSize = (uint)(sampleRate / 1000 * AUDIO_SAMPLE_PERIOD_MILLISECONDS);
                    byte[] sample = new byte[sampleSize * 2];

                    int bytesRead = _audioStreamReader.BaseStream.Read(sample, 0, sample.Length);

                    if (bytesRead > 0)
                    {
                        byte[] encodedSample = _audioEncoder.EncodeAudio(sample, _sendingFormat, AudioSamplingRatesEnum.Rate8KHz);
                        OnAudioSourceEncodedSample?.Invoke((uint)encodedSample.Length, encodedSample);
                    }

                    if (bytesRead == 0 || _audioStreamReader.EndOfStream)
                    {
                        _audioStreamReader.BaseStream.Position = 0;
                    }
                }
            }
        }

        /// <summary>
        /// Sends the sounds of silence.
        /// </summary>
        private void SendSilenceSample(object state)
        {
            if (!_streamSendInProgress)
            {
                lock (_audioStreamTimer)
                {
                    uint outputBufferSize = (uint)(_sendingAudioRtpRate / 1000 * AUDIO_SAMPLE_PERIOD_MILLISECONDS);
                    int sourceSampleRate = _sourceAudioSampleRate == AudioSamplingRatesEnum.Rate8KHz ? 8000 : 16000;

                    if (_sendingFormat == AudioCodecsEnum.G722)
                    {
                        int inputBufferSize = sourceSampleRate / 1000 * AUDIO_SAMPLE_PERIOD_MILLISECONDS;
                        short[] silencePcm = new short[inputBufferSize];

                        //OnAudioSourceRawSample?.Invoke(AudioSamplingRatesEnum.Rate8KHz, AUDIO_SAMPLE_PERIOD_MILLISECONDS, silencePcm);
                        byte[] encodedSample = _audioEncoder.EncodeAudio(silencePcm, AudioCodecsEnum.G722, _sourceAudioSampleRate);
                        OnAudioSourceEncodedSample?.Invoke(outputBufferSize, encodedSample);
                    }
                    else if (_sendingFormat == AudioCodecsEnum.PCMU
                            || _sendingFormat == AudioCodecsEnum.PCMA)
                    {
                        if (_silenceBuffer == null || _silenceBuffer.Length != outputBufferSize)
                        {
                            _silenceBuffer = new byte[outputBufferSize];
                            SetSilenceBuffer(_silenceBuffer, 0);
                        }

                        // No encoding required for PCMU/PCMA silence.
                        OnAudioSourceEncodedSample?.Invoke(outputBufferSize, _silenceBuffer);
                    }
                    else
                    {
                        Log.LogWarning($"SendSilenceSample does not know how to encode {_sendingFormat}.");
                    }
                }
            }
        }

        /// <summary>
        /// Sends a sample from a signal generator generated waveform.
        /// </summary>
        private void SendSignalGeneratorSample(object state)
        {
            if (!_streamSendInProgress)
            {
                lock (_audioStreamTimer)
                {
                    int sourceSampleRate = _sourceAudioSampleRate == AudioSamplingRatesEnum.Rate8KHz ? 8000 : 16000;
                    int inputBufferSize = sourceSampleRate / 1000 * AUDIO_SAMPLE_PERIOD_MILLISECONDS;
                    uint outputBufferSize = (uint)(_sendingAudioRtpRate / 1000 * AUDIO_SAMPLE_PERIOD_MILLISECONDS);

                    // Get the signal generator to generate the samples and then convert from
                    // signed linear to PCM.
                    float[] linear = new float[inputBufferSize];
                    _signalGenerator.Read(linear, 0, inputBufferSize);
                    short[] pcm = linear.Select(x => (short)(x * LINEAR_MAXIMUM)).ToArray();

                    byte[] encodedSample = _audioEncoder.EncodeAudio(pcm, _sendingFormat, _sourceAudioSampleRate);
                    OnAudioSourceEncodedSample?.Invoke(outputBufferSize, encodedSample);
                }
            }
        }

        /// <summary>
        /// Sends audio samples read from a file containing 16 bit PCM samples.
        /// </summary>
        private void SendStreamSample(object state)
        {
            lock (_streamSourceTimer)
            {
                if (_streamSourceReader?.BaseStream?.CanRead == true)
                {
                    int sampleRate = (_streamSourceSampleRate == AudioSamplingRatesEnum.Rate8KHz) ? 8000 : 16000;
                    int sampleSize = sampleRate / 1000 * AUDIO_SAMPLE_PERIOD_MILLISECONDS;
                    short[] sample = new short[sampleSize];
                    int samplesRead = 0;

                    for (int i = 0; i < sampleSize && _streamSourceReader.BaseStream.Position < _streamSourceReader.BaseStream.Length; i++)
                    {
                        sample[samplesRead++] = _streamSourceReader.ReadInt16();
                    }

                    if (samplesRead > 0)
                    {
                        //Log.LogDebug($"Audio stream reader bytes read {samplesRead}, sample rate{_streamSourceSampleRate}, sending codec {_sendingFormat}.");

                        if (samplesRead < sample.Length)
                        {
                            // If the sending codec supports it fill up any short samples with silence.
                            if (_sendingFormat == AudioCodecsEnum.PCMU ||
                                _sendingFormat == AudioCodecsEnum.PCMU)
                            {
                                SetSilenceBuffer(sample, samplesRead);
                            }
                        }

                        //OnAudioSourceRawSample?.Invoke(_streamSourceSampleRate, AUDIO_SAMPLE_PERIOD_MILLISECONDS, sample);
                        byte[] encodedSample = _audioEncoder.EncodeAudio(sample, _sendingFormat, _streamSourceSampleRate);
                        OnAudioSourceEncodedSample?.Invoke((uint)encodedSample.Length, encodedSample);

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

        /// <summary>
        /// Fills up the silence buffer for the sending format and period.
        /// </summary>
        /// <param name="length">The required length for the silence buffer.</param>
        private void SetSilenceBuffer(short[] buffer, int startPosn)
        {
            for (int index = startPosn; index < buffer.Length - 1; index++)
            {
                if (_sendingFormat == AudioCodecsEnum.PCMA)
                {
                    buffer[index] = (short)(PCMA_SILENCE_BYTE_ONE << 8 & 0xff00 + PCMA_SILENCE_BYTE_ZERO & 0x00ff);
                }
                else if (_sendingFormat == AudioCodecsEnum.PCMU)
                {
                    buffer[index] = (short)(PCMU_SILENCE_BYTE_ONE << 8 & 0xff00 + PCMU_SILENCE_BYTE_ZERO & 0x00ff);
                }
            }
        }

        /// <summary>
        /// Fills up the silence buffer for the sending format and period.
        /// </summary>
        /// <param name="length">The required length for the silence buffer.</param>
        private void SetSilenceBuffer(byte[] buffer, int startPosn)
        {
            for (int index = startPosn; index < buffer.Length - 1; index += 2)
            {
                if (_sendingFormat == AudioCodecsEnum.PCMA)
                {
                    buffer[index] = PCMA_SILENCE_BYTE_ZERO;
                    buffer[index + 1] = PCMA_SILENCE_BYTE_ONE;
                }
                else if (_sendingFormat == AudioCodecsEnum.PCMU)
                {
                    buffer[index] = PCMU_SILENCE_BYTE_ZERO;
                    buffer[index + 1] = PCMU_SILENCE_BYTE_ONE;
                }
            }
        }

        public void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample)
        {
            throw new NotImplementedException();
        }
    }
}
