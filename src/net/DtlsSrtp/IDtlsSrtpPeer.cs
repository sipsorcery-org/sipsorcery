using Org.BouncyCastle.Tls;
using static SIPSorcery.net.DtlsSrtp.DtlsSrtpTransport;

namespace SIPSorcery.net.DtlsSrtp
{
    public interface IDtlsSrtpPeer
    {
        event OnDtlsAlertEvent OnAlert;
        public Certificate PeerCertificate { get; }
    }
}
