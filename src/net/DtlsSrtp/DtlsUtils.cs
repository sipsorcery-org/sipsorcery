using System;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.Tls.Crypto;
using SIPSorcery.Net.SharpSRTP.DTLS;

namespace SIPSorcery.Net
{
    public class DtlsUtils
    {
        public static (Certificate certificate, AsymmetricKeyParameter privateKey) CreateSelfSignedTlsCert(BcTlsCrypto crypto, bool useRsa = false)
        {
            return DtlsCertificateUtils.GenerateCertificate("WebRTC", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30), useRsa);
        }

        public static RTCDtlsFingerprint Fingerprint(string algorithm, TlsCertificate value)
        {
            return Fingerprint(new X509Certificate(value.GetEncoded()), algorithm);
        }

        public static RTCDtlsFingerprint Fingerprint(Certificate certificate)
        {
            return Fingerprint(new X509Certificate(certificate.GetCertificateAt(0).GetEncoded()));
        }

        public static RTCDtlsFingerprint Fingerprint(X509Certificate x509Certificate, string algorithm = "sha-256")
        {
            return new RTCDtlsFingerprint { algorithm = algorithm, value = DtlsCertificateUtils.Fingerprint(x509Certificate.CertificateStructure, algorithm) };
        }

        public static bool IsHashSupported(string algStr)
        {
            return DtlsCertificateUtils.IsHashSupported(algStr);
        }
    }
}
