//-----------------------------------------------------------------------------
// Filename: DtlsSrtpClient.cs
//
// Description: This class represents the DTLS SRTP client connection handler.
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
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Tls;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    internal class DtlsSrtpTlsAuthentication
            : TlsAuthentication
    {
        private readonly DtlsSrtpClient mClient;
        private readonly TlsContext mContext;

        internal DtlsSrtpTlsAuthentication(DtlsSrtpClient client)
        {
            this.mClient = client;
            this.mContext = client.TlsContext;
        }

        public virtual void NotifyServerCertificate(Certificate serverCertificate)
        {
            //Console.WriteLine("DTLS client received server certificate chain of length " + chain.Length);
            mClient.ServerCertificate = serverCertificate;
        }

        public virtual TlsCredentials GetClientCredentials(CertificateRequest certificateRequest)
        {
            byte[] certificateTypes = certificateRequest.CertificateTypes;
            if (certificateTypes == null || !Arrays.Contains(certificateTypes, ClientCertificateType.rsa_sign))
            {
                return null;
            }

            return DtlsUtils.LoadSignerCredentials(mContext,
                certificateRequest.SupportedSignatureAlgorithms,
                SignatureAlgorithm.rsa,
                mClient.mCertificateChain,
                mClient.mPrivateKey);
        }

        public TlsCredentials GetClientCredentials(TlsContext context, CertificateRequest certificateRequest)
        {
            return GetClientCredentials(certificateRequest);
        }
    };

    public class DtlsSrtpClient : DefaultTlsClient, IDtlsSrtpPeer
    {
        private static readonly ILogger logger = Log.Logger;

        internal Certificate mCertificateChain = null;
        internal AsymmetricKeyParameter mPrivateKey = null;

        internal TlsClientContext TlsContext
        {
            get { return mContext; }
        }

        protected internal TlsSession mSession;

        //Received from server
        public Certificate ServerCertificate { get; internal set; }

        public RTCDtlsFingerprint Fingerprint { get; private set; }

        private UseSrtpData clientSrtpData;

        // Asymmetric shared keys derived from the DTLS handshake and used for the SRTP encryption/
        private byte[] srtpMasterClientKey;
        private byte[] srtpMasterServerKey;
        private byte[] srtpMasterClientSalt;
        private byte[] srtpMasterServerSalt;
        private byte[] masterSecret = null;

        // Policies
        private SrtpPolicy srtpPolicy;
        private SrtpPolicy srtcpPolicy;

        /// <summary>
        /// Parameters:
        ///  - alert level,
        ///  - alert type,
        ///  - alert description.
        /// </summary>
        public event Action<AlertLevelsEnum, AlertTypesEnum, string> OnAlert;

        public DtlsSrtpClient() :
            this(null, null, null)
        {
        }

        public DtlsSrtpClient(System.Security.Cryptography.X509Certificates.X509Certificate2 certificate) :
            this(DtlsUtils.LoadCertificateChain(certificate), DtlsUtils.LoadPrivateKeyResource(certificate))
        {
        }

        public DtlsSrtpClient(string certificatePath, string keyPath) :
            this(new string[] { certificatePath }, keyPath)
        {
        }

        public DtlsSrtpClient(string[] certificatesPath, string keyPath) :
            this(DtlsUtils.LoadCertificateChain(certificatesPath), DtlsUtils.LoadPrivateKeyResource(keyPath))
        {
        }

        public DtlsSrtpClient(Certificate certificateChain, AsymmetricKeyParameter privateKey) :
            this(certificateChain, privateKey, null)
        {
        }

        public DtlsSrtpClient(Certificate certificateChain, AsymmetricKeyParameter privateKey, UseSrtpData clientSrtpData)
        {
            if (certificateChain == null && privateKey == null)
            {
                (certificateChain, privateKey) = DtlsUtils.CreateSelfSignedTlsCert();
            }

            if (clientSrtpData == null)
            {
                SecureRandom random = new SecureRandom();
                int[] protectionProfiles = { SrtpProtectionProfile.SRTP_AES128_CM_HMAC_SHA1_80 };
                byte[] mki = new byte[(SrtpParameters.SRTP_AES128_CM_HMAC_SHA1_80.GetCipherKeyLength() + SrtpParameters.SRTP_AES128_CM_HMAC_SHA1_80.GetCipherSaltLength()) / 8];
                random.NextBytes(mki); // Reusing our secure random for generating the key.
                this.clientSrtpData = new UseSrtpData(protectionProfiles, mki);
            }
            else
            {
                this.clientSrtpData = clientSrtpData;
            }

            this.mPrivateKey = privateKey;
            mCertificateChain = certificateChain;

            //Generate FingerPrint
            var certificate = mCertificateChain.GetCertificateAt(0);
            Fingerprint = certificate != null ? DtlsUtils.Fingerprint(certificate) : null;
        }

        public DtlsSrtpClient(UseSrtpData clientSrtpData) : this(null, null, clientSrtpData)
        { }

        public override IDictionary GetClientExtensions()
        {
            var clientExtensions = base.GetClientExtensions();
            if (TlsSRTPUtils.GetUseSrtpExtension(clientExtensions) == null)
            {
                if (clientExtensions == null)
                {
                    clientExtensions = new Hashtable();
                }

                TlsSRTPUtils.AddUseSrtpExtension(clientExtensions, clientSrtpData);
            }
            return clientExtensions;
        }

        public override void ProcessServerExtensions(IDictionary clientExtensions)
        {
            base.ProcessServerExtensions(clientExtensions);

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
            clientSrtpData = new UseSrtpData(protectionProfiles, clientSrtpData.Mki);
        }

        public virtual SrtpPolicy GetSrtpPolicy()
        {
            return srtpPolicy;
        }

        public virtual SrtpPolicy GetSrtcpPolicy()
        {
            return srtcpPolicy;
        }

        public virtual byte[] GetSrtpMasterServerKey()
        {
            return srtpMasterServerKey;
        }

        public virtual byte[] GetSrtpMasterServerSalt()
        {
            return srtpMasterServerSalt;
        }

        public virtual byte[] GetSrtpMasterClientKey()
        {
            return srtpMasterClientKey;
        }

        public virtual byte[] GetSrtpMasterClientSalt()
        {
            return srtpMasterClientSalt;
        }

        public override TlsAuthentication GetAuthentication()
        {
            return new DtlsSrtpTlsAuthentication(this);
        }

        public override void NotifyHandshakeComplete()
        {
            base.NotifyHandshakeComplete();

            //Copy master Secret (will be inaccessible after this call)
            masterSecret = new byte[mContext.SecurityParameters.MasterSecret != null ? mContext.SecurityParameters.MasterSecret.Length : 0];
            Buffer.BlockCopy(mContext.SecurityParameters.MasterSecret, 0, masterSecret, 0, masterSecret.Length);

            //Prepare Srtp Keys (we must to it here because master key will be cleared after that)
            PrepareSrtpSharedSecret();
        }

        public bool IsClient()
        {
            return true;
        }

        protected byte[] GetKeyingMaterial(int length)
        {
            return mContext.ExportKeyingMaterial(ExporterLabel.dtls_srtp, null, length);
        }

        protected virtual void PrepareSrtpSharedSecret()
        {
            //Set master secret back to security parameters (only works in old bouncy castle versions)
            //mContext.SecurityParameters.MasterSecret = masterSecret;

            SrtpParameters srtpParams = SrtpParameters.GetSrtpParametersForProfile(clientSrtpData.ProtectionProfiles[0]);
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

        public override ProtocolVersion ClientVersion
        {
            get { return ProtocolVersion.DTLSv12; }
        }

        public override ProtocolVersion MinimumVersion
        {
            get { return ProtocolVersion.DTLSv10; }
        }

        public override TlsSession GetSessionToResume()
        {
            return this.mSession;
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
                logger.LogDebug($"DTLS client raised close notify: {AlertLevel.GetText(alertLevel)}, {AlertDescription.GetText(alertDescription)}, {description}.");
            }
            else
            {
                logger.LogWarning($"DTLS client raised unexpected alert: {AlertLevel.GetText(alertLevel)}, {AlertDescription.GetText(alertDescription)}, {description}.");
            }
        }

        public override void NotifyServerVersion(ProtocolVersion serverVersion)
        {
            base.NotifyServerVersion(serverVersion);
        }

        public Certificate GetRemoteCertificate()
        {
            return ServerCertificate;
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
                logger.LogDebug($"DTLS client received close notification: {AlertLevel.GetText(alertLevel)}, {description}.");
            }
            else
            {
                logger.LogWarning($"DTLS client received unexpected alert: {AlertLevel.GetText(alertLevel)}, {description}.");
            }

            OnAlert?.Invoke(level, alertType, description);
        }
    }
}
