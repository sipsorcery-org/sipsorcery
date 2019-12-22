using System.Net;

namespace SIPSorcery.SIP.App
{
    public interface IMediaSessionFactory
    {
        IMediaSession Create(IPAddress address);
        IMediaSession Create(string offerSdp);
    }
}
