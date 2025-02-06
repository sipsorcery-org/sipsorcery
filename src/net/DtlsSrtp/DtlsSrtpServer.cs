//-----------------------------------------------------------------------------
// Filename: DtlsSrtpServer.cs
//
// Description: This class represents the DTLS SRTP server connection handler.
//
// Derived From:
// https://github.com/RestComm/media-core/blob/master/rtp/src/main/java/org/restcomm/media/core/rtp/crypto/DtlsSrtpServer.java
//
// Notes:
// I was unable to find good info on how the DTLS handshake works with regards the server
// and client using different certificate type, e.g. RSA and ECDSA. Eventually I determined
// that the crucial properties are:
// - The type of the DTLS server's certificate. This certificate dictates which cipher suite
//   can be used. The digital signature algorithm in the server's certificate and in the cipher
//   suite MUST match.
// - The client's certificate is NOT used to determine the cipher suite. The client's certificate
//   is only used for authentication. It can be either RSA or ECDSA providing the server is
//   capable of verifying it (at this stage all WebRTC implementations should implement both
//   RSA and ECDSA).
//
// Based on this understanding the main failure condition is if the client only supports cipher
// suites for ONE of RSA or ECDSA. If the server's certificate is for the other type then the handshake
// cannot proceed.
//
// Aaron Clauson 25 Oct 2024.
//
// Author(s):
// Rafael Soares (raf.csoares@kyubinteractive.com)
//
// History:
// 01 Jul 2020	Rafael Soares   Created.
// 21 Oct 2024  Aaron Clauson   Improved the cipher suite selection logic.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// Original Source: AGPL-3.0 License
//-----------------------------------------------------------------------------

using System;
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
    public enum AlertLevelsEnum : byte
    {
        Warning = 1,
        Fatal = 2
    }

    public enum AlertTypesEnum : byte
    {
        close_notify = 0,
        unexpected_message = 10,
        bad_record_mac = 20,
        decryption_failed = 21,
        record_overflow = 22,
        decompression_failure = 30,
        handshake_failure = 40,
        no_certificate = 41,
        bad_certificate = 42,
        unsupported_certificate = 43,
        certificate_revoked = 44,
        certificate_expired = 45,
        certificate_unknown = 46,
        illegal_parameter = 47,
        unknown_ca = 48,
        access_denied = 49,
        decode_error = 50,
        decrypt_error = 51,
        export_restriction = 60,
        protocol_version = 70,
        insufficient_security = 71,
        internal_error = 80,
        inappropriate_fallback = 86,
        user_canceled = 90,
        no_renegotiation = 100,
        unsupported_extension = 110,
        certificate_unobtainable = 111,
        unrecognized_name = 112,
        bad_certificate_status_response = 113,
        bad_certificate_hash_value = 114,
        unknown_psk_identity = 115,
        unknown = 255
    }

    public interface IDtlsSrtpPeer
    {
        event Action<AlertLevelsEnum, AlertTypesEnum, string> OnAlert;
        bool ForceUseExtendedMasterSecret { get; set; }
        SrtpPolicy GetSrtpPolicy();
        SrtpPolicy GetSrtcpPolicy();
        byte[] GetSrtpMasterServerKey();
        byte[] GetSrtpMasterServerSalt();
        byte[] GetSrtpMasterClientKey();
        byte[] GetSrtpMasterClientSalt();
        bool IsClient();
        Certificate GetRemoteCertificate();
    }

    public class DtlsSrtpServer : DefaultTlsServer, IDtlsSrtpPeer
    {
        private static readonly ILogger logger = Log.Logger;

        Certificate mCertificateChain = null;
        AsymmetricKeyParameter mPrivateKey = null;
        bool mIsEcdsaCertificate = false;

        private RTCDtlsFingerprint mFingerPrint;

        private string mSignatureAlgorithm;

        public bool ForceUseExtendedMasterSecret { get; set; } = true;

        public Certificate ClientCertificate { get; private set; }

        // the server response to the client handshake request
        // http://tools.ietf.org/html/rfc5764#section-4.1.1
        private UseSrtpData serverSrtpData;

        // Asymmetric shared keys derived from the DTLS handshake and used for the SRTP encryption/
        private byte[] srtpMasterClientKey;
        private byte[] srtpMasterServerKey;
        private byte[] srtpMasterClientSalt;
        private byte[] srtpMasterServerSalt;
        byte[] masterSecret = null;

        // Policies
        private SrtpPolicy srtpPolicy;
        private SrtpPolicy srtcpPolicy;

        private int[] cipherSuites;

        /// <summary>
        /// Parameters:
        ///  - alert level,
        ///  - alert type,
        ///  - alert description.
        /// </summary>
        public event Action<AlertLevelsEnum, AlertTypesEnum, string> OnAlert;

        public DtlsSrtpServer() : this((Certificate)null, null)
        {
        }

        public DtlsSrtpServer(System.Security.Cryptography.X509Certificates.X509Certificate2 certificate) : this(DtlsUtils.LoadCertificateChain(certificate), DtlsUtils.LoadPrivateKeyResource(certificate))
        {
        }

        public DtlsSrtpServer(string certificatePath, string keyPath) : this(new string[] { certificatePath }, keyPath)
        {
        }

        public DtlsSrtpServer(string[] certificatesPath, string keyPath) :
            this(DtlsUtils.LoadCertificateChain(certificatesPath), DtlsUtils.LoadPrivateKeyResource(keyPath))
        {
        }

        public DtlsSrtpServer(Certificate certificateChain, AsymmetricKeyParameter privateKey)
        {
            if (certificateChain == null && privateKey == null)
            {
                (certificateChain, privateKey) = DtlsUtils.CreateSelfSignedTlsCert();
            }

            // Check if the certificate is ECDSA or RSA
            var certificate = certificateChain.GetCertificateAt(0);
            var signatureAlgorithmOid = certificate.SignatureAlgorithm.Algorithm.Id;

            // Check if the certificate is ECDSA or RSA based on the OID
            mIsEcdsaCertificate = signatureAlgorithmOid.StartsWith("1.2.840.10045.4.3"); // OID prefix for ECDSA

            int[] newCipherSuites;

            if (mIsEcdsaCertificate)
            {
                // Set only ECDSA-based cipher suites
                newCipherSuites = new int[]
                {
                    CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,            // 0xC02B
                    CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA,               // 0xC009
                    CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_CBC_SHA,                // 0xC00A
                    CipherSuite.DRAFT_TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256, // 0xCCA9
                };
            }
            else
            {
                // Set only RSA-based cipher suites
                newCipherSuites = new int[]
                {
                    CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,              // 0xC02F
                    CipherSuite.DRAFT_TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256,  // 0xCCA8
                    CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA,                 // 0xC013
                    CipherSuite.TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA                  // 0xC014
                };
            }

            // Update the cipher suites
            this.cipherSuites = newCipherSuites;

            // Set the private key and certificate chain
            mPrivateKey = privateKey;
            mCertificateChain = certificateChain;

            // Generate fingerprint
            this.mFingerPrint = certificate != null ? DtlsUtils.Fingerprint(certificate) : null;

            mSignatureAlgorithm = certificate != null ? DtlsUtils.GetSignatureAlgorithm(certificate) : string.Empty;
        }

        public RTCDtlsFingerprint Fingerprint
        {
            get
            {
                return mFingerPrint;
            }
        }

        public AsymmetricKeyParameter PrivateKey
        {
            get
            {
                return mPrivateKey;
            }
        }

        public Certificate CertificateChain
        {
            get
            {
                return mCertificateChain;
            }
        }

        protected override ProtocolVersion MaximumVersion
        {
            get
            {
                return ProtocolVersion.DTLSv12;
            }
        }

        protected override ProtocolVersion MinimumVersion
        {
            get
            {
                return ProtocolVersion.DTLSv10;
            }
        }

        public override int GetSelectedCipherSuite()
        {
            /*
             * RFC 4429 5.1. A server that receives a ClientHello containing one or both of these extensions MUST use the client's
             * enumerated capabilities to guide its selection of an appropriate cipher suite. One of the proposed ECC cipher suites
             * must be negotiated only if the server can successfully complete the handshake while using the curves and point
             * formats supported by the client [...].
             */
            bool eccCipherSuitesEnabled = SupportsClientEccCapabilities(this.mNamedCurves, this.mClientECPointFormats);

            // Get available cipher suites
            int[] cipherSuites = GetCipherSuites();

            // Convert server cipher suites to human-readable names
            var serverCipherSuiteNames = cipherSuites
                .Select(cs => DtlsUtils.CipherSuiteNames.ContainsKey(cs) ? DtlsUtils.CipherSuiteNames[cs] : cs.ToString())
                .ToArray();

            // Convert client-offered cipher suites to human-readable names
            var clientCipherSuiteNames = this.mOfferedCipherSuites
                .Select(cs => DtlsUtils.CipherSuiteNames.ContainsKey(cs) ? DtlsUtils.CipherSuiteNames[cs] : cs.ToString())
                .ToArray();

            // Log the offered cipher suites by both server and client
            logger.LogTrace("Server offered cipher suites:\n {ServerCipherSuites}", string.Join("\n ", serverCipherSuiteNames));
            logger.LogTrace("Client offered cipher suites:\n {ClientCipherSuites}", string.Join("\n ", clientCipherSuiteNames));
            // Get available cipher suites
            for (int i = 0; i < cipherSuites.Length; ++i)
            {
                int cipherSuite = cipherSuites[i];

                if (Arrays.Contains(this.mOfferedCipherSuites, cipherSuite)
                        && (eccCipherSuitesEnabled || !TlsEccUtilities.IsEccCipherSuite(cipherSuite))
                        && TlsUtilities.IsValidCipherSuiteForVersion(cipherSuite, mServerVersion))
                {
                    // Cipher suite selected
                    this.mSelectedCipherSuite = cipherSuite;

                    if (mCertificateChain == null)
                    {
                        logger.LogWarning("No certificate was set for " + nameof(DtlsSrtpServer) + ".");

                        throw new TlsFatalAlert(AlertDescription.certificate_unobtainable);
                    }

                    // Log the selected cipher suite and certificate type
                    string cipherSuiteName = DtlsUtils.CipherSuiteNames.ContainsKey(cipherSuite) ? DtlsUtils.CipherSuiteNames[cipherSuite] : cipherSuite.ToString();

                    logger.LogDebug("Selected cipher suite: {CipherSuiteName}. Using {SignatureAlgorithm} certificate with fingerprint {FingerPrint}.", cipherSuiteName, mSignatureAlgorithm, this.mFingerPrint);

                    return this.mSelectedCipherSuite;
                }
            }

            // If no matching cipher suite is found, throw a fatal alert
            logger.LogWarning("DTLS server no matching cipher suite. Most likely issue is the client not supporting the server certificate's digital signature algorithm of {SignatureAlgorithm}.", mSignatureAlgorithm);

            throw new TlsFatalAlert(AlertDescription.handshake_failure);
        }

        public override CertificateRequest GetCertificateRequest()
        {
            List<SignatureAndHashAlgorithm> serverSigAlgs = new List<SignatureAndHashAlgorithm>();

            if (TlsUtilities.IsSignatureAlgorithmsExtensionAllowed(mServerVersion))
            {
                byte[] hashAlgorithms = new byte[] { HashAlgorithm.sha512, HashAlgorithm.sha384, HashAlgorithm.sha256, HashAlgorithm.sha224, HashAlgorithm.sha1 };
                byte[] signatureAlgorithms = new byte[] { SignatureAlgorithm.rsa, SignatureAlgorithm.ecdsa };

                serverSigAlgs = new List<SignatureAndHashAlgorithm>();
                for (int i = 0; i < hashAlgorithms.Length; ++i)
                {
                    for (int j = 0; j < signatureAlgorithms.Length; ++j)
                    {
                        serverSigAlgs.Add(new SignatureAndHashAlgorithm(hashAlgorithms[i], signatureAlgorithms[j]));
                    }
                }
            }
            return new CertificateRequest(new byte[] { ClientCertificateType.rsa_sign, ClientCertificateType.ecdsa_sign }, serverSigAlgs, null);
        }

        public override void NotifyClientCertificate(Certificate clientCertificate)
        {
            ClientCertificate = clientCertificate;
        }

        public override IDictionary GetServerExtensions()
        {
            Hashtable serverExtensions = (Hashtable)base.GetServerExtensions();
            if (TlsSRTPUtils.GetUseSrtpExtension(serverExtensions) == null)
            {
                if (serverExtensions == null)
                {
                    serverExtensions = new Hashtable();
                }
                TlsSRTPUtils.AddUseSrtpExtension(serverExtensions, serverSrtpData);
            }
            return serverExtensions;
        }

        public override void ProcessClientExtensions(IDictionary clientExtensions)
        {
            base.ProcessClientExtensions(clientExtensions);

            // set to some reasonable default value
            int chosenProfile = SrtpProtectionProfile.SRTP_AES128_CM_HMAC_SHA1_80;
            UseSrtpData clientSrtpData = TlsSRTPUtils.GetUseSrtpExtension(clientExtensions);

            foreach (int profile in clientSrtpData.ProtectionProfiles)
            {
                switch (profile)
                {
                    case SrtpProtectionProfile.SRTP_AES128_CM_HMAC_SHA1_32:
                    case SrtpProtectionProfile.SRTP_AES128_CM_HMAC_SHA1_80:
                    case SrtpProtectionProfile.SRTP_NULL_HMAC_SHA1_32:
                    case SrtpProtectionProfile.SRTP_NULL_HMAC_SHA1_80:
                        chosenProfile = profile;
                        break;
                }
            }

            // server chooses a mutually supported SRTP protection profile
            // http://tools.ietf.org/html/draft-ietf-avt-dtls-srtp-07#section-4.1.2
            int[] protectionProfiles = { chosenProfile };

            // server agrees to use the MKI offered by the client
            serverSrtpData = new UseSrtpData(protectionProfiles, clientSrtpData.Mki);
        }

        public SrtpPolicy GetSrtpPolicy()
        {
            return srtpPolicy;
        }

        public SrtpPolicy GetSrtcpPolicy()
        {
            return srtcpPolicy;
        }

        public byte[] GetSrtpMasterServerKey()
        {
            return srtpMasterServerKey;
        }

        public byte[] GetSrtpMasterServerSalt()
        {
            return srtpMasterServerSalt;
        }

        public byte[] GetSrtpMasterClientKey()
        {
            return srtpMasterClientKey;
        }

        public byte[] GetSrtpMasterClientSalt()
        {
            return srtpMasterClientSalt;
        }

        public override void NotifyHandshakeComplete()
        {
            //Copy master Secret (will be inaccessible after this call)
            masterSecret = new byte[mContext.SecurityParameters.MasterSecret != null ? mContext.SecurityParameters.MasterSecret.Length : 0];
            Buffer.BlockCopy(mContext.SecurityParameters.MasterSecret, 0, masterSecret, 0, masterSecret.Length);

            //Prepare Srtp Keys (we must to it here because master key will be cleared after that)
            PrepareSrtpSharedSecret();
        }

        public bool IsClient()
        {
            return false;
        }

        protected override TlsSignerCredentials GetECDsaSignerCredentials()
        {
            return DtlsUtils.LoadSignerCredentials(mContext, mCertificateChain, mPrivateKey, new SignatureAndHashAlgorithm(HashAlgorithm.sha256, SignatureAlgorithm.ecdsa));
        }

        protected override TlsEncryptionCredentials GetRsaEncryptionCredentials()
        {
            return DtlsUtils.LoadEncryptionCredentials(mContext, mCertificateChain, mPrivateKey);
        }

        protected override TlsSignerCredentials GetRsaSignerCredentials()
        {
            /*
             * TODO Note that this code fails to provide default value for the client supported
             * algorithms if it wasn't sent.
             */
            SignatureAndHashAlgorithm signatureAndHashAlgorithm = null;
            IList sigAlgs = mSupportedSignatureAlgorithms;
            if (sigAlgs != null)
            {
                foreach (var sigAlgUncasted in sigAlgs)
                {
                    SignatureAndHashAlgorithm sigAlg = sigAlgUncasted as SignatureAndHashAlgorithm;
                    if (sigAlg != null && sigAlg.Signature == SignatureAlgorithm.rsa)
                    {
                        signatureAndHashAlgorithm = sigAlg;
                        break;
                    }
                }

                if (signatureAndHashAlgorithm == null)
                {
                    return null;
                }
            }
            return DtlsUtils.LoadSignerCredentials(mContext, mCertificateChain, mPrivateKey, signatureAndHashAlgorithm);
        }

        protected virtual void PrepareSrtpSharedSecret()
        {
            //Set master secret back to security parameters (only works in old bouncy castle versions)
            //mContext.SecurityParameters.masterSecret = masterSecret;

            SrtpParameters srtpParams = SrtpParameters.GetSrtpParametersForProfile(serverSrtpData.ProtectionProfiles[0]);
            int keyLen = srtpParams.GetCipherKeyLength();
            int saltLen = srtpParams.GetCipherSaltLength();

            srtpPolicy = srtpParams.GetSrtpPolicy();
            srtcpPolicy = srtpParams.GetSrtcpPolicy();

            srtpMasterClientKey = new byte[keyLen];
            srtpMasterServerKey = new byte[keyLen];
            srtpMasterClientSalt = new byte[saltLen];
            srtpMasterServerSalt = new byte[saltLen];

            // 2* (key + salt length) / 8. From http://tools.ietf.org/html/rfc5764#section-4-2
            // No need to divide by 8 here since lengths are already in bits
            byte[] sharedSecret = GetKeyingMaterial(2 * (keyLen + saltLen));

            /*
             * 
             * See: http://tools.ietf.org/html/rfc5764#section-4.2
             * 
             * sharedSecret is an equivalent of :
             * 
             * struct {
             *     client_write_SRTP_master_key[SRTPSecurityParams.master_key_len];
             *     server_write_SRTP_master_key[SRTPSecurityParams.master_key_len];
             *     client_write_SRTP_master_salt[SRTPSecurityParams.master_salt_len];
             *     server_write_SRTP_master_salt[SRTPSecurityParams.master_salt_len];
             *  } ;
             *
             * Here, client = local configuration, server = remote.
             * NOTE [ivelin]: 'local' makes sense if this code is used from a DTLS SRTP client. 
             *                Here we run as a server, so 'local' referring to the client is actually confusing. 
             * 
             * l(k) = KEY length
             * s(k) = salt length
             * 
             * So we have the following repartition :
             *                           l(k)                                 2*l(k)+s(k)   
             *                                                   2*l(k)                       2*(l(k)+s(k))
             * +------------------------+------------------------+---------------+-------------------+
             * + local key           |    remote key    | local salt   | remote salt   |
             * +------------------------+------------------------+---------------+-------------------+
             */
            Buffer.BlockCopy(sharedSecret, 0, srtpMasterClientKey, 0, keyLen);
            Buffer.BlockCopy(sharedSecret, keyLen, srtpMasterServerKey, 0, keyLen);
            Buffer.BlockCopy(sharedSecret, 2 * keyLen, srtpMasterClientSalt, 0, saltLen);
            Buffer.BlockCopy(sharedSecret, (2 * keyLen + saltLen), srtpMasterServerSalt, 0, saltLen);
        }

        protected byte[] GetKeyingMaterial(int length)
        {
            return GetKeyingMaterial(ExporterLabel.dtls_srtp, null, length);
        }

        protected virtual byte[] GetKeyingMaterial(string asciiLabel, byte[] context_value, int length)
        {
            if (context_value != null && !TlsUtilities.IsValidUint16(context_value.Length))
            {
                throw new ArgumentException("must have length less than 2^16 (or be null)", "context_value");
            }

            SecurityParameters sp = mContext.SecurityParameters;
            if (!sp.IsExtendedMasterSecret && RequiresExtendedMasterSecret())
            {
                /*
                 * RFC 7627 5.4. If a client or server chooses to continue with a full handshake without
                 * the extended master secret extension, [..] the client or server MUST NOT export any
                 * key material based on the new master secret for any subsequent application-level
                 * authentication. In particular, it MUST disable [RFC5705] [..].
                 */
                throw new InvalidOperationException("cannot export keying material without extended_master_secret");
            }

            byte[] cr = sp.ClientRandom, sr = sp.ServerRandom;

            int seedLength = cr.Length + sr.Length;
            if (context_value != null)
            {
                seedLength += (2 + context_value.Length);
            }

            byte[] seed = new byte[seedLength];
            int seedPos = 0;

            Array.Copy(cr, 0, seed, seedPos, cr.Length);
            seedPos += cr.Length;
            Array.Copy(sr, 0, seed, seedPos, sr.Length);
            seedPos += sr.Length;
            if (context_value != null)
            {
                TlsUtilities.WriteUint16(context_value.Length, seed, seedPos);
                seedPos += 2;
                Array.Copy(context_value, 0, seed, seedPos, context_value.Length);
                seedPos += context_value.Length;
            }

            if (seedPos != seedLength)
            {
                throw new InvalidOperationException("error in calculation of seed for export");
            }

            return TlsUtilities.PRF(mContext, sp.MasterSecret, asciiLabel, seed, length);
        }

        public override bool RequiresExtendedMasterSecret()
        {
            return ForceUseExtendedMasterSecret;
        }

        protected override int[] GetCipherSuites()
        {
            int[] cipherSuites = new int[this.cipherSuites.Length];
            for (int i = 0; i < this.cipherSuites.Length; i++)
            {
                cipherSuites[i] = this.cipherSuites[i];
            }

            return cipherSuites;
        }

        public Certificate GetRemoteCertificate()
        {
            return ClientCertificate;
        }

        public override void NotifyAlertRaised(byte alertLevel, byte alertDescription, string message, Exception cause)
        {
            string description = null;
            if (message != null)
            {
                description += message;
            }
            if (cause != null)
            {
                description += cause;
            }

            if (alertDescription == AlertTypesEnum.close_notify.GetHashCode())
            {
                logger.LogDebug("DTLS server raised close notify: {AlertMsg}", $"{AlertLevel.GetText(alertLevel)}, {AlertDescription.GetText(alertDescription)}{((!string.IsNullOrEmpty(description)) ? $", {description}." : ".")}");
            }
            else
            {
                logger.LogWarning("DTLS server raised unexpected alert: {AlertMsg}", $"{AlertLevel.GetText(alertLevel)}, {AlertDescription.GetText(alertDescription)}{((!string.IsNullOrEmpty(description)) ? $", {description}." : ".")}");
            }
        }

        public override void NotifyAlertReceived(byte alertLevel, byte alertDescription)
        {
            string description = AlertDescription.GetText(alertDescription);

            AlertLevelsEnum level = AlertLevelsEnum.Warning;
            AlertTypesEnum alertType = AlertTypesEnum.unknown;

            if (Enum.IsDefined(typeof(AlertLevelsEnum), alertLevel))
            {
                level = (AlertLevelsEnum)alertLevel;
            }

            if (Enum.IsDefined(typeof(AlertTypesEnum), alertDescription))
            {
                alertType = (AlertTypesEnum)alertDescription;
            }

            if (alertType == AlertTypesEnum.close_notify)
            {
                logger.LogDebug("DTLS server received close notification: {AlertMsg}", $"{AlertLevel.GetText(alertLevel)}{((!string.IsNullOrEmpty(description)) ? $", {description}." : ".")}");
            }
            else
            {
                logger.LogWarning("DTLS server received unexpected alert: {AlertMsg}", $"{AlertLevel.GetText(alertLevel)}{((!string.IsNullOrEmpty(description)) ? $", {description}." : ".")}");
            }

            OnAlert?.Invoke(level, alertType, description);
        }

        /// <summary>
        /// This override prevents a TLS fault from being generated if a "Client Hello" is received that
        /// does not support TLS renegotiation (https://tools.ietf.org/html/rfc5746).
        /// This override is required to be able to complete a DTLS handshake with the Pion WebRTC library,
        /// see https://github.com/pion/dtls/issues/274.
        /// </summary>
        public override void NotifySecureRenegotiation(bool secureRenegotiation)
        {
            if (!secureRenegotiation)
            {
                logger.LogWarning("DTLS server received a client handshake without renegotiation support.");
            }
        }
    }
}
