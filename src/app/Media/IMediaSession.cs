using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Net;

namespace SIPSorcery.SIP.App.Media
{
    public interface IMediaSession
    {
        SDP CreateOffer(IPAddress destinationAddress);
        void OfferAnswered(SDP remoteSDP);

        SDP AnswerOffer(SDP remoteSDP);
        SDP ReInvite(SDP remoteSDP);

        void Close();

        Task SendDtmf(byte key, CancellationToken cancellationToken = default);

        event Action<byte> DtmfCompleted;
    }
}
