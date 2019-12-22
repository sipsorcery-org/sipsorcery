using System;
using System.Net;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;

namespace SIPSorcery.SoftPhone
{
    public class RTPMediaSessionFactory
    {
        private readonly MediaManager _mediaManager;

        public RTPMediaSessionFactory(MediaManager mediaManager)
        {
            _mediaManager = mediaManager;
        }

        public SDPMediaFormatsEnum DefaultAudioFormat { get; set; } = SDPMediaFormatsEnum.PCMU;

        public virtual RTPMediaSession Create(IPAddress address)
        {
            var rtpSession = new RTPSession((int)DefaultAudioFormat, null, null, true, address.AddressFamily);

            var rtpMediaSession = new RTPMediaSession(rtpSession);

            void MediaManagerOnLocalAudioSampleReadyForSession(byte[] samples) =>
                LocalAudioSampleReady(rtpMediaSession, samples);

            rtpMediaSession.Closed += () => _mediaManager.OnLocalAudioSampleReady -= MediaManagerOnLocalAudioSampleReadyForSession;

            _mediaManager.OnLocalAudioSampleReady += MediaManagerOnLocalAudioSampleReadyForSession;
            rtpSession.OnReceivedSampleReady += RemoteAudioSampleReceived;

            return rtpMediaSession;
        }

        public virtual RTPMediaSession Create(string offerSdp)
        {
            var remoteSDP = SDP.ParseSDPDescription(offerSdp);
            var dstRtpEndPoint = remoteSDP.GetSDPRTPEndPoint();

            return Create(dstRtpEndPoint.Address);
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
        private void LocalAudioSampleReady(RTPMediaSession session, byte[] sample)
        {
            session.SendAudioFrame(_audioTimestamp, sample);
            _audioTimestamp += (uint)sample.Length; // This only works for cases where 1 sample is 1 byte.
        }
    }
}
