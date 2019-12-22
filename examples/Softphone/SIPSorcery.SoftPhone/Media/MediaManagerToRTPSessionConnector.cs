using SIPSorcery.SIP.App;

namespace SIPSorcery.SoftPhone
{
    public class MediaManagerToRTPSessionConnector
    {
        private readonly MediaManager _mediaManager;
        private readonly RTPMediaSession _mediaSession;

        public MediaManagerToRTPSessionConnector(MediaManager mediaManager, RTPMediaSession mediaSession)
        {
            _mediaManager = mediaManager;
            _mediaSession = mediaSession;

            _mediaManager.OnLocalAudioSampleReady += LocalAudioSampleReady;
            _mediaSession.OnReceivedSampleReady += RemoteAudioSampleReceived;

            mediaSession.Closed += () =>
            {
                _mediaManager.OnLocalAudioSampleReady -= LocalAudioSampleReady;
                _mediaSession.OnReceivedSampleReady -= RemoteAudioSampleReceived;
            };
        }

        private void RemoteAudioSampleReceived(byte[] sample)
        {
            _mediaManager.EncodedAudioSampleReceived(sample);
        }

        /// <summary>
        /// The RTP timestamp to set on audio packets sent for the RTP session.
        /// </summary>
        private uint _audioTimestamp = 0;

        /// <summary>
        /// Forwards samples from the local audio input device to RTP session.
        /// We leave it up to the RTP session to decide if it wants to transmit the sample or not.
        /// For example an RTP session will know whether it's on hold and whether it needs to send
        /// audio to the remote call party or not.
        /// </summary>
        /// <param name="sample">The sample from the audio input device.</param>
        private void LocalAudioSampleReady(byte[] sample)
        {
            _mediaSession.SendAudioFrame(_audioTimestamp, sample);
            _audioTimestamp += (uint)sample.Length; // This only works for cases where 1 sample is 1 byte.
        }
    }
}