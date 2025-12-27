using Org.BouncyCastle.Tls;

namespace SIPSorcery.net.DtlsSrtp
{
    public delegate void OnDtlsAlertEvent(AlertLevelsEnum alertLevel, AlertTypesEnum alertType, string alertDescription);

    public interface IDtlsSrtpPeer
    {
        event OnDtlsAlertEvent OnAlert;
        public Certificate PeerCertificate { get; }
    }
}
