using System;
using System.Net;
using SIPSorcery.Net;

namespace SIPSorcery.SIP.App.Media
{
    public class RTPMediaSessionFactory : IMediaSessionFactory
    {
        public SDPMediaFormatsEnum DefaultAudioFormat { get; set; } = SDPMediaFormatsEnum.PCMU;

        public virtual IMediaSession Create(IPAddress address)
        {
            var rtpSession = new RTPSession((int)DefaultAudioFormat, null, null, true, address.AddressFamily);

            var rtpMediaSession = new RTPMediaSession(rtpSession);

            rtpMediaSession.Closed += () => SessionEnd?.Invoke(rtpMediaSession);
            SessionStart?.Invoke(rtpMediaSession);

            return rtpMediaSession;
        }

        public IMediaSession Create(string offerSdp)
        {
            var remoteSDP = SDP.ParseSDPDescription(offerSdp);
            var dstRtpEndPoint = remoteSDP.GetSDPRTPEndPoint();

            return Create(dstRtpEndPoint.Address);
        }

        public event Action<RTPMediaSession> SessionStart;
        public event Action<RTPMediaSession> SessionEnd;
    }
}
