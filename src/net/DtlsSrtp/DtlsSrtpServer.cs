/**
 * 
 * This class represents the DTLS SRTP server connection handler.
 * 
 * The implementation follows the advise from Pierrick Grasland and Tim Panton on this forum thread:
 * http://bouncy-castle.1462172.n4.nabble.com/DTLS-SRTP-with-bouncycastle-1-49-td4656286.html
 * 
 * @author Rafael Soares (raf.csoares@kyubinteractive.com)
 * 
 *
 */

using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto.Tls;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.Utilities.IO.Pem;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace Org.BouncyCastle.Crypto.DtlsSrtp
{
    public interface IDtlsSrtpPeer
    {
        SrtpPolicy GetSrtpPolicy();
        SrtpPolicy GetSrtcpPolicy();
        byte[] GetSrtpMasterServerKey();
        byte[] GetSrtpMasterServerSalt();
        byte[] GetSrtpMasterClientKey();
        byte[] GetSrtpMasterClientSalt();
        bool IsClient();
    }

    public class DtlsSrtpServer : DefaultTlsServer, IDtlsSrtpPeer
    {
        #region Private Variables

        Certificate mCertificateChain = null;
        AsymmetricKeyParameter mPrivateKey = null;

        private string mFingerPrint = "";

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

        #endregion

        #region Constructors

        public DtlsSrtpServer() : this(DtlsUtils.CreateSelfSignedCert())
        {
        }

        public DtlsSrtpServer(X509Certificate2 certificate) : this(DtlsUtils.LoadCertificateChain(certificate), DtlsUtils.LoadPrivateKeyResource(certificate))
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
            this.cipherSuites = base.GetCipherSuites();

            this.mPrivateKey = privateKey;
            mCertificateChain = certificateChain;

            //Generate FingerPrint
            var certificate = mCertificateChain.GetCertificateAt(0);
            this.mFingerPrint = certificate != null ? DtlsUtils.Fingerprint(certificate) : string.Empty;
        }

        #endregion

        #region Properties

        public string Fingerprint
        {
            get
            {
                if (mFingerPrint == null)
                    mFingerPrint = string.Empty;
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

        #endregion

        #region Public Functions

        public override int GetSelectedCipherSuite()
        {
            /*
             * TODO RFC 5246 7.4.3. In order to negotiate correctly, the server MUST check any candidate cipher suites against the
             * "signature_algorithms" extension before selecting them. This is somewhat inelegant but is a compromise designed to
             * minimize changes to the original cipher suite design.
             */

            /*
             * RFC 4429 5.1. A server that receives a ClientHello containing one or both of these extensions MUST use the client's
             * enumerated capabilities to guide its selection of an appropriate cipher suite. One of the proposed ECC cipher suites
             * must be negotiated only if the server can successfully complete the handshake while using the curves and point
             * formats supported by the client [...].
             */
            bool eccCipherSuitesEnabled = SupportsClientEccCapabilities(this.mNamedCurves, this.mClientECPointFormats);

            int[] cipherSuites = GetCipherSuites();
            for (int i = 0; i < cipherSuites.Length; ++i)
            {
                int cipherSuite = cipherSuites[i];

                if (Arrays.Contains(this.mOfferedCipherSuites, cipherSuite)
                        && (eccCipherSuitesEnabled || !TlsEccUtilities.IsEccCipherSuite(cipherSuite))
                        && TlsUtilities.IsValidCipherSuiteForVersion(cipherSuite, mServerVersion))
                {
                    return this.mSelectedCipherSuite = cipherSuite;
                }
            }
            throw new TlsFatalAlert(AlertDescription.handshake_failure);
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
            //Copy master Secret (will be inacessible after this call)
            masterSecret = new byte[mContext.SecurityParameters.MasterSecret != null ? mContext.SecurityParameters.MasterSecret.Length : 0];
            Buffer.BlockCopy(mContext.SecurityParameters.MasterSecret, 0, masterSecret, 0, masterSecret.Length);

            //Prepare Srtp Keys (we must to it here because master key will be cleared after that)
            PrepareSrtpSharedSecret();
        }

        public bool IsClient()
        {
            return false;
        }

        #endregion

        #region Internal Helper Functions

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

            // 2* (key + salt lenght) / 8. From http://tools.ietf.org/html/rfc5764#section-4-2
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
             * s(k) = salt lenght
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
            return mContext.ExportKeyingMaterial(ExporterLabel.dtls_srtp, null, length);
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

        #endregion
    }
}