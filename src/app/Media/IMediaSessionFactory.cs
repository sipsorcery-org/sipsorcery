using System.Net;

namespace SIPSorcery.SIP.App.Media
{
    public interface IMediaSessionFactory
    {
        IMediaSession Create(IPAddress address);
        IMediaSession Create(string offerSdp);
    }
}
