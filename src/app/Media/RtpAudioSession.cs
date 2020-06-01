//-----------------------------------------------------------------------------
// Filename: RtpAudioSession.cs
//
// Description: A lightweight audio only RTP session suitable for testing.
// No rendering or capturing capabilities.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 19 Mar 2020	Aaron Clauson	Created, Dublin, Ireland.
// 21 Apr 2020  Aaron Clauson   Added alaw and mulaw decode classes.
// 31 May 2020  Aaron Clauson   Refactored codecs and signal generator to 
//                              separate class files.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;

namespace SIPSorcery.Media
{
    public enum AudioSourcesEnum
    {
        /// <summary>
        /// Plays music samples from a file. No transcoding option is available
        /// so the file format must match the selected codec.
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
        /// Don't send audio RTP packets.
        /// </summary>
        None = 5,

        /// <summary>
        /// Use this option for audio capture devices such as a microphone.
        /// </summary>
        External = 6,
    }

    /// <summary>
    /// The supported sampling rates for externally generated audio sources
    /// such as a microphone.
    /// </summary>
    public enum AudioExternalSamplingRatesEnum
    {
        SampleRate8KHz = 0,
        SampleRate16KHz = 1
    }

    public class AudioSourceOptions
    {
        /// <summary>
        /// The type of audio source to use.
        /// </summary>
        public AudioSourcesEnum AudioSource;

        /// <summary>
        /// If using a pre-recorded audio source this is the audio source file.
        /// </summary>
        public Dictionary<SDPMediaFormatsEnum, string> SourceFiles;

        /// <summary>
        /// The sampling rate for an externally generated audio source.
        /// </summary>
        public AudioExternalSamplingRatesEnum ExternalSourceSampleRate = AudioExternalSamplingRatesEnum.SampleRate8KHz;
    }

    /// <summary>
    /// An audio only RTP session that can supply an audio stream to the caller. Any incoming audio stream is 
    /// ignored and this class does NOT use any audio devices on the system for capture or playback.
    /// </summary>
    public class RtpAudioSession : RTPSession, IMediaSession
    {
        private const int SAMPLE_RATE = 8000;                 // G711 and G722 use an 8KHz for RTP timestamps clock.
        private const int G722_BIT_RATE = 64000;              // G722 sampling rate is 16KHz with bits per sample of 16.
        private const int AUDIO_SAMPLE_PERIOD_MILLISECONDS = 20;
        private static readonly byte PCMU_SILENCE_BYTE_ZERO = 0x7F;
        private static readonly byte PCMU_SILENCE_BYTE_ONE = 0xFF;
        private static readonly byte PCMA_SILENCE_BYTE_ZERO = 0x55;
        private static readonly byte PCMA_SILENCE_BYTE_ONE = 0xD5;

        private static ILogger Log = SIPSorcery.Sys.Log.Logger;

        private StreamReader _audioStreamReader;
        private SignalGenerator _signalGenerator;
        private Timer _audioStreamTimer;
        private AudioSourceOptions _audioOpts;
        private List<SDPMediaFormatsEnum> _audioCodecs; // The list of supported audio codecs.
        private SDPMediaFormat _sendingFormat;          // The codec that we've selected to send with (must be supported by remote party).
        private int _sendingAudioSampleRate;            // 8KHz for G711, 16KHz for G722.
        private int _sendingAudioRtpRate;               // 8Khz for both G711 and G722. Calculation included for clarity.
        private bool _isStarted = false;

        private G722Codec _g722Codec;
        private G722CodecState _g722CodecState;
        private G722Codec _g722Decoder;
        private G722CodecState _g722DecoderState;

        public uint RtpPacketsSent
        {
            get { return base.AudioRtcpSession.PacketsSentCount; }
        }

        public uint RtpPacketsReceived
        {
            get { return base.AudioRtcpSession.PacketsReceivedCount; }
        }

        /// <summary>
        /// Fires when an audio sample from the remote party has been decoded into an
        /// 8KHz PCM buffer. The 8KHz and 16KHz events originate from the same RTP stream
        /// so only one or the other should be handled.
        /// </summary>
        public event Action<byte[]> OnRemote8KHzPcmSampleReady;

        /// <summary>
        /// Fires when an audio sample from the remote party has been decoded into an
        /// 16KHz PCM buffer. The 8KHz and 16KHz events originate from the same RTP stream
        /// so only one or the other should be handled.
        /// </summary>
        public event Action<byte[]> OnRemote16KHzPcmSampleReady;

        /// <summary>
        /// Creates an audio only RTP session that can supply an audio stream to the caller.
        /// </summary>
        /// <param name="audioOptions">The options that determine the type of audio to stream to the remote party. Example
        /// type of audio sources are music, silence, white noise etc.</param>
        /// <param name="audioCodecs">The audio codecs to support.</param>
        /// <param name="bindAddress">Optional. If specified this address will be used as the bind address for any RTP
        /// and control sockets created. Generally this address does not need to be set. The default behaviour
        /// is to bind to [::] or 0.0.0.0,d depending on system support, which minimises network routing
        /// causing connection issues.</param>
        public RtpAudioSession(AudioSourceOptions audioOptions, List<SDPMediaFormatsEnum> audioCodecs, IPAddress bindAddress = null) :
            base(false, false, false, bindAddress)
        {
            if (audioCodecs == null || audioCodecs.Count() == 0)
            {
                _audioCodecs = new List<SDPMediaFormatsEnum> { SDPMediaFormatsEnum.PCMU, SDPMediaFormatsEnum.PCMA, SDPMediaFormatsEnum.G722 };
            }
            else if (audioCodecs.Any(x => !(x == SDPMediaFormatsEnum.PCMU || x == SDPMediaFormatsEnum.PCMA || x == SDPMediaFormatsEnum.G722)))
            {
                throw new ApplicationException("Only PCMA, PCMU and G722 audio codecs are supported.");
            }

            _audioOpts = audioOptions;
            _audioCodecs = audioCodecs ?? _audioCodecs;

            var audioCapabilities = new List<SDPMediaFormat>();
            foreach (var codec in _audioCodecs)
            {
                audioCapabilities.Add(new SDPMediaFormat(codec));
            }

            // RTP event support.
            SDPMediaFormat rtpEventFormat = new SDPMediaFormat(DTMF_EVENT_PAYLOAD_ID);
            rtpEventFormat.SetFormatAttribute($"{SDP.TELEPHONE_EVENT_ATTRIBUTE}/{SAMPLE_RATE}");
            rtpEventFormat.SetFormatParameterAttribute("0-16");
            audioCapabilities.Add(rtpEventFormat);

            // Add a local audio track to the RTP session.
            MediaStreamTrack audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, audioCapabilities);
            base.addTrack(audioTrack);
        }

        public override void Close(string reason)
        {
            base.Close(reason);

            _audioStreamTimer?.Dispose();
            _audioStreamReader?.Close();
        }

        /// <summary>
        /// Initialises the audio source as required.
        /// </summary>
        public override Task Start()
        {
            lock (this)
            {
                if (!_isStarted)
                {
                    _isStarted = true;

                    if (AudioLocalTrack == null || AudioLocalTrack.Capabilities == null || AudioLocalTrack.Capabilities.Count == 0)
                    {
                        throw new ApplicationException("Cannot start audio session without a local audio track being available.");
                    }
                    else if (AudioRemoteTrack == null || AudioRemoteTrack.Capabilities == null || AudioRemoteTrack.Capabilities.Count == 0)
                    {
                        throw new ApplicationException("Cannot start audio session without a remote audio track being available.");
                    }

                    _sendingFormat = base.GetSendingFormat(SDPMediaTypesEnum.audio);
                    _sendingAudioSampleRate = SDPMediaFormatInfo.GetClockRate(_sendingFormat.FormatCodec);
                    _sendingAudioRtpRate = SDPMediaFormatInfo.GetRtpClockRate(_sendingFormat.FormatCodec);

                    Log.LogDebug($"RTP audio session selected sending codec {_sendingFormat.FormatCodec}.");

                    if (_sendingFormat.FormatCodec == SDPMediaFormatsEnum.G722)
                    {
                        _g722Codec = new G722Codec();
                        _g722CodecState = new G722CodecState(G722_BIT_RATE, G722Flags.None);
                        _g722Decoder = new G722Codec();
                        _g722DecoderState = new G722CodecState(G722_BIT_RATE, G722Flags.None);
                    }

                    // If required start the audio source.
                    if (_audioOpts != null && _audioOpts.AudioSource != AudioSourcesEnum.None)
                    {
                        if (_audioOpts.AudioSource == AudioSourcesEnum.Silence)
                        {
                            _audioStreamTimer = new Timer(SendSilenceSample, null, 0, AUDIO_SAMPLE_PERIOD_MILLISECONDS);
                        }
                        else if (_audioOpts.AudioSource == AudioSourcesEnum.PinkNoise ||
                             _audioOpts.AudioSource == AudioSourcesEnum.WhiteNoise ||
                             _audioOpts.AudioSource == AudioSourcesEnum.SineWave)
                        {
                            _signalGenerator = new SignalGenerator(_sendingAudioSampleRate, 1);

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

                            _audioStreamTimer = new Timer(SendSignalGeneratorSample, null, 0, AUDIO_SAMPLE_PERIOD_MILLISECONDS);
                        }
                        else if (_audioOpts.AudioSource == AudioSourcesEnum.Music)
                        {
                            if (_audioOpts.SourceFiles == null || !_audioOpts.SourceFiles.ContainsKey(_sendingFormat.FormatCodec))
                            {
                                Log.LogWarning($"Source file not set for codec {_sendingFormat.FormatCodec}.");
                            }
                            else
                            {
                                string sourceFile = _audioOpts.SourceFiles[_sendingFormat.FormatCodec];

                                if (String.IsNullOrEmpty(sourceFile) || !File.Exists(sourceFile))
                                {
                                    Log.LogWarning("Could not start audio music source as the source file does not exist.");
                                }
                                else
                                {
                                    _audioStreamReader = new StreamReader(sourceFile);
                                    _audioStreamTimer = new Timer(SendMusicSample, null, 0, AUDIO_SAMPLE_PERIOD_MILLISECONDS);
                                }
                            }
                        }
                    }

                    base.OnRtpPacketReceived += RtpPacketReceived;
                }

                return base.Start();
            }
        }

        /// <summary>
        /// Sends the an externally generated sample. This is the method to call to
        /// transmit samples generated from an audio capture device such as a microphone.
        /// </summary>
        /// <param name="pcmSample">The PCM encoded sample to send.</param>
        public void SendExternalSample(byte[] pcmSample, int durationMilliseconds)
        {
            int outputBufferSize = _sendingAudioRtpRate / 1000 * durationMilliseconds;

            byte[] encodedSample = new byte[outputBufferSize];

            if (_sendingFormat.FormatCodec == SDPMediaFormatsEnum.G722)
            {
                if (_audioOpts.ExternalSourceSampleRate == AudioExternalSamplingRatesEnum.SampleRate16KHz)
                {
                    short[] pcm = new short[pcmSample.Length / 2];
                    for(int i=0; i< pcm.Length; i++)
                    {
                        pcm[i] = BitConverter.ToInt16(pcmSample, i * 2);
                    }

                    // No down sampling required.
                    _g722Codec.Encode(_g722CodecState, encodedSample, pcm, pcm.Length);
                }
            }
            else
            {
                if (_audioOpts.ExternalSourceSampleRate == AudioExternalSamplingRatesEnum.SampleRate8KHz)
                {
                    Func<short, byte> encode = (_sendingFormat.FormatCodec == SDPMediaFormatsEnum.PCMA) ?
                           (Func<short, byte>)ALawEncoder.LinearToALawSample : MuLawEncoder.LinearToMuLawSample;

                    for (int index = 0; index < pcmSample.Length; index++)
                    {
                        encodedSample[index] = encode(pcmSample[index]);
                    }
                }
            }

            SendAudioFrame((uint)outputBufferSize, (int)_sendingFormat.FormatCodec, encodedSample);
        }

        /// <summary>
        /// Event handler for receiving RTP packets from the remote party.
        /// </summary>
        /// <param name="mediaType">The media type of the packets.</param>
        /// <param name="rtpPacket">The RTP packet with the media sample.</param>
        private void RtpPacketReceived(SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                bool wants8kSamples = OnRemote8KHzPcmSampleReady != null;
                bool wants16kSamples = OnRemote16KHzPcmSampleReady != null;

                if (wants8kSamples || wants16kSamples)
                {
                    var sample = rtpPacket.Payload;

                    if (_sendingFormat.FormatCodec == SDPMediaFormatsEnum.G722)
                    {
                        short[] decodedPcm16k = new short[sample.Length * 2];
                        int decodedSampleCount = _g722Decoder.Decode(_g722DecoderState, decodedPcm16k, sample, sample.Length);

                        // The decoder provides short samples but streams and devices generally seem to want
                        // byte samples so convert them.
                        byte[] pcm8kBuffer = (wants8kSamples) ? new byte[decodedSampleCount] : null;
                        byte[] pcm16kBuffer = (wants16kSamples) ? new byte[decodedSampleCount * 2] : null;

                        for (int i = 0; i < decodedSampleCount; i++)
                        {
                            var bufferSample = BitConverter.GetBytes(decodedPcm16k[i]);

                            // For 8K samples the crude re-sampling to get from 16K to 8K is to skip 
                            // every second sample.
                            if (pcm8kBuffer != null && i % 2 == 0)
                            {
                                pcm8kBuffer[(i / 2) * 2] = bufferSample[0];
                                pcm8kBuffer[(i / 2) * 2 + 1] = bufferSample[1];
                            }

                            // G722 provides 16k samples.
                            if (pcm16kBuffer != null)
                            {
                                pcm16kBuffer[i * 2] = bufferSample[0];
                                pcm16kBuffer[i * 2 + 1] = bufferSample[1];
                            }
                        }

                        OnRemote8KHzPcmSampleReady?.Invoke(pcm8kBuffer);
                        OnRemote16KHzPcmSampleReady?.Invoke(pcm16kBuffer);
                    }
                    else if (_sendingFormat.FormatCodec == SDPMediaFormatsEnum.PCMA ||
                        _sendingFormat.FormatCodec == SDPMediaFormatsEnum.PCMU)
                    {
                        Func<byte, short> decode = (_sendingFormat.FormatCodec == SDPMediaFormatsEnum.PCMA) ?
                            (Func<byte, short>)ALawDecoder.ALawToLinearSample : MuLawDecoder.MuLawToLinearSample;

                        byte[] pcm8kBuffer = (wants8kSamples) ? new byte[sample.Length * 2] : null;
                        byte[] pcm16kBuffer = (wants16kSamples) ? new byte[sample.Length * 4] : null;

                        for (int i = 0; i < sample.Length; i++)
                        {
                            var bufferSample = BitConverter.GetBytes(decode(sample[i]));

                            // G711 samples at 8KHz.
                            if (pcm8kBuffer != null)
                            {
                                pcm8kBuffer[i * 2] = bufferSample[0];
                                pcm8kBuffer[i * 2 + 1] = bufferSample[1];
                            }

                            // The crude up-sampling approach to get 16K samples from G711 is to
                            // duplicate each 8K sample.
                            if (pcm16kBuffer != null)
                            {
                                pcm16kBuffer[i * 4] = bufferSample[0];
                                pcm16kBuffer[i * 4 + 1] = bufferSample[1];
                                pcm16kBuffer[i * 4 + 2] = bufferSample[0];
                                pcm16kBuffer[i * 4 + 3] = bufferSample[1];
                            }
                        }

                        OnRemote8KHzPcmSampleReady?.Invoke(pcm8kBuffer);
                        OnRemote16KHzPcmSampleReady?.Invoke(pcm16kBuffer);
                    }
                    else
                    {
                        // Ignore the sample. It's for an unsupported codec. It will be up to the application
                        // to decode.
                    }
                }
            }
        }

        /// <summary>
        /// Sends audio samples read from a file.
        /// </summary>
        private void SendMusicSample(object state)
        {
            lock (_audioStreamTimer)
            {
                int sampleSize = SAMPLE_RATE / 1000 * AUDIO_SAMPLE_PERIOD_MILLISECONDS;
                byte[] sample = new byte[sampleSize];

                int bytesRead = _audioStreamReader.BaseStream.Read(sample, 0, sample.Length);

                if (bytesRead > 0)
                {
                    SendAudioFrame((uint)sampleSize, (int)_sendingFormat.FormatCodec, sample);
                }

                if (bytesRead == 0 || _audioStreamReader.EndOfStream)
                {
                    _audioStreamReader.BaseStream.Position = 0;
                }
            }
        }

        /// <summary>
        /// Sends the sounds of silence.
        /// </summary>
        private void SendSilenceSample(object state)
        {
            lock (_audioStreamTimer)
            {
                int inputBufferSize = _sendingAudioSampleRate / 1000 * AUDIO_SAMPLE_PERIOD_MILLISECONDS;
                int outputBufferSize = _sendingAudioRtpRate / 1000 * AUDIO_SAMPLE_PERIOD_MILLISECONDS;

                byte[] encodedSample = new byte[outputBufferSize];

                if (_sendingFormat.FormatCodec == SDPMediaFormatsEnum.G722)
                {
                    short[] silencePcm = new short[inputBufferSize];
                    _g722Codec.Encode(_g722CodecState, encodedSample, silencePcm, inputBufferSize);
                }
                else
                {
                    for (int index = 0; index < outputBufferSize; index += 2)
                    {
                        if (_sendingFormat.FormatCodec == SDPMediaFormatsEnum.PCMA)
                        {
                            encodedSample[index] = PCMA_SILENCE_BYTE_ZERO;
                            encodedSample[index + 1] = PCMA_SILENCE_BYTE_ONE;
                        }
                        else if (_sendingFormat.FormatCodec == SDPMediaFormatsEnum.PCMU)
                        {
                            encodedSample[index] = PCMU_SILENCE_BYTE_ZERO;
                            encodedSample[index + 1] = PCMU_SILENCE_BYTE_ONE;
                        }
                    }
                }

                SendAudioFrame((uint)outputBufferSize, (int)_sendingFormat.FormatCodec, encodedSample);
            }
        }

        /// <summary>
        /// Sends a sample from a signal generator generated waveform.
        /// </summary>
        private void SendSignalGeneratorSample(object state)
        {
            lock (_audioStreamTimer)
            {
                int inputBufferSize = _sendingAudioSampleRate / 1000 * AUDIO_SAMPLE_PERIOD_MILLISECONDS;
                int outputBufferSize = _sendingAudioRtpRate / 1000 * AUDIO_SAMPLE_PERIOD_MILLISECONDS;

                // Get the signal generator to generate the samples and then convert from
                // signed linear to PCM.
                float[] linear = new float[inputBufferSize];
                _signalGenerator.Read(linear, 0, inputBufferSize);
                short[] pcm = linear.Select(x => (short)(x * 32767f)).ToArray();

                // Both G711 (lossless) and G722 (lossy) encode to 64Kbps (unless the 
                // hard coded G722 rate of 64000 is changed).
                byte[] encodedSample = new byte[outputBufferSize];

                if (_sendingFormat.FormatCodec == SDPMediaFormatsEnum.G722)
                {
                    _g722Codec.Encode(_g722CodecState, encodedSample, pcm, inputBufferSize);
                }
                else
                {
                    Func<short, byte> encode = (_sendingFormat.FormatCodec == SDPMediaFormatsEnum.PCMA) ?
                           (Func<short, byte>)ALawEncoder.LinearToALawSample : MuLawEncoder.LinearToMuLawSample;

                    for (int index = 0; index < inputBufferSize; index++)
                    {
                        encodedSample[index] = encode(pcm[index]);
                    }
                }

                SendAudioFrame((uint)outputBufferSize, (int)_sendingFormat.FormatCodec, encodedSample);
            }
        }
    }
}
