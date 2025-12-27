using System;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using SharpSRTP.SRTP;

namespace SIPSorcery.net.DtlsSrtp
{
    public class DtlsSrtpServer : SharpSRTP.SRTP.DTLSSRTPServer, IDtlsSrtpPeer
    {
        public event OnDtlsAlertEvent OnAlert;

        public DtlsSrtpServer(BcTlsCrypto crypto, Certificate dtlsCertificate, AsymmetricKeyParameter dtlsPrivateKey, short signatureAlgorithm = SignatureAlgorithm.rsa) : base(crypto)
        {
            SetCertificate(dtlsCertificate, dtlsPrivateKey, signatureAlgorithm);
        }

        public override void NotifyAlertReceived(short level, short alertDescription)
        {
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
            var securityParameters = m_context.SecurityParameters;
            this.Keys = SRTProtocol.GenerateMasterKeys(base._serverSrtpData.ProtectionProfiles[0], securityParameters);
        }

        public bool ForceUseExtendedMasterSecret { get; set; }
        public SRTPKeys Keys { get; private set; }

        public Certificate PeerCertificate => ClientCertificate;
    }
}
