using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace SIPSorcery.SIP.App.Media
{
    public interface IMediaSession
    {
        string CreateOffer(IPAddress destinationAddress = null);
        void OfferAnswered(string remoteSDP);

        string AnswerOffer(string remoteSDP);
        string RemoteReInvite(string remoteSDP);

        void Close();

        Task SendDtmf(byte key, CancellationToken cancellationToken = default);

        event Action<byte> DtmfCompleted;

        MediaState MediaState { get; }
    }
}