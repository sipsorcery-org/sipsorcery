using SIPSorcery.Net;
using SIPSorcery.SIP.App;

namespace SIPSorcery.SoftPhone
{
    public class SoftPhoneMediaSession : RTPMediaSession
    {
        private readonly MediaManager _mediaManager;

        public SoftPhoneMediaSession(RTPSession rtpSession, MediaManager mediaManager) : base(rtpSession)
        {
            _mediaManager = mediaManager;
            _mediaManager.OnLocalAudioSampleReady += LocalAudioSampleReady;
            Session.OnReceivedSampleReady += RemoteAudioSampleReceived;
        }

        public override void Close()
        {
            _mediaManager.OnLocalAudioSampleReady -= LocalAudioSampleReady;
            Session.OnReceivedSampleReady -= RemoteAudioSampleReceived;
            base.Close();
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
            Session.SendAudioFrame(_audioTimestamp, sample);
            _audioTimestamp += (uint)sample.Length; // This only works for cases where 1 sample is 1 byte.
        }
    }
}