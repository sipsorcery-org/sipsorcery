using System;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace SIPSorcery.net.DtlsSrtp
{
    public class DtlsSrtpClient : SharpSRTP.DTLS.DtlsClient, IDtlsSrtpPeer
    {
        private BcTlsCrypto crypto;
        private Certificate dtlsCertificate;
        private AsymmetricKeyParameter dtlsPrivateKey;

        public DtlsSrtpClient(BcTlsCrypto crypto, Certificate dtlsCertificate, AsymmetricKeyParameter dtlsPrivateKey) : base(null)
        {
            this.crypto = crypto;
            this.dtlsCertificate = dtlsCertificate;
            this.dtlsPrivateKey = dtlsPrivateKey;
        }

        public bool ForceUseExtendedMasterSecret { get; set; }        
    }
}
