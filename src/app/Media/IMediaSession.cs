using System;
using System.Net;
using System.Threading.Tasks;

namespace SIPSorcery.SIP.App
{
    /// <summary>
    /// Offering and Answering SDP messages so that it can be
    /// signaled to the other party using the SIPUserAgent.
    /// 
    /// The implementing class is responsible for ensuring that the client
    /// can send media to the other party including creating and managing
    /// the RTP streams and processing the audio and video.
    /// </summary>
    public interface IMediaSession
    {
        Task<string> CreateOffer(IPAddress destinationAddress = null);
        Task OfferAnswered(string remoteSDP);

        Task<string> AnswerOffer(string remoteSDP);
        Task<string> RemoteReInvite(string remoteSDP);

        void Close();

        event Action<string> SessionMediaChanged;
    }
}