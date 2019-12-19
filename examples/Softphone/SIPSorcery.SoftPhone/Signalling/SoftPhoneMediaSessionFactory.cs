using SIPSorcery.Net;
using SIPSorcery.SIP.App.Media;

namespace SIPSorcery.SoftPhone
{
    public class SoftPhoneMediaSessionFactory : IMediaSessionFactory
    {
        public SDPMediaFormatsEnum DefaultAudioFormat { get; set; } = SDPMediaFormatsEnum.PCMU;

        public SoftPhoneMediaSessionFactory()
        {

        }

        public IMediaSession Create()
        {
            var rtpSession = new RTPSession((int)DefaultAudioFormat, null, null, true);

            return new RTPMediaSession(rtpSession);
        }
    }
}