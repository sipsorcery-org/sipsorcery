// SharpSRTP
// Copyright (C) 2025 Lukas Volf
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE 
// SOFTWARE.

using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Utilities.Encoders;
using Org.BouncyCastle.X509;
using System.Collections.Generic;
using System.Text;
using System;

namespace SIPSorcery.Net.SharpSRTP.DTLS
{
    public class DtlsCertificateUtils
    {
        public static (Certificate Certificate, AsymmetricKeyParameter PrivateKey) GenerateCertificate(
            string name,
            DateTime notBefore,
            DateTime notAfter,
            bool useRSA)
        {
            if (useRSA)
            {
                return GenerateRSACertificate(name, notBefore, notAfter);
            }
            else
            {
                return GenerateECDSACertificate(name, notBefore, notAfter);
            }
        }

        public static (Certificate Certificate, AsymmetricKeyParameter PrivateKey) GenerateRSACertificate(
            string name,
            DateTime notBefore,
            DateTime notAfter,
            int keyStrength = 2048,
            string signatureAlgorithm = "SHA256WITHRSA",
            int serialNumberLength = 20)
        {
            var randomGenerator = new CryptoApiRandomGenerator();
            var random = new SecureRandom(randomGenerator);

            var keyGenerationParameters = new KeyGenerationParameters(random, keyStrength);
            var keyPairGenerator = new RsaKeyPairGenerator();
            keyPairGenerator.Init(keyGenerationParameters);

            AsymmetricCipherKeyPair subjectKeyPair = keyPairGenerator.GenerateKeyPair();
            AsymmetricCipherKeyPair issuerKeyPair = subjectKeyPair;
            ISignatureFactory signatureFactory = new Asn1SignatureFactory(signatureAlgorithm, issuerKeyPair.Private, random);

            var certificateGenerator = new X509V3CertificateGenerator();
            certificateGenerator.SetNotBefore(notBefore);
            certificateGenerator.SetNotAfter(notAfter);

            var nameOids = new List<DerObjectIdentifier>
            {
                X509Name.CN
            };

            var nameValues = new Dictionary<DerObjectIdentifier, string>()
            {
                { X509Name.CN, name }
            };

            var subjectDN = new X509Name(nameOids, nameValues);
            var issuerDN = subjectDN;

            certificateGenerator.SetIssuerDN(issuerDN);
            certificateGenerator.SetSubjectDN(subjectDN);
            certificateGenerator.SetPublicKey(issuerKeyPair.Public);

            byte[] serial = new byte[serialNumberLength];
            random.NextBytes(serial);
            serial[0] = 1;
            certificateGenerator.SetSerialNumber(new Org.BouncyCastle.Math.BigInteger(serial));

            X509Certificate x509Certificate = certificateGenerator.Generate(signatureFactory);
            AsymmetricKeyParameter privateKey = issuerKeyPair.Private;

            var crypto = new BcTlsCrypto();
            var tlsCertificate = crypto.CreateCertificate(x509Certificate.GetEncoded());
            var certificate = new Certificate(new TlsCertificate[] { tlsCertificate });

            return (certificate, privateKey);
        }

        public static (Certificate certificate, AsymmetricKeyParameter key) GenerateECDSACertificate(
            string name,
            DateTime notBefore,
            DateTime notAfter,
            string curve = "secp256r1",
            string signatureAlgorithm = "SHA256WITHECDSA",
            int serialNumberLength = 20)
        {
            var randomGenerator = new CryptoApiRandomGenerator();
            var random = new SecureRandom(randomGenerator);

            var spec = ECNamedCurveTable.GetByName(curve);
            var curveOid = ECNamedCurveTable.GetOid(curve);
            var domainParams = new ECNamedDomainParameters(curveOid, spec.Curve, spec.G, spec.N, spec.H, spec.GetSeed());

            var keyPairGenerator = new ECKeyPairGenerator("EC");
            ECKeyGenerationParameters keyGenerationParameters = new ECKeyGenerationParameters(domainParams, random);
            keyPairGenerator.Init(keyGenerationParameters);

            AsymmetricCipherKeyPair subjectKeyPair = keyPairGenerator.GenerateKeyPair();
            AsymmetricCipherKeyPair issuerKeyPair = subjectKeyPair;
            ISignatureFactory signatureFactory = new Asn1SignatureFactory(signatureAlgorithm, issuerKeyPair.Private, random);

            var certificateGenerator = new X509V3CertificateGenerator();
            certificateGenerator.SetNotBefore(notBefore);
            certificateGenerator.SetNotAfter(notAfter);

            var nameOids = new List<DerObjectIdentifier>
            {
                X509Name.CN
            };

            var nameValues = new Dictionary<DerObjectIdentifier, string>()
            {
                { X509Name.CN, name }
            };

            var subjectDN = new X509Name(nameOids, nameValues);
            var issuerDN = subjectDN;

            certificateGenerator.SetIssuerDN(issuerDN);
            certificateGenerator.SetSubjectDN(subjectDN);
            certificateGenerator.SetPublicKey(issuerKeyPair.Public);

            byte[] serial = new byte[serialNumberLength];
            random.NextBytes(serial);
            serial[0] = 1;
            certificateGenerator.SetSerialNumber(new Org.BouncyCastle.Math.BigInteger(serial));

            X509Certificate x509Certificate = certificateGenerator.Generate(signatureFactory);
            AsymmetricKeyParameter privateKey = issuerKeyPair.Private;

            var crypto = new BcTlsCrypto();
            var tlsCertificate = crypto.CreateCertificate(x509Certificate.GetEncoded());
            var certificate = new Certificate(new[] { tlsCertificate });

            return (certificate, privateKey);
        }

        public static string Fingerprint(X509CertificateStructure c, string algorithm = "SHA256")
        {
            byte[] der = c.GetEncoded();
            byte[] hash = DigestUtilities.CalculateDigest(algorithm, der);
            byte[] hexBytes = Hex.Encode(hash);
            string hex = Encoding.ASCII.GetString(hexBytes).ToUpperInvariant();

            StringBuilder fp = new StringBuilder();
            int i = 0;
            fp.Append(hex.Substring(i, 2));
            while ((i += 2) < hex.Length)
            {
                fp.Append(':');
                fp.Append(hex.Substring(i, 2));
            }
            return fp.ToString();
        }

        public static bool IsHashSupported(string algStr)
        {
            string algName = algStr.ToUpperInvariant();
            return algName == "SHA-256" || algName == "SHA256";
        }
    }
}
