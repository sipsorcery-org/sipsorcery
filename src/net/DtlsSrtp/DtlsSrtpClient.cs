using System;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace SIPSorcery.net.DtlsSrtp
{
    public class DtlsSrtpClient : SharpSRTP.DTLS.DtlsClient, IDtlsSrtpPeer
    {
        public DtlsSrtpClient(BcTlsCrypto crypto, Certificate dtlsCertificate, AsymmetricKeyParameter dtlsPrivateKey) : base(crypto)
        {
            base._myCert = dtlsCertificate;
            this._myCertKey = dtlsPrivateKey;
        }

        public bool ForceUseExtendedMasterSecret { get; set; }        
    }
}
