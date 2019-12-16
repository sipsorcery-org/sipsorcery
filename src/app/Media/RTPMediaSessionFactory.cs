using SIPSorcery.Net;

namespace SIPSorcery.SIP.App.Media
{
    public class RTPMediaSessionFactory : IMediaSessionFactory
    {
        public SDPMediaFormatsEnum DefaultAudioFormat { get; set; } = SDPMediaFormatsEnum.PCMU;

        public IMediaSession Create()
        {
            return new RTPMediaSession(new RTPSession((int)DefaultAudioFormat, null, null, true));
        }
    }
}
