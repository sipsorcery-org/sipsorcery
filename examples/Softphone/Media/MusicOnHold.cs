//-----------------------------------------------------------------------------
// Filename: MusicOnHold.cs
//
// Description: This class can be used to supply a stream of music on hold
// audio samples. A typical use case is for a VoIP client class to switch from
// audio input samples to music on hold samples when a remote party is put on 
// hold.
//
// Note: Currently only PCMU audio samples are available.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 24 Dec 2019	Aaron Clauson	Created, Dublin, Ireland
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.SoftPhone
{
    public class MusicOnHold
    {
        private const string AUDIO_FILE_PCMU = @"content\Macroform_-_Simplicity.ulaw";
        private const int AUDIO_SAMPLE_PERIOD_MILLISECONDS = 40;

        private ILogger logger = Log.Logger;

        private bool _stop = false;
        private Task _samplesTask;

        /// <summary>
        /// Fires when a music on hold audio sample is available.
        /// [sample].
        /// </summary>
        public event Action<byte[]> OnAudioSampleReady;

        /// <summary>
        /// Creates a default music on hold class.
        /// </summary>
        public MusicOnHold()
        {  }

        public void Start()
        {
            _stop = false;

            if (_samplesTask == null || _samplesTask.Status != TaskStatus.Running)
            {
                logger.LogDebug("Music on hold samples task starting.");

                _samplesTask = Task.Run(async () =>
                {
                    // Read the same file in an endless loop while samples are still required.
                    while (!_stop)
                    {
                        using (StreamReader sr = new StreamReader(AUDIO_FILE_PCMU))
                        {
                            int sampleSize = (SDPMediaFormatInfo.GetClockRate(SDPMediaFormatsEnum.PCMU) / 1000) * AUDIO_SAMPLE_PERIOD_MILLISECONDS;
                            byte[] sample = new byte[sampleSize];
                            int bytesRead = sr.BaseStream.Read(sample, 0, sample.Length);

                            while (bytesRead > 0 && !_stop)
                            {
                                if (OnAudioSampleReady == null)
                                {
                                    // Nobody needs music on hold so exit.
                                    logger.LogDebug("Music on hold has no subscribers, stopping.");
                                    return;
                                }
                                else
                                {
                                    OnAudioSampleReady(sample);
                                }

                                await Task.Delay(AUDIO_SAMPLE_PERIOD_MILLISECONDS);
                                bytesRead = sr.BaseStream.Read(sample, 0, sample.Length);
                            }
                        }
                    }
                });
            }
        }

        /// <summary>
        /// Called when the music on hold is no longer required.
        /// </summary>
        public void Stop()
        {
            _stop = true;
        }
    }
}
