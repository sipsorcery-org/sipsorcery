//-----------------------------------------------------------------------------
// Filename: PortAudioRtpSession.cs
//
// Description: Example of an RTP session that uses PortAUdio for audio
// capture and rendering. This class is a cut, paste and strip job from
// the RtpAvSession class in the SIPSorceryMedia library.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 17 Apr 2020 Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ProjectCeilidh.PortAudio;
using ProjectCeilidh.PortAudio.Native;
using SIPSorcery.Media;
using SIPSorcery.Net;

namespace demo
{
    public class PortAudioRtpSession : RtpAudioSession
    {
        private const int AUDIO_SAMPLE_BUFFER_LENGTH = 160;   // At 8Khz buffer of 160 corresponds to 20ms samples.
        private const int AUDIO_SAMPLING_RATE = 8000;
        private const float NORMALISE_FACTOR = 32768f;
        private const int SAMPLING_PERIOD_MILLISECONDS = 20;

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        /// <summary>
        /// Combined audio capture and render stream.
        /// </summary>
        //private PortAudioSharp.Stream _audioIOStream;
        private PortAudioDevice _outputDevice;
        private PortAudioDevicePump _outputDevicePump;

        private List<byte> _pendingRemoteSamples = new List<byte>();
        private ManualResetEventSlim _remoteSampleReady = new ManualResetEventSlim();
        private uint _rtpAudioTimestampPeriod = 0;
        private SDPMediaFormat _sendingAudioFormat = null;
        private bool _isStarted = false;
        private bool _isClosed = false;

        /// <summary>
        /// Creates a new basic RTP session that captures and renders audio to/from the default system devices.
        /// </summary>
        public PortAudioRtpSession()
            : base(new AudioSourceOptions { AudioSource = AudioSourcesEnum.Silence }, new List<SDPMediaFormatsEnum> { SDPMediaFormatsEnum.PCMU })
        {
            base.OnRemoteAudioSampleReady += PortAudioRtpSession_OnRemoteAudioSampleReady;
        }

        private void PortAudioRtpSession_OnRemoteAudioSampleReady(byte[] sample)
        {
            lock (_pendingRemoteSamples)
            {
                _pendingRemoteSamples.AddRange(sample);
            }

            _remoteSampleReady.Set();
        }

        /// <summary>
        /// Starts the media capturing/source devices.
        /// </summary>
        public override async Task Start()
        {
            if (!_isStarted)
            {
                await base.Start();

                _outputDevice = PortAudioHostApi.SupportedHostApis.Where(x => x.HostApiType == PortAudioHostApiType.Mme).First().DefaultOutputDevice;

                _outputDevicePump = new PortAudioDevicePump(_outputDevice, 1,
                                new PortAudioSampleFormat(PortAudioSampleFormat.PortAudioNumberFormat.Signed, 2),
                                TimeSpan.FromMilliseconds(SAMPLING_PERIOD_MILLISECONDS), AUDIO_SAMPLING_RATE, ReadAudioDataCalback);

                _outputDevicePump.Start();

                //PortAudio.Initialize();

                //var outputDevice = PortAudio.DefaultOutputDevice;
                //if (outputDevice == PortAudio.NoDevice)
                //{
                //    throw new ApplicationException("No audio output device available.");
                //}
                //else
                //{
                //    StreamParameters stmInParams = new StreamParameters { device = 0, channelCount = 2, sampleFormat = SampleFormat.Float32 };
                //    StreamParameters stmOutParams = new StreamParameters { device = outputDevice, channelCount = 2, sampleFormat = SampleFormat.Float32 };

                //    // Combined audio capture and render.
                //    _audioIOStream = new Stream(stmInParams, stmOutParams, AUDIO_SAMPLING_RATE, AUDIO_SAMPLE_BUFFER_LENGTH, StreamFlags.NoFlag, AudioSampleAvailable, null);
                //    _audioIOStream.Start();
                //}

            }
        }

        private int ReadAudioDataCalback(byte[] buffer, int offset, int count)
        {
            int bytesAvail = _pendingRemoteSamples.Count < count ? _pendingRemoteSamples.Count : count;

            if (bytesAvail == 0 && !_isClosed)
            {
                _remoteSampleReady.Reset();
                _remoteSampleReady.Wait();
                bytesAvail = _pendingRemoteSamples.Count < count ? _pendingRemoteSamples.Count : count;
            }

            if (bytesAvail > 0)
            {
                lock (_pendingRemoteSamples)
                {
                    Buffer.BlockCopy(_pendingRemoteSamples.ToArray(), 0, buffer, offset, bytesAvail);

                    if (bytesAvail == _pendingRemoteSamples.Count)
                    {
                        _pendingRemoteSamples.Clear();
                    }
                    else
                    {
                        _pendingRemoteSamples = _pendingRemoteSamples.Skip(bytesAvail).ToList();
                    }

                    return bytesAvail;
                }
            }
            else
            {
                return 0;
            }
        }

        /// <summary>
        /// Closes the session.
        /// </summary>
        /// <param name="reason">Reason for the closure.</param>
        public override void Close(string reason)
        {
            if (!_isClosed)
            {
                base.Close(reason);
                base.OnRemoteAudioSampleReady -= PortAudioRtpSession_OnRemoteAudioSampleReady;
                _outputDevicePump?.Dispose();
                _outputDevice?.Dispose();
            }
        }
    }
}
