//-----------------------------------------------------------------------------
// Filename: PortAudioRtpSession.cs
//
// Description: Example of an RTP session that uses PortAudio for audio
// capture and rendering.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 17 Apr 2020 Aaron Clauson	Created, Dublin, Ireland.
// 01 Aug 2020  Aaron Clauson   Switched from PortAudioSharp to 
//                              ProjectCeilidh.PortAudio.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProjectCeilidh.PortAudio;
using SIPSorcery.Media;
using SIPSorcery.Net;

namespace demo
{
    public class PortAudioRtpSession : RtpAudioSession
    {
        private const int AUDIO_SAMPLING_RATE = 8000;
        private const int SAMPLING_PERIOD_MILLISECONDS = 20;
        private const int AUDIO_CHANNEL_COUNT = 1;
        private const int AUDIO_BYTES_PER_SAMPLE = 2; // 16 bit samples.

        private PortAudioDevice _portAudioOutputDevice;
        private PortAudioDevice _portAudioInputDevice;
        private PortAudioDevicePump _outputDevicePump;
        private PortAudioDevicePump _inputDevicePump;

        private List<byte> _pendingRemoteSamples = new List<byte>();
        private ManualResetEventSlim _remoteSampleReady = new ManualResetEventSlim();
        private bool _isStarted = false;
        private bool _isClosed = false;

        /// <summary>
        /// Creates a new basic RTP session that captures and renders audio to/from the default system devices.
        /// </summary>
        public PortAudioRtpSession()
            : base(new AudioSourceOptions { AudioSource = AudioSourcesEnum.CaptureDevice }, new List<SDPMediaFormatsEnum> { SDPMediaFormatsEnum.PCMU })
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

                var apiType = PortAudioHostApiType.DirectSound;

                if(Environment.OSVersion.Platform == PlatformID.Unix)
                {
                    apiType = PortAudioHostApiType.Alsa;
                }

                _portAudioOutputDevice = PortAudioHostApi.SupportedHostApis.Where(x => x.HostApiType == apiType).First().DefaultOutputDevice;

                _outputDevicePump = new PortAudioDevicePump(_portAudioOutputDevice, AUDIO_CHANNEL_COUNT,
                                new PortAudioSampleFormat(PortAudioSampleFormat.PortAudioNumberFormat.Signed, AUDIO_BYTES_PER_SAMPLE),
                                TimeSpan.FromMilliseconds(SAMPLING_PERIOD_MILLISECONDS), AUDIO_SAMPLING_RATE, ReadAudioDataCalback);

                _portAudioInputDevice = PortAudioHostApi.SupportedHostApis.Where(x => x.HostApiType == apiType).First().DefaultInputDevice;

                _inputDevicePump = new PortAudioDevicePump(_portAudioInputDevice, AUDIO_CHANNEL_COUNT,
                                new PortAudioSampleFormat(PortAudioSampleFormat.PortAudioNumberFormat.Signed, AUDIO_BYTES_PER_SAMPLE),
                                TimeSpan.FromMilliseconds(SAMPLING_PERIOD_MILLISECONDS), AUDIO_SAMPLING_RATE, WriteDataCallback);

                _outputDevicePump.Start();
                _inputDevicePump.Start();
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

        void WriteDataCallback(byte[] buffer, int offset, int count)
        {
            base.SendAudioSample(buffer, count - offset, SAMPLING_PERIOD_MILLISECONDS);
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
                _inputDevicePump?.Dispose();
                _portAudioOutputDevice?.Dispose();
                _portAudioInputDevice?.Dispose();
            }
        }
    }
}
