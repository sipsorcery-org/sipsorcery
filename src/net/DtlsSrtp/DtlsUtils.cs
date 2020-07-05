//-----------------------------------------------------------------------------
// Filename: DtlsUtils.cs
//
// Description: This class provides useful functions to handle certificate in 
// DTLS-SRTP.
//
// Author(s):
// Rafael Soares (raf.csoares@kyubinteractive.com)
//
// History:
// 01 Jul 2020	Rafael Soares   Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Crypto.Tls;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.Utilities.IO.Pem;
using Org.BouncyCastle.X509;

namespace SIPSorcery.Net
{
    public class DtlsUtils
    {
        private const string SHA_1 = "sha-1";
        private const string SHA_256 = "sha-256";
        private const string SHA_512 = "sha-512";

        static readonly byte[] rsaCertData = System.Convert.FromBase64String("MIICUzCCAf2gAwIBAgIBATANBgkqhkiG9w0BAQQFADCBjzELMAkGA1UEBhMCQVUxKDAmBgNVBAoMH1RoZSBMZWdpb2"
                        + "4gb2YgdGhlIEJvdW5jeSBDYXN0bGUxEjAQBgNVBAcMCU1lbGJvdXJuZTERMA8GA1UECAwIVmljdG9yaWExLzAtBgkq"
                        + "hkiG9w0BCQEWIGZlZWRiYWNrLWNyeXB0b0Bib3VuY3ljYXN0bGUub3JnMB4XDTEzMDIyNTA2MDIwNVoXDTEzMDIyNT"
                        + "A2MDM0NVowgY8xCzAJBgNVBAYTAkFVMSgwJgYDVQQKDB9UaGUgTGVnaW9uIG9mIHRoZSBCb3VuY3kgQ2FzdGxlMRIw"
                        + "EAYDVQQHDAlNZWxib3VybmUxETAPBgNVBAgMCFZpY3RvcmlhMS8wLQYJKoZIhvcNAQkBFiBmZWVkYmFjay1jcnlwdG"
                        + "9AYm91bmN5Y2FzdGxlLm9yZzBaMA0GCSqGSIb3DQEBAQUAA0kAMEYCQQC0p+RhcFdPFqlwgrIr5YtqKmKXmEGb4Shy"
                        + "pL26Ymz66ZAPdqv7EhOdzl3lZWT6srZUMWWgQMYGiHQg4z2R7X7XAgERo0QwQjAOBgNVHQ8BAf8EBAMCBSAwEgYDVR"
                        + "0lAQH/BAgwBgYEVR0lADAcBgNVHREBAf8EEjAQgQ50ZXN0QHRlc3QudGVzdDANBgkqhkiG9w0BAQQFAANBAHU55Ncz"
                        + "eglREcTg54YLUlGWu2WOYWhit/iM1eeq8Kivro7q98eW52jTuMI3CI5ulqd0hYzshQKQaZ5GDzErMyM=");

        static readonly byte[] dudRsaCertData = System.Convert.FromBase64String("MIICUzCCAf2gAwIBAgIBATANBgkqhkiG9w0BAQQFADCBjzELMAkGA1UEBhMCQVUxKDAmBgNVBAoMH1RoZSBMZWdpb2"
                        + "4gb2YgdGhlIEJvdW5jeSBDYXN0bGUxEjAQBgNVBAcMCU1lbGJvdXJuZTERMA8GA1UECAwIVmljdG9yaWExLzAtBgkq"
                        + "hkiG9w0BCQEWIGZlZWRiYWNrLWNyeXB0b0Bib3VuY3ljYXN0bGUub3JnMB4XDTEzMDIyNTA1NDcyOFoXDTEzMDIyNT"
                        + "A1NDkwOFowgY8xCzAJBgNVBAYTAkFVMSgwJgYDVQQKDB9UaGUgTGVnaW9uIG9mIHRoZSBCb3VuY3kgQ2FzdGxlMRIw"
                        + "EAYDVQQHDAlNZWxib3VybmUxETAPBgNVBAgMCFZpY3RvcmlhMS8wLQYJKoZIhvcNAQkBFiBmZWVkYmFjay1jcnlwdG"
                        + "9AYm91bmN5Y2FzdGxlLm9yZzBaMA0GCSqGSIb3DQEBAQUAA0kAMEYCQQC0p+RhcFdPFqlwgrIr5YtqKmKXmEGb4Shy"
                        + "pL26Ymz66ZAPdqv7EhOdzl3lZWT6srZUMWWgQMYGiHQg4z2R7X7XAgERo0QwQjAOBgNVHQ8BAf8EBAMCAAEwEgYDVR"
                        + "0lAQH/BAgwBgYEVR0lADAcBgNVHREBAf8EEjAQgQ50ZXN0QHRlc3QudGVzdDANBgkqhkiG9w0BAQQFAANBAJg55PBS"
                        + "weg6obRUKF4FF6fCrWFi6oCYSQ99LWcAeupc5BofW5MstFMhCOaEucuGVqunwT5G7/DweazzCIrSzB0=");

        public static string Fingerprint(string hashFunction, Org.BouncyCastle.Asn1.X509.X509CertificateStructure c)
        {

            byte[] der = c.GetEncoded();
            byte[] sha1 = DigestOf(hashFunction, der);
            byte[] hexBytes = Org.BouncyCastle.Utilities.Encoders.Hex.Encode(sha1);
            string hex = Encoding.ASCII.GetString(hexBytes).ToUpper(CultureInfo.InvariantCulture);

            System.Text.StringBuilder fp = new System.Text.StringBuilder();
            int i = 0;
            fp.Append(hex.Substring(i, i + 2));
            while ((i += 2) < hex.Length)
            {
                fp.Append(':');
                fp.Append(hex.Substring(i, i + 2));
            }

            switch (hashFunction)
            {
                case SHA_1:
                    return SHA_1 + " " + fp.ToString();
                case SHA_512:
                    return SHA_512 + " " + fp.ToString();
                default:
                    return SHA_256 + " " + fp.ToString();
            }
        }

        public static string Fingerprint(Certificate certificateChain)
        {
            var certificate = certificateChain.GetCertificateAt(0);
            return Fingerprint(certificate);
        }

        public static string Fingerprint(X509Certificate2 certificate)
        {
            return Fingerprint(LoadCertificateResource(certificate));
        }

        public static string Fingerprint(X509CertificateStructure c)
        {
            byte[] der = c.GetEncoded();
            byte[] sha1 = DigestOf(SHA_256, der);
            byte[] hexBytes = Org.BouncyCastle.Utilities.Encoders.Hex.Encode(sha1);
            string hex = Encoding.ASCII.GetString(hexBytes).ToUpper(CultureInfo.InvariantCulture);

            StringBuilder fp = new StringBuilder();
            int i = 0;
            fp.Append(hex.Substring(i, 2));
            while ((i += 2) < hex.Length)
            {
                fp.Append(':');
                fp.Append(hex.Substring(i, 2));
            }
            return SHA_256 + " " + fp.ToString();
        }

        public static byte[] DigestOf(string hashFunction, byte[] input)
        {
            Org.BouncyCastle.Crypto.IDigest d;
            switch (hashFunction)
            {
                case SHA_1:
                    d = new Org.BouncyCastle.Crypto.Digests.Sha1Digest();
                    break;

                case SHA_512:
                    d = new Org.BouncyCastle.Crypto.Digests.Sha512Digest();
                    break;

                case SHA_256:
                default:
                    d = new Org.BouncyCastle.Crypto.Digests.Sha256Digest();
                    break;
            }

            d.BlockUpdate(input, 0, input.Length);
            byte[] result = new byte[d.GetDigestSize()];
            d.DoFinal(result, 0);
            return result;
        }

        public static TlsAgreementCredentials LoadAgreementCredentials(TlsContext context,
                Certificate certificate, AsymmetricKeyParameter privateKey)
        {
            return new DefaultTlsAgreementCredentials(certificate, privateKey);
        }

        public static TlsAgreementCredentials LoadAgreementCredentials(TlsContext context,
                string[] certResources, string keyResource)
        {
            Certificate certificate = LoadCertificateChain(certResources);
            AsymmetricKeyParameter privateKey = LoadPrivateKeyResource(keyResource);
            return LoadAgreementCredentials(context, certificate, privateKey);
        }

        public static TlsEncryptionCredentials LoadEncryptionCredentials(
                TlsContext context, Certificate certificate, AsymmetricKeyParameter privateKey)
        {
            return new DefaultTlsEncryptionCredentials(context, certificate,
                    privateKey);
        }

        public static TlsEncryptionCredentials LoadEncryptionCredentials(
                TlsContext context, string[] certResources, string keyResource)
        {
            Certificate certificate = LoadCertificateChain(certResources);
            AsymmetricKeyParameter privateKey = LoadPrivateKeyResource(keyResource);
            return LoadEncryptionCredentials(context, certificate,
                    privateKey);
        }

        public static TlsSignerCredentials LoadSignerCredentials(TlsContext context,
                Certificate certificate, AsymmetricKeyParameter privateKey)
        {
            return new DefaultTlsSignerCredentials(context, certificate, privateKey);
        }

        public static TlsSignerCredentials LoadSignerCredentials(TlsContext context,
                string[] certResources, string keyResource)
        {
            Certificate certificate = LoadCertificateChain(certResources);
            AsymmetricKeyParameter privateKey = LoadPrivateKeyResource(keyResource);
            return LoadSignerCredentials(context, certificate, privateKey);
        }

        public static TlsSignerCredentials LoadSignerCredentials(TlsContext context,
                Certificate certificate, AsymmetricKeyParameter privateKey,
                SignatureAndHashAlgorithm signatureAndHashAlgorithm)
        {
            return new DefaultTlsSignerCredentials(context, certificate,
                    privateKey, signatureAndHashAlgorithm);
        }

        public static TlsSignerCredentials LoadSignerCredentials(TlsContext context,
                string[] certResources, string keyResource,
                SignatureAndHashAlgorithm signatureAndHashAlgorithm)
        {
            Certificate certificate = LoadCertificateChain(certResources);
            Org.BouncyCastle.Crypto.AsymmetricKeyParameter privateKey = LoadPrivateKeyResource(keyResource);
            return LoadSignerCredentials(context, certificate,
                    privateKey, signatureAndHashAlgorithm);
        }

        public static TlsSignerCredentials LoadSignerCredentials(TlsContext context, IList supportedSignatureAlgorithms,
            byte signatureAlgorithm, Certificate certificate, AsymmetricKeyParameter privateKey)
        {
            /*
             * TODO Note that this code fails to provide default value for the client supported
             * algorithms if it wasn't sent.
             */

            SignatureAndHashAlgorithm signatureAndHashAlgorithm = null;
            if (supportedSignatureAlgorithms != null)
            {
                foreach (SignatureAndHashAlgorithm alg in supportedSignatureAlgorithms)
                {
                    if (alg.Signature == signatureAlgorithm)
                    {
                        signatureAndHashAlgorithm = alg;
                        break;
                    }
                }

                if (signatureAndHashAlgorithm == null)
                {
                    return null;
                }
            }

            return LoadSignerCredentials(context, certificate, privateKey, signatureAndHashAlgorithm);
        }

        public static TlsSignerCredentials LoadSignerCredentials(TlsContext context, IList supportedSignatureAlgorithms,
            byte signatureAlgorithm, string certResource, string keyResource)
        {
            Certificate certificate = LoadCertificateChain(new string[] { certResource, "x509-ca.pem" });
            AsymmetricKeyParameter privateKey = LoadPrivateKeyResource(keyResource);

            return LoadSignerCredentials(context, supportedSignatureAlgorithms, signatureAlgorithm, certificate,
                privateKey);
        }

        public static Certificate LoadCertificateChain(X509Certificate2[] certificates)
        {
            var chain = new Org.BouncyCastle.Asn1.X509.X509CertificateStructure[certificates.Length];
            for (int i = 0; i < certificates.Length; i++)
            {
                chain[i] = LoadCertificateResource(certificates[i]);
            }

            return new Certificate(chain);
        }

        public static Certificate LoadCertificateChain(X509Certificate2 certificate)
        {
            return LoadCertificateChain(new X509Certificate2[] { certificate });
        }

        public static Certificate LoadCertificateChain(string[] resources)
        {
            Org.BouncyCastle.Asn1.X509.X509CertificateStructure[]
            chain = new Org.BouncyCastle.Asn1.X509.X509CertificateStructure[resources.Length];
            for (int i = 0; i < resources.Length; ++i)
            {
                chain[i] = LoadCertificateResource(resources[i]);
            }
            return new Certificate(chain);
        }

        public static X509CertificateStructure LoadCertificateResource(X509Certificate2 certificate)
        {
            if (certificate != null)
            {
                var bouncyCertificate = DotNetUtilities.FromX509Certificate(certificate);
                return X509CertificateStructure.GetInstance(bouncyCertificate.GetEncoded());
            }
            throw new Exception("'resource' doesn't specify a valid certificate");
        }

        public static X509CertificateStructure LoadCertificateResource(string resource)
        {
            PemObject pem = LoadPemResource(resource);
            if (pem.Type.EndsWith("CERTIFICATE"))
            {
                return X509CertificateStructure.GetInstance(pem.Content);
            }
            throw new Exception("'resource' doesn't specify a valid certificate");
        }

        public static AsymmetricKeyParameter LoadPrivateKeyResource(X509Certificate2 certificate)
        {
            return DotNetUtilities.GetKeyPair(certificate.PrivateKey).Private;
        }

        public static AsymmetricKeyParameter LoadPrivateKeyResource(string resource)
        {
            PemObject pem = LoadPemResource(resource);
            if (pem.Type.EndsWith("RSA PRIVATE KEY"))
            {
                RsaPrivateKeyStructure rsa = RsaPrivateKeyStructure.GetInstance(pem.Content);
                return new RsaPrivateCrtKeyParameters(rsa.Modulus,
                        rsa.PublicExponent, rsa.PrivateExponent,
                        rsa.Prime1, rsa.Prime2, rsa.Exponent1,
                        rsa.Exponent2, rsa.Coefficient);
            }
            if (pem.Type.EndsWith("PRIVATE KEY"))
            {
                return PrivateKeyFactory.CreateKey(pem.Content);
            }
            throw new Exception(
                    "'resource' doesn't specify a valid private key");
        }

        public static PemObject LoadPemResource(string path)
        {
            using (var s = new System.IO.StreamReader(path))
            {
                PemReader p = new PemReader(s);
                PemObject o = p.ReadPemObject();
                return o;
            }
            throw new Exception(
                    "'resource' doesn't specify a valid private key");
        }

        #region Self Signed Utils

        public static X509Certificate2 CreateSelfSignedCert(AsymmetricKeyParameter privateKey = null)
        {
            return CreateSelfSignedCert("CN=localhost", "CN=root", privateKey);
        }

        public static X509Certificate2 CreateSelfSignedCert(string subjectName, string issuerName, AsymmetricKeyParameter privateKey)
        {
            const int keyStrength = 2048;
            if (privateKey == null)
            {
                privateKey = CreatePrivateKeyResource(issuerName);
            }
            var issuerPrivKey = privateKey;

            // Generating Random Numbers
            var randomGenerator = new CryptoApiRandomGenerator();
            var random = new SecureRandom(randomGenerator);
            ISignatureFactory signatureFactory = new Asn1SignatureFactory("SHA256WITHRSA", issuerPrivKey, random);
            // The Certificate Generator
            var certificateGenerator = new X509V3CertificateGenerator();
            certificateGenerator.AddExtension(X509Extensions.SubjectAlternativeName, false, new GeneralNames(new GeneralName[] { new GeneralName(GeneralName.DnsName, "localhost"), new GeneralName(GeneralName.DnsName, "127.0.0.1") }));
            certificateGenerator.AddExtension(X509Extensions.ExtendedKeyUsage, true, new ExtendedKeyUsage(new List<DerObjectIdentifier>() { new DerObjectIdentifier("1.3.6.1.5.5.7.3.1") }));

            // Serial Number
            var serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(Int64.MaxValue), random);
            certificateGenerator.SetSerialNumber(serialNumber);

            // Issuer and Subject Name
            var subjectDn = new X509Name(subjectName);
            var issuerDn = new X509Name(issuerName);
            certificateGenerator.SetIssuerDN(issuerDn);
            certificateGenerator.SetSubjectDN(subjectDn);

            // Valid For
            var notBefore = DateTime.UtcNow.Date;
            var notAfter = notBefore.AddYears(70);

            certificateGenerator.SetNotBefore(notBefore);
            certificateGenerator.SetNotAfter(notAfter);

            // Subject Public Key
            var keyGenerationParameters = new KeyGenerationParameters(random, keyStrength);
            var keyPairGenerator = new RsaKeyPairGenerator();
            keyPairGenerator.Init(keyGenerationParameters);
            var subjectKeyPair = keyPairGenerator.GenerateKeyPair();

            certificateGenerator.SetPublicKey(subjectKeyPair.Public);

            // self sign certificate
            var certificate = certificateGenerator.Generate(signatureFactory);

            // corresponding private key
            var info = Org.BouncyCastle.Pkcs.PrivateKeyInfoFactory.CreatePrivateKeyInfo(subjectKeyPair.Private);

            // merge into X509Certificate2
            var x509 = new X509Certificate2(certificate.GetEncoded());

            var seq = (Asn1Sequence)Asn1Object.FromByteArray(info.ParsePrivateKey().GetDerEncoded());
            if (seq.Count != 9)
            {
                throw new Org.BouncyCastle.OpenSsl.PemException("malformed sequence in RSA private key");
            }

            var rsa = RsaPrivateKeyStructure.GetInstance(seq); //new RsaPrivateKeyStructure(seq);
            var rsaparams = new RsaPrivateCrtKeyParameters(
                rsa.Modulus, rsa.PublicExponent, rsa.PrivateExponent, rsa.Prime1, rsa.Prime2, rsa.Exponent1, rsa.Exponent2, rsa.Coefficient);

#if NETCOREAPP
            x509 = x509.CopyWithPrivateKey(DotNetUtilities.ToRSA(rsaparams));
#else
            x509.PrivateKey = DotNetUtilities.ToRSA(rsaparams);
#endif

            return x509;
        }

        public static AsymmetricKeyParameter CreatePrivateKeyResource(string subjectName = "CN=root")
        {
            const int keyStrength = 2048;

            // Generating Random Numbers
            var randomGenerator = new CryptoApiRandomGenerator();
            var random = new SecureRandom(randomGenerator);

            // The Certificate Generator
            var certificateGenerator = new X509V3CertificateGenerator();

            // Serial Number
            var serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), random);
            certificateGenerator.SetSerialNumber(serialNumber);

            // Issuer and Subject Name
            var subjectDn = new X509Name(subjectName);
            var issuerDn = subjectDn;
            certificateGenerator.SetIssuerDN(issuerDn);
            certificateGenerator.SetSubjectDN(subjectDn);

            // Valid For
            var notBefore = DateTime.UtcNow.Date;
            var notAfter = notBefore.AddYears(70);

            certificateGenerator.SetNotBefore(notBefore);
            certificateGenerator.SetNotAfter(notAfter);

            // Subject Public Key
            var keyGenerationParameters = new KeyGenerationParameters(random, keyStrength);
            var keyPairGenerator = new RsaKeyPairGenerator();
            keyPairGenerator.Init(keyGenerationParameters);
            var subjectKeyPair = keyPairGenerator.GenerateKeyPair();

            return subjectKeyPair.Private;
        }

#endregion
    }
}
