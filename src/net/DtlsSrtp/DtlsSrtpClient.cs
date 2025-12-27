using System;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using SharpSRTP.SRTP;
using static SIPSorcery.net.DtlsSrtp.DtlsSrtpTransport;

namespace SIPSorcery.net.DtlsSrtp
{
    public class DtlsSrtpClient : SharpSRTP.DTLS.DtlsClient, IDtlsSrtpPeer
    {
        public event OnDtlsAlertEvent OnAlert;

        public DtlsSrtpClient(BcTlsCrypto crypto, Certificate dtlsCertificate, AsymmetricKeyParameter dtlsPrivateKey) : base(crypto)
        {
            base._myCert = dtlsCertificate;
            this._myCertKey = dtlsPrivateKey;
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
            this.Keys = SrtpKeyGenerator.GenerateMasterKeys(base._clientSrtpData.ProtectionProfiles[0], securityParameters);
        }

        public bool ForceUseExtendedMasterSecret { get; set; }
        public SrtpKeys Keys { get; private set; }

        public Certificate PeerCertificate => ServerCertificate?.Certificate;
    }
}
