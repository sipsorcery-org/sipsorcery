using System;
using System.Net;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;

namespace SIPSorcery.SoftPhone
{
    public class RTPMediaSessionFactory
    {
        public SDPMediaFormatsEnum DefaultAudioFormat { get; set; } = SDPMediaFormatsEnum.PCMU;

        public virtual RTPMediaSession Create(IPAddress address)
        {
            var rtpSession = new RTPSession((int)DefaultAudioFormat, null, null, true, address.AddressFamily);

            var rtpMediaSession = new RTPMediaSession(rtpSession);

            rtpMediaSession.Closed += () => SessionEnd?.Invoke(rtpMediaSession);
            SessionStart?.Invoke(rtpMediaSession);

            return rtpMediaSession;
        }

        public virtual RTPMediaSession Create(string offerSdp)
        {
            var remoteSDP = SDP.ParseSDPDescription(offerSdp);
            var dstRtpEndPoint = remoteSDP.GetSDPRTPEndPoint();

            return Create(dstRtpEndPoint.Address);
        }

        public event Action<RTPMediaSession> SessionStart;
        public event Action<RTPMediaSession> SessionEnd;
    }
}
