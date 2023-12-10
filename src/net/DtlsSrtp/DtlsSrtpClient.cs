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
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using SIPSorcery.Sys;
using System.Collections.Generic;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Tls.Crypto;

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

        public virtual void NotifyServerCertificate(TlsServerCertificate serverCertificate)
        {
            //Console.WriteLine("DTLS client received server certificate chain of length " + chain.Length);
            mClient.ServerCertificate = serverCertificate;
        }

        public virtual TlsCredentials GetClientCredentials(CertificateRequest certificateRequest)
        {
            short[] certificateTypes = certificateRequest.CertificateTypes;
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
    };

    public class DtlsSrtpClient : DefaultTlsClient, IDtlsSrtpPeer
    {
        private static readonly ILogger logger = Log.Logger;

        internal Certificate mCertificateChain = null;
        internal AsymmetricKeyParameter mPrivateKey = null;

        internal TlsClientContext TlsContext
        {
            get { return m_context; }
        }

        protected internal TlsSession mSession;

        public bool ForceUseExtendedMasterSecret { get; set; } = true;

        //Received from server
        public TlsServerCertificate ServerCertificate { get; internal set; }

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

        public DtlsSrtpClient(TlsCrypto crypto) :
            this(crypto, null, null, null)
        {
        }

        public DtlsSrtpClient(TlsCrypto crypto, System.Security.Cryptography.X509Certificates.X509Certificate2 certificate) :
            this(crypto, DtlsUtils.LoadCertificateChain(crypto, certificate), DtlsUtils.LoadPrivateKeyResource(certificate))
        {
        }

        public DtlsSrtpClient(TlsCrypto crypto, string certificatePath, string keyPath) :
            this(crypto, new string[] { certificatePath }, keyPath)
        {
        }

        public DtlsSrtpClient(TlsCrypto crypto, string[] certificatesPath, string keyPath) :
            this(crypto, DtlsUtils.LoadCertificateChain(crypto, certificatesPath), DtlsUtils.LoadPrivateKeyResource(keyPath))
        {
        }

        public DtlsSrtpClient(TlsCrypto crypto, Certificate certificateChain, Org.BouncyCastle.Crypto.AsymmetricKeyParameter privateKey) :
            this(crypto, certificateChain, privateKey, null)
        {
        }

        public DtlsSrtpClient(TlsCrypto crypto, Certificate certificateChain, Org.BouncyCastle.Crypto.AsymmetricKeyParameter privateKey, UseSrtpData clientSrtpData) : base(crypto)
        {

            if (certificateChain == null && privateKey == null)
            {
                (certificateChain, privateKey) = DtlsUtils.CreateSelfSignedTlsCert(crypto);
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

        public DtlsSrtpClient(TlsCrypto crypto, UseSrtpData clientSrtpData) : this(crypto, null, null, clientSrtpData)
        { }


        public override IDictionary<int, byte[]> GetClientExtensions()
        {
            var clientExtensions = base.GetClientExtensions();
            if (TlsSrpUtilities.GetSrpExtension(clientExtensions) == null)
            {
                if (clientExtensions == null)
                {
                    clientExtensions = new Hashtable() as IDictionary<int, byte[]>;
                }

                TlsSrtpUtilities.AddUseSrtpExtension(clientExtensions, clientSrtpData);
            }
            return clientExtensions;
        }


        public override void ProcessServerExtensions(IDictionary<int, byte[]> serverExtensions)
        {
            base.ProcessServerExtensions(serverExtensions);

            // set to some reasonable default value
            int chosenProfile = SrtpProtectionProfile.SRTP_AES128_CM_HMAC_SHA1_80;
            clientSrtpData = TlsSrtpUtilities.GetUseSrtpExtension(serverExtensions);

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

            //Prepare Srtp Keys (we must to it here because master key will be cleared after that)
            PrepareSrtpSharedSecret();

            //Copy master Secret (will be inaccessible after this call)
            masterSecret = new byte[m_context.SecurityParameters.MasterSecret != null ? m_context.SecurityParameters.MasterSecret.Length : 0];
            Buffer.BlockCopy(m_context.SecurityParameters.MasterSecret.Extract(), 0, masterSecret, 0, masterSecret.Length);
        }

        public bool IsClient()
        {
            return true;
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

            SecurityParameters sp = m_context.SecurityParameters;
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

            return TlsUtilities.Prf(sp, sp.MasterSecret, asciiLabel, seed, length).Extract();
        }

        public override bool RequiresExtendedMasterSecret()
        {
            return ForceUseExtendedMasterSecret;
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

        public override TlsSession GetSessionToResume()
        {
            return this.mSession;
        }

        public override void NotifyAlertRaised(short alertLevel, short alertDescription, string message, Exception cause)
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

            string alertMessage = $"{AlertLevel.GetText(alertLevel)}, {AlertDescription.GetText(alertDescription)}";
            alertMessage += !string.IsNullOrEmpty(description) ? $", {description}." : ".";

            if (alertDescription == (byte)AlertTypesEnum.close_notify)
            {
                logger.LogDebug($"DTLS client raised close notification: {alertMessage}");
            }
            else
            {
                logger.LogWarning($"DTLS client raised unexpected alert: {alertMessage}");
            }
        }

        public override void NotifyServerVersion(ProtocolVersion serverVersion)
        {
            base.NotifyServerVersion(serverVersion);
        }

        public Certificate GetRemoteCertificate()
        {
            return ServerCertificate.Certificate;
        }

        protected override ProtocolVersion[] GetSupportedVersions()
        {
            return new ProtocolVersion[]
            {
                ProtocolVersion.DTLSv10,
                ProtocolVersion.DTLSv12
            };
        }

        public override void NotifyAlertReceived(short alertLevel, short alertDescription)
        {
            string description = AlertDescription.GetText(alertDescription);

            AlertLevelsEnum level = AlertLevelsEnum.Warning;
            AlertTypesEnum alertType = AlertTypesEnum.unknown;

            if (Enum.IsDefined(typeof(AlertLevelsEnum), checked((byte)alertLevel)))
            {
                level = (AlertLevelsEnum)alertLevel;
            }

            if (Enum.IsDefined(typeof(AlertTypesEnum), checked((byte)alertDescription)))
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
