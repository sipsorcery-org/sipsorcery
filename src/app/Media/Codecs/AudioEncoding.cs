using System;
using System.Collections.Generic;
using System.Text;
using SIPSorcery.Net;

namespace SIPSorcery.Media
{
    /// <summary>
    /// The supported sampling rates for externally generated audio sources
    /// such as a microphone.
    /// </summary>
    public enum AudioSamplingRatesEnum
    {
        SampleRate8KHz = 0,
        SampleRate16KHz = 1
    }

    public class AudioEncoder
    {
        private const int G722_BIT_RATE = 64000;              // G722 sampling rate is 16KHz with bits per sample of 16.

        private G722Codec _g722Codec;
        private G722CodecState _g722CodecState;
        private G722Codec _g722Decoder;
        private G722CodecState _g722DecoderState;

        public byte[] EncodeAudio(byte[] pcm, SDPMediaFormat format, AudioSamplingRatesEnum sampleRate)
        {
            byte[] encodedSample = null;

            // Convert buffer into a PCM sample (array of signed shorts) that's
            // suitable for input into the chosen encoder.
            short[] pcmSigned = new short[pcm.Length / 2];
            for (int i = 0; i < pcmSigned.Length; i++)
            {
                pcmSigned[i] = BitConverter.ToInt16(pcm, i * 2);
            }

            if (format.FormatCodec == SDPMediaFormatsEnum.G722)
            {
                if(_g722Codec == null)
                {
                    _g722Codec = new G722Codec();
                    _g722CodecState = new G722CodecState(G722_BIT_RATE, G722Flags.None);
                }

                if (sampleRate == AudioSamplingRatesEnum.SampleRate16KHz)
                {
                    // No up sampling required.
                    int outputBufferSize = pcmSigned.Length / 2;
                    encodedSample = new byte[outputBufferSize];
                    int res = _g722Codec.Encode(_g722CodecState, encodedSample, pcmSigned, pcmSigned.Length);
                }
                else
                {
                    // Up sample the supplied PCM signal by doubling each sample.
                    int outputBufferSize = pcmSigned.Length;
                    encodedSample = new byte[outputBufferSize];

                    short[] pcmUpsampled = new short[pcmSigned.Length * 2];
                    for (int i = 0; i < pcmSigned.Length; i++)
                    {
                        pcmUpsampled[i * 2] = pcmSigned[i];
                        pcmUpsampled[i * 2 + 1] = pcmSigned[i];
                    }

                    _g722Codec.Encode(_g722CodecState, encodedSample, pcmUpsampled, pcmUpsampled.Length);
                }

                return encodedSample;
            }
            else if (format.FormatCodec == SDPMediaFormatsEnum.PCMA ||
                     format.FormatCodec == SDPMediaFormatsEnum.PCMU)
            {
                Func<short, byte> encode = (format.FormatCodec == SDPMediaFormatsEnum.PCMA) ?
                       (Func<short, byte>)ALawEncoder.LinearToALawSample : MuLawEncoder.LinearToMuLawSample;

                if (sampleRate == AudioSamplingRatesEnum.SampleRate8KHz)
                {
                    // No down sampling required.
                    int outputBufferSize = pcmSigned.Length;
                    encodedSample = new byte[outputBufferSize];

                    for (int index = 0; index < pcmSigned.Length; index++)
                    {
                        encodedSample[index] = encode(pcmSigned[index]);
                    }
                }
                else
                {
                    // Down sample the supplied PCM signal by skipping every second sample.
                    int outputBufferSize = pcmSigned.Length / 2;
                    encodedSample = new byte[outputBufferSize];
                    int encodedIndex = 0;

                    // Skip every second sample.
                    for (int index = 0; index < pcmSigned.Length; index += 2)
                    {
                        encodedSample[encodedIndex++] = encode(pcmSigned[index]);
                    }
                }

                return encodedSample;
            }
            else
            {
                throw new ApplicationException($"Audio format {format} cannot be encoded.");
            }
        }

        /// <summary>
        /// Event handler for receiving RTP packets from the remote party.
        /// </summary>
        /// <param name="remoteEP">The remote end point the RTP was received from.</param>
        /// <param name="mediaType">The media type of the packets.</param>
        /// <param name="rtpPacket">The RTP packet with the media sample.</param>
        public byte[] DecodeAudio(byte[] encodedSample, SDPMediaFormat format, AudioSamplingRatesEnum sampleRate)
        {
            bool wants8kSamples = sampleRate == AudioSamplingRatesEnum.SampleRate8KHz;
            bool wants16kSamples = sampleRate == AudioSamplingRatesEnum.SampleRate16KHz;

            if (format.FormatCodec == SDPMediaFormatsEnum.G722)
            {
                if (_g722Decoder == null)
                {
                    _g722Decoder = new G722Codec();
                    _g722DecoderState = new G722CodecState(G722_BIT_RATE, G722Flags.None);
                }

                short[] decodedPcm16k = new short[encodedSample.Length * 2];
                int decodedSampleCount = _g722Decoder.Decode(_g722DecoderState, decodedPcm16k, encodedSample, encodedSample.Length);

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

                return pcm8kBuffer ?? pcm16kBuffer;
            }
            else if (format.FormatCodec == SDPMediaFormatsEnum.PCMA ||
                     format.FormatCodec == SDPMediaFormatsEnum.PCMU)
            {
                Func<byte, short> decode = (format.FormatCodec == SDPMediaFormatsEnum.PCMA) ?
                    (Func<byte, short>)ALawDecoder.ALawToLinearSample : MuLawDecoder.MuLawToLinearSample;

                byte[] pcm8kBuffer = (wants8kSamples) ? new byte[encodedSample.Length * 2] : null;
                byte[] pcm16kBuffer = (wants16kSamples) ? new byte[encodedSample.Length * 4] : null;

                for (int i = 0; i < encodedSample.Length; i++)
                {
                    var bufferSample = BitConverter.GetBytes(decode(encodedSample[i]));

                    // G711 samples at 8KHz.
                    if (pcm8kBuffer != null)
                    {
                        pcm8kBuffer[i * 2] = bufferSample[0];
                        pcm8kBuffer[i * 2 + 1] = bufferSample[1];
                    }

                    // The crude up-sampling approach to get 16K samples from G711 is to
                    // duplicate each 8K sample.
                    // TODO: This re-sampling approach introduces artifacts. Applying a low pass
                    // filter seems to be recommended.
                    if (pcm16kBuffer != null)
                    {
                        pcm16kBuffer[i * 4] = bufferSample[0];
                        pcm16kBuffer[i * 4 + 1] = bufferSample[1];
                        pcm16kBuffer[i * 4 + 2] = bufferSample[0];
                        pcm16kBuffer[i * 4 + 3] = bufferSample[1];
                    }
                }

                return pcm8kBuffer ?? pcm16kBuffer;
            }
            else
            {
                throw new ApplicationException($"Audio format {format} cannot be decoded.");
            }
        }
    }
}
