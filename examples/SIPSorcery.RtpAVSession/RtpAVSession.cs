//-----------------------------------------------------------------------------
// Filename: RtpAVSession.cs
//
// Description: An example RTP audio/video session that can capture and render
// media on Windows.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 20 Feb 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using SIPSorcery.SIP.App;

namespace SIPSorcery.Net
{
    public class RtpAVSession : RTPMediaSession
    {
        public const int AUDIO_SAMPLE_PERIOD_MILLISECONDS = 20;

        /// <summary>
        /// PCMU encoding for silence, http://what-when-how.com/voip/g-711-compression-voip/
        /// </summary>
        private static readonly byte PCMU_SILENCE_BYTE_ZERO = 0x7F;
        private static readonly byte PCMU_SILENCE_BYTE_ONE = 0xFF;

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        private WaveFormat _waveFormat = new WaveFormat(8000, 16, 1);

        /// <summary>
        /// Audio render device.
        /// </summary>
        private WaveOutEvent _waveOutEvent;

        /// <summary>
        /// Buffer for audio samples to be rendered.
        /// </summary>
        private BufferedWaveProvider _waveProvider;

        /// <summary>
        /// Audio capture device.
        /// </summary>
        private WaveInEvent _waveInEvent;

        private uint _rtpAudioTimestamp = 0;
        private uint _rtpAudioTimestampPeriod = 0;
        private bool _isClosed = false;

        public RtpAVSession(SDPMediaTypesEnum mediaType, SDPMediaFormat codec, AddressFamily addrFamily)
            : base(mediaType, codec, addrFamily)
        {
            base.OnRtpPacketReceived += RtpPacketReceived;

            if (mediaType == SDPMediaTypesEnum.audio)
            {
                InitAudioDevices();
            }
        }

        /// <summary>
        /// Starts the media capturing devices.
        /// </summary>
        public void Start()
        {
            _waveOutEvent?.Play();
            _waveInEvent?.StartRecording();
        }

        public override void Close()
        {
            if (!_isClosed)
            {
                _isClosed = true;

                base.OnRtpPacketReceived -= RtpPacketReceived;

                _waveOutEvent?.Stop();
                _waveInEvent?.StopRecording();

                base.Close();
            }
        }

        /// <summary>
        /// Initialise the audio capture and render device.
        /// </summary>
        private void InitAudioDevices()
        {
            // Render device.
            _waveOutEvent = new WaveOutEvent();
            _waveProvider = new BufferedWaveProvider(_waveFormat);
            _waveProvider.DiscardOnBufferOverflow = true;
            _waveOutEvent.Init(_waveProvider);

            // Capture device.
            if (WaveInEvent.DeviceCount > 0)
            {
                _waveInEvent = new WaveInEvent();
                _waveInEvent.BufferMilliseconds = AUDIO_SAMPLE_PERIOD_MILLISECONDS;
                _waveInEvent.NumberOfBuffers = 1;
                _waveInEvent.DeviceNumber = 0;
                _waveInEvent.WaveFormat = _waveFormat;
                _waveInEvent.DataAvailable += LocalAudioSampleAvailable;
            }
            else
            {
                Log.LogWarning("No audio capture devices are available. A dummy silence stream will be sent.");

                // Send dummy silence audio packets to the remote party.
                _ = Task.Run(SendSilence);
            }

            _rtpAudioTimestampPeriod = (uint)(SDPMediaFormatInfo.GetClockRate(SDPMediaFormatsEnum.PCMU) / AUDIO_SAMPLE_PERIOD_MILLISECONDS);
        }

        /// <summary>
        /// Event handler for audio sample being supplied by local capture device.
        /// </summary>
        private void LocalAudioSampleAvailable(object sender, WaveInEventArgs args)
        {
            byte[] sample = new byte[args.Buffer.Length / 2];
            int sampleIndex = 0;

            for (int index = 0; index < args.BytesRecorded; index += 2)
            {
                var ulawByte = NAudio.Codecs.MuLawEncoder.LinearToMuLawSample(BitConverter.ToInt16(args.Buffer, index));
                sample[sampleIndex++] = ulawByte;
            }

            base.SendAudioFrame(_rtpAudioTimestamp, (int)SDPMediaFormatsEnum.PCMU, sample);
            _rtpAudioTimestamp += _rtpAudioTimestampPeriod;
        }

        /// <summary>
        /// Event handler for receiving RTP packets from a remote party.
        /// </summary>
        /// <param name="mediaType">The media type of the packets.</param>
        /// <param name="rtpPacket">The RTP packet with the media sample.</param>
        private void RtpPacketReceived(SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                RenderAudio(rtpPacket);
            }
            else if (mediaType == SDPMediaTypesEnum.video)
            {
                RenderVideo(rtpPacket);
            }
        }

        /// <summary>
        /// Render an audio RTP packet received from a remote party.
        /// </summary>
        /// <param name="rtpPacket">The RTP packet containing the audio payload.</param>
        private void RenderAudio(RTPPacket rtpPacket)
        {
            var sample = rtpPacket.Payload;
            for (int index = 0; index < sample.Length; index++)
            {
                short pcm = NAudio.Codecs.MuLawDecoder.MuLawToLinearSample(sample[index]);
                byte[] pcmSample = new byte[] { (byte)(pcm & 0xFF), (byte)(pcm >> 8) };
                _waveProvider.AddSamples(pcmSample, 0, 2);
            }
        }

        /// <summary>
        /// Render a video RTP packet received from a remote party.
        /// </summary>
        /// <param name="rtpPacket">The RTP packet containing the video payload.</param>
        private void RenderVideo(RTPPacket rtpPacket)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Sends the sounds of silence. If the destination is on the other side of a NAT this is useful to open
        /// a pinhole and hopefully get the remote RTP stream through.
        /// </summary>
        private async void SendSilence()
        {
            uint bufferSize = (uint)AUDIO_SAMPLE_PERIOD_MILLISECONDS;
            uint rtpSampleTimestamp = 0;

            while (!_isClosed)
            {
                byte[] sample = new byte[bufferSize / 2];
                int sampleIndex = 0;

                for (int index = 0; index < bufferSize; index += 2)
                {
                    sample[sampleIndex] = PCMU_SILENCE_BYTE_ZERO;
                    sample[sampleIndex + 1] = PCMU_SILENCE_BYTE_ONE;
                }

                SendAudioFrame(rtpSampleTimestamp, (int)SDPMediaFormatsEnum.PCMU, sample);
                rtpSampleTimestamp += _rtpAudioTimestampPeriod;

                await Task.Delay(AUDIO_SAMPLE_PERIOD_MILLISECONDS);
            }
        }
    }
}
