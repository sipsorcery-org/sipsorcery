//-----------------------------------------------------------------------------
// Filename: AudioEncoder.cs
//
// Description: Audio codecs for the simpler codecs.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 04 Sep 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using SIPSorceryMedia.Abstractions.V1;

namespace SIPSorcery.Media
{
    public class AudioEncoder : IAudioEncoder
    {
        private const int G722_BIT_RATE = 64000;              // G722 sampling rate is 16KHz with bits per sample of 16.

        private G722Codec _g722Codec;
        private G722CodecState _g722CodecState;
        private G722Codec _g722Decoder;
        private G722CodecState _g722DecoderState;

        public bool IsSupported(AudioCodecsEnum codec)
        {
            switch(codec)
            {
                case AudioCodecsEnum.G722:
                case AudioCodecsEnum.PCMA:
                case AudioCodecsEnum.PCMU:
                    return true;
                default:
                    return false;
            }
        }

        public byte[] EncodeAudio(byte[] pcm, AudioCodecsEnum codec, AudioSamplingRatesEnum sampleRate)
        {
            // Convert buffer into a PCM sample (array of signed shorts) that's
            // suitable for input into the chosen encoder.
            short[] pcmSigned = new short[pcm.Length / 2];
            for (int i = 0; i < pcmSigned.Length; i++)
            {
                pcmSigned[i] = BitConverter.ToInt16(pcm, i * 2);
            }

            return EncodeAudio(pcmSigned, codec, sampleRate);
        }

        public byte[] EncodeAudio(short[] pcm, AudioCodecsEnum codec, AudioSamplingRatesEnum sampleRate)
        {
            byte[] encodedSample = null;

            if (codec == AudioCodecsEnum.G722)
            {
                if (_g722Codec == null)
                {
                    _g722Codec = new G722Codec();
                    _g722CodecState = new G722CodecState(G722_BIT_RATE, G722Flags.None);
                }

                if (sampleRate == AudioSamplingRatesEnum.Rate16KHz)
                {
                    // No up sampling required.
                    int outputBufferSize = pcm.Length / 2;
                    encodedSample = new byte[outputBufferSize];
                    int res = _g722Codec.Encode(_g722CodecState, encodedSample, pcm, pcm.Length);
                }
                else
                {
                    // Up sample the supplied PCM signal by doubling each sample.
                    int outputBufferSize = pcm.Length;
                    encodedSample = new byte[outputBufferSize];

                    short[] pcmUpsampled = new short[pcm.Length * 2];
                    for (int i = 0; i < pcm.Length; i++)
                    {
                        pcmUpsampled[i * 2] = pcm[i];
                        pcmUpsampled[i * 2 + 1] = pcm[i];
                    }

                    _g722Codec.Encode(_g722CodecState, encodedSample, pcmUpsampled, pcmUpsampled.Length);
                }

                return encodedSample;
            }
            else if (codec == AudioCodecsEnum.PCMA ||
                     codec == AudioCodecsEnum.PCMU)
            {
                Func<short, byte> encode = (codec == AudioCodecsEnum.PCMA) ?
                       (Func<short, byte>)ALawEncoder.LinearToALawSample : MuLawEncoder.LinearToMuLawSample;

                if (sampleRate == AudioSamplingRatesEnum.Rate8KHz)
                {
                    // No down sampling required.
                    int outputBufferSize = pcm.Length;
                    encodedSample = new byte[outputBufferSize];

                    for (int index = 0; index < pcm.Length; index++)
                    {
                        encodedSample[index] = encode(pcm[index]);
                    }
                }
                else
                {
                    // Down sample the supplied PCM signal by skipping every second sample.
                    int outputBufferSize = pcm.Length / 2;
                    encodedSample = new byte[outputBufferSize];
                    int encodedIndex = 0;

                    // Skip every second sample.
                    for (int index = 0; index < pcm.Length; index += 2)
                    {
                        encodedSample[encodedIndex++] = encode(pcm[index]);
                    }
                }

                return encodedSample;
            }
            else
            {
                throw new ApplicationException($"Audio format {codec} cannot be encoded.");
            }
        }

        /// <summary>
        /// Event handler for receiving RTP packets from the remote party.
        /// </summary>
        /// <param name="remoteEP">The remote end point the RTP was received from.</param>
        /// <param name="codec">The encoding codec of the packets.</param>
        /// <param name="rtpPacket">The RTP packet with the media sample.</param>
        public byte[] DecodeAudio(byte[] encodedSample, AudioCodecsEnum codec, AudioSamplingRatesEnum sampleRate)
        {
            bool wants8kSamples = sampleRate == AudioSamplingRatesEnum.Rate8KHz;
            bool wants16kSamples = sampleRate == AudioSamplingRatesEnum.Rate16KHz;

            if (codec == AudioCodecsEnum.G722)
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
            else if (codec == AudioCodecsEnum.PCMA ||
                     codec == AudioCodecsEnum.PCMU)
            {
                Func<byte, short> decode = (codec == AudioCodecsEnum.PCMA) ?
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
                throw new ApplicationException($"Audio format {codec} cannot be decoded.");
            }
        }
    }
}
