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

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.Utilities.Encoders;

namespace SIPSorcery.Net.SharpSRTP.DTLS
{
    public class DtlsClient : DefaultTlsClient, IDtlsPeer
    {
        protected DatagramTransport _clientDatagramTransport; // valid only for the current session

        private TlsSession _session;

        public bool AutogenerateCertificate { get; set; } = true;

        public int TimeoutMilliseconds { get; set; } = 20000;
        public Certificate Certificate { get; private set; }
        public AsymmetricKeyParameter CertificatePrivateKey { get; private set; }
        public short CertificateSignatureAlgorithm { get; private set; }
        public short CertificateHashAlgorithm { get; private set; }

        public bool ForceUseExtendedMasterSecret { get; set; } = true;
        public TlsServerCertificate RemoteCertificate { get; private set; }
        public Certificate PeerCertificate { get { return RemoteCertificate?.Certificate; } }

        public event EventHandler<DtlsHandshakeCompletedEventArgs> OnHandshakeCompleted;
        public event EventHandler<DtlsAlertEventArgs> OnAlert;

        public DtlsClient(
            TlsSession session = null,
            Certificate certificate = null,
            AsymmetricKeyParameter privateKey = null,
            short certificateSignatureAlgorithm = SignatureAlgorithm.ecdsa,
            short certificateHashAlgorithm = HashAlgorithm.sha256)
            : this(
                  new BcTlsCrypto(),
                  session,
                  certificate,
                  privateKey,
                  certificateSignatureAlgorithm,
                  certificateHashAlgorithm)
        { }

        public DtlsClient(
            TlsCrypto crypto,
            TlsSession session = null,
            Certificate certificate = null,
            AsymmetricKeyParameter privateKey = null,
            short certificateSignatureAlgorithm = SignatureAlgorithm.ecdsa,
            short certificateHashAlgorithm = HashAlgorithm.sha256) : base(crypto)
        {
            this._session = session;
            SetCertificate(certificate, privateKey, certificateSignatureAlgorithm, certificateHashAlgorithm);
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
            return ProtocolVersion.DTLSv12.Only(); // ProtocolVersion.IsSupportedDtlsVersionClient currently does not support DTLS 1.3
        }

        protected override int[] GetSupportedCipherSuites()
        {
            // TODO: review
            if (CertificateSignatureAlgorithm == SignatureAlgorithm.rsa)
            {
                return new int[]
                {
                    // TLS 1.3 cpihers
                    //CipherSuite.TLS_AES_256_GCM_SHA384,
                    //CipherSuite.TLS_AES_128_GCM_SHA256,
                    //CipherSuite.TLS_CHACHA20_POLY1305_SHA256,

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
                    // TLS 1.3 cpihers
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
                throw new NotSupportedException();
            }
        }

        public virtual DtlsTransport DoHandshake(out string handshakeError, DatagramTransport datagramTransport, Func<string> getRemoteEndpoint = null, Func<string, DatagramTransport> createClientDatagramTransport = null)
        {
            DtlsTransport transport = null;

            try
            {
                DtlsClientProtocol clientProtocol = new DtlsClientProtocol();

                _clientDatagramTransport = datagramTransport;
                transport = clientProtocol.Connect(this, datagramTransport);
                _clientDatagramTransport = null;
            }
            catch (Exception ex)
            {
                handshakeError = ex.Message;
                return null;
            }

            handshakeError = null;
            return transport;
        }

        public override TlsSession GetSessionToResume()
        {
            return this._session;
        }

        public override int GetHandshakeTimeoutMillis() => TimeoutMilliseconds;

        public override void NotifyAlertRaised(short alertLevel, short alertDescription, string message, Exception cause)
        {
            Log.Logger.LogDtlsClientAlertRaised(alertLevel, alertDescription, message, cause);
        }

        public override void NotifyAlertReceived(short level, short alertDescription)
        {
            Log.Logger.LogDtlsClientAlertReceived(level, alertDescription);

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

        public override void NotifyServerVersion(ProtocolVersion serverVersion)
        {
            base.NotifyServerVersion(serverVersion);

            Log.Logger.LogDtlsClientNegotiated(serverVersion);
        }

        public override TlsAuthentication GetAuthentication()
        {
            return new DTlsAuthentication(m_context, this);
        }

        public override void NotifyHandshakeComplete()
        {
            base.NotifyHandshakeComplete();

            ProtocolName protocolName = m_context.SecurityParameters.ApplicationProtocol;
            if (protocolName != null)
            {
                Log.Logger.LogDtlsClientAlpn(protocolName.GetUtf8Decoding());
            }

            TlsSession newSession = m_context.Session;
            if (newSession != null)
            {
                if (newSession.IsResumable)
                {
                    byte[] newSessionID = newSession.SessionID;
                    string hex = ToHexString(newSessionID);

                    if (_session != null && Arrays.AreEqual(_session.SessionID, newSessionID))
                    {
                        Log.Logger.LogDtlsClientSessionResumed(hex);
                    }
                    else
                    {
                        Log.Logger.LogDtlsClientSessionEstablished(hex);
                    }

                    this._session = newSession;
                }

                byte[] tlsServerEndPoint = m_context.ExportChannelBinding(ChannelBinding.tls_server_end_point);
                if (null != tlsServerEndPoint)
                {
                    Log.Logger.LogDtlsClientTlsServerEndPoint(ToHexString(tlsServerEndPoint));
                }

                byte[] tlsUnique = m_context.ExportChannelBinding(ChannelBinding.tls_unique);
                Log.Logger.LogDtlsClientTlsUnique(ToHexString(tlsUnique));
            }

            OnHandshakeCompleted?.Invoke(this, new DtlsHandshakeCompletedEventArgs(m_context.SecurityParameters));
        }

        public override IDictionary<int, byte[]> GetClientExtensions()
        {
            if (m_context.SecurityParameters.ClientRandom == null)
            {
                throw new TlsFatalAlert(AlertDescription.internal_error);
            }

            return base.GetClientExtensions();
        }

        public override void ProcessServerExtensions(IDictionary<int, byte[]> serverExtensions)
        {
            if (m_context.SecurityParameters.ServerRandom == null)
            {
                throw new TlsFatalAlert(AlertDescription.internal_error);
            }

            base.ProcessServerExtensions(serverExtensions);
        }

        protected virtual string ToHexString(byte[] data)
        {
            return data == null ? "(null)" : Hex.ToHexString(data);
        }

        internal class DTlsAuthentication : TlsAuthentication
        {
            private readonly TlsContext _context;
            private readonly DtlsClient _client;

            public DTlsAuthentication(TlsContext context, DtlsClient client)
            {
                this._client = client ?? throw new ArgumentNullException(nameof(client));
                this._context = context;
            }

            public void NotifyServerCertificate(TlsServerCertificate serverCertificate)
            {
                TlsCertificate[] chain = serverCertificate.Certificate.GetCertificateList();

                Log.Logger.LogDtlsClientServerCertificateChainReceived(chain.Length);
                for (int i = 0; i != chain.Length; i++)
                {
                    X509CertificateStructure entry = X509CertificateStructure.GetInstance(chain[i].GetEncoded());
                    Log.Logger.LogDtlsClientServerCertificateFingerprint(DtlsCertificateUtils.Fingerprint(entry), entry.Subject.ToString());
                }

                bool isEmpty = serverCertificate == null || serverCertificate.Certificate == null || serverCertificate.Certificate.IsEmpty;

                if (isEmpty)
                {
                    throw new TlsFatalAlert(AlertDescription.bad_certificate);
                }

                TlsCertificate[] certPath = chain;

                // store the certificate for further fingerprint validation
                _client.RemoteCertificate = serverCertificate;

                TlsUtilities.CheckPeerSigAlgs(_context, certPath);
            }

            public TlsCredentials GetClientCredentials(CertificateRequest certificateRequest)
            {
                short[] certificateTypes = certificateRequest.CertificateTypes;
                if (certificateTypes == null || (!Arrays.Contains(certificateTypes, ClientCertificateType.rsa_sign) && !Arrays.Contains(certificateTypes, ClientCertificateType.ecdsa_sign)))
                {
                    return null;
                }

                if (_client.Certificate == null || _client.CertificatePrivateKey == null)
                {
                    if (_client.AutogenerateCertificate)
                    {
                        bool isRsa = IsServerCertificateRsa(_client.RemoteCertificate);
                        _client.AutogenerateClientCertificate(isRsa);
                    }
                    else
                    {
                        // no client certificate
                        return null;
                    }
                }

                var clientSigAlgs = _context.SecurityParameters.ClientSigAlgs;

                SignatureAndHashAlgorithm signatureAndHashAlgorithm = null;

                foreach (SignatureAndHashAlgorithm alg in clientSigAlgs)
                {
                    if (alg.Signature == _client.CertificateSignatureAlgorithm && alg.Hash == _client.CertificateHashAlgorithm)
                    {
                        signatureAndHashAlgorithm = alg;
                        break;
                    }
                }

                if (signatureAndHashAlgorithm == null)
                {
                    throw new InvalidOperationException("DTLS Client does not support the selected certificate algorithm!");
                }

                return new BcDefaultTlsCredentialedSigner(new TlsCryptoParameters(_context), (BcTlsCrypto)_context.Crypto, _client.CertificatePrivateKey, _client.Certificate, signatureAndHashAlgorithm);
            }
        }

        public static bool IsServerCertificateRsa(TlsServerCertificate serverCertificate)
        {
            if (serverCertificate == null || serverCertificate.Certificate == null || serverCertificate.Certificate.IsEmpty)
            {
                throw new ArgumentNullException(nameof(serverCertificate));
            }

            var certList = serverCertificate.Certificate.GetCertificateList();
            if (certList == null || certList.Length == 0)
            {
                throw new ArgumentException("Server certificate chain is empty.", nameof(serverCertificate));
            }

            var firstCertificate = X509CertificateStructure.GetInstance(certList[0].GetEncoded());
            var algOid = firstCertificate.SubjectPublicKeyInfo.Algorithm.Algorithm;

            if (algOid.Equals(Org.BouncyCastle.Asn1.Pkcs.PkcsObjectIdentifiers.RsaEncryption))
            {
                return true;
            }

            // Fallback: decode the public key and check its runtime type
            var pubKey = Org.BouncyCastle.Security.PublicKeyFactory.CreateKey(firstCertificate.SubjectPublicKeyInfo);
            if (pubKey is Org.BouncyCastle.Crypto.Parameters.RsaKeyParameters)
            {
                return true;
            }

            return false;
        }
    }
}
