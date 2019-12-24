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
using log4net;
using SIPSorcery.Sys;

namespace SIPSorcery.SoftPhone
{
    public class MusicOnHold
    {
        private const string AUDIO_FILE_PCMU = @"content\Macroform_-_Simplicity.ulaw";
        private const int AUDIO_SAMPLE_PERIOD_MILLISECONDS = 40;

        private ILog logger = AppState.logger;

        private bool _stop = false;
        private bool _isMusicRunning = false;

        /// <summary>
        /// Fires when a music on hold audio sample is available.
        /// [sample].
        /// </summary>
        public event Action<byte[]> OnAudioSampleReady;

        /// <summary>
        /// Creates a default music on hold class.
        /// </summary>
        public MusicOnHold()
        { }

        public async void Start()
        {
            if (!_isMusicRunning)
            {
                _isMusicRunning = true;

                using (StreamReader sr = new StreamReader(AUDIO_FILE_PCMU))
                {
                    int sampleSize = 8000 / (1000 / AUDIO_SAMPLE_PERIOD_MILLISECONDS);
                    byte[] sample = new byte[sampleSize];
                    int bytesRead = sr.BaseStream.Read(sample, 0, sample.Length);

                    while (bytesRead > 0 && !_stop)
                    {
                        if (OnAudioSampleReady == null)
                        {
                            // Nobody needs music on hold so exit.
                            break;
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

            _isMusicRunning = false;
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
