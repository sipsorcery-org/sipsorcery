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
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using Org.BouncyCastle.Utilities.Encoders;
using System;
using System.Collections.Generic;
using System.Text;

namespace SIPSorcery.Net.SRTP.DTLS
{
    public class DtlsServer : DefaultTlsServer, IDtlsPeer
    {
        public int TimeoutMilliseconds { get; set; } = 20000;

        public Certificate Certificate { get; private set; }
        public AsymmetricKeyParameter CertificatePrivateKey { get; private set; }
        public short CertificateSignatureAlgorithm { get; private set; }
        public short CertificateHashAlgorithm { get; private set; }

        public bool ForceUseExtendedMasterSecret { get; set; } = true;
        public event EventHandler<DtlsHandshakeCompletedEventArgs> OnHandshakeCompleted;
        public event EventHandler<DtlsAlertEventArgs> OnAlert;

        public DtlsServer(Certificate certificate = null, AsymmetricKeyParameter privateKey = null, short certificateSignatureAlgorithm = SignatureAlgorithm.ecdsa, short certificateHashAlgorithm = HashAlgorithm.sha256) : 
            this(new BcTlsCrypto(), certificate, privateKey, certificateSignatureAlgorithm, certificateHashAlgorithm)
        {  }

        public DtlsServer(TlsCrypto crypto, Certificate certificate = null, AsymmetricKeyParameter privateKey = null, short certificateSignatureAlgorithm = SignatureAlgorithm.ecdsa, short certificateHashAlgorithm = HashAlgorithm.sha256) : base(crypto)
        {
            if (certificate == null || privateKey == null)
            {
                // generate default self-signed certificate - SRTP_AEAD_AES_256_GCM requires ECDsa
                AutogenerateClientCertificate(false);
            }
            else
            {
                SetCertificate(certificate, privateKey, certificateSignatureAlgorithm, certificateHashAlgorithm);
            }
        }

        public virtual void SetCertificate(Certificate certificate, AsymmetricKeyParameter privateKey, short signatureAlgorithm, short hashAlgorithm)
        {
            Certificate = certificate;
            CertificatePrivateKey = privateKey;
            CertificateSignatureAlgorithm = signatureAlgorithm;
            CertificateHashAlgorithm = hashAlgorithm;
        }

        public virtual void AutogenerateClientCertificate(bool isRsa)
        {
            var cert = DtlsCertificateUtils.GenerateCertificate(GetCertificateCommonName(), DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30), isRsa);
            SetCertificate(cert.Certificate, cert.PrivateKey, isRsa ? SignatureAlgorithm.rsa : SignatureAlgorithm.ecdsa, HashAlgorithm.sha256);
        }

        protected virtual string GetCertificateCommonName()
        {
            return "DTLS";
        }

        public override bool RequiresExtendedMasterSecret()
        {
            return ForceUseExtendedMasterSecret;
        }

        protected override ProtocolVersion[] GetSupportedVersions()
        {
            //return ProtocolVersion.DTLSv13.DownTo(ProtocolVersion.DTLSv12);
            return ProtocolVersion.DTLSv12.Only(); // ProtocolVersion.IsSupportedDtlsVersionServer currently does not support DTLS 1.3
        }

        protected override int[] GetSupportedCipherSuites()
        {
            if (CertificateSignatureAlgorithm == SignatureAlgorithm.rsa)
            {
                return new int[]
                {
                    // TLS 1.2 ciphers:
                    CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
                    CipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
                    CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA256,
                    CipherSuite.TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA384,
                    CipherSuite.TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256,
                };
            }
            else if (CertificateSignatureAlgorithm == SignatureAlgorithm.ecdsa)
            {
                // ECDSA certificates require matching cipher suites
                return new int[]
                {
                    // TLS 1.3 ciphers:
                    //CipherSuite.TLS_AES_256_GCM_SHA384,
                    //CipherSuite.TLS_AES_128_GCM_SHA256,
                    //CipherSuite.TLS_CHACHA20_POLY1305_SHA256,

                    // TLS 1.2 ciphers:
                    CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
                    CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
                    CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256,
                    CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_CBC_SHA384,
                    CipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256,
                };
            }
            else
            {
                throw new InvalidOperationException($"DTLS server certificate algorithm {CertificateSignatureAlgorithm} not supported!");
            }
        }

        public virtual DtlsTransport DoHandshake(out string handshakeError, DatagramTransport datagramTransport, Func<string> getRemoteEndpoint, Func<string, DatagramTransport> createClientDatagramTransport)
        {
            if (datagramTransport == null)
            {
                throw new ArgumentNullException(nameof(datagramTransport));
            }

            if (createClientDatagramTransport == null)
            {
                throw new ArgumentNullException(nameof(createClientDatagramTransport));
            }

            DtlsTransport transport = null;

            try
            {
                DtlsServerProtocol serverProtocol = new DtlsServerProtocol();
                DtlsRequest request = null;
                string remoteEndpoint = null;

                if (getRemoteEndpoint != null)
                {
                    // Use DtlsVerifier to require a HelloVerifyRequest cookie exchange before accepting
                    DtlsVerifier verifier = new DtlsVerifier(Crypto);
                    int receiveLimit = datagramTransport.GetReceiveLimit();
                    byte[] buf = new byte[receiveLimit];
                    int receiveAttemptCounter = 0;

                    do
                    {
                        const int RECEIVE_TIMEOUT = 100;
                        int length = datagramTransport.Receive(buf, 0, receiveLimit, RECEIVE_TIMEOUT);
                        if (length > 0)
                        {
                            remoteEndpoint = getRemoteEndpoint();
                            if (string.IsNullOrEmpty(remoteEndpoint))
                            {
                                throw new InvalidOperationException();
                            }

                            byte[] clientID = Encoding.UTF8.GetBytes(remoteEndpoint);
                            request = verifier.VerifyRequest(clientID, buf, 0, length, datagramTransport);
                        }
                        else
                        {
                            receiveAttemptCounter++;

                            if (receiveAttemptCounter * RECEIVE_TIMEOUT >= TimeoutMilliseconds) // 20 seconds so that we don't wait forever
                            {
                                handshakeError = "HelloVerifyRequest cookie exchange could not be verified due to a timeout";
                                return null;
                            }
                        }
                    }
                    while (request == null);
                }

                var clientDatagramTransport = createClientDatagramTransport(remoteEndpoint);
                transport = serverProtocol.Accept(this, clientDatagramTransport, request);
            }
            catch (Exception ex)
            {
                handshakeError = ex.Message;
                return null;
            }
            
            handshakeError = null;
            return transport;
        }

        public override void NotifyAlertRaised(short alertLevel, short alertDescription, string message, Exception cause)
        {
            if (Log.DebugEnabled)
            {
                Log.Debug("DTLS server raised alert: " + AlertLevel.GetText(alertLevel) + ", " + AlertDescription.GetText(alertDescription));
            }

            if (message != null)
            {
                if (Log.DebugEnabled)
                {
                    Log.Debug("> " + message);
                }
            }
            if (cause != null)
            {
                if (Log.DebugEnabled)
                {
                    Log.Debug("", cause);
                }
            }
        }

        public override void NotifyAlertReceived(short level, short alertDescription)
        {
            if (Log.DebugEnabled)
            {
                Log.Debug("DTLS server received alert: " + AlertLevel.GetText(level) + ", " + AlertDescription.GetText(alertDescription));
            }

            TlsAlertTypesEnum alertType = TlsAlertTypesEnum.Unassigned;
            if (Enum.IsDefined(typeof(TlsAlertTypesEnum), (int)alertDescription))
            {
                alertType = (TlsAlertTypesEnum)alertDescription;
            }

            TlsAlertLevelsEnum alertLevel = TlsAlertLevelsEnum.Warn;
            if (Enum.IsDefined(typeof(TlsAlertLevelsEnum), (int)alertLevel))
            {
                alertLevel = (TlsAlertLevelsEnum)level;
            }

            OnAlert?.Invoke(this, new DtlsAlertEventArgs(alertLevel, alertType, AlertDescription.GetText(alertDescription)));
        }

        public override ProtocolVersion GetServerVersion()
        {
            ProtocolVersion serverVersion = base.GetServerVersion();
            if (Log.DebugEnabled)
            {
                Log.Debug("DTLS server negotiated " + serverVersion);
            }
            return serverVersion;
        }

        public override CertificateRequest GetCertificateRequest()
        {
            short[] certificateTypes = new short[]{ ClientCertificateType.ecdsa_sign, ClientCertificateType.rsa_sign };

            IList<SignatureAndHashAlgorithm> serverSigAlgs = null;
            if (TlsUtilities.IsSignatureAlgorithmsExtensionAllowed(m_context.ServerVersion))
            {
                serverSigAlgs = TlsUtilities.GetDefaultSupportedSignatureAlgorithms(m_context);
            }

            return new CertificateRequest(certificateTypes, serverSigAlgs, null);
        }

        public override void NotifyClientCertificate(Certificate clientCertificate)
        {
            TlsCertificate[] chain = clientCertificate.GetCertificateList();

            if (Log.DebugEnabled)
            {
                Log.Debug("DTLS server received client certificate chain of length " + chain.Length);
            }

            for (int i = 0; i != chain.Length; i++)
            {
                X509CertificateStructure entry = X509CertificateStructure.GetInstance(chain[i].GetEncoded());
                if (Log.DebugEnabled)
                {
                    Log.Debug("    fingerprint:SHA-256 " + DtlsCertificateUtils.Fingerprint(entry) + " (" + entry.Subject + ")");
                }
            }
        }

        public override void NotifyHandshakeComplete()
        {
            base.NotifyHandshakeComplete();

            ProtocolName protocolName = m_context.SecurityParameters.ApplicationProtocol;
            if (protocolName != null)
            {
                if (Log.DebugEnabled)
                {
                    Log.Debug("Server ALPN: " + protocolName.GetUtf8Decoding());
                }
            }

            byte[] tlsServerEndPoint = m_context.ExportChannelBinding(ChannelBinding.tls_server_end_point);
            if (Log.DebugEnabled)
            {
                Log.Debug("Server 'tls-server-end-point': " + ToHexString(tlsServerEndPoint));
            }

            byte[] tlsUnique = m_context.ExportChannelBinding(ChannelBinding.tls_unique);
            if (Log.DebugEnabled)
            {
                Log.Debug("Server 'tls-unique': " + ToHexString(tlsUnique));
            }

            OnHandshakeCompleted?.Invoke(this, new DtlsHandshakeCompletedEventArgs(m_context.SecurityParameters));
        }

        public override void ProcessClientExtensions(IDictionary<int, byte[]> clientExtensions)
        {
            if (m_context.SecurityParameters.ClientRandom == null)
            {
                throw new TlsFatalAlert(AlertDescription.internal_error);
            }

            base.ProcessClientExtensions(clientExtensions);
        }

        public override IDictionary<int, byte[]> GetServerExtensions()
        {
            if (m_context.SecurityParameters.ServerRandom == null)
            {
                throw new TlsFatalAlert(AlertDescription.internal_error);
            }

            return base.GetServerExtensions();
        }

        public override void GetServerExtensionsForConnection(IDictionary<int, byte[]> serverExtensions)
        {
            if (m_context.SecurityParameters.ServerRandom == null)
            {
                throw new TlsFatalAlert(AlertDescription.internal_error);
            }

            base.GetServerExtensionsForConnection(serverExtensions);
        }

        protected virtual string ToHexString(byte[] data)
        {
            return data == null ? "(null)" : Hex.ToHexString(data);
        }

        public override int GetSelectedCipherSuite()
        {
            return base.GetSelectedCipherSuite();
        }

        protected override TlsCredentialedSigner GetECDsaSignerCredentials()
        {
            IList<SignatureAndHashAlgorithm> clientSigAlgs = m_context.SecurityParameters.ClientSigAlgs;
            SignatureAndHashAlgorithm signatureAndHashAlgorithm = null;

            if (Certificate == null || CertificatePrivateKey == null)
            {
                throw new InvalidOperationException("DTLS server ECDsa certificate not set!");
            }

            foreach (SignatureAndHashAlgorithm alg in clientSigAlgs)
            {
                if (alg.Signature == CertificateSignatureAlgorithm && alg.Hash == CertificateHashAlgorithm)
                {
                    signatureAndHashAlgorithm = alg;
                    break;
                }
            }

            if (signatureAndHashAlgorithm == null)
            {
                throw new InvalidOperationException("DTLS Client does not support the selected certificate algorithm!");
            }

            return new BcDefaultTlsCredentialedSigner(new TlsCryptoParameters(m_context), (BcTlsCrypto)m_context.Crypto, CertificatePrivateKey, Certificate, signatureAndHashAlgorithm);
        }

        protected override TlsCredentialedSigner GetRsaSignerCredentials()
        {
            IList<SignatureAndHashAlgorithm> clientSigAlgs = m_context.SecurityParameters.ClientSigAlgs;
            SignatureAndHashAlgorithm signatureAndHashAlgorithm = null;

            if (Certificate == null || CertificatePrivateKey == null)
            {
                throw new InvalidOperationException("DTLS server RSA certificate not set!");
            }

            foreach (SignatureAndHashAlgorithm alg in clientSigAlgs)
            {
                if (alg.Signature == CertificateSignatureAlgorithm && alg.Hash == CertificateHashAlgorithm)
                {
                    signatureAndHashAlgorithm = alg;
                    break;
                }
            }

            if(signatureAndHashAlgorithm == null)
            {
                throw new InvalidOperationException("DTLS Client does not support the selected certificate algorithm!");
            }

            return new BcDefaultTlsCredentialedSigner(new TlsCryptoParameters(m_context), (BcTlsCrypto)m_context.Crypto, CertificatePrivateKey, Certificate, signatureAndHashAlgorithm);
        }
    }
}
