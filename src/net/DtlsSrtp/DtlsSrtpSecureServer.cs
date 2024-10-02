//-----------------------------------------------------------------------------
// Filename: DtlsSrtpSecureServer.cs
//-----------------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Tls;
using Org.BouncyCastle.Utilities;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    // The DtlsSrtpSecureServer class handles the DTLS-SRTP negotiation process, extending DefaultTlsServer
    // and implementing the IDtlsSrtpPeer interface to provide SRTP encryption keys and parameters.
    public class DtlsSrtpSecureServer : DefaultTlsServer, IDtlsSrtpPeer
    {
        private readonly ILogger _logger;
        private readonly Certificate _certificateChain;
        private readonly AsymmetricKeyParameter _privateKey;
        private readonly RTCDtlsFingerprint _fingerPrint;
        private UseSrtpData _serverSrtpData;
        private byte[] _srtpMasterClientKey;
        private byte[] _srtpMasterClientSalt;
        private byte[] _srtpMasterServerKey;
        private byte[] _srtpMasterServerSalt;
        private byte[] _masterSecret;
        private SrtpPolicy _srtpPolicy;
        private SrtpPolicy _srtcpPolicy;
        private readonly int[] _cipherSuites;
        private readonly object _lock = new object();

        // Property to force the use of Extended Master Secret
        public bool ForceUseExtendedMasterSecret { get; set; } = true;
        // Property to store the client's certificate
        public Certificate ClientCertificate { get; private set; }
        // Event to handle alerts
        public event Action<AlertLevels, AlertTypes, string> OnAlert;

        // Property to get the fingerprint of the server's certificate
        public RTCDtlsFingerprint Fingerprint => _fingerPrint;
        // Property to get the server's private key
        public AsymmetricKeyParameter PrivateKey => _privateKey;
        // Property to get the server's certificate chain
        public Certificate CertificateChain => _certificateChain;

        public DtlsSrtpSecureServer() :
            this((Certificate)null, null, null)
        {
        }

        // Constructor to initialize the server with a certificate chain and private key
        public DtlsSrtpSecureServer(Certificate certificateChain, AsymmetricKeyParameter privateKey, ILogger logger = null)
        {
            _logger = logger ?? Log.Logger;
            (_certificateChain, _privateKey) = certificateChain != null && privateKey != null
                ? (certificateChain, privateKey)
                : DtlsUtils.CreateSelfSignedTlsCert();

            _cipherSuites = base.GetCipherSuites();
            _fingerPrint = DtlsUtils.Fingerprint(_certificateChain?.GetCertificateAt(0));
        }

        // Constructor to initialize the server with a X509Certificate2 certificate
        public DtlsSrtpSecureServer(System.Security.Cryptography.X509Certificates.X509Certificate2 certificate, ILogger logger = null)
            : this(DtlsUtils.LoadCertificateChain(certificate), DtlsUtils.LoadPrivateKeyResource(certificate), logger) { }

        // Constructor to initialize the server with certificate and key paths
        public DtlsSrtpSecureServer(string certificatePath, string keyPath, ILogger logger = null)
            : this(new[] { certificatePath }, keyPath, logger) { }

        // Constructor to initialize the server with multiple certificate paths and a key path
        public DtlsSrtpSecureServer(string[] certificatesPath, string keyPath, ILogger logger = null)
            : this(DtlsUtils.LoadCertificateChain(certificatesPath), DtlsUtils.LoadPrivateKeyResource(keyPath), logger) { }

        // Property to get the maximum supported DTLS version
        protected override ProtocolVersion MaximumVersion => ProtocolVersion.DTLSv12;
        // Property to get the minimum supported DTLS version
        protected override ProtocolVersion MinimumVersion => ProtocolVersion.DTLSv10;

        // Method to select the appropriate cipher suite based on the client's capabilities
        public override int GetSelectedCipherSuite()
        {
            var eccCipherSuitesEnabled = SupportsClientEccCapabilities(mNamedCurves, mClientECPointFormats);

            foreach (var cipherSuite in _cipherSuites)
            {
                if (Arrays.Contains(mOfferedCipherSuites, cipherSuite) &&
                    (eccCipherSuitesEnabled || !TlsEccUtilities.IsEccCipherSuite(cipherSuite)) &&
                    TlsUtilities.IsValidCipherSuiteForVersion(cipherSuite, mServerVersion))
                {
                    return mSelectedCipherSuite = cipherSuite;
                }
            }
            throw new TlsFatalAlert(AlertDescription.handshake_failure);
        }

        // Method to get the certificate request for the client
        public override CertificateRequest GetCertificateRequest()
        {
            var serverSigAlgs = GetSignatureAndHashAlgorithms();
            return new CertificateRequest(new[] { ClientCertificateType.rsa_sign, ClientCertificateType.ecdsa_sign }, serverSigAlgs, null);
        }

        // Method to get the signature and hash algorithms supported by the server
        private List<SignatureAndHashAlgorithm> GetSignatureAndHashAlgorithms()
        {
            var serverSigAlgs = new List<SignatureAndHashAlgorithm>();

            if (TlsUtilities.IsSignatureAlgorithmsExtensionAllowed(mServerVersion))
            {
                var hashAlgorithms = new[] { HashAlgorithm.sha512, HashAlgorithm.sha384, HashAlgorithm.sha256, HashAlgorithm.sha224, HashAlgorithm.sha1 };
                var signatureAlgorithms = new[] { SignatureAlgorithm.rsa, SignatureAlgorithm.ecdsa };

                foreach (var hashAlg in hashAlgorithms)
                {
                    foreach (var sigAlg in signatureAlgorithms)
                    {
                        serverSigAlgs.Add(new SignatureAndHashAlgorithm(hashAlg, sigAlg));
                    }
                }
            }

            return serverSigAlgs;
        }

        // Method to notify the server of the client's certificate
        public override void NotifyClientCertificate(Certificate clientCertificate)
        {
            lock (_lock)
            {
                ClientCertificate = clientCertificate;
            }
        }

        // Method to get the server's extensions for the handshake
        public override IDictionary GetServerExtensions()
        {
            var serverExtensions = base.GetServerExtensions() as Hashtable ?? new Hashtable();
            if (TlsSRTPUtils.GetUseSrtpExtension(serverExtensions) == null)
            {
                TlsSRTPUtils.AddUseSrtpExtension(serverExtensions, _serverSrtpData);
            }
            return serverExtensions;
        }

        // Method to process the client's extensions during the handshake
        public override void ProcessClientExtensions(IDictionary clientExtensions)
        {
            base.ProcessClientExtensions(clientExtensions);

            var clientSrtpData = TlsSRTPUtils.GetUseSrtpExtension(clientExtensions);
            var chosenProfile = ChooseSrtpProtectionProfile(clientSrtpData.ProtectionProfiles);

            _serverSrtpData = new UseSrtpData(new[] { chosenProfile }, clientSrtpData.Mki);
        }

        // Method to choose the SRTP protection profile from the client's offered profiles
        private int ChooseSrtpProtectionProfile(params int[] protectionProfiles)
        {
            foreach (var profile in protectionProfiles)
            {
                if (profile == SrtpProtectionProfile.SRTP_AES128_CM_HMAC_SHA1_32 ||
                    profile == SrtpProtectionProfile.SRTP_AES128_CM_HMAC_SHA1_80 ||
                    profile == SrtpProtectionProfile.SRTP_NULL_HMAC_SHA1_32 ||
                    profile == SrtpProtectionProfile.SRTP_NULL_HMAC_SHA1_80)
                {
                    return profile;
                }
            }

            return SrtpProtectionProfile.SRTP_AES128_CM_HMAC_SHA1_80;
        }

        // Properties to expose the SRTP and SRTCP policies and keys
        public SrtpPolicy SrtpPolicy => _srtpPolicy;
        public SrtpPolicy SrtcpPolicy => _srtcpPolicy;
        public byte[] SrtpMasterServerKey => _srtpMasterServerKey;
        public byte[] SrtpMasterServerSalt => _srtpMasterServerSalt;
        public byte[] SrtpMasterClientKey => _srtpMasterClientKey;
        public byte[] SrtpMasterClientSalt => _srtpMasterClientSalt;

        // Method to notify the server that the handshake is complete
        public override void NotifyHandshakeComplete()
        {
            lock (_lock)
            {
                _masterSecret = mContext.SecurityParameters.MasterSecret.ToArray();
                PrepareSrtpSharedSecret();
            }
        }

        // Property to indicate if the server is the client (always false)
        public bool IsClient { get; } = false;

        // Method to get ECDSA signer credentials
        protected override TlsSignerCredentials GetECDsaSignerCredentials() =>
            DtlsUtils.LoadSignerCredentials(mContext, _certificateChain, _privateKey, new SignatureAndHashAlgorithm(HashAlgorithm.sha256, SignatureAlgorithm.ecdsa));

        // Method to get RSA encryption credentials
        protected override TlsEncryptionCredentials GetRsaEncryptionCredentials() =>
            DtlsUtils.LoadEncryptionCredentials(mContext, _certificateChain, _privateKey);

        // Method to get RSA signer credentials
        protected override TlsSignerCredentials GetRsaSignerCredentials()
        {
            var signatureAndHashAlgorithm = mSupportedSignatureAlgorithms?
                .OfType<SignatureAndHashAlgorithm>()
                .FirstOrDefault(sigAlg => sigAlg.Signature == SignatureAlgorithm.rsa);

            return signatureAndHashAlgorithm == null ? null : DtlsUtils.LoadSignerCredentials(mContext, _certificateChain, _privateKey, signatureAndHashAlgorithm);
        }

        // Method to prepare the SRTP shared secret using the master secret
        protected virtual void PrepareSrtpSharedSecret()
        {
            var srtpParams = SecureRtpParameters.GetParametersForProfile(_serverSrtpData.ProtectionProfiles[0]);
            var keyLen = srtpParams.EncryptionKeyLength;
            var saltLen = srtpParams.SaltLength;

            _srtpPolicy = srtpParams.GetPolicy();
            _srtcpPolicy = srtpParams.GetRtcpPolicy();

            _srtpMasterClientKey = new byte[keyLen];
            _srtpMasterServerKey = new byte[keyLen];
            _srtpMasterClientSalt = new byte[saltLen];
            _srtpMasterServerSalt = new byte[saltLen];

            var sharedSecret = GetKeyingMaterial(2 * (keyLen + saltLen));
            Buffer.BlockCopy(sharedSecret, 0, _srtpMasterClientKey, 0, keyLen);
            Buffer.BlockCopy(sharedSecret, keyLen, _srtpMasterServerKey, 0, keyLen);
            Buffer.BlockCopy(sharedSecret, 2 * keyLen, _srtpMasterClientSalt, 0, saltLen);
            Buffer.BlockCopy(sharedSecret, 2 * keyLen + saltLen, _srtpMasterServerSalt, 0, saltLen);
        }

        // Method to get the keying material for SRTP
        protected byte[] GetKeyingMaterial(int length) => GetKeyingMaterial(ExporterLabel.dtls_srtp, null, length);

        // Method to get the keying material for SRTP with a specific label and context value
        protected virtual byte[] GetKeyingMaterial(string asciiLabel, byte[] contextValue, int length)
        {
            if (contextValue != null && !TlsUtilities.IsValidUint16(contextValue.Length))
            {
                throw new ArgumentException("must have length less than 2^16 (or be null)", nameof(contextValue));
            }

            var sp = mContext.SecurityParameters;
            if (!sp.IsExtendedMasterSecret && RequiresExtendedMasterSecret())
            {
                throw new InvalidOperationException("cannot export keying material without extended_master_secret");
            }

            using (var pooledSeed = CombineSeed(sp.ClientRandom, sp.ServerRandom, contextValue))
            {
                return TlsUtilities.PRF(mContext, sp.MasterSecret, asciiLabel, pooledSeed.Memory.Slice(0, pooledSeed.Memory.Length).ToArray(), length);
            }
        }

        // Method to combine the seed values for PRF
        private static IMemoryOwner<byte> CombineSeed(byte[] clientRandom, byte[] serverRandom, byte[] contextValue)
        {
            int contextValueLength = contextValue?.Length ?? 0;
            int seedLength = clientRandom.Length + serverRandom.Length + (contextValue != null ? contextValue.Length + 2 : 0);

            var memoryPool = MemoryPool<byte>.Shared;
            IMemoryOwner<byte> seedOwner = memoryPool.Rent(seedLength);
            Span<byte> seedSpan = seedOwner.Memory.Span.Slice(0, seedLength);
            int seedPos = 0;

            clientRandom.AsSpan().CopyTo(seedSpan.Slice(seedPos, clientRandom.Length));
            seedPos += clientRandom.Length;

            serverRandom.AsSpan().CopyTo(seedSpan.Slice(seedPos, serverRandom.Length));
            seedPos += serverRandom.Length;

            if (contextValue != null)
            {
                seedSpan[seedPos] = (byte)(contextValue.Length >> 8);
                seedSpan[seedPos + 1] = (byte)contextValue.Length;
                seedPos += 2;
                contextValue.AsSpan().CopyTo(seedSpan.Slice(seedPos, contextValue.Length));
            }

            return seedOwner;
        }

        // Method to check if extended master secret is required
        public override bool RequiresExtendedMasterSecret() => ForceUseExtendedMasterSecret;
        // Method to get the supported cipher suites
        protected override int[] GetCipherSuites() => _cipherSuites;
        // Property to get the client's certificate
        public Certificate RemoteCertificate => ClientCertificate;

        // Method to handle alerts raised by the server
        public override void NotifyAlertRaised(byte alertLevel, byte alertDescription, string message, Exception cause)
        {
            var description = $"{message}{cause}";
            var alertMsg = $"{AlertLevel.GetText(alertLevel)}, {AlertDescription.GetText(alertDescription)}{(string.IsNullOrEmpty(description) ? "." : $", {description}.")}";

            if (alertDescription == (byte)AlertTypes.CloseNotify)
            {
                _logger.LogDebug($"DTLS server raised close notify: {alertMsg}");
            }
            else
            {
                _logger.LogWarning($"DTLS server raised unexpected alert: {alertMsg}");
            }
        }

        // Method to handle alerts received by the server
        public override void NotifyAlertReceived(byte alertLevel, byte alertDescription)
        {
            var description = AlertDescription.GetText(alertDescription);
            var level = Enum.IsDefined(typeof(AlertLevels), alertLevel) ? (AlertLevels)alertLevel : AlertLevels.Warning;
            var alertType = Enum.IsDefined(typeof(AlertTypes), alertDescription) ? (AlertTypes)alertDescription : AlertTypes.Unknown;

            var alertMsg = $"{AlertLevel.GetText(alertLevel)}{(string.IsNullOrEmpty(description) ? "." : $", {description}.")}";

            if (alertType == AlertTypes.CloseNotify)
            {
                _logger.LogDebug($"DTLS server received close notification: {alertMsg}");
            }
            else
            {
                _logger.LogWarning($"DTLS server received unexpected alert: {alertMsg}");
            }

            OnAlert?.Invoke(level, alertType, description);
        }

        // Method to notify secure renegotiation
        public override void NotifySecureRenegotiation(bool secureRenegotiation)
        {
            if (!secureRenegotiation)
            {
                _logger.LogWarning("DTLS server received a client handshake without renegotiation support.");
            }
        }
    }

}
