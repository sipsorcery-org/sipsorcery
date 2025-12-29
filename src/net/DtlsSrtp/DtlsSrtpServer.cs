using System;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using SharpSRTP.SRTP;

namespace SIPSorcery.Net
{
    public class DtlsSrtpServer : DTLSSRTPServer, IDtlsSrtpPeer
    {
        public SRTPKeys Keys { get; private set; }
        public Certificate PeerCertificate => ClientCertificate;

        public event OnDtlsAlertEvent OnAlert;

        public DtlsSrtpServer(BcTlsCrypto crypto, Certificate dtlsCertificate, AsymmetricKeyParameter dtlsPrivateKey, short signatureAlgorithm = SignatureAlgorithm.rsa) : base(crypto)
        {
            SetCertificate(dtlsCertificate, dtlsPrivateKey, signatureAlgorithm);
        }

        public override void NotifyAlertReceived(short level, short alertDescription)
        {
            base.NotifyAlertReceived(level, alertDescription);

            AlertTypesEnum alertType = AlertTypesEnum.Unknown;
            if (Enum.IsDefined(typeof(AlertTypesEnum), (int)alertDescription))
            {
                alertType = (AlertTypesEnum)alertDescription;
            }

            AlertLevelsEnum alertLevel = AlertLevelsEnum.Warn;
            if (Enum.IsDefined(typeof(AlertLevelsEnum), (int)alertLevel))
            {
                alertLevel = (AlertLevelsEnum)level;
            }

            OnAlert?.Invoke(alertLevel, alertType, AlertDescription.GetText(alertDescription));
        }

        public override void NotifyHandshakeComplete()
        {
            base.NotifyHandshakeComplete();

            var securityParameters = m_context.SecurityParameters;
            this.Keys = SRTProtocol.GenerateMasterKeys(base.SrtpData.ProtectionProfiles[0], base.SrtpData.Mki, securityParameters, ForceUseExtendedMasterSecret);
        }
    }
}
