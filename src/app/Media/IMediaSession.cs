using System;
using System.Net;
using SIPSorcery.Net;

namespace SIPSorcery.SIP.App.Media
{
    public interface IMediaSession
    {
        SDP GetOfferSDP(IPAddress destinationAddress);
        SDP GetAnswerSDP();

        void SetRemoteOfferSDP(SDP remoteSDP);
        void SetRemoteAnswerSDP(SDP remoteSDP);

        SDP ReInvite(SDP remoteSDP);

        void Close();

        event Action<byte> DtmfCompleted;
    }
}
