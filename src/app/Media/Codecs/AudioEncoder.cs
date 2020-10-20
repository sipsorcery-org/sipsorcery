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
using System.Linq;
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

        public bool IsSupported(AudioFormat format)
        {
            switch (format.Codec)
            {
                case AudioCodecsEnum.G722:
                case AudioCodecsEnum.PCMA:
                case AudioCodecsEnum.PCMU:
                case AudioCodecsEnum.L16:
                case AudioCodecsEnum.PCM_S16LE:
                    return true;
                default:
                    return false;
            }
        }
  
        public byte[] EncodeAudio(short[] pcm, AudioFormat format)
        {
            if (format.Codec == AudioCodecsEnum.G722)
            {
                if (_g722Codec == null)
                {
                    _g722Codec = new G722Codec();
                    _g722CodecState = new G722CodecState(G722_BIT_RATE, G722Flags.None);
                }

                int outputBufferSize = pcm.Length / 2;
                byte[] encodedSample = new byte[outputBufferSize];
                int res = _g722Codec.Encode(_g722CodecState, encodedSample, pcm, pcm.Length);

                return encodedSample;
            }
            else if (format.Codec == AudioCodecsEnum.PCMA)
            {
                return pcm.Select(x => ALawEncoder.LinearToALawSample(x)).ToArray();
            }
            else if (format.Codec == AudioCodecsEnum.PCMU)
            {
                return pcm.Select(x => MuLawEncoder.LinearToMuLawSample(x)).ToArray();
            }
            else if (format.Codec == AudioCodecsEnum.L16)
            {
                // When netstandard2.1 can be used.
                //return MemoryMarshal.Cast<short, byte>(pcm)
                
                // Put on the wire in network byte order (big endian).
                return pcm.SelectMany(x => new byte[] { (byte)(x >> 8), (byte)(x) } ).ToArray();
            }
            else if (format.Codec == AudioCodecsEnum.PCM_S16LE)
            {
                // Put on the wire as little endian.
                return pcm.SelectMany(x => new byte[] { (byte)(x), (byte)(x >> 8) }).ToArray();
            }
            else
            {
                throw new ApplicationException($"Audio format {format.Codec} cannot be encoded.");
            }
        }

        /// <summary>
        /// Event handler for receiving RTP packets from the remote party.
        /// </summary>
        /// <param name="remoteEP">The remote end point the RTP was received from.</param>
        /// <param name="format">The audio format of the encoded packets.</param>
        /// <param name="rtpPacket">The RTP packet with the media sample.</param>
        public short[] DecodeAudio(byte[] encodedSample, AudioFormat format)
        {
            if (format.Codec == AudioCodecsEnum.G722)
            {
                if (_g722Decoder == null)
                {
                    _g722Decoder = new G722Codec();
                    _g722DecoderState = new G722CodecState(G722_BIT_RATE, G722Flags.None);
                }

                short[] decodedPcm = new short[encodedSample.Length * 2];
                int decodedSampleCount = _g722Decoder.Decode(_g722DecoderState, decodedPcm, encodedSample, encodedSample.Length);

                return decodedPcm.Take(decodedSampleCount).ToArray();
            }
            else if (format.Codec == AudioCodecsEnum.PCMA)
            {
                return encodedSample.Select(x => ALawDecoder.ALawToLinearSample(x)).ToArray();
            }
            else if (format.Codec == AudioCodecsEnum.PCMU)
            {
                return encodedSample.Select(x => MuLawDecoder.MuLawToLinearSample(x)).ToArray();
            }
            else if(format.Codec == AudioCodecsEnum.L16)
            {
                // Samples are on the wire as big endian.
                return encodedSample.Where((x, i) => i % 2 == 0).Select((y, i) => (short)(encodedSample[i * 2] << 8 | encodedSample[i * 2 + 1])).ToArray();
            }
            else if (format.Codec == AudioCodecsEnum.PCM_S16LE)
            {
                // Samples are on the wire as little endian (well unlikely to be on the wire in this case but when they 
                // arrive from somewhere like the SkypeBot SDK they will be in little endian format).
                return encodedSample.Where((x, i) => i % 2 == 0).Select((y, i) => (short)(encodedSample[i * 2 + 1] << 8 | encodedSample[i * 2])).ToArray();
            }
            else
            {
                throw new ApplicationException($"Audio format {format.Codec} cannot be decoded.");
            }
        }

        public short[] Resample(short[] pcm, int inRate, int outRate)
        {
            if (inRate == outRate)
            {
                return pcm;
            }
            else if (inRate == 8000 && outRate == 16000)
            {
                // Crude up-sample to 16Khz by doubling each sample.
                return pcm.SelectMany(x => new short[] { x, x }).ToArray();
            }
            else if (inRate == 8000 && outRate == 48000)
            {
                // Crude up-sample to 48Khz by 6x each sample. This sounds bad, use for testing only.
                return pcm.SelectMany(x => new short[] { x, x, x, x, x, x }).ToArray();
            }
            else if(inRate == 16000 && outRate == 8000)
            {
                // Crude down-sample to 8Khz by skipping every second sample.
                return pcm.Where((x, i) => i % 2 == 0).ToArray();
            }
            else if (inRate == 16000 && outRate == 48000)
            {
                // Crude up-sample to 48Khz by 3x each sample. This sounds bad, use for testing only.
                return pcm.SelectMany(x => new short[] { x, x, x }).ToArray();
            }
            else
            {
                throw new ApplicationException($"Sorry don't know how to re-sample PCM from {inRate} to {outRate}. Pull requests welcome!");
            }
        }
    }
}
