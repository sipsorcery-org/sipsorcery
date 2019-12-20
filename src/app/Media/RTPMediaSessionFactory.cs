using System;
using SIPSorcery.Net;

namespace SIPSorcery.SIP.App.Media
{
    public class RTPMediaSessionFactory : IMediaSessionFactory
    {
        public SDPMediaFormatsEnum DefaultAudioFormat { get; set; } = SDPMediaFormatsEnum.PCMU;

        public virtual IMediaSession Create()
        {
            var rtpSession = new RTPSession((int)DefaultAudioFormat, null, null, true);

            var rtpMediaSession = new RTPMediaSession(rtpSession);
            
            rtpMediaSession.Closed += () => SessionEnd?.Invoke(rtpMediaSession);
            SessionStart?.Invoke(rtpMediaSession);

            return rtpMediaSession;
        }

        public event Action<RTPMediaSession> SessionStart;
        public event Action<RTPMediaSession> SessionEnd;
    }
}
