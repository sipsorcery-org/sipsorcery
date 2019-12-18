using System;
using System.Net;
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

        event Action<byte> DtmfCompleted;
    }
}
