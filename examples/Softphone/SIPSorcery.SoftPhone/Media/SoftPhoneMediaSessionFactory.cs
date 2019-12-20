using SIPSorcery.Net;
using SIPSorcery.SIP.App.Media;

namespace SIPSorcery.SoftPhone
{
    public class SoftPhoneMediaSessionFactory : IMediaSessionFactory
    {
        private readonly MediaManager _mediaManager;

        public SDPMediaFormatsEnum DefaultAudioFormat { get; set; } = SDPMediaFormatsEnum.PCMU;

        public SoftPhoneMediaSessionFactory(MediaManager mediaManager)
        {
            _mediaManager = mediaManager;
        }

        public IMediaSession Create()
        {
            var rtpSession = new RTPSession((int)DefaultAudioFormat, null, null, true);

            return new SoftPhoneMediaSession(rtpSession, _mediaManager);
        }
    }

    public class SoftPhoneRTPMediaSessionFactory : RTPMediaSessionFactory
    {
        public SoftPhoneRTPMediaSessionFactory(MediaManager mediaManager)
        {
            SessionStart += session =>
            {
                new MediaManagerToRTPSessionConnector(mediaManager, session);
            };
        }
    }
}