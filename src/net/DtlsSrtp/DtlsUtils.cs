using System;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.Tls.Crypto;
using SharpSRTP.DTLS;

namespace SIPSorcery.Net
{
    public class DtlsUtils
    {
        public static (Certificate certificate, AsymmetricKeyParameter privateKey) CreateSelfSignedTlsCert(BcTlsCrypto crypto, bool useRsa)
        {
            const string name = "WebRTC";
            DateTime notBefore = DateTime.UtcNow.AddDays(-1);
            DateTime notAfter = DateTime.UtcNow.AddDays(30);
            return DTLSCertificateUtils.GenerateServerCertificate(name, notBefore, notAfter, useRsa);
        }

        public static RTCDtlsFingerprint Fingerprint(string algorithm, TlsCertificate value)
        {
            return Fingerprint(new X509Certificate(value.GetEncoded()));
        }

        public static RTCDtlsFingerprint Fingerprint(Certificate certificate)
        {
            return Fingerprint(new X509Certificate(certificate.GetCertificateAt(0).GetEncoded()));
        }

        public static RTCDtlsFingerprint Fingerprint(X509Certificate x509Certificate)
        {
            string fingerprint = DTLSCertificateUtils.Fingerprint(x509Certificate.CertificateStructure, "SHA256");
            return new RTCDtlsFingerprint
            {
                algorithm = "sha-256",
                value = fingerprint
            };
        }

        public static bool IsHashSupported(string algStr)
        {
            return DTLSCertificateUtils.IsHashSupported(algStr);
        }
    }
}
